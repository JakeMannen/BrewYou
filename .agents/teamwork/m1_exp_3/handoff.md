# Handoff Report: Milestone 1 — Immediate SSE Broadcast and Throttled Database Persistence

**Agent**: `m1_exp_3`  
**Working Directory**: `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_exp_3\`  
**Target Milestone**: Milestone 1 (Telemetry Ingestion, Normalization & DB Throttling)  
**Parent Conversation ID**: `6986b155-3bf8-4b59-9b95-f99c2273b921`  

---

## 1. Observation

### 1.1. Authoritative Requirements
- **`ORIGINAL_REQUEST.md:11-12` (Requirement R1)**:
  > "Implement real-time telemetry ingestion from BrewLink gateways across WebSocket streaming (`ws://<gateway-ip>/ws?token=<api_token>`), MQTT broker topics (`brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry`), and HTTP REST polling fallback (`GET /api/equipment`). Parse and normalize incoming device state (temperature, heating, pump, timer, stage, and user interaction alerts) into `Equipment.LatestMetricsJson`, broadcast live updates over SSE immediately, and throttle database persistence of `EquipmentReading` records to avoid database bloat."
- **`ORIGINAL_REQUEST.md:30` (Acceptance Criteria)**:
  > "Telemetry ingestion broadcasts immediate SSE events on state changes and throttles `EquipmentReading` database persistence to 30-second intervals or significant state/temperature deltas."
- **Dispatch Mandate**:
  1. Design broadcast flow: how parsed telemetry triggers immediate SSE updates on `GET /api/v1/inventory/equipment/telemetry-stream` (and batch streams if assigned to an active stage) via `ITelemetryBroadcastService`.
  2. Design exact database throttling algorithm for `EquipmentReading` records:
     - Avoid persisting 1Hz frames to PostgreSQL.
     - Persist only if:
       1. First reading for this equipment session, OR
       2. Elapsed time since last persisted reading $\ge 30$ seconds, OR
       3. Temperature delta $|\Delta T| \ge 0.5^\circ\text{C}$, OR
       4. Discrete hardware state change (heating active/power change, pump state change, timer active/paused, prompt interaction required/cleared, stage change).
  3. Detail in-memory throttling cache / tracking mechanism to avoid DB queries during high-frequency ingestion.
  4. Detail integration test specifications for `TelemetryTests.cs` verifying zero-delay SSE broadcast and 30s/delta persistence throttling.

### 1.2. Existing Codebase Infrastructure
- **`src/BrewYou.ApiService/Services/ITelemetryBroadcastService.cs:54-85`**:
  - `EquipmentTelemetryUpdateDto`:
    ```csharp
    public record EquipmentTelemetryUpdateDto(
        Guid EquipmentId,
        string EquipmentName,
        decimal TemperatureC,
        DateTime Timestamp,
        string? Source = null,
        Guid? BatchId = null,
        decimal? SpecificGravity = null,
        decimal? CurrentVolumeLiters = null,
        decimal? FillPercentage = null,
        decimal? PressureBar = null,
        decimal? BatteryPercent = null,
        decimal? BatteryVoltage = null,
        decimal? TiltDegrees = null,
        short? Rssi = null,
        string? MetricsJson = null
    );
    ```
  - Interface methods:
    - `void BroadcastEquipmentReading(string userId, EquipmentTelemetryUpdateDto update);`
    - `EquipmentTelemetrySubscription? SubscribeEquipment(string userId);`
    - `void BroadcastReading(BatchEquipmentReadingDto reading);`
    - `TelemetrySubscription? Subscribe(Guid batchId);`
- **`src/BrewYou.ApiService/Services/TelemetryBroadcastService.cs:111-139`**:
  - `_userEquipmentSubscriptions`: `ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<EquipmentTelemetryUpdateDto>>>`.
  - Channel configuration: bounded capacity = 100, `BoundedChannelFullMode.DropOldest`, up to 20 subscribers per user.
  - `BroadcastEquipmentReading` writes to all active user channels non-blockingly via `channel.Writer.TryWrite(update)`.
- **`src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs:27-107`**:
  - `GET /api/v1/inventory/equipment/telemetry-stream`:
    - Authenticated via JWT (`RequireAuthorization()`), resolves `userId` via `ClaimTypes.NameIdentifier`.
    - Returns `text/event-stream`, `no-cache, no-transform`, `X-Accel-Buffering: no`.
    - Streams `event: reading\ndata: {json}\n\n` with `SseJsonOptions` (camelCase naming policy).
    - Periodic heartbeat `: ping\n\n` every 15 seconds.
- **`src/BrewYou.ApiService/Endpoints/BatchEndpoints.cs:469-520`**:
  - `GET /api/v1/batches/{id}/telemetry-stream`:
    - Checks batch ownership (`GetBatchByIdAsync`).
    - Subscribes via `broadcastService.Subscribe(id)` to batch channel.
    - Streams `event: reading\ndata: {json}\n\n` containing `BatchEquipmentReadingDto`.
- **`src/BrewYou.ApiService/Endpoints/BatchDtos.cs:401-421`**:
  - `BatchEquipmentReadingDto` has fields: `Id`, `EquipmentId`, `EquipmentName`, `BatchId`, `Stage`, `BatchMashStepId`, `StepName`, `TemperatureC`, `Timestamp`, `Source`, `TargetTemperatureC`, `Notes`, `SpecificGravity`, `PressureBar`, `BatteryPercent`, `BatteryVoltage`, `TiltDegrees`, `Rssi`, `MetricsJson`.
- **Current Database Persistence Defect in `src/BrewYou.ApiService/Services/TelemetryService.cs:139-155 & 745`**:
  - In `CreateAndAssociateReadingAsync` line 745:
    ```csharp
    _db.EquipmentReadings.Add(reading);
    return (reading, targetTempC, recordedGravity);
    ```
  - Followed immediately in `IngestTelemetryAsync` lines 153-154 by:
    ```csharp
    await _db.SaveChangesAsync();
    BroadcastIfAssociated(reading, equipment, targetTempC, recordedGravity);
    ```
  - **Direct Defect**: Every incoming frame triggers `_db.EquipmentReadings.Add(reading)` and `await _db.SaveChangesAsync()` before SSE broadcast. At 1Hz streaming over WebSocket, an unthrottled ingestion loop creates 3,600 rows/hour (86,400 rows/day) per vessel and subjects SSE broadcasts to disk I/O latency.
- **Existing Tests in `tests/BrewYou.ApiService.Tests/TelemetryTests.cs`**:
  - Line 1182: `EquipmentTelemetryStream_EmitsConnectedAndReceivesEquipmentReading` tests that `GET /api/v1/inventory/equipment/telemetry-stream` emits `: connected\n\n` and receives `event: reading` with `EquipmentTelemetryUpdateDto`.
  - Line 888: `TelemetryStream_EmitsConnectedAndReceivesIngestedReading` tests that `GET /api/v1/batches/{batch.Id}/telemetry-stream` receives `BatchEquipmentReadingDto`.
  - Line 1640-1664: `TelemetryIngestion_SupportsThreeVesselMashAndHlt_ActiveFermentationSteps_AndThrottledBatchReadings` verifies that `BatchReading` is throttled (15 min or 0.001 SG), but `EquipmentReading` was unthrottled (count = 3).

---

## 2. Logic Chain

### 2.1. Immediate SSE Broadcast Flow
1. **Decoupling from Database I/O**:
   - *Observation*: Live 1Hz telemetry frames arrive via WebSocket or MQTT. Gating SSE broadcasts on `await _db.SaveChangesAsync()` introduces disk write latency and locks threads. Furthermore, if database persistence is throttled, skipping the DB write must NOT skip the SSE broadcast.
   - *Design*: The broadcast step must execute **immediately** upon telemetry normalization, before or completely independent of database persistence.
2. **Dual-Stream Fan-out**:
   - **Stream 1: Equipment Inventory Stream (`GET /api/v1/inventory/equipment/telemetry-stream`)**:
     - Serializes `EquipmentTelemetryUpdateDto` with `MetricsJson` containing canonical camelCase BrewLink metrics (`currentTempC`, `targetTempC`, `boilTempC`, `heatActive`, `heatPowerPct`, `pumpActive`, `autoMode`, `timer`, `stage`, `interaction`).
     - Broadcasts to:
       1. Equipment owner (`equipment.UserId`) via `_broadcastService.BroadcastEquipmentReading`.
       2. Brewery Setup owner and all active Brewery Setup members (if `equipment.BrewerySetup != null`).
       3. Connected taps if `equipment.Type == EquipmentType.Keg`.
   - **Stream 2: Batch Workspace & HUD Stream (`GET /api/v1/batches/{id}/telemetry-stream`)**:
     - If the equipment is actively in use in an active batch (`activeBatch != null` matching `MashTunId`, `BoilerId`, `FermenterId`, `HltId`, or `BatchSensorAssignment`):
       - Generates a `BatchEquipmentReadingDto` with populated `BatchId`, `Stage`, `BatchMashStepId`, `StepName`, `TemperatureC`, `TargetTemperatureC`, and canonical `MetricsJson`.
       - Dispatches via `_broadcastService.BroadcastReading(batchDto)`.
       - Frontends subscribed to `/api/v1/batches/{id}/telemetry-stream` receive instant 1Hz telemetry updates for live HUD gauges and mash/boil progress charts.

### 2.2. Database Throttling Algorithm for `EquipmentReading`
1. **The 1Hz Bloat Problem**:
   - A single BrewLink gateway streaming at 1Hz emits 86,400 frames per day per vessel. Inserting an `EquipmentReading` for every frame causes massive PostgreSQL table bloat, WAL pressure, disk I/O exhaustion, and index degradation.
2. **Exact Persistence Criteria**:
   An `EquipmentReading` entity is queued for database insertion (`_db.EquipmentReadings.Add(reading)`) and committed (`await _db.SaveChangesAsync()`) if and only if at least one of the following four conditions is satisfied:
   - **Condition 1: Initial Session Reading**:
     $$\text{lastSnapshot} = \text{null} \quad \lor \quad (T_{now} - \text{lastSnapshot.TimestampUtc}) > \text{SessionIdleTimeout (10 min)}$$
   - **Condition 2: Time-based Cadence ($\ge 30$ seconds elapsed)**:
     $$(T_{now} - \text{lastSnapshot.TimestampUtc}) \ge 30.0\text{ seconds}$$
     Ensures steady-state brewing temperatures produce a smooth time-series log (at 120 points/hour instead of 3,600).
   - **Condition 3: Significant Temperature Delta ($|\Delta T| \ge 0.5^\circ\text{C}$)**:
     $$|T_{current} - \text{lastSnapshot.TemperatureC}| \ge 0.5^\circ\text{C}$$
     Ensures rapid heating (strike water ramp, boil ramp) or chilling (wort chiller) is recorded with full physical fidelity without waiting 30 seconds.
   - **Condition 4: Discrete Hardware State Changes**:
     Any change in discrete actuators or workflow triggers:
     - Heat active toggle: $\text{heatActive}_{now} \ne \text{heatActive}_{last}$
     - Heat power percentage change: $\text{heatPowerPct}_{now} \ne \text{heatPowerPct}_{last}$
     - Pump active toggle: $\text{pumpActive}_{now} \ne \text{pumpActive}_{last}$
     - Timer state transition: $\text{timerActive}_{now} \ne \text{timerActive}_{last} \lor \text{timerPaused}_{now} \ne \text{timerPaused}_{last}$
       *(Crucial rule: Normal 1Hz countdown of `remainingSeconds` does NOT trigger Condition 4; only active/pause transitions do).*
     - User interaction prompt alert: $\text{interactionRequired}_{now} \ne \text{interactionRequired}_{last} \lor \text{interactionCode}_{now} \ne \text{interactionCode}_{last}$
       *(Captures prompt displays like "Sparge Alert" and brewer acknowledgments).*
     - Stage transition: $\text{stepIndex}_{now} \ne \text{stepIndex}_{last} \lor \text{stepName}_{now} \ne \text{stepName}_{last} \lor \text{stage}_{now} \ne \text{stage}_{last}$
     - Specific gravity change (if hydrometer): $|SG_{now} - SG_{last}| \ge 0.001$

### 2.3. In-Memory Tracking Mechanism (Zero DB Overhead)
1. **Elimination of Database Read Queries**:
   - Querying `_db.EquipmentReadings.Where(r => r.EquipmentId == id).OrderByDescending(r => r.Timestamp).FirstOrDefaultAsync()` on every 1Hz frame would still subject PostgreSQL to 3,600 queries/hour.
   - Therefore, the tracking state MUST be maintained entirely in memory.
2. **Singleton State Container (`ITelemetryThrottlingService`)**:
   - Registered as a DI Singleton: `builder.Services.AddSingleton<ITelemetryThrottlingService, TelemetryThrottlingService>();`.
   - Thread-safe storage: `ConcurrentDictionary<Guid, EquipmentThrottleSnapshot>` keyed by `EquipmentId`.
   - Concurrency control: Dedicated per-equipment synchronization lock (`ConcurrentDictionary<Guid, object> _syncLocks`) guarantees that concurrent frames arriving on different threads (e.g. WebSocket frame and HTTP poll) evaluate and update the snapshot atomically.
   - Memory footprint: Each snapshot is an immutable struct/record of ~64 bytes. Tracking 1,000 active vessels uses only ~64 KB of RAM.

---

## 3. Caveats

1. **Timer Countdown vs. Discrete State**:
   - In 1Hz WebSocket streaming, `timer.remaining_seconds` decrements every second. If `remainingSeconds` were included in Condition 4's comparison, every second would be considered a "state change", completely defeating the 30s throttling!
   - *Design guardrail*: Condition 4 strictly checks `timer.active` and `timer.paused`, and ignores `remainingSeconds` decrements.
2. **`Equipment` Entity Updating vs. `EquipmentReading` Inserts**:
   - `Equipment` table holds `CurrentTemperatureC` and `LatestMetricsJson`.
   - Calling `await _db.SaveChangesAsync()` on every 1Hz frame just to update the `Equipment` row would still cause 3,600 `UPDATE` queries per hour per equipment.
   - *Resolution*: When `ShouldPersist == false`, the in-memory `Equipment` properties are updated and broadcast over SSE, but `_db.SaveChangesAsync()` is skipped. PostgreSQL writes occur only when `ShouldPersist == true` (at most every 30s or on delta), updating both `Equipment` and inserting `EquipmentReading` in a single atomic transaction.
3. **Existing Low-Frequency Sensors**:
   - Devices like Tilt and iSpindel push telemetry every 60–900 seconds. Because elapsed time $\ge 30$ seconds, Condition 2 always evaluates to `true`, ensuring 100% backwards compatibility with existing sensors and test suites.

---

## 4. Conclusion & Concrete Design

### 4.1. In-Memory Throttling Cache & Service

#### Model: `EquipmentThrottleSnapshot.cs`
File: `src/BrewYou.ApiService/Services/Telemetry/EquipmentThrottleSnapshot.cs`
```csharp
namespace BrewYou.ApiService.Services.Telemetry;

public sealed record EquipmentThrottleSnapshot(
    DateTime TimestampUtc,
    decimal TemperatureC,
    bool? HeatActive,
    int? HeatPowerPct,
    bool? PumpActive,
    bool? TimerActive,
    bool? TimerPaused,
    bool? InteractionRequired,
    int? InteractionCode,
    int? StepIndex,
    string? StepName,
    BrewStage? Stage,
    decimal? SpecificGravity
);
```

#### Interface: `ITelemetryThrottlingService.cs`
File: `src/BrewYou.ApiService/Services/Telemetry/ITelemetryThrottlingService.cs`
```csharp
using BrewYou.ApiService.Data.Entities;

namespace BrewYou.ApiService.Services.Telemetry;

public interface ITelemetryThrottlingService
{
    bool ShouldPersist(
        Guid equipmentId,
        DateTime timestampUtc,
        decimal temperatureC,
        BrewLinkCanonicalMetrics? metrics = null,
        decimal? specificGravity = null,
        BrewStage? stage = null);

    void Reset(Guid equipmentId);

    EquipmentThrottleSnapshot? GetLastPersisted(Guid equipmentId);
}
```

#### Implementation: `TelemetryThrottlingService.cs`
File: `src/BrewYou.ApiService/Services/Telemetry/TelemetryThrottlingService.cs`
```csharp
using BrewYou.ApiService.Data.Entities;
using System.Collections.Concurrent;

namespace BrewYou.ApiService.Services.Telemetry;

public sealed class TelemetryThrottlingService : ITelemetryThrottlingService
{
    private static readonly TimeSpan DefaultThrottleInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromMinutes(10);
    private const decimal SignificantTempDelta = 0.5m;
    private const decimal SignificantGravityDelta = 0.001m;

    private readonly ConcurrentDictionary<Guid, EquipmentThrottleSnapshot> _snapshots = new();
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
        BrewStage? stage = null)
    {
        var syncLock = _syncLocks.GetOrAdd(equipmentId, _ => new object());

        lock (syncLock)
        {
            if (!_snapshots.TryGetValue(equipmentId, out var last) ||
                (timestampUtc - last.TimestampUtc) > SessionIdleTimeout)
            {
                // Condition 1: Initial reading for this equipment session
                _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage);
                return true;
            }

            // Condition 2: Time-based cadence (>= 30 seconds elapsed)
            if ((timestampUtc - last.TimestampUtc) >= _throttleInterval)
            {
                _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage);
                return true;
            }

            // Condition 3: Significant temperature delta (>= 0.5°C)
            if (Math.Abs(temperatureC - last.TemperatureC) >= SignificantTempDelta)
            {
                _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage);
                return true;
            }

            // Condition 4: Discrete hardware state change
            if (metrics != null)
            {
                // 4a. Heating state or power percentage change
                if (metrics.HeatActive != last.HeatActive || metrics.HeatPowerPct != last.HeatPowerPct)
                {
                    _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage);
                    return true;
                }

                // 4b. Pump state change
                if (metrics.PumpActive != last.PumpActive)
                {
                    _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage);
                    return true;
                }

                // 4c. Timer active or paused state change (ignoring 1Hz remainingSeconds countdown)
                if (metrics.Timer?.Active != last.TimerActive || metrics.Timer?.Paused != last.TimerPaused)
                {
                    _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage);
                    return true;
                }

                // 4d. User interaction alert prompt required or code change
                if (metrics.Interaction?.Required != last.InteractionRequired ||
                    metrics.Interaction?.Code != last.InteractionCode)
                {
                    _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage);
                    return true;
                }

                // 4e. Stage step index or step name change
                if (metrics.Stage?.StepIndex != last.StepIndex ||
                    !string.Equals(metrics.Stage?.StepName, last.StepName, StringComparison.Ordinal))
                {
                    _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage);
                    return true;
                }
            }

            // 4f. Active brew stage transition
            if (stage.HasValue && stage != last.Stage)
            {
                _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage);
                return true;
            }

            // 4g. Specific gravity change >= 0.001
            if (specificGravity.HasValue && last.SpecificGravity.HasValue &&
                Math.Abs(specificGravity.Value - last.SpecificGravity.Value) >= SignificantGravityDelta)
            {
                _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage);
                return true;
            }

            // All conditions false -> throttle persistence
            return false;
        }
    }

    public void Reset(Guid equipmentId)
    {
        _snapshots.TryRemove(equipmentId, out _);
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
        BrewStage? stage)
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
            SpecificGravity: specificGravity
        );
    }
}
```

### 4.2. Ingestion & Broadcast Pipeline in `TelemetryService.cs`

In `TelemetryService.cs`, update the ingestion flow (`IngestTelemetryAsync`, `IngestMqttTelemetryAsync`, and `IngestBrewLinkTelemetryAsync`):

```csharp
// 1. Update in-memory Equipment state
equipment.CurrentTemperatureC = finalTempC;
equipment.TemperatureUpdatedAt = now;
equipment.LastTelemetryAt = now;
if (gravity.HasValue) equipment.CurrentSpecificGravity = gravity;
if (pressure.HasValue) equipment.CurrentPressureBar = pressure;
if (batteryPct.HasValue) equipment.CurrentBatteryPercent = batteryPct;
if (batteryVolt.HasValue) equipment.CurrentBatteryVoltage = batteryVolt;
if (!string.IsNullOrEmpty(metricsJson)) equipment.LatestMetricsJson = metricsJson;

// 2. Resolve Active Batch association (in-memory lookup)
var (activeBatch, activeMashStep, activeStage, targetTempC) = await ResolveActiveBatchAssociationAsync(equipment);

// 3. IMMEDIATE SSE BROADCAST (0-Delay, decoupled from database disk write)
var fillPercentage = equipment.CapacityLiters > 0
    ? Math.Round(Math.Clamp((equipment.CurrentVolumeLiters / equipment.CapacityLiters) * 100m, 0m, 100m), 1)
    : 0m;

var equipmentUpdate = new EquipmentTelemetryUpdateDto(
    EquipmentId: equipment.Id,
    EquipmentName: equipment.Name,
    TemperatureC: finalTempC,
    Timestamp: now,
    Source: source,
    BatchId: activeBatch?.Id,
    SpecificGravity: gravity ?? equipment.CurrentSpecificGravity,
    CurrentVolumeLiters: equipment.CurrentVolumeLiters,
    FillPercentage: fillPercentage,
    PressureBar: pressure ?? equipment.CurrentPressureBar,
    BatteryPercent: batteryPct ?? equipment.CurrentBatteryPercent,
    BatteryVoltage: batteryVolt ?? equipment.CurrentBatteryVoltage,
    TiltDegrees: tilt,
    Rssi: rssi,
    MetricsJson: metricsJson
);

// Broadcast immediately to equipment owner and brewery setup members
_broadcastService.BroadcastEquipmentReading(equipment.UserId, equipmentUpdate);
if (equipment.BrewerySetup != null)
{
    if (equipment.BrewerySetup.UserId != equipment.UserId)
    {
        _broadcastService.BroadcastEquipmentReading(equipment.BrewerySetup.UserId, equipmentUpdate);
    }
    foreach (var member in equipment.BrewerySetup.Members)
    {
        if (member.UserId != equipment.UserId && member.UserId != equipment.BrewerySetup.UserId)
        {
            _broadcastService.BroadcastEquipmentReading(member.UserId, equipmentUpdate);
        }
    }
}

// Broadcast immediately to batch stream if active
if (activeBatch != null)
{
    var batchUpdate = new BatchEquipmentReadingDto(
        Id: Guid.NewGuid(),
        EquipmentId: equipment.Id,
        EquipmentName: equipment.Name,
        BatchId: activeBatch.Id,
        Stage: activeStage,
        BatchMashStepId: activeMashStep?.Id,
        StepName: activeMashStep?.Name ?? activeStage?.ToString(),
        TemperatureC: finalTempC,
        Timestamp: now,
        Source: source,
        TargetTemperatureC: targetTempC,
        Notes: null,
        SpecificGravity: gravity,
        PressureBar: pressure,
        BatteryPercent: batteryPct,
        BatteryVoltage: batteryVolt,
        TiltDegrees: tilt,
        Rssi: rssi,
        MetricsJson: metricsJson
    );
    _broadcastService.BroadcastReading(batchUpdate);
}

// 4. THROTTLED DATABASE PERSISTENCE
var shouldPersist = _throttler.ShouldPersist(
    equipment.Id,
    now,
    finalTempC,
    canonicalMetrics,
    gravity,
    activeStage);

if (shouldPersist)
{
    var reading = new EquipmentReading
    {
        Id = Guid.NewGuid(),
        EquipmentId = equipment.Id,
        BatchId = activeBatch?.Id,
        Stage = activeStage,
        BatchMashStepId = activeMashStep?.Id,
        StepName = activeMashStep?.Name ?? activeStage?.ToString(),
        Timestamp = now,
        TemperatureC = finalTempC,
        SpecificGravity = gravity,
        PressureBar = pressure,
        BatteryPercent = batteryPct,
        BatteryVoltage = batteryVolt,
        TiltDegrees = tilt,
        Rssi = rssi,
        MetricsJson = metricsJson,
        Source = source,
        RawPayload = rawString,
        CreatedAt = now
    };

    _db.EquipmentReadings.Add(reading);

    if (activeMashStep != null)
    {
        activeMashStep.ActualTemperatureC = finalTempC;
    }

    await _db.SaveChangesAsync();
}
```

### 4.3. Integration Test Specifications for `TelemetryTests.cs`

Add four integration test methods to `tests/BrewYou.ApiService.Tests/TelemetryTests.cs`:

#### Specification 1: `BrewLinkTelemetry_ImmediateSseBroadcast_And_30sThrottling`
```csharp
[Fact]
public async Task BrewLinkTelemetry_ImmediateSseBroadcast_And_30sThrottling()
{
    // Setup authenticated user and BrewLink Boiler equipment
    var (client, userId, setupId) = await CreateUserAsync();
    var eqRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
        setupId, $"Grainfather G30 {Guid.NewGuid():N}", EquipmentType.Boiler, 30m, EquipmentSubtype.AllInOne,
        ConnectionType: EquipmentConnectionType.BrewLink));
    var eq = (await eqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

    // 1. Open SSE stream
    var streamReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/equipment/telemetry-stream");
    streamReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
    using var sseRes = await client.SendAsync(streamReq, HttpCompletionOption.ResponseHeadersRead);
    using var stream = await sseRes.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(stream);
    (await reader.ReadLineAsync()).Should().Be(": connected");
    await reader.ReadLineAsync();

    var throttler = _factory.Services.GetRequiredService<ITelemetryThrottlingService>();
    throttler.Reset(eq.Id);

    // Frame 1: Initial Frame (t = 0s, Temp = 65.0°C, Heat = true, Pump = true)
    var t0 = DateTime.UtcNow;
    // Ingest frame 1...
    // Assert SSE event received immediately:
    (await reader.ReadLineAsync()).Should().Be("event: reading");
    var line1 = await reader.ReadLineAsync();
    line1.Should().Contain("\"currentTempC\":65");

    // Assert DB has exactly 1 reading (Condition 1: initial reading)
    var dbReadings1 = (await (await client.GetAsync($"/api/v1/inventory/equipment/{eq.Id}/readings"))
        .Content.ReadFromJsonAsync<ApiResponse<List<EquipmentReadingDto>>>())!.Data!;
    dbReadings1.Should().HaveCount(1);

    // Frame 2: Rapid 1Hz Frame (t = 1s, Temp = 65.1°C, Heat = true, Pump = true)
    // |ΔT| = 0.1°C < 0.5°C, no state change
    // Assert SSE event received immediately!
    (await reader.ReadLineAsync()).Should().Be("event: reading");
    var line2 = await reader.ReadLineAsync();
    line2.Should().Contain("\"currentTempC\":65.1");

    // Assert DB STILL has exactly 1 reading (Throttled!)
    var dbReadings2 = (await (await client.GetAsync($"/api/v1/inventory/equipment/{eq.Id}/readings"))
        .Content.ReadFromJsonAsync<ApiResponse<List<EquipmentReadingDto>>>())!.Data!;
    dbReadings2.Should().HaveCount(1);

    // Frame 3: Frame at t = 31s (Elapsed >= 30s)
    // Assert SSE event received AND DB now has 2 readings (Condition 2: 30s elapsed)
    var dbReadings3 = (await (await client.GetAsync($"/api/v1/inventory/equipment/{eq.Id}/readings"))
        .Content.ReadFromJsonAsync<ApiResponse<List<EquipmentReadingDto>>>())!.Data!;
    dbReadings3.Should().HaveCount(2);
}
```

#### Specification 2: `BrewLinkTelemetry_PersistsImmediately_OnTemperatureDelta`
```csharp
[Fact]
public async Task BrewLinkTelemetry_PersistsImmediately_OnTemperatureDelta()
{
    // Frame 1 at t = 0s: 60.0°C -> DB count = 1
    // Frame 2 at t = 2s: 60.2°C (|ΔT| = 0.2°C < 0.5°C) -> Throttled, DB count = 1
    // Frame 3 at t = 4s: 60.6°C (|ΔT| = 0.6°C >= 0.5°C) -> Persisted immediately! DB count = 2
}
```

#### Specification 3: `BrewLinkTelemetry_PersistsImmediately_OnDiscreteStateChanges`
```csharp
[Fact]
public async Task BrewLinkTelemetry_PersistsImmediately_OnDiscreteStateChanges()
{
    // Baseline Frame: Heat = false, Pump = false, Timer = false, Interaction = false -> DB count = 1
    // Step A: Pump toggled ON -> DB count = 2 (Condition 4b: PumpActive changed)
    // Step B: Heater power changed (100% to 50%) -> DB count = 3 (Condition 4a: HeatPowerPct changed)
    // Step C: Timer started (active = true, remaining = 3600) -> DB count = 4 (Condition 4c: TimerActive changed)
    // Step D: Timer countdown 1Hz (active = true, remaining = 3599) -> Throttled! DB count = 4 (remainingSeconds does NOT trigger)
    // Step E: Prompt alert appeared (required = true, message = "Add Sparge") -> DB count = 5 (Condition 4d: InteractionRequired changed)
    // Step F: Prompt dismissed (required = false) -> DB count = 6 (Condition 4d: InteractionRequired cleared)
    // Step G: Stage step advanced (stepIndex: 1 -> 2) -> DB count = 7 (Condition 4e: StepIndex changed)
}
```

#### Specification 4: `BrewLinkTelemetry_BatchSseStream_BroadcastsWithFullMetrics`
```csharp
[Fact]
public async Task BrewLinkTelemetry_BatchSseStream_BroadcastsWithFullMetrics()
{
    // Create batch in Mash stage, assign Boiler as MashTunId
    // Open SSE stream on GET /api/v1/batches/{batch.Id}/telemetry-stream
    // Ingest BrewLink telemetry with stepIndex = 1, stepName = "Beta Rest"
    // Verify SSE receives event: reading with BatchEquipmentReadingDto containing MetricsJson
}
```

---

## 5. Verification Method

### 5.1. Independent Verification Commands
Run outside the sandbox with `BypassSandbox: true` (or in normal terminal):
1. **Verify Backend Build**:
   ```powershell
   dotnet build tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj
   ```
2. **Verify Telemetry Integration Tests**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~TelemetryTests"
   ```
3. **Verify Parser Tests**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~TelemetryParserTests"
   ```
4. **Verify Format & Code Standards**:
   ```powershell
   dotnet format --verify-no-changes
   ```

### 5.2. Files to Inspect
- `src/BrewYou.ApiService/Services/ITelemetryBroadcastService.cs`
- `src/BrewYou.ApiService/Services/TelemetryBroadcastService.cs`
- `src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs`
- `src/BrewYou.ApiService/Endpoints/BatchEndpoints.cs`
- `src/BrewYou.ApiService/Services/TelemetryService.cs`
- `tests/BrewYou.ApiService.Tests/TelemetryTests.cs`

### 5.3. Invalidation Conditions
- Any SSE broadcast delay caused by blocking on `_db.SaveChangesAsync()`.
- Any unthrottled 1Hz insertion into `_db.EquipmentReadings` when no temperature delta or hardware state change has occurred.
- Triggering database persistence on every 1Hz decrement of `timer.remainingSeconds`.
- Any database read query executed during the throttling evaluation of 1Hz incoming frames.
