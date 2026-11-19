namespace BrewYou.ApiService.Data.Entities;

/// <summary>
/// Structured sensory tasting note & BJCP-style scoresheet recorded over time for a batch.
/// </summary>
public class BatchTasting
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Batch Batch { get; set; } = null!;

    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public DateTime TastedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Sample source: "HydrometerSample", "BrightTank", "Keg", "Bottle", "Can", "Cask", or "Other".
    /// </summary>
    public string? SampleSource { get; set; } = "Keg";

    // BJCP-style 50-point scoresheet breakdown
    public decimal AromaScore { get; set; } // 0 - 12
    public string? AromaNotes { get; set; }
    public decimal AppearanceScore { get; set; } // 0 - 3
    public string? AppearanceNotes { get; set; }
    public decimal FlavorScore { get; set; } // 0 - 20
    public string? FlavorNotes { get; set; }
    public decimal MouthfeelScore { get; set; } // 0 - 5
    public string? MouthfeelNotes { get; set; }
    public decimal OverallScore { get; set; } // 0 - 10
    public string? OverallNotes { get; set; }
    public decimal TotalScore { get; set; } // 0 - 50

    public int StarRating { get; set; } = 3; // 1 - 5
    public bool WouldRebrew { get; set; } = true;

    /// <summary>
    /// Detected off-flavor keys: Diacetyl, Dms, Acetaldehyde, Oxidation, Phenolic, Autolysis, Lightstruck.
    /// </summary>
    public List<string> OffFlavors { get; set; } = [];

    public decimal? ServingTemperatureC { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}