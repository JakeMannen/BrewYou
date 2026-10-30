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
    Bottling,
    FirstWort
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
    /// Canonical duration in minutes (boil time, first wort boil time, whirlpool steep time, or dry hop duration in minutes; e.g. 4320 = 3 days)
    /// </summary>
    public int? DurationMinutes { get; set; }

    public IngredientUsage Usage { get; set; } = IngredientUsage.Boil;

    public string? Notes { get; set; }
    public string? Form { get; set; }
}