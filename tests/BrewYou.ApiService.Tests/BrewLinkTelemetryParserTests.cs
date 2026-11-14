using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services.Telemetry;
using FluentAssertions;
using System.Text.Json;

namespace BrewYou.ApiService.Tests;

public class BrewLinkTelemetryParserTests
{
    private static readonly Equipment TestBrewLinkEquipment = new()
    {
        Id = Guid.NewGuid(),
        UserId = "test-user-id",
        Name = "Grainfather G30 Connect",
        Type = EquipmentType.Boiler,
        Subtype = EquipmentSubtype.AllInOne,
        Capacity = 30,
        BrewerySetupId = Guid.NewGuid(),
        ConnectionType = EquipmentConnectionType.BrewLink,
        ConnectionConfigJson = """{"brewLinkGatewayId":"C44F331A8B20","targetMac":"C4:4F:33:1A:8B:22","slot":1}"""
    };

    private static readonly Equipment TestNonBrewLinkEquipment = new()
    {
        Id = Guid.NewGuid(),
        UserId = "test-user-id",
        Name = "Generic Fermenter",
        Type = EquipmentType.Fermenter,
        Subtype = EquipmentSubtype.ConicalFermenter,
        Capacity = 60,
        BrewerySetupId = Guid.NewGuid(),
        ConnectionType = EquipmentConnectionType.None
    };

    [Fact]
    public void CanParse_BrewLinkEquipment_ReturnsTrue()
    {
        var parser = new BrewLinkTelemetryParser();
        using var doc = JsonDocument.Parse("""{"some_metric": 123}""");

        var canParse = parser.CanParse(doc.RootElement, TestBrewLinkEquipment);

        canParse.Should().BeTrue();
    }

    [Fact]
    public void CanParse_WebSocketTelemetryEvent_ReturnsTrue()
    {
        var parser = new BrewLinkTelemetryParser();
        using var doc = JsonDocument.Parse("""
        {
            "event": "telemetry",
            "mac": "C44F331A8B22",
            "state": {}
        }
        """);

        var canParse = parser.CanParse(doc.RootElement, TestNonBrewLinkEquipment);

        canParse.Should().BeTrue();
    }

    [Fact]
    public void CanParse_RestGatewayPayloadWithEquipmentArray_ReturnsTrue()
    {
        var parser = new BrewLinkTelemetryParser();
        using var doc = JsonDocument.Parse("""
        {
            "gateway_id": "C44F331A8B20",
            "count": 1,
            "equipment": [
                { "slot": 1, "mac": "C4:4F:33:1A:8B:22", "state": {} }
            ]
        }
        """);

        var canParse = parser.CanParse(doc.RootElement, TestNonBrewLinkEquipment);

        canParse.Should().BeTrue();
    }

    [Fact]
    public void CanParse_DeviceObjectWithMacAndState_ReturnsTrue()
    {
        var parser = new BrewLinkTelemetryParser();
        using var doc = JsonDocument.Parse("""
        {
            "mac": "C4:4F:33:1A:8B:22",
            "state": { "current_temp_c": 65.0 }
        }
        """);

        var canParse = parser.CanParse(doc.RootElement, TestNonBrewLinkEquipment);

        canParse.Should().BeTrue();
    }

    [Fact]
    public void CanParse_FlatPayloadWithBrewLinkAttributes_ReturnsTrue()
    {
        var parser = new BrewLinkTelemetryParser();
        using var doc = JsonDocument.Parse("""{"current_temp_c": 65.0, "heat_active": true}""");

        var canParse = parser.CanParse(doc.RootElement, TestNonBrewLinkEquipment);

        canParse.Should().BeTrue();
    }

    [Fact]
    public void CanParse_UnrelatedJsonWithNonBrewLinkEquipment_ReturnsFalse()
    {
        var parser = new BrewLinkTelemetryParser();
        using var doc = JsonDocument.Parse("""{"unrelated_field": "test_value"}""");

        var canParse = parser.CanParse(doc.RootElement, TestNonBrewLinkEquipment);

        canParse.Should().BeFalse();
    }

    [Fact]
    public void CanParse_NonObjectJson_ReturnsFalse()
    {
        var parser = new BrewLinkTelemetryParser();
        using var doc = JsonDocument.Parse("""[1, 2, 3]""");

        var canParse = parser.CanParse(doc.RootElement, TestBrewLinkEquipment);

        canParse.Should().BeFalse();
    }

    [Fact]
    public void Parse_WebSocketFrame_NestedSchema_ExtractsAllCanonicalFieldsAccurately()
    {
        var parser = new BrewLinkTelemetryParser();
        using var doc = JsonDocument.Parse("""
        {
            "event": "telemetry",
            "mac": "C44F331A8B22",
            "equipment_type": "boiler",
            "model": "Grainfather G30",
            "rssi": -60,
            "state": {
                "temperature": {
                    "current_c": 65.0,
                    "target_c": 65.0,
                    "boil_c": 100.0,
                    "unit": "C"
                },
                "heating": {
                    "active": true,
                    "power_pct": 100,
                    "ramping": false
                },
                "pump": {
                    "active": true
                },
                "timer": {
                    "active": true,
                    "paused": false,
                    "remaining_seconds": 2340,
                    "total_seconds": 3600
                },
                "stage": {
                    "step_index": 1,
                    "step_name": "Mash In",
                    "auto_mode": true
                },
                "interaction": {
                    "required": false,
                    "code": 0,
                    "message": "Normal operation"
                }
            },
            "timestamp_ms": 1459200
        }
        """);

        var result = parser.Parse(doc.RootElement, TestBrewLinkEquipment);

        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(65.0m);
        result.Rssi.Should().Be(-60);
        result.DetectedFormat.Should().Be("BrewLink");
        result.MetricsJson.Should().NotBeNullOrWhiteSpace();

        var canonical = JsonSerializer.Deserialize<BrewLinkCanonicalMetrics>(result.MetricsJson!, BrewLinkTelemetryParser.JsonOptions);
        canonical.Should().NotBeNull();
        canonical!.CurrentTempC.Should().Be(65.0m);
        canonical.TargetTempC.Should().Be(65.0m);
        canonical.BoilTempC.Should().Be(100.0m);
        canonical.HeatActive.Should().BeTrue();
        canonical.HeatPowerPct.Should().Be(100m);
        canonical.RampActive.Should().BeFalse();
        canonical.PumpActive.Should().BeTrue();
        canonical.AutoMode.Should().BeTrue();
        canonical.Mac.Should().Be("C4:4F:33:1A:8B:22");
        canonical.Rssi.Should().Be(-60);

        canonical.Timer.Should().NotBeNull();
        canonical.Timer!.Active.Should().BeTrue();
        canonical.Timer.Paused.Should().BeFalse();
        canonical.Timer.RemainingSeconds.Should().Be(2340);
        canonical.Timer.TotalSeconds.Should().Be(3600);

        canonical.Stage.Should().NotBeNull();
        canonical.Stage!.StepIndex.Should().Be(1);
        canonical.Stage.StepName.Should().Be("Mash In");
        canonical.Stage.AutoMode.Should().BeTrue();

        canonical.Interaction.Should().NotBeNull();
        canonical.Interaction!.Required.Should().BeFalse();
        canonical.Interaction.Code.Should().Be(0);
        canonical.Interaction.Message.Should().Be("Normal operation");

        canonical.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Parse_RestGatewayArray_MatchesTargetEquipmentByMac_ExtractsCanonicalFields()
    {
        var parser = new BrewLinkTelemetryParser();
        using var doc = JsonDocument.Parse("""
        {
            "gateway_id": "C44F331A8B20",
            "count": 2,
            "equipment": [
                {
                    "slot": 1,
                    "mac": "C4:4F:33:1A:8B:22",
                    "rssi": -62,
                    "name": "Grainfather G30 Connect",
                    "state": {
                        "current_temp_c": 64.8,
                        "target_temp_c": 65.0,
                        "boil_temp_c": 100.0,
                        "heat_active": true,
                        "heat_power_pct": 100,
                        "ramp_active": false,
                        "pump_active": true,
                        "auto_mode": true,
                        "timer": {
                            "active": true,
                            "paused": false,
                            "remaining_seconds": 2400,
                            "total_seconds": 3600
                        },
                        "stage": {
                            "step_index": 1,
                            "step_name": "Mash Step 1"
                        },
                        "interaction": {
                            "required": false,
                            "code": 0,
                            "message": "Normal operation"
                        }
                    }
                },
                {
                    "slot": 2,
                    "mac": "C4:4F:33:1A:8B:99",
                    "rssi": -75,
                    "name": "Secondary Fermenter",
                    "state": {
                        "current_temp_c": 19.5,
                        "target_temp_c": 20.0
                    }
                }
            ]
        }
        """);

        var result = parser.Parse(doc.RootElement, TestBrewLinkEquipment);

        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(64.8m);
        result.Rssi.Should().Be(-62);

        var canonical = JsonSerializer.Deserialize<BrewLinkCanonicalMetrics>(result.MetricsJson!, BrewLinkTelemetryParser.JsonOptions);
        canonical.Should().NotBeNull();
        canonical!.CurrentTempC.Should().Be(64.8m);
        canonical.TargetTempC.Should().Be(65.0m);
        canonical.BoilTempC.Should().Be(100.0m);
        canonical.HeatActive.Should().BeTrue();
        canonical.HeatPowerPct.Should().Be(100m);
        canonical.PumpActive.Should().BeTrue();
        canonical.Mac.Should().Be("C4:4F:33:1A:8B:22");
        canonical.Stage!.StepName.Should().Be("Mash Step 1");
        canonical.Timer!.RemainingSeconds.Should().Be(2400);
    }

    [Fact]
    public void Parse_RestGatewayArray_MatchesTargetEquipmentBySlot_WhenConfigured()
    {
        var parser = new BrewLinkTelemetryParser();
        var equipmentSlot2 = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = "test-user-id",
            Name = "Fermenter",
            Type = EquipmentType.Fermenter,
            Subtype = EquipmentSubtype.ConicalFermenter,
            ConnectionType = EquipmentConnectionType.BrewLink,
            ConnectionConfigJson = """{"brewLinkGatewayId":"C44F331A8B20","slot":2}"""
        };

        using var doc = JsonDocument.Parse("""
        {
            "gateway_id": "C44F331A8B20",
            "equipment": [
                { "slot": 1, "mac": "AA:BB:CC:DD:EE:01", "state": { "current_temp_c": 65.0 } },
                { "slot": 2, "mac": "AA:BB:CC:DD:EE:02", "state": { "current_temp_c": 19.8 } }
            ]
        }
        """);

        var result = parser.Parse(doc.RootElement, equipmentSlot2);

        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(19.8m);
        var canonical = JsonSerializer.Deserialize<BrewLinkCanonicalMetrics>(result.MetricsJson!, BrewLinkTelemetryParser.JsonOptions);
        canonical!.Mac.Should().Be("AA:BB:CC:DD:EE:02");
    }

    [Fact]
    public void Parse_MqttTopicPayload_FlatSchema_ExtractsCanonicalFields()
    {
        var parser = new BrewLinkTelemetryParser();
        using var doc = JsonDocument.Parse("""
        {
            "mac": "C4:4F:33:1A:8B:22",
            "rssi": -58,
            "current_temp_c": 66.5,
            "target_temp_c": 67.0,
            "boil_temp_c": 100.0,
            "heat_active": true,
            "heat_power_pct": 80,
            "ramp_active": true,
            "pump_active": false,
            "auto_mode": true,
            "timer_active": true,
            "timer_paused": false,
            "timer_remaining_sec": 1800,
            "timer_total_sec": 3600,
            "step_index": 2,
            "step_name": "Saccharification",
            "interaction_required": false,
            "interaction_code": 0,
            "interaction_message": "Running step 2"
        }
        """);

        var result = parser.Parse(doc.RootElement, TestBrewLinkEquipment);

        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(66.5m);
        result.Rssi.Should().Be(-58);

        var canonical = JsonSerializer.Deserialize<BrewLinkCanonicalMetrics>(result.MetricsJson!, BrewLinkTelemetryParser.JsonOptions);
        canonical.Should().NotBeNull();
        canonical!.CurrentTempC.Should().Be(66.5m);
        canonical.TargetTempC.Should().Be(67.0m);
        canonical.BoilTempC.Should().Be(100.0m);
        canonical.HeatActive.Should().BeTrue();
        canonical.HeatPowerPct.Should().Be(80m);
        canonical.RampActive.Should().BeTrue();
        canonical.PumpActive.Should().BeFalse();
        canonical.AutoMode.Should().BeTrue();
        canonical.Timer!.RemainingSeconds.Should().Be(1800);
        canonical.Timer.TotalSeconds.Should().Be(3600);
        canonical.Stage!.StepIndex.Should().Be(2);
        canonical.Stage.StepName.Should().Be("Saccharification");
        canonical.Interaction!.Message.Should().Be("Running step 2");
    }

    [Fact]
    public void Parse_TemperatureConversion_FahrenheitToCelsius_NestedAndFlat()
    {
        var parser = new BrewLinkTelemetryParser();

        // 1. Nested with unit = "F" (149 F = 65 C)
        using var nestedDoc = JsonDocument.Parse("""
        {
            "mac": "C4:4F:33:1A:8B:22",
            "state": {
                "temperature": {
                    "current": 149.0,
                    "target": 150.8,
                    "boil": 212.0,
                    "unit": "F"
                }
            }
        }
        """);

        var nestedResult = parser.Parse(nestedDoc.RootElement, TestBrewLinkEquipment);
        nestedResult.Should().NotBeNull();
        nestedResult!.TemperatureC.Should().Be(65.0m);

        var nestedCanonical = JsonSerializer.Deserialize<BrewLinkCanonicalMetrics>(nestedResult.MetricsJson!, BrewLinkTelemetryParser.JsonOptions);
        nestedCanonical!.CurrentTempC.Should().Be(65.0m);
        nestedCanonical.TargetTempC.Should().Be(66.0m); // 150.8 F = (150.8-32)*5/9 = 118.8*5/9 = 66 C
        nestedCanonical.BoilTempC.Should().Be(100.0m);   // 212 F = 100 C

        // 2. Flat with explicit current_temp_f
        using var flatDoc = JsonDocument.Parse("""
        {
            "mac": "C4:4F:33:1A:8B:22",
            "current_temp_f": 68.0,
            "target_temp_f": 68.0
        }
        """);

        var flatResult = parser.Parse(flatDoc.RootElement, TestBrewLinkEquipment);
        flatResult.Should().NotBeNull();
        flatResult!.TemperatureC.Should().Be(20.0m); // 68 F = 20 C
    }

    [Fact]
    public void Parse_ShorthandProperties_FallbacksForLegacyGatewayEmissions()
    {
        var parser = new BrewLinkTelemetryParser();
        using var doc = JsonDocument.Parse("""
        {
            "mac": "C44F331A8B22",
            "state": {
                "temp_c": 64.8,
                "target_temp_c": 65.0,
                "heat_pct": 75,
                "pump_on": true,
                "step_index": 3,
                "timer_remaining_sec": 1200
            }
        }
        """);

        var result = parser.Parse(doc.RootElement, TestBrewLinkEquipment);

        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(64.8m);

        var canonical = JsonSerializer.Deserialize<BrewLinkCanonicalMetrics>(result.MetricsJson!, BrewLinkTelemetryParser.JsonOptions);
        canonical.Should().NotBeNull();
        canonical!.CurrentTempC.Should().Be(64.8m);
        canonical.TargetTempC.Should().Be(65.0m);
        canonical.HeatPowerPct.Should().Be(75m);
        canonical.HeatActive.Should().BeTrue();
        canonical.PumpActive.Should().BeTrue();
        canonical.Stage!.StepIndex.Should().Be(3);
        canonical.Timer!.RemainingSeconds.Should().Be(1200);
        canonical.Timer.Active.Should().BeTrue();
    }

    [Fact]
    public void Parse_ActiveInteractionAlertPrompt_ExtractsPromptAndFlags()
    {
        var parser = new BrewLinkTelemetryParser();

        // 1. Nested prompt
        using var nestedDoc = JsonDocument.Parse("""
        {
            "mac": "C4:4F:33:1A:8B:22",
            "state": {
                "current_temp_c": 65.0,
                "interaction": {
                    "required": true,
                    "code": 1,
                    "message": "Add Grain and Press Set"
                }
            }
        }
        """);

        var nestedResult = parser.Parse(nestedDoc.RootElement, TestBrewLinkEquipment);
        var nestedCanonical = JsonSerializer.Deserialize<BrewLinkCanonicalMetrics>(nestedResult!.MetricsJson!, BrewLinkTelemetryParser.JsonOptions);
        nestedCanonical!.Interaction.Should().NotBeNull();
        nestedCanonical.Interaction!.Required.Should().BeTrue();
        nestedCanonical.Interaction.Code.Should().Be(1);
        nestedCanonical.Interaction.Message.Should().Be("Add Grain and Press Set");

        // 2. Flat prompt shorthand
        using var flatDoc = JsonDocument.Parse("""
        {
            "current_temp_c": 75.0,
            "prompt": "Sparge Water Ready"
        }
        """);

        var flatResult = parser.Parse(flatDoc.RootElement, TestBrewLinkEquipment);
        var flatCanonical = JsonSerializer.Deserialize<BrewLinkCanonicalMetrics>(flatResult!.MetricsJson!, BrewLinkTelemetryParser.JsonOptions);
        flatCanonical!.Interaction.Should().NotBeNull();
        flatCanonical.Interaction!.Required.Should().BeTrue();
        flatCanonical.Interaction.Message.Should().Be("Sparge Water Ready");
    }

    [Fact]
    public void Parse_MissingOptionalObjects_GracefulNullHandlingWithoutExceptions()
    {
        var parser = new BrewLinkTelemetryParser();
        using var doc = JsonDocument.Parse("""
        {
            "current_temp_c": 65.0
        }
        """);

        var result = parser.Parse(doc.RootElement, TestBrewLinkEquipment);

        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(65.0m);

        var canonical = JsonSerializer.Deserialize<BrewLinkCanonicalMetrics>(result.MetricsJson!, BrewLinkTelemetryParser.JsonOptions);
        canonical.Should().NotBeNull();
        canonical!.CurrentTempC.Should().Be(65.0m);
        canonical.Timer.Should().BeNull();
        canonical.Stage.Should().BeNull();
        canonical.Interaction.Should().BeNull();
    }

    [Fact]
    public void Parse_TimestampHandling_EpochMillisecondsVsDeviceUptime()
    {
        var parser = new BrewLinkTelemetryParser();

        // 1. Device uptime in ms (1459200 ms = ~24 min uptime -> defaults to UtcNow)
        using var uptimeDoc = JsonDocument.Parse("""
        {
            "current_temp_c": 65.0,
            "timestamp_ms": 1459200
        }
        """);
        var uptimeResult = parser.Parse(uptimeDoc.RootElement, TestBrewLinkEquipment);
        var uptimeCanonical = JsonSerializer.Deserialize<BrewLinkCanonicalMetrics>(uptimeResult!.MetricsJson!, BrewLinkTelemetryParser.JsonOptions);
        uptimeCanonical!.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        // 2. Real epoch milliseconds
        var testEpochMs = 1731000000000L; // 2024-11-07T17:20:00Z
        using var epochDoc = JsonDocument.Parse($$"""
        {
            "current_temp_c": 65.0,
            "timestamp_ms": {{testEpochMs}}
        }
        """);
        var epochResult = parser.Parse(epochDoc.RootElement, TestBrewLinkEquipment);
        var epochCanonical = JsonSerializer.Deserialize<BrewLinkCanonicalMetrics>(epochResult!.MetricsJson!, BrewLinkTelemetryParser.JsonOptions);
        epochCanonical!.UpdatedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(testEpochMs).UtcDateTime);
    }

    [Fact]
    public void Parse_MacAddressFormatting_NormalizesToUppercaseColonDelimited()
    {
        // 12-char hex without colons
        BrewLinkTelemetryParser.FormatMac("c44f331a8b22").Should().Be("C4:4F:33:1A:8B:22");

        // Already with colons, lowercase
        BrewLinkTelemetryParser.FormatMac("c4:4f:33:1a:8b:22").Should().Be("C4:4F:33:1A:8B:22");

        // CleanMac strips punctuation
        BrewLinkTelemetryParser.CleanMac("C4:4F:33:1A:8B:22").Should().Be("C44F331A8B22");
    }

    [Fact]
    public void RoundTripSerialization_LatestMetricsJsonMatchesFrontendRequirements()
    {
        var canonical = new BrewLinkCanonicalMetrics(
            CurrentTempC: 65.0m,
            TargetTempC: 65.0m,
            BoilTempC: 100.0m,
            HeatActive: true,
            HeatPowerPct: 100m,
            RampActive: false,
            PumpActive: true,
            AutoMode: true,
            Timer: new BrewLinkTimerMetrics(Active: true, Paused: false, RemainingSeconds: 2340, TotalSeconds: 3600),
            Stage: new BrewLinkStageMetrics(StepIndex: 1, StepName: "Mash In", AutoMode: true),
            Interaction: new BrewLinkInteractionMetrics(Required: false, Code: 0, Message: "Normal operation"),
            Mac: "C4:4F:33:1A:8B:22",
            Rssi: -60,
            UpdatedAt: DateTime.UtcNow
        );

        var json = BrewLinkTelemetryParser.ToMetricsJson(canonical);
        json.Should().Contain("\"currentTempC\":65.0");
        json.Should().Contain("\"heatActive\":true");
        json.Should().Contain("\"pumpActive\":true");
        json.Should().Contain("\"mac\":\"C4:4F:33:1A:8B:22\"");

        var roundTripped = JsonSerializer.Deserialize<BrewLinkCanonicalMetrics>(json, BrewLinkTelemetryParser.JsonOptions);
        roundTripped.Should().BeEquivalentTo(canonical, options => options.Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromMilliseconds(50))).WhenTypeIs<DateTime>());
    }

    [Fact]
    public void Parse_G30FlagsAndErrorCode_ExtractsFromNestedFlagsAndFlatState()
    {
        var parser = new BrewLinkTelemetryParser();
        using var nestedDoc = JsonDocument.Parse("""
        {
            "event": "telemetry",
            "mac": "C44F331A8B22",
            "state": {
                "temperature": { "current_c": 65.0, "target_c": 65.0, "boil_c": 100.0 },
                "flags": {
                    "delayed_heat": true,
                    "interrupted": true,
                    "manual_power": true,
                    "sparge_alert": true
                },
                "error_code": 261
            }
        }
        """);

        var nestedResult = parser.Parse(nestedDoc.RootElement, TestBrewLinkEquipment);
        nestedResult.Should().NotBeNull();
        var nestedCanonical = JsonSerializer.Deserialize<BrewLinkCanonicalMetrics>(nestedResult!.MetricsJson!, BrewLinkTelemetryParser.JsonOptions);
        nestedCanonical!.DelayedHeat.Should().BeTrue();
        nestedCanonical.Interrupted.Should().BeTrue();
        nestedCanonical.ManualPower.Should().BeTrue();
        nestedCanonical.SpargeAlert.Should().BeTrue();
        nestedCanonical.ErrorCode.Should().Be(261);

        using var flatDoc = JsonDocument.Parse("""
        {
            "current_temp_c": 99.0,
            "delayed_heat": false,
            "interrupted": false,
            "manual_power": false,
            "sparge_alert": true,
            "error_code": 0
        }
        """);

        var flatResult = parser.Parse(flatDoc.RootElement, TestBrewLinkEquipment);
        flatResult.Should().NotBeNull();
        var flatCanonical = JsonSerializer.Deserialize<BrewLinkCanonicalMetrics>(flatResult!.MetricsJson!, BrewLinkTelemetryParser.JsonOptions);
        flatCanonical!.DelayedHeat.Should().BeFalse();
        flatCanonical.Interrupted.Should().BeFalse();
        flatCanonical.ManualPower.Should().BeFalse();
        flatCanonical.SpargeAlert.Should().BeTrue();
        flatCanonical.ErrorCode.Should().Be(0);
    }
}