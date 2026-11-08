# BrewLink Real-Time Telemetry & Hardware Control: E2E Test Infrastructure

## 1. Overview & Architecture
The BrewLink Real-Time Telemetry and Hardware Control test infrastructure validates end-to-end integration across Requirements R1 through R5 as defined in `ORIGINAL_REQUEST.md` and `PROJECT.md`.

The test suite tests the system as an opaque box through HTTP and Server-Sent Events (SSE) interfaces, with simulated BrewLink IoT gateways and multi-role brewery collaboration.

```
                           +----------------------------------------+
                           |           Test Suite Client            |
                           |   (Owner, Brewer, Viewer, Anonymous)   |
                           +-------------------+--------------------+
                                               |
                                     HTTP API & SSE Stream
                                               |
                                               v
+-----------------------------------------------------------------------------------+
|                        WebApplicationFactory<Program>                             |
|                                                                                   |
|  [EquipmentEndpoints]        [TelemetryEndpoints]       [BrewLinkEndpoints]       |
|            |                          |                          |                |
|            v                          v                          |                |
|    [BrewLinkService]       [TelemetryBroadcastService]           |                |
|            |                          |                          |                |
|            +--------------------------+--------------------------+                |
|                                       |                                           |
|                                       v                                           |
|                             [BrewYouDbContext]                                    |
|                         (In-Memory Test Database)                                 |
+-----------------------------------------------------------------------------------+
                                        |
                          Outbound HTTP / WebSockets
                                        |
                                        v
                       +----------------------------------+
                       |  FakeBrewLinkHttpMessageHandler  |
                       |    (Simulated ESP32 Gateway)     |
                       +----------------------------------+
```

---

## 2. 4-Tier Test Architecture

### Tier 1: Feature Coverage (>=5 test cases per feature)
Verifies nominal happy-path functionality across all primary features:
1. **Telemetry Parsing & Ingestion**:
   - WebSocket frame ingestion into `LatestMetricsJson`
   - REST polling state parsing into `LatestMetricsJson`
   - Dynamic temperature update (`CurrentTemperatureC`)
   - Complete state mapping (heating, pump, timer, stage, interaction)
   - Throttled persistence of `EquipmentReading` records
2. **SSE Real-Time Streaming**:
   - SSE connection headers (`text/event-stream`, `no-cache`, `no-transform`)
   - Initial connection event (`: connected`)
   - Immediate `reading` event emission on telemetry frame receipt
   - Immediate `reading` event emission on control action execution
   - Immediate `reading` event emission on on-demand sync
3. **Hardware Control Actions (12 Actions)**:
   - Pump toggle (`set_pump`) with `{ state: true|false }`
   - Heater power & state (`set_heat`) with `{ state: bool, power_pct: int }`
   - Target temperature setpoint (`set_target_temp`) with `{ temp_c: decimal }`
   - Hardware countdown timer (`set_timer`) with `{ duration_min: int }`
   - Timer state controls (`pause_timer`, `resume_timer`, `cancel_timer`)
   - Interactive prompt acknowledgment (`press_set`, `ack_interaction`, `dismiss_alert`, `dismiss_sparge_alert`, `cancel_session`)
4. **Host Validation & Outbound Security**:
   - Valid private LAN IP allowed (`192.168.1.50`)
   - Valid public host allowed
   - Loopback IP blocked (`127.0.0.1`)
   - Localhost hostname blocked (`localhost`)
   - Cloud metadata IP blocked (`169.254.169.254`)
   - Unspecified address blocked (`0.0.0.0`)
5. **Role-Based Authorization**:
   - Brewery Owner allowed to execute control commands
   - Brewer allowed to execute control commands
   - Viewer rejected with 403 Forbidden on control commands
   - Viewer permitted to read equipment state & telemetry stream
   - Unauthenticated client rejected with 401 Unauthorized
6. **State Synchronization**:
   - Successful state pull updates `Equipment` record
   - Disconnected/unreachable gateway returns 502 Bad Gateway
   - Live temperature extracted and updated in DB
   - `LatestMetricsJson` updated with fresh gateway snapshot
   - Unauthenticated sync rejected with 401 Unauthorized
7. **Equipment Model & Connection Type Expansion**:
   - Boiler with `Subtype = AllInOne` allows `ConnectionType = BrewLink`
   - Boiler with `Subtype = BoilKettle` allows `ConnectionType = BrewLink`
   - Fermenter with `Subtype = Conical` allows `ConnectionType = BrewLink`
   - Fermenter with `Subtype = Unitank` allows `ConnectionType = BrewLink`
   - Non-AllInOne vessel rejects recipe upload while allowing control

### Tier 2: Boundary & Corner Cases (>=5 test cases per feature)
Stresses edge conditions, anomalous data, and error boundaries:
1. **Malformed Frames & Payloads**:
   - Empty JSON string
   - Missing required payload root
   - Non-numeric string in temperature field
   - Truncated / invalid JSON syntax
   - Out-of-spec data types (array instead of object)
2. **Extreme & Boundary Temperatures**:
   - Sub-zero freezing temperature (`-10.0°C`)
   - Freezing point boundary (`0.0°C`)
   - Boiling temperature (`100.0°C`)
   - Extreme heating element temperature (`120.0°C`)
   - High decimal precision (`65.4321°C`)
3. **Timer Boundaries**:
   - Zero duration timer (`0 min`)
   - 1-minute minimum timer
   - Maximum 24-hour timer (`1440 min`)
   - Negative duration rejection (`-5 min`)
   - Out-of-range timer duration (>24 hours)
4. **Invalid Signatures & Replay Timestamps**:
   - Stale timestamp (>300 seconds elapsed)
   - Future timestamp (>300 seconds ahead)
   - Corrupted HMAC signature string
   - Wrong gateway API secret token
   - Blank / omitted HMAC signature
5. **Offline Gateway & Network Failures**:
   - Gateway connection refused / timeout
   - Gateway 502 Bad Gateway response mapping
   - Gateway 409 Equipment Disconnected mapping
   - Gateway 401 Token Revoked mapping
   - Connection reset during transfer
6. **Loopback SSRF & Encoding Evasion**:
   - IPv6 loopback (`[::1]`)
   - Decimal IP representation (`http://2130706433/`)
   - Octal IP representation (`http://0177.0.0.1/`)
   - Hexadecimal IP representation (`http://0x7f000001/`)
   - Link-local address (`169.254.0.1`)

### Tier 3: Cross-Feature Combinations
Tests emergent behavior and interactions between subsystems:
1. **Control Execution + Immediate SSE Broadcast**:
   - Dispatch `set_pump(state=true)` -> verify equipment state updated AND `reading` event emitted to SSE subscriber within 500ms.
2. **Sync + State Update**:
   - Issue `POST /sync` -> verify gateway returns updated temperature -> DB record updated -> SSE broadcast emitted.
3. **Multi-Vessel Telemetry Isolation**:
   - Two distinct vessels (Boiler A and Fermenter B) on different gateways receiving simultaneous telemetry updates without cross-contamination.
4. **Role Boundary Transitions**:
   - User demoted from Brewer to Viewer mid-session -> subsequent control requests immediately return 403 Forbidden.

### Tier 4: Real-World Application Scenarios
End-to-end user journeys replicating operational brew day workflows:
1. **Full Brew Day Telemetry Monitoring**:
   - Setup mash equipment -> heat to strike temperature -> hold mash rest -> boil phase -> record telemetry progression.
2. **Step Prompt Acknowledgment**:
   - Strike temperature reached -> gateway emits "Add Grain" interaction prompt -> brewer acknowledges via `ack_interaction` -> timer starts.
3. **Emergency Boil-Over Cancel Session**:
   - Rapid boil alert -> brewer issues `cancel_session` -> heater shut off, pump switched off, session aborted safely.
4. **Hardware Timer Progression & Control**:
   - 60-minute boil timer started -> paused for addition -> resumed -> remaining seconds tick down -> timer completes.

---

## 3. Test Fixture & Harness Conventions

### `BrewLinkE2ETestFactory`
Derived from `WebApplicationFactory<Program>`, provides:
- Test-isolated `FakeBrewLinkHttpMessageHandler` for gateway mocking.
- Registered mock HTTP client handler for outbound gateway communications.
- Clean database state per test method or class.

### Multi-Role Brewery Setup Helpers
- `CreateUserWithRoleAsync(role: BreweryRole)`: Creates user, sets up brewery workspace, assigns collaboration role (Owner, Brewer, Viewer).
- `CreateBrewLinkEquipmentAsync(client, setupId, gatewayId, type, subtype, mac, slot)`: Provisions equipment registered to gateway.
- `SubscribeSseEventsAsync(client, timeout)`: Connects to `/api/v1/inventory/equipment/telemetry-stream` and reads events.

---

## 4. Test Runner & Execution Guide

### Command Line
```powershell
# Run the complete BrewLink E2E test suite
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkE2ETests

# Run a specific test tier
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkE2ETests.Tier1
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkE2ETests.Tier2
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkE2ETests.Tier3
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkE2ETests.Tier4
```
