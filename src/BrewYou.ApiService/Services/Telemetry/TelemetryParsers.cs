using BrewYou.ApiService.Data.Entities;
using System.Globalization;
using System.Text.Json;

namespace BrewYou.ApiService.Services.Telemetry;

public static class TelemetryHelper
{
    public static bool TryGetPropertyInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    public static bool TryExtractDecimal(JsonElement element, out decimal value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString();
            if (!string.IsNullOrWhiteSpace(str) &&
                decimal.TryParse(str, NumberStyles.Float | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    public static decimal ConvertToCelsius(decimal value, string unit)
    {
        var normalized = unit.Trim().ToUpperInvariant();
        if (normalized.StartsWith("F") || normalized.Contains("FAHRENHEIT"))
        {
            var c = (value - 32m) * (5m / 9m);
            return Math.Round(c, 2);
        }

        return Math.Round(value, 2);
    }

    public static decimal ConvertToBar(decimal value, string unit)
    {
        var normalized = unit.Trim().ToLowerInvariant();
        if (normalized.Contains("psi"))
        {
            return Math.Round(value * 0.0689476m, 2);
        }
        if (normalized.Contains("kpa"))
        {
            return Math.Round(value * 0.01m, 2);
        }

        return Math.Round(value, 2);
    }

    public static decimal? ConvertToSpecificGravity(decimal value, string unit)
    {
        var normalized = unit.Trim().ToLowerInvariant();
        if (normalized.Contains("plato") || normalized.Contains("brix"))
        {
            // Lincoln equation: SG = 1 + (Plato / (258.6 - ((Plato / 258.2) * 227.1)))
            var plato = (double)value;
            if (plato < 0 || plato > 40) return null;
            var sg = 1.0 + (plato / (258.6 - ((plato / 258.2) * 227.1)));
            return Math.Round((decimal)sg, 4);
        }

        if (value >= 900m && value <= 1200m)
        {
            value /= 1000m;
        }

        if (value >= 0.980m && value <= 1.250m)
        {
            return Math.Round(value, 4);
        }

        return null;
    }

    public static string? ExtractRemainingJson(JsonElement root, HashSet<string> consumedKeys)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in root.EnumerateObject())
        {
            if (!consumedKeys.Contains(prop.Name))
            {
                dict[prop.Name] = ConvertJsonElement(prop.Value);
            }
        }

        return dict.Count > 0 ? JsonSerializer.Serialize(dict) : null;
    }

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetDecimal(out var d) ? d : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText()
    };
}

public class UniversalBrewYouTelemetryParser : ITelemetryParser
{
    public int Priority => 10;

    public bool CanParse(JsonElement root, Equipment equipment, string? preferredPath = null)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;

        return (TelemetryHelper.TryGetPropertyInsensitive(root, "temperature", out _) ||
                TelemetryHelper.TryGetPropertyInsensitive(root, "temp", out _)) &&
               (TelemetryHelper.TryGetPropertyInsensitive(root, "tempUnit", out _) ||
                TelemetryHelper.TryGetPropertyInsensitive(root, "gravityUnit", out _) ||
                TelemetryHelper.TryGetPropertyInsensitive(root, "pressureUnit", out _) ||
                TelemetryHelper.TryGetPropertyInsensitive(root, "batteryUnit", out _));
    }

    public NormalizedTelemetryData? Parse(JsonElement root, Equipment equipment, string? preferredPath = null)
    {
        if (!TelemetryHelper.TryGetPropertyInsensitive(root, "temperature", out var tempElem) &&
            !TelemetryHelper.TryGetPropertyInsensitive(root, "temp", out tempElem))
        {
            return null;
        }

        if (!TelemetryHelper.TryExtractDecimal(tempElem, out var tempVal)) return null;

        var tempUnit = "C";
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "tempUnit", out var tu))
        {
            tempUnit = tu.GetString() ?? "C";
        }
        var tempC = TelemetryHelper.ConvertToCelsius(tempVal, tempUnit);

        var consumedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "temperature", "temp", "tempUnit", "temp_unit", "timestamp"
        };

        decimal? sg = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "gravity", out var gElem) &&
            TelemetryHelper.TryExtractDecimal(gElem, out var gVal))
        {
            var gUnit = "SG";
            if (TelemetryHelper.TryGetPropertyInsensitive(root, "gravityUnit", out var gu))
            {
                gUnit = gu.GetString() ?? "SG";
            }
            sg = TelemetryHelper.ConvertToSpecificGravity(gVal, gUnit);
            consumedKeys.Add("gravity");
            consumedKeys.Add("gravityUnit");
        }

        decimal? pressureBar = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "pressure", out var pElem) &&
            TelemetryHelper.TryExtractDecimal(pElem, out var pVal))
        {
            var pUnit = "bar";
            if (TelemetryHelper.TryGetPropertyInsensitive(root, "pressureUnit", out var pu))
            {
                pUnit = pu.GetString() ?? "bar";
            }
            pressureBar = TelemetryHelper.ConvertToBar(pVal, pUnit);
            consumedKeys.Add("pressure");
            consumedKeys.Add("pressureUnit");
        }

        decimal? batteryPct = null;
        decimal? batteryVolt = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "battery", out var bElem) &&
            TelemetryHelper.TryExtractDecimal(bElem, out var bVal))
        {
            consumedKeys.Add("battery");
            consumedKeys.Add("batteryUnit");
            var bUnit = "";
            if (TelemetryHelper.TryGetPropertyInsensitive(root, "batteryUnit", out var bu))
            {
                bUnit = bu.GetString() ?? "";
            }

            if (bUnit.Equals("%", StringComparison.OrdinalIgnoreCase) || bVal > 5.0m)
            {
                batteryPct = Math.Clamp(bVal, 0.0m, 100.0m);
            }
            else
            {
                batteryVolt = bVal;
                batteryPct = Math.Clamp((bVal - 3.2m) / (4.2m - 3.2m) * 100m, 0.0m, 100.0m);
            }
        }

        decimal? tilt = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "tilt", out var tElem) &&
            TelemetryHelper.TryExtractDecimal(tElem, out var tVal))
        {
            tilt = Math.Round(tVal, 2);
            consumedKeys.Add("tilt");
            consumedKeys.Add("tiltUnit");
        }

        short? rssi = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "rssi", out var rElem) &&
            rElem.TryGetInt16(out var rVal))
        {
            rssi = rVal;
            consumedKeys.Add("rssi");
        }

        string? metricsJson = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "raw", out var rawElem) && rawElem.ValueKind == JsonValueKind.Object)
        {
            metricsJson = rawElem.GetRawText();
            consumedKeys.Add("raw");
        }
        else
        {
            metricsJson = TelemetryHelper.ExtractRemainingJson(root, consumedKeys);
        }

        return new NormalizedTelemetryData(
            tempC,
            sg,
            pressureBar,
            batteryPct,
            batteryVolt,
            tilt,
            rssi,
            metricsJson,
            "BrewYouUniversal"
        );
    }
}

public class ISpindelTelemetryParser : ITelemetryParser
{
    public int Priority => 20;

    public bool CanParse(JsonElement root, Equipment equipment, string? preferredPath = null)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;

        return TelemetryHelper.TryGetPropertyInsensitive(root, "temp_units", out _) &&
               (TelemetryHelper.TryGetPropertyInsensitive(root, "angle", out _) ||
                TelemetryHelper.TryGetPropertyInsensitive(root, "gravity", out _) ||
                (TelemetryHelper.TryGetPropertyInsensitive(root, "name", out var name) &&
                 name.GetString()?.StartsWith("iSpindel", StringComparison.OrdinalIgnoreCase) == true));
    }

    public NormalizedTelemetryData? Parse(JsonElement root, Equipment equipment, string? preferredPath = null)
    {
        if (!TelemetryHelper.TryGetPropertyInsensitive(root, "temperature", out var tempElem) &&
            !TelemetryHelper.TryGetPropertyInsensitive(root, "temp", out tempElem))
        {
            return null;
        }

        if (!TelemetryHelper.TryExtractDecimal(tempElem, out var tempVal)) return null;

        var tempUnit = "C";
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "temp_units", out var tu))
        {
            tempUnit = tu.GetString() ?? "C";
        }
        var tempC = TelemetryHelper.ConvertToCelsius(tempVal, tempUnit);

        var consumedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "temperature", "temp", "temp_units", "name"
        };

        decimal? sg = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "gravity", out var gElem) &&
            TelemetryHelper.TryExtractDecimal(gElem, out var gVal))
        {
            sg = TelemetryHelper.ConvertToSpecificGravity(gVal, "SG");
            consumedKeys.Add("gravity");
        }

        decimal? tilt = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "angle", out var aElem) &&
            TelemetryHelper.TryExtractDecimal(aElem, out var aVal))
        {
            tilt = Math.Round(aVal, 2);
            consumedKeys.Add("angle");
        }

        decimal? batteryVolt = null;
        decimal? batteryPct = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "battery", out var bElem) &&
            TelemetryHelper.TryExtractDecimal(bElem, out var bVal))
        {
            consumedKeys.Add("battery");
            if (bVal > 5.0m)
            {
                batteryPct = Math.Clamp(bVal, 0.0m, 100.0m);
            }
            else
            {
                batteryVolt = Math.Round(bVal, 2);
                batteryPct = Math.Round(Math.Clamp((bVal - 3.2m) / (4.2m - 3.2m) * 100m, 0.0m, 100.0m), 1);
            }
        }

        short? rssi = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "RSSI", out var rElem) &&
            rElem.TryGetInt16(out var rVal))
        {
            rssi = rVal;
            consumedKeys.Add("RSSI");
        }

        var metricsJson = TelemetryHelper.ExtractRemainingJson(root, consumedKeys);

        return new NormalizedTelemetryData(
            tempC,
            sg,
            null,
            batteryPct,
            batteryVolt,
            tilt,
            rssi,
            metricsJson,
            "iSpindel"
        );
    }
}

public class TiltTelemetryParser : ITelemetryParser
{
    public int Priority => 30;

    public bool CanParse(JsonElement root, Equipment equipment, string? preferredPath = null)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;

        return TelemetryHelper.TryGetPropertyInsensitive(root, "color", out _) &&
               (TelemetryHelper.TryGetPropertyInsensitive(root, "temp", out _) ||
                TelemetryHelper.TryGetPropertyInsensitive(root, "temperature", out _));
    }

    public NormalizedTelemetryData? Parse(JsonElement root, Equipment equipment, string? preferredPath = null)
    {
        if (!TelemetryHelper.TryGetPropertyInsensitive(root, "temp", out var tempElem) &&
            !TelemetryHelper.TryGetPropertyInsensitive(root, "temperature", out tempElem))
        {
            return null;
        }

        if (!TelemetryHelper.TryExtractDecimal(tempElem, out var tempVal)) return null;

        // Tilt firmware natively reports temperature in Fahrenheit
        var tempC = TelemetryHelper.ConvertToCelsius(tempVal, "F");

        var consumedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "temp", "temperature", "color"
        };

        decimal? sg = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "gravity", out var gElem) &&
            TelemetryHelper.TryExtractDecimal(gElem, out var gVal))
        {
            sg = TelemetryHelper.ConvertToSpecificGravity(gVal, "SG");
            consumedKeys.Add("gravity");
        }

        short? rssi = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "rssi", out var rElem) &&
            rElem.TryGetInt16(out var rVal))
        {
            rssi = rVal;
            consumedKeys.Add("rssi");
        }

        var metricsJson = TelemetryHelper.ExtractRemainingJson(root, consumedKeys);

        return new NormalizedTelemetryData(
            tempC,
            sg,
            null,
            null,
            null,
            null,
            rssi,
            metricsJson,
            "TiltHydrometer"
        );
    }
}

public class GenericJsonTelemetryParser : ITelemetryParser
{
    public int Priority => 100;

    public bool CanParse(JsonElement root, Equipment equipment, string? preferredPath = null)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;

        if (!string.IsNullOrWhiteSpace(preferredPath)) return true;

        return TelemetryHelper.TryGetPropertyInsensitive(root, "temperature", out _) ||
               TelemetryHelper.TryGetPropertyInsensitive(root, "temp", out _) ||
               TelemetryHelper.TryGetPropertyInsensitive(root, "current_temp", out _) ||
               TelemetryHelper.TryGetPropertyInsensitive(root, "currentTemperature", out _);
    }

    public NormalizedTelemetryData? Parse(JsonElement root, Equipment equipment, string? preferredPath = null)
    {
        decimal? tempVal = null;
        var unit = "C";
        var consumedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Check preferredPath if provided (e.g. "sensors.temp_1")
        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            var segments = preferredPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var current = root;
            var pathResolved = true;

            foreach (var segment in segments)
            {
                if (current.ValueKind == JsonValueKind.Object && TelemetryHelper.TryGetPropertyInsensitive(current, segment, out var next))
                {
                    current = next;
                }
                else
                {
                    pathResolved = false;
                    break;
                }
            }

            if (pathResolved && TelemetryHelper.TryExtractDecimal(current, out var explicitValue))
            {
                tempVal = explicitValue;
                if (TelemetryHelper.TryGetPropertyInsensitive(root, "unit", out var u1) ||
                    TelemetryHelper.TryGetPropertyInsensitive(root, "temp_units", out u1) ||
                    TelemetryHelper.TryGetPropertyInsensitive(root, "temp_unit", out u1))
                {
                    unit = u1.GetString() ?? "C";
                }
            }
        }

        if (!tempVal.HasValue)
        {
            if (TelemetryHelper.TryGetPropertyInsensitive(root, "temperature", out var tElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(root, "temp", out tElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(root, "current_temp", out tElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(root, "currentTemperature", out tElem))
            {
                if (TelemetryHelper.TryExtractDecimal(tElem, out var v))
                {
                    tempVal = v;
                    consumedKeys.Add("temperature");
                    consumedKeys.Add("temp");
                    consumedKeys.Add("current_temp");
                    consumedKeys.Add("currentTemperature");

                    if (TelemetryHelper.TryGetPropertyInsensitive(root, "unit", out var u) ||
                        TelemetryHelper.TryGetPropertyInsensitive(root, "temp_unit", out u) ||
                        TelemetryHelper.TryGetPropertyInsensitive(root, "temp_units", out u) ||
                        TelemetryHelper.TryGetPropertyInsensitive(root, "units", out u))
                    {
                        unit = u.GetString() ?? "C";
                        consumedKeys.Add("unit");
                        consumedKeys.Add("temp_unit");
                        consumedKeys.Add("temp_units");
                        consumedKeys.Add("units");
                    }
                }
            }
        }

        if (!tempVal.HasValue) return null;

        var tempC = TelemetryHelper.ConvertToCelsius(tempVal.Value, unit);

        // Check for pressure
        decimal? pressureBar = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "pressure", out var pElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "head_pressure", out pElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "current_pressure", out pElem))
        {
            if (TelemetryHelper.TryExtractDecimal(pElem, out var pVal))
            {
                var pUnit = "bar";
                if (TelemetryHelper.TryGetPropertyInsensitive(root, "pressure_unit", out var pu) ||
                    TelemetryHelper.TryGetPropertyInsensitive(root, "pressure_units", out pu))
                {
                    pUnit = pu.GetString() ?? "bar";
                    consumedKeys.Add("pressure_unit");
                    consumedKeys.Add("pressure_units");
                }
                pressureBar = TelemetryHelper.ConvertToBar(pVal, pUnit);
                consumedKeys.Add("pressure");
                consumedKeys.Add("head_pressure");
                consumedKeys.Add("current_pressure");
            }
        }

        // Check for gravity
        decimal? sg = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "gravity", out var gElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "sg", out gElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "specific_gravity", out gElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "specificGravity", out gElem))
        {
            if (TelemetryHelper.TryExtractDecimal(gElem, out var gVal))
            {
                var gUnit = "SG";
                if (TelemetryHelper.TryGetPropertyInsensitive(root, "gravity_unit", out var gu) ||
                    TelemetryHelper.TryGetPropertyInsensitive(root, "gravity_units", out gu))
                {
                    gUnit = gu.GetString() ?? "SG";
                    consumedKeys.Add("gravity_unit");
                    consumedKeys.Add("gravity_units");
                }
                sg = TelemetryHelper.ConvertToSpecificGravity(gVal, gUnit);
                consumedKeys.Add("gravity");
                consumedKeys.Add("sg");
                consumedKeys.Add("specific_gravity");
                consumedKeys.Add("specificGravity");
            }
        }

        // Check for battery
        decimal? batteryPct = null;
        decimal? batteryVolt = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "battery", out var bElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "battery_level", out bElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "battery_pct", out bElem))
        {
            if (TelemetryHelper.TryExtractDecimal(bElem, out var bVal))
            {
                consumedKeys.Add("battery");
                consumedKeys.Add("battery_level");
                consumedKeys.Add("battery_pct");
                if (bVal > 5.0m)
                {
                    batteryPct = Math.Clamp(bVal, 0.0m, 100.0m);
                }
                else
                {
                    batteryVolt = Math.Round(bVal, 2);
                    batteryPct = Math.Round(Math.Clamp((bVal - 3.2m) / (4.2m - 3.2m) * 100m, 0.0m, 100.0m), 1);
                }
            }
        }

        // Check for tilt
        decimal? tilt = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "tilt", out var tiltElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "angle", out tiltElem))
        {
            if (TelemetryHelper.TryExtractDecimal(tiltElem, out var tiltVal))
            {
                tilt = Math.Round(tiltVal, 2);
                consumedKeys.Add("tilt");
                consumedKeys.Add("angle");
            }
        }

        // Check for rssi
        short? rssi = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "rssi", out var rElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "wifi_rssi", out rElem))
        {
            if (rElem.TryGetInt16(out var rVal))
            {
                rssi = rVal;
                consumedKeys.Add("rssi");
                consumedKeys.Add("wifi_rssi");
            }
        }

        var metricsJson = TelemetryHelper.ExtractRemainingJson(root, consumedKeys);

        return new NormalizedTelemetryData(
            tempC,
            sg,
            pressureBar,
            batteryPct,
            batteryVolt,
            tilt,
            rssi,
            metricsJson,
            "GenericJson"
        );
    }
}