# Handoff Report: Requirement R1 - Multi-Transport Telemetry Ingestion and State Normalization

## 1. Observation

### 1.1. Authoritative Requirements & External Specifications
- **Authoritative User Request**: `ORIGINAL_REQUEST.md:11-12`:
  > "### R1. Multi-Transport Telemetry Ingestion and State Normalization
  > Implement real-time telemetry ingestion from BrewLink gateways across WebSocket streaming (`ws://<gateway-ip>/ws?token=<api_token>`), MQTT broker topics (`brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry`), and HTTP REST polling fallback (`GET /api/equipment`). Parse and normalize incoming device state (temperature, heating, pump, timer, stage, and user interaction alerts) into `Equipment.LatestMetricsJson`, broadcast live updates over SSE immediately, and throttle database persistence of `EquipmentReading` records to avoid database bloat."
- **Acceptance Criteria**: `ORIGINAL_REQUEST.md:28-30`:
  > "- [ ] `BrewLinkTelemetryParserTests` verifies normalization of WebSocket frames and REST state payloads into canonical `LatestMetricsJson` and temperature readings.
  > - [ ] Telemetry ingestion broadcasts immediate SSE events on state changes and throttles `EquipmentReading` database persistence to 30-second intervals or significant state/temperature deltas."
- **BrewLink External Client Reference**: `.agents/skills/brewlink-external-client/SKILL.md`:
  - **WebSocket frame format (`event: "telemetry"`)** (`SKILL.md:339-354`):
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
  - **MQTT topic pattern & payload** (`SKILL.md:408`):
    - Topic: `brewlink/{topicKey}/{gatewayId}/equipment/{MAC}/telemetry` (or wildcard subscribe `brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry`).
    - Payload: UTF-8 JSON containing the telemetry event or device state.
  - **HTTP REST format (`GET /api/equipment`)** (`SKILL.md:122-165`):
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

### 1.2. Existing Backend Telemetry Infrastructure
- **Entity Definitions**:
  - `Equipment.cs:133-168`:
    - `decimal? CurrentTemperatureC`
    - `DateTime? TemperatureUpdatedAt`
    - `DateTime? LastTelemetryAt`
    - `string? LatestMetricsJson` (stores cached JSON payload of latest telemetry attributes).
    - `EquipmentConnectionType ConnectionType`: supports `None`, `HttpPush`, `HttpPoll`, `Mqtt`, `BrewLink`.
    - `string? ConnectionConfigJson`: stores connection options (e.g. `brewLinkGatewayId`, `targetMac`, `slot`, `targetName`).
  - `EquipmentReading.cs:3-90`:
    - Stores time-series reading: `EquipmentId`, `BatchId`, `Stage`, `BatchMashStepId`, `StepName`, `Timestamp`, `TemperatureC`, `SpecificGravity`, `PressureBar`, `BatteryPercent`, `BatteryVoltage`, `TiltDegrees`, `Rssi`, `MetricsJson`, `Source`, `RawPayload`, `CreatedAt`.
  - `BrewLinkGateway.cs:3-43`:
    - Stores paired gateway: `UserId`, `Name`, `Host`, `GatewayId`, `ApiToken` (protected), `MqttTopicKey` (protected), `ClientName`.

- **Existing Telemetry Processing & Parsers**:
  - `ITelemetryParser.cs:6-23`:
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
    public interface ITelemetryParser {
        int Priority { get; }
        bool CanParse(JsonElement root, Equipment equipment, string? preferredPath = null);
        NormalizedTelemetryData? Parse(JsonElement root, Equipment equipment, string? preferredPath = null);
    }
    ```
  - `TelemetryService.cs:22-28`:
    Currently instantiates `_parsers` statically:
    - `UniversalBrewYouTelemetryParser` (Priority 10)
    - `ISpindelTelemetryParser` (Priority 20)
    - `TiltTelemetryParser` (Priority 30)
    - `GenericJsonTelemetryParser` (Priority 100)
    - **Defect/Gap**: There is NO `BrewLinkTelemetryParser` currently registered.
  - `BrewLinkService.cs:450-461`:
    Existing partial parsing in `GetGatewayEquipmentAsync` checks shorthand properties:
    ```csharp
    state = new BrewLinkEquipmentStateDto(
        TempC: stateProp.TryGetProperty("temp_c", out var tc) && tc.TryGetDecimal(out var tcVal) ? tcVal : null,
        TargetTempC: stateProp.TryGetProperty("target_temp_c", out var ttc) && ttc.TryGetDecimal(out var ttcVal) ? ttcVal : null,
        HeatPct: stateProp.TryGetProperty("heat_pct", out var hp) && hp.TryGetDecimal(out var hpVal) ? hpVal : null,
        PumpOn: stateProp.TryGetProperty("pump_on", out var po) && po.ValueKind is JsonValueKind.True or JsonValueKind.False ? po.GetBoolean() : null,
        StepIndex: stateProp.TryGetProperty("step_index", out var si) && si.TryGetInt32(out var siVal) ? siVal : null,
        TimerRemainingSec: stateProp.TryGetProperty("timer_remaining_sec", out var tr) && tr.TryGetInt32(out var trVal) ? trVal : null
    );
    ```
    This misses standard `current_temp_c` or nested `temperature.current_c`, `heat_active`, `heat_power_pct`, `pump_active`, full `timer` (active, paused, total), `stage` step name, and `interaction` alert prompt fields.

- **Current Persistence Bloat Defect**:
  - `TelemetryService.cs:139-155` and `745`:
    Every call to `IngestTelemetryAsync`, `IngestMqttTelemetryAsync`, or `ExecutePollAsync` executes:
    ```csharp
    _db.EquipmentReadings.Add(reading);
    await _db.SaveChangesAsync();
    BroadcastIfAssociated(reading, equipment, targetTempC, recordedGravity);
    ```
    At 1Hz streaming over WebSocket, an unthrottled loop inserts 3,600 rows/hour (86,400 rows/day) per vessel into PostgreSQL.

- **SSE Streaming Infrastructure**:
  - `ITelemetryBroadcastService.cs:54-85`:
    - `BroadcastEquipmentReading(string userId, EquipmentTelemetryUpdateDto update)`
    - `BroadcastReading(BatchEquipmentReadingDto reading)`
    - `EquipmentTelemetryUpdateDto` includes `MetricsJson`, `TemperatureC`, `Timestamp`, `Source`, `BatchId`, `EquipmentId`, `EquipmentName`.
  - `EquipmentEndpoints.cs:27-100`:
    - Endpoint: `GET /api/v1/inventory/equipment/telemetry-stream` (SSE, `text/event-stream`, ping every 15s).
    - Event name: `reading`. Serialized via `SseJsonOptions` (CamelCase).
  - `BatchEndpoints.cs:469-520`:
    - Endpoint: `GET /api/v1/batches/{id}/telemetry-stream` (SSE, `text/event-stream`).

- **MQTT Infrastructure**:
  - `MqttService.cs:205-242`:
    - Subscribes to `brewlink/discovery/+` and `EquipmentConnectionType.Mqtt` topics.
    - **Gap**: Does not yet subscribe to `brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry` for paired `BrewLinkGateway` records.
  - `MqttService.cs:268-305`:
    - Dispatches received messages to `_brewLinkDiscoveryService` or `telemetryService.IngestMqttTelemetryAsync`.

- **Test Suite Status**:
  - Build & tests verified outside sandbox (`BypassSandbox: true`):
    - `dotnet test ... --filter "FullyQualifiedName~TelemetryParserTests"` -> Passed (23/23).
    - `dotnet test ... --filter "FullyQualifiedName~BrewLinkEndpointsTests"` -> Passed (30/30).
    - `dotnet test ... --filter "FullyQualifiedName~MqttTests"` -> Passed (43/43).
    - `dotnet test ... --filter "FullyQualifiedName~BackupEndpointsTests"` -> Passed (14/14).

---

## 2. Logic Chain

1. **Multi-Transport Ingestion Strategy**:
   - *Premise*: BrewLink gateways communicate over local WebSockets (`ws://<ip>/ws?token=<token>`), broker MQTT topics (`brewlink/{topicKey}/{gatewayId}/equipment/{mac}/telemetry`), and HTTP REST (`GET /api/equipment`).
   - *Inference*: The backend must ingest telemetry through all three channels:
     - **Transport 1 (WebSocket)**: A background service `BrewLinkWebSocketManager` (hosted via `BrewLinkWebSocketWorker`) maintains full-duplex WebSocket connections to paired gateways using `ClientWebSocket`. Validates hosts using `NetworkSecurityValidator` (with `allowPrivateNetworks: true`). On frame `event == "telemetry"`, resolves equipment by MAC/slot and dispatches to ingestion.
     - **Transport 2 (MQTT)**: `MqttService.RefreshSubscriptionsAsync` must include `brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry` for all paired gateways. When a message arrives matching that pattern, `HandleApplicationMessageReceivedAsync` extracts gateway ID and MAC, normalizes the payload, and feeds the ingestion pipeline.
     - **Transport 3 (HTTP REST Polling Fallback & On-Demand Sync)**: `EquipmentPollingWorker` or an on-demand sync endpoint (`POST /api/v1/inventory/equipment/{id}/brewlink/sync`) calls `GET /api/equipment`, parses device state via the same parser, and feeds the ingestion pipeline.

2. **State Normalization Scheme (`Equipment.LatestMetricsJson`)**:
   - *Premise*: WebSocket frames use nested objects (`state.temperature.current_c`, `state.heating.active`, `state.pump.active`), REST uses flat state properties (`current_temp_c`, `heat_active`, `pump_active`), and earlier prototypes used shorthand keys (`temp_c`, `heat_pct`, `pump_on`).
   - *Inference*: A canonical `BrewLinkTelemetryParser` must inspect both nested and flat properties and produce a single canonical, strongly-typed JSON schema with camelCase naming for `Equipment.LatestMetricsJson`:
     ```json
     {
       "currentTempC": 64.8,
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
         "remainingSeconds": 2400,
         "totalSeconds": 3600
       },
       "stage": {
         "stepIndex": 1,
         "stepName": "Mash Step 1",
         "autoMode": true
       },
       "interaction": {
         "required": false,
         "code": 0,
         "message": "Normal operation"
       },
       "mac": "C44F331A8B22",
       "rssi": -62,
       "updatedAt": "2026-11-07T19:00:00Z"
     }
     ```
   - *Compatibility*:
     - `Equipment.CurrentTemperatureC` is updated directly on the entity.
     - `getParsedAttributes` in `src/BrewYou.Web/src/routes/inventory/equipment/+page.svelte` extracts flat keys (`heatActive`, `heatPowerPct`, `pumpActive`, `targetTempC`) for immediate attribute badges.
     - `BrewLinkControlPanel.svelte` (Requirement R4) deserializes the canonical JSON directly to control heating power, pump toggles, hardware timer, and interactive prompt alerts.

3. **Immediate SSE Broadcast vs. Throttled Database Persistence**:
   - *Premise*: Live telemetry arrives at 1Hz or faster, but acceptance criteria requires immediate SSE broadcasts on state changes and throttled database persistence to 30s intervals or significant state/temperature deltas.
   - *Inference*: Telemetry ingestion must decouple **live broadcast** from **database row insertion**:
     - **Live Broadcast (Immediate, 0s delay)**: Every valid frame immediately updates `Equipment.CurrentTemperatureC`, `Equipment.LatestMetricsJson`, `Equipment.TemperatureUpdatedAt`, and `Equipment.LastTelemetryAt`, and fires `ITelemetryBroadcastService.BroadcastEquipmentReading` (and `BroadcastReading` if attached to an active batch).
     - **Database Persistence (`EquipmentReading`) Throttling**: A new `EquipmentReading` record is inserted into `_db.EquipmentReadings` if and only if:
       1. First reading ever for this equipment (`lastPersisted == null`), OR
       2. At least 30 seconds have elapsed since last persisted reading (`(now - lastPersisted.Timestamp).TotalSeconds >= 30.0`), OR
       3. Significant temperature delta: `Math.Abs(tempC - lastPersisted.TemperatureC) >= 0.5m`, OR
       4. Significant discrete state change:
          - `heatActive` changed (turned on/off)
          - `pumpActive` changed (turned on/off)
          - `interaction.required` changed (prompt appeared or was dismissed)
          - `stage.stepIndex` changed (mash step advanced)
          - `timer.active` or `timer.paused` changed (timer started, paused, or completed)
     - *Result*: Zero database bloat while brewers enjoy instantaneous, real-time UI updates.

4. **Integration with Existing Services & Batches**:
   - *Premise*: When equipment is used in an active batch (e.g. Grainfather G30 as `BoilerId` or `MashTunId`), `TelemetryService.CreateAndAssociateReadingAsync` updates active step actual temperature and sends batch readings.
   - *Inference*: Ingesting BrewLink telemetry updates active batch step temperatures seamlessly and broadcasts over both `/api/v1/inventory/equipment/telemetry-stream` and `/api/v1/batches/{id}/telemetry-stream`.

---

## 3. Caveats

1. **Direct Gateway Network Reachability**:
   - In cloud-hosted deployments (outside the user's home LAN), backend WebSocket connections to private IPs (e.g. `192.168.1.50`) cannot be established directly.
   - *Mitigation*: The multi-transport design explicitly provides MQTT topic streaming (`brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry`) via a shared or cloud broker and on-demand client-triggered HTTP sync (`POST .../brewlink/sync`) for cloud setups.
2. **SSRF URI Scheme Validation**:
   - `NetworkSecurityValidator.ValidateUrlAsync` currently enforces `uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps`.
   - *Mitigation*: For WebSocket URLs (`ws://` / `wss://`), the validator should either accept WebSocket schemes or validate the HTTP equivalent before connecting `ClientWebSocket`.
3. **Firmware Payload Variations**:
   - Different ESP32 firmware revisions may emit either full nested objects (`state.temperature.current_c`) or flattened properties (`state.current_temp_c`), or shorthand (`temp_c`).
   - *Mitigation*: The `BrewLinkTelemetryParser` must defensively parse all three formats with fallback precedence.

---

## 4. Conclusion

1. **Parser & Data Contracts**:
   - Create `src/BrewYou.ApiService/Services/Telemetry/BrewLinkTelemetryParser.cs` implementing `ITelemetryParser` (Priority 5).
   - Normalize all incoming WebSocket, MQTT, and REST payloads into canonical `BrewLinkCanonicalMetrics` and serialize as camelCase `LatestMetricsJson`.
2. **Ingestion Services**:
   - Create `BrewLinkWebSocketManager` / `BrewLinkWebSocketWorker` to maintain persistent WebSocket connections to paired gateways.
   - Extend `MqttService.RefreshSubscriptionsAsync` to subscribe to `brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry`.
   - Add on-demand sync in `BrewLinkService.SyncEquipmentStateAsync` (for `POST /api/v1/inventory/equipment/{id}/brewlink/sync`).
3. **Throttling & SSE Dispatch**:
   - Update `TelemetryService` to immediately broadcast SSE on every incoming state change.
   - Implement an in-memory `EquipmentPersistenceTracker` to throttle `EquipmentReading` database insertions to 30-second windows or significant temperature/state deltas ($\ge 0.5^\circ\text{C}$, pump, heat, alert, step, timer toggles).
4. **Test Suite**:
   - Add `tests/BrewYou.ApiService.Tests/BrewLinkTelemetryParserTests.cs` to test:
     - WebSocket frame normalization (`event: "telemetry"`)
     - REST state normalization (`GET /api/equipment`)
     - MQTT topic payload normalization
     - Shorthand property fallbacks (`temp_c`, `heat_pct`, `pump_on`, `timer_remaining_sec`)
     - Missing optional objects (null timer, null stage, null interaction)
     - Unit conversion (Fahrenheit to Celsius)
   - Add integration tests in `TelemetryTests.cs` verifying immediate SSE broadcasts and database persistence throttling.

---

## 5. Verification Method

### 5.1. Independent Verification Commands
Run the test suites with `BypassSandbox: true` (or normal terminal outside sandbox):

1. **Verify Telemetry Parser Tests**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~TelemetryParserTests"
   ```
2. **Verify BrewLink Gateway Tests**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~BrewLinkEndpointsTests"
   ```
3. **Verify MQTT Service Tests**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~MqttTests"
   ```
4. **Verify Backup & Restore Compatibility**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~BackupEndpointsTests"
   ```
5. **Verify Full Backend Build**:
   ```powershell
   dotnet build tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj
   ```

### 5.2. Files to Inspect
- `src/BrewYou.ApiService/Services/Telemetry/ITelemetryParser.cs`
- `src/BrewYou.ApiService/Services/Telemetry/TelemetryParsers.cs`
- `src/BrewYou.ApiService/Services/TelemetryService.cs`
- `src/BrewYou.ApiService/Services/MqttService.cs`
- `src/BrewYou.ApiService/Services/BrewLinkService.cs`
- `src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs`
- `tests/BrewYou.ApiService.Tests/TelemetryParserTests.cs`

### 5.3. Invalidation Conditions
- Any change to `LatestMetricsJson` that breaks `getParsedAttributes` in `src/BrewYou.Web/src/routes/inventory/equipment/+page.svelte`.
- Any unthrottled loop writing `EquipmentReading` rows at 1Hz without adhering to the 30-second / state delta threshold.
- Rejection of `ConnectionType = BrewLink` on non-All-in-One Boilers or Fermenters (violating Requirement R3).
