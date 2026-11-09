using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace BrewYou.ApiService.Tests;

public class FakeBrewLinkHttpMessageHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage?>? Handler { get; set; }

    public List<(HttpRequestMessage Request, string? Body)> RecordedRequests { get; } = new();

    public void Reset()
    {
        Handler = null;
        RecordedRequests.Clear();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = null;
        if (request.Content != null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        RecordedRequests.Add((request, body));

        if (Handler != null)
        {
            var response = Handler(request);
            if (response != null)
            {
                return response;
            }
        }

        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path.EndsWith("/api/pair/start", StringComparison.OrdinalIgnoreCase))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\"}")
            };
        }

        if (path.EndsWith("/api/pair/claim", StringComparison.OrdinalIgnoreCase))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    gateway_id = $"gw-{Guid.NewGuid():N}",
                    api_token = "raw-secret-token-12345",
                    mqtt_topic_key = "topic-key-xyz"
                }))
            };
        }

        if (path.EndsWith("/api/equipment/recipe", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/api/equipment/cmd", StringComparison.OrdinalIgnoreCase))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    message = "Recipe queued",
                    req_id = "req-1",
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }))
            };
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\"}")
        };
    }
}

public class BrewLinkTestFactory : WebApplicationFactory<Program>
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

public class BrewLinkEndpointsTests : IClassFixture<BrewLinkTestFactory>, IDisposable
{
    private readonly BrewLinkTestFactory _factory;
    private readonly HttpClient _client;

    public BrewLinkEndpointsTests(BrewLinkTestFactory factory)
    {
        _factory = factory;
        _factory.HttpHandler.Reset();
        _client = factory.CreateClient();
    }

    public void Dispose()
    {
        _factory.HttpHandler.Reset();
    }

    private async Task<(HttpClient Client, string UserId, string Email, Guid DefaultSetupId)> CreateAuthenticatedUserAsync()
    {
        var client = _factory.CreateClient();
        var email = $"brewlink_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, "BrewLink Brewer", "en", VolumeUnit.Liters);

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

    [Fact]
    public async Task BrewLinkEndpoints_RequireAuthentication()
    {
        var unauthenticatedClient = _factory.CreateClient();

        var getRes = await unauthenticatedClient.GetAsync("/api/v1/brewlink/gateways");
        getRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var pairStartRes = await unauthenticatedClient.PostAsJsonAsync("/api/v1/brewlink/gateways/pair-start",
            new StartBrewLinkPairingRequest("192.168.1.50"));
        pairStartRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var pairRes = await unauthenticatedClient.PostAsJsonAsync("/api/v1/brewlink/gateways/pair",
            new PairBrewLinkRequest("192.168.1.50", "123456", "Test"));
        pairRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PairStart_ValidLanHost_CallsGatewayAndReturnsOk()
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        _factory.HttpHandler.RecordedRequests.Clear();
        _factory.HttpHandler.Handler = req =>
        {
            req.RequestUri!.ToString().Should().Be("http://192.168.1.50/api/pair/start");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\"}")
            };
        };

        var res = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair-start",
            new StartBrewLinkPairingRequest("192.168.1.50"));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.HttpHandler.RecordedRequests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("::1")]
    [InlineData("169.254.169.254")]
    [InlineData("http://127.0.0.1")]
    public async Task PairStart_SsrfForbiddenHosts_ReturnsBadRequest(string invalidHost)
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        var res = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair-start",
            new StartBrewLinkPairingRequest(invalidHost));

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PairGateway_Success_StoresGateway_ReturnsCreated()
    {
        var (client, userId, _, _) = await CreateAuthenticatedUserAsync();

        _factory.HttpHandler.RecordedRequests.Clear();
        _factory.HttpHandler.Handler = req =>
        {
            req.RequestUri!.ToString().Should().Be("http://192.168.1.120/api/pair/claim");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    gateway_id = "brewlink-gateway-aabb11",
                    api_token = "raw-secret-token-12345",
                    mqtt_topic_key = "topic-key-xyz"
                }))
            };
        };

        var reqDto = new PairBrewLinkRequest("192.168.1.120", "654321", "Workshop BrewLink");
        var res = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair", reqDto);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<BrewLinkGatewayDto>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Data!.Name.Should().Be("Workshop BrewLink");
        envelope.Data.Host.Should().Be("192.168.1.120");
        envelope.Data.GatewayId.Should().Be("BREWLINK-GATEWAY-AABB11");

        // Verify request payload sent to ESP32
        var recorded = _factory.HttpHandler.RecordedRequests.Should().ContainSingle().Subject;
        recorded.Body.Should().Contain("\"pin\":\"654321\"");
        recorded.Body.Should().Contain("\"client_name\":\"BrewYou\"");

        // Verify database entity stored with encrypted credentials (not plaintext)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var entity = db.BrewLinkGateways.FirstOrDefault(g => g.UserId == userId && g.GatewayId == "BREWLINK-GATEWAY-AABB11");
        entity.Should().NotBeNull();
        entity!.ApiToken.Should().NotBe("raw-secret-token-12345"); // encrypted
        entity.MqttTopicKey.Should().NotBe("topic-key-xyz"); // encrypted
    }

    [Fact]
    public async Task PairGateway_InvalidPin_ReturnsBadRequest()
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        _factory.HttpHandler.RecordedRequests.Clear();
        _factory.HttpHandler.Handler = req =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"status\":\"error\",\"message\":\"Invalid PIN\"}")
            };

        var reqDto = new PairBrewLinkRequest("192.168.1.120", "000000", "Failing BrewLink");
        var res = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair", reqDto);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("123 456", "123456")]
    [InlineData("123-456", "123456")]
    [InlineData(" 654321 ", "654321")]
    [InlineData("A1B2C3", "A1B2C3")]
    [InlineData("a8f302", "a8f302")]
    public async Task PairGateway_FormatsAndAlphanumericPin_CleansPinAndSucceeds(string inputPin, string expectedSentPin)
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        _factory.HttpHandler.RecordedRequests.Clear();
        _factory.HttpHandler.Handler = req =>
        {
            req.RequestUri!.ToString().Should().Be("http://192.168.1.130/api/pair/claim");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    gateway_id = "brewlink-gateway-cleanpin",
                    api_token = "tok",
                    mqtt_topic_key = "key"
                }))
            };
        };

        var reqDto = new PairBrewLinkRequest("192.168.1.130", inputPin, "Formatted PIN Gateway");
        var res = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair", reqDto);

        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var recorded = _factory.HttpHandler.RecordedRequests.Should().ContainSingle().Subject;
        recorded.Body.Should().Contain($"\"pin\":\"{expectedSentPin}\"");
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12#45!")]
    public async Task PairGateway_InvalidPinFormat_ReturnsBadRequest(string invalidPin)
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        var reqDto = new PairBrewLinkRequest("192.168.1.130", invalidPin, "Invalid PIN Gateway");
        var res = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair", reqDto);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse>();
        envelope!.Error!.Message.Should().Be("Pairing PIN must be a 6-digit code.");
    }

    [Fact]
    public async Task DiscoverGateways_IngestsBroadcastBeacon_AndReturnsActiveGateways()
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        var discoveryService = _factory.Services.GetRequiredService<IBrewLinkDiscoveryService>();
        var beaconJson = JsonSerializer.Serialize(new
        {
            @event = "brewlink_pairing",
            gateway_id = "brewlink-esp32-beacon1",
            name = "Brewery BrewLink Beacon",
            ip = "192.168.1.188",
            http_port = 80,
            claim_url = "http://192.168.1.188/api/pair/claim",
            ws_url = "ws://192.168.1.188/ws",
            remaining_sec = 55
        });

        discoveryService.IngestBeaconJson(beaconJson).Should().BeTrue();

        var res = await client.GetAsync("/api/v1/brewlink/gateways/discover");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var env = await res.Content.ReadFromJsonAsync<ApiResponse<List<BrewLinkDiscoveredGatewayDto>>>();
        env!.Success.Should().BeTrue();
        env.Data.Should().Contain(g =>
            g.GatewayId == "BREWLINK-ESP32-BEACON1" &&
            g.Name == "Brewery BrewLink Beacon" &&
            g.Host == "192.168.1.188" &&
            g.RemainingSec > 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PairGateway_CodeFreePushButtonWithoutPin_SucceedsAndRevokesOnDelete(string? emptyPin)
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        _factory.HttpHandler.RecordedRequests.Clear();
        _factory.HttpHandler.Handler = req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/pair/claim"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        status = "ok",
                        gateway_id = "brewlink-pushbtn-01",
                        client_name = "BrewYou",
                        api_token = "pushbtn-token-123",
                        mqtt_topic_key = "pushbtn-mqtt-456"
                    }))
                };
            }

            if (req.RequestUri.AbsolutePath.EndsWith("/api/pair/delete"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\"}")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var reqDto = new PairBrewLinkRequest("192.168.1.189", emptyPin, "PushButton Gateway");
        var res = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair", reqDto);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var env = await res.Content.ReadFromJsonAsync<ApiResponse<BrewLinkGatewayDto>>();
        env!.Data!.GatewayId.Should().Be("BREWLINK-PUSHBTN-01");

        var claimReq = _factory.HttpHandler.RecordedRequests.Should().ContainSingle().Subject;
        claimReq.Body.Should().Contain("\"client_name\":\"BrewYou\"");
        claimReq.Body.Should().NotContain("\"pin\"");

        // Now delete/unpair the gateway and verify /api/pair/delete is called with client_name
        _factory.HttpHandler.RecordedRequests.Clear();
        var delRes = await client.DeleteAsync($"/api/v1/brewlink/gateways/{env.Data.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var deleteReq = _factory.HttpHandler.RecordedRequests.Should().ContainSingle().Subject;
        deleteReq.Request.RequestUri!.ToString().Should().Be("http://192.168.1.189/api/pair/delete");
        deleteReq.Body.Should().Contain("\"client_name\":\"BrewYou\"");
    }

    [Fact]
    public async Task GetGatewayEquipment_ReturnsParsedEquipmentSlotsAndCapabilities()
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        _factory.HttpHandler.Handler = req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    gateway_id = "gw-equip-list",
                    api_token = "equip-token-789",
                    mqtt_topic_key = "equip-key"
                }))
            };

        var pairRes = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair",
            new PairBrewLinkRequest("192.168.1.190", null, "Equipment Gateway"));
        var gw = (await pairRes.Content.ReadFromJsonAsync<ApiResponse<BrewLinkGatewayDto>>())!.Data!;

        _factory.HttpHandler.RecordedRequests.Clear();
        _factory.HttpHandler.Handler = req =>
        {
            req.RequestUri!.ToString().Should().Be("http://192.168.1.190/api/equipment");
            req.Headers.Authorization!.Scheme.Should().Be("Bearer");
            req.Headers.Authorization.Parameter.Should().Be("equip-token-789");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "max_slots": 3,
                  "active_connections": 1,
                  "equipment": [
                    {
                      "slot": 1,
                      "paired": true,
                      "connected": true,
                      "name": "Grainfather G30",
                      "model": "Grainfather G30",
                      "equipment_type": "boiler",
                      "mac": "C4:4F:33:1A:8B:22",
                      "rssi": -61,
                      "capabilities": {
                        "recipe_upload": true,
                        "mash_profile": true,
                        "pump_control": true,
                        "heat_control": true
                      },
                      "state": {
                        "temp_c": 67.2,
                        "target_temp_c": 67.0,
                        "pump_on": true,
                        "heat_pct": 65,
                        "timer_remaining_sec": 2140,
                        "step_index": 1
                      }
                    },
                    {
                      "slot": 2,
                      "paired": false,
                      "connected": false
                    }
                  ]
                }
                """)
            };
        };

        var eqListRes = await client.GetAsync($"/api/v1/brewlink/gateways/{gw.Id}/equipment");
        eqListRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var eqEnv = await eqListRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewLinkEquipmentDeviceDto>>>();
        eqEnv!.Success.Should().BeTrue();
        eqEnv.Data.Should().ContainSingle();

        var slot1 = eqEnv.Data!.First(d => d.Slot == 1);
        slot1.Paired.Should().BeTrue();
        slot1.Connected.Should().BeTrue();
        slot1.Name.Should().Be("Grainfather G30");
        slot1.Mac.Should().Be("C4:4F:33:1A:8B:22");
        slot1.Capabilities.Should().NotBeNull();
        slot1.Capabilities.RecipeUpload.Should().BeTrue();
        slot1.State.Should().NotBeNull();
        slot1.State!.TempC.Should().Be(67.2m);
        slot1.State.StepIndex.Should().Be(1);
    }

    [Fact]
    public async Task SendRecipe_MatchedBySlotAndTargetName_IncludesSlotAndTargetNameInEnvelope()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();

        _factory.HttpHandler.Handler = req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    gateway_id = "gw-slot-match",
                    api_token = "slot-token-123",
                    mqtt_topic_key = "slot-key-123"
                }))
            };

        var pairRes = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair",
            new PairBrewLinkRequest("192.168.1.195", null, "Slot Match Gateway"));
        var gw = (await pairRes.Content.ReadFromJsonAsync<ApiResponse<BrewLinkGatewayDto>>())!.Data!;

        // Create equipment matched via slot + targetName + targetMac
        var eqConfig = JsonSerializer.Serialize(new
        {
            brewLinkGatewayId = gw.Id,
            targetMac = "C44F331A8B22",
            targetName = "Grainfather G30",
            slot = 1
        });

        var eqRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(defaultSetupId, "Grainfather G30", EquipmentType.Boiler, 30,
                Subtype: EquipmentSubtype.AllInOne,
                ConnectionType: EquipmentConnectionType.BrewLink,
                ConnectionConfigJson: eqConfig));
        eqRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var eq = (await eqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var recipeRes = await client.PostAsJsonAsync("/api/v1/recipes", new CreateRecipeRequest(
            Name: "Slot Matched Kölsch",
            Description: "Testing slot and target_name targeting",
            BeerStyle: "Kölsch",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            IsPublic: false,
            Ingredients: new List<RecipeIngredientInputDto>(),
            MashSteps: new List<RecipeMashStepInputDto>(),
            FermentationSteps: new List<RecipeFermentationStepInputDto>()
        ));
        var recipe = (await recipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        _factory.HttpHandler.RecordedRequests.Clear();
        _factory.HttpHandler.Handler = req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\",\"message\":\"Recipe queued\",\"req_id\":\"req-slot\"}")
            };

        var sendRes = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/send-recipe",
            new SendBrewLinkRecipeRequest(RecipeId: recipe.Id, BatchId: null));
        sendRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var recorded = _factory.HttpHandler.RecordedRequests.Should().ContainSingle().Subject;
        var json = JsonDocument.Parse(recorded.Body!);
        json.RootElement.GetProperty("target_mac").GetString().Should().Be("C44F331A8B22");
        json.RootElement.GetProperty("target_name").GetString().Should().Be("Grainfather G30");
        json.RootElement.GetProperty("slot").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetGateways_MultiUserIsolation_ReturnsOnlyCurrentUserGateways()
    {
        var (clientA, _, _, _) = await CreateAuthenticatedUserAsync();
        var (clientB, _, _, _) = await CreateAuthenticatedUserAsync();

        _factory.HttpHandler.Handler = req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    gateway_id = $"gw-{Guid.NewGuid():N}",
                    api_token = "tok",
                    mqtt_topic_key = "key"
                }))
            };

        // User A pairs gateway
        await clientA.PostAsJsonAsync("/api/v1/brewlink/gateways/pair",
            new PairBrewLinkRequest("192.168.1.101", "111111", "User A Gateway"));

        // User B pairs gateway
        await clientB.PostAsJsonAsync("/api/v1/brewlink/gateways/pair",
            new PairBrewLinkRequest("192.168.1.102", "222222", "User B Gateway"));

        // User A lists gateways
        var resA = await clientA.GetAsync("/api/v1/brewlink/gateways");
        resA.StatusCode.Should().Be(HttpStatusCode.OK);
        var envA = await resA.Content.ReadFromJsonAsync<ApiResponse<List<BrewLinkGatewayDto>>>();
        envA!.Data.Should().ContainSingle(g => g.Name == "User A Gateway");
        envA.Data.Should().NotContain(g => g.Name == "User B Gateway");

        // User B lists gateways
        var resB = await clientB.GetAsync("/api/v1/brewlink/gateways");
        resB.StatusCode.Should().Be(HttpStatusCode.OK);
        var envB = await resB.Content.ReadFromJsonAsync<ApiResponse<List<BrewLinkGatewayDto>>>();
        envB!.Data.Should().ContainSingle(g => g.Name == "User B Gateway");
        envB.Data.Should().NotContain(g => g.Name == "User A Gateway");
    }

    [Fact]
    public async Task UpdateAndDeleteGateway_LifecycleWorks()
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        _factory.HttpHandler.Handler = req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    gateway_id = "gw-update-test",
                    api_token = "tok",
                    mqtt_topic_key = "key"
                }))
            };

        var pairRes = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair",
            new PairBrewLinkRequest("192.168.1.105", "123123", "Initial Name"));
        var gw = (await pairRes.Content.ReadFromJsonAsync<ApiResponse<BrewLinkGatewayDto>>())!.Data!;

        // Update
        var updateRes = await client.PutAsJsonAsync($"/api/v1/brewlink/gateways/{gw.Id}",
            new UpdateBrewLinkGatewayRequest("Updated Name", "192.168.1.106"));
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedGw = (await updateRes.Content.ReadFromJsonAsync<ApiResponse<BrewLinkGatewayDto>>())!.Data!;
        updatedGw.Name.Should().Be("Updated Name");
        updatedGw.Host.Should().Be("192.168.1.106");

        // Rename with unchanged host (and with mDNS domain) succeeds without failing host validation
        var renameRes = await client.PutAsJsonAsync($"/api/v1/brewlink/gateways/{gw.Id}",
            new UpdateBrewLinkGatewayRequest("Renamed Gateway", "192.168.1.106"));
        renameRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var renamedGw = (await renameRes.Content.ReadFromJsonAsync<ApiResponse<BrewLinkGatewayDto>>())!.Data!;
        renamedGw.Name.Should().Be("Renamed Gateway");

        var mdnsRes = await client.PutAsJsonAsync($"/api/v1/brewlink/gateways/{gw.Id}",
            new UpdateBrewLinkGatewayRequest("Renamed Gateway", "brewlink.local"));
        mdnsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var mdnsGw = (await mdnsRes.Content.ReadFromJsonAsync<ApiResponse<BrewLinkGatewayDto>>())!.Data!;
        mdnsGw.Host.Should().Be("brewlink.local");

        // Delete
        var delRes = await client.DeleteAsync($"/api/v1/brewlink/gateways/{gw.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify gone
        var listRes = await client.GetAsync("/api/v1/brewlink/gateways");
        var list = (await listRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewLinkGatewayDto>>>())!.Data!;
        list.Should().NotContain(g => g.Id == gw.Id);
    }

    [Fact]
    public async Task SendRecipe_NonBrewLinkEquipment_ReturnsBadRequest()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();

        // Create standard equipment (ConnectionType = None)
        var eqRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(defaultSetupId, "Plain Kettle", EquipmentType.Boiler, 30,
                Subtype: EquipmentSubtype.AllInOne,
                ConnectionType: EquipmentConnectionType.None));
        eqRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var eq = (await eqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var sendRes = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/send-recipe",
            new SendBrewLinkRecipeRequest(RecipeId: Guid.NewGuid(), BatchId: null));

        sendRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SendRecipe_Success_SendsHmacSignedPayloadWithWaterCalculations()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();

        // 1. Pair a gateway
        _factory.HttpHandler.Handler = req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    gateway_id = "gw-recipe-test",
                    api_token = "secret-token-xyz",
                    mqtt_topic_key = "topic-secret-abc"
                }))
            };

        var pairRes = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair",
            new PairBrewLinkRequest("192.168.1.150", "999888", "Kitchen Gateway"));
        var gw = (await pairRes.Content.ReadFromJsonAsync<ApiResponse<BrewLinkGatewayDto>>())!.Data!;

        // 2. Create All-in-One Boiler with BrewLink
        var eqConfig = JsonSerializer.Serialize(new
        {
            brewLinkGatewayId = gw.Id,
            targetMac = "C4:4F:33:1A:8B:22"
        });

        var eqRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(defaultSetupId, "G30 Boiler", EquipmentType.Boiler, 30,
                Subtype: EquipmentSubtype.AllInOne,
                ConnectionType: EquipmentConnectionType.BrewLink,
                ConnectionConfigJson: eqConfig));
        eqRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var eq = (await eqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // 3. Create a Recipe with Grain
        var ingsRes = await client.GetAsync("/api/v1/ingredients");
        var ingsEnv = await ingsRes.Content.ReadFromJsonAsync<ApiResponse<List<IngredientDto>>>();
        var grain = ingsEnv!.Data!.First(i => i.Type == IngredientType.Fermentable);

        var recipeRes = await client.PostAsJsonAsync("/api/v1/recipes", new CreateRecipeRequest(
            Name: "Session IPA",
            Description: "A tasty session IPA",
            BeerStyle: "IPA",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            IsPublic: false,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(grain.Id, 5.0m, "kg", 60, IngredientUsage.Mash, "Base Malt")
            },
            MashSteps: new List<RecipeMashStepInputDto>
            {
                new(1, "Saccharification", MashStepType.Temperature, 65m, 60, null, null, null),
                new(2, "Mash Out", MashStepType.Temperature, 75m, 10, null, null, null)
            },
            FermentationSteps: new List<RecipeFermentationStepInputDto>()
        ));
        recipeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipe = (await recipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // 4. Send Recipe
        _factory.HttpHandler.RecordedRequests.Clear();
        _factory.HttpHandler.Handler = req =>
        {
            req.RequestUri!.ToString().Should().Be("http://192.168.1.150/api/equipment/recipe");
            req.Headers.Authorization.Should().NotBeNull();
            req.Headers.Authorization!.Scheme.Should().Be("Bearer");
            req.Headers.Authorization.Parameter.Should().Be("secret-token-xyz");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    message = "Recipe queued",
                    req_id = "req-1",
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }))
            };
        };

        var sendRes = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/send-recipe",
            new SendBrewLinkRecipeRequest(RecipeId: recipe.Id, BatchId: null));

        sendRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var sendEnv = await sendRes.Content.ReadFromJsonAsync<ApiResponse<BrewLinkCommandResultDto>>();
        sendEnv!.Success.Should().BeTrue();
        sendEnv.Data!.Status.Should().Be("queued");

        // Verify sent payload
        var recorded = _factory.HttpHandler.RecordedRequests.Should().ContainSingle().Subject;
        var json = JsonDocument.Parse(recorded.Body!);
        json.RootElement.GetProperty("action").GetString().Should().Be("load_recipe");
        json.RootElement.GetProperty("sig").GetString().Should().NotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("target_mac").GetString().Should().Be("C44F331A8B22"); // Normalized without colons
        json.RootElement.GetProperty("equipment_type").GetString().Should().Be("boiler");

        var recipeEl = json.RootElement.GetProperty("params").GetProperty("recipe");
        recipeEl.GetProperty("name").GetString().Should().Be("Session IPA");
        recipeEl.GetProperty("mash_steps").GetArrayLength().Should().Be(2);
        recipeEl.GetProperty("mash_water_l").GetDecimal().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SendRecipe_EquipmentDisconnected_Returns409Conflict()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();

        _factory.HttpHandler.Handler = req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    gateway_id = "gw-disconnect-test",
                    api_token = "tok",
                    mqtt_topic_key = "key"
                }))
            };

        var pairRes = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair",
            new PairBrewLinkRequest("192.168.1.160", "123123", "Garage"));
        var gw = (await pairRes.Content.ReadFromJsonAsync<ApiResponse<BrewLinkGatewayDto>>())!.Data!;

        var eqConfig = JsonSerializer.Serialize(new
        {
            brewLinkGatewayId = gw.Id,
            targetMac = "AABBCCDDEEFF"
        });

        var eqRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(defaultSetupId, "G30", EquipmentType.Boiler, 30,
                Subtype: EquipmentSubtype.AllInOne,
                ConnectionType: EquipmentConnectionType.BrewLink,
                ConnectionConfigJson: eqConfig));
        var eq = (await eqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var recipeRes = await client.PostAsJsonAsync("/api/v1/recipes", new CreateRecipeRequest(
            Name: "Disconnected Test",
            Description: "Testing disconnected state",
            BeerStyle: "Ale",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            IsPublic: false,
            Ingredients: new List<RecipeIngredientInputDto>(),
            MashSteps: new List<RecipeMashStepInputDto>(),
            FermentationSteps: new List<RecipeFermentationStepInputDto>()
        ));
        var recipe = (await recipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // Gateway returns 409 equipment disconnected
        _factory.HttpHandler.Handler = req =>
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "error",
                    error_code = 261,
                    message = "Equipment disconnected or out of Bluetooth range"
                }))
            };

        var sendRes = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/send-recipe",
            new SendBrewLinkRecipeRequest(RecipeId: recipe.Id, BatchId: null));

        sendRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PairGateway_PairingExpired_Returns410Message()
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        _factory.HttpHandler.RecordedRequests.Clear();
        _factory.HttpHandler.Handler = req =>
            new HttpResponseMessage(HttpStatusCode.Gone)
            {
                Content = new StringContent("{\"status\":\"error\",\"message\":\"Pairing window expired\"}")
            };

        var reqDto = new PairBrewLinkRequest("192.168.1.120", "123456", "Expired BrewLink");
        var res = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair", reqDto);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse>();
        envelope!.Error!.Message.Should().Contain("Pairing window expired");
    }

    [Fact]
    public async Task SendRecipe_Primary404_FallsBackToCmdEndpoint_AndSucceeds()
    {
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();

        // 1. Pair a gateway
        var pairRes = await client.PostAsJsonAsync("/api/v1/brewlink/gateways/pair",
            new PairBrewLinkRequest("192.168.1.170", "123456", "Fallback Gateway"));
        var gw = (await pairRes.Content.ReadFromJsonAsync<ApiResponse<BrewLinkGatewayDto>>())!.Data!;

        // 2. Create All-in-One Boiler
        var eqConfig = JsonSerializer.Serialize(new
        {
            brewLinkGatewayId = gw.Id,
            targetMac = "C44F331A8B99"
        });

        var eqRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(defaultSetupId, "Legacy Boiler", EquipmentType.Boiler, 30,
                Subtype: EquipmentSubtype.AllInOne,
                ConnectionType: EquipmentConnectionType.BrewLink,
                ConnectionConfigJson: eqConfig));
        var eq = (await eqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // 3. Create Recipe
        var recipeRes = await client.PostAsJsonAsync("/api/v1/recipes", new CreateRecipeRequest(
            Name: "Fallback Pale Ale",
            Description: "Testing fallback endpoint",
            BeerStyle: "Pale Ale",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            IsPublic: false,
            Ingredients: new List<RecipeIngredientInputDto>(),
            MashSteps: new List<RecipeMashStepInputDto>(),
            FermentationSteps: new List<RecipeFermentationStepInputDto>()
        ));
        var recipe = (await recipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // 4. Primary /api/equipment/recipe returns 404, fallback /api/equipment/cmd succeeds
        _factory.HttpHandler.RecordedRequests.Clear();
        _factory.HttpHandler.Handler = req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/equipment/recipe"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"status\":\"error\",\"message\":\"Not found\"}")
                };
            }

            if (req.RequestUri!.AbsolutePath.EndsWith("/api/equipment/cmd"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        status = "ok",
                        message = "Recipe queued on legacy fallback",
                        req_id = "req-fallback-1",
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    }))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var sendRes = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/send-recipe",
            new SendBrewLinkRecipeRequest(RecipeId: recipe.Id, BatchId: null));

        sendRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var sendEnv = await sendRes.Content.ReadFromJsonAsync<ApiResponse<BrewLinkCommandResultDto>>();
        sendEnv!.Success.Should().BeTrue();
        sendEnv.Data!.Status.Should().Be("queued");

        // Verify two requests were made: first to /recipe, then to /cmd
        _factory.HttpHandler.RecordedRequests.Should().HaveCount(2);
        _factory.HttpHandler.RecordedRequests[0].Request.RequestUri!.AbsolutePath.Should().EndWith("/api/equipment/recipe");
        _factory.HttpHandler.RecordedRequests[1].Request.RequestUri!.AbsolutePath.Should().EndWith("/api/equipment/cmd");
    }
}