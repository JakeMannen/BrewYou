using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Data.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MashStepType
{
    Infusion,
    Temperature,
    Decoction
}

public class RecipeMashStep
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RecipeId { get; set; }
    public Recipe? Recipe { get; set; }

    /// <summary>
    /// 1-based order of the mash step (1, 2, 3...)
    /// </summary>
    public int StepOrder { get; set; }

    public required string Name { get; set; }

    public MashStepType Type { get; set; } = MashStepType.Temperature;

    /// <summary>
    /// Target step temperature in degrees Celsius (e.g. 65.0°C)
    /// </summary>
    public decimal TemperatureC { get; set; }

    /// <summary>
    /// Rest hold duration in minutes
    /// </summary>
    public int DurationMinutes { get; set; }

    /// <summary>
    /// Optional ramp time to reach temperature from previous rest (minutes)
    /// </summary>
    public int? RampTimeMinutes { get; set; }

    /// <summary>
    /// Optional water addition volume for infusion or decoction steps (liters)
    /// </summary>
    public decimal? InfuseAmountLiters { get; set; }

    public string? Notes { get; set; }
}