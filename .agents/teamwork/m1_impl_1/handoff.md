# Milestone 1 Handoff Report: Telemetry Ingestion, Normalization & DB Throttling

**Agent**: `m1_impl_1` (Worker)
**Parent Agent**: `6986b155-3bf8-4b59-9b95-f99c2273b921`
**Working Directory**: `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_impl_1\`
**Date**: 2026-11-07

---

## 1. Observation

### Implementation Scope & Created/Modified Files
The following files were created or modified within exclusive write ownership:

1. **`src/BrewYou.ApiService/Services/BrewLinkTelemetryParser.cs`** (New):
   - Implements `ITelemetryParser` at Priority 5 with `FormatName = "BrewLink"`.
   - Normalizes 14 canonical fields (`currentTempC`, `targetTempC`, `boilTempC`, `heatActive`, `heatPowerPct`, `rampActive`, `pumpActive`, `autoMode`, `timer`, `stage`, `interaction`, `mac`, `rssi`, `updatedAt`).
   - Supports polymorphic input payloads: WebSocket frames (`event: "telemetry"`), REST gateway equipment arrays (`GET /api/equipment`), and MQTT topic messages.
   - Handles Fahrenheit-to-Celsius conversions, nested vs flat layouts, shorthand keys, MAC canonicalization (`XX:XX:XX:XX:XX:XX`), and JSON serialization/deserialization via `ToMetricsJson` / `FromMetricsJson`.

2. **`src/BrewYou.ApiService/Services/ITelemetryThrottlingService.cs`** & **`src/BrewYou.ApiService/Services/TelemetryThrottlingService.cs`** (New):
   - Thread-safe in-memory singleton using `ConcurrentDictionary<Guid, EquipmentThrottleSnapshot>`.
   - Enforces database persistence conditions for `EquipmentReading`:
     1. Initial reading for equipment / session idle (> 10 minutes elapsed).
     2. Time cadence: $\ge 30$ seconds elapsed since last persisted reading.
     3. Significant temperature change: $|\Delta T| \ge 0.5^\circ\text{C}$.
     4. Discrete actuator / system state changes:
        - Heating state toggle (`HeatActive`) or power change (`HeatPowerPct`)
        - Pump state toggle (`PumpActive`)
        - Timer activation/pause state change (`Timer.Active`, `Timer.Paused`, or `AutoMode`) — explicitly filtering out continuous $1\text{Hz}$ `remaining_seconds` countdown jitter
        - Interaction alert code / prompt requirement (`Interaction.Required`, `Interaction.Code`)
        - Step advancement (`Stage.StepIndex`, `Stage.StepName`).

3. **`src/BrewYou.ApiService/Services/BrewLinkWebSocketManager.cs`** & **`src/BrewYou.ApiService/Services/BrewLinkWebSocketWorker.cs`** (New):
   - Singleton manager handling long-lived `ClientWebSocket` streams to `ws://<gateway-host>/ws?token=<token>`.
   - Host validation against SSRF via `ValidateAndNormalizeHostAsync` (blocking loopback/private IP bypass attacks).
   - Credential decryption via `IDataProtectionProvider` with purpose `"BrewYou.BrewLinkCredentials"`.
   - 15-second keepalive pings, exponential backoff with jitter ($2\text{s} \dots 60\text{s}$), streaming JSON frame extraction up to 64KB, and routing to `ITelemetryService.IngestBrewLinkTelemetryAsync`.
   - `BrewLinkWebSocketWorker` background service reconciling active gateways every 30 seconds.

4. **`src/BrewYou.ApiService/Services/TelemetryService.cs`** (Modified):
   - Registered `BrewLinkTelemetryParser` at Priority 5 before generic parsers.
   - Injected `ITelemetryThrottlingService _throttler`.
   - Added `IngestBrewLinkTelemetryAsync(Guid equipmentId, JsonElement payload, string source)` method.
   - Updated `IngestTelemetryAsync`, `IngestMqttTelemetryAsync`, and `ExecutePollAsync`:
     - Broadcasts live reading to SSE clients immediately with 0 delay (`BroadcastIfAssociated`).
     - Evaluates `_throttler.ShouldPersist(...)` before conditionally appending to `_db.EquipmentReadings` and committing via `SaveChangesAsync()`.
     - Updates live equipment fields (`CurrentTemperatureC`, `TemperatureUpdatedAt`, `LastTelemetryAt`, `LatestMetricsJson`) on every incoming frame.

5. **`src/BrewYou.ApiService/Services/MqttService.cs`** (Modified):
   - Injected credential protection with purpose `"BrewYou.BrewLinkCredentials"`.
   - Extended `RefreshSubscriptionsAsync` to subscribe to `brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry`.
   - Extended `HandleApplicationMessageReceivedAsync` to parse matching topic patterns and route incoming MQTT frames to `telemetryService.IngestMqttTelemetryAsync`.

6. **`src/BrewYou.ApiService/Services/EquipmentPollingWorker.cs`** (Modified):
   - Added REST polling fallback (`GET /api/equipment`) for BrewLink equipment when its gateway WebSocket connection is disconnected or down, suppressing polling when WebSocket streaming is active.

7. **`src/BrewYou.ApiService/Program.cs`** (Modified):
   - Registered `ITelemetryThrottlingService, TelemetryThrottlingService` as Singleton.
   - Registered `IBrewLinkWebSocketManager, BrewLinkWebSocketManager` as Singleton.
   - Registered `BrewLinkWebSocketWorker` as HostedService.

8. **`tests/BrewYou.ApiService.Tests/BrewLinkTelemetryParserTests.cs`** (New):
   - 18 comprehensive unit tests verifying parser behavior across nested WS frames, REST arrays, MQTT messages, Fahrenheit-to-Celsius conversions, shorthand keys, MAC canonicalization, and fallback heuristics.

9. **`tests/BrewYou.ApiService.Tests/TelemetryTests.cs`** (Modified):
   - Added 4 integration tests covering:
     - `BrewLinkTelemetry_ImmediateSseBroadcast_And_30sThrottling`: verifies zero-delay SSE broadcast while throttling rapid incoming frames in the DB to 30-second cadence.
     - `BrewLinkTelemetry_PersistsImmediately_OnTemperatureDelta`: verifies immediate persistence when $|\Delta T| \ge 0.5^\circ\text{C}$ while throttling smaller fluctuations.
     - `BrewLinkTelemetry_PersistsImmediately_OnDiscreteStateChanges`: verifies immediate persistence when heat, pump, timer state, user interaction prompts, or mash steps change, while ignoring countdown seconds.
     - `BrewLinkTelemetry_BatchSseStream_BroadcastsWithFullMetrics`: verifies batch SSE streaming propagates full `LatestMetricsJson` including hardware actuator states.

### Build and Test Results
- **Build**: `dotnet build tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj` succeeded with **0 Warnings, 0 Errors**.
- **Parser Unit Tests**: `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkTelemetryParserTests` -> **Passed! 18 Passed, 0 Failed, 0 Skipped**.
- **Telemetry Integration Tests**: `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~TelemetryTests` -> **Passed! 49 Passed, 0 Failed, 0 Skipped**.
- **Full Solution Test Suite**: `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1` -> **Passed! 676 Passed, 0 Failed, 0 Skipped (Duration: 6s)**.
- **Code Style & Formatting**: `dotnet format --verify-no-changes` -> **Passed with exit code 0** (0 violations).

---

## 2. Logic Chain

1. **Telemetry Normalization**:
   BrewLink controllers emit varied JSON schemas depending on firmware and transport (flat keys like `current_temp_c` vs nested `state.temperature.current_c`, unit indicators `"unit": "F"`, or gateway wrapper arrays `{"gateway_id": "...", "equipment": [...]}`). `BrewLinkTelemetryParser` extracts these properties into a strongly typed `BrewLinkCanonicalMetrics` record and serializes it to camelCase `LatestMetricsJson`.

2. **Immediate SSE Broadcast with DB Throttling**:
   Clients (UI brew monitors, live dashboards) require real-time 1-second responsiveness without polling lag. However, writing every 1-second reading to PostgreSQL creates database bloat and excessive I/O.
   By decoupling SSE dispatch from database writes in `TelemetryService`:
   - `BroadcastIfAssociated` broadcasts each valid frame immediately to active SSE subscribers (`/telemetry-stream` and `/batches/{batchId}/telemetry-stream`).
   - `_throttler.ShouldPersist` checks in-memory snapshot state: only readings meeting time ($\ge 30\text{s}$), temperature delta ($|\Delta T| \ge 0.5^\circ\text{C}$), or discrete actuator changes (pump, heat, timer active, prompts, steps) are persisted to `EquipmentReading`.

3. **Multi-Transport Ingestion Parity**:
   Whether telemetry arrives via `BrewLinkWebSocketManager` (WebSocket push), `MqttService` (MQTT topic subscription), or `EquipmentPollingWorker` (HTTP REST polling fallback), all transports normalize payload through `BrewLinkTelemetryParser`, broadcast over SSE, and route through `TelemetryThrottlingService`.

4. **Integration Test Verification**:
   The integration tests in `TelemetryTests.cs` simulate rapid incoming telemetry frames:
   - Verifying that SSE events arrive immediately for each frame.
   - Verifying that intermediate frames within 30s and $<0.5^\circ\text{C}$ delta do not increment the reading count in the database.
   - Verifying that discrete events (pump toggle, heater power changes, interaction prompts, temperature jumps) bypass the 30s timer and persist immediately.

---

## 3. Caveats

1. **In-Memory Throttling State**:
   `TelemetryThrottlingService` maintains equipment state snapshots in memory (`ConcurrentDictionary`). In a multi-node horizontal deployment behind a load balancer without sticky sessions, each node maintains its own throttle timer. For BrewYou's single-instance Aspire/container architecture, this provides optimal zero-latency performance without Redis overhead.
2. **Timer Countdown Handling**:
   The throttler specifically compares `Timer.Active` and `Timer.Paused` rather than `Timer.RemainingSeconds`, so that normal 1-second tick countdowns do not bypass the 30-second persistence throttling. If persistence on specific remaining time thresholds is ever required in future milestones, additional conditions can be added.
3. **Execution Environment**:
   When running commands on Windows, tests and builds that utilize inter-process communication (such as `vstest.console` connecting to `testhost.exe` via loopback) must run with `BypassSandbox: true` to avoid AppContainer sandbox loopback isolation.

---

## 4. Conclusion

Milestone 1 is complete and fully verified:
- `BrewLinkTelemetryParser` normalizes WebSocket, REST, and MQTT telemetry into canonical `LatestMetricsJson`.
- Multi-transport ingestion is supported across WebSocket (`ws://<host>/ws?token=<token>`), MQTT broker topics (`brewlink/.../telemetry`), and fallback REST polling (`GET /api/equipment`).
- Real-time SSE broadcasting executes immediately (0 delay) on every incoming frame.
- Database throttling prevents excessive write load, enforcing 30-second intervals while persisting immediately on significant temperature ($\ge 0.5^\circ\text{C}$) or discrete hardware actuator state changes.
- All 676 unit and integration tests pass with zero warnings, zero errors, and clean `.editorconfig` compliance.

---

## 5. Verification Method

To independently reproduce and verify the implementation:

1. **Verify Formatting & Static Analysis**:
   ```powershell
   dotnet format --verify-no-changes
   ```
   *Expected result*: Exit code 0, 0 files changed.

2. **Run Parser Unit Tests**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkTelemetryParserTests
   ```
   *Expected result*: 18 passed, 0 failed.

3. **Run Telemetry Integration Tests**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~TelemetryTests
   ```
   *Expected result*: 49 passed, 0 failed.

4. **Run Full Test Suite**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1
   ```
   *Expected result*: 676 passed, 0 failed, 0 skipped.
