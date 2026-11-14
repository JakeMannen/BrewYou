using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using BrewYou.ApiService.Services.Telemetry;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BrewYou.ApiService.Tests;

public class BrewLinkEmpiricalStressTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BrewLinkEmpiricalStressTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    #region 1. High-Frequency Stream & Throttling Verification (1,000 Frames)

    [Fact]
    public void HighFrequencyStream_1000Frames_IdenticalData_Within30s_OnlyPersistsOnce()
    {
        var throttler = new TelemetryThrottlingService();
        var equipmentId = Guid.NewGuid();
        var t0 = new DateTime(2026, 11, 7, 20, 0, 0, DateTimeKind.Utc);

        var metrics = new BrewLinkCanonicalMetrics(
            CurrentTempC: 65.0m,
            TargetTempC: 65.0m,
            BoilTempC: 100.0m,
            HeatActive: true,
            HeatPowerPct: 80m,
            RampActive: false,
            PumpActive: true,
            AutoMode: true,
            Timer: new BrewLinkTimerMetrics(Active: true, Paused: false, RemainingSeconds: 3600, TotalSeconds: 3600),
            Stage: new BrewLinkStageMetrics(StepIndex: 1, StepName: "Mash In"),
            Interaction: new BrewLinkInteractionMetrics(Required: false, Code: 0, Message: "Normal"),
            Mac: "C4:4F:33:1A:8B:22",
            Rssi: -60,
            UpdatedAt: t0
        );

        var persistedCount = 0;
        var throttledCount = 0;

        // Push 1,000 frames within 29.9 seconds (every ~29.9ms)
        for (var i = 0; i < 1000; i++)
        {
            var frameTime = t0.AddMilliseconds(i * 29.9);
            var shouldPersist = throttler.ShouldPersist(
                equipmentId,
                frameTime,
                65.0m,
                metrics
            );

            if (shouldPersist)
            {
                persistedCount++;
            }
            else
            {
                throttledCount++;
            }
        }

        persistedCount.Should().Be(1, "only the initial frame in a 30-second steady stream should persist");
        throttledCount.Should().Be(999, "all subsequent 999 frames within 30s with identical state must be throttled");

        // Frame 1001 at t = 30.1s -> Must persist!
        var t30 = t0.AddSeconds(30.1);
        var persistsAfter30s = throttler.ShouldPersist(equipmentId, t30, 65.0m, metrics);
        persistsAfter30s.Should().BeTrue("frame at t >= 30 seconds must trigger persistence");
    }

    [Fact]
    public void HighFrequencyStream_1000Frames_RapidTimerCountdown_DoesNotTriggerPersistence()
    {
        var throttler = new TelemetryThrottlingService();
        var equipmentId = Guid.NewGuid();
        var t0 = new DateTime(2026, 11, 7, 20, 0, 0, DateTimeKind.Utc);

        var persistedCount = 0;

        // Simulate 1,000 frames where RemainingSeconds counts down (3600 -> 2601) within 25 seconds
        for (var i = 0; i < 1000; i++)
        {
            var frameTime = t0.AddMilliseconds(i * 25);
            var remaining = 3600 - i;
            var metrics = new BrewLinkCanonicalMetrics(
                CurrentTempC: 65.0m,
                TargetTempC: 65.0m,
                BoilTempC: 100.0m,
                HeatActive: true,
                HeatPowerPct: 100m,
                RampActive: false,
                PumpActive: true,
                AutoMode: true,
                Timer: new BrewLinkTimerMetrics(Active: true, Paused: false, RemainingSeconds: remaining, TotalSeconds: 3600),
                Stage: new BrewLinkStageMetrics(StepIndex: 1, StepName: "Mash In"),
                Interaction: new BrewLinkInteractionMetrics(Required: false, Code: 0, Message: null),
                Mac: "C4:4F:33:1A:8B:22",
                Rssi: -60,
                UpdatedAt: frameTime
            );

            if (throttler.ShouldPersist(equipmentId, frameTime, 65.0m, metrics))
            {
                persistedCount++;
            }
        }

        persistedCount.Should().Be(1, "normal 1Hz timer countdown ticks must be filtered out and never bypass the 30s throttle");
    }

    #endregion

    #region 2. Temperature Jump Verification (>= 0.5°C Delta)

    [Theory]
    [InlineData(0.00, false)]
    [InlineData(0.10, false)]
    [InlineData(-0.20, false)]
    [InlineData(0.40, false)]
    [InlineData(0.49, false)]
    [InlineData(-0.49, false)]
    [InlineData(0.50, true)]
    [InlineData(-0.50, true)]
    [InlineData(0.51, true)]
    [InlineData(-0.51, true)]
    [InlineData(1.50, true)]
    [InlineData(-3.00, true)]
    public void TemperatureDelta_Threshold_CorrectlyEvaluated(decimal delta, bool expectedPersist)
    {
        var throttler = new TelemetryThrottlingService();
        var equipmentId = Guid.NewGuid();
        var t0 = new DateTime(2026, 11, 7, 20, 0, 0, DateTimeKind.Utc);

        // Frame 1: Baseline at 65.0°C (always persists)
        var first = throttler.ShouldPersist(equipmentId, t0, 65.0m);
        first.Should().BeTrue();

        // Frame 2: 5 seconds later with delta
        var t1 = t0.AddSeconds(5);
        var second = throttler.ShouldPersist(equipmentId, t1, 65.0m + delta);

        second.Should().Be(expectedPersist, $"delta of {delta}°C relative to baseline should result in persist = {expectedPersist}");
    }

    [Fact]
    public void TemperatureDrift_CumulativeSum_TriggersPersistenceWhenTotalDriftReaches0Point5()
    {
        var throttler = new TelemetryThrottlingService();
        var equipmentId = Guid.NewGuid();
        var t0 = new DateTime(2026, 11, 7, 20, 0, 0, DateTimeKind.Utc);

        // Frame 1: 65.0°C -> persists (count = 1)
        throttler.ShouldPersist(equipmentId, t0, 65.0m).Should().BeTrue();

        // Frame 2: t = 2s, 65.2°C (|Δ| = 0.2 < 0.5) -> throttled
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(2), 65.2m).Should().BeFalse();

        // Frame 3: t = 4s, 65.4°C (|Δ| = 0.4 from last persisted 65.0 < 0.5) -> throttled
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(4), 65.4m).Should().BeFalse();

        // Frame 4: t = 6s, 65.6°C (|Δ| = 0.6 from last persisted 65.0 >= 0.5) -> PERSISTS!
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(6), 65.6m).Should().BeTrue();

        // Frame 5: t = 8s, 65.8°C (|Δ| = 0.2 from NEW baseline 65.6 < 0.5) -> throttled!
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(8), 65.8m).Should().BeFalse();

        // Frame 6: t = 10s, 65.1°C (|Δ| = 0.5 from baseline 65.6 >= 0.5) -> PERSISTS!
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(10), 65.1m).Should().BeTrue();
    }

    #endregion

    #region 3. Discrete Actuator & State Changes Verification

    [Fact]
    public void ActuatorStateChanges_BypassTimeCadenceAndPersistImmediately()
    {
        var throttler = new TelemetryThrottlingService();
        var equipmentId = Guid.NewGuid();
        var t0 = new DateTime(2026, 11, 7, 20, 0, 0, DateTimeKind.Utc);

        var baseline = new BrewLinkCanonicalMetrics(
            CurrentTempC: 65.0m,
            TargetTempC: 65.0m,
            BoilTempC: 100.0m,
            HeatActive: false,
            HeatPowerPct: 0m,
            RampActive: false,
            PumpActive: false,
            AutoMode: false,
            Timer: new BrewLinkTimerMetrics(Active: false, Paused: false, RemainingSeconds: 0, TotalSeconds: 0),
            Stage: new BrewLinkStageMetrics(StepIndex: 1, StepName: "Mash In"),
            Interaction: new BrewLinkInteractionMetrics(Required: false, Code: 0, Message: null),
            Mac: "C4:4F:33:1A:8B:22",
            Rssi: -60,
            UpdatedAt: t0
        );

        // 1. Initial persistence
        throttler.ShouldPersist(equipmentId, t0, 65.0m, baseline).Should().BeTrue();

        // 2. HeatActive toggled -> Persists
        var heatOn = baseline with { HeatActive = true, HeatPowerPct = 100m };
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(1), 65.0m, heatOn).Should().BeTrue();

        // 3. HeatPowerPct changed (100 -> 50) -> Persists
        var heatHalf = heatOn with { HeatPowerPct = 50m };
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(2), 65.0m, heatHalf).Should().BeTrue();

        // 4. PumpActive toggled -> Persists
        var pumpOn = heatHalf with { PumpActive = true };
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(3), 65.0m, pumpOn).Should().BeTrue();

        // 5. Timer activated -> Persists
        var timerOn = pumpOn with { Timer = new BrewLinkTimerMetrics(Active: true, Paused: false, RemainingSeconds: 3600, TotalSeconds: 3600) };
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(4), 65.0m, timerOn).Should().BeTrue();

        // 6. Timer paused -> Persists
        var timerPaused = timerOn with { Timer = timerOn.Timer! with { Paused = true } };
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(5), 65.0m, timerPaused).Should().BeTrue();

        // 7. Interaction prompt appears -> Persists
        var alertOn = timerPaused with { Interaction = new BrewLinkInteractionMetrics(Required: true, Code: 201, Message: "Add Sparge Water") };
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(6), 65.0m, alertOn).Should().BeTrue();

        // 8. Interaction prompt dismissed -> Persists
        var alertOff = alertOn with { Interaction = new BrewLinkInteractionMetrics(Required: false, Code: 0, Message: null) };
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(7), 65.0m, alertOff).Should().BeTrue();

        // 9. Mash Step advanced -> Persists
        var stepNext = alertOff with { Stage = new BrewLinkStageMetrics(StepIndex: 2, StepName: "Sparge") };
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(8), 65.0m, stepNext).Should().BeTrue();

        // 10. Steady state 2 seconds later -> Throttled!
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(10), 65.0m, stepNext).Should().BeFalse();
    }

    #endregion

    #region 4. Multi-Gateway Concurrency & Thread-Safety Stress Harness

    [Fact]
    public async Task MultiGateway_ConcurrentStreams_ThreadSafeAndAccuratelyIsolated()
    {
        var throttler = new TelemetryThrottlingService();
        const int gatewayCount = 20;
        const int framesPerGateway = 500;
        var gatewayIds = Enumerable.Range(0, gatewayCount).Select(_ => Guid.NewGuid()).ToList();

        var t0 = new DateTime(2026, 11, 7, 20, 0, 0, DateTimeKind.Utc);
        var persistCounts = new ConcurrentDictionary<Guid, int>();

        var tasks = gatewayIds.Select(async gwId =>
        {
            await Task.Yield();
            var localPersisted = 0;

            for (var frame = 0; frame < framesPerGateway; frame++)
            {
                var frameTime = t0.AddMilliseconds(frame * 50); // 25 seconds total

                // Pattern:
                // Frame 0: Baseline (60.0°C) -> Persists
                // Frame 100: Temp jump to 61.0°C (+1.0°C) -> Persists
                // Frame 200: Pump toggled ON -> Persists
                // Frame 300: Pump toggled OFF -> Persists
                // Frame 400: Temp jump to 62.0°C (+1.0°C) -> Persists
                // All other frames: Steady state -> Throttled!
                var temp = 60.0m;
                if (frame >= 400) temp = 62.0m;
                else if (frame >= 100) temp = 61.0m;

                var pump = frame >= 200 && frame < 300;

                var metrics = new BrewLinkCanonicalMetrics(
                    CurrentTempC: temp,
                    TargetTempC: 65.0m,
                    BoilTempC: 100.0m,
                    HeatActive: true,
                    HeatPowerPct: 100m,
                    RampActive: false,
                    PumpActive: pump,
                    AutoMode: true,
                    Timer: null,
                    Stage: null,
                    Interaction: null,
                    Mac: null,
                    Rssi: null,
                    UpdatedAt: frameTime
                );

                if (throttler.ShouldPersist(gwId, frameTime, temp, metrics))
                {
                    localPersisted++;
                }
            }

            persistCounts[gwId] = localPersisted;
        });

        await Task.WhenAll(tasks);

        // Verify every single gateway had exactly 5 persisted readings
        // (frame 0 initial, frame 100 temp jump, frame 200 pump on, frame 300 pump off, frame 400 temp jump)
        foreach (var gwId in gatewayIds)
        {
            persistCounts.Should().ContainKey(gwId);
            persistCounts[gwId].Should().Be(5, $"Gateway {gwId} should have exactly 5 persisted frames, verifying isolation");
        }
    }

    #endregion

    #region 5. Parser Throughput & Memory Stability (100,000 Frames)

    [Fact]
    public void ParserAndThrottler_100000Frames_ExecutesUnderTightPerformanceBudgetWithoutLeak()
    {
        var parser = new BrewLinkTelemetryParser();
        var throttler = new TelemetryThrottlingService();
        var equipmentId = Guid.NewGuid();
        var equipment = new Equipment
        {
            Id = equipmentId,
            UserId = "user-perf",
            Name = "Perf Kettle",
            Type = EquipmentType.Boiler,
            Subtype = EquipmentSubtype.AllInOne,
            ConnectionType = EquipmentConnectionType.BrewLink,
            ConnectionConfigJson = """{"targetMac":"C4:4F:33:1A:8B:22","slot":1}"""
        };

        const string sampleJson = """
        {
            "event": "telemetry",
            "mac": "C4:4F:33:1A:8B:22",
            "state": {
                "temperature": {
                    "current_c": 65.5,
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
                    "remaining_seconds": 2400,
                    "total_seconds": 3600
                },
                "stage": {
                    "step_index": 2,
                    "step_name": "Saccharification"
                },
                "interaction": {
                    "required": false,
                    "code": 0
                },
                "rssi": -65
            },
            "timestamp": "2026-11-07T20:00:00Z"
        }
        """;

        using var doc = JsonDocument.Parse(sampleJson);
        var root = doc.RootElement;

        // Warm up JIT
        for (var i = 0; i < 1000; i++)
        {
            var parsed = parser.Parse(root, equipment);
            var warmupCanonical = BrewLinkTelemetryParser.ParseCanonical(root, equipment);
            _ = throttler.ShouldPersist(equipmentId, DateTime.UtcNow, parsed!.TemperatureC, warmupCanonical);
        }

        throttler.Reset(equipmentId);

        // Benchmark 100,000 iterations
        const int iterations = 100_000;
        var sw = Stopwatch.StartNew();

        var t0 = DateTime.UtcNow;
        var persisted = 0;

        for (var i = 0; i < iterations; i++)
        {
            var canonical = BrewLinkTelemetryParser.ParseCanonical(root, equipment);
            if (canonical != null && throttler.ShouldPersist(equipmentId, t0.AddMilliseconds(i * 10), canonical.CurrentTempC!.Value, canonical))
            {
                persisted++;
            }
        }

        sw.Stop();

        // Throughput must exceed 20,000 ops/second (100k frames under 5000ms)
        sw.ElapsedMilliseconds.Should().BeLessThan(5000,
            $"100,000 parse & throttle operations took {sw.ElapsedMilliseconds}ms (budget < 5000ms)");

        persisted.Should().Be(1, "100k steady frames within 1000s with same timestamp pace should only persist at expected cadence");
    }

    #endregion

    #region 6. Parser Adversarial Edge Cases & Malformed Inputs

    private static Equipment CreateMockEquipment(string name = "Test Kettle") => new()
    {
        Id = Guid.NewGuid(),
        UserId = "user-test",
        Name = name,
        Type = EquipmentType.Boiler,
        Subtype = EquipmentSubtype.AllInOne,
        ConnectionType = EquipmentConnectionType.BrewLink
    };

    [Fact]
    public void Parser_EmptyObject_ReturnsNullGracefullyWithoutThrowing()
    {
        var parser = new BrewLinkTelemetryParser();
        using var doc = JsonDocument.Parse("{}");

        var result = parser.Parse(doc.RootElement, CreateMockEquipment());
        result.Should().BeNull();
    }

    [Fact]
    public void Parser_MalformedValues_HandledGracefully()
    {
        var parser = new BrewLinkTelemetryParser();
        using var doc = JsonDocument.Parse("""
        {
            "mac": "not-a-valid-mac",
            "state": {
                "temperature": {
                    "current_c": "not-a-number",
                    "target_c": null
                },
                "heating": {
                    "power_pct": "one-hundred"
                },
                "timer": null,
                "stage": "not-an-object",
                "interaction": 12345
            },
            "rssi": "very-strong",
            "timestamp": "not-a-date"
        }
        """);

        var result = parser.Parse(doc.RootElement, CreateMockEquipment());
        result.Should().BeNull("payload with invalid non-numeric current_c should return null without crashing");
    }

    [Fact]
    public void Parser_StringNumbers_AreParsedCorrectly()
    {
        var parser = new BrewLinkTelemetryParser();
        using var doc = JsonDocument.Parse("""
        {
            "mac": "c44f331a8b22",
            "state": {
                "current_temp_c": "66.5",
                "target_temp_c": "68.0",
                "heat_power_pct": "75.5",
                "timer_remaining_sec": "1800",
                "step_index": "3"
            }
        }
        """);

        var result = parser.Parse(doc.RootElement, CreateMockEquipment());
        result.Should().NotBeNull();
        result!.TemperatureC.Should().Be(66.5m);

        var canonical = BrewLinkTelemetryParser.FromMetricsJson(result.MetricsJson);
        canonical.Should().NotBeNull();
        canonical!.TargetTempC.Should().Be(68.0m);
        canonical.HeatPowerPct.Should().Be(75.5m);
        canonical.Timer?.RemainingSeconds.Should().Be(1800);
        canonical.Stage?.StepIndex.Should().Be(3);
        canonical.Mac.Should().Be("C4:4F:33:1A:8B:22");
    }

    [Fact]
    public void Parser_FahrenheitUnitConversion_IsAccurate()
    {
        var parser = new BrewLinkTelemetryParser();
        using var doc = JsonDocument.Parse("""
        {
            "mac": "C4:4F:33:1A:8B:22",
            "state": {
                "temperature": {
                    "current": 149.0,
                    "target": 154.4,
                    "unit": "F"
                }
            }
        }
        """);

        var result = parser.Parse(doc.RootElement, CreateMockEquipment());
        result.Should().NotBeNull();
        // 149°F = (149 - 32) * 5/9 = 65.0°C
        result!.TemperatureC.Should().Be(65.0m);

        var canonical = BrewLinkTelemetryParser.FromMetricsJson(result.MetricsJson);
        canonical.Should().NotBeNull();
        // 154.4°F = (154.4 - 32) * 5/9 = 68.0°C
        canonical!.TargetTempC.Should().Be(68.0m);
    }

    [Fact]
    public void Parser_RestGatewayPayload_SelectsMatchingDeviceByMacOrSlot()
    {
        var equipment = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            Name = "Boiler 2",
            Type = EquipmentType.Boiler,
            Subtype = EquipmentSubtype.AllInOne,
            ConnectionType = EquipmentConnectionType.BrewLink,
            ConnectionConfigJson = """{"targetMac":"AA:BB:CC:DD:EE:02","slot":2}"""
        };

        using var doc = JsonDocument.Parse("""
        {
            "gateway_id": "GW01",
            "count": 2,
            "equipment": [
                {
                    "slot": 1,
                    "mac": "AA:BB:CC:DD:EE:01",
                    "state": { "current_temp_c": 50.0 }
                },
                {
                    "slot": 2,
                    "mac": "AA:BB:CC:DD:EE:02",
                    "state": { "current_temp_c": 65.0 }
                }
            ]
        }
        """);

        var canonical = BrewLinkTelemetryParser.ParseCanonical(doc.RootElement, equipment);
        canonical.Should().NotBeNull();
        canonical!.CurrentTempC.Should().Be(65.0m);
        canonical.Mac.Should().Be("AA:BB:CC:DD:EE:02");
    }

    #endregion

    #region 7. Full Integration E2E HTTP Stream Ingestion Throttling

    [Fact]
    public async Task EndToEnd_TelemetryEndpoint_RapidBurst50Frames_OnlyPersistsInitialReading()
    {
        var client = _factory.CreateClient();

        // 1. Setup user & equipment with BrewLink connection
        var reg = new RegisterRequest($"stress_{Guid.NewGuid():N}@test.local", "Password123!", "Stress Tester", "en", VolumeUnit.Liters);
        var authRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        authRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var authEnvelope = await authRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        var token = authEnvelope!.Data!.AccessToken;

        var userClient = _factory.CreateClient();
        userClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var gwId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()
                .CreateProtector("BrewYou.BrewLinkCredentials");
            db.BrewLinkGateways.Add(new BrewLinkGateway
            {
                Id = gwId,
                UserId = authEnvelope.Data.User.Id,
                Name = "Test Gateway",
                Host = "192.168.1.100",
                GatewayId = "C44F331A8B20",
                ApiToken = protector.Protect("token"),
                MqttTopicKey = protector.Protect("key")
            });
            await db.SaveChangesAsync();
        }

        var setupsRes = await userClient.GetAsync("/api/v1/brewery-setups");
        var setupsEnv = await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>();
        var setupId = setupsEnv!.Data!.First(s => s.IsDefault).Id;

        var eqRes = await userClient.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: $"Stress Boiler {Guid.NewGuid():N}",
            Type: EquipmentType.Boiler,
            Capacity: 35,
            Subtype: EquipmentSubtype.AllInOne,
            ConnectionType: EquipmentConnectionType.BrewLink,
            ConnectionConfigJson: $"{{\"brewLinkGatewayId\":\"{gwId}\",\"targetMac\":\"C4:4F:33:1A:8B:22\",\"slot\":1}}"
        ));
        eqRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var eq = (await eqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // Directly assign connection token
        var deviceToken = "test-token-" + Guid.NewGuid().ToString("N");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var entity = await db.Equipment.FindAsync(eq.Id);
            entity!.ConnectionToken = deviceToken;
            await db.SaveChangesAsync();
        }

        var throttler = _factory.Services.GetRequiredService<ITelemetryThrottlingService>();
        throttler.Reset(eq.Id);

        var publicClient = _factory.CreateClient();

        // 2. Fire 50 rapid requests in parallel
        var payload = new
        {
            current_temp_c = 65.0,
            target_temp_c = 65.0,
            heat_active = true,
            heat_power_pct = 100,
            pump_active = true
        };

        var postTasks = Enumerable.Range(0, 50).Select(async _ =>
        {
            var res = await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{deviceToken}", payload);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        });

        await Task.WhenAll(postTasks);

        // 3. Verify exactly 1 reading was persisted in the DB
        var readingsRes = await userClient.GetAsync($"/api/v1/inventory/equipment/{eq.Id}/readings");
        readingsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var readings = (await readingsRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentReadingDto>>>())!.Data!;
        readings.Should().HaveCount(1, "all 50 rapid identical frames must be throttled to 1 database write");

        // 4. Send frame with +0.5°C jump -> must immediately persist!
        var jumpRes = await publicClient.PostAsJsonAsync($"/api/v1/telemetry/equipment/{deviceToken}", new
        {
            current_temp_c = 65.5,
            target_temp_c = 65.0,
            heat_active = true,
            heat_power_pct = 100,
            pump_active = true
        });
        jumpRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var readingsAfterJump = (await (await userClient.GetAsync($"/api/v1/inventory/equipment/{eq.Id}/readings"))
            .Content.ReadFromJsonAsync<ApiResponse<List<EquipmentReadingDto>>>())!.Data!;
        readingsAfterJump.Should().HaveCount(2, "+0.5°C jump must trigger immediate persistence");
    }

    #endregion
}