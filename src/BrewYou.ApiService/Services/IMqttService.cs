namespace BrewYou.ApiService.Services;

public record MqttStatusInfo(
    bool Configured,
    bool Connected,
    string? Host = null,
    int? Port = null,
    string? Error = null,
    DateTime? LastConnectedAt = null
);

public interface IMqttService
{
    bool IsConnected { get; }
    string? ConnectedHost { get; }
    int? ConnectedPort { get; }
    DateTime? LastConnectedAt { get; }

    Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectUserAsync(string userId, CancellationToken cancellationToken = default);
    Task RefreshSubscriptionsAsync(CancellationToken cancellationToken = default);
    MqttStatusInfo GetStatus(string userId, string? host = null, int? port = null);
    MqttStatusInfo GetStatus(string? host = null, int? port = null);
}