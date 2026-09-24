namespace BrewYou.ApiService.Data.Entities;

public class IngredientStock
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public Guid IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }

    /// <summary>
    /// Current quantity in stock (kg for fermentables, grams for hops/yeast/other by default)
    /// </summary>
    public decimal Amount { get; set; } = 0.0m;

    /// <summary>
    /// Display and measurement unit ("kg", "g", "pkg", "items")
    /// </summary>
    public string Unit { get; set; } = "kg";

    public decimal? LowStockAlertThreshold { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}