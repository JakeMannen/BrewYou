using FluentValidation;

namespace BrewYou.ApiService.Endpoints;

public record WaterProfileDto(
    Guid Id,
    string Name,
    string? Description,
    string Category,
    bool IsPreset,
    decimal CalciumPpm,
    decimal MagnesiumPpm,
    decimal SodiumPpm,
    decimal SulfatePpm,
    decimal ChloridePpm,
    decimal BicarbonatePpm,
    decimal? Ph,
    decimal ResidualAlkalinityPpm,
    decimal? SulfateToChlorideRatio,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateWaterProfileRequest(
    string Name,
    string Category = "Source",
    string? Description = null,
    decimal CalciumPpm = 0m,
    decimal MagnesiumPpm = 0m,
    decimal SodiumPpm = 0m,
    decimal SulfatePpm = 0m,
    decimal ChloridePpm = 0m,
    decimal BicarbonatePpm = 0m,
    decimal? Ph = 7.0m);

public record UpdateWaterProfileRequest(
    string? Name = null,
    string? Category = null,
    string? Description = null,
    decimal? CalciumPpm = null,
    decimal? MagnesiumPpm = null,
    decimal? SodiumPpm = null,
    decimal? SulfatePpm = null,
    decimal? ChloridePpm = null,
    decimal? BicarbonatePpm = null,
    decimal? Ph = null);

public record RecipeWaterPlanInputDto(
    Guid? SourceProfileId = null,
    string? SourceProfileName = null,
    decimal SourceCalciumPpm = 0m,
    decimal SourceMagnesiumPpm = 0m,
    decimal SourceSodiumPpm = 0m,
    decimal SourceSulfatePpm = 0m,
    decimal SourceChloridePpm = 0m,
    decimal SourceBicarbonatePpm = 0m,
    decimal SourcePh = 7.0m,
    Guid? TargetProfileId = null,
    string? TargetProfileName = null,
    decimal? TargetCalciumPpm = null,
    decimal? TargetMagnesiumPpm = null,
    decimal? TargetSodiumPpm = null,
    decimal? TargetSulfatePpm = null,
    decimal? TargetChloridePpm = null,
    decimal? TargetBicarbonatePpm = null,
    decimal TargetMashPh = 5.4m,
    decimal MashGypsumGrams = 0m,
    decimal MashCalciumChlorideGrams = 0m,
    decimal MashEpsomSaltGrams = 0m,
    decimal MashTableSaltGrams = 0m,
    decimal MashBakingSodaGrams = 0m,
    decimal MashChalkGrams = 0m,
    string? MashAcidType = "Lactic80",
    decimal MashAcidAmountMlOrG = 0m,
    decimal SpargeGypsumGrams = 0m,
    decimal SpargeCalciumChlorideGrams = 0m,
    decimal SpargeEpsomSaltGrams = 0m,
    decimal SpargeTableSaltGrams = 0m,
    decimal SpargeBakingSodaGrams = 0m,
    decimal SpargeChalkGrams = 0m,
    string? SpargeAcidType = "Lactic80",
    decimal SpargeAcidAmountMlOrG = 0m,
    string? Notes = null);

public record RecipeWaterPlanDto(
    Guid Id,
    Guid RecipeId,
    Guid? SourceProfileId,
    string? SourceProfileName,
    decimal SourceCalciumPpm,
    decimal SourceMagnesiumPpm,
    decimal SourceSodiumPpm,
    decimal SourceSulfatePpm,
    decimal SourceChloridePpm,
    decimal SourceBicarbonatePpm,
    decimal SourcePh,
    Guid? TargetProfileId,
    string? TargetProfileName,
    decimal? TargetCalciumPpm,
    decimal? TargetMagnesiumPpm,
    decimal? TargetSodiumPpm,
    decimal? TargetSulfatePpm,
    decimal? TargetChloridePpm,
    decimal? TargetBicarbonatePpm,
    decimal TargetMashPh,
    decimal MashGypsumGrams,
    decimal MashCalciumChlorideGrams,
    decimal MashEpsomSaltGrams,
    decimal MashTableSaltGrams,
    decimal MashBakingSodaGrams,
    decimal MashChalkGrams,
    string? MashAcidType,
    decimal MashAcidAmountMlOrG,
    decimal SpargeGypsumGrams,
    decimal SpargeCalciumChlorideGrams,
    decimal SpargeEpsomSaltGrams,
    decimal SpargeTableSaltGrams,
    decimal SpargeBakingSodaGrams,
    decimal SpargeChalkGrams,
    string? SpargeAcidType,
    decimal SpargeAcidAmountMlOrG,
    decimal ResultingCalciumPpm,
    decimal ResultingMagnesiumPpm,
    decimal ResultingSodiumPpm,
    decimal ResultingSulfatePpm,
    decimal ResultingChloridePpm,
    decimal ResultingBicarbonatePpm,
    decimal? SulfateToChlorideRatio,
    string BalanceProfile,
    decimal ResidualAlkalinityPpm,
    decimal PredictedMashPh,
    string? Notes,
    DateTime UpdatedAt);

public record WaterGristGrainDto(
    decimal WeightKg,
    decimal ColorSrm,
    string? Name = null);

public record WaterSaltAdditionSetDto(
    decimal GypsumGrams,
    decimal CalciumChlorideGrams,
    decimal EpsomSaltGrams,
    decimal TableSaltGrams,
    decimal BakingSodaGrams,
    decimal ChalkGrams,
    string? AcidType,
    decimal AcidAmountMlOrG);

public record CalculateWaterChemistryRequest(
    decimal SourceCalciumPpm,
    decimal SourceMagnesiumPpm,
    decimal SourceSodiumPpm,
    decimal SourceSulfatePpm,
    decimal SourceChloridePpm,
    decimal SourceBicarbonatePpm,
    decimal MashVolumeLiters,
    decimal SpargeVolumeLiters,
    WaterSaltAdditionSetDto MashAdditions,
    WaterSaltAdditionSetDto? SpargeAdditions = null,
    List<WaterGristGrainDto>? Grist = null);

public record SuggestWaterSaltsRequest(
    decimal SourceCalciumPpm,
    decimal SourceMagnesiumPpm,
    decimal SourceSodiumPpm,
    decimal SourceSulfatePpm,
    decimal SourceChloridePpm,
    decimal SourceBicarbonatePpm,
    decimal TargetCalciumPpm,
    decimal TargetMagnesiumPpm,
    decimal TargetSodiumPpm,
    decimal TargetSulfatePpm,
    decimal TargetChloridePpm,
    decimal TargetBicarbonatePpm,
    decimal MashVolumeLiters,
    decimal SpargeVolumeLiters,
    decimal TargetMashPh = 5.4m,
    string AcidType = "Lactic80",
    List<WaterGristGrainDto>? Grist = null);

public record WaterChemistryResultDto(
    decimal MashCalciumPpm,
    decimal MashMagnesiumPpm,
    decimal MashSodiumPpm,
    decimal MashSulfatePpm,
    decimal MashChloridePpm,
    decimal MashBicarbonatePpm,
    decimal ResultingCalciumPpm,
    decimal ResultingMagnesiumPpm,
    decimal ResultingSodiumPpm,
    decimal ResultingSulfatePpm,
    decimal ResultingChloridePpm,
    decimal ResultingBicarbonatePpm,
    decimal? SulfateToChlorideRatio,
    string BalanceProfile,
    decimal AlkalinityPpm,
    decimal ResidualAlkalinityPpm,
    decimal PredictedMashPh,
    bool HasChalkWarning);

public record SuggestWaterSaltsResponse(
    WaterSaltAdditionSetDto MashAdditions,
    WaterSaltAdditionSetDto SpargeAdditions,
    WaterChemistryResultDto Preview);

public class CreateWaterProfileRequestValidator : AbstractValidator<CreateWaterProfileRequest>
{
    private static readonly string[] AllowedCategories = ["Source", "Target"];

    public CreateWaterProfileRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Category).Must(c => AllowedCategories.Contains(c, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Category must be Source or Target.");
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.CalciumPpm).InclusiveBetween(0m, 2000m);
        RuleFor(x => x.MagnesiumPpm).InclusiveBetween(0m, 1000m);
        RuleFor(x => x.SodiumPpm).InclusiveBetween(0m, 1500m);
        RuleFor(x => x.SulfatePpm).InclusiveBetween(0m, 3000m);
        RuleFor(x => x.ChloridePpm).InclusiveBetween(0m, 2000m);
        RuleFor(x => x.BicarbonatePpm).InclusiveBetween(0m, 2000m);
        When(x => x.Ph.HasValue, () => RuleFor(x => x.Ph!.Value).InclusiveBetween(3.0m, 11.0m));
    }
}

public class UpdateWaterProfileRequestValidator : AbstractValidator<UpdateWaterProfileRequest>
{
    private static readonly string[] AllowedCategories = ["Source", "Target"];

    public UpdateWaterProfileRequestValidator()
    {
        When(x => x.Name is not null, () => RuleFor(x => x.Name!).NotEmpty().MaximumLength(100));
        When(x => x.Category is not null, () => RuleFor(x => x.Category!).Must(c => AllowedCategories.Contains(c, StringComparer.OrdinalIgnoreCase)));
        When(x => x.Description is not null, () => RuleFor(x => x.Description!).MaximumLength(500));
        When(x => x.CalciumPpm.HasValue, () => RuleFor(x => x.CalciumPpm!.Value).InclusiveBetween(0m, 2000m));
        When(x => x.MagnesiumPpm.HasValue, () => RuleFor(x => x.MagnesiumPpm!.Value).InclusiveBetween(0m, 1000m));
        When(x => x.SodiumPpm.HasValue, () => RuleFor(x => x.SodiumPpm!.Value).InclusiveBetween(0m, 1500m));
        When(x => x.SulfatePpm.HasValue, () => RuleFor(x => x.SulfatePpm!.Value).InclusiveBetween(0m, 3000m));
        When(x => x.ChloridePpm.HasValue, () => RuleFor(x => x.ChloridePpm!.Value).InclusiveBetween(0m, 2000m));
        When(x => x.BicarbonatePpm.HasValue, () => RuleFor(x => x.BicarbonatePpm!.Value).InclusiveBetween(0m, 2000m));
        When(x => x.Ph.HasValue, () => RuleFor(x => x.Ph!.Value).InclusiveBetween(3.0m, 11.0m));
    }
}