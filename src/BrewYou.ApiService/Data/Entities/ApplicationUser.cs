using Microsoft.AspNetCore.Identity;
using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Data.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WeightUnit
{
    Metric,
    Imperial
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TemperatureUnit
{
    Celsius,
    Fahrenheit
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GravityUnit
{
    SpecificGravity,
    Plato
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThemePreference
{
    Dark,
    Light,
    System,
    ImperialStout,
    ChocolatePorter,
    Obsidian
}

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
    public string PreferredLanguage { get; set; } = "en";
    public VolumeUnit PreferredVolumeUnit { get; set; } = VolumeUnit.Liters;
    public WeightUnit PreferredWeightUnit { get; set; } = WeightUnit.Metric;
    public TemperatureUnit PreferredTemperatureUnit { get; set; } = TemperatureUnit.Celsius;
    public GravityUnit PreferredGravityUnit { get; set; } = GravityUnit.SpecificGravity;
    public ThemePreference PreferredTheme { get; set; } = ThemePreference.ImperialStout;
    public decimal DefaultBatchSizeLiters { get; set; } = 20.0m;
    public decimal DefaultEfficiencyPercent { get; set; } = 75.0m;
    public int DefaultBoilTimeMinutes { get; set; } = 60;
    public string? MqttHost { get; set; }
    public int? MqttPort { get; set; } = 1883;
    public string? MqttUsername { get; set; }
    public string? MqttPassword { get; set; }
    public string? MqttCertificate { get; set; }
    public string? MqttTopicPrefix { get; set; } = "brewyou/equipment";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Recipe> Recipes { get; set; } = new List<Recipe>();
    public ICollection<Equipment> Equipment { get; set; } = new List<Equipment>();
    public ICollection<BrewerySetup> BrewerySetups { get; set; } = new List<BrewerySetup>();
}