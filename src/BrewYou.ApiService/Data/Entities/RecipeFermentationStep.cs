using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Data.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FermentationStepType
{
    Primary,
    DiacetylRest,
    Ramp,
    FreeRise,
    Secondary,
    ColdCrash,
    Conditioning
}

public class RecipeFermentationStep
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RecipeId { get; set; }
    public Recipe? Recipe { get; set; }

    /// <summary>
    /// 1-based order of the fermentation step (1, 2, 3...)
    /// </summary>
    public int StepOrder { get; set; }

    public required string Name { get; set; }

    public FermentationStepType Type { get; set; } = FermentationStepType.Primary;

    /// <summary>
    /// Target step temperature in degrees Celsius (e.g. 19.0°C)
    /// </summary>
    public decimal TargetTemperatureC { get; set; }

    /// <summary>
    /// Step duration in days (e.g. 14 days)
    /// </summary>
    public int DurationDays { get; set; }

    /// <summary>
    /// Optional ramp time in hours to reach target temperature from previous step
    /// </summary>
    public int? RampTimeHours { get; set; }

    /// <summary>
    /// Optional specific gravity trigger threshold (e.g. 1.018)
    /// </summary>
    public decimal? TriggerGravity { get; set; }

    public string? Notes { get; set; }
}