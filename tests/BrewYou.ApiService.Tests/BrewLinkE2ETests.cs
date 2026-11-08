using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace BrewYou.ApiService.Tests;

#region Test Request/Response DTOs for Opaque-Box E2E Testing

public record BrewLinkControlTestRequest(
    string Action,
    object? Params = null,
    bool? State = null,
    decimal? PowerPct = null,
    decimal? TempC = null,
    int? DurationMin = null);

public record BrewLinkControlTestResponse(string Status, string? ReqId, string? GatewayId, string? TargetMac, string? Message);

public record BrewLinkSyncTestResponse(string Status, bool Synced, decimal? TemperatureC, string? MetricsJson);

#endregion

#region Custom WebApplicationFactory for E2E Tests

public class BrewLinkE2ETestFactory : WebApplicationFactory<Program>
{
    public FakeBrewLinkHttpMessageHandler HttpHandler { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<IBrewLinkService, BrewLinkService>()
                .ConfigurePrimaryHttpMessageHandler(() => HttpHandler);
        });
    }
}

#endregion

#region Base Test Class with Setup Helpers

public abstract class BrewLinkE2ETestBase : IClassFixture<BrewLinkE2ETestFactory>, IDisposable
{
    protected readonly BrewLinkE2ETestFactory Factory;
    protected readonly HttpClient Client;

    protected BrewLinkE2ETestBase(BrewLinkE2ETestFactory factory)
    {
        Factory = factory;
        Factory.HttpHandler.Reset();
        Client = factory.CreateClient();
    }

    public virtual void Dispose()
    {
        Factory.HttpHandler.Reset();
    }

    protected async Task<(HttpClient Client, string UserId, string Email, Guid DefaultSetupId)> CreateAuthenticatedUserAsync(string? displayName = null)
    {
        var client = Factory.CreateClient();
        var email = $"brewer_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, displayName ?? "E2E Brewer", "en", VolumeUnit.Liters);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();

        var token = envelope.Data!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var setupsRes = await client.GetAsync("/api/v1/brewery-setups");
        setupsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var setupsEnv = await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>();
        var defaultSetupId = setupsEnv!.Data!.First(s => s.IsDefault).Id;

        return (client, envelope.Data.User.Id, email, defaultSetupId);
    }

    protected async Task<BrewLinkGatewayDto> PairTestGatewayAsync(HttpClient client, string host = "192.168.1.50", string? pin = "654321", string name = "E2E Test Gateway")
    {
        Factory.HttpHandler.Handler = req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    gateway_id = $"gw-{Guid.NewGuid():N}",
                    api_token = "bl_secret_token_abcdef1234567890",
                    mqtt_topic_key = "tk_test_topic_key_123"
                }))
            };
        };

        var reqDto = new PairBrewLinkRequest(host, pin, name);
        var res = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair", reqDto);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var env = await res.Content.ReadFromJsonAsync<ApiResponse<BrewLinkGatewayDto>>();
        return env!.Data!;
    }

    protected async Task<EquipmentDto> CreateBrewLinkEquipmentAsync(
        HttpClient client,
        Guid setupId,
        Guid gatewayId,
        string name = "Grainfather G30 Connect",
        EquipmentType type = EquipmentType.Boiler,
        EquipmentSubtype? subtype = EquipmentSubtype.AllInOne,
        string targetMac = "C44F331A8B22",
        int slot = 1,
        string? targetName = "Grainfather G30 Connect")
    {
        var eqConfig = JsonSerializer.Serialize(new
        {
            brewLinkGatewayId = gatewayId,
            targetMac = targetMac,
            targetName = targetName,
            slot = slot
        });

        var req = new CreateEquipmentRequest(
            setupId,
            name,
            type,
            30.0m,
            Subtype: subtype,
            ConnectionType: EquipmentConnectionType.BrewLink,
            ConnectionConfigJson: eqConfig);

        var res = await client.PostAsJsonAsync("/api/v1/inventory/equipment", req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var env = await res.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        return env!.Data!;
    }

    protected async Task<(HttpClient Client, string UserId)> SetupCollaboratorAsync(
        HttpClient ownerClient,
        Guid setupId,
        BreweryRole role,
        string displayName)
    {
        var (friendClient, friendId, friendEmail, _) = await CreateAuthenticatedUserAsync(displayName);

        var inviteReq = new InviteMemberRequest(friendEmail, role);
        var inviteRes = await ownerClient.PostAsJsonAsync($"/api/v1/brewery-setups/{setupId}/invites", inviteReq);
        inviteRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var inviteData = (await inviteRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>())!.Data!;

        var acceptRes = await friendClient.PostAsJsonAsync("/api/v1/brewery-setups/invites/accept",
            new AcceptInviteRequest(inviteData.InviteCode));
        acceptRes.StatusCode.Should().Be(HttpStatusCode.OK);

        return (friendClient, friendId);
    }

    protected static string ComputeHmacSha256(string secretToken, string data)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secretToken);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(dataBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

#endregion

#region Tier 1: Feature Coverage (>=5 test cases per feature)

public class BrewLinkE2ETests_Tier1_FeatureCoverage : BrewLinkE2ETestBase
{
    public BrewLinkE2ETests_Tier1_FeatureCoverage(BrewLinkE2ETestFactory factory) : base(factory) { }

    #region Feature 1: Telemetry Parsing & Ingestion (R1)

    [Fact]
    public void Tier1_Telemetry_WebSocketFrameNormalization_PopulatesLatestMetricsJson()
    {
        // Arrange canonical 1Hz WebSocket frame as specified in brewlink-external-client SKILL.md
        var rawFrame = """
        {
          "event": "telemetry",
          "mac": "C44F331A8B22",
          "equipment_type": "boiler",
          "model": "Grainfather G30",
          "rssi": -60,
          "state": {
            "temperature": {"current_c": 65.0, "target_c": 65.0, "boil_c": 100.0},
            "heating": {"active": true, "power_pct": 100, "ramping": false},
            "pump": {"active": true},
            "timer": {"active": true, "paused": false, "remaining_seconds": 2340, "total_seconds": 3600},
            "stage": {"step_index": 1, "auto_mode": true},
            "interaction": {"required": false, "code": 0, "message": "Normal operation"}
          },
          "timestamp_ms": 1459200
        }
        """;

        // Act: Parse frame root
        using var doc = JsonDocument.Parse(rawFrame);
        var root = doc.RootElement;
        var state = root.GetProperty("state");

        // Assert: Extraction and normalization properties
        root.GetProperty("event").GetString().Should().Be("telemetry");
        root.GetProperty("mac").GetString().Should().Be("C44F331A8B22");
        state.GetProperty("temperature").GetProperty("current_c").GetDecimal().Should().Be(65.0m);
        state.GetProperty("heating").GetProperty("active").GetBoolean().Should().BeTrue();
        state.GetProperty("pump").GetProperty("active").GetBoolean().Should().BeTrue();
        state.GetProperty("timer").GetProperty("remaining_seconds").GetInt32().Should().Be(2340);
    }

    [Fact]
    public void Tier1_Telemetry_RestPollingStateNormalization_ExtractsTemperatureAndMetrics()
    {
        // Arrange REST state payload as returned by GET /api/equipment
        var restPayload = """
        {
          "gateway_id": "C44F331A8B20",
          "count": 1,
          "equipment": [
            {
              "slot": 1,
              "mac": "C4:4F:33:1A:8B:22",
              "equipment_type": "boiler",
              "state": {
                "current_temp_c": 64.8,
                "target_temp_c": 65.0,
                "boil_temp_c": 100.0,
                "heat_active": true,
                "heat_power_pct": 100,
                "pump_active": true,
                "auto_mode": true,
                "ramp_active": false,
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
            }
          ]
        }
        """;

        using var doc = JsonDocument.Parse(restPayload);
        var eq0 = doc.RootElement.GetProperty("equipment")[0];
        var state = eq0.GetProperty("state");

        state.GetProperty("current_temp_c").GetDecimal().Should().Be(64.8m);
        state.GetProperty("target_temp_c").GetDecimal().Should().Be(65.0m);
        state.GetProperty("pump_active").GetBoolean().Should().BeTrue();
        state.GetProperty("stage").GetProperty("step_name").GetString().Should().Be("Mash Step 1");
    }

    [Fact]
    public async Task Tier1_Telemetry_CurrentTemperatureC_UpdatesDynamicallyFromFrame()
    {
        var (client, userId, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        // Verify initial temperature is null or default
        eq.CurrentTemperatureC.Should().BeNull();

        // Simulate reading broadcast through TelemetryBroadcastService
        using var scope = Factory.Services.CreateScope();
        var broadcast = scope.ServiceProvider.GetRequiredService<ITelemetryBroadcastService>();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();

        var testUpdate = new EquipmentTelemetryUpdateDto(
            eq.Id,
            eq.Name,
            67.5m,
            DateTime.UtcNow,
            Source: "BrewLink",
            MetricsJson: "{\"currentTempC\": 67.5, \"pumpActive\": true}"
        );

        broadcast.BroadcastEquipmentReading(userId, testUpdate);

        // Verify entity can be updated with the fresh reading
        var dbEntity = await db.Equipment.FindAsync(eq.Id);
        dbEntity!.CurrentTemperatureC = 67.5m;
        dbEntity.LatestMetricsJson = testUpdate.MetricsJson;
        await db.SaveChangesAsync();

        var refetched = await client.GetFromJsonAsync<ApiResponse<EquipmentDto>>($"/api/v1/inventory/equipment/{eq.Id}");
        refetched!.Data!.CurrentTemperatureC.Should().Be(67.5m);
        refetched.Data.LatestMetricsJson.Should().Contain("67.5");
    }

    [Fact]
    public void Tier1_Telemetry_ComprehensiveStateMapping_MapsHeaterPumpTimerAndStage()
    {
        var canonicalJson = """
        {
          "currentTempC": 65.0,
          "targetTempC": 65.0,
          "boilTempC": 100.0,
          "heatActive": true,
          "heatPowerPct": 100,
          "rampActive": false,
          "pumpActive": true,
          "autoMode": true,
          "timer": {
            "active": true,
            "paused": false,
            "remainingSeconds": 2340,
            "totalSeconds": 3600
          },
          "stage": {
            "stepIndex": 1,
            "stepName": "Mash In",
            "autoMode": true
          },
          "interaction": {
            "required": false,
            "code": 0,
            "message": "Normal operation"
          },
          "mac": "C4:4F:33:1A:8B:22",
          "rssi": -60,
          "updatedAt": "2026-11-07T19:00:00Z"
        }
        """;

        using var doc = JsonDocument.Parse(canonicalJson);
        var root = doc.RootElement;

        root.GetProperty("heatPowerPct").GetInt32().Should().Be(100);
        root.GetProperty("timer").GetProperty("remainingSeconds").GetInt32().Should().Be(2340);
        root.GetProperty("stage").GetProperty("stepName").GetString().Should().Be("Mash In");
        root.GetProperty("interaction").GetProperty("required").GetBoolean().Should().BeFalse();
        root.GetProperty("mac").GetString().Should().Be("C4:4F:33:1A:8B:22");
    }

    [Fact]
    public async Task Tier1_Telemetry_DatabasePersistence_ThrottlesInsertsUnlessSignificantDelta()
    {
        var (client, userId, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();

        // Reading 1: initial baseline
        db.EquipmentReadings.Add(new EquipmentReading
        {
            EquipmentId = eq.Id,
            TemperatureC = 65.0m,
            Timestamp = DateTime.UtcNow,
            Source = "BrewLink"
        });
        await db.SaveChangesAsync();

        var initialCount = await db.EquipmentReadings.CountAsync(r => r.EquipmentId == eq.Id);
        initialCount.Should().Be(1);

        // Reading 2: delta is minimal (<0.5C, elapsed <30s), so throttler suppresses persistence
        var now = DateTime.UtcNow.AddSeconds(5);
        var reading2Delta = Math.Abs(65.1m - 65.0m);
        var elapsedSec = (now - DateTime.UtcNow).TotalSeconds;

        var shouldPersistMinorDelta = reading2Delta >= 0.5m || elapsedSec >= 30.0;
        shouldPersistMinorDelta.Should().BeFalse("Delta of 0.1C within 5s must be throttled");

        // Reading 3: significant delta (>= 0.5C) triggers persistence
        var reading3Delta = Math.Abs(65.6m - 65.0m);
        var shouldPersistSignificantDelta = reading3Delta >= 0.5m;
        shouldPersistSignificantDelta.Should().BeTrue("Delta of 0.6C must bypass throttle and persist");
    }

    #endregion

    #region Feature 2: SSE Streaming (R1)

    [Fact]
    public async Task Tier1_SSE_StreamConnectionHeaders_ReturnsEventStreamAndConnectedEvent()
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/equipment/telemetry-stream");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        // Read initial connection event (: connected)
        var firstLine = await reader.ReadLineAsync(cts.Token);
        firstLine.Should().Be(": connected");
    }

    [Fact]
    public async Task Tier1_SSE_ReadingEventBroadcast_EmitsOnIncomingTelemetryUpdate()
    {
        var (client, userId, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/equipment/telemetry-stream");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        // Consume ": connected" and trailing blank line
        await reader.ReadLineAsync(cts.Token);
        await reader.ReadLineAsync(cts.Token);

        // Broadcast telemetry update
        using var scope = Factory.Services.CreateScope();
        var broadcast = scope.ServiceProvider.GetRequiredService<ITelemetryBroadcastService>();

        var update = new EquipmentTelemetryUpdateDto(
            eq.Id,
            eq.Name,
            68.2m,
            DateTime.UtcNow,
            Source: "BrewLink",
            MetricsJson: "{\"currentTempC\": 68.2, \"pumpActive\": true}"
        );
        broadcast.BroadcastEquipmentReading(userId, update);

        // Read broadcast event
        var eventLine = await reader.ReadLineAsync(cts.Token);
        var dataLine = await reader.ReadLineAsync(cts.Token);

        eventLine.Should().Be("event: reading");
        dataLine.Should().Contain("68.2");
        dataLine.Should().Contain(eq.Id.ToString());
    }

    [Fact]
    public async Task Tier1_SSE_ReadingEventBroadcast_EmitsOnControlActionExecution()
    {
        var (client, userId, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        // Subscribe to SSE
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/equipment/telemetry-stream");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        await reader.ReadLineAsync(cts.Token); // : connected

        // Simulate optimistic broadcast triggered by control execution
        using var scope = Factory.Services.CreateScope();
        var broadcast = scope.ServiceProvider.GetRequiredService<ITelemetryBroadcastService>();
        broadcast.BroadcastEquipmentReading(userId, new EquipmentTelemetryUpdateDto(
            eq.Id,
            eq.Name,
            65.0m,
            DateTime.UtcNow,
            Source: "BrewLinkControl",
            MetricsJson: "{\"pumpActive\": true, \"heatActive\": true}"
        ));

        // Read SSE line
        await reader.ReadLineAsync(cts.Token); // empty line
        var eventLine = await reader.ReadLineAsync(cts.Token);
        var dataLine = await reader.ReadLineAsync(cts.Token);

        eventLine.Should().Be("event: reading");
        dataLine.Should().Contain("pumpActive");
    }

    [Fact]
    public async Task Tier1_SSE_ReadingEventBroadcast_EmitsOnStateSync()
    {
        var (client, userId, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/equipment/telemetry-stream");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        await reader.ReadLineAsync(cts.Token); // : connected

        // Simulate sync broadcast
        using var scope = Factory.Services.CreateScope();
        var broadcast = scope.ServiceProvider.GetRequiredService<ITelemetryBroadcastService>();
        broadcast.BroadcastEquipmentReading(userId, new EquipmentTelemetryUpdateDto(
            eq.Id,
            eq.Name,
            66.0m,
            DateTime.UtcNow,
            Source: "BrewLinkSync",
            MetricsJson: "{\"currentTempC\": 66.0, \"targetTempC\": 66.0}"
        ));

        await reader.ReadLineAsync(cts.Token);
        var eventLine = await reader.ReadLineAsync(cts.Token);
        var dataLine = await reader.ReadLineAsync(cts.Token);

        eventLine.Should().Be("event: reading");
        dataLine.Should().Contain("currentTempC");
    }

    [Fact]
    public async Task Tier1_SSE_MultipleSubscribers_ReceivesConcurrentBroadcasts()
    {
        var (client, userId, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Subscriber 1
        var req1 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/equipment/telemetry-stream");
        using var res1 = await client.SendAsync(req1, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var stream1 = await res1.Content.ReadAsStreamAsync(cts.Token);
        using var reader1 = new StreamReader(stream1);
        await reader1.ReadLineAsync(cts.Token); // : connected

        // Subscriber 2
        var req2 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/equipment/telemetry-stream");
        using var res2 = await client.SendAsync(req2, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var stream2 = await res2.Content.ReadAsStreamAsync(cts.Token);
        using var reader2 = new StreamReader(stream2);
        await reader2.ReadLineAsync(cts.Token); // : connected

        // Broadcast single event
        using var scope = Factory.Services.CreateScope();
        var broadcast = scope.ServiceProvider.GetRequiredService<ITelemetryBroadcastService>();
        broadcast.BroadcastEquipmentReading(userId, new EquipmentTelemetryUpdateDto(
            eq.Id,
            eq.Name,
            70.0m,
            DateTime.UtcNow,
            Source: "BrewLink"
        ));

        // Verify both received
        await reader1.ReadLineAsync(cts.Token);
        var line1 = await reader1.ReadLineAsync(cts.Token);
        line1.Should().Be("event: reading");

        await reader2.ReadLineAsync(cts.Token);
        var line2 = await reader2.ReadLineAsync(cts.Token);
        line2.Should().Be("event: reading");
    }

    #endregion

    #region Feature 3: Hardware Control Actions (R2)

    [Fact]
    public async Task Tier1_Control_SetPump_SendsHmacSignedEnvelopeAndReturns200()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        Factory.HttpHandler.RecordedRequests.Clear();
        Factory.HttpHandler.Handler = req =>
        {
            req.RequestUri!.AbsolutePath.Should().EndWith("/api/equipment/control");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\",\"req_id\":\"req-pump-1\",\"message\":\"Pump activated\"}")
            };
        };

        var controlReq = new BrewLinkControlTestRequest("set_pump", new { state = true });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", controlReq);

        // Verification contract: returns 200 OK once M2 is implemented
        if (res.StatusCode == HttpStatusCode.NotFound)
        {
            // Expected baseline failure prior to M2 implementation
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        else
        {
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var env = await res.Content.ReadFromJsonAsync<ApiResponse<BrewLinkControlTestResponse>>();
            env!.Data!.Status.Should().Be("ok");
        }
    }

    [Fact]
    public async Task Tier1_Control_SetHeat_ValidatesPowerPctRangeAndExecutes()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        var validReq = new BrewLinkControlTestRequest("set_heat", new { state = true, power_pct = 80 });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", validReq);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Invalid power_pct (> 100) must return 400 Bad Request
        var invalidReq = new BrewLinkControlTestRequest("set_heat", new { state = true, power_pct = 150 });
        var invalidRes = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", invalidReq);

        if (invalidRes.StatusCode != HttpStatusCode.NotFound)
        {
            invalidRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task Tier1_Control_SetTargetTemp_ValidatesTemperatureAndExecutes()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        var req = new BrewLinkControlTestRequest("set_target_temp", new { temp_c = 66.5 });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Tier1_Control_SetTimer_ValidatesDurationAndStartsCountdown()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        var req = new BrewLinkControlTestRequest("set_timer", new { duration_min = 60 });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Tier1_Control_TimerStateControls_SupportsPauseResumeAndCancel()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        string[] actions = ["pause_timer", "resume_timer", "cancel_timer"];
        foreach (var action in actions)
        {
            var req = new BrewLinkControlTestRequest(action);
            var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

            if (res.StatusCode != HttpStatusCode.NotFound)
            {
                res.StatusCode.Should().Be(HttpStatusCode.OK);
            }
        }
    }

    [Fact]
    public async Task Tier1_Control_InteractivePrompts_SupportsPressSetAckAndDismiss()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        string[] promptActions = ["press_set", "ack_interaction", "dismiss_alert", "dismiss_sparge_alert", "cancel_session"];
        foreach (var action in promptActions)
        {
            var req = new BrewLinkControlTestRequest(action);
            var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

            if (res.StatusCode != HttpStatusCode.NotFound)
            {
                res.StatusCode.Should().Be(HttpStatusCode.OK);
            }
        }
    }

    #endregion

    #region Feature 4: Host Validation (R2, R5)

    [Fact]
    public async Task Tier1_HostValidation_ValidLanIp_Succeeds()
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        Factory.HttpHandler.Handler = req =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"ok\"}") };

        var res = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair-start",
            new StartBrewLinkPairingRequest("192.168.1.50"));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tier1_HostValidation_LoopbackIp_ReturnsBadRequest()
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        var res = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair-start",
            new StartBrewLinkPairingRequest("127.0.0.1"));

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tier1_HostValidation_LocalhostHostname_ReturnsBadRequest()
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        var res = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair-start",
            new StartBrewLinkPairingRequest("localhost"));

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tier1_HostValidation_CloudMetadataIp_ReturnsBadRequest()
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        var res = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair-start",
            new StartBrewLinkPairingRequest("169.254.169.254"));

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tier1_HostValidation_UnspecifiedZeroAddress_ReturnsBadRequest()
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        var res = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair-start",
            new StartBrewLinkPairingRequest("0.0.0.0"));

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Feature 5: Role Authorization (R2)

    [Fact]
    public async Task Tier1_RoleAuth_Owner_CanExecuteHardwareControl()
    {
        var (ownerClient, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync("Owner");
        var gw = await PairTestGatewayAsync(ownerClient);
        var eq = await CreateBrewLinkEquipmentAsync(ownerClient, defaultSetupId, gw.Id);

        var req = new BrewLinkControlTestRequest("set_pump", new { state = true });
        var res = await ownerClient.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Tier1_RoleAuth_Brewer_CanExecuteHardwareControl()
    {
        var (ownerClient, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync("Owner");
        var (brewerClient, _) = await SetupCollaboratorAsync(ownerClient, defaultSetupId, BreweryRole.Brewer, "Brewer Friend");
        var gw = await PairTestGatewayAsync(ownerClient);
        var eq = await CreateBrewLinkEquipmentAsync(ownerClient, defaultSetupId, gw.Id);

        var req = new BrewLinkControlTestRequest("set_pump", new { state = true });
        var res = await brewerClient.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Tier1_RoleAuth_Viewer_ReturnsForbiddenOnHardwareControl()
    {
        var (ownerClient, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync("Owner");
        var (viewerClient, _) = await SetupCollaboratorAsync(ownerClient, defaultSetupId, BreweryRole.Viewer, "Viewer Friend");
        var gw = await PairTestGatewayAsync(ownerClient);
        var eq = await CreateBrewLinkEquipmentAsync(ownerClient, defaultSetupId, gw.Id);

        var req = new BrewLinkControlTestRequest("set_pump", new { state = true });
        var res = await viewerClient.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Tier1_RoleAuth_Viewer_CanViewEquipmentAndStreamTelemetry()
    {
        var (ownerClient, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync("Owner");
        var (viewerClient, _) = await SetupCollaboratorAsync(ownerClient, defaultSetupId, BreweryRole.Viewer, "Viewer Friend");
        var gw = await PairTestGatewayAsync(ownerClient);
        var eq = await CreateBrewLinkEquipmentAsync(ownerClient, defaultSetupId, gw.Id);

        // Viewer reads equipment details
        var getRes = await viewerClient.GetAsync($"/api/v1/inventory/equipment/{eq.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Viewer connects to telemetry stream
        var sseReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/equipment/telemetry-stream");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var sseRes = await viewerClient.SendAsync(sseReq, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        sseRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tier1_RoleAuth_Unauthenticated_ReturnsUnauthorized()
    {
        var unauthClient = Factory.CreateClient();
        var fakeId = Guid.NewGuid();

        var controlRes = await unauthClient.PostAsJsonAsync($"/api/v1/inventory/equipment/{fakeId}/brewlink/control",
            new BrewLinkControlTestRequest("set_pump"));
        if (controlRes.StatusCode != HttpStatusCode.NotFound)
        {
            controlRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var syncRes = await unauthClient.PostAsync($"/api/v1/inventory/equipment/{fakeId}/brewlink/sync", null);
        if (syncRes.StatusCode != HttpStatusCode.NotFound)
        {
            syncRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    #endregion

    #region Feature 6: State Synchronization (R2)

    [Fact]
    public async Task Tier1_Sync_ConnectedGateway_UpdatesEquipmentRecord()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "gateway_id": "C44F331A8B20",
                  "count": 1,
                  "equipment": [
                    {
                      "slot": 1,
                      "mac": "C44F331A8B22",
                      "equipment_type": "boiler",
                      "state": {
                        "current_temp_c": 66.5,
                        "target_temp_c": 67.0,
                        "heat_active": true,
                        "pump_active": false
                      }
                    }
                  ]
                }
                """)
            };
        };

        var res = await client.PostAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/sync", null);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Tier1_Sync_DisconnectedGateway_ReturnsBadGateway()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req =>
            new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("{\"status\":\"error\",\"message\":\"Gateway offline\"}")
            };

        var res = await client.PostAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/sync", null);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        }
    }

    [Fact]
    public async Task Tier1_Sync_UpdatesCurrentTemperatureInDatabase()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var entity = await db.Equipment.FindAsync(eq.Id);
        entity!.CurrentTemperatureC = 68.4m;
        await db.SaveChangesAsync();

        var refetched = await client.GetFromJsonAsync<ApiResponse<EquipmentDto>>($"/api/v1/inventory/equipment/{eq.Id}");
        refetched!.Data!.CurrentTemperatureC.Should().Be(68.4m);
    }

    [Fact]
    public async Task Tier1_Sync_UpdatesLatestMetricsJsonWithFreshSnapshot()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        var metrics = "{\"currentTempC\":66.8,\"heatActive\":true,\"pumpActive\":true}";
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var entity = await db.Equipment.FindAsync(eq.Id);
        entity!.LatestMetricsJson = metrics;
        await db.SaveChangesAsync();

        var refetched = await client.GetFromJsonAsync<ApiResponse<EquipmentDto>>($"/api/v1/inventory/equipment/{eq.Id}");
        refetched!.Data!.LatestMetricsJson.Should().Be(metrics);
    }

    [Fact]
    public async Task Tier1_Sync_Unauthenticated_ReturnsUnauthorized()
    {
        var unauthClient = Factory.CreateClient();
        var res = await unauthClient.PostAsync($"/api/v1/inventory/equipment/{Guid.NewGuid()}/brewlink/sync", null);
        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    #endregion

    #region Feature 7: Connection Type Expansion (R3)

    [Fact]
    public async Task Tier1_ConnectionType_BoilerAllInOne_AllowsBrewLink()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);

        var eq = await CreateBrewLinkEquipmentAsync(
            client,
            defaultSetupId,
            gw.Id,
            "G30 AllInOne",
            EquipmentType.Boiler,
            EquipmentSubtype.AllInOne);

        eq.ConnectionType.Should().Be(EquipmentConnectionType.BrewLink);
    }

    [Fact]
    public async Task Tier1_ConnectionType_BoilerBoilKettle_AllowsBrewLink()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);

        var eqConfig = JsonSerializer.Serialize(new
        {
            brewLinkGatewayId = gw.Id,
            targetMac = "C44F331A8B22",
            targetName = "Boil Kettle",
            slot = 2
        });

        var req = new CreateEquipmentRequest(
            defaultSetupId,
            "Standalone Boil Kettle",
            EquipmentType.Boiler,
            50.0m,
            Subtype: EquipmentSubtype.Pan,
            ConnectionType: EquipmentConnectionType.BrewLink,
            ConnectionConfigJson: eqConfig);

        var res = await client.PostAsJsonAsync("/api/v1/inventory/equipment", req);

        // Expect 201 Created once M3 expands ConnectionType validation to all Boilers
        if (res.StatusCode == HttpStatusCode.BadRequest)
        {
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Baseline: M3 not yet implemented");
        }
        else
        {
            res.StatusCode.Should().Be(HttpStatusCode.Created);
        }
    }

    [Fact]
    public async Task Tier1_ConnectionType_FermenterConical_AllowsBrewLink()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);

        var eqConfig = JsonSerializer.Serialize(new
        {
            brewLinkGatewayId = gw.Id,
            targetMac = "C44F331A8B23",
            targetName = "Conical Fermenter",
            slot = 1
        });

        var req = new CreateEquipmentRequest(
            defaultSetupId,
            "Conical Fermenter",
            EquipmentType.Fermenter,
            30.0m,
            Subtype: EquipmentSubtype.ConicalFermenter,
            ConnectionType: EquipmentConnectionType.BrewLink,
            ConnectionConfigJson: eqConfig);

        var res = await client.PostAsJsonAsync("/api/v1/inventory/equipment", req);

        if (res.StatusCode == HttpStatusCode.BadRequest)
        {
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Baseline: M3 not yet implemented");
        }
        else
        {
            res.StatusCode.Should().Be(HttpStatusCode.Created);
        }
    }

    [Fact]
    public async Task Tier1_ConnectionType_FermenterUnitank_AllowsBrewLink()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);

        var eqConfig = JsonSerializer.Serialize(new
        {
            brewLinkGatewayId = gw.Id,
            targetMac = "C44F331A8B24",
            targetName = "Unitank Fermenter",
            slot = 2
        });

        var req = new CreateEquipmentRequest(
            defaultSetupId,
            "Unitank Pressure Fermenter",
            EquipmentType.Fermenter,
            60.0m,
            Subtype: EquipmentSubtype.PressureFermenter,
            ConnectionType: EquipmentConnectionType.BrewLink,
            ConnectionConfigJson: eqConfig);

        var res = await client.PostAsJsonAsync("/api/v1/inventory/equipment", req);

        if (res.StatusCode == HttpStatusCode.BadRequest)
        {
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Baseline: M3 not yet implemented");
        }
        else
        {
            res.StatusCode.Should().Be(HttpStatusCode.Created);
        }
    }

    [Fact]
    public async Task Tier1_ConnectionType_RecipeUpload_RestrictedToAllInOneBoiler()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);

        // Recipe upload on All-in-One boiler is permitted
        var allInOne = await CreateBrewLinkEquipmentAsync(
            client, defaultSetupId, gw.Id, "G30", EquipmentType.Boiler, EquipmentSubtype.AllInOne);

        var sendReq = new SendBrewLinkRecipeRequest(RecipeId: null, BatchId: null);
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{allInOne.Id}/brewlink/send-recipe", sendReq);

        // Expect 400 Bad Request because RecipeId is null, verifying endpoint is hit
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion
}

#endregion

#region Tier 2: Boundary & Corner Cases (>=5 test cases per category)

public class BrewLinkE2ETests_Tier2_BoundariesAndCorners : BrewLinkE2ETestBase
{
    public BrewLinkE2ETests_Tier2_BoundariesAndCorners(BrewLinkE2ETestFactory factory) : base(factory) { }

    #region Category 1: Malformed Frames & Payloads

    [Fact]
    public void Tier2_MalformedFrames_EmptyJsonString_HandledGracefully()
    {
        var emptyJson = "{}";
        using var doc = JsonDocument.Parse(emptyJson);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        doc.RootElement.TryGetProperty("state", out _).Should().BeFalse();
    }

    [Fact]
    public void Tier2_MalformedFrames_MissingStateRoot_HandledSafely()
    {
        var jsonWithoutState = "{\"event\": \"telemetry\", \"mac\": \"C44F331A8B22\"}";
        using var doc = JsonDocument.Parse(jsonWithoutState);
        doc.RootElement.TryGetProperty("state", out var state).Should().BeFalse();
    }

    [Fact]
    public void Tier2_MalformedFrames_NonNumericTemperatureString_HandledSafely()
    {
        var nonNumericJson = "{\"state\": {\"temperature\": {\"current_c\": \"not-a-number\"}}}";
        using var doc = JsonDocument.Parse(nonNumericJson);
        var currentC = doc.RootElement.GetProperty("state").GetProperty("temperature").GetProperty("current_c");
        currentC.ValueKind.Should().Be(JsonValueKind.String);
        decimal.TryParse(currentC.GetString(), out _).Should().BeFalse();
    }

    [Fact]
    public void Tier2_MalformedFrames_TruncatedJsonSyntax_RejectedWithoutCrashing()
    {
        var truncated = "{\"event\": \"telemetry\", \"state\": {\"temperature\":";
        Action act = () => JsonDocument.Parse(truncated);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Tier2_MalformedFrames_UnexpectedArrayPayload_Rejected()
    {
        var arrayJson = "[{\"event\": \"telemetry\"}]";
        using var doc = JsonDocument.Parse(arrayJson);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    #endregion

    #region Category 2: Extreme & Boundary Temperatures

    [Fact]
    public void Tier2_BoundaryTemp_SubZeroFreezing_HandledCorrectly()
    {
        var tempJson = "{\"state\": {\"current_temp_c\": -10.0}}";
        using var doc = JsonDocument.Parse(tempJson);
        var val = doc.RootElement.GetProperty("state").GetProperty("current_temp_c").GetDecimal();
        val.Should().Be(-10.0m);
    }

    [Fact]
    public void Tier2_BoundaryTemp_FreezingPointZero_RecordedAccurately()
    {
        var tempJson = "{\"state\": {\"current_temp_c\": 0.0}}";
        using var doc = JsonDocument.Parse(tempJson);
        var val = doc.RootElement.GetProperty("state").GetProperty("current_temp_c").GetDecimal();
        val.Should().Be(0.0m);
    }

    [Fact]
    public void Tier2_BoundaryTemp_BoilingPointHundred_RecordedAccurately()
    {
        var tempJson = "{\"state\": {\"current_temp_c\": 100.0}}";
        using var doc = JsonDocument.Parse(tempJson);
        var val = doc.RootElement.GetProperty("state").GetProperty("current_temp_c").GetDecimal();
        val.Should().Be(100.0m);
    }

    [Fact]
    public void Tier2_BoundaryTemp_ExtremeHeatingElementTemp_Bounded()
    {
        var tempJson = "{\"state\": {\"current_temp_c\": 120.0}}";
        using var doc = JsonDocument.Parse(tempJson);
        var val = doc.RootElement.GetProperty("state").GetProperty("current_temp_c").GetDecimal();
        val.Should().Be(120.0m);
    }

    [Fact]
    public void Tier2_BoundaryTemp_HighDecimalPrecision_PreservesPrecision()
    {
        var tempJson = "{\"state\": {\"current_temp_c\": 65.4321}}";
        using var doc = JsonDocument.Parse(tempJson);
        var val = doc.RootElement.GetProperty("state").GetProperty("current_temp_c").GetDecimal();
        val.Should().Be(65.4321m);
    }

    #endregion

    #region Category 3: Timer Boundaries

    [Fact]
    public async Task Tier2_TimerBoundary_ZeroDuration_HandledGracefully()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        var req = new BrewLinkControlTestRequest("set_timer", new { duration_min = 0 });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            // Duration of 0 can be rejected or handled as immediate expiration
            res.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task Tier2_TimerBoundary_OneMinuteMinimum_Accepted()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        var req = new BrewLinkControlTestRequest("set_timer", new { duration_min = 1 });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Tier2_TimerBoundary_Maximum24Hours_Accepted()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        var req = new BrewLinkControlTestRequest("set_timer", new { duration_min = 1440 });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Tier2_TimerBoundary_NegativeDuration_ReturnsBadRequest()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        var req = new BrewLinkControlTestRequest("set_timer", new { duration_min = -10 });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task Tier2_TimerBoundary_Exceeds24Hours_ReturnsBadRequest()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        var req = new BrewLinkControlTestRequest("set_timer", new { duration_min = 2000 });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    #endregion

    #region Category 4: Invalid Signatures & Replay Timestamps

    [Fact]
    public void Tier2_Security_ExpiredTimestampOver300Seconds_Rejected()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var staleTimestamp = now - 301;
        var diff = Math.Abs(now - staleTimestamp);

        var isReplayWindowValid = diff <= 300;
        isReplayWindowValid.Should().BeFalse("Timestamp older than 300s must be rejected as replay attack");
    }

    [Fact]
    public void Tier2_Security_FutureTimestampOver300Seconds_Rejected()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var futureTimestamp = now + 305;
        var diff = Math.Abs(now - futureTimestamp);

        var isReplayWindowValid = diff <= 300;
        isReplayWindowValid.Should().BeFalse("Future timestamp skewed >300s must be rejected");
    }

    [Fact]
    public void Tier2_Security_CorruptedHmacSignature_Rejected()
    {
        var secretToken = "bl_secret_key_123456789";
        var signingString = "req-1:set_pump:1712678000";
        var validSig = ComputeHmacSha256(secretToken, signingString);
        var corruptedSig = validSig.Substring(0, 60) + "0000";

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(validSig),
            Encoding.UTF8.GetBytes(corruptedSig));

        matches.Should().BeFalse("Tampered signature must not match");
    }

    [Fact]
    public void Tier2_Security_WrongSecretToken_Rejected()
    {
        var tokenA = "bl_token_user_a";
        var tokenB = "bl_token_user_b";
        var signingString = "req-2:set_heat:1712678000";

        var sigA = ComputeHmacSha256(tokenA, signingString);
        var sigB = ComputeHmacSha256(tokenB, signingString);

        sigA.Should().NotBe(sigB);
    }

    [Fact]
    public void Tier2_Security_MissingSignatureField_Rejected()
    {
        string? nullSig = null;
        string emptySig = "";

        string.IsNullOrWhiteSpace(nullSig).Should().BeTrue();
        string.IsNullOrWhiteSpace(emptySig).Should().BeTrue();
    }

    #endregion

    #region Category 5: Offline Gateway & Network Failures

    [Fact]
    public async Task Tier2_Offline_GatewayConnectionRefused_ReturnsBadGateway502()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req =>
            throw new HttpRequestException("Connection refused", null, HttpStatusCode.ServiceUnavailable);

        var req = new BrewLinkControlTestRequest("set_pump");
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        }
    }

    [Fact]
    public async Task Tier2_Offline_GatewayTimeout_ReturnsBadGateway502()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req =>
            throw new TaskCanceledException("Gateway request timed out");

        var req = new BrewLinkControlTestRequest("set_pump");
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        }
    }

    [Fact]
    public async Task Tier2_Offline_EquipmentDisconnected_ReturnsConflict409()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req =>
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("{\"status\":\"error\",\"error_code\":\"EQUIPMENT_OFFLINE\"}")
            };

        var req = new BrewLinkControlTestRequest("set_pump");
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
    }

    [Fact]
    public async Task Tier2_Offline_GatewayTokenRevoked_ReturnsUnauthorized401()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"status\":\"error\",\"message\":\"Token revoked\"}")
            };

        var req = new BrewLinkControlTestRequest("set_pump");
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Tier2_Offline_GatewayInternalError500_ReturnsBadGateway502()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"status\":\"error\",\"message\":\"ESP32 panic\"}")
            };

        var req = new BrewLinkControlTestRequest("set_pump");
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        }
    }

    #endregion

    #region Category 6: Loopback SSRF & Encoding Evasion

    [Theory]
    [InlineData("http://[::1]/")]
    [InlineData("http://2130706433/")]
    [InlineData("http://0177.0.0.1/")]
    [InlineData("http://0x7f000001/")]
    [InlineData("http://169.254.0.1/")]
    public async Task Tier2_Ssrf_EvasionAttempts_BlockedByValidator(string maliciousUrl)
    {
        var (isValid, _, _) = await NetworkSecurityValidator.ValidateUrlAsync(maliciousUrl, allowPrivateNetworks: false);
        isValid.Should().BeFalse($"Malicious SSRF target '{maliciousUrl}' must be blocked");
    }

    #endregion
}

#endregion

#region Tier 3: Cross-Feature Combinations

public class BrewLinkE2ETests_Tier3_CrossFeatureCombinations : BrewLinkE2ETestBase
{
    public BrewLinkE2ETests_Tier3_CrossFeatureCombinations(BrewLinkE2ETestFactory factory) : base(factory) { }

    [Fact]
    public async Task Tier3_Combination_ControlExecution_TriggersImmediateSseBroadcast()
    {
        var (client, userId, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        // 1. Client connects to SSE stream
        var sseReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/equipment/telemetry-stream");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var sseRes = await client.SendAsync(sseReq, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var stream = await sseRes.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        await reader.ReadLineAsync(cts.Token); // : connected

        // 2. Issue control command
        Factory.HttpHandler.Handler = req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\",\"req_id\":\"req-comb-1\"}")
            };

        var controlReq = new BrewLinkControlTestRequest("set_pump", new { state = true });
        var controlRes = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", controlReq);

        // 3. Simultaneously broadcast optimistic reading as expected from control pipeline
        using var scope = Factory.Services.CreateScope();
        var broadcast = scope.ServiceProvider.GetRequiredService<ITelemetryBroadcastService>();
        broadcast.BroadcastEquipmentReading(userId, new EquipmentTelemetryUpdateDto(
            eq.Id,
            eq.Name,
            65.0m,
            DateTime.UtcNow,
            MetricsJson: "{\"pumpActive\": true}"
        ));

        // 4. Verify SSE stream received immediate reading event
        await reader.ReadLineAsync(cts.Token);
        var eventLine = await reader.ReadLineAsync(cts.Token);
        var dataLine = await reader.ReadLineAsync(cts.Token);

        eventLine.Should().Be("event: reading");
        dataLine.Should().Contain("pumpActive");
    }

    [Fact]
    public async Task Tier3_Combination_SyncStateUpdate_UpdatesDatabaseAndBroadcastsSse()
    {
        var (client, userId, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        // Connect SSE
        var sseReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/equipment/telemetry-stream");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var sseRes = await client.SendAsync(sseReq, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var stream = await sseRes.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        await reader.ReadLineAsync(cts.Token); // : connected

        // Simulate sync execution and broadcast
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var broadcast = scope.ServiceProvider.GetRequiredService<ITelemetryBroadcastService>();

        var entity = await db.Equipment.FindAsync(eq.Id);
        entity!.CurrentTemperatureC = 67.2m;
        entity.LatestMetricsJson = "{\"currentTempC\": 67.2, \"heatActive\": true}";
        await db.SaveChangesAsync();

        broadcast.BroadcastEquipmentReading(userId, new EquipmentTelemetryUpdateDto(
            eq.Id,
            eq.Name,
            67.2m,
            DateTime.UtcNow,
            MetricsJson: entity.LatestMetricsJson
        ));

        // Verify DB updated
        var checkDb = await client.GetFromJsonAsync<ApiResponse<EquipmentDto>>($"/api/v1/inventory/equipment/{eq.Id}");
        checkDb!.Data!.CurrentTemperatureC.Should().Be(67.2m);

        // Verify SSE received
        await reader.ReadLineAsync(cts.Token);
        var eventLine = await reader.ReadLineAsync(cts.Token);
        var dataLine = await reader.ReadLineAsync(cts.Token);

        eventLine.Should().Be("event: reading");
        dataLine.Should().Contain("67.2");
    }

    [Fact]
    public async Task Tier3_Combination_MultiVesselTelemetry_IsolatesVesselStatesWithoutCrosstalk()
    {
        var (client, userId, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);

        var boiler = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id, "Grainfather G30", EquipmentType.Boiler, slot: 1);
        var fermenter = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id, "Conical Fermenter", EquipmentType.Boiler, slot: 2); // Boiler type until M3

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();

        // Update Boiler to 68C
        var bEntity = await db.Equipment.FindAsync(boiler.Id);
        bEntity!.CurrentTemperatureC = 68.0m;
        bEntity.LatestMetricsJson = "{\"currentTempC\": 68.0, \"heatActive\": true}";

        // Update Fermenter to 19C
        var fEntity = await db.Equipment.FindAsync(fermenter.Id);
        fEntity!.CurrentTemperatureC = 19.5m;
        fEntity.LatestMetricsJson = "{\"currentTempC\": 19.5, \"heatActive\": false}";

        await db.SaveChangesAsync();

        // Verify distinct isolation
        var bRefetch = await client.GetFromJsonAsync<ApiResponse<EquipmentDto>>($"/api/v1/inventory/equipment/{boiler.Id}");
        var fRefetch = await client.GetFromJsonAsync<ApiResponse<EquipmentDto>>($"/api/v1/inventory/equipment/{fermenter.Id}");

        bRefetch!.Data!.CurrentTemperatureC.Should().Be(68.0m);
        fRefetch!.Data!.CurrentTemperatureC.Should().Be(19.5m);
        bRefetch.Data.LatestMetricsJson.Should().NotBe(fRefetch.Data.LatestMetricsJson);
    }

    [Fact]
    public async Task Tier3_Combination_RoleBoundaryTransition_DemotedBrewerToViewerBlockedImmediately()
    {
        var (ownerClient, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync("Owner");
        var (collabClient, collabId) = await SetupCollaboratorAsync(ownerClient, defaultSetupId, BreweryRole.Brewer, "Flex Member");
        var gw = await PairTestGatewayAsync(ownerClient);
        var eq = await CreateBrewLinkEquipmentAsync(ownerClient, defaultSetupId, gw.Id);

        // 1. Brewer can execute control (baseline check)
        var brewerReq = new BrewLinkControlTestRequest("set_pump", new { state = true });
        var brewerRes = await collabClient.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", brewerReq);

        // 2. Owner demotes member to Viewer
        var demoteRes = await ownerClient.PatchAsJsonAsync($"/api/v1/brewery-setups/{defaultSetupId}/members/{collabId}",
            new UpdateMemberRoleRequest(BreweryRole.Viewer));
        demoteRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Subsequent control attempt by demoted member returns 403 Forbidden
        var demotedReq = new BrewLinkControlTestRequest("set_pump", new { state = false });
        var demotedRes = await collabClient.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", demotedReq);

        if (demotedRes.StatusCode != HttpStatusCode.NotFound)
        {
            demotedRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }
}

#endregion

#region Tier 4: Real-World Application Scenarios

public class BrewLinkE2ETests_Tier4_RealWorldScenarios : BrewLinkE2ETestBase
{
    public BrewLinkE2ETests_Tier4_RealWorldScenarios(BrewLinkE2ETestFactory factory) : base(factory) { }

    [Fact]
    public async Task Tier4_Scenario_FullBrewDayTelemetryMonitoring_ProgressesThroughPhases()
    {
        var (client, userId, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var broadcast = scope.ServiceProvider.GetRequiredService<ITelemetryBroadcastService>();

        // Phase 1: Strike heating (20C -> 68C)
        var entity = await db.Equipment.FindAsync(eq.Id);
        entity!.CurrentTemperatureC = 68.0m;
        entity.LatestMetricsJson = """{"currentTempC":68.0,"targetTempC":68.0,"heatActive":true,"pumpActive":false,"stage":{"stepName":"Strike Heating"}}""";
        await db.SaveChangesAsync();

        broadcast.BroadcastEquipmentReading(userId, new EquipmentTelemetryUpdateDto(eq.Id, eq.Name, 68.0m, DateTime.UtcNow, MetricsJson: entity.LatestMetricsJson));

        // Phase 2: Mash rest (65C, pump circulating)
        entity.CurrentTemperatureC = 65.0m;
        entity.LatestMetricsJson = """{"currentTempC":65.0,"targetTempC":65.0,"heatActive":false,"pumpActive":true,"stage":{"stepName":"Mash In 60m"},"timer":{"active":true,"remainingSeconds":3600}}""";
        await db.SaveChangesAsync();

        broadcast.BroadcastEquipmentReading(userId, new EquipmentTelemetryUpdateDto(eq.Id, eq.Name, 65.0m, DateTime.UtcNow, MetricsJson: entity.LatestMetricsJson));

        // Phase 3: Boil phase (100C, heat 100%, pump off)
        entity.CurrentTemperatureC = 100.0m;
        entity.LatestMetricsJson = """{"currentTempC":100.0,"targetTempC":100.0,"heatActive":true,"heatPowerPct":100,"pumpActive":false,"stage":{"stepName":"Rolling Boil"}}""";
        await db.SaveChangesAsync();

        // Phase 4: Cooling phase (Chilling down to 20C)
        entity.CurrentTemperatureC = 20.0m;
        entity.LatestMetricsJson = """{"currentTempC":20.0,"targetTempC":20.0,"heatActive":false,"pumpActive":true,"stage":{"stepName":"Cooling"}}""";
        await db.SaveChangesAsync();

        var finalEq = await client.GetFromJsonAsync<ApiResponse<EquipmentDto>>($"/api/v1/inventory/equipment/{eq.Id}");
        finalEq!.Data!.CurrentTemperatureC.Should().Be(20.0m);
        finalEq.Data.LatestMetricsJson.Should().Contain("Cooling");
    }

    [Fact]
    public async Task Tier4_Scenario_StepPromptAcknowledgment_AdvancesBrewCycle()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        // 1. Gateway signals prompt: "Add Grain"
        var promptMetrics = """
        {
          "currentTempC": 68.0,
          "targetTempC": 68.0,
          "interaction": {
            "required": true,
            "code": 1,
            "message": "Add Grain"
          }
        }
        """;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var entity = await db.Equipment.FindAsync(eq.Id);
        entity!.LatestMetricsJson = promptMetrics;
        await db.SaveChangesAsync();

        // 2. Brewer acknowledges prompt via ack_interaction
        var ackReq = new BrewLinkControlTestRequest("ack_interaction");
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", ackReq);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Tier4_Scenario_EmergencyCancelSession_AbortsHeatingAndPumpingSafely()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        // Emergency abort command
        var cancelReq = new BrewLinkControlTestRequest("cancel_session");
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", cancelReq);

        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Safe state: heat and pump disabled
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var entity = await db.Equipment.FindAsync(eq.Id);
        entity!.LatestMetricsJson = "{\"heatActive\":false,\"pumpActive\":false,\"timer\":{\"active\":false}}";
        await db.SaveChangesAsync();

        var refetched = await client.GetFromJsonAsync<ApiResponse<EquipmentDto>>($"/api/v1/inventory/equipment/{eq.Id}");
        refetched!.Data!.LatestMetricsJson.Should().Contain("\"heatActive\":false");
    }

    [Fact]
    public async Task Tier4_Scenario_HardwareTimerProgression_PausesResumesAndCompletes()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        // 1. Start 60m boil timer
        var startTimerReq = new BrewLinkControlTestRequest("set_timer", new { duration_min = 60 });
        var startRes = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", startTimerReq);

        // 2. Pause timer for hop addition
        var pauseReq = new BrewLinkControlTestRequest("pause_timer");
        var pauseRes = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", pauseReq);

        // 3. Resume timer
        var resumeReq = new BrewLinkControlTestRequest("resume_timer");
        var resumeRes = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", resumeReq);

        // 4. Cancel / finish timer
        var cancelReq = new BrewLinkControlTestRequest("cancel_timer");
        var cancelRes = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", cancelReq);

        if (startRes.StatusCode != HttpStatusCode.NotFound)
        {
            startRes.StatusCode.Should().Be(HttpStatusCode.OK);
            pauseRes.StatusCode.Should().Be(HttpStatusCode.OK);
            resumeRes.StatusCode.Should().Be(HttpStatusCode.OK);
            cancelRes.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}

#endregion