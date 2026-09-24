using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services.Telemetry;
using FluentAssertions;
using System.Text.Json;

namespace BrewYou.ApiService.Tests;

public class TelemetryParserTests
{
    private static readonly Equipment TestEquipment = new()
    {
        Id = Guid.NewGuid(),
        UserId = "test-user-id",
        Name = "Test Fermenter",
        Type = EquipmentType.Fermenter,
        Subtype = EquipmentSubtype.ConicalFermenter,
        Capacity = 60,
        BrewerySetupId = Guid.NewGuid()
    };

    [Fact]
    public void TelemetryHelper_TryGetPropertyInsensitive_FindsPropertiesIgnoringCase()
    {
        using var doc = JsonDocument.Parse("{\"Temperature\": 19.5, \"UnIT\": \"C\"}");
        var root = doc.RootElement;

        TelemetryHelper.TryGetPropertyInsensitive(root, "temperature", out var tempElem).Should().BeTrue();
        tempElem.GetDecimal().Should().Be(19.5m);

        TelemetryHelper.TryGetPropertyInsensitive(root, "unit", out var unitElem).Should().BeTrue();
        unitElem.GetString().Should().Be("C");

        TelemetryHelper.TryGetPropertyInsensitive(root, "non_existent", out _).Should().BeFalse();

        // Non-object
        var arrayElem = JsonDocument.Parse("[1, 2, 3]").RootElement;
        TelemetryHelper.TryGetPropertyInsensitive(arrayElem, "temperature", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"val\": 12.34}", 12.34, true)]
    [InlineData("{\"val\": \"56.78\"}", 56.78, true)]
    [InlineData("{\"val\": \"not_a_number\"}", 0, false)]
    [InlineData("{\"val\": \"\"}", 0, false)]
    [InlineData("{\"val\": true}", 0, false)]
    public void TelemetryHelper_TryExtractDecimal_ExtractsCorrectly(string json, double expectedValue, bool expectedSuccess)
    {
        using var doc = JsonDocument.Parse(json);
        var elem = doc.RootElement.GetProperty("val");

        var success = TelemetryHelper.TryExtractDecimal(elem, out var val);

        success.Should().Be(expectedSuccess);
        if (expectedSuccess)
        {
            val.Should().Be((decimal)expectedValue);
        }
    }

    [Theory]
    [InlineData(68.0, "F", 20.0)]
    [InlineData(68.0, "fahrenheit", 20.0)]
    [InlineData(21.5, "C", 21.5)]
    [InlineData(21.5, "celsius", 21.5)]
    public void TelemetryHelper_ConvertToCelsius_ConvertsAccurately(double input, string unit, double expected)
    {
        var result = TelemetryHelper.ConvertToCelsius((decimal)input, unit);
        result.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData(14.5038, "psi", 1.0)]
    [InlineData(100.0, "kpa", 1.0)]
    [InlineData(1.5, "bar", 1.5)]
    public void TelemetryHelper_ConvertToBar_ConvertsAccurately(double input, string unit, double expected)
    {
        var result = TelemetryHelper.ConvertToBar((decimal)input, unit);
        result.Should().Be((decimal)expected);
    }

    [Fact]
    public void TelemetryHelper_ConvertToSpecificGravity_HandlesPlatoAndBrixAndRawPoints()
    {
        // 12 Plato -> SG ~ 1.0484
        var sgPlato = TelemetryHelper.ConvertToSpecificGravity(12.0m, "plato");
        sgPlato.Should().NotBeNull();
        sgPlato!.Value.Should().BeInRange(1.048m, 1.049m);

        // Negative or over 40 plato returns null
        TelemetryHelper.ConvertToSpecificGravity(-1.0m, "plato").Should().BeNull();
        TelemetryHelper.ConvertToSpecificGravity(45.0m, "brix").Should().BeNull();

        // Raw gravity points (e.g. 1050 -> 1.050)
        var rawSg = TelemetryHelper.ConvertToSpecificGravity(1050m, "SG");
        rawSg.Should().Be(1.0500m);

        // Standard SG in valid range (0.980 to 1.250)
        var standardSg = TelemetryHelper.ConvertToSpecificGravity(1.048m, "SG");
        standardSg.Should().Be(1.0480m);

        // Out of range SG returns null
        TelemetryHelper.ConvertToSpecificGravity(0.5m, "SG").Should().BeNull();
        TelemetryHelper.ConvertToSpecificGravity(2.5m, "SG").Should().BeNull();
    }

    [Fact]
    public void TelemetryHelper_ExtractRemainingJson_FiltersConsumedKeysAndSerializesPrimitives()
    {
        using var doc = JsonDocument.Parse("""
        {
            "temperature": 20.0,
            "extra_str": "hello",
            "extra_num": 42.5,
            "extra_bool": true,
            "extra_null": null
        }
        """);

        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "temperature" };
        var remaining = TelemetryHelper.ExtractRemainingJson(doc.RootElement, consumed);

        remaining.Should().NotBeNull();
        remaining.Should().NotContain("temperature");
        remaining.Should().Contain("extra_str");
        remaining.Should().Contain("extra_num");
        remaining.Should().Contain("extra_bool");

        // Non-object returns null
        var nonObject = JsonDocument.Parse("[1, 2]").RootElement;
        TelemetryHelper.ExtractRemainingJson(nonObject, consumed).Should().BeNull();

        // Empty remaining returns null
        var allConsumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "temperature", "extra_str", "extra_num", "extra_bool", "extra_null"
        };
        TelemetryHelper.ExtractRemainingJson(doc.RootElement, allConsumed).Should().BeNull();
    }

    [Fact]
    public void GenericJsonTelemetryParser_CanParse_ValidatesExpectedProperties()
    {
        var parser = new GenericJsonTelemetryParser();
        parser.Priority.Should().Be(100);

        using var validDoc = JsonDocument.Parse("{\"temp\": 20.0}");
        parser.CanParse(validDoc.RootElement, TestEquipment).Should().BeTrue();

        using var validCurrentTempDoc = JsonDocument.Parse("{\"current_temp\": 20.0}");
        parser.CanParse(validCurrentTempDoc.RootElement, TestEquipment).Should().BeTrue();

        using var validCurrentTemperatureDoc = JsonDocument.Parse("{\"currentTemperature\": 20.0}");
        parser.CanParse(validCurrentTemperatureDoc.RootElement, TestEquipment).Should().BeTrue();

        using var emptyDoc = JsonDocument.Parse("{\"other\": 123}");
        parser.CanParse(emptyDoc.RootElement, TestEquipment).Should().BeFalse();

        // With preferredPath, returns true even if temperature isn't root property
        parser.CanParse(emptyDoc.RootElement, TestEquipment, "other").Should().BeTrue();
    }

    [Fact]
    public void GenericJsonTelemetryParser_Parse_HandlesPreferredPathNestedAndUnit()
    {
        var parser = new GenericJsonTelemetryParser();
        using var doc = JsonDocument.Parse("""
        {
            "sensors": {
                "chamber": {
                    "probe1": 68.0
                }
            },
            "temp_units": "F"
        }
        """);

        var result = parser.Parse(doc.RootElement, TestEquipment, "sensors.chamber.probe1");

        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(20.0m);
        result.DetectedFormat.Should().Be("GenericJson");
    }

    [Fact]
    public void GenericJsonTelemetryParser_Parse_ExtractsPressureGravityBatteryTiltAndRssi()
    {
        var parser = new GenericJsonTelemetryParser();
        using var doc = JsonDocument.Parse("""
        {
            "temperature": 18.5,
            "head_pressure": 15.0,
            "pressure_unit": "psi",
            "specific_gravity": 1052,
            "battery": 3.8,
            "angle": 42.12,
            "wifi_rssi": -65,
            "custom_metadata": "craft-lager"
        }
        """);

        var result = parser.Parse(doc.RootElement, TestEquipment);

        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(18.5m);
        result.PressureBar.Should().Be(1.03m); // 15 psi * 0.0689476 = 1.03 bar
        result.SpecificGravity.Should().Be(1.0520m);
        result.BatteryVoltage.Should().Be(3.8m);
        result.BatteryPercent.Should().Be(60.0m); // (3.8 - 3.2)/(4.2 - 3.2)*100 = 60%
        result.TiltDegrees.Should().Be(42.12m);
        result.Rssi.Should().Be(-65);
        result.MetricsJson.Should().Contain("custom_metadata");
    }

    [Fact]
    public void GenericJsonTelemetryParser_Parse_BatteryPercentageGreaterThanFive_UsedDirectly()
    {
        var parser = new GenericJsonTelemetryParser();
        using var doc = JsonDocument.Parse("""
        {
            "temperature": 21.0,
            "battery_pct": 88.5
        }
        """);

        var result = parser.Parse(doc.RootElement, TestEquipment);

        result.Should().NotBeNull();
        result!.BatteryPercent.Should().Be(88.5m);
        result.BatteryVoltage.Should().BeNull();
    }

    [Fact]
    public void GenericJsonTelemetryParser_Parse_NoTemperature_ReturnsNull()
    {
        var parser = new GenericJsonTelemetryParser();
        using var doc = JsonDocument.Parse("{\"gravity\": 1.045}");

        var result = parser.Parse(doc.RootElement, TestEquipment);
        result.Should().BeNull();
    }

    [Fact]
    public void UniversalBrewYouTelemetryParser_CanParseAndParse_FullCoverage()
    {
        var parser = new UniversalBrewYouTelemetryParser();
        parser.Priority.Should().Be(10);

        using var doc = JsonDocument.Parse("""
        {
            "temperature": 77.0,
            "tempUnit": "F",
            "gravity": 1.050,
            "gravityUnit": "SG",
            "pressure": 1.2,
            "pressureUnit": "bar",
            "battery": 95,
            "batteryUnit": "%",
            "tilt": 40.5,
            "rssi": -70,
            "raw": { "device": "ESP32", "firmware": "1.2.0" }
        }
        """);

        parser.CanParse(doc.RootElement, TestEquipment).Should().BeTrue();

        var result = parser.Parse(doc.RootElement, TestEquipment);
        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(25.0m);
        result.SpecificGravity.Should().Be(1.0500m);
        result.PressureBar.Should().Be(1.2m);
        result.BatteryPercent.Should().Be(95.0m);
        result.TiltDegrees.Should().Be(40.5m);
        result.Rssi.Should().Be(-70);
        result.DetectedFormat.Should().Be("BrewYouUniversal");
        result.MetricsJson.Should().Contain("ESP32");
    }

    [Fact]
    public void ISpindelTelemetryParser_ExtractsValidFields()
    {
        var parser = new ISpindelTelemetryParser();
        parser.Priority.Should().Be(20);

        using var doc = JsonDocument.Parse("""
        {
            "name": "iSpindel_Primary",
            "temperature": 19.2,
            "temp_units": "C",
            "gravity": 1045,
            "angle": 38.4,
            "battery": 4.15,
            "RSSI": -55
        }
        """);

        parser.CanParse(doc.RootElement, TestEquipment).Should().BeTrue();

        var result = parser.Parse(doc.RootElement, TestEquipment);
        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(19.2m);
        result.SpecificGravity.Should().Be(1.0450m);
        result.TiltDegrees.Should().Be(38.4m);
        result.BatteryVoltage.Should().Be(4.15m);
        result.Rssi.Should().Be(-55);
        result.DetectedFormat.Should().Be("iSpindel");
    }

    [Fact]
    public void TiltTelemetryParser_ExtractsValidFields()
    {
        var parser = new TiltTelemetryParser();
        parser.Priority.Should().Be(30);

        using var doc = JsonDocument.Parse("""
        {
            "color": "PURPLE",
            "temp": 68.0,
            "gravity": 1054,
            "rssi": -60
        }
        """);

        parser.CanParse(doc.RootElement, TestEquipment).Should().BeTrue();

        var result = parser.Parse(doc.RootElement, TestEquipment);
        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(20.0m);
        result.SpecificGravity.Should().Be(1.0540m);
        result.Rssi.Should().Be(-60);
        result.DetectedFormat.Should().Be("TiltHydrometer");
    }
}