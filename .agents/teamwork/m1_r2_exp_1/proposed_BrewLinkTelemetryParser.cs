using BrewYou.ApiService.Data.Entities;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Services.Telemetry;

public sealed record BrewLinkTimerMetrics(
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("paused")] bool Paused,
    [property: JsonPropertyName("remainingSeconds")] int? RemainingSeconds,
    [property: JsonPropertyName("totalSeconds")] int? TotalSeconds
);

public sealed record BrewLinkStageMetrics(
    [property: JsonPropertyName("stepIndex")] int? StepIndex,
    [property: JsonPropertyName("stepName")] string? StepName,
    [property: JsonPropertyName("autoMode")] bool? AutoMode = null
);

public sealed record BrewLinkInteractionMetrics(
    [property: JsonPropertyName("required")] bool Required,
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string? Message
);

public sealed record BrewLinkCanonicalMetrics(
    [property: JsonPropertyName("currentTempC")] decimal? CurrentTempC,
    [property: JsonPropertyName("targetTempC")] decimal? TargetTempC,
    [property: JsonPropertyName("boilTempC")] decimal? BoilTempC,
    [property: JsonPropertyName("heatActive")] bool? HeatActive,
    [property: JsonPropertyName("heatPowerPct")] decimal? HeatPowerPct,
    [property: JsonPropertyName("rampActive")] bool? RampActive,
    [property: JsonPropertyName("pumpActive")] bool? PumpActive,
    [property: JsonPropertyName("autoMode")] bool? AutoMode,
    [property: JsonPropertyName("timer")] BrewLinkTimerMetrics? Timer,
    [property: JsonPropertyName("stage")] BrewLinkStageMetrics? Stage,
    [property: JsonPropertyName("interaction")] BrewLinkInteractionMetrics? Interaction,
    [property: JsonPropertyName("mac")] string? Mac,
    [property: JsonPropertyName("rssi")] short? Rssi,
    [property: JsonPropertyName("updatedAt")] DateTime UpdatedAt
);

public class BrewLinkTelemetryParser : ITelemetryParser
{
    public const string FormatName = "BrewLink";
    public int Priority => 5;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public bool CanParse(JsonElement root, Equipment equipment, string? preferredPath = null)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (equipment?.ConnectionType == EquipmentConnectionType.BrewLink)
        {
            return true;
        }

        if (TelemetryHelper.TryGetPropertyInsensitive(root, "event", out var eventElem) &&
            eventElem.ValueKind == JsonValueKind.String &&
            string.Equals(eventElem.GetString(), "telemetry", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (TelemetryHelper.TryGetPropertyInsensitive(root, "equipment", out var eqElem) &&
            eqElem.ValueKind == JsonValueKind.Array &&
            TelemetryHelper.TryGetPropertyInsensitive(root, "gateway_id", out _))
        {
            return true;
        }

        if (TelemetryHelper.TryGetPropertyInsensitive(root, "mac", out _) &&
            TelemetryHelper.TryGetPropertyInsensitive(root, "state", out var stateProp) &&
            stateProp.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        if (TelemetryHelper.TryGetPropertyInsensitive(root, "heat_active", out _) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "heatActive", out _) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "pump_active", out _) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "pumpActive", out _) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "boil_temp_c", out _) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "boilTempC", out _))
        {
            return true;
        }

        return false;
    }

    public NormalizedTelemetryData? Parse(JsonElement root, Equipment equipment, string? preferredPath = null)
    {
        var canonical = ParseCanonical(root, equipment);
        if (canonical == null || !canonical.CurrentTempC.HasValue)
        {
            return null;
        }

        var metricsJson = ToMetricsJson(canonical);

        return new NormalizedTelemetryData(
            TemperatureC: canonical.CurrentTempC.Value,
            SpecificGravity: null,
            PressureBar: null,
            BatteryPercent: null,
            BatteryVoltage: null,
            TiltDegrees: null,
            Rssi: canonical.Rssi,
            MetricsJson: metricsJson,
            DetectedFormat: FormatName
        );
    }

    public static string ToMetricsJson(BrewLinkCanonicalMetrics metrics) =>
        JsonSerializer.Serialize(metrics, JsonOptions);

    public static BrewLinkCanonicalMetrics? FromMetricsJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BrewLinkCanonicalMetrics>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static BrewLinkCanonicalMetrics? ParseCanonical(JsonElement root, Equipment? equipment = null)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var deviceElem = root;
        var stateElem = root;
        string? extractedMac = null;
        short? extractedRssi = null;

        // 1. Resolve device from REST gateway payload array
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "equipment", out var eqArray) &&
            eqArray.ValueKind == JsonValueKind.Array)
        {
            deviceElem = ResolveTargetDevice(eqArray, equipment);
            if (deviceElem.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
        }

        // 2. Resolve nested state object
        if (TelemetryHelper.TryGetPropertyInsensitive(deviceElem, "state", out var nestedState) &&
            nestedState.ValueKind == JsonValueKind.Object)
        {
            stateElem = nestedState;
        }

        // 3. Extract MAC address safely
        if ((TelemetryHelper.TryGetPropertyInsensitive(deviceElem, "mac", out var macElem) ||
             TelemetryHelper.TryGetPropertyInsensitive(deviceElem, "target_mac", out macElem) ||
             TelemetryHelper.TryGetPropertyInsensitive(deviceElem, "targetMac", out macElem) ||
             TelemetryHelper.TryGetPropertyInsensitive(stateElem, "mac", out macElem)) &&
            macElem.ValueKind == JsonValueKind.String)
        {
            extractedMac = FormatMac(macElem.GetString());
        }

        if (string.IsNullOrWhiteSpace(extractedMac) && equipment != null)
        {
            extractedMac = ExtractConfiguredMac(equipment);
        }

        // 4. Extract RSSI safely (number or string)
        if (TelemetryHelper.TryGetPropertyInsensitive(deviceElem, "rssi", out var rssiElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(stateElem, "rssi", out rssiElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(deviceElem, "wifi_rssi", out rssiElem))
        {
            if (TryExtractInt16(rssiElem, out var rVal))
            {
                extractedRssi = rVal;
            }
        }

        // 5. Extract Timestamp safely
        var extractedUpdatedAt = ExtractTimestamp(deviceElem, stateElem);

        // 6. Extract Temperatures
        var currentTempC = ExtractTemperature(stateElem, "current");
        var targetTempC = ExtractTemperature(stateElem, "target");
        var boilTempC = ExtractTemperature(stateElem, "boil");

        // 7. Extract Heating
        ExtractHeating(stateElem, out var heatActive, out var heatPowerPct, out var rampActive);

        // 8. Extract Pump
        var pumpActive = ExtractPump(stateElem);

        // 9. Extract Auto Mode
        var autoMode = ExtractAutoMode(deviceElem, stateElem);

        // 10. Extract Timer
        var timer = ExtractTimer(stateElem);

        // 11. Extract Stage
        var stage = ExtractStage(stateElem, autoMode);

        // 12. Extract Interaction
        var interaction = ExtractInteraction(stateElem);

        return new BrewLinkCanonicalMetrics(
            CurrentTempC: currentTempC,
            TargetTempC: targetTempC,
            BoilTempC: boilTempC,
            HeatActive: heatActive,
            HeatPowerPct: heatPowerPct,
            RampActive: rampActive,
            PumpActive: pumpActive,
            AutoMode: autoMode,
            Timer: timer,
            Stage: stage,
            Interaction: interaction,
            Mac: extractedMac,
            Rssi: extractedRssi,
            UpdatedAt: extractedUpdatedAt
        );
    }

    private static JsonElement ResolveTargetDevice(JsonElement eqArray, Equipment? equipment)
    {
        if (eqArray.GetArrayLength() == 0)
        {
            return default;
        }

        string? targetMac = null;
        int? targetSlot = null;
        string? targetName = null;

        if (equipment != null && !string.IsNullOrWhiteSpace(equipment.ConnectionConfigJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(equipment.ConnectionConfigJson);
                var root = doc.RootElement;
                if ((TelemetryHelper.TryGetPropertyInsensitive(root, "targetMac", out var tm) ||
                     TelemetryHelper.TryGetPropertyInsensitive(root, "target_mac", out tm)) &&
                    tm.ValueKind == JsonValueKind.String)
                {
                    targetMac = CleanMac(tm.GetString());
                }
                if (TelemetryHelper.TryGetPropertyInsensitive(root, "slot", out var slotElem) &&
                    TryExtractInt32(slotElem, out var s))
                {
                    targetSlot = s;
                }
                if ((TelemetryHelper.TryGetPropertyInsensitive(root, "targetName", out var tn) ||
                     TelemetryHelper.TryGetPropertyInsensitive(root, "target_name", out tn)) &&
                    tn.ValueKind == JsonValueKind.String)
                {
                    targetName = tn.GetString();
                }
            }
            catch
            {
                // Ignore parse errors on malformed connection config
            }
        }

        foreach (var item in eqArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(targetMac) &&
                TelemetryHelper.TryGetPropertyInsensitive(item, "mac", out var mElem) &&
                mElem.ValueKind == JsonValueKind.String &&
                string.Equals(CleanMac(mElem.GetString()), targetMac, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }

            if (targetSlot.HasValue &&
                TelemetryHelper.TryGetPropertyInsensitive(item, "slot", out var slElem) &&
                TryExtractInt32(slElem, out var sVal) && sVal == targetSlot.Value)
            {
                return item;
            }

            if (!string.IsNullOrEmpty(targetName) &&
                TelemetryHelper.TryGetPropertyInsensitive(item, "name", out var nElem) &&
                nElem.ValueKind == JsonValueKind.String &&
                string.Equals(nElem.GetString(), targetName, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        // Fallback: return the first valid JSON object in eqArray
        foreach (var item in eqArray.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                return item;
            }
        }

        return eqArray[0];
    }

    private static decimal? ExtractTemperature(JsonElement state, string kind)
    {
        // 1. Nested in "temperature" object
        if (TelemetryHelper.TryGetPropertyInsensitive(state, "temperature", out var tempObj) &&
            tempObj.ValueKind == JsonValueKind.Object)
        {
            var tempUnit = "C";
            if ((TelemetryHelper.TryGetPropertyInsensitive(tempObj, "unit", out var u) ||
                 TelemetryHelper.TryGetPropertyInsensitive(tempObj, "temp_unit", out u) ||
                 TelemetryHelper.TryGetPropertyInsensitive(tempObj, "temp_units", out u)) &&
                u.ValueKind == JsonValueKind.String)
            {
                tempUnit = u.GetString() ?? "C";
            }

            string[] nestedKeys = kind switch
            {
                "current" => ["current_c", "current", "temp_c", "temp", "current_temp_c", "current_temp"],
                "target" => ["target_c", "target", "target_temp_c", "target_temp", "setpoint_c", "setpoint"],
                "boil" => ["boil_c", "boil", "boil_temp_c", "boil_temp"],
                _ => []
            };

            foreach (var key in nestedKeys)
            {
                if (TelemetryHelper.TryGetPropertyInsensitive(tempObj, key, out var elem) &&
                    TelemetryHelper.TryExtractDecimal(elem, out var val))
                {
                    return TelemetryHelper.ConvertToCelsius(val, tempUnit);
                }
            }

            string[] fKeys = kind switch
            {
                "current" => ["current_f", "temp_f", "current_temp_f"],
                "target" => ["target_f", "target_temp_f"],
                "boil" => ["boil_f", "boil_temp_f"],
                _ => []
            };

            foreach (var key in fKeys)
            {
                if (TelemetryHelper.TryGetPropertyInsensitive(tempObj, key, out var elem) &&
                    TelemetryHelper.TryExtractDecimal(elem, out var val))
                {
                    return TelemetryHelper.ConvertToCelsius(val, "F");
                }
            }
        }

        // 2. Flat properties in state
        var flatUnit = "C";
        if ((TelemetryHelper.TryGetPropertyInsensitive(state, "unit", out var su) ||
             TelemetryHelper.TryGetPropertyInsensitive(state, "temp_unit", out su) ||
             TelemetryHelper.TryGetPropertyInsensitive(state, "temp_units", out su) ||
             TelemetryHelper.TryGetPropertyInsensitive(state, "temperature_unit", out su)) &&
            su.ValueKind == JsonValueKind.String)
        {
            flatUnit = su.GetString() ?? "C";
        }

        string[] flatKeys = kind switch
        {
            "current" => ["current_temp_c", "currentTempC", "current_temp", "currentTemperature", "temp_c", "temp", "temperature"],
            "target" => ["target_temp_c", "targetTempC", "target_temp", "target_c", "target", "targetTemp", "setpoint_c", "setpoint"],
            "boil" => ["boil_temp_c", "boilTempC", "boil_temp", "boil_c"],
            _ => []
        };

        foreach (var key in flatKeys)
        {
            if (TelemetryHelper.TryGetPropertyInsensitive(state, key, out var elem) &&
                TelemetryHelper.TryExtractDecimal(elem, out var val))
            {
                return TelemetryHelper.ConvertToCelsius(val, flatUnit);
            }
        }

        string[] flatFKeys = kind switch
        {
            "current" => ["current_temp_f", "currentTempF", "temp_f"],
            "target" => ["target_temp_f", "targetTempF", "target_f"],
            "boil" => ["boil_temp_f", "boilTempF", "boil_f"],
            _ => []
        };

        foreach (var key in flatFKeys)
        {
            if (TelemetryHelper.TryGetPropertyInsensitive(state, key, out var elem) &&
                TelemetryHelper.TryExtractDecimal(elem, out var val))
            {
                return TelemetryHelper.ConvertToCelsius(val, "F");
            }
        }

        return null;
    }

    private static void ExtractHeating(
        JsonElement state,
        out bool? heatActive,
        out decimal? heatPowerPct,
        out bool? rampActive)
    {
        heatActive = null;
        heatPowerPct = null;
        rampActive = null;

        // 1. Nested in "heating"
        if (TelemetryHelper.TryGetPropertyInsensitive(state, "heating", out var heatingObj) &&
            heatingObj.ValueKind == JsonValueKind.Object)
        {
            if (TelemetryHelper.TryGetPropertyInsensitive(heatingObj, "active", out var actElem) &&
                actElem.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                heatActive = actElem.GetBoolean();
            }

            if (TelemetryHelper.TryGetPropertyInsensitive(heatingObj, "power_pct", out var pElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(heatingObj, "power", out pElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(heatingObj, "pct", out pElem))
            {
                if (TelemetryHelper.TryExtractDecimal(pElem, out var pVal))
                {
                    heatPowerPct = Math.Clamp(pVal, 0m, 100m);
                }
            }

            if (TelemetryHelper.TryGetPropertyInsensitive(heatingObj, "ramping", out var rampElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(heatingObj, "ramp_active", out rampElem))
            {
                if (rampElem.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    rampActive = rampElem.GetBoolean();
                }
            }
        }

        // 2. Flat properties in state
        if (!heatActive.HasValue)
        {
            if (TelemetryHelper.TryGetPropertyInsensitive(state, "heat_active", out var haElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(state, "heatActive", out haElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(state, "is_heating", out haElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(state, "heater_on", out haElem))
            {
                if (haElem.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    heatActive = haElem.GetBoolean();
                }
            }
        }

        if (!heatPowerPct.HasValue)
        {
            if (TelemetryHelper.TryGetPropertyInsensitive(state, "heat_power_pct", out var hpElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(state, "heatPowerPct", out hpElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(state, "heat_pct", out hpElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(state, "heater_power", out hpElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(state, "heaterDutyPct", out hpElem))
            {
                if (TelemetryHelper.TryExtractDecimal(hpElem, out var hpVal))
                {
                    heatPowerPct = Math.Clamp(hpVal, 0m, 100m);
                }
            }
        }

        if (!rampActive.HasValue)
        {
            if (TelemetryHelper.TryGetPropertyInsensitive(state, "ramp_active", out var raElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(state, "rampActive", out raElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(state, "ramping", out raElem))
            {
                if (raElem.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    rampActive = raElem.GetBoolean();
                }
            }
        }

        // Logical fallback: if heatPowerPct is set, infer heatActive
        if (!heatActive.HasValue && heatPowerPct.HasValue)
        {
            heatActive = heatPowerPct.Value > 0m;
        }
    }

    private static bool? ExtractPump(JsonElement state)
    {
        // 1. Nested in "pump"
        if (TelemetryHelper.TryGetPropertyInsensitive(state, "pump", out var pumpObj))
        {
            if (pumpObj.ValueKind == JsonValueKind.Object)
            {
                if (TelemetryHelper.TryGetPropertyInsensitive(pumpObj, "active", out var actElem) &&
                    actElem.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return actElem.GetBoolean();
                }
            }
            else if (pumpObj.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return pumpObj.GetBoolean();
            }
        }

        // 2. Flat properties in state
        if (TelemetryHelper.TryGetPropertyInsensitive(state, "pump_active", out var paElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(state, "pumpActive", out paElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(state, "pump_on", out paElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(state, "is_pump_active", out paElem))
        {
            if (paElem.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return paElem.GetBoolean();
            }
        }

        return null;
    }

    private static bool? ExtractAutoMode(JsonElement root, JsonElement state)
    {
        if (TelemetryHelper.TryGetPropertyInsensitive(state, "auto_mode", out var amElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(state, "autoMode", out amElem) ||
            (TelemetryHelper.TryGetPropertyInsensitive(state, "stage", out var stg) &&
             stg.ValueKind == JsonValueKind.Object &&
             (TelemetryHelper.TryGetPropertyInsensitive(stg, "auto_mode", out amElem) ||
              TelemetryHelper.TryGetPropertyInsensitive(stg, "autoMode", out amElem))) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "auto_mode", out amElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "autoMode", out amElem))
        {
            if (amElem.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return amElem.GetBoolean();
            }
        }

        return null;
    }

    private static BrewLinkTimerMetrics? ExtractTimer(JsonElement state)
    {
        // 1. Nested in "timer"
        if (TelemetryHelper.TryGetPropertyInsensitive(state, "timer", out var timerObj) &&
            timerObj.ValueKind == JsonValueKind.Object)
        {
            var active = false;
            var paused = false;
            int? remainingSeconds = null;
            int? totalSeconds = null;

            if (TelemetryHelper.TryGetPropertyInsensitive(timerObj, "active", out var actElem) &&
                actElem.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                active = actElem.GetBoolean();
            }

            if (TelemetryHelper.TryGetPropertyInsensitive(timerObj, "paused", out var pElem) &&
                pElem.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                paused = pElem.GetBoolean();
            }

            if (TelemetryHelper.TryGetPropertyInsensitive(timerObj, "remaining_seconds", out var remElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(timerObj, "remainingSeconds", out remElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(timerObj, "remaining_sec", out remElem))
            {
                if (TryExtractInt32(remElem, out var rVal))
                {
                    remainingSeconds = rVal;
                    if (!TelemetryHelper.TryGetPropertyInsensitive(timerObj, "active", out _))
                    {
                        active = rVal > 0;
                    }
                }
            }

            if (TelemetryHelper.TryGetPropertyInsensitive(timerObj, "total_seconds", out var totElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(timerObj, "totalSeconds", out totElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(timerObj, "total_sec", out totElem))
            {
                if (TryExtractInt32(totElem, out var tVal))
                {
                    totalSeconds = tVal;
                }
            }

            return new BrewLinkTimerMetrics(active, paused, remainingSeconds, totalSeconds);
        }

        // 2. Flat properties in state
        int? flatRemaining = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(state, "timer_remaining_sec", out var frElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(state, "timer_remaining_seconds", out frElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(state, "timerRemainingSeconds", out frElem))
        {
            if (TryExtractInt32(frElem, out var frVal))
            {
                flatRemaining = frVal;
            }
        }

        int? flatTotal = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(state, "timer_total_sec", out var ftElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(state, "timer_total_seconds", out ftElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(state, "timerTotalSeconds", out ftElem))
        {
            if (TryExtractInt32(ftElem, out var ftVal))
            {
                flatTotal = ftVal;
            }
        }

        bool? flatActive = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(state, "timer_active", out var faElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(state, "timerActive", out faElem))
        {
            if (faElem.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                flatActive = faElem.GetBoolean();
            }
        }

        var flatPaused = false;
        if (TelemetryHelper.TryGetPropertyInsensitive(state, "timer_paused", out var fpElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(state, "timerPaused", out fpElem))
        {
            if (fpElem.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                flatPaused = fpElem.GetBoolean();
            }
        }

        if (flatRemaining.HasValue || flatTotal.HasValue || flatActive.HasValue)
        {
            return new BrewLinkTimerMetrics(
                Active: flatActive ?? (flatRemaining.HasValue && flatRemaining.Value > 0),
                Paused: flatPaused,
                RemainingSeconds: flatRemaining,
                TotalSeconds: flatTotal
            );
        }

        return null;
    }

    private static BrewLinkStageMetrics? ExtractStage(JsonElement state, bool? fallbackAutoMode)
    {
        // 1. Nested in "stage"
        if (TelemetryHelper.TryGetPropertyInsensitive(state, "stage", out var stageObj) &&
            stageObj.ValueKind == JsonValueKind.Object)
        {
            int? stepIndex = null;
            string? stepName = null;
            var autoMode = fallbackAutoMode;

            if ((TelemetryHelper.TryGetPropertyInsensitive(stageObj, "step_index", out var siElem) ||
                 TelemetryHelper.TryGetPropertyInsensitive(stageObj, "stepIndex", out siElem)) &&
                TryExtractInt32(siElem, out var sVal))
            {
                stepIndex = sVal;
            }

            if ((TelemetryHelper.TryGetPropertyInsensitive(stageObj, "step_name", out var snElem) ||
                 TelemetryHelper.TryGetPropertyInsensitive(stageObj, "stepName", out snElem) ||
                 TelemetryHelper.TryGetPropertyInsensitive(stageObj, "name", out snElem)) &&
                snElem.ValueKind == JsonValueKind.String)
            {
                stepName = snElem.GetString();
            }

            if (TelemetryHelper.TryGetPropertyInsensitive(stageObj, "auto_mode", out var amElem) ||
                TelemetryHelper.TryGetPropertyInsensitive(stageObj, "autoMode", out amElem))
            {
                if (amElem.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    autoMode = amElem.GetBoolean();
                }
            }

            return new BrewLinkStageMetrics(stepIndex, stepName, autoMode);
        }

        // 2. Flat properties in state
        int? flatStepIndex = null;
        if ((TelemetryHelper.TryGetPropertyInsensitive(state, "step_index", out var fsiElem) ||
             TelemetryHelper.TryGetPropertyInsensitive(state, "stepIndex", out fsiElem) ||
             TelemetryHelper.TryGetPropertyInsensitive(state, "stage_step_index", out fsiElem)) &&
            TryExtractInt32(fsiElem, out var siVal))
        {
            flatStepIndex = siVal;
        }

        string? flatStepName = null;
        if ((TelemetryHelper.TryGetPropertyInsensitive(state, "step_name", out var fsnElem) ||
             TelemetryHelper.TryGetPropertyInsensitive(state, "stepName", out fsnElem) ||
             TelemetryHelper.TryGetPropertyInsensitive(state, "stage_step_name", out fsnElem)) &&
            fsnElem.ValueKind == JsonValueKind.String)
        {
            flatStepName = fsnElem.GetString();
        }

        if (flatStepIndex.HasValue || !string.IsNullOrWhiteSpace(flatStepName))
        {
            return new BrewLinkStageMetrics(flatStepIndex, flatStepName, fallbackAutoMode);
        }

        return null;
    }

    private static BrewLinkInteractionMetrics? ExtractInteraction(JsonElement state)
    {
        // 1. Nested in "interaction"
        if (TelemetryHelper.TryGetPropertyInsensitive(state, "interaction", out var interObj) &&
            interObj.ValueKind == JsonValueKind.Object)
        {
            var required = false;
            var code = 0;
            string? message = null;

            if (TelemetryHelper.TryGetPropertyInsensitive(interObj, "required", out var reqElem) &&
                reqElem.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                required = reqElem.GetBoolean();
            }

            if (TelemetryHelper.TryGetPropertyInsensitive(interObj, "code", out var codeElem) &&
                TryExtractInt32(codeElem, out var cVal))
            {
                code = cVal;
            }

            if ((TelemetryHelper.TryGetPropertyInsensitive(interObj, "message", out var msgElem) ||
                 TelemetryHelper.TryGetPropertyInsensitive(interObj, "prompt", out msgElem) ||
                 TelemetryHelper.TryGetPropertyInsensitive(interObj, "alert", out msgElem)) &&
                msgElem.ValueKind == JsonValueKind.String)
            {
                message = msgElem.GetString();
            }

            return new BrewLinkInteractionMetrics(required, code, message);
        }

        // 2. Flat properties in state
        bool? flatRequired = null;
        if (TelemetryHelper.TryGetPropertyInsensitive(state, "interaction_required", out var firElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(state, "interactionRequired", out firElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(state, "alert_active", out firElem))
        {
            if (firElem.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                flatRequired = firElem.GetBoolean();
            }
        }

        var flatCode = 0;
        if ((TelemetryHelper.TryGetPropertyInsensitive(state, "interaction_code", out var ficElem) ||
             TelemetryHelper.TryGetPropertyInsensitive(state, "interactionCode", out ficElem) ||
             TelemetryHelper.TryGetPropertyInsensitive(state, "alert_code", out ficElem)) &&
            TryExtractInt32(ficElem, out var c))
        {
            flatCode = c;
        }

        string? flatMessage = null;
        if ((TelemetryHelper.TryGetPropertyInsensitive(state, "interaction_message", out var fimElem) ||
             TelemetryHelper.TryGetPropertyInsensitive(state, "interactionMessage", out fimElem) ||
             TelemetryHelper.TryGetPropertyInsensitive(state, "prompt", out fimElem) ||
             TelemetryHelper.TryGetPropertyInsensitive(state, "alert_message", out fimElem)) &&
            fimElem.ValueKind == JsonValueKind.String)
        {
            flatMessage = fimElem.GetString();
        }

        if (flatRequired.HasValue || !string.IsNullOrWhiteSpace(flatMessage) || flatCode != 0)
        {
            return new BrewLinkInteractionMetrics(
                Required: flatRequired ?? !string.IsNullOrWhiteSpace(flatMessage),
                Code: flatCode,
                Message: flatMessage
            );
        }

        return null;
    }

    private static DateTime ExtractTimestamp(JsonElement root, JsonElement state)
    {
        if (TelemetryHelper.TryGetPropertyInsensitive(root, "timestamp_ms", out var tsElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(state, "timestamp_ms", out tsElem))
        {
            if (TryExtractInt64(tsElem, out var tsVal))
            {
                try
                {
                    if (tsVal >= 10_000_000_000L && tsVal <= 253_402_300_799_999L)
                    {
                        return DateTimeOffset.FromUnixTimeMilliseconds(tsVal).UtcDateTime;
                    }
                    if (tsVal > 100_000_000L && tsVal <= 253_402_300_799L)
                    {
                        return DateTimeOffset.FromUnixTimeSeconds(tsVal).UtcDateTime;
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Fall back to UtcNow on range violations
                }
                return DateTime.UtcNow;
            }
        }

        if (TelemetryHelper.TryGetPropertyInsensitive(root, "timestamp", out var sElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(state, "timestamp", out sElem))
        {
            if (sElem.ValueKind == JsonValueKind.Number && sElem.TryGetInt64(out var secVal))
            {
                try
                {
                    if (secVal > 100_000_000L && secVal <= 253_402_300_799L)
                    {
                        return DateTimeOffset.FromUnixTimeSeconds(secVal).UtcDateTime;
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Fall back to UtcNow
                }
            }
            else if (sElem.ValueKind == JsonValueKind.String)
            {
                var str = sElem.GetString();
                if (!string.IsNullOrWhiteSpace(str))
                {
                    if (DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                    {
                        return dt.UtcDateTime;
                    }
                    if (long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSec))
                    {
                        try
                        {
                            if (parsedSec > 100_000_000L && parsedSec <= 253_402_300_799L)
                            {
                                return DateTimeOffset.FromUnixTimeSeconds(parsedSec).UtcDateTime;
                            }
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            // Fall back to UtcNow
                        }
                    }
                }
            }
        }

        if (TelemetryHelper.TryGetPropertyInsensitive(root, "updated_at", out var uElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(root, "updatedAt", out uElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(state, "updated_at", out uElem) ||
            TelemetryHelper.TryGetPropertyInsensitive(state, "updatedAt", out uElem))
        {
            if (uElem.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(uElem.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            {
                return dt.UtcDateTime;
            }
        }

        return DateTime.UtcNow;
    }

    public static string? FormatMac(string? rawMac)
    {
        if (string.IsNullOrWhiteSpace(rawMac))
        {
            return null;
        }

        var cleaned = CleanMac(rawMac);
        if (cleaned.Length == 12)
        {
            return string.Join(":", Enumerable.Range(0, 6).Select(i => cleaned.Substring(i * 2, 2)));
        }

        return rawMac.Trim();
    }

    public static string CleanMac(string? rawMac)
    {
        if (string.IsNullOrWhiteSpace(rawMac))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(12);
        foreach (var ch in rawMac)
        {
            if (char.IsAsciiHexDigit(ch))
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
        }
        return sb.ToString();
    }

    private static string? ExtractConfiguredMac(Equipment equipment)
    {
        if (string.IsNullOrWhiteSpace(equipment.ConnectionConfigJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(equipment.ConnectionConfigJson);
            if ((TelemetryHelper.TryGetPropertyInsensitive(doc.RootElement, "targetMac", out var tm) ||
                 TelemetryHelper.TryGetPropertyInsensitive(doc.RootElement, "target_mac", out tm)) &&
                tm.ValueKind == JsonValueKind.String)
            {
                return FormatMac(tm.GetString());
            }
        }
        catch
        {
            // Ignore parse errors on malformed config
        }

        return null;
    }

    private static bool TryExtractInt32(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString();
            if (!string.IsNullOrWhiteSpace(str) &&
                int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryExtractInt16(JsonElement element, out short value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt16(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString();
            if (!string.IsNullOrWhiteSpace(str) &&
                short.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryExtractInt64(JsonElement element, out long value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString();
            if (!string.IsNullOrWhiteSpace(str) &&
                long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }
}
