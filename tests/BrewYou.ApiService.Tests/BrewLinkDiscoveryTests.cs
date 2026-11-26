using BrewYou.ApiService.Services;
using FluentAssertions;
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
}