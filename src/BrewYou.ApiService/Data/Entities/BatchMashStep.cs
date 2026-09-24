namespace BrewYou.ApiService.Data.Entities;

public class BatchMashStep
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }

    /// <summary>
    /// Optional reference to the source recipe step for auditing.
    /// Not an enforced foreign key dependency to allow deep decoupling.
    /// </summary>
    public Guid? RecipeMashStepId { get; set; }

    /// <summary>
    /// 1-based order of the mash step (1, 2, 3...)
    /// </summary>
    public int StepOrder { get; set; }

    public required string Name { get; set; }

    public MashStepType Type { get; set; } = MashStepType.Temperature;

    // Target specifications snapshotted at creation
    public decimal TargetTemperatureC { get; set; }
    public int DurationMinutes { get; set; }
    public int? RampTimeMinutes { get; set; }
    public decimal? InfuseAmountLiters { get; set; }

    // Actual brew day execution measurements
    public decimal? ActualTemperatureC { get; set; }
    public int? ActualDurationMinutes { get; set; }
    public bool IsCompleted { get; set; } = false;
    public DateTime? CompletedAt { get; set; }

    public string? Notes { get; set; }
}