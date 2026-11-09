using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services.Telemetry;
using System.Collections.Concurrent;

namespace BrewYou.ApiService.Services;

public sealed class TelemetryThrottlingService : ITelemetryThrottlingService
{
    private static readonly TimeSpan DefaultThrottleInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromMinutes(10);
    private const decimal SignificantTempDelta = 0.5m;
    private const decimal SignificantGravityDelta = 0.001m;

    private readonly ConcurrentDictionary<Guid, EquipmentThrottleSnapshot> _snapshots = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _lastSeen = new();
    private readonly ConcurrentDictionary<Guid, object> _syncLocks = new();
    private readonly TimeSpan _throttleInterval;

    public TelemetryThrottlingService(TimeSpan? throttleInterval = null)
    {
        _throttleInterval = throttleInterval ?? DefaultThrottleInterval;
    }

    public bool ShouldPersist(
        Guid equipmentId,
        DateTime timestampUtc,
        decimal temperatureC,
        BrewLinkCanonicalMetrics? metrics = null,
        decimal? specificGravity = null,
        BrewStage? stage = null,
        Guid? batchId = null,
        Guid? batchMashStepId = null)
    {
        var syncLock = _syncLocks.GetOrAdd(equipmentId, _ => new object());

        lock (syncLock)
        {
            var isIdleTimeout = _lastSeen.TryGetValue(equipmentId, out var lastSeen) &&
                                (timestampUtc - lastSeen) > SessionIdleTimeout;
            _lastSeen[equipmentId] = timestampUtc;

            if (!_snapshots.TryGetValue(equipmentId, out var last) || isIdleTimeout)
            {
                // Condition 1: Initial reading for this equipment session
                _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage, batchId, batchMashStepId);
                return true;
            }

            // Condition 2: Time-based cadence (>= 30 seconds elapsed)
            if ((timestampUtc - last.TimestampUtc) >= _throttleInterval)
            {
                if (metrics != null && metrics.UpdatedAt != default && (timestampUtc - metrics.UpdatedAt) > SessionIdleTimeout)
                {
                    return false;
                }

                _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage, batchId, batchMashStepId);
                return true;
            }

            // Condition 3: Significant temperature delta (>= 0.5°C)
            try
            {
                if (Math.Abs(temperatureC - last.TemperatureC) >= SignificantTempDelta)
                {
                    _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage, batchId, batchMashStepId);
                    return true;
                }
            }
            catch (OverflowException)
            {
                // Extreme temperature delta -> persist immediately
                _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage, batchId, batchMashStepId);
                return true;
            }

            // Condition 4: Discrete hardware state change
            if (metrics != null)
            {
                // 4a. Heating state or power percentage change
                if (metrics.HeatActive != last.HeatActive || metrics.HeatPowerPct != last.HeatPowerPct)
                {
                    _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage, batchId, batchMashStepId);
                    return true;
                }

                // 4b. Pump state change
                if (metrics.PumpActive != last.PumpActive)
                {
                    _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage, batchId, batchMashStepId);
                    return true;
                }

                // 4c. Timer active or paused state change (ignoring 1Hz remainingSeconds countdown)
                if (metrics.Timer?.Active != last.TimerActive || metrics.Timer?.Paused != last.TimerPaused)
                {
                    _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage, batchId, batchMashStepId);
                    return true;
                }

                // 4d. User interaction alert prompt required or code change
                if (metrics.Interaction?.Required != last.InteractionRequired ||
                    metrics.Interaction?.Code != last.InteractionCode)
                {
                    _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage, batchId, batchMashStepId);
                    return true;
                }

                // 4e. Stage step index or step name change
                if (metrics.Stage?.StepIndex != last.StepIndex ||
                    !string.Equals(metrics.Stage?.StepName, last.StepName, StringComparison.Ordinal))
                {
                    _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage, batchId, batchMashStepId);
                    return true;
                }
            }

            // 4f. Active brew stage transition
            if (stage.HasValue && stage != last.Stage)
            {
                _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage, batchId, batchMashStepId);
                return true;
            }

            // 4h. Batch or mash step transition (guarantees immediate persistence for a new mash step or new batch)
            if (batchId.HasValue && (batchId != last.BatchId || batchMashStepId != last.BatchMashStepId))
            {
                _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage, batchId, batchMashStepId);
                return true;
            }

            // 4i. Active mash or boil stage for an in-progress batch: persist on every temperature update
            if (batchId.HasValue && stage is BrewStage.Mash or BrewStage.Boil)
            {
                _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage, batchId, batchMashStepId);
                return true;
            }

            // 4g. Specific gravity change >= 0.001
            if (specificGravity.HasValue && last.SpecificGravity.HasValue)
            {
                try
                {
                    if (Math.Abs(specificGravity.Value - last.SpecificGravity.Value) >= SignificantGravityDelta)
                    {
                        _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage, batchId, batchMashStepId);
                        return true;
                    }
                }
                catch (OverflowException)
                {
                    _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage, batchId, batchMashStepId);
                    return true;
                }
            }

            // All conditions false -> throttle persistence
            return false;
        }
    }

    public void Reset(Guid equipmentId)
    {
        _snapshots.TryRemove(equipmentId, out _);
        _lastSeen.TryRemove(equipmentId, out _);
    }

    public EquipmentThrottleSnapshot? GetLastPersisted(Guid equipmentId)
    {
        return _snapshots.TryGetValue(equipmentId, out var snapshot) ? snapshot : null;
    }

    private static EquipmentThrottleSnapshot CreateSnapshot(
        DateTime timestampUtc,
        decimal temperatureC,
        BrewLinkCanonicalMetrics? metrics,
        decimal? specificGravity,
        BrewStage? stage,
        Guid? batchId,
        Guid? batchMashStepId)
    {
        return new EquipmentThrottleSnapshot(
            TimestampUtc: timestampUtc,
            TemperatureC: temperatureC,
            HeatActive: metrics?.HeatActive,
            HeatPowerPct: metrics?.HeatPowerPct,
            PumpActive: metrics?.PumpActive,
            TimerActive: metrics?.Timer?.Active,
            TimerPaused: metrics?.Timer?.Paused,
            InteractionRequired: metrics?.Interaction?.Required,
            InteractionCode: metrics?.Interaction?.Code,
            StepIndex: metrics?.Stage?.StepIndex,
            StepName: metrics?.Stage?.StepName,
            Stage: stage,
            SpecificGravity: specificGravity,
            BatchId: batchId,
            BatchMashStepId: batchMashStepId
        );
    }
}