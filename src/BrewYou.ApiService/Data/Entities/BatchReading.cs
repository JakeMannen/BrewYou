namespace BrewYou.ApiService.Data.Entities;

public class BatchReading
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public decimal SpecificGravity { get; set; }
    public decimal? TemperatureC { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}