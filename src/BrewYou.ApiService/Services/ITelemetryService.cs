using System.Text.Json;

namespace BrewYou.ApiService.Services;

public enum TelemetryIngestStatus
{
    Success,
    NotFound,
    InvalidPayload,
    OutOfRange
}

public record TelemetryIngestResult(
    TelemetryIngestStatus Status,
    decimal? TemperatureC = null,
    string? ErrorMessage = null,
    DateTime? Timestamp = null
);

public record EquipmentReadingDto(
    Guid Id,
    Guid EquipmentId,
    DateTime Timestamp,
    decimal TemperatureC,
    string? Source,
    DateTime CreatedAt,
    decimal? SpecificGravity = null,
    decimal? PressureBar = null,
    decimal? BatteryPercent = null,
    decimal? BatteryVoltage = null,
    decimal? TiltDegrees = null,
    short? Rssi = null,
    string? MetricsJson = null
);

public record TelemetryPollResult(
    bool Success,
    decimal? TemperatureC = null,
    string? ErrorMessage = null,
    string? ResponseSnippet = null
);

public interface ITelemetryService
{
    Task<TelemetryIngestResult> IngestTelemetryAsync(string token, JsonElement payload);
    Task<(bool Found, List<EquipmentReadingDto>? Readings)> GetEquipmentReadingsAsync(Guid equipmentId, string userId, int limit = 50);
    Task<TelemetryPollResult> TestPollAsync(Guid equipmentId, string userId);
    Task<TelemetryPollResult> PollEquipmentAsync(Guid equipmentId);
    Task<TelemetryIngestResult> IngestMqttTelemetryAsync(Guid equipmentId, string payload);
}