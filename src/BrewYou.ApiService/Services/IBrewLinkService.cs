using BrewYou.ApiService.Data.Entities;
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
    JsonElement? Params = null,
    int? Minutes = null,
    int? Seconds = null
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
    int? TimerRemainingSec = null,
    decimal? BoilTempC = null,
    bool? HeatActive = null,
    bool? AutoMode = null,
    bool? RampActive = null,
    bool? TimerActive = null,
    bool? TimerPaused = null,
    int? TimerTotalSec = null,
    string? StepName = null,
    bool? InteractionRequired = null,
    int? InteractionCode = null,
    string? InteractionMessage = null,
    bool? DelayedHeat = null,
    bool? Interrupted = null,
    bool? ManualPower = null,
    bool? SpargeAlert = null,
    int? ErrorCode = null
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
    BrewLinkEquipmentStateDto? State = null,
    Guid? ExistingEquipmentId = null,
    string? ExistingEquipmentName = null,
    BrewYou.ApiService.Data.Entities.EquipmentType MappedEquipmentType = BrewYou.ApiService.Data.Entities.EquipmentType.Boiler,
    EquipmentSubtype MappedSubtype = EquipmentSubtype.AllInOne,
    decimal RecommendedCapacityLiters = 30.0m
);

public record ProvisionBrewLinkDeviceItem(
    int Slot,
    string? Mac = null,
    string? Name = null,
    string? Model = null,
    string? EquipmentType = null
);

public record ProvisionBrewLinkEquipmentRequest(
    Guid BrewerySetupId,
    List<ProvisionBrewLinkDeviceItem> Devices
);

public record ProvisionBrewLinkEquipmentResultDto(
    List<EquipmentDto> CreatedEquipment,
    List<Guid> SkippedExistingIds
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

    Task<(bool Succeeded, bool Conflict, string? ErrorMessage)> DeleteGatewayAsync(
        string userId,
        Guid id,
        bool force = false,
        CancellationToken ct = default);

    Task<(bool Succeeded, List<BrewLinkEquipmentDeviceDto>? Equipment, string? ErrorMessage)> GetGatewayEquipmentAsync(
        string userId,
        Guid gatewayId,
        Guid? setupId = null,
        CancellationToken ct = default);

    Task<(BrewLinkSendResult Result, ProvisionBrewLinkEquipmentResultDto? Data, string? ErrorMessage)> ProvisionGatewayEquipmentAsync(
        string userId,
        Guid gatewayId,
        ProvisionBrewLinkEquipmentRequest request,
        CancellationToken ct = default);

    Task RevokeGatewayTokenBestEffortAsync(BrewLinkGateway gateway, CancellationToken ct = default);

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