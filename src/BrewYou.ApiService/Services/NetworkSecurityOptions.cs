namespace BrewYou.ApiService.Services;

public class NetworkSecurityOptions
{
    public const string SectionName = "NetworkSecurity";

    /// <summary>
    /// Allows outbound connections (e.g., MQTT broker checks and connections) to RFC 1918 / IPv6 ULA private networks.
    /// Defaults to false for safe multi-tenant deployments; enable explicitly for self-hosted LAN installations.
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>
    /// Allows outbound connections to loopback addresses (127.0.0.0/8, ::1, localhost).
    /// Defaults to false and must be explicitly opted in.
    /// </summary>
    public bool AllowLoopback { get; set; }
}