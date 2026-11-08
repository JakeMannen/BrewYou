using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using BrewYou.ApiService.Services.Telemetry;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BrewYou.ApiService.Tests;

public class BrewLinkTelemetryAdversarialTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BrewLinkTelemetryAdversarialTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static readonly Equipment TestBrewLinkEquipment = new()
    {
        Id = Guid.NewGuid(),
        UserId = "adversarial-user",
        Name = "Adversarial Test Boiler",
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
        UserId = "adversarial-user",
        Name = "Non-BrewLink Device",
        Type = EquipmentType.Fermenter,
        Subtype = EquipmentSubtype.ConicalFermenter,
        Capacity = 60,
        BrewerySetupId = Guid.NewGuid(),
        ConnectionType = EquipmentConnectionType.None
    };

    #region 1. Malformed and Truncated JSON Payloads

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("{")]
    [InlineData("{\"currentTempC\":")]
    [InlineData("{ malformed json }")]
    [InlineData("{\"event\": \"telemetry\", ")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"just a string\"")]
    [InlineData("12345")]
    public void FromMetricsJson_MalformedOrTruncatedJson_ReturnsNullWithoutThrowing(string? malformedJson)
    {
        var act = () => BrewLinkTelemetryParser.FromMetricsJson(malformedJson);
        act.Should().NotThrow();
        var result = act();
        result.Should().BeNull();
    }

    [Fact]
    public void CanParse_NonObjectJsonElement_ReturnsFalseWithoutThrowing()
    {
        var parser = new BrewLinkTelemetryParser();

        using var arrayDoc = JsonDocument.Parse("[1, 2, 3]");
        using var stringDoc = JsonDocument.Parse("\"some string\"");
        using var numberDoc = JsonDocument.Parse("12345");
        using var boolDoc = JsonDocument.Parse("true");
        using var nullDoc = JsonDocument.Parse("null");

        parser.CanParse(arrayDoc.RootElement, TestBrewLinkEquipment).Should().BeFalse();
        parser.CanParse(stringDoc.RootElement, TestBrewLinkEquipment).Should().BeFalse();
        parser.CanParse(numberDoc.RootElement, TestBrewLinkEquipment).Should().BeFalse();
        parser.CanParse(boolDoc.RootElement, TestBrewLinkEquipment).Should().BeFalse();
        parser.CanParse(nullDoc.RootElement, TestBrewLinkEquipment).Should().BeFalse();
    }

    [Fact]
    public void Parse_NonObjectJsonElement_ReturnsNullWithoutThrowing()
    {
        var parser = new BrewLinkTelemetryParser();

        using var arrayDoc = JsonDocument.Parse("[{\"temp\": 65.0}]");
        using var stringDoc = JsonDocument.Parse("\"temp: 65.0\"");

        var arrayResult = parser.Parse(arrayDoc.RootElement, TestBrewLinkEquipment);
        var stringResult = parser.Parse(stringDoc.RootElement, TestBrewLinkEquipment);

        arrayResult.Should().BeNull();
        stringResult.Should().BeNull();
    }

    #endregion

    #region 2. Unexpected Data Types (Arrays/Numbers/Nulls instead of expected types)

    [Fact]
    public void CanParse_UnexpectedDataTypesInProperties_ShouldNotThrow()
    {
        var parser = new BrewLinkTelemetryParser();

        // "event" as number, array, object, null
        using var docNumEvent = JsonDocument.Parse("""{"event": 12345}""");
        using var docArrEvent = JsonDocument.Parse("""{"event": ["telemetry"]}""");
        using var docObjEvent = JsonDocument.Parse("""{"event": {"type": "telemetry"}}""");
        using var docNullEvent = JsonDocument.Parse("""{"event": null}""");

        // "equipment" as string or object instead of array
        using var docStrEquip = JsonDocument.Parse("""{"gateway_id": "gw1", "equipment": "string_not_array"}""");
        using var docObjEquip = JsonDocument.Parse("""{"gateway_id": "gw1", "equipment": {"slot": 1}}""");

        // "mac" and "state" with invalid types
        using var docNumMac = JsonDocument.Parse("""{"mac": 12345, "state": "not_an_object"}""");

        var act1 = () => parser.CanParse(docNumEvent.RootElement, TestNonBrewLinkEquipment);
        var act2 = () => parser.CanParse(docArrEvent.RootElement, TestNonBrewLinkEquipment);
        var act3 = () => parser.CanParse(docObjEvent.RootElement, TestNonBrewLinkEquipment);
        var act4 = () => parser.CanParse(docNullEvent.RootElement, TestNonBrewLinkEquipment);
        var act5 = () => parser.CanParse(docStrEquip.RootElement, TestNonBrewLinkEquipment);
        var act6 = () => parser.CanParse(docObjEquip.RootElement, TestNonBrewLinkEquipment);
        var act7 = () => parser.CanParse(docNumMac.RootElement, TestNonBrewLinkEquipment);

        act1.Should().NotThrow("number in event property must not throw InvalidOperationException");
        act2.Should().NotThrow("array in event property must not throw InvalidOperationException");
        act3.Should().NotThrow("object in event property must not throw InvalidOperationException");
        act4.Should().NotThrow("null in event property must not throw InvalidOperationException");
        act5.Should().NotThrow();
        act6.Should().NotThrow();
        act7.Should().NotThrow();
    }

    [Fact]
    public void Parse_UnexpectedDataTypesInFields_ShouldExtractValidFieldsWithoutThrowing()
    {
        var parser = new BrewLinkTelemetryParser();

        using var doc = JsonDocument.Parse("""
        {
            "event": 999,
            "mac": 123456,
            "rssi": "not_a_short",
            "state": {
                "temperature": {
                    "current_c": 66.5,
                    "target_c": ["unexpected", "array"],
                    "boil_c": {"unexpected": "object"},
                    "unit": 123
                },
                "heating": {
                    "active": "yes_active",
                    "power_pct": "not_a_number",
                    "ramping": 1
                },
                "pump": {
                    "active": 42
                },
                "timer": {
                    "active": "true_str",
                    "paused": null,
                    "remaining_seconds": "sixty",
                    "total_seconds": [3600]
                },
                "stage": {
                    "step_index": "one",
                    "step_name": 123,
                    "auto_mode": "auto"
                },
                "interaction": {
                    "required": "false",
                    "code": "CODE_01",
                    "message": 456
                }
            }
        }
        """);

        var act = () => parser.Parse(doc.RootElement, TestBrewLinkEquipment);
        act.Should().NotThrow("unexpected data types in payload fields must not cause parser to throw");

        var result = act();
        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(66.5m);

        var metrics = BrewLinkTelemetryParser.FromMetricsJson(result.MetricsJson);
        metrics.Should().NotBeNull();
        metrics!.CurrentTempC.Should().Be(66.5m);
        metrics.TargetTempC.Should().BeNull();
        metrics.BoilTempC.Should().BeNull();
        metrics.HeatActive.Should().BeNull();
        metrics.HeatPowerPct.Should().BeNull();
        metrics.RampActive.Should().BeNull();
        metrics.PumpActive.Should().BeNull();
    }

    [Fact]
    public void Parse_RestGatewayPayloadWithCorruptEquipmentArray_ShouldNotThrow()
    {
        var parser = new BrewLinkTelemetryParser();

        using var doc = JsonDocument.Parse("""
        {
            "gateway_id": "C44F331A8B20",
            "count": 4,
            "equipment": [
                null,
                123,
                "not_an_object",
                {
                    "slot": "not_an_int",
                    "mac": 9999,
                    "name": 1234,
                    "state": {
                        "current_temp_c": 68.2
                    }
                }
            ]
        }
        """);

        var act = () => parser.Parse(doc.RootElement, TestBrewLinkEquipment);
        act.Should().NotThrow("corrupt items and non-string mac in equipment array must not crash ResolveTargetDevice");

        var result = act();
        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(68.2m);
    }

    #endregion

    #region 3. Out-of-Range Temperatures and Extreme Decimals

    [Theory]
    [InlineData(-50.0)]
    [InlineData(250.0)]
    [InlineData(-273.15)]
    [InlineData(1500.0)]
    [InlineData(65.1234567890123456789)]
    [InlineData(-0.0000000000001)]
    public void Parse_ExtremeTemperatures_ParsesSuccessfullyWithoutCrashing(decimal temp)
    {
        var parser = new BrewLinkTelemetryParser();
        var json = $$"""{"current_temp_c": {{temp.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""";
        using var doc = JsonDocument.Parse(json);

        var result = parser.Parse(doc.RootElement, TestBrewLinkEquipment);

        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(Math.Round(temp, 2));
    }

    [Theory]
    [InlineData("-40", -40.0)] // -40 F = -40 C
    [InlineData("212", 100.0)] // 212 F = 100 C
    [InlineData("32", 0.0)]    // 32 F = 0 C
    [InlineData("450", 232.22)] // 450 F = 232.22 C
    public void Parse_ExtremeFahrenheitTemperatures_ConvertsSafely(string fahrStr, decimal expectedC)
    {
        var parser = new BrewLinkTelemetryParser();
        var json = $$"""
        {
            "state": {
                "temperature": {
                    "current_f": {{fahrStr}}
                }
            }
        }
        """;
        using var doc = JsonDocument.Parse(json);

        var result = parser.Parse(doc.RootElement, TestBrewLinkEquipment);

        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(expectedC);
    }

    [Fact]
    public void Parse_MissingRequiredKeys_ReturnsNullGracefully()
    {
        var parser = new BrewLinkTelemetryParser();

        using var docNoTemp = JsonDocument.Parse("""
        {
            "state": {
                "heat_active": true,
                "pump_active": false
            }
        }
        """);

        var result = parser.Parse(docNoTemp.RootElement, TestBrewLinkEquipment);
        result.Should().BeNull();
    }

    #endregion

    #region 4. Shorthand vs Nested Schema Combinations

    [Fact]
    public void Parse_MixedShorthandAndNestedCombinations_ExtractsAllAvailableMetrics()
    {
        var parser = new BrewLinkTelemetryParser();

        using var doc = JsonDocument.Parse("""
        {
            "temp": 64.5,
            "target_temp_c": 65.0,
            "is_heating": true,
            "heater_power": 85,
            "pump_on": true,
            "auto_mode": false,
            "timer_remaining_sec": 1200,
            "timer_total_sec": 3600,
            "timer_active": true,
            "step_index": 2,
            "step_name": "Protein Rest",
            "alert_active": true,
            "alert_code": 3,
            "alert_message": "Add Hops"
        }
        """);

        var result = parser.Parse(doc.RootElement, TestBrewLinkEquipment);

        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(64.5m);

        var metrics = BrewLinkTelemetryParser.FromMetricsJson(result.MetricsJson);
        metrics.Should().NotBeNull();
        metrics!.CurrentTempC.Should().Be(64.5m);
        metrics.TargetTempC.Should().Be(65.0m);
        metrics.HeatActive.Should().BeTrue();
        metrics.HeatPowerPct.Should().Be(85m);
        metrics.PumpActive.Should().BeTrue();
        metrics.AutoMode.Should().BeFalse();

        metrics.Timer.Should().NotBeNull();
        metrics.Timer!.Active.Should().BeTrue();
        metrics.Timer.RemainingSeconds.Should().Be(1200);
        metrics.Timer.TotalSeconds.Should().Be(3600);

        metrics.Stage.Should().NotBeNull();
        metrics.Stage!.StepIndex.Should().Be(2);
        metrics.Stage.StepName.Should().Be("Protein Rest");

        metrics.Interaction.Should().NotBeNull();
        metrics.Interaction!.Required.Should().BeTrue();
        metrics.Interaction.Code.Should().Be(3);
        metrics.Interaction.Message.Should().Be("Add Hops");
    }

    [Fact]
    public void Parse_NestedOverridesFlat_WhenBothArePresent()
    {
        var parser = new BrewLinkTelemetryParser();

        using var doc = JsonDocument.Parse("""
        {
            "current_temp_c": 50.0,
            "state": {
                "temperature": {
                    "current_c": 68.0
                },
                "heating": {
                    "active": false,
                    "power_pct": 0
                },
                "heat_active": true,
                "heat_power_pct": 100
            }
        }
        """);

        var result = parser.Parse(doc.RootElement, TestBrewLinkEquipment);

        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(68.0m);

        var metrics = BrewLinkTelemetryParser.FromMetricsJson(result.MetricsJson);
        metrics.Should().NotBeNull();
        metrics!.CurrentTempC.Should().Be(68.0m);
        metrics.HeatActive.Should().BeFalse();
        metrics.HeatPowerPct.Should().Be(0m);
    }

    #endregion

    #region 5. Edge Case MAC Address Formats

    [Theory]
    [InlineData("c4:4f:33:1a:8b:22", "C4:4F:33:1A:8B:22")]
    [InlineData("c44f331a8b22", "C4:4F:33:1A:8B:22")]
    [InlineData("C4-4F-33-1A-8B-22", "C4:4F:33:1A:8B:22")]
    [InlineData(" c4:4F:33-1a:8b:22 ", "C4:4F:33:1A:8B:22")]
    [InlineData("c4.4f.33.1a.8b.22", "C4:4F:33:1A:8B:22")]
    [InlineData("C44F331A8B22", "C4:4F:33:1A:8B:22")]
    public void FormatMac_ValidVariations_ProducesCanonicalMac(string input, string expected)
    {
        BrewLinkTelemetryParser.FormatMac(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("12:34", "12:34")] // Too short, returned trimmed
    [InlineData("C4:4F:33:1A:8B:22:99", "C4:4F:33:1A:8B:22:99")] // Too long, returned trimmed
    [InlineData("INVALID_MAC_CHARS", "INVALID_MAC_CHARS")]
    public void FormatMac_InvalidOrBoundaryInputs_HandlesGracefully(string? input, string? expected)
    {
        var result = BrewLinkTelemetryParser.FormatMac(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void CleanMac_StripsNonHexAndUppercases()
    {
        BrewLinkTelemetryParser.CleanMac("c4:4f:33:1a:8b:22").Should().Be("C44F331A8B22");
        BrewLinkTelemetryParser.CleanMac("zz-gg-11-22-33-44").Should().Be("11223344");
        BrewLinkTelemetryParser.CleanMac(null).Should().Be(string.Empty);
    }

    #endregion

    #region 6. Replay Timestamps and Boundary Countdown Values

    [Fact]
    public void ExtractTimestamp_ExtremeUnixTimestamps_ShouldNotThrow()
    {
        var parser = new BrewLinkTelemetryParser();

        // Very large timestamp_ms (e.g. 99999999999999999L)
        using var docHugeMs = JsonDocument.Parse("""
        {
            "timestamp_ms": 99999999999999999,
            "current_temp_c": 65.0
        }
        """);

        // Negative timestamp
        using var docNegMs = JsonDocument.Parse("""
        {
            "timestamp_ms": -1000,
            "current_temp_c": 65.0
        }
        """);

        // Timestamp string garbage
        using var docGarbageStr = JsonDocument.Parse("""
        {
            "timestamp": "not_a_valid_date",
            "current_temp_c": 65.0
        }
        """);

        var act1 = () => parser.Parse(docHugeMs.RootElement, TestBrewLinkEquipment);
        var act2 = () => parser.Parse(docNegMs.RootElement, TestBrewLinkEquipment);
        var act3 = () => parser.Parse(docGarbageStr.RootElement, TestBrewLinkEquipment);

        act1.Should().NotThrow("extreme timestamp_ms must fall back safely to UtcNow without ArgumentOutOfRangeException");
        act2.Should().NotThrow();
        act3.Should().NotThrow();
    }

    [Fact]
    public void TelemetryThrottlingService_CountdownJitter_IsThrottled()
    {
        var throttler = new TelemetryThrottlingService(TimeSpan.FromSeconds(30));
        var equipmentId = Guid.NewGuid();
        var startTime = DateTime.UtcNow;

        var timerMetrics = new BrewLinkTimerMetrics(
            Active: true,
            Paused: false,
            RemainingSeconds: 3600,
            TotalSeconds: 3600
        );

        var canonical = new BrewLinkCanonicalMetrics(
            CurrentTempC: 65.0m,
            TargetTempC: 65.0m,
            BoilTempC: null,
            HeatActive: true,
            HeatPowerPct: 100m,
            RampActive: false,
            PumpActive: true,
            AutoMode: true,
            Timer: timerMetrics,
            Stage: new BrewLinkStageMetrics(1, "Mash"),
            Interaction: null,
            Mac: "C4:4F:33:1A:8B:22",
            Rssi: -60,
            UpdatedAt: startTime
        );

        // 1. Initial frame -> must persist
        throttler.ShouldPersist(equipmentId, startTime, 65.0m, canonical).Should().BeTrue();

        // 2. Continuous 1Hz ticks with countdown decrementing from 3599 down to 3571 (29 seconds)
        for (var sec = 1; sec < 30; sec++)
        {
            var tickTime = startTime.AddSeconds(sec);
            var tickMetrics = canonical with
            {
                Timer = timerMetrics with { RemainingSeconds = 3600 - sec },
                UpdatedAt = tickTime
            };

            var shouldPersist = throttler.ShouldPersist(equipmentId, tickTime, 65.0m, tickMetrics);
            shouldPersist.Should().BeFalse($"countdown tick at sec {sec} should be throttled");
        }

        // 3. At 30 seconds -> time interval triggers persistence
        var t30 = startTime.AddSeconds(30);
        var t30Metrics = canonical with
        {
            Timer = timerMetrics with { RemainingSeconds = 3570 },
            UpdatedAt = t30
        };
        throttler.ShouldPersist(equipmentId, t30, 65.0m, t30Metrics).Should().BeTrue("30-second interval elapsed");
    }

    [Fact]
    public void TelemetryThrottlingService_TimerBoundary_ActiveStateToggle_PersistsImmediately()
    {
        var throttler = new TelemetryThrottlingService(TimeSpan.FromSeconds(30));
        var equipmentId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;

        var baseMetrics = new BrewLinkCanonicalMetrics(
            CurrentTempC: 65.0m,
            TargetTempC: 65.0m,
            BoilTempC: null,
            HeatActive: true,
            HeatPowerPct: 100m,
            RampActive: false,
            PumpActive: true,
            AutoMode: true,
            Timer: new BrewLinkTimerMetrics(Active: true, Paused: false, RemainingSeconds: 1, TotalSeconds: 3600),
            Stage: null,
            Interaction: null,
            Mac: null,
            Rssi: null,
            UpdatedAt: t0
        );

        // T0: Initial
        throttler.ShouldPersist(equipmentId, t0, 65.0m, baseMetrics).Should().BeTrue();

        // T1 (1 second later): Timer reaches 0 and Active flips to false
        var t1 = t0.AddSeconds(1);
        var finishedTimerMetrics = baseMetrics with
        {
            Timer = new BrewLinkTimerMetrics(Active: false, Paused: false, RemainingSeconds: 0, TotalSeconds: 3600),
            UpdatedAt = t1
        };

        throttler.ShouldPersist(equipmentId, t1, 65.0m, finishedTimerMetrics).Should().BeTrue("timer completed and active toggled to false");
    }

    [Fact]
    public void TelemetryThrottlingService_ReplayTimestamp_HandledGracefully()
    {
        var throttler = new TelemetryThrottlingService(TimeSpan.FromSeconds(30));
        var equipmentId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var metrics = new BrewLinkCanonicalMetrics(
            CurrentTempC: 65.0m,
            TargetTempC: null,
            BoilTempC: null,
            HeatActive: false,
            HeatPowerPct: null,
            RampActive: null,
            PumpActive: false,
            AutoMode: null,
            Timer: null,
            Stage: null,
            Interaction: null,
            Mac: null,
            Rssi: null,
            UpdatedAt: now
        );

        // Initial reading
        throttler.ShouldPersist(equipmentId, now, 65.0m, metrics).Should().BeTrue();

        // Packet with past timestamp arriving out of order with same temp/state
        var pastTime = now.AddMinutes(-5);
        var shouldPersist = throttler.ShouldPersist(equipmentId, pastTime, 65.0m, metrics);
        shouldPersist.Should().BeFalse("out-of-order / replay packet without changes should be throttled");
    }

    [Fact]
    public void TelemetryThrottlingService_TemperatureDeltaBoundary_TestsExactThresholds()
    {
        var throttler = new TelemetryThrottlingService(TimeSpan.FromSeconds(30));
        var equipmentId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;

        throttler.ShouldPersist(equipmentId, t0, 65.0m).Should().BeTrue();

        // +0.49°C delta after 5 seconds -> below 0.5 threshold, throttled
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(5), 65.49m).Should().BeFalse();

        // -0.49°C delta from baseline 65.0m -> throttled
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(10), 64.51m).Should().BeFalse();

        // +0.50°C delta -> exactly on threshold, must persist
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(15), 65.50m).Should().BeTrue();

        // Snapshot is now at 65.50m. Another +0.49°C to 65.99m -> throttled
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(20), 65.99m).Should().BeFalse();

        // -0.50°C delta from 65.50m to 65.00m -> must persist
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(25), 65.00m).Should().BeTrue();
    }

    [Fact]
    public void TelemetryThrottlingService_ConcurrentAccess_IsThreadSafe()
    {
        var throttler = new TelemetryThrottlingService(TimeSpan.FromSeconds(30));
        var equipmentId = Guid.NewGuid();
        var startTime = DateTime.UtcNow;

        var persistCount = 0;
        Parallel.For(0, 50, i =>
        {
            var isPersisted = throttler.ShouldPersist(equipmentId, startTime.AddSeconds(i % 10), 65.0m + (i * 0.01m));
            if (isPersisted)
            {
                Interlocked.Increment(ref persistCount);
            }
        });

        persistCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TelemetryThrottlingService_ExtremeDecimals_ShouldNotThrowOverflowException()
    {
        var throttler = new TelemetryThrottlingService(TimeSpan.FromSeconds(30));
        var equipmentId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;

        throttler.ShouldPersist(equipmentId, t0, -10.0m);

        // Extreme decimal difference: decimal.MaxValue
        var act1 = () => throttler.ShouldPersist(equipmentId, t0.AddSeconds(5), decimal.MaxValue);
        act1.Should().NotThrow("decimal delta subtraction must not throw System.OverflowException");

        // Extreme decimal difference: decimal.MinValue
        var act2 = () => throttler.ShouldPersist(equipmentId, t0.AddSeconds(10), decimal.MinValue);
        act2.Should().NotThrow("decimal delta subtraction must not throw System.OverflowException");
    }

    #endregion

    #region 7. Full Integration Ingestion and Range Checks

    private async Task<(HttpClient Client, string UserId, Guid SetupId, Equipment Equipment, string Token)> CreateUserAndEquipmentAsync(
        EquipmentConnectionType connectionType = EquipmentConnectionType.BrewLink)
    {
        var client = _factory.CreateClient();
        var email = $"adv_user_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, "Adv Brewer", "en", VolumeUnit.Liters);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        var token = envelope!.Data!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var setupsRes = await client.GetAsync("/api/v1/brewery-setups");
        var setupsEnv = await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>();
        var defaultSetupId = setupsEnv!.Data!.First(s => s.IsDefault).Id;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var connToken = "adv-token-" + Guid.NewGuid().ToString("N");
        var eq = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = envelope.Data.User.Id,
            BrewerySetupId = defaultSetupId,
            Name = "Adv Equipment " + Guid.NewGuid().ToString("N")[..6],
            Type = EquipmentType.Boiler,
            Subtype = EquipmentSubtype.AllInOne,
            Capacity = 30,
            ConnectionType = connectionType,
            ConnectionToken = connToken,
            ConnectionConfigJson = """{"brewLinkGatewayId":"C44F331A8B20","targetMac":"C4:4F:33:1A:8B:22","slot":1}"""
        };
        db.Equipment.Add(eq);
        await db.SaveChangesAsync();

        return (client, envelope.Data.User.Id, defaultSetupId, eq, connToken);
    }

    [Fact]
    public async Task TelemetryService_IngestBrewLinkTelemetryAsync_RejectsOutOfRangeTemperatures()
    {
        var (_, _, _, eq, _) = await CreateUserAndEquipmentAsync(EquipmentConnectionType.BrewLink);

        using var scope = _factory.Services.CreateScope();
        var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();

        // 1. -50°C (below MinTemperatureC of -20°C)
        using var docTooLow = JsonDocument.Parse("""{"current_temp_c": -50.0}""");
        var resLow = await telemetryService.IngestBrewLinkTelemetryAsync(eq.Id, docTooLow.RootElement);
        resLow.Status.Should().Be(TelemetryIngestStatus.OutOfRange);
        resLow.TemperatureC.Should().Be(-50.0m);

        // 2. 250°C (above MaxTemperatureC of 120°C)
        using var docTooHigh = JsonDocument.Parse("""{"current_temp_c": 250.0}""");
        var resHigh = await telemetryService.IngestBrewLinkTelemetryAsync(eq.Id, docTooHigh.RootElement);
        resHigh.Status.Should().Be(TelemetryIngestStatus.OutOfRange);
        resHigh.TemperatureC.Should().Be(250.0m);

        // 3. -20.0°C (exactly at MinTemperatureC)
        using var docMin = JsonDocument.Parse("""{"current_temp_c": -20.0}""");
        var resMin = await telemetryService.IngestBrewLinkTelemetryAsync(eq.Id, docMin.RootElement);
        resMin.Status.Should().Be(TelemetryIngestStatus.Success);
        resMin.TemperatureC.Should().Be(-20.0m);

        // 4. 120.0°C (exactly at MaxTemperatureC)
        using var docMax = JsonDocument.Parse("""{"current_temp_c": 120.0}""");
        var resMax = await telemetryService.IngestBrewLinkTelemetryAsync(eq.Id, docMax.RootElement);
        resMax.Status.Should().Be(TelemetryIngestStatus.Success);
        resMax.TemperatureC.Should().Be(120.0m);
    }

    [Fact]
    public async Task TelemetryEndpoint_IngestTelemetry_WithNonStringEvent_ShouldNotThrow500()
    {
        var (client, _, _, _, token) = await CreateUserAndEquipmentAsync(EquipmentConnectionType.HttpPush);
        var publicClient = _factory.CreateClient();

        // Payload with non-string "event" property that triggers CanParse on BrewLinkTelemetryParser
        var payload = new
        {
            @event = 12345,
            temperature = 22.5,
            tempUnit = "C"
        };

        var response = await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{token}", payload);

        // Should return 200 OK, NEVER 500 Internal Server Error
        response.StatusCode.Should().Be(HttpStatusCode.OK, "non-string event property must not cause 500 Internal Server Error");
    }

    #endregion
}