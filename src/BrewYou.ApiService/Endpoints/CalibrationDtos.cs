using FluentValidation;

namespace BrewYou.ApiService.Endpoints;

public record BatchVarianceItemDto(
    Guid BatchId,
    string BatchCode,
    string Name,
    string BeerStyle,
    DateOnly BrewDate,
    string Status,
    string? RecipeName,
    decimal TargetOg,
    decimal? MeasuredOg,
    decimal? OgVariancePoints,
    decimal TargetFg,
    decimal? MeasuredFg,
    decimal? FgVariancePoints,
    decimal TargetAbv,
    decimal? MeasuredAbv,
    decimal? AbvVariance,
    decimal TargetEfficiencyPercent,
    decimal? ActualBrewhouseEfficiencyPercent,
    decimal? EfficiencyVariancePercent,
    decimal? ActualMashEfficiencyPercent,
    decimal? TargetPreBoilVolumeLiters,
    decimal? MeasuredPreBoilVolumeLiters,
    decimal? TargetPostBoilVolumeLiters,
    decimal? MeasuredPostBoilVolumeLiters,
    decimal? TargetFermenterVolumeLiters,
    decimal? MeasuredFermenterVolumeLiters,
    decimal? VolumeVarianceLiters,
    decimal? TargetPackagedVolumeLiters,
    decimal? MeasuredPackagedVolumeLiters,
    decimal? ConfiguredBoilOffRatePerHour,
    decimal? ObservedBoilOffRatePerHour,
    decimal? ObservedKettleTrubLossLiters,
    decimal? ObservedFermenterLossLiters,
    decimal? ObservedGrainAbsorptionLPerKg,
    Guid? BoilerId = null,
    string? BoilerName = null,
    Guid? FermenterId = null,
    string? FermenterName = null);

public record RigCalibrationReportDto(
    Guid BrewerySetupId,
    string BrewerySetupName,
    int SampleSize,
    int AnalyzedBatchCount,
    decimal CurrentDefaultBoilOffRatePerHour,
    decimal? SuggestedBoilOffRatePerHour,
    int BoilOffSampleCount,
    decimal CurrentDefaultKettleTrubLossLiters,
    decimal? SuggestedKettleTrubLossLiters,
    int KettleTrubSampleCount,
    decimal CurrentDefaultFermenterLossLiters,
    decimal? SuggestedFermenterLossLiters,
    int FermenterLossSampleCount,
    decimal CurrentDefaultGrainAbsorptionRate,
    decimal? SuggestedGrainAbsorptionRate,
    int GrainAbsorptionSampleCount,
    decimal? SuggestedBrewhouseEfficiencyPercent,
    int EfficiencySampleCount,
    decimal? SuggestedMashEfficiencyPercent,
    decimal? AverageOgVariancePoints,
    decimal? AverageVolumeVarianceLiters,
    bool HasSuggestions,
    List<BatchVarianceItemDto> Batches,
    Guid? BoilerId = null,
    string? BoilerName = null,
    Guid? FermenterId = null,
    string? FermenterName = null,
    decimal CurrentDefaultEfficiencyPercent = 72.0m);

public record ApplyRigCalibrationRequest(
    int SampleSize = 5,
    bool ApplyBoilOffRate = true,
    bool ApplyKettleTrubLoss = true,
    bool ApplyFermenterLoss = true,
    bool ApplyGrainAbsorption = true,
    bool ApplyToPrimaryEquipment = true,
    Guid? TargetBoilerId = null,
    Guid? TargetFermenterId = null,
    bool ApplyEfficiency = true,
    bool ApplyToSetup = true);

public class ApplyRigCalibrationRequestValidator : AbstractValidator<ApplyRigCalibrationRequest>
{
    public ApplyRigCalibrationRequestValidator()
    {
        RuleFor(x => x.SampleSize).InclusiveBetween(1, 25);
    }
}