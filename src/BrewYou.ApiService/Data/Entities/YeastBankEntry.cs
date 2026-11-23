namespace BrewYou.ApiService.Data.Entities;

/// <summary>
/// Represents harvested yeast slurry or banked yeast stored by a brewer for tracking generations and repitching.
/// </summary>
public class YeastBankEntry
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public Guid? IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }

    public required string StrainName { get; set; }
    public string? LabOrBrand { get; set; }
    public string? ProductCode { get; set; }

    /// <summary>
    /// Form factor: "Slurry", "Liquid", or "Dry".
    /// </summary>
    public string Form { get; set; } = "Slurry";

    public int Generation { get; set; } = 1;
    public DateTime HarvestDate { get; set; } = DateTime.UtcNow;

    public Guid? SourceBatchId { get; set; }
    public Batch? SourceBatch { get; set; }
    public string? SourceBatchName { get; set; }
    public string? SourceBatchCode { get; set; }

    public decimal VolumeMl { get; set; }
    public decimal EstimatedDensityBillionPerMl { get; set; } = 1.0m;
    public decimal BaseViabilityPercent { get; set; } = 95m;

    public string? StorageLocation { get; set; }

    /// <summary>
    /// Availability status: "Available", "Depleted", or "Discarded".
    /// </summary>
    public string Status { get; set; } = "Available";

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}