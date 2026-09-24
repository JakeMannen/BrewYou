using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;

namespace BrewYou.ApiService.Services;

public record CreateEquipmentRequest(
    Guid BrewerySetupId,
    string Name,
    EquipmentType Type,
    decimal Capacity,
    EquipmentSubtype? Subtype = null,
    decimal? CurrentVolume = null,
    VolumeUnit? Unit = null,
    string? Description = null,
    string? Notes = null,
    decimal? BoilOffRatePerHour = null,
    decimal? TrubLossLiters = null,
    decimal? MashTunDeadSpaceLiters = null,
    decimal? PackagingLossLiters = null,
    decimal? CurrentTemperatureC = null,
    EquipmentConnectionType? ConnectionType = null,
    string? ConnectionConfigJson = null
);

public record UpdateEquipmentRequest(
    string Name,
    EquipmentType Type,
    decimal Capacity,
    Guid? BrewerySetupId = null,
    EquipmentSubtype? Subtype = null,
    decimal? CurrentVolume = null,
    VolumeUnit? Unit = null,
    string? Description = null,
    string? Notes = null,
    decimal? BoilOffRatePerHour = null,
    decimal? TrubLossLiters = null,
    decimal? MashTunDeadSpaceLiters = null,
    decimal? PackagingLossLiters = null,
    decimal? CurrentTemperatureC = null,
    EquipmentConnectionType? ConnectionType = null,
    string? ConnectionConfigJson = null
);

public record EquipmentDto(
    Guid Id,
    Guid BrewerySetupId,
    string Name,
    EquipmentType Type,
    EquipmentSubtype Subtype,
    decimal Capacity,
    VolumeUnit Unit,
    decimal CapacityLiters,
    decimal CurrentVolume,
    decimal CurrentVolumeLiters,
    decimal? BoilOffRatePerHour,
    decimal? TrubLossLiters,
    decimal? MashTunDeadSpaceLiters,
    decimal? PackagingLossLiters,
    decimal FillPercentage,
    decimal? CurrentTemperatureC,
    DateTime? TemperatureUpdatedAt,
    EquipmentConnectionType ConnectionType,
    string? ConnectionToken,
    string? ConnectionConfigJson,
    string? Description,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    decimal? CurrentSpecificGravity = null,
    decimal? CurrentPressureBar = null,
    decimal? CurrentBatteryPercent = null,
    decimal? CurrentBatteryVoltage = null,
    DateTime? LastTelemetryAt = null,
    string? LatestMetricsJson = null
);

public record EquipmentActiveBatchDto(
    Guid Id,
    string BatchCode,
    string Name,
    BatchStatus Status,
    BrewStage CurrentStage
);

public enum EquipmentAccessResult
{
    Success,
    NotFound,
    Forbidden,
    QuotaExceeded,
    DuplicateName
}

public interface IEquipmentService
{
    Task<(List<EquipmentDto> Items, PaginationMeta Pagination)> GetEquipmentListAsync(
        string userId, Guid? setupId, EquipmentType? type, string? search, int page, int limit);

    Task<(EquipmentAccessResult Result, EquipmentDto? Equipment)> GetEquipmentByIdAsync(Guid id, string userId);

    Task<(EquipmentAccessResult Result, EquipmentDto? Equipment, string? ErrorMessage)> CreateEquipmentAsync(
        CreateEquipmentRequest request, string userId);

    Task<(EquipmentAccessResult Result, EquipmentDto? Equipment, string? ErrorMessage)> UpdateEquipmentAsync(
        Guid id, UpdateEquipmentRequest request, string userId);

    Task<EquipmentAccessResult> DeleteEquipmentAsync(Guid id, string userId);

    Task<(EquipmentAccessResult Result, List<EquipmentActiveBatchDto>? Batches)> GetActiveBatchesForEquipmentAsync(
        Guid equipmentId, string userId);

    Task<(EquipmentAccessResult Result, EquipmentDto? Equipment, string? NewToken)> RegenerateConnectionTokenAsync(
        Guid id, string userId);
}