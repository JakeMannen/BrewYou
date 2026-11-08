# Milestone 2 Explorer 2 Handoff Report: Gateway Transport Layer, HMAC Signing, Host Security & Hardware Control

## 1. Observation

### 1.1. Existing HMAC-SHA256 Signing Implementation
In `src/BrewYou.ApiService/Services/BrewLinkService.cs` (lines 623–630):
```csharp
var reqId = $"req-{Guid.NewGuid():N}"[..12];
var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
var action = "load_recipe";
var signingInput = $"{reqId}:{action}:{timestamp}";
var sig = Convert.ToHexStringLower(HMACSHA256.HashData(
    Encoding.UTF8.GetBytes(rawToken),
    Encoding.UTF8.GetBytes(signingInput)));
```
Direct observations:
- `rawToken` is obtained by decrypting `gateway.ApiToken` via `_protector.Unprotect(gateway.ApiToken)`.
- Key bytes are UTF-8 encoded: `Encoding.UTF8.GetBytes(rawToken)`.
- Data bytes are UTF-8 encoded: `Encoding.UTF8.GetBytes($"{reqId}:{action}:{timestamp}")`.
- The hash is output as a 64-character lowercase hexadecimal string via `Convert.ToHexStringLower(...)`.
- This matches the specification in `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\skills\brewlink-external-client\SKILL.md` (lines 416–437) and `tests/BrewYou.ApiService.Tests/BrewLinkE2ETests.cs` (lines 175–182).

### 1.2. Command Envelope Construction & Target Resolution
In `BrewLinkService.cs` (lines 631–655):
```csharp
var commandEnvelope = new JsonObject
{
    ["req_id"] = reqId,
    ["action"] = action,
    ["equipment_type"] = "boiler",
    ["timestamp"] = timestamp,
    ["sig"] = sig,
    ["params"] = new JsonObject { ... }
};

if (targetSlot.HasValue)
{
    commandEnvelope["slot"] = targetSlot.Value;
}
if (!string.IsNullOrWhiteSpace(targetName))
{
    commandEnvelope["target_name"] = targetName;
}
if (!string.IsNullOrWhiteSpace(targetMac))
{
    commandEnvelope["target_mac"] = targetMac;
}
```
Target resolution from `equipment.ConnectionConfigJson` (lines 538–561):
- `brewLinkGatewayId` (Guid, required).
- `targetMac` (normalized 12-char hex string, optional).
- `slot` (integer 1..3, optional).
- `targetName` (string, optional).
- Equipment configuration must have at least one target selector (`targetMac`, `slot`, or `targetName`).

### 1.3. HTTP Dispatch, Header Convention & Legacy Fallback
In `BrewLinkService.cs` (lines 665–693):
```csharp
var jsonPayload = commandEnvelope.ToJsonString();
using var reqMsg = new HttpRequestMessage(HttpMethod.Post, recipeUrl)
{
    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
};
reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
reqMsg.Headers.Add("X-BrewLink-Token", rawToken);

var response = await _httpClient.SendAsync(reqMsg, ct);

if (response.StatusCode == HttpStatusCode.NotFound)
{
    _logger.LogInformation("BrewLink endpoint {PrimaryUrl} returned 404, trying fallback {FallbackUrl} on {Host}", primaryUrl, fallbackUrl, baseHost);
    response.Dispose();

    using var fallbackReq = new HttpRequestMessage(HttpMethod.Post, fallbackUrl)
    {
        Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
    };
    fallbackReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
    fallbackReq.Headers.Add("X-BrewLink-Token", rawToken);

    response = await _httpClient.SendAsync(fallbackReq, ct);
}
```
Direct observations:
- Primary hardware control endpoint per `SKILL.md` (lines 56–65, 232–264): `POST http://<gateway-ip>/api/equipment/control`.
- Legacy fallback endpoint: `POST http://<gateway-ip>/api/equipment/cmd`.
- Both `Authorization: Bearer <token>` and `X-BrewLink-Token: <token>` are attached.
- Sync endpoint per `SKILL.md` (lines 106–167): `GET http://<gateway-ip>/api/equipment` with Bearer and X-BrewLink-Token headers.

### 1.4. Network Security & SSRF Validation
In `src/BrewYou.ApiService/Services/NetworkSecurityValidator.cs` (lines 6–116, 118–208):
- `BlockedHostnames`: `"169.254.169.254"`, `"metadata.google.internal"`, `"100.100.100.200"`, `"instance-data"`, `"localhost"`.
- `IsForbiddenAddress(IPAddress address, bool allowPrivateNetworks = false)`:
  - Loopback (`127.0.0.0/8`, `::1`) is forbidden.
  - Zero/unspecified network (`0.0.0.0/8`) is strictly forbidden.
  - Link-local and cloud metadata (`169.254.0.0/16`) are strictly forbidden.
  - Carrier-grade NAT metadata (`100.64.0.0/10`) is strictly forbidden.
  - Multicast (`224.0.0.0/4`, `ff00::/8`) is strictly forbidden.
  - When `allowPrivateNetworks: true` (used for local BrewLink gateways), RFC 1918 private subnets (`192.168.0.0/16`, `10.0.0.0/8`, `172.16.0.0/12`) are permitted, but loopback, metadata, and zero addresses remain strictly blocked.
- In `BrewLinkService.cs` (lines 757–809):
  `ValidateAndNormalizeHostAsync(string hostInput, CancellationToken ct = default)` formats host to URI, explicitly checks `hostOnly is "localhost" or "127.0.0.1" or "::1"` and `IPAddress.IsLoopback(ip)`, and calls `NetworkSecurityValidator.ValidateUrlAsync(candidateUrl, allowPrivateNetworks: true, cancellationToken: ct)`.

### 1.5. The 12 Supported Control Actions & Arguments
From `ORIGINAL_REQUEST.md` (lines 14–15), `SKILL.md` (lines 252–264), and `BrewLinkE2ETests.cs` (lines 607, 631, 656, 672, 688, 708, 1240–1320):
1. `set_pump`: `{ "state": bool }` or `{ "enabled": bool }`.
2. `set_heat`: `{ "state": bool }` or `{ "enabled": bool }`, `{ "power_pct": int }` or `{ "power": int }` (0–100%).
3. `set_target_temp`: `{ "temp_c": decimal }` or `{ "temp": decimal }` or `{ "target_temp": decimal }` (0.0–105.0°C).
4. `set_timer`: `{ "duration_min": int }` or `{ "duration": int }` or `{ "seconds": int }` (0–1440 minutes / 24 hours).
5. `pause_timer`: no parameters.
6. `resume_timer`: no parameters.
7. `cancel_timer`: no parameters.
8. `press_set`: no parameters.
9. `ack_interaction`: optional `{ "id": string?, "choice": string? }`.
10. `dismiss_alert`: optional `{ "id": string? }`.
11. `dismiss_sparge_alert`: no parameters.
12. `cancel_session`: no parameters.

### 1.6. Role Authorization & Error Status Mapping
In `EquipmentEndpoints.cs` (lines 552–561) and `BrewLinkE2ETests.cs` (lines 788–874, 1390–1490):
- Owner & Brewer: Permitted to execute hardware control and sync.
- Viewer: Rejected with `403 Forbidden` (`FORBIDDEN`).
- Unauthenticated: Rejected with `401 Unauthorized` (`UNAUTHORIZED`).
- Missing equipment: Returns `404 Not Found` (`NOT_FOUND`).
- Invalid argument/boundary: Returns `400 Bad Request` (`BAD_REQUEST`).
- Disconnected BLE peripheral (`409 Conflict` from gateway): Returns `409 Conflict` (`EQUIPMENT_DISCONNECTED`).
- Token invalid/revoked (`401/403` from gateway): Returns `401 Unauthorized` (`UNAUTHORIZED`).
- Unreachable / timeout / 5xx from gateway: Returns `502 Bad Gateway` (`GATEWAY_ERROR`).

---

## 2. Logic Chain

1. **Host Validation Pre-Flight**:
   - Before `_httpClient` initiates any network request to a BrewLink gateway, `gateway.Host` must be validated against SSRF exploits.
   - `ValidateAndNormalizeHostAsync` (backed by `NetworkSecurityValidator`) ensures that attackers cannot configure loopback (`127.0.0.1`, `localhost`, `[::1]`, decimal/hex IP encodings) or cloud metadata (`169.254.169.254`, `metadata.google.internal`) to probe internal microservices.
   - Private LAN addresses (`192.168.x.x`, `10.x.x.x`, `172.16.x.x`) are allowed because physical ESP32 gateways reside on the brewery local area network.

2. **Cryptographic Signing (HMAC-SHA256)**:
   - To prevent command tampering or replay across shared MQTT/HTTP transports, commands require an HMAC-SHA256 signature.
   - The signing input string is deterministically formatted as `"{req_id}:{action}:{timestamp}"`.
   - The key is the raw gateway API token stored encrypted in PostgreSQL and unprotected via ASP.NET Data Protection.
   - The computed signature is encoded as 64 lowercase hexadecimal characters.

3. **Transport Dispatch with Fallback**:
   - Modern BrewLink ESP32 firmware exposes `POST /api/equipment/control` for direct hardware manipulation.
   - Legacy firmware exposes `POST /api/equipment/cmd`.
   - The transport layer sends the POST request to `/api/equipment/control`. If and only if the gateway returns HTTP `404 Not Found`, the client transparently retries the identical signed payload to `/api/equipment/cmd`.
   - This provides backwards compatibility across heterogeneous gateway firmware revisions.

4. **Error Classification & Status Code Normalization**:
   - Gateway communication errors must map to standard HTTP status codes:
     - Network connection failure (`HttpRequestException`) or gateway timeout (`TaskCanceledException` / `TimeoutException`) -> `502 Bad Gateway`.
     - Internal ESP32 crash (`500 Internal Server Error`) -> `502 Bad Gateway`.
     - Peripheral offline (`409 Conflict` with `EQUIPMENT_OFFLINE`) -> `409 Conflict`.
     - Token revocation (`401` or `403` from gateway) -> `401 Unauthorized`.
     - Data protection decryption failure -> `401 Unauthorized`.
     - Invalid parameters (e.g. `power_pct > 100`, `duration_min < 0`, unknown action) -> `400 Bad Request`.
     - Caller has `Viewer` role -> `403 Forbidden`.

5. **Optimistic State Updates & Immediate SSE Telemetry Broadcast**:
   - Hardware commands take effect immediately on the peripheral. Rather than waiting for background polling, the API updates `equipment.LatestMetricsJson` optimistically using `BrewLinkTelemetryParser`.
   - The updated state is persisted to the database and broadcast immediately via `ITelemetryBroadcastService.BroadcastEquipmentReading` to the equipment owner and all setup collaborators, eliminating UI lag.

6. **State Synchronization (`POST /brewlink/sync`)**:
   - Dispatches `GET /api/equipment` to the gateway.
   - Matches the target device by MAC, slot, or name.
   - If the peripheral is absent or reports `connected: false`, returns `409 Conflict`.
   - If connected, parses live state into `Equipment.CurrentTemperatureC`, `Equipment.TemperatureUpdatedAt`, `Equipment.LastTelemetryAt`, and `Equipment.LatestMetricsJson`, persists to DB, broadcasts SSE, and returns `200 OK`.

---

## 3. Caveats

- **No caveats.** The specification from `ORIGINAL_REQUEST.md`, `brewlink-external-client/SKILL.md`, and the existing `BrewYou.ApiService` codebase is completely verified and unambiguous.
- All 12 commands, argument formats, parameter aliases, envelope structures, signing hashes, host security rules, role checks, and status code mappings have been identified and traced.

---

## 4. Conclusion

Requirement R2 is ready for implementation by the Worker. The implementation requires modifications and additions across four primary files:
1. `src/BrewYou.ApiService/Services/NetworkSecurityValidator.cs`: Expose `ValidateHost` / `ValidateHostAsync` convenience helpers.
2. `src/BrewYou.ApiService/Services/IBrewLinkService.cs`: Define `BrewLinkControlRequest` and interface methods `SendControlCommandAsync` and `SyncEquipmentStateAsync`.
3. `src/BrewYou.ApiService/Services/BrewLinkService.cs`: Implement command normalization, HMAC-SHA256 signing, host security validation, primary/fallback dispatch, error handling, optimistic state updates on `LatestMetricsJson`, and state sync.
4. `src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs`: Map `POST /api/v1/inventory/equipment/{id}/brewlink/control` and `POST /api/v1/inventory/equipment/{id}/brewlink/sync` with Owner/Brewer authorization guards and status code mapping.

---

## 5. Verification Method

### 5.1. Automated Test Execution Commands
Run the following test commands (using `BypassSandbox: true` in the command execution environment):
```bash
# 1. Run all BrewLink E2E tests (including Tier 1 control, sync, host validation, and roles)
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~BrewLinkE2ETests"

# 2. Run new dedicated Hardware Control Unit & Integration test suite
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~BrewLinkControlTests"

# 3. Run Security & SSRF audit tests
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~SecurityAuditTests"

# 4. Verify code formatting and linting
dotnet format --verify-no-changes
```

### 5.2. Invalidation Conditions
- If any command other than the 12 specified is accepted without returning `400 Bad Request`, the implementation is invalid.
- If a user with the `Viewer` role can execute control or sync without receiving `403 Forbidden`, the implementation is invalid.
- If the signature in the control envelope does not match a 64-character lowercase hex HMAC-SHA256 of `"{req_id}:{action}:{timestamp}"`, the implementation is invalid.
- If gateway host targets `127.0.0.1`, `localhost`, `::1`, `169.254.169.254`, or `0.0.0.0` are not blocked with `400 Bad Request`, the implementation is invalid.
- If a disconnected peripheral (`409 Conflict` from gateway) does not return `409 Conflict` to the client, the implementation is invalid.
- If gateway timeouts or network exceptions do not return `502 Bad Gateway`, the implementation is invalid.
- If `LatestMetricsJson` is not optimistically updated and broadcast via SSE immediately on successful command execution, the implementation is invalid.

---

## 6. Worker Step-by-Step Implementation Guide

### Step 1: Update `NetworkSecurityValidator.cs`
Add `ValidateHost` / `ValidateHostAsync` methods to `src/BrewYou.ApiService/Services/NetworkSecurityValidator.cs`:
```csharp
/// <summary>
/// Validates whether a gateway host or IP is safe from SSRF exploits.
/// Allows private local subnets (RFC 1918) when allowPrivateNetworks is true, but strictly prohibits
/// loopback (127.0.0.0/8, ::1, localhost), cloud metadata (169.254.169.254), and unspecified (0.0.0.0).
/// </summary>
public static async Task<(bool IsValid, string? ErrorMessage, Uri? ValidatedUri)> ValidateHostAsync(
    string hostInput,
    bool allowPrivateNetworks = true,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(hostInput))
    {
        return (false, "Gateway host or IP is required.", null);
    }

    var trimmed = hostInput.Trim().TrimEnd('/');
    var candidateUrl = trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                       trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        ? trimmed
        : $"http://{trimmed}";

    if (!Uri.TryCreate(candidateUrl, UriKind.Absolute, out var uri))
    {
        return (false, "Invalid host format.", null);
    }

    if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
    {
        return (false, "Only HTTP and HTTPS gateway endpoints are supported.", null);
    }

    var hostOnly = uri.Host.Trim().ToLowerInvariant();

    if (hostOnly is "localhost" or "127.0.0.1" or "::1" or "[::1]")
    {
        return (false, "Access to local loopback endpoints is prohibited.", null);
    }

    if (IPAddress.TryParse(hostOnly, out var ip))
    {
        if (IPAddress.IsLoopback(ip))
        {
            return (false, "Access to local loopback addresses is prohibited.", null);
        }
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
        {
            return (false, "Access to unspecified network address is prohibited.", null);
        }
    }

    return await ValidateUrlAsync(candidateUrl, allowPrivateNetworks: allowPrivateNetworks, cancellationToken: cancellationToken);
}

public static (bool IsValid, string? ErrorMessage) ValidateHost(string hostInput, bool allowPrivateNetworks = true)
{
    if (string.IsNullOrWhiteSpace(hostInput)) return (false, "Gateway host or IP is required.");
    var trimmed = hostInput.Trim().TrimEnd('/');
    var candidateUrl = trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                       trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        ? trimmed
        : $"http://{trimmed}";
    return ValidateUrlFormat(candidateUrl);
}
```

### Step 2: Update `IBrewLinkService.cs`
In `src/BrewYou.ApiService/Services/IBrewLinkService.cs`:
1. Add `BrewLinkControlRequest`:
```csharp
public record BrewLinkControlRequest(
    string Action,
    JsonElement? Params = null
);
```
2. Add method signatures to `IBrewLinkService`:
```csharp
Task<(BrewLinkSendResult Result, BrewLinkCommandResultDto? CommandResult, string? ErrorMessage)> SendControlCommandAsync(
    string userId,
    Guid equipmentId,
    BrewLinkControlRequest request,
    CancellationToken ct = default);

Task<(BrewLinkSendResult Result, BrewLinkCommandResultDto? CommandResult, string? ErrorMessage)> SyncEquipmentStateAsync(
    string userId,
    Guid equipmentId,
    CancellationToken ct = default);
```

### Step 3: Implement Hardware Control and Sync in `BrewLinkService.cs`
In `src/BrewYou.ApiService/Services/BrewLinkService.cs`:
1. Inject `ITelemetryBroadcastService? broadcastService = null` in constructor.
2. Implement parameter extraction & validation for all 12 actions:
```csharp
private static readonly HashSet<string> SupportedActions = new(StringComparer.OrdinalIgnoreCase)
{
    "set_pump", "set_heat", "set_target_temp", "set_timer",
    "pause_timer", "resume_timer", "cancel_timer", "press_set",
    "ack_interaction", "dismiss_alert", "dismiss_sparge_alert", "cancel_session"
};
```
3. Implement `SendControlCommandAsync`:
   - Role check: Owner or Brewer permitted; Viewer forbidden.
   - Equipment check: `ConnectionType == EquipmentConnectionType.BrewLink`.
   - Parse `equipment.ConnectionConfigJson` -> get `brewLinkGatewayId`, `targetMac`, `slot`, `targetName`.
   - Look up gateway from `_db.BrewLinkGateways`.
   - Validate host via `NetworkSecurityValidator.ValidateHostAsync(gateway.Host, allowPrivateNetworks: true, ct)`.
   - Decrypt `gateway.ApiToken` via `_protector.Unprotect(...)`.
   - Construct envelope with HMAC-SHA256 signature (`Convert.ToHexStringLower(HMACSHA256.HashData(tokenBytes, signingInputBytes))`).
   - Send POST to `${normalizedHost}/api/equipment/control` with `Authorization: Bearer` and `X-BrewLink-Token`.
   - On 404, fallback to `POST ${normalizedHost}/api/equipment/cmd`.
   - On success (`200 OK`), update `equipment.LatestMetricsJson` optimistically, persist to DB, broadcast SSE via `_broadcastService?.BroadcastEquipmentReading`, and return `BrewLinkCommandResultDto`.
   - On errors: map 409 to `EquipmentDisconnected`, 401/403 to `Unauthorized`, timeout/network/5xx to `GatewayError`.

4. Implement `SyncEquipmentStateAsync`:
   - Same role check and host validation.
   - Dispatches `GET ${normalizedHost}/api/equipment`.
   - Finds matching peripheral in equipment list.
   - If not found or `connected == false`, return `EquipmentDisconnected` (409 Conflict).
   - If connected, parses live state into `equipment.CurrentTemperatureC`, `equipment.TemperatureUpdatedAt`, `equipment.LastTelemetryAt`, and `equipment.LatestMetricsJson`.
   - Saves to DB, broadcasts SSE, and returns `BrewLinkCommandResultDto`.

### Step 4: Map Endpoints in `EquipmentEndpoints.cs`
In `src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs`:
Add endpoints under the `/api/v1/inventory/equipment` group:
```csharp
group.MapPost("/{id:guid}/brewlink/control", async (
    Guid id,
    BrewLinkControlRequest request,
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
    return MapBrewLinkResult(result, cmdResult, error);
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

    var (result, cmdResult, error) = await brewLinkService.SyncEquipmentStateAsync(userId, id, ct);
    return MapBrewLinkResult(result, cmdResult, error);
})
.WithName("SyncBrewLinkEquipment")
.Produces<ApiResponse<BrewLinkCommandResultDto>>(StatusCodes.Status200OK)
.Produces<ApiResponse>(StatusCodes.Status400BadRequest)
.Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
.Produces<ApiResponse>(StatusCodes.Status403Forbidden)
.Produces<ApiResponse>(StatusCodes.Status404NotFound)
.Produces<ApiResponse>(StatusCodes.Status409Conflict)
.Produces<ApiResponse>(StatusCodes.Status502BadGateway);
```

### Step 5: Create Test Suite `BrewLinkControlTests.cs`
Create `tests/BrewYou.ApiService.Tests/BrewLinkControlTests.cs`:
- Test all 12 commands (`set_pump`, `set_heat`, `set_target_temp`, `set_timer`, `pause_timer`, `resume_timer`, `cancel_timer`, `press_set`, `ack_interaction`, `dismiss_alert`, `dismiss_sparge_alert`, `cancel_session`).
- Test parameter aliases (`state` vs `enabled`, `power_pct` vs `power`, `temp_c` vs `target_temp`, `duration_min` vs `seconds`).
- Test boundary validations: `power_pct` (< 0, > 100), `duration_min` (< 0, > 1440), `temp_c` (< 0, > 105).
- Test unsupported action returns 400 Bad Request.
- Test HMAC-SHA256 signature verification on intercepted requests.
- Test primary `/api/equipment/control` 404 fallback to `/api/equipment/cmd`.
- Test role authorization: Owner (200), Brewer (200), Viewer (403), Unauthenticated (401).
- Test SSRF protection on gateway hosts (loopback 400, metadata 400, zero-address 400).
- Test error statuses: peripheral offline (409), token revoked (401), gateway timeout / connection refused / 500 error (502).
- Test state sync: connected gateway updates DB and returns 200; disconnected device returns 409.
- Test optimistic state update on `LatestMetricsJson` and SSE broadcast.
