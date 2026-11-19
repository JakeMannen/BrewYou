using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;

namespace BrewYou.ApiService.Services;

public interface IMqttConnectivityChecker
{
    Task<bool> CanConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default);
    Task<IPAddress[]> ResolveValidatedAddressesAsync(string host, CancellationToken cancellationToken = default);
}

public class MqttConnectivityChecker : IMqttConnectivityChecker
{
    private static readonly HashSet<string> BlockedMetadataHostnames = new(StringComparer.OrdinalIgnoreCase)
    {
        "169.254.169.254",
        "metadata.google.internal",
        "100.100.100.200",
        "instance-data"
    };

    private readonly NetworkSecurityOptions _options;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>>? _dnsResolver;

    public MqttConnectivityChecker(
        IOptions<NetworkSecurityOptions>? options = null,
        Func<string, CancellationToken, Task<IPAddress[]>>? dnsResolver = null)
    {
        _options = options?.Value ?? new NetworkSecurityOptions();
        _dnsResolver = dnsResolver;
    }

    public async Task<IPAddress[]> ResolveValidatedAddressesAsync(string host, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 255)
        {
            return [];
        }

        var trimmedHost = host.Trim();
        var lowerHost = trimmedHost.ToLowerInvariant();
        var unbracketed = lowerHost.Trim('[', ']');

        if (BlockedMetadataHostnames.Contains(lowerHost) || BlockedMetadataHostnames.Contains(unbracketed))
        {
            return [];
        }

        if (!_options.AllowLoopback &&
            (string.Equals(unbracketed, "localhost", StringComparison.OrdinalIgnoreCase) ||
             unbracketed.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        try
        {
            IPAddress[] ipAddresses;
            if (IPAddress.TryParse(unbracketed, out var literalIp))
            {
                ipAddresses = [literalIp];
            }
            else
            {
                ipAddresses = _dnsResolver != null
                    ? await _dnsResolver(trimmedHost, cancellationToken)
                    : await Dns.GetHostAddressesAsync(trimmedHost, cancellationToken);
            }

            if (ipAddresses.Length == 0)
            {
                return [];
            }

            foreach (var ip in ipAddresses)
            {
                if (IsForbiddenMqttAddress(ip))
                {
                    return [];
                }
            }

            return ipAddresses;
        }
        catch
        {
            return [];
        }
    }

    public async Task<bool> CanConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 255 || port < 1 || port > 65535)
        {
            return false;
        }

        try
        {
            // SSRF Defense: Resolve DNS once and validate every IP against NetworkSecurityOptions
            var ipAddresses = await ResolveValidatedAddressesAsync(host, cancellationToken);
            if (ipAddresses.Length == 0)
            {
                return false;
            }

            // Connect directly to validated IP(s) to prevent DNS rebinding
            foreach (var ip in ipAddresses)
            {
                try
                {
                    using var client = new TcpClient(ip.AddressFamily);
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(timeout);

                    await client.ConnectAsync(ip, port, cts.Token);
                    if (client.Connected)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Continue to next resolved address
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool IsForbiddenMqttAddress(IPAddress ip)
    {
        var normalized = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

        if (IPAddress.IsLoopback(normalized))
        {
            return !_options.AllowLoopback;
        }

        return NetworkSecurityValidator.IsForbiddenAddress(normalized, _options.AllowPrivateNetworks);
    }
}