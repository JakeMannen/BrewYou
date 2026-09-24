namespace BrewYou.ApiService.Services;

public record CreateBrewerySetupRequest(
    string Name,
    string? Description = null,
    bool IsDefault = false,
    decimal? DefaultGrainAbsorptionRate = null,
    decimal? DefaultBoilOffRatePerHour = null,
    decimal? DefaultKettleTrubLossLiters = null,
    decimal? DefaultFermenterLossLiters = null,
    decimal? DefaultMashTunDeadSpaceLiters = null,
    decimal? CoolingShrinkagePercent = null,
    decimal? DefaultPackagingLossLiters = null
);

public record UpdateBrewerySetupRequest(
    string Name,
    string? Description = null,
    bool? IsDefault = null,
    decimal? DefaultGrainAbsorptionRate = null,
    decimal? DefaultBoilOffRatePerHour = null,
    decimal? DefaultKettleTrubLossLiters = null,
    decimal? DefaultFermenterLossLiters = null,
    decimal? DefaultMashTunDeadSpaceLiters = null,
    decimal? CoolingShrinkagePercent = null,
    decimal? DefaultPackagingLossLiters = null
);

public record BrewerySetupDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsDefault,
    int EquipmentCount,
    decimal DefaultGrainAbsorptionRate,
    decimal DefaultBoilOffRatePerHour,
    decimal DefaultKettleTrubLossLiters,
    decimal DefaultFermenterLossLiters,
    decimal DefaultMashTunDeadSpaceLiters,
    decimal CoolingShrinkagePercent,
    decimal DefaultPackagingLossLiters,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public enum BrewerySetupAccessResult
{
    Success,
    NotFound,
    Forbidden,
    CannotDeleteLastSetup,
    QuotaExceeded
}

public interface IBrewerySetupService
{
    Task<List<BrewerySetupDto>> GetSetupsAsync(string userId);

    Task<(BrewerySetupAccessResult Result, BrewerySetupDto? Setup)> GetSetupByIdAsync(Guid id, string userId);

    Task<(BrewerySetupAccessResult Result, BrewerySetupDto? Setup, string? ErrorMessage)> CreateSetupAsync(
        CreateBrewerySetupRequest request, string userId);

    Task<(BrewerySetupAccessResult Result, BrewerySetupDto? Setup, string? ErrorMessage)> UpdateSetupAsync(
        Guid id, UpdateBrewerySetupRequest request, string userId);

    Task<(BrewerySetupAccessResult Result, string? ErrorMessage)> DeleteSetupAsync(Guid id, string userId);

    Task<(BrewerySetupAccessResult Result, BrewerySetupDto? Setup, string? ErrorMessage)> SetDefaultSetupAsync(
        Guid id, string userId);
}