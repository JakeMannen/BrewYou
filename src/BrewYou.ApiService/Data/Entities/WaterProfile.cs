namespace BrewYou.ApiService.Data.Entities;

/// <summary>
/// Represents a water chemistry profile (either a built-in target preset or user-saved source/target profile).
/// All mineral concentrations are stored in ppm (mg/L).
/// </summary>
public class WaterProfile
{
    public Guid Id { get; set; }
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// Profile category: "Source" or "Target".
    /// </summary>
    public string Category { get; set; } = "Target";

    public bool IsPreset { get; set; }

    public decimal CalciumPpm { get; set; }
    public decimal MagnesiumPpm { get; set; }
    public decimal SodiumPpm { get; set; }
    public decimal SulfatePpm { get; set; }
    public decimal ChloridePpm { get; set; }
    public decimal BicarbonatePpm { get; set; }
    public decimal? Ph { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}