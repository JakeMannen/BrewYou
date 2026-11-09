using System.Text.Json;

namespace BrewYou.ApiService.Services;

public record BrewLinkGatewayDto(
    Guid Id,
    string Name,
    string Host,
    string GatewayId,
    DateTime PairedAt,
    DateTime UpdatedAt
);

public record StartBrewLinkPairingRequest(string Host);

public record PairBrewLinkRequest(string Host, string? Pin = null, string? Name = null);

public record UpdateBrewLinkGatewayRequest(string Name, string? Host = null);

public record SendBrewLinkRecipeRequest(Guid? RecipeId, Guid? BatchId);

public record BrewLinkCommandResultDto(
    string Status,
    string ReqId,
    string GatewayId,
    string TargetMac,
    string Message,
    string? LatestMetricsJson = null
);

public record BrewLinkControlRequest(
    string Action,
    bool? State = null,
    decimal? PowerPct = null,
    decimal? TempC = null,
    int? DurationMin = null,
    JsonElement? Params = null
);

public record BrewLinkSyncResultDto(
    string Status,
    bool Synced,
    decimal? TemperatureC,
    string? MetricsJson,
    Guid? EquipmentId = null,
    EquipmentDto? Equipment = null
);

public record BrewLinkEquipmentCapabilitiesDto(
    bool RecipeUpload = false,
    bool MashProfile = false,
    bool FermentationProfile = false,
    bool PumpControl = false,
    bool HeatControl = false
);

public record BrewLinkEquipmentStateDto(
    decimal? TempC = null,
    decimal? TargetTempC = null,
    decimal? HeatPct = null,
    bool? PumpOn = null,
    int? StepIndex = null,
    int? TimerRemainingSec = null
);

public record BrewLinkEquipmentDeviceDto(
    int Slot,
    bool Paired,
    bool Connected,
    string Name,
    string? Model,
    string EquipmentType,
    string Mac,
    int? Rssi,
    BrewLinkEquipmentCapabilitiesDto Capabilities,
    BrewLinkEquipmentStateDto? State = null
);

public enum BrewLinkSendResult
{
    Success,
    EquipmentDisconnected,
    Unauthorized,
    NotFound,
    Forbidden,
    BadRequest,
    GatewayError
}

public interface IBrewLinkService
{
    Task<List<BrewLinkGatewayDto>> ListGatewaysAsync(string userId, CancellationToken ct = default);

    Task<List<BrewLinkDiscoveredGatewayDto>> DiscoverGatewaysAsync(string userId, CancellationToken ct = default);

    Task<(bool Succeeded, string? ErrorMessage)> StartPairingWindowAsync(
        string userId,
        StartBrewLinkPairingRequest request,
        CancellationToken ct = default);

    Task<(bool Succeeded, BrewLinkGatewayDto? Gateway, string? ErrorMessage)> PairGatewayAsync(
        string userId,
        PairBrewLinkRequest request,
        CancellationToken ct = default);

    Task<(bool Succeeded, BrewLinkGatewayDto? Gateway, string? ErrorMessage)> UpdateGatewayAsync(
        string userId,
        Guid id,
        UpdateBrewLinkGatewayRequest request,
        CancellationToken ct = default);

    Task<(bool Succeeded, string? ErrorMessage)> DeleteGatewayAsync(
        string userId,
        Guid id,
        CancellationToken ct = default);

    Task<(bool Succeeded, List<BrewLinkEquipmentDeviceDto>? Equipment, string? ErrorMessage)> GetGatewayEquipmentAsync(
        string userId,
        Guid gatewayId,
        CancellationToken ct = default);

    Task<(BrewLinkSendResult Result, BrewLinkCommandResultDto? CommandResult, string? ErrorMessage)> SendRecipeToEquipmentAsync(
        string userId,
        Guid equipmentId,
        SendBrewLinkRecipeRequest request,
        CancellationToken ct = default);

    Task<(BrewLinkSendResult Result, BrewLinkCommandResultDto? CommandResult, string? ErrorMessage)> SendControlCommandAsync(
        string userId,
        Guid equipmentId,
        BrewLinkControlRequest request,
        CancellationToken ct = default);

    Task<(BrewLinkSendResult Result, BrewLinkSyncResultDto? SyncResult, string? ErrorMessage)> SyncEquipmentStateAsync(
        string userId,
        Guid equipmentId,
        CancellationToken ct = default);
}