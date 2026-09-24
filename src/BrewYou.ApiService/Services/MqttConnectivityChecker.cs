using System.Net;
using System.Net.Sockets;

namespace BrewYou.ApiService.Services;

public interface IMqttConnectivityChecker
{
    Task<bool> CanConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default);
}

public class MqttConnectivityChecker : IMqttConnectivityChecker
{
    public async Task<bool> CanConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 255 || port < 1 || port > 65535)
        {
            return false;
        }

        try
        {
            var trimmedHost = host.Trim();

            // SSRF Defense: Resolve DNS and validate IP addresses
            var ipAddresses = await Dns.GetHostAddressesAsync(trimmedHost, cancellationToken);
            if (ipAddresses.Length == 0 || ipAddresses.Any(IsForbiddenAddress))
            {
                return false;
            }

            // Dual-stack: Try connecting to each resolved address until one succeeds (e.g. IPv4 fallback when localhost resolves to ::1 first)
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

    private static bool IsForbiddenAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            // Link-local / Cloud metadata: 169.254.0.0/16
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            // Unspecified / 0.0.0.0
            if (bytes[0] == 0)
            {
                return true;
            }

            // Multicast and Broadcast (>= 224.0.0.0)
            if (bytes[0] >= 224)
            {
                return true;
            }
        }

        return false;
    }
}