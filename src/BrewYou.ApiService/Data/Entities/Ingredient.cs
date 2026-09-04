using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Data.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IngredientType
{
    Fermentable,
    Hop,
    Yeast,
    Other
}

public class Ingredient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public IngredientType Type { get; set; }

    /// <summary>
    /// For Fermentables: Specific gravity yield in 1 gallon/1 lb (e.g., 1.037)
    /// </summary>
    public decimal? PotentialGravity { get; set; }

    /// <summary>
    /// For Fermentables: Color contribution in Lovibond/SRM
    /// </summary>
    public decimal? ColorSrm { get; set; }

    /// <summary>
    /// For Hops: Alpha acid percentage (e.g., 13.5)
    /// </summary>
    public decimal? AlphaAcidPercent { get; set; }

    /// <summary>
    /// For Yeasts: Typical attenuation percentage (e.g., 78.0)
    /// </summary>
    public decimal? AttenuationPercent { get; set; }

    public string? Description { get; set; }
    public bool IsCatalogItem { get; set; } = true;
    public string? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RecipeIngredient> RecipeIngredients { get; set; } = new List<RecipeIngredient>();
}
