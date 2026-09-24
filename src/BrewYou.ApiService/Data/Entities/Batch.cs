using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Data.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BatchStatus
{
    Planned,
    Brewing,
    Fermenting,
    Conditioning,
    Completed,
    Archived
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BrewStage
{
    Mash,
    Boil,
    Ferment,
    Condition,
    Package
}

public class Batch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public Guid? RecipeId { get; set; }
    public Recipe? Recipe { get; set; }

    public Guid? BrewerySetupId { get; set; }
    public BrewerySetup? BrewerySetup { get; set; }

    public required string BatchCode { get; set; }
    public required string Name { get; set; }
    public string BeerStyle { get; set; } = "American IPA";

    public BatchStatus Status { get; set; } = BatchStatus.Brewing;
    public BrewStage CurrentStage { get; set; } = BrewStage.Mash;

    public DateOnly BrewDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    // Target specifications snapshotted at creation
    public decimal TargetOg { get; set; } = 1.050m;
    public decimal TargetFg { get; set; } = 1.010m;
    public decimal TargetAbv { get; set; } = 5.25m;
    public decimal TargetIbu { get; set; } = 35.0m;
    public decimal TargetColorSrm { get; set; } = 6.0m;
    public decimal TargetBatchSizeLiters { get; set; } = 20.0m;
    public int BoilTimeMinutes { get; set; } = 60;
    public decimal EfficiencyPercent { get; set; } = 72.0m;

    // Actual measured brewing & fermentation metrics
    public decimal? MeasuredOg { get; set; }
    public decimal? CurrentGravity { get; set; }
    public decimal? MeasuredFg { get; set; }
    public decimal? AlcoholByVolume { get; set; }
    public decimal? BrewhouseEfficiency { get; set; }
    public decimal? MeasuredBatchSizeLiters { get; set; }
    public decimal? PitchTemperatureC { get; set; }

    // Equipment assignments
    public Guid? BoilerId { get; set; }
    public Equipment? Boiler { get; set; }

    public Guid? FermenterId { get; set; }
    public Equipment? Fermenter { get; set; }

    public Guid? PackagingVesselId { get; set; }
    public Equipment? PackagingVessel { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public ICollection<BatchReading> Readings { get; set; } = new List<BatchReading>();
    public ICollection<EquipmentReading> EquipmentReadings { get; set; } = new List<EquipmentReading>();
    public ICollection<BatchIngredient> Ingredients { get; set; } = new List<BatchIngredient>();
    public ICollection<BatchMashStep> MashSteps { get; set; } = new List<BatchMashStep>();
    public ICollection<BatchFermentationStep> FermentationSteps { get; set; } = new List<BatchFermentationStep>();
    public ICollection<BatchStageHistory> StageHistory { get; set; } = new List<BatchStageHistory>();
    public ICollection<BatchSensorAssignment> SensorAssignments { get; set; } = new List<BatchSensorAssignment>();
    public BatchVolumeProfile? VolumeProfile { get; set; }
}