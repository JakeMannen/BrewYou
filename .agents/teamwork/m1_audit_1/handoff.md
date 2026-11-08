# Forensic Audit Report: Milestone 1 — Telemetry Ingestion, Normalization & DB Throttling

**Work Product**: Milestone 1 Telemetry Subsystem (`BrewLinkTelemetryParser.cs`, `TelemetryThrottlingService.cs`, `BrewLinkWebSocketManager.cs`, `BrewLinkWebSocketWorker.cs`, `MqttService.cs`, `TelemetryService.cs`, `EquipmentPollingWorker.cs`)  
**Profile**: General Project  
**Integrity Mode**: Development (per `ORIGINAL_REQUEST.md`)  
**Verdict**: **CLEAN**  

---

## 1. Observation

### 1.1 Integrity Mode & Ground-Truth Constraints
- `ORIGINAL_REQUEST.md:7`:
  > `Integrity mode: development`
- Under Development Mode:
  - Permitted: Code reuse, standard libraries, helper utilities, reference implementations.
  - Prohibited: Hardcoded test results, facade/dummy implementations, fabricated verification logs or outputs, self-certifying tests.

### 1.2 Prohibited Patterns Inspection
1. **Hardcoded Test Results**:
   - Inspected `src/BrewYou.ApiService/Services/BrewLinkTelemetryParser.cs` (908 lines):
     - Zero hardcoded test equipment IDs, MAC addresses (e.g. searched for `C4:4F:33:1A:8B:22`, `C44F331A8B20`), or test temperatures.
     - Parsing and conversions are dynamic: mathematical Fahrenheit-to-Celsius conversion (`(val - 32m) * 5m / 9m`), regex/ASCII MAC canonicalization, and dynamic JsonElement traversal across nested and flat schema layouts.
   - Inspected `src/BrewYou.ApiService/Services/TelemetryThrottlingService.cs` (151 lines):
     - Dynamic snapshot tracking in `ConcurrentDictionary<Guid, EquipmentThrottleSnapshot>`.
     - Zero hardcoded boolean shortcuts or constant return values.
2. **Facade Implementations**:
   - Zero classes or methods with dummy `return true;`, `return null;`, or `throw new NotImplementedException()`.
   - `BrewLinkWebSocketManager`: Full `ClientWebSocket` lifecycle management with 15s keepalive ping, SSRF host validation via `NetworkSecurityValidator`, Data Protection token decryption, and frame slicing up to 64KB.
   - `MqttService`: Full topic subscription pattern `brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry`, payload parsing, and gateway mapping.
   - `EquipmentPollingWorker`: REST polling fallback via `GET /api/equipment` when WebSocket stream is disconnected, suppressing polling when WebSocket streaming is active.
3. **Pre-Populated Verification Artifacts**:
   - Ran `find_by_name` for `*result*` and `*.log` across the repository before audit runs.
   - Found only normal MSBuild logs and test results from earlier compilation passes. Zero fake test attestation or pre-generated proof files.
4. **Test Assertion Genuineness**:
   - Inspected `tests/BrewYou.ApiService.Tests/BrewLinkTelemetryParserTests.cs` (18 tests) and `tests/BrewYou.ApiService.Tests/TelemetryTests.cs` (49 tests).
   - Tests assert specific values (e.g., `result!.TemperatureC.Should().Be(65.0m)`, `canonical.HeatActive.Should().BeTrue()`, `readings1.Should().HaveCount(1)` followed by `readings2.Should().HaveCount(1)` for throttling, followed by `readings3.Should().HaveCount(2)` for significant delta).
   - Zero trivial assertions (e.g., `Assert.True(true)`).

### 1.3 Behavioral & Build Verification
1. **Code Formatting & Static Analysis**:
   - Tool Command: `dotnet format --verify-no-changes`
   - Result: Exited with code 0 (0 violations).
2. **Project Compilation**:
   - Tool Command: `dotnet build tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj`
   - Result: Succeeded with 0 Warnings and 0 Errors.
3. **Parser Unit Tests Execution**:
   - Tool Command: `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkTelemetryParserTests`
   - Result:
     ```
     Passed!  - Failed: 0, Passed: 18, Skipped: 0, Total: 18, Duration: 76 ms
     ```
4. **Telemetry Integration Tests Execution**:
   - Tool Command: `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~TelemetryTests`
   - Result:
     ```
     Passed!  - Failed: 0, Passed: 49, Skipped: 0, Total: 49, Duration: 4 s
     ```
5. **Empirical Stress Test Execution**:
   - Tool Command: `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkEmpiricalStressTests`
   - Result:
     ```
     Passed: 21, Failed: 3, Total: 24
     ```
   - All 21 core stress tests passed, proving:
     - 1,000 rapid identical frames within 30s only persist 1 database reading.
     - Significant temperature delta ($|\Delta T| \ge 0.5^\circ\text{C}$) persists immediately.
     - Actuator toggles (pump, heat, timer active, prompts, stages) persist immediately.
     - Multi-threaded concurrency across 20 threads serializes snapshots safely without race conditions.
     - The 3 failed tests surfaced an edge case in `System.Text.Json` type handling (detailed below).

---

## 2. Logic Chain

1. **Integrity Mode Conformance**:
   The authoritative user specification in `ORIGINAL_REQUEST.md` mandates `Integrity mode: development`. Under this mode, the forensic criteria require confirming that implementations are genuine (not facades) and that verification outputs are not fabricated or hardcoded.
2. **Empirical Code Analysis**:
   - In `BrewLinkTelemetryParser.cs`, all 14 canonical fields (`currentTempC`, `targetTempC`, `boilTempC`, `heatActive`, `heatPowerPct`, `rampActive`, `pumpActive`, `autoMode`, `timer`, `stage`, `interaction`, `mac`, `rssi`, `updatedAt`) are parsed through dynamic JSON element extraction and serialized to camelCase `LatestMetricsJson`.
   - In `TelemetryThrottlingService.cs`, thread-safe per-equipment snapshot comparison governs `EquipmentReading` database persistence.
   - In `TelemetryService.cs`, `BroadcastIfAssociated` broadcasts live readings over SSE immediately (0 delay) on every frame before evaluating `_throttler.ShouldPersist`.
3. **Verification of Test Authenticity**:
   - Test suites execute against live test server pipelines (`WebApplicationFactory<Program>`).
   - Readings are read from real SSE stream readers (`StreamReader` on `text/event-stream`) and asserted against database counts (`GET /api/v1/inventory/equipment/{id}/readings`).
   - Intermediate frames within 30s and $<0.5^\circ\text{C}$ delta do not increment reading counts, verifying genuine DB throttling.
4. **Adversarial Discovery (Non-Integrity Quality Finding)**:
   During adversarial stress testing, 3 stress tests failed because `BrewLinkTelemetryParser.cs` calls `JsonElement.TryGetInt64(...)`, `TryGetInt32(...)`, and `TryGetInt16(...)` without first validating `elem.ValueKind == JsonValueKind.Number`. In `System.Text.Json`, `TryGet*` throws `InvalidOperationException` when called on elements with `ValueKind == JsonValueKind.String` (such as ISO-8601 string timestamps `"2026-11-07T20:00:00Z"` or stringified numbers `"1800"`). While this is an edge-case bug rather than an integrity violation, it should be addressed before general production release.

---

## 3. Caveats

1. **`JsonElement` String Type Sensitivity**:
   If an external gateway or broker publishes payloads where numeric properties (`timestamp`, `rssi`, `timer_remaining_sec`, `step_index`) are serialized as strings instead of numbers, `BrewLinkTelemetryParser.ParseCanonical` throws `System.InvalidOperationException`. In standard numeric JSON payloads (as verified by the 67 unit and integration tests), parsing executes with zero errors.
2. **In-Memory Throttling State**:
   `TelemetryThrottlingService` maintains state in memory (`ConcurrentDictionary`). This is ideal for single-instance container deployments, but multiple load-balanced worker instances would require sticky sessions or distributed caching.

---

## 4. Conclusion

**Verdict: CLEAN**

Milestone 1 satisfies all forensic integrity criteria:
- **No Cheating**: Zero hardcoded outputs, zero facades, zero fabricated test artifacts.
- **Genuine DB Throttling**: Persistence to `EquipmentReading` is genuinely throttled to 30s or significant delta ($|\Delta T| \ge 0.5^\circ\text{C}$, actuator state changes, prompt alerts).
- **Multi-Transport Streaming & SSE Normalization**: WebSocket frames and MQTT broker messages are ingested, normalized into canonical camelCase `LatestMetricsJson`, and broadcast immediately to SSE clients with 0 latency.
- **Test Integrity**: All 18 parser tests and 49 integration tests execute dynamically and assert authentic behavior.

**Advisory Note for Team**: Update `BrewLinkTelemetryParser.cs` to check `elem.ValueKind == JsonValueKind.Number` before invoking `TryGetInt*` to gracefully handle payloads with string-formatted timestamps or numbers.

---

## 5. Verification Method

To independently reproduce the forensic verification:

1. **Verify Static Analysis and Code Formatting**:
   ```powershell
   dotnet format --verify-no-changes
   ```
   *Expected*: Exit code 0 (0 violations).

2. **Run Parser Unit Tests**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkTelemetryParserTests
   ```
   *Expected*: 18 passed, 0 failed, 0 skipped.

3. **Run Telemetry Integration Tests**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~TelemetryTests
   ```
   *Expected*: 49 passed, 0 failed, 0 skipped.

4. **Verify Absence of Hardcoded Test Values**:
   ```powershell
   git grep -E "C4:4F:33:1A:8B:22" src/BrewYou.ApiService/
   ```
   *Expected*: 0 matches in production source code.
