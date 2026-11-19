using System.Net;
using System.Net.Sockets;

namespace BrewYou.ApiService.Services;

public static class NetworkSecurityValidator
{
    private static readonly HashSet<string> BlockedHostnames = new(StringComparer.OrdinalIgnoreCase)
    {
        "169.254.169.254",
        "metadata.google.internal",
        "100.100.100.200",
        "instance-data",
        "localhost"
    };

    /// <summary>
    /// Validates whether a URI or host string is safe from SSRF exploits.
    /// By default, private/internal IP ranges (RFC 1918, ULA, Loopback, Link-Local) and cloud metadata are forbidden.
    /// Set allowPrivateNetworks to true only if explicitly configured for local intranet self-hosting.
    /// </summary>
    public static async Task<(bool IsValid, string? ErrorMessage, Uri? ValidatedUri)> ValidateUrlAsync(
        string urlString,
        bool allowPrivateNetworks = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(urlString))
        {
            return (false, "URL is required.", null);
        }

        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var uri))
        {
            return (false, "Invalid URL format.", null);
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return (false, "Only HTTP and HTTPS URLs are allowed.", null);
        }

        var host = uri.Host.Trim().ToLowerInvariant();

        if (IsBlockedHostname(host))
        {
            return (false, "Access to metadata and local loopback endpoints is prohibited.", null);
        }

        // Try IP parsing first
        if (IPAddress.TryParse(host, out var ip))
        {
            if (IsForbiddenAddress(ip, allowPrivateNetworks))
            {
                return (false, "Target IP address is in a prohibited or private range.", null);
            }
            return (true, null, uri);
        }

        // Local mDNS/LAN domains are accepted as private networks without requiring unicast DNS
        if (allowPrivateNetworks && (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                                     host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) ||
                                     host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase)))
        {
            return (true, null, uri);
        }

        // Host is a domain name - resolve DNS to prevent DNS rebinding to internal addresses
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            if (addresses.Length == 0)
            {
                if (allowPrivateNetworks)
                {
                    return (true, null, uri);
                }
                return (false, "Host could not be resolved.", null);
            }

            foreach (var resolvedIp in addresses)
            {
                if (IsForbiddenAddress(resolvedIp, allowPrivateNetworks))
                {
                    return (false, "Target domain resolves to a prohibited internal or private network address.", null);
                }
            }
        }
        catch (Exception)
        {
            if (allowPrivateNetworks)
            {
                return (true, null, uri);
            }
            return (false, "Host could not be resolved.", null);
        }

        return (true, null, uri);
    }

    /// <summary>
    /// Synchronous preliminary format and blocked-host check.
    /// </summary>
    public static (bool IsValid, string? ErrorMessage) ValidateUrlFormat(string urlString)
    {
        if (string.IsNullOrWhiteSpace(urlString))
        {
            return (false, "URL is required.");
        }

        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var uri))
        {
            return (false, "Invalid URL format.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return (false, "Only HTTP and HTTPS URLs are allowed.");
        }

        var host = uri.Host.Trim().ToLowerInvariant();
        if (IsBlockedHostname(host))
        {
            return (false, "Access to metadata and local loopback endpoints is prohibited.");
        }

        if (IPAddress.TryParse(host, out var ip) && IsForbiddenAddress(ip, allowPrivateNetworks: false))
        {
            return (false, "Access to private or metadata network addresses is prohibited.");
        }

        return (true, null);
    }

    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> CreateSsrfSafeConnectCallback(
        bool allowPrivateNetworks = false,
        bool allowLoopback = false)
    {
        return (context, cancellationToken) => ConnectToValidatedHostAsync(
            context.DnsEndPoint.Host,
            context.DnsEndPoint.Port,
            allowPrivateNetworks,
            allowLoopback,
            dnsResolver: null,
            cancellationToken);
    }

    public static async ValueTask<Stream> ConnectToValidatedHostAsync(
        string hostInput,
        int port,
        bool allowPrivateNetworks = false,
        bool allowLoopback = false,
        Func<string, CancellationToken, Task<IPAddress[]>>? dnsResolver = null,
        CancellationToken cancellationToken = default)
    {
        var host = hostInput.Trim().ToLowerInvariant();
        if (IsBlockedHostname(host))
        {
            throw new HttpRequestException("Access to metadata and local loopback endpoints is prohibited.");
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literalIp))
        {
            addresses = [literalIp];
        }
        else
        {
            addresses = dnsResolver != null
                ? await dnsResolver(host, cancellationToken)
                : await Dns.GetHostAddressesAsync(host, cancellationToken);
            if (addresses.Length == 0)
            {
                throw new HttpRequestException("Host could not be resolved.");
            }
        }

        foreach (var ip in addresses)
        {
            if ((!allowLoopback && IPAddress.IsLoopback(ip)) || IsForbiddenAddress(ip, allowPrivateNetworks))
            {
                throw new HttpRequestException("Target IP address is in a prohibited or private range.");
            }
        }

        Exception? lastException = null;
        foreach (var ip in addresses)
        {
            var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(ip, port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                socket.Dispose();
                lastException = ex;
            }
        }

        throw lastException ?? new HttpRequestException("Failed to connect to target host.");
    }

    private static bool IsBlockedHostname(string host)
    {
        var unbracketed = host.Trim('[', ']');
        return BlockedHostnames.Contains(host) ||
               BlockedHostnames.Contains(unbracketed) ||
               unbracketed.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsForbiddenAddress(IPAddress address, bool allowPrivateNetworks = false)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        // Cloud metadata & Link-Local IPv6
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            if (IPAddress.IsLoopback(address))
            {
                return !allowPrivateNetworks;
            }

            // 0.0.0.0/8 (Current network)
            if (bytes[0] == 0)
            {
                return true;
            }

            // 169.254.0.0/16 (Link-local / AWS & GCP cloud metadata)
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            // 100.64.0.0/10 (Carrier-grade NAT, includes Alibaba/Tencent metadata)
            if (bytes[0] == 100 && (bytes[1] >= 64 && bytes[1] <= 127))
            {
                return true;
            }

            // 224.0.0.0/4 (Multicast) and 240.0.0.0/4 (Reserved / Broadcast)
            if (bytes[0] >= 224)
            {
                return true;
            }

            // If private networks are strictly forbidden (default for SSRF protection):
            if (!allowPrivateNetworks)
            {
                // 10.0.0.0/8 (Private-Use)
                if (bytes[0] == 10)
                {
                    return true;
                }

                // 172.16.0.0/12 (Private-Use)
                if (bytes[0] == 172 && (bytes[1] >= 16 && bytes[1] <= 31))
                {
                    return true;
                }

                // 192.168.0.0/16 (Private-Use)
                if (bytes[0] == 192 && bytes[1] == 168)
                {
                    return true;
                }

                // 127.0.0.0/8 (Loopback)
                if (bytes[0] == 127)
                {
                    return true;
                }
            }
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // ::/128 (Unspecified address)
            if (address.Equals(IPAddress.IPv6Any) || bytes.All(b => b == 0))
            {
                return true;
            }

            // ::1/128 (Loopback)
            if (IPAddress.IsLoopback(address))
            {
                return !allowPrivateNetworks;
            }

            // ::/96 (IPv4-compatible IPv6 - deprecated RFC 4291)
            if (bytes.Take(12).All(b => b == 0))
            {
                return true;
            }

            // 64:ff9b::/96 (NAT64 well-known prefix, RFC 6052) and 64:ff9b:1::/48 (NAT64 local-use, RFC 8215)
            if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B &&
                ((bytes[4..12].All(b => b == 0)) || (bytes[4] == 0x00 && bytes[5] == 0x01)))
            {
                return true;
            }

            // 2002::/16 (6to4 tunneling, RFC 3056)
            if (bytes[0] == 0x20 && bytes[1] == 0x02)
            {
                return true;
            }

            // fec0::/10 (Deprecated IPv6 site-local)
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0xC0)
            {
                return true;
            }

            // IPv6 Unique Local Addresses (fc00::/7)
            if ((bytes[0] & 0xFE) == 0xFC && !allowPrivateNetworks)
            {
                return true;
            }

            // IPv6 Multicast (ff00::/8)
            if (bytes[0] == 0xFF)
            {
                return true;
            }
        }

        return false;
    }
}