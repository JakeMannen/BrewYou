using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Globalization;
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

        // C/ESP-IDF UDP sendto() payloads may include trailing NUL bytes or fixed-buffer padding after '\0'.
        var nulIndex = jsonPayload.IndexOf('\0');
        var sanitized = (nulIndex >= 0 ? jsonPayload[..nulIndex] : jsonPayload)
            .Trim('\uFEFF', ' ', '\t', '\r', '\n');

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(sanitized);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (TryGetStringProperty(root, out var evt, "event") &&
                !string.IsNullOrWhiteSpace(evt) &&
                !string.Equals(evt.Trim(), "brewlink_pairing", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!TryGetStringProperty(root, out var gatewayId, "gateway_id", "gatewayId") ||
                string.IsNullOrWhiteSpace(gatewayId))
            {
                _logger.LogWarning("Ignored BrewLink discovery beacon missing gateway_id (sender {SenderIp}).", NormalizeIp(senderIp) ?? "unknown");
                return false;
            }

            var cleanGatewayId = gatewayId.Trim().ToUpperInvariant();

            var remainingSec = 60;
            if (TryGetPropertyIgnoreCase(root, out var remProp, "remaining_sec", "remainingSec") &&
                TryGetIntValue(remProp, out var parsedRem))
            {
                if (parsedRem <= 0)
                {
                    if (_beacons.TryRemove(cleanGatewayId, out _))
                    {
                        _logger.LogInformation("BrewLink pairing window closed for gateway {GatewayId}; removed discovery beacon.", cleanGatewayId);
                    }
                    return false;
                }

                remainingSec = Math.Clamp(parsedRem, 1, 120);
            }

            var name = TryGetStringProperty(root, out var rawName, "name") && !string.IsNullOrWhiteSpace(rawName)
                ? rawName.Trim()
                : $"BrewLink ({cleanGatewayId})";

            var ip = TryGetStringProperty(root, out var rawIp, "ip") ? NormalizeIp(rawIp) : null;
            if (string.IsNullOrWhiteSpace(ip))
            {
                ip = NormalizeIp(senderIp);
            }

            var httpPort = 80;
            if (TryGetPropertyIgnoreCase(root, out var portProp, "http_port", "httpPort") &&
                TryGetIntValue(portProp, out var parsedPort) &&
                parsedPort is > 0 and <= 65535)
            {
                httpPort = parsedPort;
            }

            var claimUrl = TryGetStringProperty(root, out var rawClaimUrl, "claim_url", "claimUrl")
                ? rawClaimUrl?.Trim()
                : null;
            if (string.IsNullOrWhiteSpace(ip) &&
                !string.IsNullOrWhiteSpace(claimUrl) &&
                Uri.TryCreate(claimUrl, UriKind.Absolute, out var claimUri))
            {
                ip = NormalizeIp(claimUri.Host);
                if (!claimUri.IsDefaultPort && claimUri.Port > 0)
                {
                    httpPort = claimUri.Port;
                }
            }

            if (string.IsNullOrWhiteSpace(ip))
            {
                _logger.LogWarning("Ignored BrewLink discovery beacon for gateway {GatewayId} because no IP address could be determined.", cleanGatewayId);
                return false;
            }

            var host = httpPort == 80 ? ip : $"{ip}:{httpPort}";
            if (string.IsNullOrWhiteSpace(claimUrl))
            {
                claimUrl = $"http://{host}/api/pair/claim";
            }

            var wsUrl = TryGetStringProperty(root, out var rawWsUrl, "ws_url", "wsUrl") && !string.IsNullOrWhiteSpace(rawWsUrl)
                ? rawWsUrl.Trim()
                : $"ws://{host}/ws";

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

            var isNewOrChanged = !_beacons.TryGetValue(cleanGatewayId, out var existing) ||
                                 !string.Equals(existing.Host, host, StringComparison.OrdinalIgnoreCase);

            _beacons[cleanGatewayId] = entry;

            if (isNewOrChanged)
            {
                _logger.LogInformation("Discovered BrewLink pairing beacon for gateway {GatewayId} at {Host} ({RemainingSec}s remaining)",
                    cleanGatewayId, host, remainingSec);
            }
            else
            {
                _logger.LogDebug("Refreshed BrewLink pairing beacon for gateway {GatewayId} at {Host} ({RemainingSec}s remaining)",
                    cleanGatewayId, host, remainingSec);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ignored malformed BrewLink discovery beacon payload from {SenderIp}", NormalizeIp(senderIp) ?? "unknown");
            return false;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        foreach (var prop in root.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetStringProperty(JsonElement root, out string? value, params string[] names)
    {
        if (TryGetPropertyIgnoreCase(root, out var prop, names) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString();
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetIntValue(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetInt32(out value))
            {
                return true;
            }

            if (element.TryGetDouble(out var d) && double.IsFinite(d) && d >= int.MinValue && d <= int.MaxValue)
            {
                value = (int)Math.Round(d, MidpointRounding.AwayFromZero);
                return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString()?.Trim();
            if (int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static string? NormalizeIp(string? rawIp)
    {
        if (string.IsNullOrWhiteSpace(rawIp))
        {
            return null;
        }

        var trimmed = rawIp.Trim();
        if (IPAddress.TryParse(trimmed, out var parsed))
        {
            return parsed.IsIPv4MappedToIPv6
                ? parsed.MapToIPv4().ToString()
                : parsed.ToString();
        }

        return trimmed;
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
    private const int SioUdpConnReset = -1744830452;

    private readonly IBrewLinkDiscoveryService _discoveryService;
    private readonly IHostEnvironment _env;
    private readonly ILogger<BrewLinkDiscoveryWorker> _logger;
    private readonly int _udpPort;
    private readonly bool _runInTestingEnvironment;
    private readonly TimeSpan _retryDelay;
    private readonly TaskCompletionSource<int> _listeningTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int BoundPort { get; private set; }
    public bool IsListening { get; private set; }

    [ActivatorUtilitiesConstructor]
    public BrewLinkDiscoveryWorker(
        IBrewLinkDiscoveryService discoveryService,
        IHostEnvironment env,
        ILogger<BrewLinkDiscoveryWorker> logger)
        : this(discoveryService, env, logger, DiscoveryUdpPort, runInTestingEnvironment: false, retryDelay: null)
    {
    }

    public BrewLinkDiscoveryWorker(
        IBrewLinkDiscoveryService discoveryService,
        IHostEnvironment env,
        ILogger<BrewLinkDiscoveryWorker> logger,
        int udpPort,
        bool runInTestingEnvironment = false,
        TimeSpan? retryDelay = null)
    {
        _discoveryService = discoveryService;
        _env = env;
        _logger = logger;
        _udpPort = udpPort;
        _runInTestingEnvironment = runInTestingEnvironment;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);
    }

    public Task<int> WaitForListeningAsync(CancellationToken cancellationToken = default) =>
        _listeningTcs.Task.WaitAsync(cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_runInTestingEnvironment && _env.IsEnvironment("Testing"))
        {
            return;
        }

        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            UdpClient? udpClient = null;
            try
            {
                udpClient = new UdpClient(AddressFamily.InterNetwork);
                udpClient.EnableBroadcast = true;
                udpClient.Client.ExclusiveAddressUse = false;
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        udpClient.Client.IOControl((IOControlCode)SioUdpConnReset, [0, 0, 0, 0], null);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not configure SIO_UDP_CONNRESET on BrewLink discovery socket.");
                    }
                }

                udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _udpPort));
                BoundPort = ((IPEndPoint)udpClient.Client.LocalEndPoint!).Port;
                IsListening = true;
                _listeningTcs.TrySetResult(BoundPort);

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

                _logger.LogInformation("BrewLink UDP Broadcast Discovery listener started on 0.0.0.0:{Port}", BoundPort);

                while (!stoppingToken.IsCancellationRequested)
                {
                    UdpReceiveResult result;
                    try
                    {
                        result = await udpClient.ReceiveAsync(stoppingToken);
                    }
                    catch (SocketException ex) when (!stoppingToken.IsCancellationRequested &&
                                                     ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize or SocketError.Interrupted)
                    {
                        _logger.LogDebug(ex, "Ignored transient UDP socket error on BrewLink discovery listener.");
                        continue;
                    }

                    if (result.Buffer.Length is > 0 and <= 4096)
                    {
                        var json = Encoding.UTF8.GetString(result.Buffer);
                        var remoteAddress = result.RemoteEndPoint.Address;
                        var senderIp = remoteAddress.IsIPv4MappedToIPv6
                            ? remoteAddress.MapToIPv4().ToString()
                            : remoteAddress.ToString();
                        _discoveryService.IngestBeaconJson(json, senderIp);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException ex) when (!stoppingToken.IsCancellationRequested)
            {
                IsListening = false;
                _logger.LogWarning(ex,
                    "BrewLink UDP discovery listener failed on port {Port}. Retrying in {RetrySeconds}s. MQTT and direct HTTP pairing remain available.",
                    _udpPort, _retryDelay.TotalSeconds);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                IsListening = false;
                _logger.LogWarning(ex,
                    "BrewLink UDP discovery listener stopped unexpectedly. Retrying in {RetrySeconds}s.",
                    _retryDelay.TotalSeconds);
            }
            finally
            {
                IsListening = false;
                udpClient?.Dispose();
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_retryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}