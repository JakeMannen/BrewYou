using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace BrewYou.ApiService.Tests;

public class BrewLinkDiscoveryTests
{
    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "BrewYou.ApiService.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void IngestBeaconJson_ValidSpecPayload_StoresActiveBeacon()
    {
        var service = new BrewLinkDiscoveryService(NullLogger<BrewLinkDiscoveryService>.Instance);

        var payload = JsonSerializer.Serialize(new
        {
            @event = "brewlink_pairing",
            gateway_id = "c44f331a8b20",
            name = "BrewLink Gateway",
            ip = "192.168.1.50",
            http_port = 80,
            claim_url = "http://192.168.1.50/api/pair/claim",
            ws_url = "ws://192.168.1.50/ws",
            remaining_sec = 58
        });

        service.IngestBeaconJson(payload).Should().BeTrue();

        var beacons = service.GetActiveBeacons();
        var beacon = beacons.Should().ContainSingle().Subject;
        beacon.GatewayId.Should().Be("C44F331A8B20");
        beacon.Name.Should().Be("BrewLink Gateway");
        beacon.Ip.Should().Be("192.168.1.50");
        beacon.HttpPort.Should().Be(80);
        beacon.Host.Should().Be("192.168.1.50");
        beacon.ClaimUrl.Should().Be("http://192.168.1.50/api/pair/claim");
        beacon.WsUrl.Should().Be("ws://192.168.1.50/ws");
        beacon.RemainingSec.Should().BeInRange(50, 58);
    }

    [Fact]
    public void IngestBeaconJson_WithTrailingNullBytesAndBom_ParsesSuccessfully()
    {
        var service = new BrewLinkDiscoveryService(NullLogger<BrewLinkDiscoveryService>.Instance);

        // Simulates ESP-IDF C sendto() including trailing '\0' terminator and fixed-buffer padding
        var payload = "\uFEFF{\"event\":\"brewlink_pairing\",\"gateway_id\":\"AABBCCDDEEFF\",\"name\":\"Garage BrewLink\",\"ip\":\"192.168.1.77\",\"http_port\":80,\"remaining_sec\":42}\0\0garbage_after_nul";

        service.IngestBeaconJson(payload).Should().BeTrue();

        var beacon = service.GetActiveBeacons().Should().ContainSingle().Subject;
        beacon.GatewayId.Should().Be("AABBCCDDEEFF");
        beacon.Ip.Should().Be("192.168.1.77");
        beacon.Name.Should().Be("Garage BrewLink");
    }

    [Fact]
    public void IngestBeaconJson_WithStringNumbersNullOptionalsAndIpv4MappedSenderIp_ParsesSuccessfully()
    {
        var service = new BrewLinkDiscoveryService(NullLogger<BrewLinkDiscoveryService>.Instance);

        var payload = """
            {
              "event": "brewlink_pairing",
              "gatewayId": "112233445566",
              "name": null,
              "ip": null,
              "http_port": "8080",
              "claim_url": null,
              "ws_url": null,
              "remaining_sec": "45"
            }
            """;

        service.IngestBeaconJson(payload, "::ffff:192.168.1.91").Should().BeTrue();

        var beacon = service.GetActiveBeacons().Should().ContainSingle().Subject;
        beacon.GatewayId.Should().Be("112233445566");
        beacon.Name.Should().Be("BrewLink (112233445566)");
        beacon.Ip.Should().Be("192.168.1.91");
        beacon.HttpPort.Should().Be(8080);
        beacon.Host.Should().Be("192.168.1.91:8080");
        beacon.ClaimUrl.Should().Be("http://192.168.1.91:8080/api/pair/claim");
        beacon.WsUrl.Should().Be("ws://192.168.1.91:8080/ws");
    }

    [Fact]
    public void IngestBeaconJson_WithZeroRemainingSec_RemovesExistingBeacon()
    {
        var service = new BrewLinkDiscoveryService(NullLogger<BrewLinkDiscoveryService>.Instance);

        var openPayload = """{"event":"brewlink_pairing","gateway_id":"C44F331A8B20","ip":"192.168.1.50","remaining_sec":30}""";
        var closedPayload = """{"event":"brewlink_pairing","gateway_id":"C44F331A8B20","ip":"192.168.1.50","remaining_sec":0}""";

        service.IngestBeaconJson(openPayload).Should().BeTrue();
        service.GetActiveBeacons().Should().ContainSingle();

        service.IngestBeaconJson(closedPayload).Should().BeFalse();
        service.GetActiveBeacons().Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoveryWorker_ReceivesRealUdpDatagrams_AndIngestsBeacons()
    {
        var service = new BrewLinkDiscoveryService(NullLogger<BrewLinkDiscoveryService>.Instance);
        var env = new StubHostEnvironment("Development");
        using var worker = new BrewLinkDiscoveryWorker(
            service,
            env,
            NullLogger<BrewLinkDiscoveryWorker>.Instance,
            udpPort: 0,
            runInTestingEnvironment: true,
            retryDelay: TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(cts.Token);

        try
        {
            var boundPort = await worker.WaitForListeningAsync(cts.Token);
            boundPort.Should().BeGreaterThan(0);
            worker.IsListening.Should().BeTrue();

            using var sender = new UdpClient(AddressFamily.InterNetwork);
            sender.EnableBroadcast = true;

            // Send an invalid datagram first to verify the listener survives malformed traffic
            var garbage = Encoding.UTF8.GetBytes("not-json");
            await sender.SendAsync(garbage, garbage.Length, new IPEndPoint(IPAddress.Loopback, boundPort));

            // Send a spec-compliant BrewLink beacon with trailing NUL byte (as emitted by C/ESP-IDF)
            var beaconJson = JsonSerializer.Serialize(new
            {
                @event = "brewlink_pairing",
                gateway_id = "C44F331A8B20",
                name = "ESP32 UDP Beacon",
                ip = "192.168.1.50",
                http_port = 80,
                claim_url = "http://192.168.1.50/api/pair/claim",
                ws_url = "ws://192.168.1.50/ws",
                remaining_sec = 58
            }) + "\0";
            var payloadBytes = Encoding.UTF8.GetBytes(beaconJson);
            await sender.SendAsync(payloadBytes, payloadBytes.Length, new IPEndPoint(IPAddress.Loopback, boundPort));

            // Poll until ingested
            var deadline = DateTime.UtcNow.AddSeconds(3);
            List<BrewLinkDiscoveredGatewayDto> beacons = [];
            while (DateTime.UtcNow < deadline)
            {
                beacons = service.GetActiveBeacons();
                if (beacons.Count > 0)
                {
                    break;
                }
                await Task.Delay(25, cts.Token);
            }

            var discovered = beacons.Should().ContainSingle().Subject;
            discovered.GatewayId.Should().Be("C44F331A8B20");
            discovered.Name.Should().Be("ESP32 UDP Beacon");
            discovered.Host.Should().Be("192.168.1.50");
            discovered.ClaimUrl.Should().Be("http://192.168.1.50/api/pair/claim");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("""{"gateway_id":"C44F331A8B20","ip":"192.168.1.50"}""")]
    [InlineData("""{"event":"","gateway_id":"C44F331A8B20","ip":"192.168.1.50"}""")]
    [InlineData("""{"event":"telemetry","gateway_id":"C44F331A8B20","ip":"192.168.1.50"}""")]
    public void IngestBeaconJson_WithoutBrewLinkPairingEvent_IsIgnored(string payload)
    {
        var service = new BrewLinkDiscoveryService(NullLogger<BrewLinkDiscoveryService>.Instance);

        service.IngestBeaconJson(payload, "192.168.1.50").Should().BeFalse();
        service.GetActiveBeacons().Should().BeEmpty();
    }

    [Fact]
    public void IngestBeaconJson_HonorsClaimUrlPortWhenHttpPortOmitted_AndRemovesByHost()
    {
        var service = new BrewLinkDiscoveryService(NullLogger<BrewLinkDiscoveryService>.Instance);

        var payload = """
            {
              "event": "brewlink_pairing",
              "gateway_id": "C44F331A8B20",
              "name": "SoftAP BrewLink",
              "ip": "192.168.4.1",
              "claim_url": "http://192.168.4.1:8080/api/pair/claim",
              "remaining_sec": 55
            }
            """;

        service.IngestBeaconJson(payload).Should().BeTrue();
        var beacon = service.GetActiveBeacons().Should().ContainSingle().Subject;
        beacon.HttpPort.Should().Be(8080);
        beacon.Host.Should().Be("192.168.4.1:8080");

        service.RemoveBeaconsByHost("http://192.168.4.1:8080");
        service.GetActiveBeacons().Should().BeEmpty();
    }

    private sealed class RecordingWebSocketManager : IBrewLinkWebSocketManager
    {
        public List<List<BrewLinkGateway>> ReconciledSnapshots { get; } = [];
        public bool IsGatewayConnected(Guid gatewayId) => false;
        public Task DisconnectAllAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReconcileGatewaysAsync(IEnumerable<BrewLinkGateway> gateways, CancellationToken ct = default)
        {
            ReconciledSnapshots.Add(gateways.ToList());
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PairGateway_FromDiscoveredBeacon_ImmediatelyReconcilesWebSocket_AndHandles403And500()
    {
        var dbOptions = new DbContextOptionsBuilder<BrewYouDbContext>()
            .UseInMemoryDatabase($"BrewLinkScanPairTest_{Guid.NewGuid():N}")
            .Options;
        await using var db = new BrewYouDbContext(dbOptions);

        var discovery = new BrewLinkDiscoveryService(NullLogger<BrewLinkDiscoveryService>.Instance);
        var wsManager = new RecordingWebSocketManager();
        var services = new ServiceCollection();
        services.AddSingleton<IBrewLinkWebSocketManager>(wsManager);
        await using var serviceProvider = services.BuildServiceProvider();

        var httpHandler = new FakeBrewLinkHttpMessageHandler();
        using var httpClient = new HttpClient(httpHandler);
        var dataProtection = new EphemeralDataProtectionProvider();

        var brewLinkService = new BrewLinkService(
            db,
            httpClient,
            dataProtection,
            NullLogger<BrewLinkService>.Instance,
            discoveryService: discovery,
            serviceProvider: serviceProvider);

        var beaconJson = JsonSerializer.Serialize(new
        {
            @event = "brewlink_pairing",
            gateway_id = "C44F331A8B20",
            name = "BrewLink Gateway",
            ip = "192.168.4.1",
            http_port = 80,
            claim_url = "http://192.168.4.1/api/pair/claim",
            ws_url = "ws://192.168.4.1/ws",
            remaining_sec = 58
        });

        // 1. 500 PAIRING_SAVE_FAILED keeps beacon active and returns retryable guidance
        discovery.IngestBeaconJson(beaconJson).Should().BeTrue();
        httpHandler.Handler = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("""{"error":"PAIRING_SAVE_FAILED"}""")
        };
        var saveFailed = await brewLinkService.PairGatewayAsync("user-1", new PairBrewLinkRequest("192.168.4.1", null, "BrewLink Gateway"));
        saveFailed.Succeeded.Should().BeFalse();
        saveFailed.ErrorMessage.Should().Contain("PAIRING_SAVE_FAILED");
        discovery.GetActiveBeacons().Should().ContainSingle();

        // 2. 403 PAIRING_WINDOW_CLOSED evicts stale beacon
        httpHandler.Handler = _ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"error":"PAIRING_WINDOW_CLOSED"}""")
        };
        var windowClosed = await brewLinkService.PairGatewayAsync("user-1", new PairBrewLinkRequest("192.168.4.1", null, "BrewLink Gateway"));
        windowClosed.Succeeded.Should().BeFalse();
        discovery.GetActiveBeacons().Should().BeEmpty();

        // 3. Successful code-free claim removes beacon and immediately reconciles WebSocket connectivity
        discovery.IngestBeaconJson(beaconJson).Should().BeTrue();
        httpHandler.RecordedRequests.Clear();
        httpHandler.Handler = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                status = "paired",
                gateway_id = "C44F331A8B20",
                client_name = "BrewYou-A4F2",
                api_token = "bl_8f3a9e2c4b1d6e7f8a0b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f",
                ws_url = "ws://192.168.4.1/ws",
                mqtt_broker_uri = "mqtt://192.168.1.100:1883",
                mqtt_topic_key = "tk_a1b2c3d4e5f6a7b8",
                mqtt_topic_prefix = "brewlink/tk_a1b2c3d4e5f6a7b8/"
            }))
        };

        var paired = await brewLinkService.PairGatewayAsync("user-1", new PairBrewLinkRequest("192.168.4.1", null, "BrewLink Gateway"));
        paired.Succeeded.Should().BeTrue();
        paired.Gateway!.GatewayId.Should().Be("C44F331A8B20");
        discovery.GetActiveBeacons().Should().BeEmpty();

        var claimReq = httpHandler.RecordedRequests.Should().ContainSingle().Subject;
        claimReq.Request.RequestUri!.ToString().Should().Be("http://192.168.4.1/api/pair/claim");
        Encoding.UTF8.GetByteCount(claimReq.Body!).Should().BeLessThan(256);
        claimReq.Body.Should().Be("{\"client_name\":\"BrewYou\"}");

        wsManager.ReconciledSnapshots.Should().ContainSingle();
        wsManager.ReconciledSnapshots[0].Should().ContainSingle(g =>
            g.GatewayId == "C44F331A8B20" &&
            g.Host == "192.168.4.1" &&
            g.ClientName == "BrewYou-A4F2");
    }
}