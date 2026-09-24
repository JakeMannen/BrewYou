using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Data.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StageStatus
{
    Pending,
    InProgress,
    Completed,
    Skipped
}

public class BatchStageHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }

    public BrewStage Stage { get; set; }
    public StageStatus Status { get; set; } = StageStatus.Pending;

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Notes { get; set; }
}