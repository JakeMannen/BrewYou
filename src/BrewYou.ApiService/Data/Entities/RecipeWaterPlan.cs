namespace BrewYou.ApiService.Data.Entities;

/// <summary>
/// Persisted per-recipe water chemistry plan covering source/target mineral snapshots,
/// mash and sparge salt/acid additions, sulfate-to-chloride balance, residual alkalinity, and predicted mash pH.
/// </summary>
public class RecipeWaterPlan
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    public Guid? SourceProfileId { get; set; }
    public WaterProfile? SourceProfile { get; set; }
    public string? SourceProfileName { get; set; }
    public decimal SourceCalciumPpm { get; set; }
    public decimal SourceMagnesiumPpm { get; set; }
    public decimal SourceSodiumPpm { get; set; }
    public decimal SourceSulfatePpm { get; set; }
    public decimal SourceChloridePpm { get; set; }
    public decimal SourceBicarbonatePpm { get; set; }
    public decimal SourcePh { get; set; } = 7.0m;

    public Guid? TargetProfileId { get; set; }
    public WaterProfile? TargetProfile { get; set; }
    public string? TargetProfileName { get; set; }
    public decimal? TargetCalciumPpm { get; set; }
    public decimal? TargetMagnesiumPpm { get; set; }
    public decimal? TargetSodiumPpm { get; set; }
    public decimal? TargetSulfatePpm { get; set; }
    public decimal? TargetChloridePpm { get; set; }
    public decimal? TargetBicarbonatePpm { get; set; }
    public decimal TargetMashPh { get; set; } = 5.4m;

    // Mash water salt & acid additions (for the recipe's strike/mash volume)
    public decimal MashGypsumGrams { get; set; }
    public decimal MashCalciumChlorideGrams { get; set; }
    public decimal MashEpsomSaltGrams { get; set; }
    public decimal MashTableSaltGrams { get; set; }
    public decimal MashBakingSodaGrams { get; set; }
    public decimal MashChalkGrams { get; set; }
    public string? MashAcidType { get; set; } = "Lactic80";
    public decimal MashAcidAmountMlOrG { get; set; }

    // Sparge water salt & acid additions (for the recipe's sparge volume)
    public decimal SpargeGypsumGrams { get; set; }
    public decimal SpargeCalciumChlorideGrams { get; set; }
    public decimal SpargeEpsomSaltGrams { get; set; }
    public decimal SpargeTableSaltGrams { get; set; }
    public decimal SpargeBakingSodaGrams { get; set; }
    public decimal SpargeChalkGrams { get; set; }
    public string? SpargeAcidType { get; set; } = "Lactic80";
    public decimal SpargeAcidAmountMlOrG { get; set; }

    // Computed water chemistry summary
    public decimal ResultingCalciumPpm { get; set; }
    public decimal ResultingMagnesiumPpm { get; set; }
    public decimal ResultingSodiumPpm { get; set; }
    public decimal ResultingSulfatePpm { get; set; }
    public decimal ResultingChloridePpm { get; set; }
    public decimal ResultingBicarbonatePpm { get; set; }
    public decimal? SulfateToChlorideRatio { get; set; }
    public decimal ResidualAlkalinityPpm { get; set; }
    public decimal PredictedMashPh { get; set; }

    public string? Notes { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}