# Milestone 1 Challenger 2 Handoff Report: Adversarial Verification

**Agent**: `m1_chal_2` (Challenger 2 — critic, specialist)  
**Parent Agent**: `6986b155-3bf8-4b59-9b95-f99c2273b921`  
**Working Directory**: `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_chal_2\`  
**Date**: 2026-11-07  
**Verdict**: **`REQUEST_CHANGES`**  

---

## 1. Observation

An empirical adversarial test suite consisting of 50 test cases was created in `tests/BrewYou.ApiService.Tests/BrewLinkTelemetryAdversarialTests.cs` and executed against the Milestone 1 implementation (`BrewLinkTelemetryParser` and `TelemetryThrottlingService`).

Execution command:
```powershell
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkTelemetryAdversarialTests
```

### Test Results Summary
- **Total Tests**: 50
- **Passed**: 44
- **Failed**: 6
- **Skipped**: 0

### Direct Failure Observations & Verbatim Errors

#### Observation 1: `InvalidOperationException` in `BrewLinkTelemetryParser.CanParse` Crashes Public Telemetry Endpoint
- **Test**: `TelemetryEndpoint_IngestTelemetry_WithNonStringEvent_ShouldNotThrow500`
- **Error**:
  ```
  Expected response.StatusCode to be HttpStatusCode.OK {value: 200} because non-string event property must not cause 500 Internal Server Error, but found HttpStatusCode.InternalServerError {value: 500}.
  ```
- **Code Location**: `src/BrewYou.ApiService/Services/BrewLinkTelemetryParser.cs:line 69`
  ```csharp
  if (TelemetryHelper.TryGetPropertyInsensitive(root, "event", out var eventElem) &&
      string.Equals(eventElem.GetString(), "telemetry", StringComparison.OrdinalIgnoreCase))
  ```
- **Verbatim Exception**:
  ```
  System.InvalidOperationException: The requested operation requires an element of type 'String', but the target element has type 'Number'.
     at System.Text.Json.ThrowHelper.ThrowJsonElementWrongTypeException(JsonTokenType expectedType, JsonTokenType actualType)
     at System.Text.Json.JsonDocument.GetString(Int32 index, JsonTokenType expectedType)
     at BrewYou.ApiService.Services.Telemetry.BrewLinkTelemetryParser.CanParse(JsonElement root, Equipment equipment, String preferredPath) in BrewLinkTelemetryParser.cs:line 69
     at BrewYou.ApiService.Services.TelemetryService.NormalizeJsonPayload(JsonElement root, Equipment equipment, String preferredPath) in TelemetryService.cs:line 50
  ```
- **Impact**: Any HTTP push or MQTT message from a non-BrewLink sensor that contains a non-string `event` property (e.g. `{"event": 12345, "temperature": 22.5, "tempUnit": "C"}`) causes `CanParse` to throw an unhandled exception, resulting in HTTP 500 crashes.

#### Observation 2: `InvalidOperationException` in `CanParse` on Non-String `event` Property
- **Test**: `CanParse_UnexpectedDataTypesInProperties_ShouldNotThrow`
- **Error**:
  ```
  Did not expect any exception, but found System.InvalidOperationException: The requested operation requires an element of type 'String', but the target element has type 'Number'.
     at System.Text.Json.ThrowHelper.ThrowJsonElementWrongTypeException(JsonTokenType expectedType, JsonTokenType actualType)
     at System.Text.Json.JsonDocument.GetString(Int32 index, JsonTokenType expectedType)
     at BrewYou.ApiService.Services.Telemetry.BrewLinkTelemetryParser.CanParse(JsonElement root, Equipment equipment, String preferredPath) in BrewLinkTelemetryParser.cs:line 69
  ```

#### Observation 3: `InvalidOperationException` in `ParseCanonical` on Non-String `mac`
- **Test**: `Parse_UnexpectedDataTypesInFields_ShouldExtractValidFieldsWithoutThrowing`
- **Code Location**: `src/BrewYou.ApiService/Services/BrewLinkTelemetryParser.cs:line 181`
  ```csharp
  if (TelemetryHelper.TryGetPropertyInsensitive(deviceElem, "mac", out var macElem) ||
      TelemetryHelper.TryGetPropertyInsensitive(deviceElem, "target_mac", out macElem) ||
      TelemetryHelper.TryGetPropertyInsensitive(deviceElem, "targetMac", out macElem) ||
      TelemetryHelper.TryGetPropertyInsensitive(stateElem, "mac", out macElem))
  {
      extractedMac = FormatMac(macElem.GetString());
  }
  ```
- **Verbatim Exception**:
  ```
  System.InvalidOperationException: The requested operation requires an element of type 'String', but the target element has type 'Number'.
     at System.Text.Json.ThrowHelper.ThrowJsonElementWrongTypeException(JsonTokenType expectedType, JsonTokenType actualType)
     at System.Text.Json.JsonDocument.GetString(Int32 index, JsonTokenType expectedType)
     at BrewYou.ApiService.Services.Telemetry.BrewLinkTelemetryParser.ParseCanonical(JsonElement root, Equipment equipment) in BrewLinkTelemetryParser.cs:line 181
  ```
- **Same Pattern**: The same unguarded `.GetString()` calls exist for:
  - `state.temperature.unit`: line 326 (`u.GetString()`)
  - `state.unit`: line 371 (`su.GetString()`)
  - `stage.step_name`: line 684 (`snElem.GetString()`)
  - `state.step_name`: line 716 (`fsnElem.GetString()`)
  - `interaction.message`: line 753 (`msgElem.GetString()`)
  - `state.interaction_message`: line 788 (`fimElem.GetString()`)

#### Observation 4: `InvalidOperationException` in `ResolveTargetDevice` on Array Items with Non-String Properties
- **Test**: `Parse_RestGatewayPayloadWithCorruptEquipmentArray_ShouldNotThrow`
- **Code Location**: `src/BrewYou.ApiService/Services/BrewLinkTelemetryParser.cs:line 290`
  ```csharp
  if (!string.IsNullOrEmpty(targetMac) &&
      TelemetryHelper.TryGetPropertyInsensitive(item, "mac", out var mElem) &&
      string.Equals(CleanMac(mElem.GetString()), targetMac, StringComparison.OrdinalIgnoreCase))
  ```
  And line 307:
  ```csharp
  if (!string.IsNullOrEmpty(targetName) &&
      TelemetryHelper.TryGetPropertyInsensitive(item, "name", out var nElem) &&
      string.Equals(nElem.GetString(), targetName, StringComparison.OrdinalIgnoreCase))
  ```
- **Verbatim Exception**:
  ```
  System.InvalidOperationException: The requested operation requires an element of type 'String', but the target element has type 'Number'.
     at System.Text.Json.ThrowHelper.ThrowJsonElementWrongTypeException(JsonTokenType expectedType, JsonTokenType actualType)
     at System.Text.Json.JsonDocument.GetString(Int32 index, JsonTokenType expectedType)
     at BrewYou.ApiService.Services.Telemetry.BrewLinkTelemetryParser.ResolveTargetDevice(JsonElement eqArray, Equipment equipment) in BrewLinkTelemetryParser.cs:line 290
  ```

#### Observation 5: `ArgumentOutOfRangeException` in `ExtractTimestamp` on Extreme Timestamps
- **Test**: `ExtractTimestamp_ExtremeUnixTimestamps_ShouldNotThrow`
- **Code Location**: `src/BrewYou.ApiService/Services/BrewLinkTelemetryParser.cs:line 810`
  ```csharp
  if (tsElem.TryGetInt64(out var tsVal))
  {
      if (tsVal > 10_000_000_000L)
      {
          return DateTimeOffset.FromUnixTimeMilliseconds(tsVal).UtcDateTime;
      }
      if (tsVal > 100_000_000L)
      {
          return DateTimeOffset.FromUnixTimeSeconds(tsVal).UtcDateTime;
      }
      return DateTime.UtcNow;
  }
  ```
- **Verbatim Exception**:
  ```
  System.ArgumentOutOfRangeException: Valid values are between -62135596800000 and 253402300799999, inclusive. (Parameter 'milliseconds')
  Actual value was 99999999999999999.
     at System.DateTimeOffset.FromUnixTimeMilliseconds(Int64 milliseconds)
     at BrewYou.ApiService.Services.Telemetry.BrewLinkTelemetryParser.ExtractTimestamp(JsonElement root, JsonElement state) in BrewLinkTelemetryParser.cs:line 812
  ```

#### Observation 6: `OverflowException` in `TelemetryThrottlingService.ShouldPersist` on Extreme Decimal Subtraction
- **Test**: `TelemetryThrottlingService_ExtremeDecimals_ShouldNotThrowOverflowException`
- **Code Location**: `src/BrewYou.ApiService/Services/TelemetryThrottlingService.cs:line 51`
  ```csharp
  if (Math.Abs(temperatureC - last.TemperatureC) >= SignificantTempDelta)
  ```
  And line 107:
  ```csharp
  if (specificGravity.HasValue && last.SpecificGravity.HasValue &&
      Math.Abs(specificGravity.Value - last.SpecificGravity.Value) >= SignificantGravityDelta)
  ```
- **Verbatim Exception**:
  ```
  System.OverflowException: Value was either too large or too small for a Decimal.
     at System.Number.ThrowOverflowException(String message)
     at System.Decimal.DecCalc.ScaleResult(Buf24* bufRes, UInt32 hiRes, Int32 scale)
     at System.Decimal.DecCalc.DecAddSub(DecCalc& d1, DecCalc& d2, Boolean sign)
     at System.Decimal.op_Subtraction(Decimal d1, Decimal d2)
     at BrewYou.ApiService.Services.TelemetryThrottlingService.ShouldPersist(Guid equipmentId, DateTime timestampUtc, Decimal temperatureC, BrewLinkCanonicalMetrics metrics, Nullable`1 specificGravity, Nullable`1 stage) in TelemetryThrottlingService.cs:line 51
  ```

#### Observation 7: Schema Asymmetry — Shorthand Key `"target"` Omitted from Flat Keys
- **Code Location**: `src/BrewYou.ApiService/Services/BrewLinkTelemetryParser.cs:line 377`
  In nested temperature extraction (line 332):
  ```csharp
  "target" => ["target_c", "target", "target_temp_c", "target_temp", "setpoint_c", "setpoint"],
  ```
  In flat extraction (line 377):
  ```csharp
  "target" => ["target_temp_c", "targetTempC", "target_temp", "target_c", "setpoint_c", "setpoint"],
  ```
  Notice `"target"` is missing from `flatKeys`. A payload emitting `{"temp": 65.0, "target": 65.0}` parses temperature but loses the target setpoint.

---

## 2. Logic Chain

1. **Acceptance Criteria Requirement**: "Verify graceful handling without unhandled exceptions or service crashes."
2. **Crash Vector via `CanParse`**:
   - `BrewLinkTelemetryParser` is registered at priority 5 (the very first parser evaluated in `TelemetryService.NormalizeJsonPayload`).
   - Every single inbound payload on `/api/v1/telemetry/equipment/{token}` evaluates `CanParse(root, equipment)`.
   - When a payload includes a non-string `"event"` property (such as numeric event codes or arrays), `eventElem.GetString()` immediately throws `InvalidOperationException`.
   - Because `NormalizeJsonPayload` contains no `try ... catch`, this unhandled exception crashes the request pipeline and returns HTTP 500. (Directly proven by Observation 1 and Observation 2).
3. **Crash Vector via `ParseCanonical` and `ResolveTargetDevice`**:
   - Similar unguarded `.GetString()` calls exist on `"mac"`, `"name"`, `"unit"`, `"step_name"`, and `"message"`.
   - Corrupt or third-party telemetry frames containing non-string primitives throw `InvalidOperationException` and terminate the WebSocket receive loop or poll cycle. (Directly proven by Observations 3 and 4).
4. **Crash Vector via `ExtractTimestamp`**:
   - `DateTimeOffset.FromUnixTimeMilliseconds` and `FromUnixTimeSeconds` throw `ArgumentOutOfRangeException` whenever an int64 value exceeds the year 9999 boundary ($> 253402300799999\text{ ms}$).
   - `BrewLinkTelemetryParser.ExtractTimestamp` checks `tsVal > 10_000_000_000L` but has no upper-bound guard or try/catch fallback to `DateTime.UtcNow`. (Directly proven by Observation 5).
5. **Crash Vector via `TelemetryThrottlingService`**:
   - Subtracting decimals with extreme difference (`decimal.MaxValue - (-10m)`) overflows `System.Decimal` and throws `OverflowException`.
   - If an extreme decimal reaches the throttler, the unhandled `OverflowException` crashes background telemetry ingestion. (Directly proven by Observation 6).

---

## 3. Caveats

1. **Robust Aspects Confirmed**:
   - Truncated or syntactically invalid JSON strings passed to `FromMetricsJson` are safely caught and return `null`.
   - Temperature range enforcement in `TelemetryService` correctly rejects -50°C and 250°C with `TelemetryIngestStatus.OutOfRange`, while accepting boundary values (-20°C and 120°C).
   - 30-second countdown jitter throttling works as designed: continuous 1Hz remaining-seconds countdown decrements are throttled to 30-second cadence, while timer completion (`Active: false`) immediately bypasses throttling.
   - Temperature boundary threshold is exact: $|\Delta T| < 0.50^\circ\text{C}$ throttles, while $|\Delta T| \ge 0.50^\circ\text{C}$ persists immediately.
   - `CleanMac` and `FormatMac` successfully handle lowercase, colons, hyphens, and whitespace variations.
2. **Scope of Test Suite**:
   - All tests were conducted against compiled .NET binaries and in-memory WebApplicationFactory integration pipelines.

---

## 4. Conclusion

**Verdict: `REQUEST_CHANGES`**

Milestone 1 cannot be approved in its current state because `BrewLinkTelemetryParser` and `TelemetryThrottlingService` contain multiple unhandled exception paths (`InvalidOperationException`, `ArgumentOutOfRangeException`, and `OverflowException`) that cause server crashes (HTTP 500) and ingestion loop termination on untrusted device payloads.

### Required Changes for Implementation Worker:
1. **Guard `JsonElement.GetString()` with type checks**:
   - In `CanParse`: check `eventElem.ValueKind == JsonValueKind.String` before calling `.GetString()`.
   - In `ParseCanonical`: check `macElem.ValueKind == JsonValueKind.String` before calling `.GetString()`.
   - In `ResolveTargetDevice`: check `mElem.ValueKind == JsonValueKind.String` and `nElem.ValueKind == JsonValueKind.String`.
   - In `ExtractTemperature`: check `u.ValueKind == JsonValueKind.String` and `su.ValueKind == JsonValueKind.String`.
   - In `ExtractStage`: check `snElem.ValueKind == JsonValueKind.String` and `fsnElem.ValueKind == JsonValueKind.String`.
   - In `ExtractInteraction`: check `msgElem.ValueKind == JsonValueKind.String` and `fimElem.ValueKind == JsonValueKind.String`.
2. **Guard Timestamp Conversion in `ExtractTimestamp`**:
   - Wrap `DateTimeOffset.FromUnixTimeMilliseconds` and `FromUnixTimeSeconds` in a `try ... catch (ArgumentOutOfRangeException)` block (or validate `tsVal <= 253_402_300_799_999L && tsVal >= -62_135_596_800_000L`) and fallback safely to `DateTime.UtcNow`.
3. **Guard Decimal Differences in `TelemetryThrottlingService`**:
   - Wrap decimal subtraction in `try ... catch (OverflowException)` or validate difference bounds, persisting immediately if an extreme delta occurs rather than throwing an unhandled exception.
4. **Add `"target"` to `flatKeys` in `ExtractTemperature`**:
   - Include `"target"` in `flatKeys` alongside `"target_temp_c"`, `"target_temp"`, `"target_c"`, etc., for parity with nested schema support.

---

## 5. Verification Method

To independently verify these findings:

1. **Run Adversarial Test Suite**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkTelemetryAdversarialTests
   ```
   *Expected outcome prior to fix*: 6 tests fail with `InvalidOperationException`, `ArgumentOutOfRangeException`, `OverflowException`, and `500 Internal Server Error`.
   *Expected outcome after fix*: 50 passed, 0 failed.

2. **Run Full Test Suite**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1
   ```

3. **Verify Formatting**:
   ```powershell
   dotnet format --verify-no-changes
   ```
