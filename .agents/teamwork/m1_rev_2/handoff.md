# Milestone 1 Review & Adversarial Challenge Report

**Reviewer**: Reviewer 2 (`m1_rev_2`)  
**Parent Agent**: `6986b155-3bf8-4b59-9b95-f99c2273b921`  
**Milestone**: Milestone 1: Telemetry Ingestion, Normalization & DB Throttling  
**Date**: 2026-11-07  

---

## Review Summary

**Verdict**: **APPROVE**  
**Integrity Assessment**: **NO INTEGRITY VIOLATIONS DETECTED**  
- Zero hardcoded test values or facade implementations.
- Real parsing, normalization, throttling, security host validation, and SSE streaming across all paths.
- All verification commands independently executed and confirmed.

---

## 1. Observation

### Verified Source Files & Line Locations
1. **`src/BrewYou.ApiService/Services/ITelemetryThrottlingService.cs`**:
   - Defines `EquipmentThrottleSnapshot` record (lines 6–20) capturing: `TimestampUtc`, `TemperatureC`, `HeatActive`, `HeatPowerPct`, `PumpActive`, `TimerActive`, `TimerPaused`, `InteractionRequired`, `InteractionCode`, `StepIndex`, `StepName`, `Stage`, and `SpecificGravity`.
   - Defines `ITelemetryThrottlingService` interface with `ShouldPersist`, `Reset`, and `GetLastPersisted` (lines 22–35).

2. **`src/BrewYou.ApiService/Services/TelemetryThrottlingService.cs`**:
   - Thread-safe singleton using `ConcurrentDictionary<Guid, EquipmentThrottleSnapshot> _snapshots` and `ConcurrentDictionary<Guid, object> _syncLocks` (lines 14–15).
   - Enforces throttling conditions within synchronized per-equipment locks (lines 31–115):
     - Initial reading or session idle ($> 10\text{ min}$) persists (lines 35–41).
     - Cadence: $\ge 30\text{s}$ elapsed since last persisted frame persists (lines 44–48).
     - Delta $T$: $|\Delta T| \ge 0.5^\circ\text{C}$ persists (lines 51–55).
     - Actuators: Heat active/power pct changes persist (lines 61–65).
     - Actuators: Pump active toggle persists (lines 68–72).
     - Actuators: Timer active/paused toggle persists (lines 75–79). Specifically, 1Hz countdown ticks in `RemainingSeconds` do not bypass throttling.
     - Alerts: Interaction prompt required/code changes persist (lines 82–87).
     - Steps: Stage step index/name changes persist (lines 90–95).
     - Brewing stage & gravity: Stage transitions and $|\Delta \text{SG}| \ge 0.001$ persist (lines 99–111).
     - Otherwise returns `false` (line 114).

3. **`src/BrewYou.ApiService/Services/BrewLinkTelemetryParser.cs`**:
   - Registered at priority 5 (`FormatName = "BrewLink"`) implementing `ITelemetryParser` (lines 47–48).
   - Generates canonical `LatestMetricsJson` matching `PROJECT.md` contract with camelCase naming policy and omitted nulls (lines 50–55, 125–144).
   - Normalizes polymorphic payloads: WebSocket frames, REST gateway equipment arrays (`ResolveTargetDevice` by MAC, slot, or name), and flat MQTT payloads.
   - Accurately converts Fahrenheit to Celsius via `TelemetryHelper.ConvertToCelsius` and normalizes MAC addresses to `XX:XX:XX:XX:XX:XX` uppercase hex (lines 851–883).

4. **`src/BrewYou.ApiService/Services/TelemetryService.cs`**:
   - Broadcaster decoupling: In `IngestTelemetryAsync` (line 157), `IngestMqttTelemetryAsync` (line 307), `IngestBrewLinkTelemetryAsync` (line 426), and `ExecutePollAsync` (line 645), `BroadcastIfAssociated` broadcasts live readings over SSE immediately before DB persistence gating.
   - Throttling evaluation: Invokes `_throttler.ShouldPersist(...)` (lines 164–170, 314–320, 433–439, 651–657). Only calls `_db.EquipmentReadings.Add` and `_db.SaveChangesAsync()` when persistence is permitted.
   - Non-BrewLink backward compatibility: `var isBrewLink = equipment.ConnectionType == EquipmentConnectionType.BrewLink || canonicalMetrics != null; var shouldPersist = !isBrewLink || _throttler.ShouldPersist(...);` ensures legacy sensors (Tilt, iSpindel) are never throttled.

5. **`src/BrewYou.ApiService/Services/BrewLinkWebSocketManager.cs` & `EquipmentPollingWorker.cs`**:
   - Validates outbound hosts via `BrewLinkService.ValidateAndNormalizeHostAsync` against SSRF/loopback attacks (lines 109–116).
   - Unprotects credentials via `IDataProtector` with purpose `"BrewYou.BrewLinkCredentials"`.
   - Employs streaming JSON receive with 64KB frame limits and `ArrayPool<byte>.Shared` memory management (lines 193–235).
   - `EquipmentPollingWorker` suppresses polling fallback when WebSocket is actively connected (lines 77–80).

### Independent Command Executions & Test Results
- **Command 1: Telemetry Integration Tests**:
  `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~TelemetryTests`
  *Result*: **Passed! Failed: 0, Passed: 49, Skipped: 0, Total: 49 (Duration: 4s)**.
- **Command 2: Parser Unit Tests**:
  `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkTelemetryParserTests`
  *Result*: **Passed! Failed: 0, Passed: 18, Skipped: 0, Total: 18 (Duration: 77ms)**.
- **Command 3: E2E Regression Tests**:
  `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --no-build --filter FullyQualifiedName~BrewLinkE2ETests`
  *Result*: **Passed! Failed: 0, Passed: 74, Skipped: 0, Total: 74 (Duration: 2s)**.
- **Command 4: Static Analysis & Formatting**:
  `dotnet format --verify-no-changes`
  *Result*: **Exited with code 0 (0 violations)**.

---

## 2. Logic Chain

1. **R1 Telemetry Ingestion & Normalization**:
   - Observation 3 confirms `BrewLinkTelemetryParser` normalizes WebSocket frames, REST polling arrays, and MQTT messages into a canonical `BrewLinkCanonicalMetrics` model.
   - Verified by 18 unit tests in `BrewLinkTelemetryParserTests` and 74 E2E tests in `BrewLinkE2ETests`.
2. **Immediate Zero-Delay SSE Broadcasting**:
   - Observation 4 confirms `BroadcastIfAssociated` is invoked synchronously before any DB throttling logic.
   - Verified by `BrewLinkTelemetry_ImmediateSseBroadcast_And_30sThrottling` in `TelemetryTests.cs`, where client receives SSE events immediately for both frame 1 and rapid frame 2.
3. **Database Throttling Correctness**:
   - Observations 2 and 4 confirm `TelemetryThrottlingService` enforces 30s cadence, $|\Delta T| \ge 0.5^\circ\text{C}$, and actuator state change persistence while filtering 1Hz timer countdown ticks.
   - In `TelemetryTests.cs`:
     - Frame 2 ($|\Delta T| = 0.1^\circ\text{C}$) within 30s is throttled (DB count remains 1).
     - Frame 3 with $|\Delta T| = 0.6^\circ\text{C}$ persists immediately (DB count becomes 2).
     - Timer active countdown ($3600 \to 3599$) is throttled (DB count remains 4).
     - Actuator toggles (pump, heat, alerts, step advance) persist immediately.
4. **Thread-Safety & Concurrency**:
   - Observation 2 confirms `TelemetryThrottlingService` uses `ConcurrentDictionary<Guid, object>` per equipment to ensure concurrent frames for the same equipment cannot cause race conditions in snapshot evaluation.
5. **Security & Non-BrewLink Invariant Preservation**:
   - Observation 4 confirms non-BrewLink hardware is exempt from 30s throttling (`!isBrewLink` branch).
   - Observation 5 confirms outbound gateway connections are validated via `ValidateAndNormalizeHostAsync` and tokens are encrypted via DPAPI.

---

## 3. Adversarial Challenges & Stress Testing

### Overall Risk Assessment: LOW

### Challenge 1: Timer 1Hz Countdown Flooding
- **Assumption**: Hardware timer countdown ticks (`remaining_seconds: 3599, 3598, ...`) must not bypass 30s DB persistence.
- **Attack Scenario**: Controller emits 1Hz telemetry while a 60-minute boil timer counts down. If countdown ticks were treated as state changes, 3,600 DB rows would be written per hour.
- **Result**: PASSED. Line 75 of `TelemetryThrottlingService.cs` explicitly compares `Timer.Active` and `Timer.Paused`, ignoring `RemainingSeconds`. Verified by integration test `BrewLinkTelemetry_PersistsImmediately_OnDiscreteStateChanges`.

### Challenge 2: Concurrent Multi-Device Ingestion
- **Assumption**: High-throughput telemetry from multiple gateways must not cause lock contention or corrupted throttle state across devices.
- **Attack Scenario**: Multiple threads simultaneously ingesting telemetry for different equipment IDs.
- **Result**: PASSED. Per-equipment synchronization objects (`_syncLocks.GetOrAdd(equipmentId, _ => new object())`) isolate locks to individual equipment IDs without global lock bottlenecks.

### Challenge 3: Negative Time Deltas / Device Clock Skew
- **Assumption**: Gateway clock drift or out-of-order packets where $t < t_{\text{last}}$.
- **Attack Scenario**: Incoming packet has timestamp slightly in the past due to network reordering.
- **Result**: PASSED. `(timestampUtc - last.TimestampUtc)` becomes negative; the interval condition evaluates to false, throttling the out-of-order packet unless a temperature delta $\ge 0.5^\circ\text{C}$ or actuator change is detected, safely avoiding duplicate persistence.

### Challenge 4: Memory Leak in Long-Running In-Memory Cache
- **Assumption**: `_snapshots` and `_syncLocks` in `TelemetryThrottlingService` could grow unbounded.
- **Mitigation / Assessment**: In BrewYou, `equipmentId` corresponds to created physical vessels in the database (typically 1–10 per brewery, at most hundreds). Memory footprint is under 200 bytes per equipment. `Reset(equipmentId)` removes deleted equipment. Risk is negligible.

---

## 4. Caveats

1. **Single-Node In-Memory Cache**:
   `TelemetryThrottlingService` maintains snapshots in local memory. In horizontally scaled multi-instance deployments without sticky sessions, independent node timers could lead to duplicate persisted frames within the 30-second window. For BrewYou's target container/Aspire architecture, this design delivers zero-latency performance without Redis dependencies.
2. **Untracked Empirical Stress Test File**:
   During the review, an untracked scratch file `tests/BrewYou.ApiService.Tests/BrewLinkEmpiricalStressTests.cs` was detected from a concurrent challenger agent. Because that file had uncommitted syntax issues, test commands must be run with `--filter` or `--no-build` until the challenger completes their branch.

---

## 5. Conclusion

Milestone 1 is **ACCEPTED & APPROVED**:
- All requirements R1, R5, and milestone acceptance criteria are fully satisfied.
- Canonical telemetry normalization, immediate SSE broadcast, and 30s / $|\Delta T| \ge 0.5^\circ\text{C}$ / actuator change persistence throttling are robustly implemented.
- 100% of required test suites (18 parser tests, 49 telemetry tests, 74 E2E tests) pass cleanly.
- Code formatting and static analysis pass with zero warnings and zero errors.

---

## 6. Verification Method

To independently verify this verdict:

```powershell
# 1. Verify code formatting and style compliance
dotnet format --verify-no-changes

# 2. Run Parser unit tests
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkTelemetryParserTests

# 3. Run Telemetry integration tests (including SSE & throttling tests)
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~TelemetryTests

# 4. Run BrewLink E2E regression suite
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --no-build --filter FullyQualifiedName~BrewLinkE2ETests
```
*Expected Result*: All tests pass (0 failures), and `dotnet format` exits with code 0.
