using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services.Telemetry;

namespace BrewYou.ApiService.Services;

public sealed record EquipmentThrottleSnapshot(
    DateTime TimestampUtc,
    decimal TemperatureC,
    bool? HeatActive,
    decimal? HeatPowerPct,
    bool? PumpActive,
    bool? TimerActive,
    bool? TimerPaused,
    bool? InteractionRequired,
    int? InteractionCode,
    int? StepIndex,
    string? StepName,
    BrewStage? Stage,
    decimal? SpecificGravity,
    Guid? BatchId = null,
    Guid? BatchMashStepId = null
);

public interface ITelemetryThrottlingService
{
    bool ShouldPersist(
        Guid equipmentId,
        DateTime timestampUtc,
        decimal temperatureC,
        BrewLinkCanonicalMetrics? metrics = null,
        decimal? specificGravity = null,
        BrewStage? stage = null,
        Guid? batchId = null,
        Guid? batchMashStepId = null);

    void Reset(Guid equipmentId);

    EquipmentThrottleSnapshot? GetLastPersisted(Guid equipmentId);
}