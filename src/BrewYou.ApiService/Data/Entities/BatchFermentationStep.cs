namespace BrewYou.ApiService.Data.Entities;

public class BatchFermentationStep
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }

    public Guid? RecipeFermentationStepId { get; set; }

    /// <summary>
    /// 1-based order of the fermentation step (1, 2, 3...)
    /// </summary>
    public int StepOrder { get; set; }

    public required string Name { get; set; }

    public FermentationStepType Type { get; set; } = FermentationStepType.Primary;

    /// <summary>
    /// Target step temperature in degrees Celsius (e.g. 19.0°C)
    /// </summary>
    public decimal TargetTemperatureC { get; set; }

    /// <summary>
    /// Actual measured or logged temperature for this step
    /// </summary>
    public decimal? ActualTemperatureC { get; set; }

    /// <summary>
    /// Target step duration in days
    /// </summary>
    public int DurationDays { get; set; }

    /// <summary>
    /// Optional ramp time in hours to reach target temperature
    /// </summary>
    public int? RampTimeHours { get; set; }

    /// <summary>
    /// Optional specific gravity trigger threshold (e.g. 1.018)
    /// </summary>
    public decimal? TriggerGravity { get; set; }

    public string? Notes { get; set; }

    public bool IsCompleted { get; set; } = false;

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}