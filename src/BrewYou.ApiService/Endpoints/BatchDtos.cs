using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services;
using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Endpoints;

public record BatchSummaryDto(
    Guid Id,
    string BatchCode,
    string Name,
    Guid? RecipeId,
    string BeerStyle,
    BatchStatus Status,
    BrewStage CurrentStage,
    DateOnly BrewDate,
    int DaysActive,
    decimal TargetOg,
    decimal? MeasuredOg,
    decimal? CurrentGravity,
    decimal TargetFg,
    decimal? MeasuredFg,
    decimal? AlcoholByVolume,
    decimal ColorSrm,
    decimal? VesselTempC,
    Guid? FermenterId,
    string? FermenterName,
    decimal TargetBatchSizeLiters,
    decimal? MeasuredBatchSizeLiters,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record BatchReadingDto(
    Guid Id,
    Guid BatchId,
    DateTime Timestamp,
    decimal SpecificGravity,
    decimal? TemperatureC,
    string? Notes,
    DateTime CreatedAt,
    decimal? AlcoholByVolume = null
);

public record BatchIngredientDto(
    Guid Id,
    Guid BatchId,
    Guid? SourceIngredientId,
    string Name,
    IngredientType Type,
    decimal Amount,
    string Unit,
    IngredientUsage AdditionStage,
    int? AdditionTimeMinutes,
    bool IsChecked,
    bool IsDeducted,
    string? Notes,
    string? Form = null
);

public record BatchMashStepDto(
    Guid Id,
    Guid BatchId,
    Guid? RecipeMashStepId,
    int StepOrder,
    string Name,
    MashStepType Type,
    decimal TargetTemperatureC,
    int DurationMinutes,
    int? RampTimeMinutes,
    decimal? InfuseAmountLiters,
    decimal? ActualTemperatureC,
    int? ActualDurationMinutes,
    bool IsCompleted,
    DateTime? CompletedAt,
    string? Notes
);

public record BatchFermentationStepDto(
    Guid Id,
    Guid BatchId,
    Guid? RecipeFermentationStepId,
    int StepOrder,
    string Name,
    FermentationStepType Type,
    decimal TargetTemperatureC,
    decimal? ActualTemperatureC,
    int DurationDays,
    int? RampTimeHours,
    decimal? TriggerGravity,
    bool IsCompleted,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? Notes
);

public record BatchStageHistoryDto(
    Guid Id,
    Guid BatchId,
    BrewStage Stage,
    StageStatus Status,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int? DurationMinutes,
    string? Notes
);

public record BatchVolumeProfileDto(
    // Water Schedule
    decimal TotalWaterLiters,
    decimal StrikeWaterLiters,
    decimal SpargeWaterLiters,

    // Pre-Boil
    decimal TargetPreBoilVolumeLiters,
    decimal? MeasuredPreBoilVolumeLiters,
    decimal? MeasuredPreBoilGravity,

    // Post-Boil
    decimal TargetPostBoilVolumeLiters,
    decimal? MeasuredPostBoilVolumeLiters,

    // Fermenter
    decimal TargetFermenterVolumeLiters,
    decimal? MeasuredFermenterVolumeLiters,

    // Packaged
    decimal TargetPackagedVolumeLiters,
    decimal? MeasuredPackagedVolumeLiters,

    // Snapshotted Losses
    decimal BoilOffRatePerHour,
    decimal GrainAbsorptionRateLPerKg,
    decimal KettleTrubLossLiters,
    decimal FermenterTrubLossLiters,
    decimal MashTunDeadSpaceLiters,
    decimal CoolingShrinkagePercent,
    decimal PackagingLossLiters
);

public record BatchSensorAssignmentDto(
    Guid Id,
    Guid EquipmentId,
    string? EquipmentName,
    EquipmentSubtype? EquipmentSubtype,
    BrewStage? Stage,
    Guid? BatchMashStepId,
    string? StepName
);

public record BatchSensorAssignmentInput(
    Guid EquipmentId,
    BrewStage? Stage = null,
    Guid? BatchMashStepId = null
);

public record BatchDetailDto(
    Guid Id,
    string UserId,
    Guid? RecipeId,
    string? RecipeName,
    string BatchCode,
    string Name,
    string BeerStyle,
    BatchStatus Status,
    BrewStage CurrentStage,
    DateOnly BrewDate,
    int DaysActive,

    // Target specifications
    decimal TargetOg,
    decimal TargetFg,
    decimal TargetAbv,
    decimal TargetIbu,
    decimal TargetColorSrm,
    decimal TargetBatchSizeLiters,
    int BoilTimeMinutes,
    decimal EfficiencyPercent,

    // Actual measured metrics
    decimal? MeasuredOg,
    decimal? CurrentGravity,
    decimal? MeasuredFg,
    decimal? AlcoholByVolume,
    decimal? BrewhouseEfficiency,
    decimal? MeasuredBatchSizeLiters,
    decimal? PitchTemperatureC,

    // Equipment assignments
    Guid? BoilerId,
    string? BoilerName,
    Guid? FermenterId,
    string? FermenterName,
    Guid? PackagingVesselId,
    string? PackagingVesselName,

    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt,

    List<BatchReadingDto> Readings,
    List<BatchIngredientDto> Ingredients,
    List<BatchMashStepDto> MashSteps,
    List<BatchFermentationStepDto> FermentationSteps,
    List<BatchStageHistoryDto> StageHistory,
    List<BatchSensorAssignmentDto> SensorAssignments,
    BatchVolumeProfileDto? VolumeProfile
);

public record CreateBatchRequest(
    Guid? RecipeId,
    string? BatchCode,
    string Name,
    string? BeerStyle,
    DateOnly? BrewDate,
    decimal? TargetBatchSizeLiters,
    decimal? TargetOg,
    decimal? TargetFg,
    decimal? TargetAbv,
    decimal? TargetIbu,
    decimal? TargetColorSrm,
    int? BoilTimeMinutes,
    decimal? EfficiencyPercent,
    Guid? BoilerId,
    Guid? FermenterId,
    Guid? PackagingVesselId = null,
    TargetVolumeBasis? TargetVolumeBasis = null,
    decimal? PackagingLossLiters = null,
    decimal? MeasuredOg = null,
    decimal? PitchTemperatureC = null,
    string? Notes = null,
    List<BatchIngredientInputDto>? CustomIngredients = null,
    List<BatchMashStepInputDto>? CustomMashSteps = null,
    List<BatchFermentationStepInputDto>? CustomFermentationSteps = null,
    decimal? BoilOffRatePerHour = null,
    decimal? KettleTrubLossLiters = null,
    decimal? MashTunDeadSpaceLiters = null,
    decimal? FermenterLossLiters = null,
    bool? SpargeEnabled = null,
    List<BatchSensorAssignmentInput>? SensorAssignments = null
);

public record BatchMashStepInputDto(
    int StepOrder,
    string Name,
    MashStepType Type,
    decimal TargetTemperatureC,
    int DurationMinutes,
    int? RampTimeMinutes = null,
    decimal? InfuseAmountLiters = null,
    string? Notes = null
);

public record BatchFermentationStepInputDto(
    int StepOrder,
    string Name,
    FermentationStepType Type,
    decimal TargetTemperatureC,
    int DurationDays,
    int? RampTimeHours = null,
    decimal? TriggerGravity = null,
    string? Notes = null
);

public record ToggleBatchMashStepRequest(
    bool IsCompleted,
    decimal? ActualTemperatureC = null,
    int? ActualDurationMinutes = null,
    string? Notes = null
);

public record ToggleBatchFermentationStepRequest(
    bool IsCompleted,
    decimal? ActualTemperatureC = null,
    string? Notes = null
);

public record BatchIngredientInputDto(
    string Name,
    IngredientType Type,
    decimal Amount,
    string Unit,
    IngredientUsage AdditionStage,
    int? AdditionTimeMinutes,
    string? Notes,
    string? Form = null
);

public record AdvanceBatchStageRequest(
    BrewStage TargetStage,
    decimal? MeasuredOg = null,
    decimal? MeasuredBatchSizeLiters = null,
    decimal? PitchTemperatureC = null,
    Guid? PackagingVesselId = null,
    decimal? MeasuredFg = null,
    string? Notes = null,
    decimal? MeasuredPreBoilVolumeLiters = null,
    decimal? MeasuredPreBoilGravity = null,
    decimal? MeasuredPostBoilVolumeLiters = null,
    decimal? MeasuredPackagedVolumeLiters = null
);

[method: JsonConstructor]
public record AddBatchReadingRequest(
    DateTime? Timestamp,
    decimal SpecificGravity,
    decimal? TemperatureC,
    string? Notes
)
{
    public AddBatchReadingRequest(decimal specificGravity, decimal? temperatureC = null, string? notes = null)
        : this(null, specificGravity, temperatureC, notes)
    {
    }
}

public record UpdateBatchRequest(
    string? Name = null,
    string? BeerStyle = null,
    string? Notes = null,
    decimal? MeasuredOg = null,
    decimal? MeasuredFg = null,
    decimal? MeasuredBatchSizeLiters = null,
    decimal? PitchTemperatureC = null,
    Guid? BoilerId = null,
    Guid? FermenterId = null,
    Guid? PackagingVesselId = null,
    decimal? MeasuredPreBoilVolumeLiters = null,
    decimal? MeasuredPreBoilGravity = null,
    decimal? MeasuredPostBoilVolumeLiters = null,
    decimal? MeasuredPackagedVolumeLiters = null,
    List<BatchSensorAssignmentInput>? SensorAssignments = null
);

public record NextBatchCodeResponse(string BatchCode);

public record CalculateWaterVolumeRequest(
    decimal BatchSizeLiters,
    int BoilTimeMinutes,
    decimal TotalGrainWeightKg,
    TargetVolumeBasis? TargetBasis = null,
    bool? SpargeEnabled = null,
    decimal? BoilOffRatePerHourLiters = null,
    decimal? KettleTrubLossLiters = null,
    decimal? GrainAbsorptionRateLPerKg = null,
    decimal? FermenterLossLiters = null,
    decimal? MashTunDeadSpaceLiters = null,
    decimal? CoolingShrinkagePercent = null,
    decimal? PackagingLossLiters = null,
    decimal? MashThicknessLitersPerKg = null
);

public record CalculateWaterVolumeResponse(
    decimal StrikeWaterLiters,
    decimal SpargeWaterLiters,
    decimal TotalWaterLiters,
    decimal EstimatedPreBoilVolumeLiters,
    decimal EstimatedPostBoilVolumeLiters,
    decimal EstimatedIntoFermenterVolumeLiters,
    decimal EstimatedPackagedVolumeLiters,
    decimal GrainAbsorptionLossLiters,
    decimal BoilOffLossLiters,
    decimal KettleTrubLossLiters,
    decimal ShrinkageLossLiters,
    decimal FermenterLossLiters,
    decimal PackagingLossLiters
);

public record BatchEquipmentReadingDto(
    Guid Id,
    Guid EquipmentId,
    string EquipmentName,
    Guid BatchId,
    BrewStage? Stage,
    Guid? BatchMashStepId,
    string? StepName,
    decimal TemperatureC,
    DateTime Timestamp,
    string? Source,
    decimal? TargetTemperatureC,
    string? Notes,
    decimal? SpecificGravity = null,
    decimal? PressureBar = null,
    decimal? BatteryPercent = null,
    decimal? BatteryVoltage = null,
    decimal? TiltDegrees = null,
    short? Rssi = null,
    string? MetricsJson = null
);

public record LogBatchTemperatureRequest(
    decimal TemperatureC,
    Guid? EquipmentId = null,
    BrewStage? Stage = null,
    Guid? BatchMashStepId = null,
    string? StepName = null,
    DateTime? Timestamp = null,
    string? Notes = null
);

public record CheckBatchStockRequest(
    Guid RecipeId,
    decimal TargetBatchSizeLiters,
    TargetVolumeBasis? TargetVolumeBasis = null,
    decimal? FermenterLossLiters = null,
    decimal? PackagingLossLiters = null
);

public record IngredientShortageDto(
    Guid IngredientId,
    string Name,
    IngredientType Type,
    decimal RequiredAmount,
    decimal StockAmount,
    string Unit,
    decimal Deficit
);

public record BatchStockCheckResult(
    bool HasShortage,
    List<IngredientShortageDto> Shortages
);