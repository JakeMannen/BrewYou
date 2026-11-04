using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;

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
    string? ConnectionConfigJson = null,
    int? DisplayWidthPx = null,
    int? DisplayHeightPx = null,
    DisplayOrientation? DisplayOrientation = null
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
    string? ConnectionConfigJson = null,
    int? DisplayWidthPx = null,
    int? DisplayHeightPx = null,
    DisplayOrientation? DisplayOrientation = null
);

public record TapAssignedBatchDto(
    Guid Id,
    string BatchCode,
    string Name,
    string BeerStyle,
    decimal? ActualAbv,
    DateTime? AssignedAt,
    Guid? RecipeId = null,
    string? LabelImageUrl = null
);

public record AssignedKegSummaryDto(
    Guid Id,
    string Name,
    EquipmentSubtype Subtype,
    decimal CapacityLiters,
    decimal CurrentVolumeLiters,
    VolumeUnit Unit,
    decimal FillPercentage,
    TapAssignedBatchDto? AssignedBatch,
    decimal? CurrentTemperatureC = null,
    DateTime? TemperatureUpdatedAt = null
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
    string? LatestMetricsJson = null,
    int? DisplayWidthPx = null,
    int? DisplayHeightPx = null,
    DisplayOrientation? DisplayOrientation = null,
    bool DisplayLinkEnabled = false,
    TapAssignedBatchDto? AssignedBatch = null,
    Guid? AssignedKegId = null,
    AssignedKegSummaryDto? AssignedKeg = null,
    Guid? AssignedBatchId = null,
    decimal? FillLevelPercent = null
);

public record EquipmentActiveBatchDto(
    Guid Id,
    string BatchCode,
    string Name,
    BatchStatus Status,
    BrewStage CurrentStage,
    string? Role = null
);

public enum EquipmentAccessResult
{
    Success,
    NotFound,
    Forbidden,
    QuotaExceeded,
    DuplicateName,
    InvalidTapAssignment,
    Conflict
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

    Task<EquipmentAccessResult> DeleteEquipmentAsync(Guid id, string userId, bool force = false);

    Task<(EquipmentAccessResult Result, List<EquipmentActiveBatchDto>? Batches)> GetActiveBatchesForEquipmentAsync(
        Guid equipmentId, string userId);

    Task<(EquipmentAccessResult Result, EquipmentDto? Equipment, string? NewToken)> RegenerateConnectionTokenAsync(
        Guid id, string userId);

    Task<(EquipmentAccessResult Result, EquipmentDto? Equipment, string? ErrorMessage)> AssignTapAsync(
        Guid tapId, AssignTapRequest request, string userId);

    Task<(EquipmentAccessResult Result, EquipmentDto? Equipment, string? ErrorMessage)> AssignKegBatchAsync(
        Guid kegId, AssignKegBatchRequest request, string userId);

    Task<(EquipmentAccessResult Result, EquipmentDto? Equipment, string? ErrorMessage)> AssignTapBatchAsync(
        Guid tapId, Guid batchId, string userId);

    Task<(EquipmentAccessResult Result, EquipmentDto? Equipment, string? ErrorMessage)> UnassignTapBatchAsync(
        Guid tapId, string userId);

    Task<(EquipmentAccessResult Result, TapDisplayLinkDto? Link, string? ErrorMessage)> GetTapDisplayLinkAsync(
        Guid tapId, string userId, string appBaseUrl, string apiBaseUrl);

    Task<(EquipmentAccessResult Result, TapDisplayLinkDto? Link, string? ErrorMessage)> EnableOrRegenerateTapDisplayLinkAsync(
        Guid tapId, string userId, string appBaseUrl, string apiBaseUrl);

    Task<(EquipmentAccessResult Result, string? ErrorMessage)> RevokeTapDisplayLinkAsync(
        Guid tapId, string userId);

    Task<(TapDisplayManifestDto? Manifest, string ETag)?> GetTapManifestByTokenAsync(
        string token, string apiBaseUrl);

    Task<(byte[]? Bytes, string ContentType, string ETag)?> RenderTapImageByTokenAsync(
        string token, string? format, bool abv = true);
}