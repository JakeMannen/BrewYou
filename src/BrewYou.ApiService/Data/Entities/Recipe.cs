namespace BrewYou.ApiService.Data.Entities;

public class Recipe
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }
    public string BeerStyle { get; set; } = "American IPA";

    /// <summary>
    /// Target final batch volume in liters (standard metric)
    /// </summary>
    public decimal BatchSizeLiters { get; set; } = 20.0m;

    /// <summary>
    /// Boil duration in minutes (standard: 60 min)
    /// </summary>
    public int BoilTimeMinutes { get; set; } = 60;

    /// <summary>
    /// Brewhouse / mash efficiency percentage (e.g., 72%)
    /// </summary>
    public decimal EfficiencyPercent { get; set; } = 72.0m;

    // Calculated brewing metrics
    public decimal OriginalGravity { get; set; } = 1.050m;
    public decimal FinalGravity { get; set; } = 1.010m;
    public decimal AlcoholByVolume { get; set; } = 5.25m;
    public decimal BitternessIbu { get; set; } = 35.0m;
    public decimal ColorSrm { get; set; } = 6.0m;

    public bool IsPublic { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RecipeIngredient> Ingredients { get; set; } = new List<RecipeIngredient>();
}
