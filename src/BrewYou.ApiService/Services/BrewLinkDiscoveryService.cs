using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace BrewYou.ApiService.Services;

public record BrewLinkDiscoveredGatewayDto(
    string GatewayId,
    string Name,
    string Ip,
    int HttpPort,
    string Host,
    string ClaimUrl,
    string? WsUrl,
    int RemainingSec,
    DateTime DiscoveredAtUtc,
    bool AlreadyPaired = false
);

public interface IBrewLinkDiscoveryService
{
    bool IngestBeaconJson(string jsonPayload, string? senderIp = null);
    List<BrewLinkDiscoveredGatewayDto> GetActiveBeacons(ISet<string>? pairedGatewayIds = null);
    void RemoveBeacon(string gatewayId);
    void Clear();
}

public class BrewLinkDiscoveryService : IBrewLinkDiscoveryService
{
    private sealed record DiscoveredBeaconEntry(
        string GatewayId,
        string Name,
        string Ip,
        int HttpPort,
        string Host,
        string ClaimUrl,
        string? WsUrl,
        DateTime DiscoveredAtUtc,
        DateTime ExpiresAtUtc
    );

    private readonly ConcurrentDictionary<string, DiscoveredBeaconEntry> _beacons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<BrewLinkDiscoveryService> _logger;

    public BrewLinkDiscoveryService(ILogger<BrewLinkDiscoveryService> logger)
    {
        _logger = logger;
    }

    public bool IngestBeaconJson(string jsonPayload, string? senderIp = null)
    {
        if (string.IsNullOrWhiteSpace(jsonPayload) || jsonPayload.Length > 4096)
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonPayload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (root.TryGetProperty("event", out var eventProp))
            {
                var evt = eventProp.GetString();
                if (!string.IsNullOrWhiteSpace(evt) &&
                    !string.Equals(evt, "brewlink_pairing", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            var gatewayId = root.TryGetProperty("gateway_id", out var gwProp) ? gwProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(gatewayId))
            {
                return false;
            }

            var cleanGatewayId = gatewayId.Trim().ToUpperInvariant();
            var name = root.TryGetProperty("name", out var nameProp) && !string.IsNullOrWhiteSpace(nameProp.GetString())
                ? nameProp.GetString()!.Trim()
                : $"BrewLink ({cleanGatewayId})";

            var ip = root.TryGetProperty("ip", out var ipProp) ? ipProp.GetString()?.Trim() : null;
            if (string.IsNullOrWhiteSpace(ip))
            {
                ip = senderIp?.Trim();
            }

            var httpPort = 80;
            if (root.TryGetProperty("http_port", out var portProp) && portProp.TryGetInt32(out var parsedPort) && parsedPort is > 0 and <= 65535)
            {
                httpPort = parsedPort;
            }

            var claimUrl = root.TryGetProperty("claim_url", out var claimProp) ? claimProp.GetString()?.Trim() : null;
            if (string.IsNullOrWhiteSpace(ip) && !string.IsNullOrWhiteSpace(claimUrl) && Uri.TryCreate(claimUrl, UriKind.Absolute, out var claimUri))
            {
                ip = claimUri.Host;
                if (!claimUri.IsDefaultPort && claimUri.Port > 0)
                {
                    httpPort = claimUri.Port;
                }
            }

            if (string.IsNullOrWhiteSpace(ip))
            {
                return false;
            }

            var host = httpPort == 80 ? ip : $"{ip}:{httpPort}";
            if (string.IsNullOrWhiteSpace(claimUrl))
            {
                claimUrl = $"http://{host}/api/pair/claim";
            }

            var wsUrl = root.TryGetProperty("ws_url", out var wsProp) ? wsProp.GetString()?.Trim() : $"ws://{host}/ws";

            var remainingSec = 60;
            if (root.TryGetProperty("remaining_sec", out var remProp) && remProp.TryGetInt32(out var parsedRem))
            {
                remainingSec = Math.Clamp(parsedRem, 2, 120);
            }

            var now = DateTime.UtcNow;
            var entry = new DiscoveredBeaconEntry(
                GatewayId: cleanGatewayId,
                Name: name,
                Ip: ip,
                HttpPort: httpPort,
                Host: host,
                ClaimUrl: claimUrl,
                WsUrl: wsUrl,
                DiscoveredAtUtc: now,
                ExpiresAtUtc: now.AddSeconds(remainingSec)
            );

            _beacons[cleanGatewayId] = entry;
            _logger.LogDebug("Ingested BrewLink pairing beacon for gateway {GatewayId} at {Host} ({RemainingSec}s remaining)",
                cleanGatewayId, host, remainingSec);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ignored malformed BrewLink discovery beacon payload");
            return false;
        }
    }

    public List<BrewLinkDiscoveredGatewayDto> GetActiveBeacons(ISet<string>? pairedGatewayIds = null)
    {
        var now = DateTime.UtcNow;
        var active = new List<BrewLinkDiscoveredGatewayDto>();

        foreach (var kvp in _beacons)
        {
            if (kvp.Value.ExpiresAtUtc <= now)
            {
                _beacons.TryRemove(kvp.Key, out _);
                continue;
            }

            var remainingSec = (int)Math.Max(1, Math.Ceiling((kvp.Value.ExpiresAtUtc - now).TotalSeconds));
            var isPaired = pairedGatewayIds != null && pairedGatewayIds.Contains(kvp.Value.GatewayId);

            active.Add(new BrewLinkDiscoveredGatewayDto(
                GatewayId: kvp.Value.GatewayId,
                Name: kvp.Value.Name,
                Ip: kvp.Value.Ip,
                HttpPort: kvp.Value.HttpPort,
                Host: kvp.Value.Host,
                ClaimUrl: kvp.Value.ClaimUrl,
                WsUrl: kvp.Value.WsUrl,
                RemainingSec: remainingSec,
                DiscoveredAtUtc: kvp.Value.DiscoveredAtUtc,
                AlreadyPaired: isPaired
            ));
        }

        return active
            .OrderByDescending(b => b.RemainingSec)
            .ThenBy(b => b.Name)
            .ToList();
    }

    public void RemoveBeacon(string gatewayId)
    {
        if (!string.IsNullOrWhiteSpace(gatewayId))
        {
            _beacons.TryRemove(gatewayId.Trim().ToUpperInvariant(), out _);
        }
    }

    public void Clear()
    {
        _beacons.Clear();
    }
}

public class BrewLinkDiscoveryWorker : BackgroundService
{
    public const int DiscoveryUdpPort = 53540;

    private readonly IBrewLinkDiscoveryService _discoveryService;
    private readonly IHostEnvironment _env;
    private readonly ILogger<BrewLinkDiscoveryWorker> _logger;

    public BrewLinkDiscoveryWorker(
        IBrewLinkDiscoveryService discoveryService,
        IHostEnvironment env,
        ILogger<BrewLinkDiscoveryWorker> logger)
    {
        _discoveryService = discoveryService;
        _env = env;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_env.IsEnvironment("Testing"))
        {
            return;
        }

        await Task.Yield();

        UdpClient? udpClient = null;
        try
        {
            udpClient = new UdpClient();
            udpClient.Client.ExclusiveAddressUse = false;
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryUdpPort));

            using var ctr = stoppingToken.Register(() =>
            {
                try
                {
                    udpClient.Close();
                }
                catch
                {
                    // Ignore dispose race
                }
            });

            _logger.LogInformation("BrewLink UDP Broadcast Discovery listener started on 0.0.0.0:{Port}", DiscoveryUdpPort);

            while (!stoppingToken.IsCancellationRequested)
            {
                var result = await udpClient.ReceiveAsync(stoppingToken);
                if (result.Buffer.Length is > 0 and <= 4096)
                {
                    var json = Encoding.UTF8.GetString(result.Buffer);
                    var senderIp = result.RemoteEndPoint.Address.ToString();
                    _discoveryService.IngestBeaconJson(json, senderIp);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown when socket closed by cancellation token registration
        }
        catch (SocketException ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not bind BrewLink UDP discovery listener on port {Port}. MQTT and direct HTTP pairing remain available.", DiscoveryUdpPort);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "BrewLink UDP discovery listener stopped unexpectedly.");
        }
        finally
        {
            udpClient?.Dispose();
        }
    }
}