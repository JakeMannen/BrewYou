namespace BrewYou.ApiService.Data.Entities;

public class BatchIngredient
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }

    public Guid? SourceIngredientId { get; set; }

    public required string Name { get; set; }
    public IngredientType Type { get; set; }
    public decimal Amount { get; set; }
    public required string Unit { get; set; } = "kg";

    public IngredientUsage AdditionStage { get; set; } = IngredientUsage.Boil;
    public int? AdditionTimeMinutes { get; set; }
    public bool IsChecked { get; set; } = false;
    public bool IsDeducted { get; set; } = false;
    public DateTime? DeductedAt { get; set; }

    public string? Notes { get; set; }
    public string? Form { get; set; }
}