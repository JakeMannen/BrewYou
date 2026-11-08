# Handoff Report: Milestone 1 - Telemetry Parsing and Normalization

**Agent**: `m1_exp_1` (Teamwork Explorer)  
**Parent Agent**: `6986b155-3bf8-4b59-9b95-f99c2273b921`  
**Milestone**: Milestone 1 - Telemetry Ingestion, Normalization & DB Throttling  
**Working Directory**: `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_exp_1`  
**Artifacts Generated**:
- `proposed_BrewLinkTelemetryParser.cs`
- `proposed_BrewLinkTelemetryParserTests.cs`

---

## 1. Observation

### 1.1. Authoritative Requirements and Invariants
- **`ORIGINAL_REQUEST.md:11-12`**:
  > "Implement real-time telemetry ingestion from BrewLink gateways across WebSocket streaming (`ws://<gateway-ip>/ws?token=<api_token>`), MQTT broker topics (`brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry`), and HTTP REST polling fallback (`GET /api/equipment`). Parse and normalize incoming device state (temperature, heating, pump, timer, stage, and user interaction alerts) into `Equipment.LatestMetricsJson`, broadcast live updates over SSE immediately, and throttle database persistence of `EquipmentReading` records to avoid database bloat."
- **`ORIGINAL_REQUEST.md:29-30`**:
  > "- [ ] `BrewLinkTelemetryParserTests` verifies normalization of WebSocket frames and REST state payloads into canonical `LatestMetricsJson` and temperature readings.  
  > - [ ] Telemetry ingestion broadcasts immediate SSE events on state changes and throttles `EquipmentReading` database persistence to 30-second intervals or significant state/temperature deltas."
- **`PROJECT.md:88-118` Canonical Telemetry Schema**:
  ```json
  {
    "currentTempC": 65.0,
    "targetTempC": 65.0,
    "boilTempC": 100.0,
    "heatActive": true,
    "heatPowerPct": 100,
    "rampActive": false,
    "pumpActive": true,
    "autoMode": true,
    "timer": {
      "active": true,
      "paused": false,
      "remainingSeconds": 2340,
      "totalSeconds": 3600
    },
    "stage": {
      "stepIndex": 1,
      "stepName": "Mash In",
      "autoMode": true
    },
    "interaction": {
      "required": false,
      "code": 0,
      "message": "Normal operation"
    },
    "mac": "C4:4F:33:1A:8B:22",
    "rssi": -60,
    "updatedAt": "2026-11-07T19:00:00Z"
  }
  ```

### 1.2. Observed Payloads from Gateway Specifications
From `.agents/skills/brewlink-external-client/SKILL.md`:
1. **WebSocket Telemetry Stream Frame (`event: "telemetry"`)** (`SKILL.md:339-354`):
   ```json
   {
     "event": "telemetry",
     "mac": "C44F331A8B22",
     "equipment_type": "boiler",
     "model": "Grainfather G30",
     "rssi": -60,
     "state": {
       "temperature": {"current_c": 65.0, "target_c": 65.0, "boil_c": 100.0},
       "heating": {"active": true, "power_pct": 100, "ramping": false},
       "pump": {"active": true},
       "timer": {"active": true, "paused": false, "remaining_seconds": 2340, "total_seconds": 3600},
       "stage": {"step_index": 1, "auto_mode": true},
       "interaction": {"required": false, "code": 0, "message": "Normal operation"}
     },
     "timestamp_ms": 1459200
   }
   ```
   *Note*: The gateway provides `timestamp_ms: 1459200`. In ESP32 Arduino/FreeRTOS, this represents the internal boot uptime via `millis()`, NOT a Unix epoch timestamp (since 1,459,200 ms is ~24 minutes, which if parsed as Unix epoch would incorrectly evaluate to January 1, 1970).

2. **HTTP REST Gateway Response (`GET /api/equipment`)** (`SKILL.md:122-165`):
   ```json
   {
     "gateway_id": "C44F331A8B20",
     "count": 1,
     "equipment": [
       {
         "slot": 1,
         "paired": true,
         "connected": true,
         "name": "Grainfather G30 Connect",
         "model": "Grainfather G30 Connect",
         "equipment_type": "boiler",
         "mac": "C4:4F:33:1A:8B:22",
         "rssi": -62,
         "capabilities": {
           "recipe_upload": true,
           "mash_profile": true,
           "fermentation_profile": false,
           "pump_control": true,
           "heat_control": true
         },
         "state": {
           "current_temp_c": 64.8,
           "target_temp_c": 65.0,
           "boil_temp_c": 100.0,
           "heat_active": true,
           "heat_power_pct": 100,
           "pump_active": true,
           "auto_mode": true,
           "ramp_active": false,
           "timer": {
             "active": true,
             "paused": false,
             "remaining_seconds": 2400,
             "total_seconds": 3600
           },
           "stage": {
             "step_index": 1,
             "step_name": "Mash Step 1"
           },
           "interaction": {
             "required": false,
             "code": 0,
             "message": "Normal operation"
           }
         }
       }
     ]
   }
   ```

3. **MQTT Broker Telemetry Topic (`brewlink/{topicKey}/{gatewayId}/equipment/{MAC}/telemetry`)** (`SKILL.md:408`):
   Gateway publishes JSON telemetry payload matching the device state or WebSocket event schema.

4. **Existing Shorthand / Legacy Payload Elements**:
   Observed in `src/BrewYou.ApiService/Services/BrewLinkService.cs:453-460`:
   `temp_c`, `target_temp_c`, `heat_pct`, `pump_on`, `step_index`, `timer_remaining_sec`.

### 1.3. Existing Telemetry Infrastructure in Codebase
- **`ITelemetryParser.cs:6-23`**:
  ```csharp
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
  ```
- **`TelemetryService.cs:22-28`**:
  Current parser registration list:
  ```csharp
  private readonly List<ITelemetryParser> _parsers =
  [
      new UniversalBrewYouTelemetryParser(), // Priority 10
      new ISpindelTelemetryParser(),          // Priority 20
      new TiltTelemetryParser(),              // Priority 30
      new GenericJsonTelemetryParser()        // Priority 100
  ];
  ```
  *Gap*: `BrewLinkTelemetryParser` is not yet defined or registered. It requires `Priority => 5` to intercept BrewLink payloads before `UniversalBrewYouTelemetryParser`.
- **Frontend Attribute Consumer**:
  Observed in `src/BrewYou.Web/src/routes/inventory/equipment/+page.svelte:94-118` and `equipment-telemetry-attributes.test.ts:4-23`:
  `getParsedAttributes` parses `EquipmentDto.latestMetricsJson` as key-value pairs (`for (const [k, v] of Object.entries(parsed))`). Nested objects and primitives serialize cleanly.

---

## 2. Logic Chain

1. **Parser Registration & Hierarchy**:
   - *Observation*: `TelemetryService.NormalizeJsonPayload` evaluates parsers ordered by `Priority`. Universal parser has Priority 10.
   - *Reasoning*: A dedicated `BrewLinkTelemetryParser` implementing `ITelemetryParser` with `Priority => 5` ensures that any equipment configured with `ConnectionType == EquipmentConnectionType.BrewLink` or any JSON payload containing BrewLink markers (`event: "telemetry"`, `equipment` array with `gateway_id`, `mac` with `state`, or BrewLink hardware keys) is processed by `BrewLinkTelemetryParser` without being misidentified by generic parsers.

2. **Multi-Transport Envelope Resolution**:
   - *Observation*: WebSockets deliver `{ "event": "telemetry", "state": { ... } }`, REST delivers `{ "gateway_id": "...", "equipment": [ { "slot": 1, "mac": "...", "state": { ... } } ] }`, and MQTT delivers either WebSocket frames or flat device state.
   - *Reasoning*: `BrewLinkTelemetryParser.ParseCanonical` must first resolve the target device:
     - If the root has an `equipment` JSON array (REST `GET /api/equipment`), it cross-references `equipment.ConnectionConfigJson` to match by `targetMac`, `slot`, or `targetName`, falling back to the first device in the array.
     - If the device contains a nested `state` object, extraction targets `state` while reading envelope metadata (`mac`, `rssi`, `timestamp`) from the parent.

3. **Field Normalization to Canonical CamelCase Schema**:
   - *Observation*: Different firmware versions and transports emit nested (`state.temperature.current_c`, `state.heating.power_pct`), flat (`state.current_temp_c`, `state.heat_power_pct`), or legacy shorthand (`temp_c`, `heat_pct`, `pump_on`).
   - *Reasoning*: The parser normalizes all 14 required fields into strongly-typed C# records:
     1. `currentTempC`: `decimal?` (with Fahrenheit detection and conversion to Celsius)
     2. `targetTempC`: `decimal?` (with Fahrenheit conversion)
     3. `boilTempC`: `decimal?` (with Fahrenheit conversion)
     4. `heatActive`: `bool?` (direct bool or inferred when `heatPowerPct > 0`)
     5. `heatPowerPct`: `decimal?` (clamped 0 to 100)
     6. `rampActive`: `bool?`
     7. `pumpActive`: `bool?` (from `pump.active`, `pump_active`, or `pump_on`)
     8. `autoMode`: `bool?` (from root, state, or `stage.auto_mode`)
     9. `timer`: `BrewLinkTimerMetrics` (`active`, `paused`, `remainingSeconds`, `totalSeconds`)
     10. `stage`: `BrewLinkStageMetrics` (`stepIndex`, `stepName`, `autoMode`)
     11. `interaction`: `BrewLinkInteractionMetrics` (`required`, `code`, `message`)
     12. `mac`: `string?` (formatted canonically to uppercase colon-delimited `XX:XX:XX:XX:XX:XX`)
     13. `rssi`: `short?`
     14. `updatedAt`: `DateTime` (ISO-8601 UTC)
   - Serialization to `Equipment.LatestMetricsJson` uses `JsonNamingPolicy.CamelCase` and `JsonIgnoreCondition.WhenWritingNull`, matching `PROJECT.md:88-118`.

4. **Timestamp & Uptime Resolution**:
   - *Observation*: Gateway uptime in `timestamp_ms` (e.g. `1459200`) is <100,000,000. True epoch timestamps are $\ge 100,000,000$ (seconds) or $>10,000,000,000$ (milliseconds).
   - *Reasoning*: If `timestamp_ms` or `timestamp` is a small number (<100,000,000), it represents device uptime since boot; the parser falls back to `DateTime.UtcNow`. If it represents real epoch time, it converts to UTC `DateTime`. String timestamps (`updated_at`, `timestamp`) are parsed with `DateTimeOffset.TryParse`.

5. **Temperature Unit Conversion**:
   - *Observation*: Gateways may emit values in Fahrenheit via `"unit": "F"`, `"temp_unit": "F"`, or properties like `current_temp_f`, `target_temp_f`, `boil_temp_f`.
   - *Reasoning*: The parser applies $C = (F - 32) \times \frac{5}{9}$, rounded to 2 decimal places using `TelemetryHelper.ConvertToCelsius`.

---

## 3. Caveats

1. **Tap Equipment Ingestion**:
   - Tap equipment does not support IoT sensor telemetry (`TelemetryService.cs:75-78`). If Tap equipment receives BrewLink telemetry, it should be ignored or routed to the assigned keg.
2. **Missing Temperature in Non-Telemetry Frames**:
   - Frames like `event: "cmd_ack"` or state frames without temperature return `null` for `ITelemetryParser.Parse` (as `NormalizedTelemetryData` requires a valid `TemperatureC`), but `ParseCanonical` still returns the parsed metrics for state tracking.
3. **Firmware Key Precedence**:
   - When both nested `state.temperature.current_c` and flat `state.current_temp_c` exist in the same payload, nested object keys take precedence.

---

## 4. Conclusion

The design for `BrewLinkTelemetryParser.cs` and `BrewLinkTelemetryParserTests.cs` is complete, robust, and fully specified. The drop-in code artifacts have been authored in this directory:
- `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_exp_1\proposed_BrewLinkTelemetryParser.cs`
- `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_exp_1\proposed_BrewLinkTelemetryParserTests.cs`

### 4.1. Implementation File: `BrewLinkTelemetryParser.cs`
**Target Path**: `src/BrewYou.ApiService/Services/Telemetry/BrewLinkTelemetryParser.cs`  
**Namespace**: `BrewYou.ApiService.Services.Telemetry`  
**Summary of Key Types**:
- `public sealed record BrewLinkTimerMetrics(bool Active, bool Paused, int? RemainingSeconds, int? TotalSeconds);`
- `public sealed record BrewLinkStageMetrics(int? StepIndex, string? StepName, bool? AutoMode = null);`
- `public sealed record BrewLinkInteractionMetrics(bool Required, int Code, string? Message);`
- `public sealed record BrewLinkCanonicalMetrics(...)` matching the 14 attributes.
- `public class BrewLinkTelemetryParser : ITelemetryParser`:
  - `public int Priority => 5;`
  - `public bool CanParse(JsonElement root, Equipment equipment, string? preferredPath = null);`
  - `public NormalizedTelemetryData? Parse(JsonElement root, Equipment equipment, string? preferredPath = null);`
  - `public static BrewLinkCanonicalMetrics? ParseCanonical(JsonElement root, Equipment? equipment = null);`
  - `public static string ToMetricsJson(BrewLinkCanonicalMetrics metrics);`
  - `public static string? FormatMac(string? rawMac);`
  - `public static string CleanMac(string? rawMac);`

### 4.2. Implementation File: `BrewLinkTelemetryParserTests.cs`
**Target Path**: `tests/BrewYou.ApiService.Tests/BrewLinkTelemetryParserTests.cs`  
**Namespace**: `BrewYou.ApiService.Tests`  
**Summary of Test Scenarios** (18 comprehensive tests):
1. `CanParse_BrewLinkEquipment_ReturnsTrue`: equipment with `ConnectionType.BrewLink` recognized.
2. `CanParse_WebSocketTelemetryEvent_ReturnsTrue`: recognizes `event: "telemetry"`.
3. `CanParse_RestGatewayPayloadWithEquipmentArray_ReturnsTrue`: recognizes `gateway_id` and `equipment` array.
4. `CanParse_DeviceObjectWithMacAndState_ReturnsTrue`: recognizes `mac` + `state` object.
5. `CanParse_FlatPayloadWithBrewLinkAttributes_ReturnsTrue`: recognizes `current_temp_c` + `heat_active`.
6. `CanParse_UnrelatedJsonWithNonBrewLinkEquipment_ReturnsFalse`: rejects non-BrewLink payloads.
7. `CanParse_NonObjectJson_ReturnsFalse`: rejects arrays and non-objects.
8. `Parse_WebSocketFrame_NestedSchema_ExtractsAllCanonicalFieldsAccurately`: verifies complete 14-field extraction from WebSocket frame.
9. `Parse_RestGatewayArray_MatchesTargetEquipmentByMac_ExtractsCanonicalFields`: verifies multi-device REST array matching by MAC.
10. `Parse_RestGatewayArray_MatchesTargetEquipmentBySlot_WhenConfigured`: verifies REST array matching by slot number.
11. `Parse_MqttTopicPayload_FlatSchema_ExtractsCanonicalFields`: verifies MQTT broker flat message ingestion.
12. `Parse_TemperatureConversion_FahrenheitToCelsius_NestedAndFlat`: verifies $149^\circ\text{F} \to 65.0^\circ\text{C}$ and $68^\circ\text{F} \to 20.0^\circ\text{C}$.
13. `Parse_ShorthandProperties_FallbacksForLegacyGatewayEmissions`: verifies `temp_c`, `heat_pct`, `pump_on`, `step_index`, `timer_remaining_sec`.
14. `Parse_ActiveInteractionAlertPrompt_ExtractsPromptAndFlags`: verifies prompt messages (`"Add Grain"`, `"Sparge Water Ready"`) and `interaction.required = true`.
15. `Parse_MissingOptionalObjects_GracefulNullHandlingWithoutExceptions`: verifies robust parsing when timer, stage, or interaction are absent.
16. `Parse_TimestampHandling_EpochMillisecondsVsDeviceUptime`: verifies uptime fallback to `UtcNow` vs epoch ms parsing.
17. `Parse_MacAddressFormatting_NormalizesToUppercaseColonDelimited`: verifies formatting from `c44f331a8b22` to `C4:4F:33:1A:8B:22`.
18. `RoundTripSerialization_LatestMetricsJsonMatchesFrontendRequirements`: verifies camelCase JSON serialization and deserialization parity.

### 4.3. Registration in `TelemetryService.cs`
In `src/BrewYou.ApiService/Services/TelemetryService.cs:22-29`, register `BrewLinkTelemetryParser` at the top of `_parsers`:
```csharp
private readonly List<ITelemetryParser> _parsers =
[
    new BrewLinkTelemetryParser(),
    new UniversalBrewYouTelemetryParser(),
    new ISpindelTelemetryParser(),
    new TiltTelemetryParser(),
    new GenericJsonTelemetryParser()
];
```

---

## 5. Verification Method

### 5.1. Test Execution Commands
The test suite can be run with PowerShell outside the sandbox (`BypassSandbox: true`):

1. **Execute BrewLink Telemetry Parser Tests**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~BrewLinkTelemetryParserTests"
   ```
2. **Execute All Telemetry Parser Tests**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~TelemetryParserTests"
   ```
3. **Execute Full Backend Test Suite**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj
   ```
4. **Verify Formatting & Static Analysis**:
   ```powershell
   dotnet format --verify-no-changes
   ```

### 5.2. Files to Inspect
- `src/BrewYou.ApiService/Services/Telemetry/BrewLinkTelemetryParser.cs`
- `tests/BrewYou.ApiService.Tests/BrewLinkTelemetryParserTests.cs`
- `src/BrewYou.ApiService/Services/TelemetryService.cs`
- `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_exp_1\proposed_BrewLinkTelemetryParser.cs`
- `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_exp_1\proposed_BrewLinkTelemetryParserTests.cs`

### 5.3. Invalidation Conditions
- Any parsing logic that outputs PascalCase or snake_case keys in `LatestMetricsJson` (must remain camelCase).
- Any failure to convert Fahrenheit temperatures to Celsius when units or `_f` suffixes are provided.
- Any regression that breaks existing `TelemetryParserTests` or `TelemetryTests`.
