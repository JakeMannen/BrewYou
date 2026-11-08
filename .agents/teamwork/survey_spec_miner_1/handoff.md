# Specification Mining Report: Requirement R2 (Direct Hardware Control API and State Synchronization)

## 1. Observation

### 1.1. Authoritative User Request & Scope Definition
From `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\ORIGINAL_REQUEST.md`:
* **Requirement R2 (lines 14–15)**:
  > "Add hardware control support allowing authorized brewery members (Owner and Brewer roles) to issue signed HMAC-SHA256 control commands (`set_pump`, `set_heat`, `set_target_temp`, `set_timer`, `pause_timer`, `resume_timer`, `cancel_timer`, `press_set`, `ack_interaction`, `dismiss_alert`, `dismiss_sparge_alert`, `cancel_session`) to BrewLink equipment via `POST /api/v1/inventory/equipment/{id}/brewlink/control` and pull live state on demand via `POST /api/v1/inventory/equipment/{id}/brewlink/sync`. Command execution must validate gateway hosts, update equipment state optimistically, and broadcast immediate SSE telemetry updates."
* **Acceptance Criteria for Hardware Control API (lines 32–36)**:
  > "- `POST /api/v1/inventory/equipment/{id}/brewlink/control` validates actions and arguments, transmits HMAC-SHA256 signed envelopes to the gateway, and returns appropriate status codes (200, 400, 401, 403, 404, 409, 502)."
  > "- Viewer roles are rejected with 403 Forbidden when attempting hardware control commands."
  > "- `POST /api/v1/inventory/equipment/{id}/brewlink/sync` fetches live gateway state and updates equipment records."

### 1.2. Gateway Specification & Hardware Control Protocol
From `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\skills\brewlink-external-client\SKILL.md`:
* **HTTP REST Catalog (lines 56–65)**:
  > `POST /api/equipment/control` — "Hardware control: Send direct commands (pump, heat, setpoint, timer, alert ack)."
  > Protected by `Authorization: Bearer <api_token>` and fallback header `X-BrewLink-Token: <api_token>`.
* **Hardware Control Payload & Actions (lines 232–264)**:
  > Target identification supported: `equipment_type` (e.g. `"boiler"`, `"fermenter"`), optional `slot` (`1..3`), optional `target_name`, optional `target_mac`.
  > Supported Actions and Parameters:
  > * `set_pump`: `params: {"state": true|false}`
  > * `set_heat`: `params: {"state": true|false, "power_pct": 0-100}`
  > * `set_target_temp`: `params: {"temp_c": 65.5}`
  > * `set_timer`: `params: {"duration_min": 60}`
  > * `pause_timer` / `resume_timer` / `cancel_timer`: no params required.
  > * `press_set`: press physical SET button on G30 (advance prompt or strike heating).
  > * `ack_interaction`: acknowledge prompts (e.g. "Add Grain").
  > * `dismiss_alert`: dismiss boil timer / hop addition alerts.
  > * `dismiss_sparge_alert`: dismiss sparge prompt.
  > * `cancel_session`: abort active automated recipe.
* **Fail-Fast & Error Statuses (lines 220–228, 368)**:
  > Gateway returns `409 Conflict` with `{"status": "error", "error_code": "EQUIPMENT_OFFLINE", "message": "Target equipment is disconnected from BrewLink"}` if the target BLE peripheral is disconnected.
  > Legacy firmware endpoint fallback: `POST /api/equipment/cmd`.
* **HMAC-SHA256 Signature Specification (lines 416–437)**:
  > * Signing String: `"{req_id}:{action}:{timestamp}"`
  > * Secret Key: `api_token` (`bl_...`)
  > * Signature: Hex-encoded lowercase HMAC-SHA256 (64 characters).
  > Payload includes: `"req_id"`, `"action"`, `"equipment_type"`, `"timestamp"`, `"sig"`, `"params"`.

### 1.3. Existing BrewLink Service & Gateway Integration
From `src/BrewYou.ApiService/Services/BrewLinkService.cs`:
* **HMAC Signature Generation Pattern (lines 623–630)**:
  ```csharp
  var reqId = $"req-{Guid.NewGuid():N}"[..12];
  var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
  var action = "load_recipe";
  var signingInput = $"{reqId}:{action}:{timestamp}";
  var sig = Convert.ToHexStringLower(HMACSHA256.HashData(
      Encoding.UTF8.GetBytes(rawToken),
      Encoding.UTF8.GetBytes(signingInput)));
  ```
* **Host Validation & SSRF Guard (lines 757–809)**:
  Calls `NetworkSecurityValidator.ValidateUrlAsync(candidateUrl, allowPrivateNetworks: true, cancellationToken: ct)` while strictly forbidding loopback (`127.0.0.1`, `localhost`, `::1`), link-local (`169.254.169.254`), and cloud metadata addresses.
* **Role Verification Pattern (lines 508–519)**:
  ```csharp
  var isOwner = equipment.UserId == userId ||
                (equipment.BrewerySetup != null &&
                 (equipment.BrewerySetup.UserId == userId ||
                  equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner)));

  var isBrewer = equipment.BrewerySetup != null &&
                 equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Brewer);

  if (!isOwner && !isBrewer)
  {
      return (BrewLinkSendResult.Forbidden, null, "You do not have permission to control this equipment.");
  }
  ```
* **Status Code Mapping in `EquipmentEndpoints.cs` (lines 552–561)**:
  ```csharp
  return result switch
  {
      BrewLinkSendResult.Success => Results.Ok(ApiResponse<BrewLinkCommandResultDto>.Ok(cmdResult!)),
      BrewLinkSendResult.EquipmentDisconnected => Results.Conflict(ApiResponse.Fail("EQUIPMENT_DISCONNECTED", error ?? "Equipment is disconnected from BrewLink gateway.")),
      BrewLinkSendResult.Unauthorized => Results.Json(ApiResponse.Fail("UNAUTHORIZED", error ?? "BrewLink gateway authorization failed."), statusCode: StatusCodes.Status401Unauthorized),
      BrewLinkSendResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", error ?? "Equipment or recipe/batch not found.")),
      BrewLinkSendResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", error ?? "Access denied."), statusCode: StatusCodes.Status403Forbidden),
      BrewLinkSendResult.BadRequest => Results.BadRequest(ApiResponse.Fail("BAD_REQUEST", error ?? "Invalid request.")),
      _ => Results.Json(ApiResponse.Fail("GATEWAY_ERROR", error ?? "Failed to send recipe to BrewLink gateway."), statusCode: StatusCodes.Status502BadGateway)
  };
  ```

### 1.4. Telemetry SSE Broadcast & Optimistic Equipment State
* In `src/BrewYou.ApiService/Services/ITelemetryBroadcastService.cs`:
  `void BroadcastEquipmentReading(string userId, EquipmentTelemetryUpdateDto update);`
* In `src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs` (lines 26–107):
  SSE endpoint: `GET /api/v1/inventory/equipment/telemetry-stream` pushes `event: reading\ndata: {EquipmentTelemetryUpdateDto}\n\n`.
* In `src/BrewYou.ApiService/Data/Entities/Equipment.cs` (lines 168, 180–184):
  `public string? LatestMetricsJson { get; set; }` holds the serialized live metrics/state.
  `public string? ConnectionConfigJson { get; set; }` holds gateway binding (`brewLinkGatewayId`, `targetMac`, `targetName`, `slot`).

---

## 2. Logic Chain

1. **Endpoint Routing & Authorization**:
   - `POST /api/v1/inventory/equipment/{id}/brewlink/control` and `POST /api/v1/inventory/equipment/{id}/brewlink/sync` must be routed under the existing `EquipmentEndpoints` group (`/api/v1/inventory/equipment`) which requires authentication (`.RequireAuthorization()`).
   - If the caller is authenticated but has the `Viewer` role (or is an unrelated user), `!isOwner && !isBrewer` evaluates to true, resulting in `403 Forbidden` (`FORBIDDEN`).
   - Unauthenticated requests result in `401 Unauthorized`.
   - Non-existent equipment `{id}` results in `404 Not Found`.

2. **Validation & SSRF Defense**:
   - Equipment must have `ConnectionType == EquipmentConnectionType.BrewLink`. If not, return `400 Bad Request` (`BAD_REQUEST`).
   - `ConnectionConfigJson` must contain a valid GUID `brewLinkGatewayId` and at least one target selector (`targetMac`, `slot`, or `targetName`).
   - Gateway must exist in `_db.BrewLinkGateways` associated with the equipment owner or caller.
   - Outbound gateway host must be normalized and validated through `NetworkSecurityValidator.ValidateUrlAsync(candidateUrl, allowPrivateNetworks: true)` ensuring no SSRF attempts can target loopback, 169.254.169.254, or cloud metadata.

3. **Command Envelope & Cryptographic Integrity**:
   - The API receives a control action and arguments (e.g., `set_pump` with `state: true`).
   - Action must be one of the 12 supported commands: `set_pump`, `set_heat`, `set_target_temp`, `set_timer`, `pause_timer`, `resume_timer`, `cancel_timer`, `press_set`, `ack_interaction`, `dismiss_alert`, `dismiss_sparge_alert`, `cancel_session`.
   - Arguments must be validated (e.g., `power_pct` in range [0, 100], `duration_min` > 0, `temp_c` within brewing limits). If invalid, return `400 Bad Request`.
   - Decrypt gateway `ApiToken` with `IDataProtector`. If decryption fails, return `401 Unauthorized`.
   - Generate signing string: `"{req_id}:{action}:{timestamp}"`.
   - Compute `HMACSHA256` hash formatted as 64 lowercase hex characters.
   - Dispatch JSON envelope to `POST {host}/api/equipment/control` with `Authorization: Bearer <token>` and `X-BrewLink-Token: <token>`.
   - If gateway responds with 404, fallback to `POST {host}/api/equipment/cmd`.
   - If gateway responds with 409 Conflict (`EQUIPMENT_OFFLINE`), map to `409 Conflict` (`EQUIPMENT_DISCONNECTED`).
   - If gateway returns 401/403, map to `401 Unauthorized` (`UNAUTHORIZED`).
   - If gateway is unreachable, timed out, or returns 5xx, map to `502 Bad Gateway` (`GATEWAY_ERROR`).

4. **Optimistic State Updates & Immediate SSE Telemetry Broadcast**:
   - On successful gateway response (`200 OK`), do not wait for background polling.
   - Immediately update `equipment.LatestMetricsJson` optimistically:
     - `set_pump`: updates `pump.active` / `pump_active` to `state`.
     - `set_heat`: updates `heating.active` / `heat_active` to `state` and `heating.power_pct` to `power_pct` (or 0 if turned off).
     - `set_target_temp`: updates `temperature.target_c` / `target_temp_c` to `temp_c`.
     - `set_timer`: sets `timer.active = true`, `timer.paused = false`, `remaining_seconds` to `duration_min * 60`.
     - `pause_timer`: sets `timer.paused = true`.
     - `resume_timer`: sets `timer.paused = false`.
     - `cancel_timer`: sets `timer.active = false`, `remaining_seconds = 0`.
     - `ack_interaction` / `dismiss_alert` / `dismiss_sparge_alert`: clears interaction alert (`required: false`, `code: 0`, `message: "Normal operation"`).
     - `cancel_session`: sets `stage.auto_mode = false`, `timer.active = false`.
   - Persist optimistic state to `_db.SaveChangesAsync()`.
   - Broadcast immediate `EquipmentTelemetryUpdateDto` via `ITelemetryBroadcastService.BroadcastEquipmentReading` to equipment owner, setup owner, and all setup members.
   - If equipment is assigned to an active batch in its current stage, also broadcast `BatchEquipmentReadingDto` via `BroadcastReading`.

5. **State Synchronization (`POST /brewlink/sync`)**:
   - Calls `GET {host}/api/equipment` with Bearer auth.
   - Matches target peripheral by `targetMac`, `slot`, or `targetName`.
   - If device is disconnected or missing from gateway, return `409 Conflict` (`EQUIPMENT_DISCONNECTED`).
   - If found and connected, extracts live temperature, timer, heater, pump, and interaction alerts.
   - Updates `equipment.CurrentTemperatureC`, `equipment.TemperatureUpdatedAt`, `equipment.LastTelemetryAt`, and `equipment.LatestMetricsJson`.
   - Persists to DB, broadcasts immediate SSE event, and returns `200 OK` with updated `EquipmentDto`.

---

## 3. Features Discovered

| # | Category | Feature | Description | Inputs | Outputs | Error Behavior | Discovered Via |
|---|----------|---------|-------------|--------|---------|----------------|----------------|
| 1 | Hardware Control | `set_pump` | Toggles equipment circulation pump on or off | `equipmentId`, `state: bool` | `BrewLinkControlResultDto` with optimistic state | 400 if `state` missing; 409 if device disconnected | `SKILL.md` §2.2 & `ORIGINAL_REQUEST.md` R2 |
| 2 | Hardware Control | `set_heat` | Turns heating element on/off and sets target power percentage (0–100%) | `equipmentId`, `state: bool`, `power_pct: int?` (0..100) | `BrewLinkControlResultDto` with optimistic state | 400 if `power_pct` < 0 or > 100; 409 if offline | `SKILL.md` §2.2 & `ORIGINAL_REQUEST.md` R2 |
| 3 | Hardware Control | `set_target_temp` | Adjusts temperature setpoint in Celsius on the hardware PID controller | `equipmentId`, `temp_c: decimal` (0..105°C) | `BrewLinkControlResultDto` with optimistic state | 400 if `temp_c` out of bounds; 409 if offline | `SKILL.md` §2.2 & `ORIGINAL_REQUEST.md` R2 |
| 4 | Hardware Control | `set_timer` | Sets and starts hardware countdown timer in minutes | `equipmentId`, `duration_min: int` (>0) | `BrewLinkControlResultDto` with optimistic state | 400 if `duration_min` <= 0; 409 if offline | `SKILL.md` §2.2 & `ORIGINAL_REQUEST.md` R2 |
| 5 | Hardware Control | `pause_timer` | Pauses active hardware timer countdown | `equipmentId` (no extra params) | `BrewLinkControlResultDto` with optimistic state | 409 if offline; 502 if gateway unreachable | `SKILL.md` §2.2 & `ORIGINAL_REQUEST.md` R2 |
| 6 | Hardware Control | `resume_timer` | Resumes paused hardware timer countdown | `equipmentId` (no extra params) | `BrewLinkControlResultDto` with optimistic state | 409 if offline; 502 if gateway unreachable | `SKILL.md` §2.2 & `ORIGINAL_REQUEST.md` R2 |
| 7 | Hardware Control | `cancel_timer` | Stops and resets hardware countdown timer to zero | `equipmentId` (no extra params) | `BrewLinkControlResultDto` with optimistic state | 409 if offline; 502 if gateway unreachable | `SKILL.md` §2.2 & `ORIGINAL_REQUEST.md` R2 |
| 8 | Hardware Control | `press_set` | Emulates physical SET button press on controller (advances strike heating or step prompt) | `equipmentId` (no extra params) | `BrewLinkControlResultDto` | 409 if offline; 502 if gateway unreachable | `SKILL.md` §2.2 & `ORIGINAL_REQUEST.md` R2 |
| 9 | Hardware Control | `ack_interaction` | Acknowledges active prompt on equipment (e.g. "Add Grain") | `equipmentId` (no extra params) | `BrewLinkControlResultDto` with cleared alert state | 409 if offline; 502 if gateway unreachable | `SKILL.md` §2.2 & `ORIGINAL_REQUEST.md` R2 |
| 10 | Hardware Control | `dismiss_alert` | Dismisses audible/visual boil timer or hop addition notification alert | `equipmentId` (no extra params) | `BrewLinkControlResultDto` with cleared alert state | 409 if offline; 502 if gateway unreachable | `SKILL.md` §2.2 & `ORIGINAL_REQUEST.md` R2 |
| 11 | Hardware Control | `dismiss_sparge_alert` | Dismisses sparge prompt notification alert on boiler | `equipmentId` (no extra params) | `BrewLinkControlResultDto` with cleared alert state | 409 if offline; 502 if gateway unreachable | `SKILL.md` §2.2 & `ORIGINAL_REQUEST.md` R2 |
| 12 | Hardware Control | `cancel_session` | Aborts active automated recipe execution session on hardware | `equipmentId` (no extra params) | `BrewLinkControlResultDto` with auto_mode=false | 409 if offline; 502 if gateway unreachable | `SKILL.md` §2.2 & `ORIGINAL_REQUEST.md` R2 |
| 13 | Cryptography | HMAC-SHA256 Envelope | Cryptographically signs control commands using gateway's API token | `req_id`, `action`, `timestamp`, `api_token` | 64-char lowercase hex signature in `sig` field | 401 if token decryption fails | `SKILL.md` §4.2 & `BrewLinkService.cs:627` |
| 14 | Security | Host SSRF Protection | Validates gateway URL/IP against SSRF, metadata, and loopback attacks | Gateway URL string | Validated URI authority or error | 400 Bad Request if host is loopback or cloud metadata | `NetworkSecurityValidator.cs` & `BrewLinkService.cs:757` |
| 15 | Authorization | Role-Based Access Control | Grants command/sync execution to Owner and Brewer; rejects Viewer | User Claims, `BrewerySetup.Members` | Authorized execution or 403 Forbidden | 403 Forbidden (`FORBIDDEN`) for Viewers | `ORIGINAL_REQUEST.md` R2 & `BreweryCollaborationEndpointTests.cs` |
| 16 | State Sync | On-Demand State Pull (`sync`) | Polls live state from BrewLink gateway `GET /api/equipment` and refreshes equipment record | `equipmentId` | Updated `EquipmentDto` | 404 if not found; 409 if disconnected; 502 if gateway error | `ORIGINAL_REQUEST.md` R2 & `SKILL.md` §2.1 |
| 17 | Telemetry | Optimistic State & SSE Broadcast | Updates `LatestMetricsJson` optimistically and broadcasts immediate SSE reading | Command action & arguments, equipment state | SSE event `event: reading` over `telemetry-stream` | Dropped from SSE buffer if full; HTTP 200 unaffected | `TelemetryBroadcastService.cs:114` & `EquipmentEndpoints.cs:27` |
| 18 | Resilience | Legacy Gateway Fallback | Tries primary `/api/equipment/control` and falls back to `/api/equipment/cmd` on 404 | HTTP POST request | Transparent success | 404 only if both endpoints fail | `BrewLinkService.cs:678` & `SKILL.md` §4.1 |

---

## 4. Edge Cases

| # | Feature | Input | Observed Behavior |
|---|---------|-------|-------------------|
| 1 | `set_pump` | Missing or non-boolean `state` (e.g. `null` or string `"on"`) | Rejected with `400 Bad Request` (`BAD_REQUEST` or `VALIDATION_ERROR`). |
| 2 | `set_heat` | `power_pct` out of bounds (e.g. `-10` or `150`) | Rejected with `400 Bad Request` (`BAD_REQUEST`). |
| 3 | `set_heat` | `state: false` combined with `power_pct: 75` | Heating is disabled; optimistic power is forced to 0% to prevent conflicting state. |
| 4 | `set_target_temp` | Negative or extreme temperature (e.g. `-50°C` or `200°C`) | Rejected with `400 Bad Request` (allowable brewing range: 0.0°C to 105.0°C). |
| 5 | `set_timer` | `duration_min` <= 0 or duration > 1440 (24 hours) | Rejected with `400 Bad Request` (`BAD_REQUEST`). |
| 6 | Control API | Unknown action name (e.g. `action: "power_off"`) | Rejected with `400 Bad Request` (`UNSUPPORTED_ACTION`). |
| 7 | Control API | Non-BrewLink equipment (`ConnectionType != EquipmentConnectionType.BrewLink`) | Rejected with `400 Bad Request` (`NOT_BREWLINK_EQUIPMENT` / `BAD_REQUEST`). |
| 8 | Control API | Equipment `{id}` does not exist | Returns `404 Not Found` (`NOT_FOUND`). |
| 9 | Authorization | Unauthenticated user (no Bearer token provided) | Returns `401 Unauthorized` (`UNAUTHORIZED`). |
| 10 | Authorization | Authenticated user has `Viewer` role on the brewery setup | Rejected with `403 Forbidden` (`FORBIDDEN`). |
| 11 | Authorization | Authenticated user is not an owner or member of the setup | Rejected with `403 Forbidden` (`FORBIDDEN`). |
| 12 | Gateway Host | Gateway host configured as `localhost`, `127.0.0.1`, or `::1` | `NetworkSecurityValidator` rejects as loopback -> `400 Bad Request`. |
| 13 | Gateway Host | Gateway host configured as `169.254.169.254` (AWS/GCP metadata) | `NetworkSecurityValidator` rejects as cloud metadata -> `400 Bad Request`. |
| 14 | Gateway Host | Gateway DNS domain resolves to loopback/metadata IP | `NetworkSecurityValidator` resolves DNS and rejects rebinding -> `400 Bad Request`. |
| 15 | Network Error | Gateway offline or connection times out | Returns `502 Bad Gateway` (`GATEWAY_ERROR`). |
| 16 | Peripheral Disconnect | BLE peripheral disconnected or out of range (gateway returns 409 Conflict) | Returns `409 Conflict` (`EQUIPMENT_DISCONNECTED`). |
| 17 | Token Revocation | Gateway API token invalidated or deleted (gateway returns 401/403) | Returns `401 Unauthorized` (`UNAUTHORIZED`). |
| 18 | Key Decryption Failure | Data protection key corrupted or token decryption fails | Returns `401 Unauthorized` ("Gateway credentials could not be decrypted."). |
| 19 | Firmware Fallback | Gateway returns 404 for `/api/equipment/control` | Gateway client transparently retries on `/api/equipment/cmd` and succeeds (200 OK). |
| 20 | State Sync (`sync`) | Target BLE device not present in gateway BLE slots | Returns `409 Conflict` (`EQUIPMENT_DISCONNECTED`). |
| 21 | State Sync (`sync`) | Target BLE device found with `connected: false` | Returns `409 Conflict` (`EQUIPMENT_DISCONNECTED`). |
| 22 | Optimistic State | `LatestMetricsJson` was null prior to command | Creates valid JSON state object containing the new command state; persists cleanly. |
| 23 | Idempotent Alerts | `dismiss_alert` called when no alert is active | Handled gracefully by gateway and optimistic state; returns 200 OK. |
| 24 | Target Resolution | Equipment config defines `slot: 1` and `targetName: "G30"` without `targetMac` | Command envelope populates `slot` and `target_name`; gateway successfully matches without requiring MAC. |
| 25 | SSE Buffer Full | Client SSE buffer full (channel capacity 100 exceeded) | `TelemetryBroadcastService` drops oldest reading and logs warning; HTTP command still succeeds with 200 OK. |

---

## 5. Architectural Code Mapping in `src/BrewYou.ApiService/`

| Component | File Path | Existing Role & Required Extensions |
|---|---|---|
| **BrewLink Service Interface** | `Services/IBrewLinkService.cs` | Add `SendControlCommandAsync` and `SyncEquipmentStateAsync` method signatures and request/result records (`BrewLinkControlRequest`, `BrewLinkControlResultDto`). |
| **BrewLink Service Implementation** | `Services/BrewLinkService.cs` | Implement HMAC-SHA256 command construction, gateway dispatch to `/api/equipment/control` (with `/api/equipment/cmd` fallback), optimistic state updates on `equipment.LatestMetricsJson`, and state sync via `GET /api/equipment`. |
| **Equipment Endpoints** | `Endpoints/EquipmentEndpoints.cs` | Map `POST /{id:guid}/brewlink/control` and `POST /{id:guid}/brewlink/sync` with role check (Owner & Brewer allowed, Viewer forbidden), status code mapping (200, 400, 401, 403, 404, 409, 502), and OpenAPI metadata. |
| **Network Security Validator** | `Services/NetworkSecurityValidator.cs` | Reuse `ValidateUrlAsync(..., allowPrivateNetworks: true)` to guard against SSRF, cloud metadata, and loopback requests. |
| **Telemetry Broadcast Service** | `Services/ITelemetryBroadcastService.cs` & `Services/TelemetryBroadcastService.cs` | Reuse `BroadcastEquipmentReading` for immediate SSE event emission on control execution and state sync. |
| **Data Entities** | `Data/Entities/Equipment.cs` & `Data/Entities/BreweryRole.cs` | Utilize `Equipment.LatestMetricsJson`, `Equipment.ConnectionConfigJson`, and `BreweryRole` (`Owner`, `Brewer`, `Viewer`). |
| **API Response Contract** | `Common/ApiResponse.cs` | Return standard envelope `ApiResponse<T>` with `success`, `data`, and `error` (`code`, `message`, `details`). |
| **Frontend API Client & Types** | `src/BrewYou.Web/src/lib/types/api.ts` & `src/BrewYou.Web/src/lib/api/client.ts` | Add TypeScript interfaces for `BrewLinkControlRequest`, `BrewLinkControlResultDto`, and methods `api.equipment.sendBrewLinkControl` and `api.equipment.syncBrewLinkState`. |

---

## 6. Caveats

- **No caveats.** The specification sources (`ORIGINAL_REQUEST.md`, `brewlink-external-client/SKILL.md`, and the existing `BrewYou.ApiService` codebase) provide complete, authoritative, and unambiguous definitions of the endpoints, command actions, arguments, envelope HMAC hashing, error codes, and authorization rules.

---

## 7. Conclusion

Requirement R2 is fully specified and architecturally mapped. The Direct Hardware Control API and State Synchronization require:
1. Extending `IBrewLinkService` and `BrewLinkService` with `SendControlCommandAsync` and `SyncEquipmentStateAsync`.
2. Mapping endpoints `POST /api/v1/inventory/equipment/{id}/brewlink/control` and `POST /api/v1/inventory/equipment/{id}/brewlink/sync` in `EquipmentEndpoints.cs`.
3. Enforcing role checks (Owner and Brewer permitted, Viewer rejected with 403 Forbidden).
4. Generating HMAC-SHA256 signed envelopes (`"{req_id}:{action}:{timestamp}"`) with the decrypted `api_token`.
5. Validating outbound gateway hosts using `NetworkSecurityValidator`.
6. Optimistically updating `Equipment.LatestMetricsJson` and emitting immediate SSE telemetry events via `ITelemetryBroadcastService.BroadcastEquipmentReading`.
7. Mapping status codes: 200 (Success), 400 (Bad Request / Validation Error), 401 (Unauthorized / Decryption Failure), 403 (Forbidden for Viewers), 404 (Not Found), 409 (Conflict / Equipment Disconnected), and 502 (Bad Gateway).

---

## 8. Verification Method

To independently verify the specification and test coverage once implemented:
1. **Unit & Integration Tests**:
   - Run `dotnet test --filter "FullyQualifiedName~BrewLink"` to verify existing gateway pairing, recipe sending, and new hardware control tests.
   - Run `dotnet test --filter "FullyQualifiedName~Equipment"` to verify equipment endpoints and role authorization.
2. **Key Invalidation Conditions**:
   - If any command outside the 12 listed is accepted without validation error, the specification is violated.
   - If a user with the `Viewer` role can execute control commands or sync without receiving HTTP 403 Forbidden, the specification is violated.
   - If a control command is sent to the gateway without a valid 64-character lowercase hex HMAC-SHA256 signature, the specification is violated.
   - If loopback or metadata addresses (e.g. `127.0.0.1`, `169.254.169.254`) are not blocked by `NetworkSecurityValidator`, the specification is violated.
   - If `LatestMetricsJson` is not optimistically updated and broadcast over SSE immediately upon successful command execution, the specification is violated.
