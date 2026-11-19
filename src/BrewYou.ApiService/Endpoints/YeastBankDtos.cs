using FluentValidation;

namespace BrewYou.ApiService.Endpoints;

public record YeastBankEntryDto(
    Guid Id,
    Guid? IngredientId,
    string StrainName,
    string? LabOrBrand,
    string? ProductCode,
    string Form,
    int Generation,
    DateTime HarvestDate,
    Guid? SourceBatchId,
    string? SourceBatchName,
    string? SourceBatchCode,
    decimal VolumeMl,
    decimal EstimatedDensityBillionPerMl,
    decimal BaseViabilityPercent,
    decimal CurrentEstimatedViabilityPercent,
    decimal EstimatedTotalViableCellsBillions,
    int AgeDays,
    string? StorageLocation,
    string Status,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateYeastBankEntryRequest(
    string StrainName,
    Guid? IngredientId = null,
    string? LabOrBrand = null,
    string? ProductCode = null,
    string Form = "Slurry",
    int Generation = 1,
    DateTime? HarvestDate = null,
    Guid? SourceBatchId = null,
    decimal VolumeMl = 250m,
    decimal EstimatedDensityBillionPerMl = 1.0m,
    decimal BaseViabilityPercent = 95m,
    string? StorageLocation = null,
    string? Notes = null);

public record UpdateYeastBankEntryRequest(
    string? StrainName = null,
    string? LabOrBrand = null,
    string? ProductCode = null,
    string? Form = null,
    int? Generation = null,
    DateTime? HarvestDate = null,
    decimal? VolumeMl = null,
    decimal? EstimatedDensityBillionPerMl = null,
    decimal? BaseViabilityPercent = null,
    string? StorageLocation = null,
    string? Status = null,
    string? Notes = null);

public record HarvestYeastRequest(
    string? StrainName = null,
    Guid? IngredientId = null,
    string? LabOrBrand = null,
    string? ProductCode = null,
    DateTime? HarvestDate = null,
    decimal VolumeMl = 500m,
    decimal EstimatedDensityBillionPerMl = 1.2m,
    decimal BaseViabilityPercent = 95m,
    string? StorageLocation = null,
    string? Notes = null);

public record CalculateYeastPitchRequest(
    decimal BatchVolumeLiters,
    decimal OriginalGravity,
    string StyleCategory = "Ale",
    decimal? CustomPitchRateMillionPerMlPlato = null,
    string YeastForm = "Liquid",
    decimal? PackageAgeDays = 0m,
    decimal? BaseViabilityPercent = null,
    decimal? CellsPerUnitBillions = null,
    int PacksAvailable = 1,
    decimal? SlurryDensityBillionPerMl = 1.0m,
    decimal? SlurryVolumeAvailableMl = null,
    string StarterMethod = "StirPlate",
    decimal? StarterVolumeLiters = null,
    Guid? YeastBankEntryId = null);

public record YeastPitchPlanDto(
    decimal Plato,
    decimal TargetPitchRateMillionPerMlPlato,
    decimal RequiredCellsBillions,
    decimal EstimatedViabilityPercent,
    decimal ViableCellsPerUnitBillions,
    decimal InitialViableCellsBillions,
    decimal DeficitCellsBillions,
    decimal DryPacksExact,
    int DryPacksRecommended,
    decimal RequiredSlurryMl,
    decimal RecommendedStarterVolumeLiters,
    decimal StarterDmeGrams,
    decimal ProjectedStarterCellsBillions,
    bool NeedsMultiStepStarter,
    string RecommendationSummaryKey,
    Guid? YeastBankEntryId = null,
    string? StrainName = null);

public class CreateYeastBankEntryRequestValidator : AbstractValidator<CreateYeastBankEntryRequest>
{
    private static readonly string[] AllowedForms = ["Slurry", "Liquid", "Dry"];

    public CreateYeastBankEntryRequestValidator()
    {
        RuleFor(x => x.StrainName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Form).Must(f => AllowedForms.Contains(f, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Form must be Slurry, Liquid, or Dry.");
        RuleFor(x => x.Generation).InclusiveBetween(0, 50);
        RuleFor(x => x.VolumeMl).InclusiveBetween(0m, 100000m);
        RuleFor(x => x.EstimatedDensityBillionPerMl).InclusiveBetween(0.01m, 25m);
        RuleFor(x => x.BaseViabilityPercent).InclusiveBetween(1m, 100m);
        RuleFor(x => x.LabOrBrand).MaximumLength(100);
        RuleFor(x => x.ProductCode).MaximumLength(50);
        RuleFor(x => x.StorageLocation).MaximumLength(150);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class UpdateYeastBankEntryRequestValidator : AbstractValidator<UpdateYeastBankEntryRequest>
{
    private static readonly string[] AllowedForms = ["Slurry", "Liquid", "Dry"];
    private static readonly string[] AllowedStatuses = ["Available", "Depleted", "Discarded"];

    public UpdateYeastBankEntryRequestValidator()
    {
        When(x => x.StrainName is not null, () => RuleFor(x => x.StrainName!).NotEmpty().MaximumLength(150));
        When(x => x.Form is not null, () => RuleFor(x => x.Form!).Must(f => AllowedForms.Contains(f, StringComparer.OrdinalIgnoreCase)));
        When(x => x.Status is not null, () => RuleFor(x => x.Status!).Must(s => AllowedStatuses.Contains(s, StringComparer.OrdinalIgnoreCase)));
        When(x => x.Generation.HasValue, () => RuleFor(x => x.Generation!.Value).InclusiveBetween(0, 50));
        When(x => x.VolumeMl.HasValue, () => RuleFor(x => x.VolumeMl!.Value).InclusiveBetween(0m, 100000m));
        When(x => x.EstimatedDensityBillionPerMl.HasValue, () => RuleFor(x => x.EstimatedDensityBillionPerMl!.Value).InclusiveBetween(0.01m, 25m));
        When(x => x.BaseViabilityPercent.HasValue, () => RuleFor(x => x.BaseViabilityPercent!.Value).InclusiveBetween(1m, 100m));
    }
}

public class HarvestYeastRequestValidator : AbstractValidator<HarvestYeastRequest>
{
    public HarvestYeastRequestValidator()
    {
        RuleFor(x => x.StrainName).MaximumLength(150);
        RuleFor(x => x.VolumeMl).InclusiveBetween(1m, 100000m);
        RuleFor(x => x.EstimatedDensityBillionPerMl).InclusiveBetween(0.01m, 25m);
        RuleFor(x => x.BaseViabilityPercent).InclusiveBetween(1m, 100m);
        RuleFor(x => x.StorageLocation).MaximumLength(150);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class CalculateYeastPitchRequestValidator : AbstractValidator<CalculateYeastPitchRequest>
{
    public CalculateYeastPitchRequestValidator()
    {
        RuleFor(x => x.BatchVolumeLiters).InclusiveBetween(0.5m, 10000m);
        RuleFor(x => x.OriginalGravity).InclusiveBetween(1.005m, 1.200m);
        RuleFor(x => x.PacksAvailable).InclusiveBetween(0, 100);
        When(x => x.CustomPitchRateMillionPerMlPlato.HasValue, () =>
            RuleFor(x => x.CustomPitchRateMillionPerMlPlato!.Value).InclusiveBetween(0.1m, 5.0m));
    }
}