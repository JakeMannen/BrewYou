using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace BrewYou.ApiService.Tests;

public class BrewLinkControlTests : BrewLinkE2ETestBase
{
    private const string TestGatewayToken = "bl_secret_token_abcdef1234567890";

    public BrewLinkControlTests(BrewLinkE2ETestFactory factory) : base(factory)
    {
    }

    #region 1. All 12 Actions & HMAC-SHA256 Envelope Verification

    [Theory]
    [InlineData("set_pump", "{\"state\":true}")]
    [InlineData("set_heat", "{\"state\":true,\"power_pct\":80}")]
    [InlineData("set_target_temp", "{\"temp_c\":68.5}")]
    [InlineData("set_timer", "{\"duration_min\":45}")]
    [InlineData("pause_timer", "{}")]
    [InlineData("resume_timer", "{}")]
    [InlineData("cancel_timer", "{}")]
    [InlineData("press_set", "{}")]
    [InlineData("ack_interaction", "{\"id\":\"prompt-1\",\"choice\":\"continue\"}")]
    [InlineData("dismiss_alert", "{\"id\":\"alert-1\"}")]
    [InlineData("dismiss_sparge_alert", "{}")]
    [InlineData("cancel_session", "{}")]
    public async Task Control_All12Actions_SendValidHmacSignedEnvelopeAndReturn200(string action, string paramJson)
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        HttpRequestMessage? interceptedRequest = null;
        string? interceptedBody = null;

        Factory.HttpHandler.Handler = req =>
        {
            interceptedRequest = req;
            interceptedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    req_id = "req-12345678",
                    gateway_id = gw.GatewayId,
                    target_mac = "C44F331A8B22",
                    message = $"Action {action} executed"
                }))
            };
        };

        var controlRequest = new BrewLinkControlTestRequest(action, JsonNode.Parse(paramJson));
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", controlRequest);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<BrewLinkControlTestResponse>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Data!.Status.Should().Be("ok");

        // Assert HTTP headers
        interceptedRequest.Should().NotBeNull();
        interceptedRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        interceptedRequest.Headers.Authorization.Parameter.Should().Be(TestGatewayToken);
        interceptedRequest.Headers.GetValues("X-BrewLink-Token").Should().Contain(TestGatewayToken);

        // Assert JSON body envelope and cryptographic HMAC-SHA256 signature
        using var bodyDoc = JsonDocument.Parse(interceptedBody!);
        var root = bodyDoc.RootElement;
        root.GetProperty("action").GetString().Should().Be(action);

        var reqId = root.GetProperty("req_id").GetString()!;
        var timestamp = root.GetProperty("timestamp").GetInt64();
        var sig = root.GetProperty("sig").GetString()!;

        sig.Should().HaveLength(64, "HMAC-SHA256 hex string must be 64 characters");
        sig.Should().MatchRegex("^[0-9a-f]{64}$", "Signature must be lowercase hex characters");

        var expectedSigningInput = $"{reqId}:{action}:{timestamp}";
        var expectedSig = ComputeHmacSha256(TestGatewayToken, expectedSigningInput);
        sig.Should().Be(expectedSig, "The gateway command envelope signature must match the exact computed HMAC-SHA256");
    }

    #endregion

    #region 2. Parameter Validation & Boundary Edge Cases

    [Fact]
    public async Task Control_UnsupportedAction_ReturnsBadRequest400()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        var req = new BrewLinkControlTestRequest("unknown_action");
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await res.Content.ReadFromJsonAsync<ApiResponse>();
        body!.Error!.Code.Should().Be("BAD_REQUEST");
    }

    [Theory]
    [InlineData(101)]
    [InlineData(150)]
    [InlineData(-1)]
    public async Task Control_SetHeat_InvalidPowerPct_ReturnsBadRequest400(int invalidPower)
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        var req = new BrewLinkControlTestRequest("set_heat", new { state = true, power_pct = invalidPower });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await res.Content.ReadFromJsonAsync<ApiResponse>();
        body!.Error!.Code.Should().Be("BAD_REQUEST");
    }

    [Theory]
    [InlineData(-5.0)]
    [InlineData(110.0)]
    public async Task Control_SetTargetTemp_OutOfRange_ReturnsBadRequest400(double invalidTemp)
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        var req = new BrewLinkControlTestRequest("set_target_temp", new { temp_c = invalidTemp });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await res.Content.ReadFromJsonAsync<ApiResponse>();
        body!.Error!.Code.Should().Be("BAD_REQUEST");
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(2000)]
    public async Task Control_SetTimer_OutOfRange_ReturnsBadRequest400(int invalidDuration)
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        var req = new BrewLinkControlTestRequest("set_timer", new { duration_min = invalidDuration });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await res.Content.ReadFromJsonAsync<ApiResponse>();
        body!.Error!.Code.Should().Be("BAD_REQUEST");
    }

    #endregion

    #region 3. Primary & Fallback Transport Routing

    [Fact]
    public async Task Control_PrimaryReturns404_FallsBackToCmdEndpointAndSucceeds()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        var attempts = new List<string>();

        Factory.HttpHandler.Handler = req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            attempts.Add(path);

            if (path.EndsWith("/api/equipment/control"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"error\":\"Endpoint not found\"}")
                };
            }

            if (path.EndsWith("/api/equipment/cmd"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"req_id\":\"fallback-1\",\"message\":\"Executed on legacy endpoint\"}")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var req = new BrewLinkControlTestRequest("set_pump", new { state = true });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        attempts.Should().Contain(p => p.EndsWith("/api/equipment/control"));
        attempts.Should().Contain(p => p.EndsWith("/api/equipment/cmd"));
    }

    #endregion

    #region 4. Role Authorization

    [Fact]
    public async Task Control_RoleAuthorization_OwnerCanExecute()
    {
        var (ownerClient, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync("Owner");
        var gw = await PairTestGatewayAsync(ownerClient);
        var eq = await CreateBrewLinkEquipmentAsync(ownerClient, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\",\"message\":\"Owner command success\"}")
        };

        var req = new BrewLinkControlTestRequest("set_pump", new { state = true });
        var res = await ownerClient.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Control_RoleAuthorization_BrewerCanExecute()
    {
        var (ownerClient, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync("Owner");
        var (brewerClient, _) = await SetupCollaboratorAsync(ownerClient, defaultSetupId, BreweryRole.Brewer, "Brewer Friend");
        var gw = await PairTestGatewayAsync(ownerClient);
        var eq = await CreateBrewLinkEquipmentAsync(ownerClient, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\",\"message\":\"Brewer command success\"}")
        };

        var req = new BrewLinkControlTestRequest("set_pump", new { state = true });
        var res = await brewerClient.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Control_RoleAuthorization_ViewerReceives403Forbidden()
    {
        var (ownerClient, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync("Owner");
        var (viewerClient, _) = await SetupCollaboratorAsync(ownerClient, defaultSetupId, BreweryRole.Viewer, "Viewer Friend");
        var gw = await PairTestGatewayAsync(ownerClient);
        var eq = await CreateBrewLinkEquipmentAsync(ownerClient, defaultSetupId, gw.Id);

        var req = new BrewLinkControlTestRequest("set_pump", new { state = true });
        var res = await viewerClient.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await res.Content.ReadFromJsonAsync<ApiResponse>();
        body!.Error!.Code.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Control_RoleAuthorization_UnauthenticatedReceives401Unauthorized()
    {
        var unauthClient = Factory.CreateClient();
        var fakeId = Guid.NewGuid();

        var res = await unauthClient.PostAsJsonAsync($"/api/v1/inventory/equipment/{fakeId}/brewlink/control",
            new BrewLinkControlTestRequest("set_pump"));

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region 5. Outbound Network Security & SSRF Protection

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("169.254.169.254")]
    [InlineData("0.0.0.0")]
    public async Task Control_ProhibitedGatewayHost_ReturnsBadRequest400(string prohibitedHost)
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        var res = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair-start",
            new StartBrewLinkPairingRequest(prohibitedHost));

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Control_ValidLanGatewayHost_Succeeds()
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        Factory.HttpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\"}")
        };

        var res = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair-start",
            new StartBrewLinkPairingRequest("192.168.1.120"));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region 6. Gateway Error Handling & Status Codes

    [Fact]
    public async Task Control_OfflinePeripheral_Gateway409_ReturnsConflict409()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"status\":\"error\",\"error_code\":\"EQUIPMENT_OFFLINE\"}")
        };

        var req = new BrewLinkControlTestRequest("set_pump", new { state = true });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await res.Content.ReadFromJsonAsync<ApiResponse>();
        body!.Error!.Code.Should().Be("EQUIPMENT_DISCONNECTED");
    }

    [Fact]
    public async Task Control_GatewayTimeout_ReturnsBadGateway502()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req => throw new TaskCanceledException("Gateway connection timed out");

        var req = new BrewLinkControlTestRequest("set_pump", new { state = true });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        res.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var body = await res.Content.ReadFromJsonAsync<ApiResponse>();
        body!.Error!.Code.Should().Be("GATEWAY_ERROR");
    }

    [Fact]
    public async Task Control_NonBrewLinkEquipment_ReturnsConflict409()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();

        var eqRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(defaultSetupId, "Manual Kettle", EquipmentType.Boiler, 30,
                Subtype: EquipmentSubtype.AllInOne,
                ConnectionType: EquipmentConnectionType.None));
        eqRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var eq = (await eqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var req = new BrewLinkControlTestRequest("set_pump", new { state = true });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await res.Content.ReadFromJsonAsync<ApiResponse>();
        body!.Error!.Code.Should().Be("EQUIPMENT_DISCONNECTED");
    }

    #endregion

    #region 7. Optimistic State Updates & Immediate SSE Fanout

    [Fact]
    public async Task Control_OptimisticallyUpdatesLatestMetricsJsonInDatabase()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\",\"req_id\":\"opt-1\"}")
        };

        var req = new BrewLinkControlTestRequest("set_heat", new { state = true, power_pct = 75 });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var dbEq = await db.Equipment.FindAsync(eq.Id);

        dbEq!.LatestMetricsJson.Should().NotBeNull();
        dbEq.LatestMetricsJson.Should().Contain("\"heatActive\":true");
        dbEq.LatestMetricsJson.Should().Contain("\"heatPowerPct\":75");
    }

    [Fact]
    public async Task Control_BroadcastsImmediateSseReadingOnStream()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\",\"req_id\":\"opt-2\"}")
        };

        var sseReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/equipment/telemetry-stream");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var sseRes = await client.SendAsync(sseReq, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var stream = await sseRes.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        await reader.ReadLineAsync(cts.Token); // : connected
        await reader.ReadLineAsync(cts.Token); // blank line

        var controlReq = new BrewLinkControlTestRequest("set_pump", new { state = true });
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", controlReq);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var eventLine = await reader.ReadLineAsync(cts.Token);
        var dataLine = await reader.ReadLineAsync(cts.Token);

        eventLine.Should().Be("event: reading");
        dataLine.Should().StartWith("data: ");
        var payloadJson = dataLine!["data: ".Length..];
        using var sseDoc = JsonDocument.Parse(payloadJson);
        sseDoc.RootElement.GetProperty("metricsJson").GetString().Should().Contain("\"pumpActive\":true");
    }

    [Fact]
    public async Task Control_SetHeat_WithStateFalse_DisablesHeaterAndSetsPowerToZero()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        HttpRequestMessage? interceptedRequest = null;
        string? interceptedBody = null;

        Factory.HttpHandler.Handler = req =>
        {
            interceptedRequest = req;
            interceptedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    req_id = "req-heat-off",
                    gateway_id = gw.GatewayId,
                    target_mac = "C44F331A8B22",
                    message = "Action set_heat executed"
                }))
            };
        };

        // Send set_heat with state = false and powerPct = 0
        var controlReq = new BrewLinkControlTestRequest("set_heat", new { state = false, power_pct = 0 }, State: false, PowerPct: 0m);
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", controlReq);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify gateway envelope params
        interceptedBody.Should().NotBeNull();
        using var bodyDoc = JsonDocument.Parse(interceptedBody!);
        var root = bodyDoc.RootElement;
        var p = root.GetProperty("params");
        p.GetProperty("state").GetBoolean().Should().BeFalse();
        p.GetProperty("power_pct").GetDecimal().Should().Be(0m);

        // Verify updated entity in database has heatActive = false and heatPowerPct = 0
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var updatedEq = await db.Equipment.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eq.Id);
        updatedEq.Should().NotBeNull();
        updatedEq!.LatestMetricsJson.Should().Contain("\"heatActive\":false");
        updatedEq.LatestMetricsJson.Should().Contain("\"heatPowerPct\":0");
    }

    [Fact]
    public async Task Control_SetPump_WithStateFalse_DisablesPump()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        HttpRequestMessage? interceptedRequest = null;
        string? interceptedBody = null;

        Factory.HttpHandler.Handler = req =>
        {
            interceptedRequest = req;
            interceptedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    req_id = "req-pump-off",
                    gateway_id = gw.GatewayId,
                    target_mac = "C44F331A8B22",
                    message = "Action set_pump executed"
                }))
            };
        };

        // Send set_pump with state = false
        var controlReq = new BrewLinkControlTestRequest("set_pump", new { state = false }, State: false);
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", controlReq);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify gateway envelope params
        interceptedBody.Should().NotBeNull();
        using var bodyDoc = JsonDocument.Parse(interceptedBody!);
        var root = bodyDoc.RootElement;
        var p = root.GetProperty("params");
        p.GetProperty("state").GetBoolean().Should().BeFalse();

        // Verify updated entity in database has pumpActive = false
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var updatedEq = await db.Equipment.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eq.Id);
        updatedEq.Should().NotBeNull();
        updatedEq!.LatestMetricsJson.Should().Contain("\"pumpActive\":false");
    }

    #endregion

    #region 8. State Synchronization (`POST /brewlink/sync`)

    [Fact]
    public async Task Sync_ConnectedEquipment_UpdatesCurrentTempAndMetricsJsonInDatabase()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req =>
        {
            req.RequestUri!.AbsolutePath.Should().EndWith("/api/equipment");
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
                      "connected": true,
                      "state": {
                        "current_temp_c": 67.2,
                        "target_temp_c": 68.0,
                        "heat_active": true,
                        "pump_active": true
                      }
                    }
                  ]
                }
                """)
            };
        };

        var res = await client.PostAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/sync", null);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<BrewLinkSyncTestResponse>>();
        envelope.Should().NotBeNull();
        envelope!.Data!.Synced.Should().BeTrue();
        envelope.Data.TemperatureC.Should().Be(67.2m);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var dbEq = await db.Equipment.FindAsync(eq.Id);
        dbEq!.CurrentTemperatureC.Should().Be(67.2m);
        dbEq.LatestMetricsJson.Should().Contain("67.2");
    }

    [Fact]
    public async Task Sync_DisconnectedDevice_ReturnsConflict409()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "gateway_id": "C44F331A8B20",
              "count": 1,
              "equipment": [
                {
                  "slot": 1,
                  "mac": "C44F331A8B22",
                  "connected": false
                }
              ]
            }
            """)
        };

        var res = await client.PostAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/sync", null);

        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await res.Content.ReadFromJsonAsync<ApiResponse>();
        body!.Error!.Code.Should().Be("EQUIPMENT_DISCONNECTED");
    }

    [Fact]
    public async Task Sync_Gateway502_ReturnsBadGateway502()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("{\"error\":\"Gateway offline\"}")
        };

        var res = await client.PostAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/sync", null);

        res.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var body = await res.Content.ReadFromJsonAsync<ApiResponse>();
        body!.Error!.Code.Should().Be("GATEWAY_ERROR");
    }

    [Fact]
    public async Task Sync_ViewerRole_ReturnsForbidden403()
    {
        var (ownerClient, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync("Owner");
        var (viewerClient, _) = await SetupCollaboratorAsync(ownerClient, defaultSetupId, BreweryRole.Viewer, "Viewer");
        var gw = await PairTestGatewayAsync(ownerClient);
        var eq = await CreateBrewLinkEquipmentAsync(ownerClient, defaultSetupId, gw.Id);

        var res = await viewerClient.PostAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/sync", null);

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await res.Content.ReadFromJsonAsync<ApiResponse>();
        body!.Error!.Code.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Sync_BroadcastsImmediateSseReading()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        Factory.HttpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "gateway_id": "C44F331A8B20",
              "count": 1,
              "equipment": [
                {
                  "slot": 1,
                  "mac": "C44F331A8B22",
                  "connected": true,
                  "state": {
                    "current_temp_c": 69.1,
                    "target_temp_c": 70.0
                  }
                }
              ]
            }
            """)
        };

        var sseReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/equipment/telemetry-stream");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var sseRes = await client.SendAsync(sseReq, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var stream = await sseRes.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        await reader.ReadLineAsync(cts.Token); // : connected
        await reader.ReadLineAsync(cts.Token); // blank line

        var res = await client.PostAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/sync", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var eventLine = await reader.ReadLineAsync(cts.Token);
        var dataLine = await reader.ReadLineAsync(cts.Token);

        eventLine.Should().Be("event: reading");
        dataLine.Should().Contain("69.1");
    }

    #endregion
}