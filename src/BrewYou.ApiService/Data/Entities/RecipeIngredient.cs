using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Data.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IngredientUsage
{
    Mash,
    Boil,
    Whirlpool,
    Primary,
    Secondary,
    DryHop,
    Bottling
}

public class RecipeIngredient
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RecipeId { get; set; }
    public Recipe? Recipe { get; set; }

    public Guid IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }

    /// <summary>
    /// Amount in metric: kg for fermentables, grams for hops/yeast/other
    /// </summary>
    public decimal Amount { get; set; }

    public required string Unit { get; set; } = "kg";

    /// <summary>
    /// Duration in minutes (boil time, whirlpool time) or days (dry hop)
    /// </summary>
    public int? DurationMinutes { get; set; }

    public IngredientUsage Usage { get; set; } = IngredientUsage.Boil;

    public string? Notes { get; set; }
}
