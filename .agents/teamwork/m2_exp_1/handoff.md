# Milestone 2 Investigation Report: Hardware Control API & State Sync Architecture

## 1. Observation

### 1.1. Authoritative Requirements & Context
* **From `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\ORIGINAL_REQUEST.md` (lines 14–15, 32–36)**:
  - Requirement R2: "Add hardware control support allowing authorized brewery members (Owner and Brewer roles) to issue signed HMAC-SHA256 control commands (`set_pump`, `set_heat`, `set_target_temp`, `set_timer`, `pause_timer`, `resume_timer`, `cancel_timer`, `press_set`, `ack_interaction`, `dismiss_alert`, `dismiss_sparge_alert`, `cancel_session`) to BrewLink equipment via `POST /api/v1/inventory/equipment/{id}/brewlink/control` and pull live state on demand via `POST /api/v1/inventory/equipment/{id}/brewlink/sync`."
  - Acceptance Criteria:
    - "`POST /api/v1/inventory/equipment/{id}/brewlink/control` validates actions and arguments, transmits HMAC-SHA256 signed envelopes to the gateway, and returns appropriate status codes (200, 400, 401, 403, 404, 409, 502)."
    - "Viewer roles are rejected with 403 Forbidden when attempting hardware control commands."
    - "`POST /api/v1/inventory/equipment/{id}/brewlink/sync` fetches live gateway state and updates equipment records."

### 1.2. Existing Route & Pattern in `EquipmentEndpoints.cs`
* In `src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs` (lines 20–25, 538–572):
  - Base route group:
    ```csharp
    var group = routes.MapGroup("/api/v1/inventory/equipment")
        .WithTags("Equipment")
        .RequireAuthorization();
    ```
  - Existing recipe dispatch endpoint (`POST /{id:guid}/brewlink/send-recipe`):
    ```csharp
    var (result, cmdResult, error) = await brewLinkService.SendRecipeToEquipmentAsync(userId, id, request, ct);
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

### 1.3. Existing Service Contracts in `IBrewLinkService.cs`
* In `src/BrewYou.ApiService/Services/IBrewLinkService.cs` (lines 20–26, 58–67):
  ```csharp
  public record BrewLinkCommandResultDto(
      string Status,
      string ReqId,
      string GatewayId,
      string TargetMac,
      string Message
  );

  public enum BrewLinkSendResult
  {
      Success,
      EquipmentDisconnected,
      Unauthorized,
      NotFound,
      Forbidden,
      BadRequest,
      GatewayError
  }
  ```

### 1.4. Existing Authorization Logic in `BrewLinkService.cs`
* In `src/BrewYou.ApiService/Services/BrewLinkService.cs` (lines 508–519):
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

### 1.5. Existing Opaque-Box Test Expectations in `BrewLinkE2ETests.cs`
* In `tests/BrewYou.ApiService.Tests/BrewLinkE2ETests.cs` (lines 25–29, 607–621, 804–835, 1428–1448):
  - Request DTO shape: `new BrewLinkControlTestRequest(string Action, object? Params = null)`
  - Expected Response DTO shapes:
    - Control: `BrewLinkControlTestResponse(string Status, string? ReqId, string? GatewayId, string? TargetMac, string? Message)`
    - Sync: `BrewLinkSyncTestResponse(string Status, bool Synced, decimal? TemperatureC, string? MetricsJson)`
  - Status expectations:
    - Owner and Brewer get `200 OK` on control.
    - Viewer receives `403 Forbidden` (`FORBIDDEN`).
    - Unauthenticated receives `401 Unauthorized` (`UNAUTHORIZED`).
    - Disconnected/offline device receives `409 Conflict` (`EQUIPMENT_DISCONNECTED`).
    - Gateway timeout or 500 error returns `502 Bad Gateway` (`GATEWAY_ERROR`).
    - Out-of-bounds arguments (`power_pct > 100`, `duration_min < 0` or `> 1440`) return `400 Bad Request` (`BAD_REQUEST`).

---

## 2. Logic Chain

1. **Route Co-location**:
   - Observations 1.2 demonstrate that `/api/v1/inventory/equipment` already hosts equipment operations and authenticated endpoints.
   - Therefore, the control endpoint must be mapped as `POST /{id:guid}/brewlink/control` and the sync endpoint as `POST /{id:guid}/brewlink/sync` inside `MapEquipmentEndpoints(this IEndpointRouteBuilder routes)`.

2. **DTO Contract Modeling**:
   - Observation 1.5 demonstrates that test callers and frontend clients supply `{ "action": "<action_name>", "params": { ... } }`.
   - Modeling `BrewLinkControlRequest` with `string Action` and `JsonElement? Params = null` supports both parameterless commands (e.g. `pause_timer`, `press_set`, `cancel_session`) and parameterized commands (e.g. `set_pump`, `set_heat`, `set_target_temp`, `set_timer`).
   - For `BrewLinkCommandResultDto`, Observation 1.3 shows it already exists with fields (`Status`, `ReqId`, `GatewayId`, `TargetMac`, `Message`), perfectly matching `BrewLinkControlTestResponse` in Observation 1.5. Adding an optional property `string? LatestMetricsJson = null` preserves full backward compatibility for existing recipe upload calls while enabling optimistic metrics delivery.
   - For `BrewLinkSyncResultDto`, Observation 1.5 expects `Status`, `Synced`, `TemperatureC`, `MetricsJson`. Providing these top-level fields plus optional `EquipmentDto? Equipment = null` satisfies both the E2E test assertions and frontend store updates.

3. **Validation & Normalization Rules**:
   - Based on Observations 1.1, 1.4, and 1.5, all 12 actions must be recognized. Any other action string must be rejected with `400 Bad Request`.
   - Arguments must tolerate parameter naming variations:
     - `set_pump`: `state` or `enabled` (boolean).
     - `set_heat`: `state` or `enabled` (boolean); `power_pct` or `power` (int 0..100). If `state == false`, clamp power to 0. If `state == true` and power omitted, default to 100.
     - `set_target_temp`: `temp_c` or `temp` or `target_temp` (decimal 0.0..105.0°C).
     - `set_timer`: `duration_min` or `duration` or `seconds` (int > 0 and $\le 1440$). If `seconds` provided, convert: `seconds / 60`.
     - `ack_interaction`, `dismiss_alert`: optional string identifier.
     - `pause_timer`, `resume_timer`, `cancel_timer`, `press_set`, `dismiss_sparge_alert`, `cancel_session`: no parameters required.

4. **Authorization & RBAC Enforcement**:
   - From Observation 1.4, role checking evaluates `isOwner` and `isBrewer`.
   - If user is setup Owner or equipment owner: permitted.
   - If user has `BreweryRole.Brewer`: permitted.
   - If user has `BreweryRole.Viewer` or is an unassociated user: `!isOwner && !isBrewer` evaluates to true $\rightarrow$ returns `BrewLinkSendResult.Forbidden` $\rightarrow$ maps to `403 Forbidden` (`FORBIDDEN`).
   - If caller is unauthenticated: returns `401 Unauthorized` (`UNAUTHORIZED`).

5. **Status Code Mapping Consistency**:
   - Using the existing `switch (result)` pattern in Observation 1.2:
     - `Success` $\rightarrow$ `200 OK`
     - `BadRequest` $\rightarrow$ `400 Bad Request`
     - `Unauthorized` $\rightarrow$ `401 Unauthorized`
     - `Forbidden` $\rightarrow$ `403 Forbidden`
     - `NotFound` $\rightarrow$ `404 Not Found`
     - `EquipmentDisconnected` $\rightarrow$ `409 Conflict` (for offline, disconnected, or non-BrewLink equipment)
     - `GatewayError` $\rightarrow$ `502 Bad Gateway` (for timeouts, gateway 5xx, or network errors)

---

## 3. Caveats

- **No caveats.** The contracts, parameters, status codes, and authorization rules are fully specified by `ORIGINAL_REQUEST.md`, `brewlink-external-client/SKILL.md`, and the existing baseline tests in `BrewLinkE2ETests.cs`.

---

## 4. Conclusion & Concrete Specifications

### 4.1. Exact DTO Definitions

File: `src/BrewYou.ApiService/Services/IBrewLinkService.cs`

```csharp
// --- Milestone 2 Request & Response DTOs ---

public record BrewLinkControlRequest(
    string Action,
    JsonElement? Params = null
);

public record BrewLinkSyncResultDto(
    string Status,
    bool Synced,
    decimal? TemperatureC,
    string? MetricsJson,
    Guid? EquipmentId = null,
    EquipmentDto? Equipment = null
);

// Note: BrewLinkCommandResultDto already exists on line 20 of IBrewLinkService.cs:
// public record BrewLinkCommandResultDto(
//     string Status,
//     string ReqId,
//     string GatewayId,
//     string TargetMac,
//     string Message,
//     string? LatestMetricsJson = null
// );
```

### 4.2. Exact Interface Signatures in `IBrewLinkService.cs`

```csharp
public interface IBrewLinkService
{
    // ... existing methods ...

    Task<(BrewLinkSendResult Result, BrewLinkCommandResultDto? CommandResult, string? ErrorMessage)> SendControlCommandAsync(
        string userId,
        Guid equipmentId,
        BrewLinkControlRequest request,
        CancellationToken ct = default);

    Task<(BrewLinkSendResult Result, BrewLinkSyncResultDto? SyncResult, string? ErrorMessage)> SyncEquipmentStateAsync(
        string userId,
        Guid equipmentId,
        CancellationToken ct = default);
}
```

### 4.3. Exact Route Definitions in `EquipmentEndpoints.cs`

File: `src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs`

```csharp
        group.MapPost("/{id:guid}/brewlink/control", async (
            Guid id,
            [FromBody] BrewLinkControlRequest request,
            ClaimsPrincipal principal,
            IBrewLinkService brewLinkService,
            CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, cmdResult, error) = await brewLinkService.SendControlCommandAsync(userId, id, request, ct);
            return result switch
            {
                BrewLinkSendResult.Success => Results.Ok(ApiResponse<BrewLinkCommandResultDto>.Ok(cmdResult!)),
                BrewLinkSendResult.EquipmentDisconnected => Results.Conflict(ApiResponse.Fail("EQUIPMENT_DISCONNECTED", error ?? "Equipment is disconnected from BrewLink gateway.")),
                BrewLinkSendResult.Unauthorized => Results.Json(ApiResponse.Fail("UNAUTHORIZED", error ?? "BrewLink gateway authorization failed."), statusCode: StatusCodes.Status401Unauthorized),
                BrewLinkSendResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", error ?? "Equipment not found.")),
                BrewLinkSendResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", error ?? "Access denied."), statusCode: StatusCodes.Status403Forbidden),
                BrewLinkSendResult.BadRequest => Results.BadRequest(ApiResponse.Fail("BAD_REQUEST", error ?? "Invalid request.")),
                _ => Results.Json(ApiResponse.Fail("GATEWAY_ERROR", error ?? "Failed to send command to BrewLink gateway."), statusCode: StatusCodes.Status502BadGateway)
            };
        })
        .WithName("SendBrewLinkControl")
        .Produces<ApiResponse<BrewLinkCommandResultDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict)
        .Produces<ApiResponse>(StatusCodes.Status502BadGateway);

        group.MapPost("/{id:guid}/brewlink/sync", async (
            Guid id,
            ClaimsPrincipal principal,
            IBrewLinkService brewLinkService,
            CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, syncResult, error) = await brewLinkService.SyncEquipmentStateAsync(userId, id, ct);
            return result switch
            {
                BrewLinkSendResult.Success => Results.Ok(ApiResponse<BrewLinkSyncResultDto>.Ok(syncResult!)),
                BrewLinkSendResult.EquipmentDisconnected => Results.Conflict(ApiResponse.Fail("EQUIPMENT_DISCONNECTED", error ?? "Equipment is disconnected from BrewLink gateway.")),
                BrewLinkSendResult.Unauthorized => Results.Json(ApiResponse.Fail("UNAUTHORIZED", error ?? "BrewLink gateway authorization failed."), statusCode: StatusCodes.Status401Unauthorized),
                BrewLinkSendResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", error ?? "Equipment not found.")),
                BrewLinkSendResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", error ?? "Access denied."), statusCode: StatusCodes.Status403Forbidden),
                BrewLinkSendResult.BadRequest => Results.BadRequest(ApiResponse.Fail("BAD_REQUEST", error ?? "Invalid request.")),
                _ => Results.Json(ApiResponse.Fail("GATEWAY_ERROR", error ?? "Failed to sync equipment state from BrewLink gateway."), statusCode: StatusCodes.Status502BadGateway)
            };
        })
        .WithName("SyncBrewLinkEquipmentState")
        .Produces<ApiResponse<BrewLinkSyncResultDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict)
        .Produces<ApiResponse>(StatusCodes.Status502BadGateway);
```

### 4.4. Step-by-Step Implementation Plan for the Worker

1. **Step 1: Declare DTOs and Signatures in `IBrewLinkService.cs`**:
   - Add `BrewLinkControlRequest` and `BrewLinkSyncResultDto` records.
   - Add `SendControlCommandAsync` and `SyncEquipmentStateAsync` to the `IBrewLinkService` interface.

2. **Step 2: Map Endpoints in `EquipmentEndpoints.cs`**:
   - Add `POST /{id:guid}/brewlink/control` and `POST /{id:guid}/brewlink/sync` route handlers with full status code mapping switch expressions and OpenAPI annotations.

3. **Step 3: Implement `SendControlCommandAsync` in `BrewLinkService.cs`**:
   - Fetch equipment with `BrewerySetup.Members`.
   - Enforce authorization: `isOwner || isBrewer` (return `Forbidden` if false).
   - Enforce `ConnectionType == EquipmentConnectionType.BrewLink` (return `EquipmentDisconnected` if false).
   - Validate action against 12 supported commands (return `BadRequest` if unknown).
   - Parse and validate action parameters (return `BadRequest` if invalid).
   - Retrieve paired gateway and unprotect `api_token` (return `Unauthorized` if decryption fails).
   - Validate host via `NetworkSecurityValidator` (return `BadRequest` if invalid/loopback).
   - Generate HMAC-SHA256 signature over `"{req_id}:{action}:{timestamp}"`.
   - Dispatch POST request to `http://<host>/api/equipment/control` (with fallback to `/api/equipment/cmd` on 404).
   - On 200 OK: optimistically update `equipment.LatestMetricsJson`, persist to DB, broadcast immediate SSE via `ITelemetryBroadcastService.BroadcastEquipmentReading`, and return `BrewLinkCommandResultDto`.
   - On 409 Conflict: return `EquipmentDisconnected`.
   - On 401/403: return `Unauthorized`.
   - On 5xx / timeout / network exception: return `GatewayError`.

4. **Step 4: Implement `SyncEquipmentStateAsync` in `BrewLinkService.cs`**:
   - Enforce authorization (`isOwner || isBrewer`).
   - Validate `ConnectionType == EquipmentConnectionType.BrewLink`.
   - Query gateway via `GET /api/equipment`.
   - Match target BLE equipment by `targetMac`, `slot`, or `targetName`.
   - If not found or `connected == false`: return `EquipmentDisconnected` (409 Conflict).
   - Parse telemetry into canonical metrics and update `equipment.CurrentTemperatureC`, `TemperatureUpdatedAt`, `LastTelemetryAt`, and `LatestMetricsJson`.
   - Save DB changes and broadcast immediate SSE event.
   - Return `BrewLinkSyncResultDto`.

---

## 5. Verification Method

### 5.1. Test Commands
Execute the following verification commands from workspace root:
1. `dotnet test tests/BrewYou.ApiService.Tests/ --filter "FullyQualifiedName~BrewLink"`
   - Verifies all existing and new BrewLink unit, integration, and E2E tests.
2. `dotnet test tests/BrewYou.ApiService.Tests/ --filter "FullyQualifiedName~Tier1_Control|Tier1_RoleAuth|Tier1_Sync"`
   - Specifically targets the opaque-box test cases for hardware control, viewer 403 rejection, and state sync in `BrewLinkE2ETests.cs`.
3. `dotnet format --verify-no-changes`
   - Confirms formatting and static analysis compliance across all modified backend files.

### 5.2. Invalidation Conditions
The implementation is invalid if:
- `POST /api/v1/inventory/equipment/{id}/brewlink/control` or `/sync` is accessible without authentication.
- A user with role `BreweryRole.Viewer` receives anything other than `403 Forbidden` (`FORBIDDEN`) on `/control` or `/sync`.
- Any command outside the 12 specified actions succeeds instead of returning `400 Bad Request`.
- An invalid parameter (e.g. `power_pct = 150` or `duration_min = -10`) succeeds instead of returning `400 Bad Request`.
- Disconnected equipment returns `200 OK` or `500 InternalServerError` instead of `409 Conflict`.
- Gateway timeout returns `500` instead of `502 Bad Gateway`.
- The HMAC-SHA256 signature is omitted or computed with an incorrect key or string format.
