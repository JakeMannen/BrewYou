# Milestone 2 Implementation Handoff Report: Hardware Control API, State Sync & Security

## 1. Observation

### 1.1. Authoritative Requirements & Direct Directives
- **From `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\ORIGINAL_REQUEST.md` (lines 14–15, 32–36)**:
  - R2: "Add hardware control support allowing authorized brewery members (Owner and Brewer roles) to issue signed HMAC-SHA256 control commands (`set_pump`, `set_heat`, `set_target_temp`, `set_timer`, `pause_timer`, `resume_timer`, `cancel_timer`, `press_set`, `ack_interaction`, `dismiss_alert`, `dismiss_sparge_alert`, `cancel_session`) to BrewLink equipment via `POST /api/v1/inventory/equipment/{id}/brewlink/control` and pull live state on demand via `POST /api/v1/inventory/equipment/{id}/brewlink/sync`."
  - Acceptance Criteria:
    - "`POST /api/v1/inventory/equipment/{id}/brewlink/control` validates actions and arguments, transmits HMAC-SHA256 signed envelopes to the gateway, and returns appropriate status codes (200, 400, 401, 403, 404, 409, 502)."
    - "Viewer roles are rejected with 403 Forbidden when attempting hardware control commands."
    - "`POST /api/v1/inventory/equipment/{id}/brewlink/sync` fetches live gateway state and updates equipment records."

### 1.2. Implemented Codebase Changes
1. **`src/BrewYou.ApiService/Services/IBrewLinkService.cs`**:
   - Added DTO definitions:
     ```csharp
     public record BrewLinkControlRequest(string Action, JsonElement? Params = null);

     public record BrewLinkSyncResultDto(
         string Status,
         bool Synced,
         decimal? TemperatureC,
         string? MetricsJson,
         Guid? EquipmentId = null,
         EquipmentDto? Equipment = null
     );
     ```
   - Extended `BrewLinkCommandResultDto` with optional `string? LatestMetricsJson = null` for backwards compatibility.
   - Added methods `SendControlCommandAsync` and `SyncEquipmentStateAsync` to `IBrewLinkService`.

2. **`src/BrewYou.ApiService/Services/BrewLinkService.cs`**:
   - Injected `ITelemetryBroadcastService? broadcastService = null` in constructor.
   - Defined `SupportedActions` supporting all 12 commands: `set_pump`, `set_heat`, `set_target_temp`, `set_timer`, `pause_timer`, `resume_timer`, `cancel_timer`, `press_set`, `ack_interaction`, `dismiss_alert`, `dismiss_sparge_alert`, `cancel_session`.
   - Implemented parameter validation and normalization (`ValidateAndBuildParams`) with boundary checks (power 0..100%, temp 0..105°C, duration 0..1440 min).
   - Implemented role-based authorization: Owner and Brewer permitted; Viewer and unassociated users rejected with `BrewLinkSendResult.Forbidden`.
   - Enforced host security validation via `NetworkSecurityValidator.ValidateUrlAsync` prohibiting loopback (`127.0.0.1`, `localhost`, `::1`, `[::1]`), unspecified zero addresses (`0.0.0.0`), and cloud metadata (`169.254.169.254`).
   - Generated genuine HMAC-SHA256 signed envelope (`Convert.ToHexStringLower(HMACSHA256.HashData(...))`) over `"{req_id}:{action}:{timestamp}"` using the decrypted gateway API token.
   - Implemented dual headers: `Authorization: Bearer <token>` and `X-BrewLink-Token: <token>`.
   - Transmitted HTTP POST to primary `/api/equipment/control` with automatic fallback to `/api/equipment/cmd` on 404.
   - Applied optimistic state mutation (`MutateCanonicalMetrics`) on `Equipment.LatestMetricsJson` and persisted to DB.
   - Pushed immediate SSE reading via `ITelemetryBroadcastService.BroadcastEquipmentReading` to equipment owner and all setup members (`BroadcastEquipmentState`).
   - Implemented `SyncEquipmentStateAsync`: calls `GET /api/equipment`, verifies connected status, parses canonical metrics via `BrewLinkTelemetryParser.ParseCanonical`, updates `Equipment.CurrentTemperatureC` and `Equipment.LatestMetricsJson`, and broadcasts live SSE.

3. **`src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs`**:
   - Mapped `POST /api/v1/inventory/equipment/{id:guid}/brewlink/control` and `POST /api/v1/inventory/equipment/{id:guid}/brewlink/sync`.
   - Mapped result codes to HTTP responses:
     - `BrewLinkSendResult.Success` -> `200 OK`
     - `BrewLinkSendResult.BadRequest` -> `400 Bad Request`
     - `BrewLinkSendResult.Unauthorized` -> `401 Unauthorized`
     - `BrewLinkSendResult.Forbidden` -> `403 Forbidden`
     - `BrewLinkSendResult.NotFound` -> `404 Not Found`
     - `BrewLinkSendResult.EquipmentDisconnected` -> `409 Conflict`
     - `BrewLinkSendResult.GatewayError` -> `502 Bad Gateway`

4. **`tests/BrewYou.ApiService.Tests/BrewLinkControlTests.cs`**:
   - Implemented 40 unit and integration tests covering:
     - All 12 actions and HMAC-SHA256 signature verification over intercepted request envelopes.
     - Out-of-bounds parameter validation returning 400 Bad Request.
     - 404 primary endpoint fallback to `/api/equipment/cmd`.
     - Role authorization: Owner and Brewer succeed, Viewer returns 403 Forbidden, Unauthenticated returns 401 Unauthorized.
     - SSRF protection: loopback (`127.0.0.1`, `localhost`), metadata (`169.254.169.254`), and zero address (`0.0.0.0`) return 400 Bad Request; valid LAN IP succeeds.
     - Gateway error mapping: offline peripheral returns 409 Conflict, gateway timeout returns 502 Bad Gateway, non-BrewLink equipment returns 409 Conflict.
     - Optimistic state update in database and immediate SSE stream broadcast.
     - State synchronization: connected device updates DB and returns 200, disconnected device returns 409, gateway error returns 502, viewer returns 403, and live SSE broadcast.

### 1.3. Verification Commands & Verbatim Outputs
- **Build**:
  `dotnet build BrewYou.slnx /warnaserror`
  Output: `Build succeeded. 0 Warning(s) 0 Error(s). Time Elapsed 00:00:05.75`
- **Formatting & Static Analysis**:
  `dotnet format BrewYou.slnx --verify-no-changes`
  Output: `Exited with code 0. Zero warnings, zero errors.`
- **New Control Test Suite**:
  `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkControlTests`
  Output: `Passed! - Failed: 0, Passed: 40, Skipped: 0, Total: 40, Duration: 10 s`
- **Existing E2E Test Suite**:
  `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkE2ETests`
  Output: `Passed! - Failed: 0, Passed: 74, Skipped: 0, Total: 74, Duration: 18 s`
- **Full Backend Test Suite**:
  `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1`
  Output: `Passed! - Failed: 0, Passed: 790, Skipped: 0, Total: 790, Duration: 19 s`

---

## 2. Logic Chain

1. **Requirement Mapping**:
   - `ORIGINAL_REQUEST.md` R2 specifies that hardware control commands must validate actions and arguments, generate signed HMAC-SHA256 envelopes using `api_token` hashed over `"{req_id}:{action}:{timestamp}"`, enforce role authorization (Owner and Brewer permitted, Viewer rejected with 403 Forbidden), update equipment state optimistically, broadcast immediate SSE telemetry, and support on-demand state sync.
2. **Implementation Alignment**:
   - `BrewLinkControlRequest` and `BrewLinkSyncResultDto` were implemented in `IBrewLinkService.cs` matching `EquipmentEndpoints.cs` and `BrewLinkE2ETests.cs` expectations.
   - In `BrewLinkService.cs`, `SendControlCommandAsync` validates action names and arguments against the 12 supported commands, retrieves and decrypts the gateway token, computes the HMAC-SHA256 signature, validates the host against SSRF vulnerabilities, and sends dual authentication headers.
   - If the primary endpoint `/api/equipment/control` returns 404, the transport automatically falls back to `/api/equipment/cmd`.
   - On successful execution, `equipment.LatestMetricsJson` is updated optimistically and broadcast via `ITelemetryBroadcastService.BroadcastEquipmentReading` to all brewery members.
   - In `SyncEquipmentStateAsync`, `GET /api/equipment` is queried and normalized via `BrewLinkTelemetryParser.ParseCanonical`, reconciling hardware ground truth with local state.
3. **Verification Integrity**:
   - 40 dedicated unit and integration tests were created in `BrewLinkControlTests.cs`, and all 74 E2E tests in `BrewLinkE2ETests.cs` plus all 790 tests in `BrewYou.ApiService.Tests` passed without a single failure or regression.
   - `dotnet format BrewYou.slnx --verify-no-changes` confirmed zero formatting or linting violations.

---

## 3. Caveats

- **No caveats.** All requirements from `ORIGINAL_REQUEST.md`, `PROJECT.md`, and the Explorer blueprints have been implemented and verified.

---

## 4. Conclusion

Milestone 2 (Hardware Control API, State Sync & Security) is complete, robust, and verified.
- The hardware control endpoint (`POST /api/v1/inventory/equipment/{id}/brewlink/control`) and state synchronization endpoint (`POST /api/v1/inventory/equipment/{id}/brewlink/sync`) are fully operational.
- HMAC-SHA256 envelope signing, SSRF host security, role-based authorization (Owner/Brewer vs Viewer 403), transparent 404 fallback, optimistic state updates, and immediate SSE broadcasts are verified by automated tests.

---

## 5. Verification Method

To independently verify this milestone:

1. **Run Static Analysis and Formatting**:
   ```powershell
   dotnet format BrewYou.slnx --verify-no-changes
   ```
   *Expected*: Exits with code 0.

2. **Run Dedicated Hardware Control Tests**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkControlTests
   ```
   *Expected*: 40 passed, 0 failed.

3. **Run E2E Test Suite**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkE2ETests
   ```
   *Expected*: 74 passed, 0 failed.

4. **Run Full Test Suite**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1
   ```
   *Expected*: 790 passed, 0 failed.

### Invalidation Conditions
The milestone is invalid if:
- Any user with the `Viewer` role can execute `/control` or `/sync` without receiving HTTP 403 Forbidden.
- An outgoing hardware control command omits or incorrectly hashes the HMAC-SHA256 signature in the `"sig"` envelope property.
- Loopback or cloud metadata addresses are accepted as gateway hosts without returning HTTP 400 Bad Request.
- Successful control execution fails to optimistically update `Equipment.LatestMetricsJson` or fails to emit an immediate SSE reading event on `/telemetry-stream`.
- A disconnected peripheral does not return HTTP 409 Conflict (`EQUIPMENT_DISCONNECTED`).
