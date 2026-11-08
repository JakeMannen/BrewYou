# Handoff Report: Multi-Transport Telemetry Ingestion Architecture (Milestone 1)

## 1. Observation

### 1.1. Authoritative Requirements & External Specifications
- **Authoritative User Request** (`ORIGINAL_REQUEST.md:11-12`):
  > "### R1. Multi-Transport Telemetry Ingestion and State Normalization
  > Implement real-time telemetry ingestion from BrewLink gateways across WebSocket streaming (`ws://<gateway-ip>/ws?token=<api_token>`), MQTT broker topics (`brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry`), and HTTP REST polling fallback (`GET /api/equipment`). Parse and normalize incoming device state (temperature, heating, pump, timer, stage, and user interaction alerts) into `Equipment.LatestMetricsJson`, broadcast live updates over SSE immediately, and throttle database persistence of `EquipmentReading` records to avoid database bloat."
- **Acceptance Criteria** (`ORIGINAL_REQUEST.md:28-30`):
  > "- [ ] `BrewLinkTelemetryParserTests` verifies normalization of WebSocket frames and REST state payloads into canonical `LatestMetricsJson` and temperature readings.
  > - [ ] Telemetry ingestion broadcasts immediate SSE events on state changes and throttles `EquipmentReading` database persistence to 30-second intervals or significant state/temperature deltas."
- **BrewLink External Client Specification** (`.agents/skills/brewlink-external-client/SKILL.md`):
  - **WebSocket URL & Auth** (`SKILL.md:270-275`):
    - URL query token: `ws://<gateway-ip>/ws?token=<api_token>`
    - URL header auth: `ws://<gateway-ip>/ws` with `Authorization: Bearer <api_token>`
  - **WebSocket Telemetry Frame** (`SKILL.md:339-354`):
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
  - **MQTT Topic Structure** (`SKILL.md:396-413`):
    - Prefix: `brewlink/{mqtt_topic_key}/{gateway_id}/`
    - Telemetry stream: `brewlink/{topicKey}/{gatewayId}/equipment/{MAC}/telemetry` (wildcard subscribe: `brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry`).
    - Announce: `brewlink/{topicKey}/{gatewayId}/equipment/{MAC}/announce`.
  - **HTTP REST Polling Format** (`SKILL.md:118-166`):
    - Endpoint: `GET /api/equipment` on port 80.
    - Headers: `Authorization: Bearer <api_token>` and `X-BrewLink-Token: <api_token>`.
    - Returns `{ "gateway_id": "...", "count": 1, "equipment": [ { "slot": 1, "mac": "C4:4F:33:1A:8B:22", "name": "...", "state": { ... } } ] }`.

### 1.2. Existing Codebase Analysis
- **Entity Models**:
  - `src/BrewYou.ApiService/Data/Entities/BrewLinkGateway.cs:3-43`:
    - `Guid Id`: Database primary key.
    - `string UserId`: Owner user identifier.
    - `string Name`: User-assigned label (e.g., "Garage BrewLink").
    - `string Host`: IP/hostname (e.g., "192.168.1.50" or "192.168.1.50:80").
    - `string GatewayId`: ESP32 12-char hex identifier (e.g., "C44F331A8B20").
    - `string ApiToken`: Protected via `IDataProtector` ("BrewYou.BrewLinkCredentials").
    - `string? MqttTopicKey`: Protected via `IDataProtector` ("BrewYou.BrewLinkCredentials").
    - `string? ClientName`: Pairing client identifier (e.g., "BrewYou-A4F2").
  - `src/BrewYou.ApiService/Data/Entities/Equipment.cs:133-185`:
    - `decimal? CurrentTemperatureC`, `DateTime? TemperatureUpdatedAt`, `DateTime? LastTelemetryAt`.
    - `string? LatestMetricsJson`: Stores canonical JSON schema of latest device metrics.
    - `EquipmentConnectionType ConnectionType`: Enum including `None`, `HttpPush`, `HttpPoll`, `Mqtt`, `BrewLink`.
    - `string? ConnectionConfigJson`: Serialized JSON storing gateway binding options.
- **Equipment Gateway Binding Validation & Config**:
  - `src/BrewYou.ApiService/Validation/Validators.cs:558-607`:
    `ConnectionConfigJson` for `EquipmentConnectionType.BrewLink` requires:
    ```json
    {
      "brewLinkGatewayId": "9f7b4943-3e11-4712-b91c-197e41e3df6a",
      "targetMac": "C4:4F:33:1A:8B:22",
      "targetName": "Grainfather G30 Connect",
      "slot": 1
    }
    ```
    Validation requires valid `brewLinkGatewayId` (non-empty `Guid`) AND at least one of: `targetMac` (12 hex chars), `slot` (1..3), or non-empty `targetName`.
  - `src/BrewYou.ApiService/Services/BrewLinkService.cs:747-755`:
    `NormalizeMac(string rawMac)`:
    `NonHexRegex().Replace(rawMac, string.Empty).ToUpperInvariant()` (verifies 12 hex characters).
- **Background Workers & DI Registration**:
  - `src/BrewYou.ApiService/Program.cs:134-157`:
    - Singletons: `ITelemetryBroadcastService`, `IBrewLinkDiscoveryService`, `IMqttConnectivityChecker`, `IMqttService`.
    - Scoped: `ITelemetryService`, `IEquipmentService`, `BrewYouDbContext`.
    - Hosted Services: `EquipmentPollingWorker`, `MqttWorker`, `BrewLinkDiscoveryWorker`.
  - `src/BrewYou.ApiService/Services/EquipmentPollingWorker.cs:50-54`:
    Currently queries `e.ConnectionType == EquipmentConnectionType.HttpPoll` every 30s. Does not poll `EquipmentConnectionType.BrewLink`.
  - `src/BrewYou.ApiService/Services/MqttService.cs:205-242` and `267-305`:
    - Subscribes to `brewlink/discovery/+` and `EquipmentConnectionType.Mqtt` topics.
    - Does NOT yet subscribe to `brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry` for `BrewLinkGateway` records.
- **Network Security Validation**:
  - `src/BrewYou.ApiService/Services/NetworkSecurityValidator.cs:22-41`:
    - `ValidateUrlAsync` enforces `uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps`.
    - If called directly with `ws://...`, it returns `Only HTTP and HTTPS URLs are allowed.`
    - `BrewLinkService.ValidateAndNormalizeHostAsync` (`BrewLinkService.cs:767-809`) validates against `candidateUrl = $"http://{trimmed}"` with `allowPrivateNetworks: true` and explicitly blocks loopback (`localhost`, `127.0.0.1`, `::1`).
- **Telemetry Ingestion & Broadcast**:
  - `src/BrewYou.ApiService/Services/TelemetryService.cs:139-155`:
    Every call to `IngestTelemetryAsync`, `IngestMqttTelemetryAsync`, or `ExecutePollAsync` writes a row to `_db.EquipmentReadings` and calls `_db.SaveChangesAsync()` before broadcasting.

---

## 2. Logic Chain

### 2.1. Ingestion Pipeline 1: WebSocket Streaming (`ws://<gateway-ip>/ws?token=<api_token>`)
1. *Premise*: BrewLink gateways broadcast real-time telemetry frames at 1Hz over WebSocket. A brewery installation may have 1 to 5 gateways, each communicating with up to 3 BLE devices.
2. *Connection Lifecycle Architecture*:
   - Follow the established architecture in `BrewYou.ApiService` where a **Singleton Manager** holds active state and sockets, driven by a **Background Worker**:
     - `IBrewLinkWebSocketManager` / `BrewLinkWebSocketManager` (Singleton):
       Maintains active `ClientWebSocket` connections keyed by `BrewLinkGateway.Id` in a `ConcurrentDictionary<Guid, GatewayWebSocketSession>`.
     - `BrewLinkWebSocketWorker : BackgroundService` (Hosted Service):
       Runs a periodic reconciliation loop (every 15–30s) querying active `BrewLinkGateway` entities in EF Core and requesting the manager to connect newly paired gateways or dispose removed gateways.
3. *Authentication & Connection Handshake*:
   - For gateway `g`:
     - Validate host safety using `NetworkSecurityValidator.ValidateUrlAsync($"http://{g.Host}", allowPrivateNetworks: true)`. If invalid or loopback (`127.0.0.1`, `localhost`), abort connection with logged warning.
     - Decrypt API token using `IDataProtector` with purpose `"BrewYou.BrewLinkCredentials"`.
     - Construct WebSocket URL: `ws://{cleanHost}/ws?token={Uri.EscapeDataString(token)}`.
     - Configure `ClientWebSocket`:
       ```csharp
       var ws = new ClientWebSocket();
       ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15); // RFC 6455 Ping/Pong
       ws.Options.SetRequestHeader("Authorization", $"Bearer {token}");
       ws.Options.SetRequestHeader("X-BrewLink-Token", token);
       ```
     - Connect via `ws.ConnectAsync(uri, cts.Token)` with a 5-second connect timeout.
4. *Heartbeat & Liveness*:
   - **Protocol Heartbeat**: WebSocket ping/pong handled automatically every 15s via `ClientWebSocketOptions.KeepAliveInterval`.
   - **Application Heartbeat**: Gateway emits frames at 1Hz. Maintain `LastFrameReceivedAt`. If no frame is received for > 30 seconds (indicating silent Wi-Fi drop on ESP32 without TCP FIN), abort the socket and trigger exponential backoff reconnect.
5. *Auto-Reconnect with Exponential Backoff & Jitter*:
   - Maintain `consecutiveFailures` per gateway.
   - Delay formula:
     $$\text{delay} = \min(2^{\text{consecutiveFailures}} \times 1\text{s}, 60\text{s}) + \text{jitter}(0..1000\text{ms})$$
     (e.g., 2s, 4s, 8s, 16s, 32s, 60s).
   - On receiving first valid frame: reset `consecutiveFailures = 0`.
6. *Frame Handling Loop*:
   - Buffer: 4,096 bytes rented from `ArrayPool<byte>.Shared`.
   - Accumulate frames until `result.EndOfMessage == true`. Enforce maximum frame size limit of 64KB.
   - On `result.MessageType == WebSocketMessageType.Text`:
     - Parse JSON with `JsonDocument.Parse`.
     - Inspect `event` property:
       - `event == "telemetry"`:
         - Extract `mac` (e.g. `"C44F331A8B22"`), `equipment_type`, `model`, `rssi`.
         - Map to `Equipment` via equipment gateway binding lookup.
         - Dispatch to `ITelemetryService.IngestBrewLinkTelemetryAsync(...)`.
       - `event == "equipment_list"`:
         - Update gateway equipment cache (`slot` $\leftrightarrow$ `mac` $\leftrightarrow$ `name`).
       - `event == "cmd_ack"`:
         - Dispatched to command acknowledgment tracker (for M2 async command completion).
       - `event == "connected"` / `"disconnected"`:
         - Log BLE peripheral lifecycle event; update equipment status if appropriate.
   - On `result.MessageType == WebSocketMessageType.Close` or `WebSocketException`:
     - Gracefully break receive loop, dispose socket, and transition to reconnect backoff.

### 2.2. Ingestion Pipeline 2: MQTT Broker Topic Subscriber
1. *Premise*: Cloud-hosted deployments or users utilizing central MQTT brokers stream telemetry to `brewlink/{topicKey}/{gatewayId}/equipment/{mac}/telemetry`.
2. *Subscription Expansion in `MqttService.RefreshSubscriptionsAsync`*:
   - Inject `IDataProtectionProvider` to obtain `"BrewYou.BrewLinkCredentials"` protector.
   - In `RefreshSubscriptionsAsync`:
     - Query `db.BrewLinkGateways.Where(g => !string.IsNullOrEmpty(g.MqttTopicKey))`.
     - For each gateway, unprotect `MqttTopicKey`.
     - Construct wildcard subscription topic:
       $$\text{Topic} = \texttt{brewlink/} + \texttt{<topicKey>/} + \texttt{<gatewayId>/equipment/+/telemetry}$$
     - Subscribe via `_mqttClient.SubscribeAsync(topic, cancellationToken)`.
3. *Message Routing in `HandleApplicationMessageReceivedAsync`*:
   - Parse incoming topic by splitting on `/`:
     ```csharp
     var segments = topic.Split('/');
     // brewlink / {topicKey} / {gatewayId} / equipment / {mac} / telemetry
     if (segments.Length == 6 &&
         string.Equals(segments[0], "brewlink", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(segments[3], "equipment", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(segments[5], "telemetry", StringComparison.OrdinalIgnoreCase))
     {
         var topicKey = segments[1];
         var gatewayId = segments[2];
         var mac = segments[4];
         // Process BrewLink telemetry
     }
     ```
   - Query DB or cache for `BrewLinkGateway` matching `gatewayId` (or `topicKey`).
   - Query DB or cache for `Equipment` where `ConnectionType == EquipmentConnectionType.BrewLink`, `brewLinkGatewayId == gateway.Id`, and `NormalizeMac(targetMac) == NormalizeMac(mac)`.
   - Dispatch to `telemetryService.IngestBrewLinkTelemetryAsync(equipment.Id, payloadElement, source: "BrewLinkMqtt")`.

### 2.3. Ingestion Pipeline 3: HTTP REST Polling Fallback (`GET /api/equipment`)
1. *Premise*: When WebSocket streaming is not available (e.g. backend cannot reach local subnet, gateway firewall blocks WS, or WS connection is dropped), or when user triggers on-demand sync (`POST /api/v1/inventory/equipment/{id}/brewlink/sync`), the system polls `GET /api/equipment`.
2. *Avoid Redundant Polling*:
   - If a gateway has an active WebSocket streaming connection receiving frames, HTTP polling fallback is **dormant** for that gateway. ESP32 microcontroller resources are constrained; redundant polling causes memory contention and BLE latency.
   - If a gateway's WebSocket is disconnected, failed, or disabled, fallback polling activates at a 30-second interval.
3. *Polling Execution*:
   - Validate target URL `http://{gateway.Host}/api/equipment` via `NetworkSecurityValidator.ValidateUrlAsync` (`allowPrivateNetworks: true`).
   - Create HTTP request with `Authorization: Bearer <api_token>` and `X-BrewLink-Token: <api_token>`.
   - Use `HttpClient` configured with `SocketsHttpHandler` (`AllowAutoRedirect = false`, 5-second timeout).
   - Parse response JSON array `equipment`:
     - For each peripheral item, match corresponding `Equipment` entity by `brewLinkGatewayId` + (`mac`, `slot`, or `name`).
     - Pass peripheral state object to `telemetryService.IngestBrewLinkTelemetryAsync(equipment.Id, itemElement, source: "BrewLinkPoll")`.

### 2.4. Equipment Gateway Binding Lookup (`ConnectionConfigJson`)
1. *Config Schema*:
   Equipment records with `ConnectionType == EquipmentConnectionType.BrewLink` contain:
   ```json
   {
     "brewLinkGatewayId": "9f7b4943-3e11-4712-b91c-197e41e3df6a",
     "targetMac": "C4:4F:33:1A:8B:22",
     "targetName": "Grainfather G30 Connect",
     "slot": 1
   }
   ```
2. *Matching Algorithm*:
   Given incoming telemetry with `gatewayDbId` (or `gatewayHardwareId`), `mac`, optional `slot`, and optional `name`:
   - Step 1: Filter equipment candidates where `ConnectionType == EquipmentConnectionType.BrewLink` and `brewLinkGatewayId == gatewayDbId`.
   - Step 2: Match by MAC (Highest Precedence):
     `NormalizeMac(targetMac) == NormalizeMac(mac)`.
   - Step 3: Match by Slot (Secondary Precedence):
     If `targetMac` is omitted/empty in config and `slot.HasValue`, match `config.slot == incomingSlot`.
   - Step 4: Match by Name (Fallback Precedence):
     If `targetMac` and `slot` are omitted, match `config.targetName.Equals(incomingName, OrdinalIgnoreCase)`.
3. *Performance Optimization*:
   Maintain an in-memory dictionary `ConcurrentDictionary<(Guid GatewayId, string CleanMac), Guid EquipmentId>` updated on equipment change/reconciliation, avoiding repetitive JSON parsing per frame at 1Hz.

### 2.5. Background Service Lifecycle & DI Architecture
1. *Singleton vs Scoped Clean Separation*:
   - Background workers (`BrewLinkWebSocketWorker`, `MqttWorker`, `EquipmentPollingWorker`) and Managers (`BrewLinkWebSocketManager`, `MqttService`) are **Singletons**.
   - Entity Framework context (`BrewYouDbContext`) and `ITelemetryService` are **Scoped**.
   - When a frame or message arrives, the worker/manager invokes `_serviceProvider.CreateScope()` to resolve `BrewYouDbContext` and `ITelemetryService`.
2. *DI Registration Plan* in `src/BrewYou.ApiService/Program.cs`:
   ```csharp
   // BrewLink WebSocket Streaming
   builder.Services.AddSingleton<IBrewLinkWebSocketManager, BrewLinkWebSocketManager>();
   builder.Services.AddHostedService<BrewLinkWebSocketWorker>();

   // MQTT Service & Worker (existing, extended)
   builder.Services.AddSingleton<IMqttService, MqttService>();
   builder.Services.AddHostedService<MqttWorker>();

   // REST Polling Worker (existing, extended with BrewLink fallback)
   builder.Services.AddHostedService<EquipmentPollingWorker>();
   ```

### 2.6. Error Recovery Matrix
| Failure Scenario | Detection Mechanism | Immediate Action | Recovery Strategy |
| :--- | :--- | :--- | :--- |
| **Gateway Reboot / Power Off** | `ClientWebSocket` throws `WebSocketException` or EOF | Catch exception, close and dispose socket | Exponential backoff reconnect (2s $\rightarrow$ 60s); fallback HTTP polling activates if configured |
| **Silent Wi-Fi Drop / ESP32 Freeze** | No frame received for > 30s; or ping/pong timeout (15s) | Cancel socket CTS, abort socket | Treat as disconnected; initiate backoff reconnection |
| **MQTT Broker Disconnect** | `OnClientDisconnectedAsync` callback | Clear subscribed topics set | `MqttWorker` checks `IsConnected` every 30s and calls `EnsureConnectedAsync` |
| **HTTP Polling Network Timeout** | `TaskCanceledException` (5s limit) | Request aborted | Log warning; retry on next 30s tick; no process crash |
| **Malformed JSON Frame** | `JsonException` in frame parse | Frame dropped | Log truncated snippet at Warning level; keep socket open |
| **Unknown Device MAC** | Binding lookup returns null | Telemetry ignored | Log Debug level; no DB or SSE action; socket remains healthy |
| **Graceful App Shutdown** | `stoppingToken` signaled | Send `CloseAsync` (NormalClosure) | 2s timeout for graceful close; dispose all sockets cleanly |

---

## 3. Caveats

1. **Network Reachability in Containerized / Cloud Deployments**:
   - In cloud-hosted containers, backend WebSocket connections to local LAN IPs (e.g. `192.168.1.50`) will fail.
   - *Design Provision*: The multi-transport architecture handles this cleanly: in cloud deployments, MQTT broker topics (`brewlink/{topicKey}/{gatewayId}/...`) and on-demand client-triggered HTTP sync (`POST .../brewlink/sync`) serve as the active telemetry channels.
2. **SSRF Validator Scheme Restriction**:
   - `NetworkSecurityValidator.ValidateUrlAsync` strictly requires `http` or `https` schemes.
   - *Design Provision*: To maintain strict security without modifying `NetworkSecurityValidator`, validate the gateway host using `http://{gateway.Host}` before initiating the `ClientWebSocket` connection to `ws://{gateway.Host}/ws?token={token}`. This ensures identical DNS, IP range, and loopback security checks.
3. **Microcontroller Concurrency Contention**:
   - ESP32 hardware cannot handle excessive concurrent HTTP and WebSocket connections.
   - *Design Provision*: When a WebSocket connection is active for a gateway, HTTP polling must remain suppressed.

---

## 4. Conclusion

1. **Architecture Model**:
   - Ingestion across all three transports (WebSocket, MQTT, REST polling) converges into a unified service method:
     `ITelemetryService.IngestBrewLinkTelemetryAsync(Guid equipmentId, JsonElement payload, string source, CancellationToken ct)`.
   - Normalization is handled by `BrewLinkTelemetryParser` (Priority 5), producing canonical `LatestMetricsJson` with camelCase keys (`currentTempC`, `targetTempC`, `heatActive`, `pumpActive`, `timer`, `stage`, `interaction`).
   - Every incoming frame immediately broadcasts an SSE `reading` event via `ITelemetryBroadcastService`.
   - Database writes to `EquipmentReading` are throttled to 30-second intervals or significant state deltas ($\ge 0.5^\circ\text{C}$, pump, heat, alert, step, timer state changes).
2. **WebSocket Pipeline Component**:
   - Implement `IBrewLinkWebSocketManager` / `BrewLinkWebSocketManager` as a Singleton managing per-gateway `ClientWebSocket` connections with exponential backoff (2s–60s) and 15s keepalive.
   - Implement `BrewLinkWebSocketWorker : BackgroundService` to drive gateway connection reconciliation every 30s.
3. **MQTT Pipeline Component**:
   - Extend `MqttService.RefreshSubscriptionsAsync` to subscribe to `brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry` for all paired `BrewLinkGateway` entities.
   - Extend `HandleApplicationMessageReceivedAsync` to route matching messages to `IngestBrewLinkTelemetryAsync`.
4. **REST Polling Fallback Component**:
   - Add fallback polling in `EquipmentPollingWorker` (or within `BrewLinkWebSocketManager`) for gateways where WebSocket is disconnected.
   - Support on-demand sync via `POST /api/v1/inventory/equipment/{id}/brewlink/sync`.
5. **Gateway Binding Component**:
   - Implement `BrewLinkEquipmentBindingResolver` matching equipment via `brewLinkGatewayId` + `NormalizeMac(targetMac)` > `slot` > `targetName`.

---

## 5. Verification Method

### 5.1. Independent Verification Commands
Run outside the sandbox with `BypassSandbox: true`:

1. **Verify Existing MQTT and BrewLink Test Suites**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~MqttTests|FullyQualifiedName~BrewLinkEndpointsTests"
   ```
2. **Verify Telemetry Parser Tests**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~TelemetryParserTests"
   ```
3. **Verify Full Backend Build**:
   ```powershell
   dotnet build src/BrewYou.ApiService/BrewYou.ApiService.csproj
   ```

### 5.2. Files to Inspect
- `src/BrewYou.ApiService/Services/MqttService.cs`
- `src/BrewYou.ApiService/Services/EquipmentPollingWorker.cs`
- `src/BrewYou.ApiService/Services/BrewLinkService.cs`
- `src/BrewYou.ApiService/Services/TelemetryService.cs`
- `src/BrewYou.ApiService/Data/Entities/BrewLinkGateway.cs`
- `src/BrewYou.ApiService/Data/Entities/Equipment.cs`
- `src/BrewYou.ApiService/Validation/Validators.cs`

### 5.3. Invalidation Conditions
- Any WebSocket client connecting to loopback (`127.0.0.1`, `localhost`, `::1`) violating SSRF protection.
- HTTP polling running concurrently against an active, healthy WebSocket streaming connection (causing gateway overload).
- Inability to unprotect `BrewLinkGateway.MqttTopicKey` in `MqttService` due to missing `IDataProtector` configuration.
- Failure to match equipment when `targetMac` contains formatting delimiters (e.g. colons or dashes) vs. clean 12-char hex.
