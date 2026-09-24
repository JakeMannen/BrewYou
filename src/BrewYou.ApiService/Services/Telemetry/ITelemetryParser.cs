using BrewYou.ApiService.Data.Entities;
using System.Text.Json;

namespace BrewYou.ApiService.Services.Telemetry;

public record NormalizedTelemetryData(
    decimal TemperatureC,
    decimal? SpecificGravity = null,
    decimal? PressureBar = null,
    decimal? BatteryPercent = null,
    decimal? BatteryVoltage = null,
    decimal? TiltDegrees = null,
    short? Rssi = null,
    string? MetricsJson = null,
    string? DetectedFormat = null
);

public interface ITelemetryParser
{
    int Priority { get; }
    bool CanParse(JsonElement root, Equipment equipment, string? preferredPath = null);
    NormalizedTelemetryData? Parse(JsonElement root, Equipment equipment, string? preferredPath = null);
}