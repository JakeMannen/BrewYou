namespace BrewYou.ApiService.Data.Entities;

public class BrewLinkGateway
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }

    /// <summary>
    /// User-friendly label for this gateway, e.g. "Garage BrewLink".
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// IP address or hostname, e.g. "192.168.1.50" or "brewlink.local".
    /// </summary>
    public required string Host { get; set; }

    /// <summary>
    /// Hardware gateway identifier reported during pairing, e.g. "C44F331A8B20".
    /// </summary>
    public required string GatewayId { get; set; }

    /// <summary>
    /// Encrypted API bearer token for HTTP and WebSocket authentication.
    /// </summary>
    public required string ApiToken { get; set; }

    /// <summary>
    /// Encrypted MQTT topic key for MQTT root prefix isolation.
    /// </summary>
    public string? MqttTopicKey { get; set; }

    /// <summary>
    /// Unique client name assigned by the BrewLink gateway during pairing (e.g. "BrewYou-A4F2"),
    /// used when revoking credentials via POST /api/pair/delete.
    /// </summary>
    public string? ClientName { get; set; }

    public DateTime PairedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}