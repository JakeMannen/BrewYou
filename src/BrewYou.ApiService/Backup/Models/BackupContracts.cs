using BrewYou.ApiService.Data.Entities;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Backup.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RestoreConflictResolutionMode
{
    MergeSkipExisting,
    MergeOverwriteExisting,
    ReplaceAll
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SharedSetupFallbackMode
{
    CloneAsPersonal,
    DetachReferences
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CollaboratorRestoreMode
{
    RestoreMembershipsIfAvailable,
    CreateInvitesOnly,
    SkipCollaborators
}

public static class BackupSchemaConstants
{
    public const string FormatIdentifier = "brewyou-user-backup";
    public const int CurrentSchemaVersion = 2;
    public const string CurrentAppVersion = "1.6.1";
}

public record BackupEncryptionMetadataDto(
    bool SecretsEncrypted,
    string Algorithm = "AES-256-GCM",
    string Kdf = "PBKDF2-SHA256",
    int Iterations = 600_000,
    string? SaltBase64 = null
);

public record BrewYouBackupEnvelope(
    string Format,
    int SchemaVersion,
    string AppVersion,
    DateTime ExportedAtUtc,
    string ChecksumSha256,
    JsonElement Data,
    BackupEncryptionMetadataDto? Encryption = null
);

public record UserSettingsBackupDto(
    string OriginalUserId,
    string? Email,
    string? DisplayName,
    string PreferredLanguage = "en",
    VolumeUnit PreferredVolumeUnit = VolumeUnit.Liters,
    WeightUnit PreferredWeightUnit = WeightUnit.Metric,
    string? PreferredGrainWeightUnit = "kg",
    string? PreferredHopWeightUnit = "g",
    string? PreferredColorUnit = "SRM",
    string? PreferredCarbonationUnit = "Volumes",
    TemperatureUnit PreferredTemperatureUnit = TemperatureUnit.Celsius,
    GravityUnit PreferredGravityUnit = GravityUnit.SpecificGravity,
    ThemePreference PreferredTheme = ThemePreference.ImperialStout,
    string? PreferredBitternessFormula = "Tinseth",
    string? PreferredColorFormula = "Morey",
    string? PreferredAbvFormula = "Linear",
    decimal DefaultBatchSizeLiters = 20.0m,
    decimal DefaultEfficiencyPercent = 75.0m,
    int DefaultBoilTimeMinutes = 60,
    string? MqttHost = null,
    int? MqttPort = 1883,
    string? MqttUsername = null,
    string? MqttPasswordEncrypted = null,
    bool HasMqttPassword = false,
    string? MqttCertificate = null,
    string? MqttTopicPrefix = "brewyou/equipment"
);

public record IngredientSnapshotDto(
    string Name,
    IngredientType Type,
    decimal? PotentialGravity = null,
    decimal? ColorSrm = null,
    decimal? AlphaAcidPercent = null,
    decimal? AttenuationPercent = null,
    string? Form = null,
    string? Description = null
);

public record CustomIngredientBackupDto(
    Guid Id,
    string Name,
    IngredientType Type,
    decimal? PotentialGravity = null,
    decimal? ColorSrm = null,
    decimal? AlphaAcidPercent = null,
    decimal? AttenuationPercent = null,
    string? Form = null,
    string? Description = null,
    DateTime CreatedAt = default
);

public record IngredientStockBackupDto(
    Guid Id,
    Guid IngredientId,
    string IngredientName,
    IngredientType IngredientType,
    bool IsCatalogItem,
    decimal Amount,
    string Unit,
    decimal? LowStockAlertThreshold = null,
    DateTime UpdatedAt = default,
    IngredientSnapshotDto? IngredientSnapshot = null
);

public record BreweryMemberBackupDto(
    string UserId,
    string? Email,
    string? DisplayName,
    BreweryRole Role,
    DateTime JoinedAt
);

public record BreweryInviteBackupDto(
    string InvitedEmail,
    BreweryRole Role,
    DateTime CreatedAt,
    DateTime ExpiresAt
);

public record BrewerySetupBackupDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsDefault,
    decimal DefaultGrainAbsorptionRate = 0.96m,
    decimal DefaultBoilOffRatePerHour = 3.0m,
    decimal DefaultKettleTrubLossLiters = 1.5m,
    decimal DefaultFermenterLossLiters = 1.5m,
    decimal DefaultMashTunDeadSpaceLiters = 0.0m,
    decimal CoolingShrinkagePercent = 4.0m,
    decimal DefaultPackagingLossLiters = 0.5m,
    DateTime CreatedAt = default,
    DateTime UpdatedAt = default,
    List<BreweryMemberBackupDto>? Members = null,
    List<BreweryInviteBackupDto>? PendingInvites = null
);

public record EquipmentBackupDto(
    Guid Id,
    Guid BrewerySetupId,
    string Name,
    EquipmentType Type,
    EquipmentSubtype Subtype = EquipmentSubtype.Other,
    decimal Capacity = 0.0m,
    VolumeUnit Unit = VolumeUnit.Liters,
    decimal CapacityLiters = 0.0m,
    decimal CurrentVolume = 0.0m,
    decimal CurrentVolumeLiters = 0.0m,
    decimal? BoilOffRatePerHour = null,
    decimal? TrubLossLiters = null,
    decimal? MashTunDeadSpaceLiters = null,
    decimal? PackagingLossLiters = null,
    decimal? CurrentTemperatureC = null,
    decimal? CurrentSpecificGravity = null,
    decimal? CurrentPressureBar = null,
    decimal? CurrentBatteryPercent = null,
    decimal? CurrentBatteryVoltage = null,
    DateTime? TemperatureUpdatedAt = null,
    DateTime? LastTelemetryAt = null,
    string? LatestMetricsJson = null,
    EquipmentConnectionType ConnectionType = EquipmentConnectionType.None,
    string? ConnectionTokenEncrypted = null,
    string? ConnectionConfigJson = null,
    string? Description = null,
    string? Notes = null,
    DateTime CreatedAt = default,
    DateTime UpdatedAt = default
);

public record SharedSetupMembershipBackupDto(
    Guid OriginalSetupId,
    string SetupName,
    string OwnerUserId,
    string? OwnerEmail,
    string? OwnerDisplayName,
    BreweryRole MyRole,
    DateTime JoinedAt,
    BrewerySetupBackupDto SetupSnapshot,
    List<EquipmentBackupDto> ReferencedEquipmentSnapshots
);

public record RecipeIngredientBackupDto(
    Guid Id,
    Guid IngredientId,
    string IngredientName,
    IngredientType IngredientType,
    bool IsCatalogItem,
    decimal Amount,
    string Unit,
    int? DurationMinutes = null,
    IngredientUsage Usage = IngredientUsage.Boil,
    string? Notes = null,
    string? Form = null,
    IngredientSnapshotDto? IngredientSnapshot = null
);

public record RecipeMashStepBackupDto(
    Guid Id,
    int StepOrder,
    string Name,
    MashStepType Type = MashStepType.Temperature,
    decimal TemperatureC = 65.0m,
    int DurationMinutes = 60,
    int? RampTimeMinutes = null,
    decimal? InfuseAmountLiters = null,
    string? Notes = null
);

public record RecipeFermentationStepBackupDto(
    Guid Id,
    int StepOrder,
    string Name,
    FermentationStepType Type = FermentationStepType.Primary,
    decimal TargetTemperatureC = 19.0m,
    int DurationDays = 14,
    int? RampTimeHours = null,
    decimal? TriggerGravity = null,
    string? Notes = null
);

public record RecipeBackupDto(
    Guid Id,
    string Name,
    string? Description,
    string BeerStyle = "American IPA",
    decimal BatchSizeLiters = 20.0m,
    int BoilTimeMinutes = 60,
    decimal EfficiencyPercent = 72.0m,
    decimal OriginalGravity = 1.050m,
    decimal FinalGravity = 1.010m,
    decimal AlcoholByVolume = 5.25m,
    decimal BitternessIbu = 35.0m,
    decimal ColorSrm = 6.0m,
    bool IsPublic = false,
    DateTime CreatedAt = default,
    DateTime UpdatedAt = default,
    List<RecipeIngredientBackupDto>? Ingredients = null,
    List<RecipeMashStepBackupDto>? MashSteps = null,
    List<RecipeFermentationStepBackupDto>? FermentationSteps = null
);

public record BatchVolumeProfileBackupDto(
    decimal TotalWaterLiters,
    decimal StrikeWaterLiters,
    decimal SpargeWaterLiters,
    decimal TargetPreBoilVolumeLiters,
    decimal? MeasuredPreBoilVolumeLiters,
    decimal? MeasuredPreBoilGravity,
    decimal TargetPostBoilVolumeLiters,
    decimal? MeasuredPostBoilVolumeLiters,
    decimal TargetFermenterVolumeLiters,
    decimal? MeasuredFermenterVolumeLiters,
    decimal TargetPackagedVolumeLiters,
    decimal? MeasuredPackagedVolumeLiters,
    decimal BoilOffRatePerHour,
    decimal GrainAbsorptionRateLPerKg,
    decimal KettleTrubLossLiters,
    decimal FermenterTrubLossLiters,
    decimal MashTunDeadSpaceLiters,
    decimal CoolingShrinkagePercent,
    decimal PackagingLossLiters,
    DateTime CreatedAt = default,
    DateTime UpdatedAt = default
);

public record BatchIngredientBackupDto(
    Guid Id,
    Guid? SourceIngredientId,
    string Name,
    IngredientType Type,
    decimal Amount,
    string Unit,
    IngredientUsage AdditionStage = IngredientUsage.Boil,
    int? AdditionTimeMinutes = null,
    bool IsChecked = false,
    bool IsDeducted = false,
    DateTime? DeductedAt = null,
    string? Notes = null,
    string? Form = null
);

public record BatchMashStepBackupDto(
    Guid Id,
    Guid? RecipeMashStepId,
    int StepOrder,
    string Name,
    MashStepType Type = MashStepType.Temperature,
    decimal TargetTemperatureC = 65.0m,
    int DurationMinutes = 60,
    int? RampTimeMinutes = null,
    decimal? InfuseAmountLiters = null,
    decimal? ActualTemperatureC = null,
    int? ActualDurationMinutes = null,
    bool IsCompleted = false,
    DateTime? CompletedAt = null,
    string? Notes = null
);

public record BatchFermentationStepBackupDto(
    Guid Id,
    Guid? RecipeFermentationStepId,
    int StepOrder,
    string Name,
    FermentationStepType Type = FermentationStepType.Primary,
    decimal TargetTemperatureC = 19.0m,
    decimal? ActualTemperatureC = null,
    int DurationDays = 14,
    int? RampTimeHours = null,
    decimal? TriggerGravity = null,
    string? Notes = null,
    bool IsCompleted = false,
    DateTime? StartedAt = null,
    DateTime? CompletedAt = null
);

public record BatchStageHistoryBackupDto(
    Guid Id,
    BrewStage Stage,
    StageStatus Status = StageStatus.Pending,
    DateTime? StartedAt = null,
    DateTime? CompletedAt = null,
    int? DurationMinutes = null,
    string? Notes = null
);

public record BatchReadingBackupDto(
    Guid Id,
    DateTime Timestamp,
    decimal SpecificGravity,
    decimal? TemperatureC = null,
    string? Notes = null,
    DateTime CreatedAt = default
);

public record BatchSensorAssignmentBackupDto(
    Guid Id,
    Guid EquipmentId,
    BrewStage? Stage = null,
    Guid? BatchMashStepId = null,
    DateTime CreatedAt = default
);

public record BatchBackupDto(
    Guid Id,
    Guid? RecipeId,
    Guid? BrewerySetupId,
    string BatchCode,
    string Name,
    string BeerStyle = "American IPA",
    BatchStatus Status = BatchStatus.Brewing,
    BrewStage CurrentStage = BrewStage.Mash,
    DateOnly BrewDate = default,
    decimal TargetOg = 1.050m,
    decimal TargetFg = 1.010m,
    decimal TargetAbv = 5.25m,
    decimal TargetIbu = 35.0m,
    decimal TargetColorSrm = 6.0m,
    decimal TargetBatchSizeLiters = 20.0m,
    int BoilTimeMinutes = 60,
    decimal EfficiencyPercent = 72.0m,
    decimal? MeasuredOg = null,
    decimal? CurrentGravity = null,
    decimal? MeasuredFg = null,
    decimal? AlcoholByVolume = null,
    decimal? BrewhouseEfficiency = null,
    decimal? MeasuredBatchSizeLiters = null,
    decimal? PitchTemperatureC = null,
    Guid? BoilerId = null,
    Guid? FermenterId = null,
    Guid? PackagingVesselId = null,
    string? Notes = null,
    DateTime CreatedAt = default,
    DateTime UpdatedAt = default,
    DateTime? CompletedAt = null,
    BatchVolumeProfileBackupDto? VolumeProfile = null,
    List<BatchIngredientBackupDto>? Ingredients = null,
    List<BatchMashStepBackupDto>? MashSteps = null,
    List<BatchFermentationStepBackupDto>? FermentationSteps = null,
    List<BatchStageHistoryBackupDto>? StageHistory = null,
    List<BatchReadingBackupDto>? Readings = null,
    List<BatchSensorAssignmentBackupDto>? SensorAssignments = null
);

public record EquipmentReadingBackupDto(
    Guid Id,
    Guid EquipmentId,
    Guid? BatchId,
    BrewStage? Stage,
    Guid? BatchMashStepId,
    string? StepName,
    DateTime Timestamp,
    decimal TemperatureC,
    decimal? SpecificGravity = null,
    decimal? PressureBar = null,
    decimal? BatteryPercent = null,
    decimal? BatteryVoltage = null,
    decimal? TiltDegrees = null,
    short? Rssi = null,
    string? MetricsJson = null,
    string? Source = null,
    string? RawPayload = null,
    string? Notes = null,
    DateTime CreatedAt = default
);

public class BackupPayload
{
    public UserSettingsBackupDto? UserSettings { get; set; }
    public List<CustomIngredientBackupDto> CustomIngredients { get; set; } = [];
    public List<IngredientStockBackupDto> IngredientStocks { get; set; } = [];
    public List<BrewerySetupBackupDto> OwnedBrewerySetups { get; set; } = [];
    public List<SharedSetupMembershipBackupDto> SharedSetupMemberships { get; set; } = [];
    public List<EquipmentBackupDto> Equipment { get; set; } = [];
    public List<RecipeBackupDto> Recipes { get; set; } = [];
    public List<RecipeBackupDto> ReferencedExternalRecipes { get; set; } = [];
    public List<BatchBackupDto> Batches { get; set; } = [];
    public List<EquipmentReadingBackupDto> EquipmentReadings { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public record ExportBackupRequest(
    bool IncludeTelemetryReadings = true,
    bool IncludeSecrets = false,
    string? Passphrase = null
);

public record RestoreOptionsDto(
    RestoreConflictResolutionMode ConflictResolution = RestoreConflictResolutionMode.MergeSkipExisting,
    bool RestoreUserSettings = true,
    bool RestoreSharedSetups = true,
    SharedSetupFallbackMode SharedSetupFallback = SharedSetupFallbackMode.CloneAsPersonal,
    CollaboratorRestoreMode CollaboratorRestore = CollaboratorRestoreMode.RestoreMembershipsIfAvailable,
    bool RestoreTelemetryReadings = true,
    string? Passphrase = null
);

public record PreviewRestoreRequest(
    BrewYouBackupEnvelope Backup,
    RestoreOptionsDto? Options = null
);

public record ExecuteRestoreRequest(
    BrewYouBackupEnvelope Backup,
    RestoreOptionsDto? Options = null
);

public record BackupItemCountsDto(
    int OwnedSetups,
    int SharedSetups,
    int Equipment,
    int CustomIngredients,
    int IngredientStocks,
    int Recipes,
    int Batches,
    int TelemetryReadings,
    int Collaborators,
    int PendingInvites
);

public record BackupConflictSummaryDto(
    int MatchingSetups,
    int MatchingEquipment,
    int MatchingCustomIngredients,
    int MatchingRecipes,
    int MatchingBatches
);

public record SharedSetupPreviewItemDto(
    Guid OriginalSetupId,
    string SetupName,
    string? OwnerDisplayName,
    BreweryRole Role,
    bool LiveSetupAvailable,
    string PlannedAction
);

public record RestorePreviewReportDto(
    bool IsValid,
    bool ChecksumVerified,
    int BackupSchemaVersion,
    int CurrentSchemaVersion,
    bool WasUpgraded,
    List<string> MigrationNotes,
    string? BackupAppVersion,
    DateTime? ExportedAtUtc,
    bool SecretsEncrypted,
    bool PassphraseRequired,
    bool PassphraseValid,
    BackupItemCountsDto Counts,
    BackupConflictSummaryDto Conflicts,
    List<SharedSetupPreviewItemDto> SharedSetups,
    List<string> Warnings,
    List<string> Errors
);

public record RestoreActionCountsDto(
    int SetupsCreated,
    int SetupsUpdated,
    int SetupsSkipped,
    int EquipmentCreated,
    int EquipmentUpdated,
    int EquipmentSkipped,
    int CustomIngredientsCreated,
    int CustomIngredientsUpdated,
    int CustomIngredientsSkipped,
    int StocksUpserted,
    int RecipesCreated,
    int RecipesUpdated,
    int RecipesSkipped,
    int BatchesCreated,
    int BatchesUpdated,
    int BatchesSkipped,
    int TelemetryReadingsRestored,
    int CollaboratorsRestored
);

public record RestoredInviteInfoDto(
    Guid BrewerySetupId,
    string BrewerySetupName,
    string InvitedEmail,
    BreweryRole Role,
    string InviteCode
);

public record RestoreResultDto(
    bool Succeeded,
    int BackupSchemaVersion,
    int CurrentSchemaVersion,
    bool SettingsRestored,
    RestoreActionCountsDto Summary,
    List<RestoredInviteInfoDto> GeneratedInvites,
    List<string> Warnings
);