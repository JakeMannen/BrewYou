namespace BrewYou.ApiService.Data.Entities;

public class BatchSensorAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BatchId { get; set; }
    public Batch Batch { get; set; } = null!;

    public Guid EquipmentId { get; set; }
    public Equipment Equipment { get; set; } = null!;

    /// <summary>
    /// Optional stage scope. If null, the sensor is active across all stages of the batch.
    /// </summary>
    public BrewStage? Stage { get; set; }

    /// <summary>
    /// Optional mash step scope. If set, sensor logs readings specifically to this mash step.
    /// </summary>
    public Guid? BatchMashStepId { get; set; }
    public BatchMashStep? BatchMashStep { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}