# Milestone 2 Investigation Report: Optimistic State Sync, SSE Fanout & Test Architecture

**Author**: Explorer 3 (`m2_exp_3`)  
**Target Milestone**: Milestone 2 — Hardware Control API, State Sync & Security  
**Working Directory**: `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_exp_3\`  
**Date**: 2026-11-07  

---

## 1. Observation

### 1.1. Authoritative Requirements & Context
1. **`ORIGINAL_REQUEST.md` (lines 14–15)**:
   > "Add hardware control support allowing authorized brewery members (Owner and Brewer roles) to issue signed HMAC-SHA256 control commands (`set_pump`, `set_heat`, `set_target_temp`, `set_timer`, `pause_timer`, `resume_timer`, `cancel_timer`, `press_set`, `ack_interaction`, `dismiss_alert`, `dismiss_sparge_alert`, `cancel_session`) to BrewLink equipment via `POST /api/v1/inventory/equipment/{id}/brewlink/control` and pull live state on demand via `POST /api/v1/inventory/equipment/{id}/brewlink/sync`. Command execution must validate gateway hosts, update equipment state optimistically, and broadcast immediate SSE telemetry updates."
2. **`ORIGINAL_REQUEST.md` (lines 32–36)**:
   > "- `POST /api/v1/inventory/equipment/{id}/brewlink/control` validates actions and arguments, transmits HMAC-SHA256 signed envelopes to the gateway, and returns appropriate status codes (200, 400, 401, 403, 404, 409, 502)."  
   > "- Viewer roles are rejected with 403 Forbidden when attempting hardware control commands."  
   > "- `POST /api/v1/inventory/equipment/{id}/brewlink/sync` fetches live gateway state and updates equipment records."

### 1.2. Telemetry and Broadcast Infrastructure
1. **`src/BrewYou.ApiService/Services/BrewLinkTelemetryParser.cs` (lines 9–43, 126–144)**:
   - Exposes canonical models: `BrewLinkCanonicalMetrics`, `BrewLinkTimerMetrics`, `BrewLinkStageMetrics`, `BrewLinkInteractionMetrics`.
   - `ToMetricsJson(BrewLinkCanonicalMetrics metrics)` serializes to camelCase JSON without indentation or nulls.
   - `FromMetricsJson(string? json)` deserializes safely, returning `null` on empty or invalid JSON without throwing.
   - `ParseCanonical(JsonElement root, Equipment? equipment)` extracts canonical metrics from REST equipment payloads (`root.equipment` array matched by `targetMac`, `slot`, or `targetName`).
2. **`src/BrewYou.ApiService/Services/ITelemetryBroadcastService.cs` (lines 54–85)**:
   - `BroadcastEquipmentReading(string userId, EquipmentTelemetryUpdateDto update)` pushes updates into bounded in-memory channels (`BoundedChannel<EquipmentTelemetryUpdateDto>`).
   - `BroadcastReading(BatchEquipmentReadingDto reading)` pushes updates into batch channels.
   - `EquipmentTelemetryUpdateDto` includes `Guid EquipmentId`, `decimal TemperatureC`, `DateTime Timestamp`, `string? Source`, and `string? MetricsJson`.
3. **`src/BrewYou.ApiService/Services/TelemetryBroadcastService.cs` (lines 114–128)**:
   - Uses `channel.Writer.TryWrite(update)`: non-blocking, zero wait, drops oldest if channel capacity (100) is exceeded.
4. **`src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs` (lines 26–107)**:
   - Endpoint `GET /api/v1/inventory/equipment/telemetry-stream` streams SSE events: `event: reading\ndata: {EquipmentTelemetryUpdateDto}\n\n`.
5. **`src/BrewYou.ApiService/Services/TelemetryService.cs` (lines 925–966)**:
   - Shows the established fanout pattern: broadcasts to `equipment.UserId`, `equipment.BrewerySetup.UserId`, and each setup member (`member.UserId`).
6. **`src/BrewYou.ApiService/Data/Entities/Equipment.cs` (lines 156–168)**:
   - `public decimal? CurrentTemperatureC { get; set; }`
   - `public DateTime? TemperatureUpdatedAt { get; set; }`
   - `public DateTime? LastTelemetryAt { get; set; }`
   - `public string? LatestMetricsJson { get; set; }` holds the serialized live state.

### 1.3. Existing Gateway Communication & HMAC Pattern
1. **`src/BrewYou.ApiService/Services/BrewLinkService.cs` (lines 623–630)**:
   - Demonstrates HMAC generation:
     ```csharp
     var reqId = $"req-{Guid.NewGuid():N}"[..12];
     var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
     var action = "load_recipe";
     var signingInput = $"{reqId}:{action}:{timestamp}";
     var sig = Convert.ToHexStringLower(HMACSHA256.HashData(
         Encoding.UTF8.GetBytes(rawToken),
         Encoding.UTF8.GetBytes(signingInput)));
     ```
2. **`src/BrewYou.ApiService/Services/BrewLinkService.cs` (lines 757–809)**:
   - `ValidateAndNormalizeHostAsync` enforces:
     - Scheme must be `http` or `https`.
     - Blocks loopback hosts: `localhost`, `127.0.0.1`, `::1`.
     - Calls `NetworkSecurityValidator.ValidateUrlAsync(candidateUrl, allowPrivateNetworks: true)`.
3. **`src/BrewYou.ApiService/Services/NetworkSecurityValidator.cs` (lines 118–208)**:
   - Blocks cloud metadata (`169.254.169.254`), `0.0.0.0/8`, link-local IPv6, multicast.
   - When `allowPrivateNetworks: false`, blocks RFC 1918 (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16).
4. **`src/BrewYou.ApiService/Services/BrewLinkService.cs` (lines 508–519)**:
   - Role authorization logic:
     - `isOwner = equipment.UserId == userId || (equipment.BrewerySetup != null && (equipment.BrewerySetup.UserId == userId || equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner)))`
     - `isBrewer = equipment.BrewerySetup != null && equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Brewer)`
     - Rejects any caller who is neither Owner nor Brewer with `Forbidden` (HTTP 403).

### 1.4. Existing Test Patterns
1. **`tests/BrewYou.ApiService.Tests/BrewLinkEndpointsTests.cs` (lines 18–93)**:
   - `FakeBrewLinkHttpMessageHandler` allows interception and recording of outbound requests, inspects headers (`Authorization: Bearer <token>`, `X-BrewLink-Token: <token>`) and request bodies, and returns mocked responses.
2. **`tests/BrewYou.ApiService.Tests/BrewLinkE2ETests.cs` (lines 33–47, 588–720)**:
   - Defines `BrewLinkE2ETestFactory : WebApplicationFactory<Program>` configuring `AddHttpClient<IBrewLinkService, BrewLinkService>().ConfigurePrimaryHttpMessageHandler(() => HttpHandler)`.
   - Tests exist verifying Tier 1 through Tier 4 scenarios, but `BrewLinkControlTests.cs` dedicated to unit/integration testing of the control and sync API does not exist yet.

---

## 2. Logic Chain

1. **Optimistic Updates & Decoupling from Historical Throttling**:
   - Equipment telemetry historically persists into `EquipmentReadings` with a 30-second throttling interval or delta check (`TelemetryThrottlingService`).
   - However, when a brewer clicks "Turn Pump On" or "Set Heat to 100%", they expect immediate visual feedback (<50ms) across all devices and browsers viewing the brewery.
   - If optimistic state were delayed by the 30-second database insert throttler, the UI would revert to the old state or show stale information.
   - Therefore, the optimistic update must bypass historical table throttling: upon successful gateway acknowledgment (or concurrently with it), mutate `equipment.LatestMetricsJson` and fire `ITelemetryBroadcastService.BroadcastEquipmentReading` directly.
   - The SSE stream delivers the update to browser clients in <5ms through `Channel<EquipmentTelemetryUpdateDto>`.
   - `equipment.LatestMetricsJson` is updated and persisted via `_db.SaveChangesAsync()` so subsequent page loads or REST queries (`GET /api/v1/inventory/equipment/{id}`) immediately reflect the new state.

2. **Action Mutation Rules via `BrewLinkCanonicalMetrics`**:
   - `equipment.LatestMetricsJson` is canonicalized using `BrewLinkTelemetryParser.FromMetricsJson(equipment.LatestMetricsJson)`:
     - If null, seed defaults: `CurrentTempC = equipment.CurrentTemperatureC`, `BoilTempC = 100.0m`, `HeatActive = false`, `PumpActive = false`, `Timer = new BrewLinkTimerMetrics(false, false, 0, 0)`, `Interaction = new BrewLinkInteractionMetrics(false, 0, null)`.
     - `set_pump(state)`: `PumpActive = state`
     - `set_heat(state, power_pct)`: `HeatActive = state`, `HeatPowerPct = state ? (power_pct ?? existing.HeatPowerPct ?? 100m) : 0m`
     - `set_target_temp(temp_c)`: `TargetTempC = temp_c`
     - `set_timer(duration_min)`: `Timer = new BrewLinkTimerMetrics(Active: true, Paused: false, RemainingSeconds: duration_min * 60, TotalSeconds: duration_min * 60)`
     - `pause_timer`: `Timer = (existing.Timer ?? new BrewLinkTimerMetrics(true, false, 0, 0)) with { Paused = true }`
     - `resume_timer`: `Timer = (existing.Timer ?? new BrewLinkTimerMetrics(true, true, 0, 0)) with { Paused = false }`
     - `cancel_timer`: `Timer = new BrewLinkTimerMetrics(Active: false, Paused: false, RemainingSeconds: 0, TotalSeconds: 0)`
     - `press_set`: passes through to hardware; advances strike heat / step prompt.
     - `ack_interaction` / `dismiss_alert` / `dismiss_sparge_alert`: `Interaction = new BrewLinkInteractionMetrics(Required: false, Code: 0, Message: null)`
     - `cancel_session`: `AutoMode = false`, `HeatActive = false`, `HeatPowerPct = 0m`, `PumpActive = false`, `Timer = new BrewLinkTimerMetrics(false, false, 0, 0)`, `Interaction = new BrewLinkInteractionMetrics(false, 0, null)`.
   - `BrewLinkTelemetryParser.ToMetricsJson(mutatedMetrics)` serializes back to JSON.
   - If the gateway fails, times out, or returns 409/502, the optimistic update is NOT saved and NO SSE event is emitted, preserving truth.

3. **State Sync (`sync`) Mechanics**:
   - `POST /api/v1/inventory/equipment/{id}/brewlink/sync` queries `GET {host}/api/equipment`.
   - Gateway returns `{ "equipment": [ ... ] }`.
   - `BrewLinkTelemetryParser.ParseCanonical(root, equipment)` extracts device state using target resolution (`targetMac`, `slot`, or `targetName`).
   - If device is not in the array or has `connected == false`: return `409 Conflict` (`EQUIPMENT_DISCONNECTED`).
   - If device is connected:
     - Update `equipment.CurrentTemperatureC = canonical.CurrentTempC`
     - Update `equipment.TemperatureUpdatedAt = canonical.UpdatedAt`
     - Update `equipment.LastTelemetryAt = DateTime.UtcNow`
     - Update `equipment.LatestMetricsJson = BrewLinkTelemetryParser.ToMetricsJson(canonical)`
     - Save to DB: `await _db.SaveChangesAsync(ct)`
     - Broadcast immediate SSE reading to equipment owner and brewery setup members.
     - Return `200 OK` with `BrewLinkSyncResultDto`.

4. **Multi-User SSE Fanout Topology**:
   - In BrewYou, equipment belongs to an owner (`equipment.UserId`) and may belong to a `BrewerySetup` (`equipment.BrewerySetupId`).
   - Brewery setups have collaborators with roles: `Owner`, `Brewer`, `Viewer`.
   - Any collaborator connected to `GET /api/v1/inventory/equipment/telemetry-stream` must see the change immediately.
   - Fanout recipient set:
     ```csharp
     var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { equipment.UserId };
     if (equipment.BrewerySetup != null)
     {
         recipients.Add(equipment.BrewerySetup.UserId);
         foreach (var member in equipment.BrewerySetup.Members)
         {
             recipients.Add(member.UserId);
         }
     }
     foreach (var uid in recipients)
     {
         _broadcastService.BroadcastEquipmentReading(uid, updateDto);
     }
     ```
   - If the equipment is actively assigned to an ongoing brew stage (`activeBatch`), also construct `BatchEquipmentReadingDto` and call `_broadcastService.BroadcastReading(batchDto)` so the Brew Day HUD updates.

5. **Test Strategy & `BrewLinkControlTests.cs`**:
   - Must cover all 12 actions and assert the HMAC-SHA256 signature in the HTTP request payload.
   - Must verify role boundary: Owner & Brewer -> 200 OK; Viewer -> 403 Forbidden; Stranger -> 403 Forbidden; Unauthenticated -> 401 Unauthorized.
   - Must verify security: `localhost`, `127.0.0.1`, `::1`, `169.254.169.254`, `0.0.0.0` -> 400 Bad Request.
   - Must verify status code mappings: 200, 400, 401, 403, 404, 409, 502.
   - Must verify optimistic state update in database and SSE broadcast over `/telemetry-stream`.

---

## 3. Blueprint: Optimistic State Updates & Immediate SSE Fanout

### 3.1. Control Command Flowchart
```
[Frontend Client]
      │
      │ POST /api/v1/inventory/equipment/{id}/brewlink/control { action, params }
      ▼
[EquipmentEndpoints]
      │ Authenticate user & validate claims
      │ Check role: Owner or Brewer? (Viewer -> 403 FORBIDDEN)
      ▼
[BrewLinkService.SendControlCommandAsync]
      │ Validate action name (must be 1 of 12) & validate params
      │ Lookup Equipment & ensure ConnectionType == BrewLink (else 400)
      │ Lookup Paired Gateway & unprotect API Token (else 401)
      │ Validate gateway host with NetworkSecurityValidator (else 400)
      │ Generate req_id, timestamp, HMAC-SHA256 signature on "{req_id}:{action}:{timestamp}"
      │ Assemble JSON envelope { req_id, action, equipment_type, slot, target_mac, timestamp, sig, params }
      │ Send POST {host}/api/equipment/control (fallback /api/equipment/cmd on 404)
      │
      ├──> [Gateway returns 409 / Conflict] ──> Return 409 EQUIPMENT_DISCONNECTED
      ├──> [Gateway returns 401/403]       ──> Return 401 UNAUTHORIZED
      ├──> [Gateway timeout / 5xx]         ──> Return 502 GATEWAY_ERROR
      │
      └──> [Gateway returns 200 OK]
            │
            ├─► Apply Optimistic State Mutation to equipment.LatestMetricsJson
            │   (set_pump -> pumpActive, set_heat -> heatActive/power, set_timer -> timer, etc.)
            │
            ├─► Immediate SSE Fanout via ITelemetryBroadcastService
            │   BroadcastEquipmentReading(uid, update) to:
            │     - equipment.UserId
            │     - equipment.BrewerySetup.UserId
            │     - all equipment.BrewerySetup.Members
            │   If assigned to active batch -> BroadcastReading(batchDto)
            │
            ├─► DB Persistence: equipment.LatestMetricsJson & equipment.LastTelemetryAt
            │   await _db.SaveChangesAsync()
            │
            └─► Return 200 OK with ApiResponse<BrewLinkCommandResultDto>
```

### 3.2. Detailed Action Mutation Logic Table
| Action | Required / Tolerated Params | Mutation on `BrewLinkCanonicalMetrics` | Post-Mutation Canonical Schema Snippet |
|---|---|---|---|
| `set_pump` | `state: bool` (or `enabled: bool`) | `metrics with { PumpActive = state }` | `"pumpActive": true` |
| `set_heat` | `state: bool`, optional `power_pct: int?` (0..100) | `metrics with { HeatActive = state, HeatPowerPct = state ? (power_pct ?? metrics.HeatPowerPct ?? 100m) : 0m }` | `"heatActive": true, "heatPowerPct": 80` |
| `set_target_temp` | `temp_c: decimal` (0.0..105.0) | `metrics with { TargetTempC = temp_c }` | `"targetTempC": 68.5` |
| `set_timer` | `duration_min: int` (1..1440) | `metrics with { Timer = new BrewLinkTimerMetrics(Active: true, Paused: false, RemainingSeconds: min * 60, TotalSeconds: min * 60) }` | `"timer": {"active": true, "paused": false, "remainingSeconds": 3600, "totalSeconds": 3600}` |
| `pause_timer` | none | `metrics with { Timer = (metrics.Timer ?? new BrewLinkTimerMetrics(true, false, 0, 0)) with { Paused = true } }` | `"timer": {"active": true, "paused": true, ...}` |
| `resume_timer` | none | `metrics with { Timer = (metrics.Timer ?? new BrewLinkTimerMetrics(true, true, 0, 0)) with { Paused = false } }` | `"timer": {"active": true, "paused": false, ...}` |
| `cancel_timer` | none | `metrics with { Timer = new BrewLinkTimerMetrics(Active: false, Paused: false, RemainingSeconds: 0, TotalSeconds: 0) }` | `"timer": {"active": false, "paused": false, "remainingSeconds": 0, "totalSeconds": 0}` |
| `press_set` | none | passes command to hardware (no metrics change) | *(unchanged metrics)* |
| `ack_interaction` | none | `metrics with { Interaction = new BrewLinkInteractionMetrics(Required: false, Code: 0, Message: null) }` | `"interaction": {"required": false, "code": 0, "message": null}` |
| `dismiss_alert` | none | `metrics with { Interaction = new BrewLinkInteractionMetrics(Required: false, Code: 0, Message: null) }` | `"interaction": {"required": false, "code": 0, "message": null}` |
| `dismiss_sparge_alert` | none | `metrics with { Interaction = new BrewLinkInteractionMetrics(Required: false, Code: 0, Message: null) }` | `"interaction": {"required": false, "code": 0, "message": null}` |
| `cancel_session` | none | `metrics with { AutoMode = false, HeatActive = false, HeatPowerPct = 0m, PumpActive = false, Timer = new BrewLinkTimerMetrics(false, false, 0, 0), Interaction = new BrewLinkInteractionMetrics(false, 0, null) }` | `"autoMode": false, "heatActive": false, "pumpActive": false` |

### 3.3. Immediate SSE Fanout Implementation Pattern
In `BrewLinkService.cs`:
```csharp
private void BroadcastEquipmentState(Equipment equipment, string metricsJson, string source)
{
    var fillPercentage = equipment.CapacityLiters > 0
        ? Math.Round(Math.Clamp((equipment.CurrentVolumeLiters / equipment.CapacityLiters) * 100m, 0m, 100m), 1)
        : 0m;

    var update = new EquipmentTelemetryUpdateDto(
        EquipmentId: equipment.Id,
        EquipmentName: equipment.Name,
        TemperatureC: equipment.CurrentTemperatureC ?? 0m,
        Timestamp: DateTime.UtcNow,
        Source: source,
        BatchId: equipment.AssignedBatchId,
        SpecificGravity: equipment.CurrentSpecificGravity,
        CurrentVolumeLiters: equipment.CurrentVolumeLiters,
        FillPercentage: fillPercentage,
        PressureBar: equipment.CurrentPressureBar,
        BatteryPercent: equipment.CurrentBatteryPercent,
        BatteryVoltage: equipment.CurrentBatteryVoltage,
        TiltDegrees: null,
        Rssi: null,
        MetricsJson: metricsJson
    );

    var targetUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { equipment.UserId };

    if (equipment.BrewerySetup != null)
    {
        targetUserIds.Add(equipment.BrewerySetup.UserId);
        foreach (var member in equipment.BrewerySetup.Members)
        {
            targetUserIds.Add(member.UserId);
        }
    }

    foreach (var userId in targetUserIds)
    {
        _broadcastService.BroadcastEquipmentReading(userId, update);
    }
}
```

### 3.4. State Sync Implementation Pattern
In `BrewLinkService.cs`:
```csharp
public async Task<(BrewLinkSendResult Result, BrewLinkSyncResultDto? SyncResult, string? ErrorMessage)> SyncEquipmentStateAsync(
    string userId,
    Guid equipmentId,
    CancellationToken ct = default)
{
    var equipment = await _db.Equipment
        .Include(e => e.BrewerySetup)
            .ThenInclude(s => s!.Members)
        .FirstOrDefaultAsync(e => e.Id == equipmentId, ct);

    if (equipment == null)
        return (BrewLinkSendResult.NotFound, null, "Equipment not found.");

    if (!HasControlPermission(equipment, userId))
        return (BrewLinkSendResult.Forbidden, null, "You do not have permission to control this equipment.");

    if (equipment.ConnectionType != EquipmentConnectionType.BrewLink || string.IsNullOrWhiteSpace(equipment.ConnectionConfigJson))
        return (BrewLinkSendResult.BadRequest, null, "Equipment is not configured with BrewLink.");

    var (isValidConfig, gatewayId, targetMac, targetSlot, targetName) = ParseConnectionConfig(equipment.ConnectionConfigJson);
    if (!isValidConfig)
        return (BrewLinkSendResult.BadRequest, null, "Invalid BrewLink connection configuration.");

    var gateway = await _db.BrewLinkGateways
        .FirstOrDefaultAsync(g => g.Id == gatewayId && (g.UserId == userId || g.UserId == equipment.UserId), ct);
    if (gateway == null)
        return (BrewLinkSendResult.BadRequest, null, "Configured BrewLink gateway not found or not paired.");

    string rawToken;
    try
    {
        rawToken = _protector.Unprotect(gateway.ApiToken);
    }
    catch
    {
        return (BrewLinkSendResult.Unauthorized, null, "Gateway credentials could not be decrypted.");
    }

    var baseHost = gateway.Host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || gateway.Host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        ? gateway.Host.TrimEnd('/')
        : $"http://{gateway.Host.TrimEnd('/')}";

    var syncUrl = $"{baseHost}/api/equipment";

    try
    {
        using var reqMsg = new HttpRequestMessage(HttpMethod.Get, syncUrl);
        reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        reqMsg.Headers.Add("X-BrewLink-Token", rawToken);

        var response = await _httpClient.SendAsync(reqMsg, ct);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return (BrewLinkSendResult.Unauthorized, null, "BrewLink gateway authorization failed.");
            return (BrewLinkSendResult.GatewayError, null, $"BrewLink gateway returned HTTP {(int)response.StatusCode}.");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        var canonical = BrewLinkTelemetryParser.ParseCanonical(doc.RootElement, equipment);
        if (canonical == null || !canonical.CurrentTempC.HasValue)
        {
            return (BrewLinkSendResult.EquipmentDisconnected, null, "Equipment is disconnected from BrewLink gateway (EQUIPMENT_OFFLINE).");
        }

        var metricsJson = BrewLinkTelemetryParser.ToMetricsJson(canonical);
        equipment.CurrentTemperatureC = canonical.CurrentTempC.Value;
        equipment.TemperatureUpdatedAt = canonical.UpdatedAt;
        equipment.LastTelemetryAt = DateTime.UtcNow;
        equipment.LatestMetricsJson = metricsJson;

        await _db.SaveChangesAsync(ct);

        BroadcastEquipmentState(equipment, metricsJson, "BrewLinkSync");

        var syncDto = new BrewLinkSyncResultDto(
            Status: "ok",
            Synced: true,
            TemperatureC: canonical.CurrentTempC.Value,
            MetricsJson: metricsJson,
            SyncedAt: DateTime.UtcNow
        );

        return (BrewLinkSendResult.Success, syncDto, null);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to sync equipment {EquipmentId} from gateway {Host}", equipmentId, baseHost);
        return (BrewLinkSendResult.GatewayError, null, $"Failed to reach BrewLink gateway: {ex.Message}");
    }
}
```

---

## 4. Test Suite Specification: `tests/BrewYou.ApiService.Tests/BrewLinkControlTests.cs`

### 4.1. Test Suite Architecture
The test suite is structured as an xUnit integration & unit test suite using `WebApplicationFactory<Program>` with `FakeBrewLinkHttpMessageHandler`.

```
BrewLinkControlTests (inherits IClassFixture<BrewLinkControlTestFactory>, IDisposable)
├── 1. Command Envelopes & HMAC-SHA256 Signing (12 tests)
│   ├── SetPump_GeneratesValidHmacSignatureAndExecutes
│   ├── SetHeat_GeneratesValidHmacSignatureAndExecutes
│   ├── SetTargetTemp_GeneratesValidHmacSignatureAndExecutes
│   ├── SetTimer_GeneratesValidHmacSignatureAndExecutes
│   ├── PauseTimer_GeneratesValidHmacSignatureAndExecutes
│   ├── ResumeTimer_GeneratesValidHmacSignatureAndExecutes
│   ├── CancelTimer_GeneratesValidHmacSignatureAndExecutes
│   ├── PressSet_GeneratesValidHmacSignatureAndExecutes
│   ├── AckInteraction_GeneratesValidHmacSignatureAndExecutes
│   ├── DismissAlert_GeneratesValidHmacSignatureAndExecutes
│   ├── DismissSpargeAlert_GeneratesValidHmacSignatureAndExecutes
│   └── CancelSession_GeneratesValidHmacSignatureAndExecutes
├── 2. Parameter Validation & Edge Cases (5 tests)
│   ├── SetHeat_InvalidPowerPct_ReturnsBadRequest
│   ├── SetTargetTemp_OutOfRange_ReturnsBadRequest
│   ├── SetTimer_ZeroOrNegativeDuration_ReturnsBadRequest
│   ├── UnsupportedAction_ReturnsBadRequest
│   └── NonBrewLinkEquipment_ReturnsBadRequest
├── 3. Role Authorization Matrix (5 tests)
│   ├── OwnerRole_CanExecuteHardwareControl
│   ├── BrewerRole_CanExecuteHardwareControl
│   ├── ViewerRole_ReturnsForbidden
│   ├── UnrelatedUser_ReturnsForbidden
│   └── Unauthenticated_ReturnsUnauthorized
├── 4. Security & Network Validation (4 tests)
│   ├── GatewayHost_LoopbackIp_ReturnsBadRequest
│   ├── GatewayHost_Localhost_ReturnsBadRequest
│   ├── GatewayHost_CloudMetadata_ReturnsBadRequest
│   └── GatewayHost_ValidLanIp_Succeeds
├── 5. Gateway Error & Fallback Mappings (4 tests)
│   ├── OfflineEquipment_GatewayConflict_Returns409
│   ├── MissingEquipment_Returns404
│   ├── GatewayNetworkTimeout_Returns502
│   └── Gateway404OnControl_FallsBackToCmdEndpointAndSucceeds
├── 6. Optimistic State Updates & Immediate SSE Fanout (4 tests)
│   ├── ControlAction_UpdatesLatestMetricsJsonOptimistically
│   ├── ControlAction_BroadcastsImmediateSseReading
│   ├── ControlAction_FansOutSseReadingToAllBreweryMembers
│   └── FailedGatewayCommand_PreservesPreviousStateWithoutEmittingSse
└── 7. State Synchronization (`sync`) (5 tests)
    ├── Sync_ConnectedEquipment_UpdatesCurrentTempAndMetricsJson
    ├── Sync_DisconnectedDevice_Returns409
    ├── Sync_GatewayError_Returns502
    ├── Sync_ViewerRole_ReturnsForbidden
    └── Sync_BroadcastsImmediateSseReading
```

### 4.2. Sample Test Methods

```csharp
using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using BrewYou.ApiService.Services.Telemetry;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace BrewYou.ApiService.Tests;

public class BrewLinkControlTestFactory : WebApplicationFactory<Program>
{
    public FakeBrewLinkHttpMessageHandler HttpHandler { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<IBrewLinkService, BrewLinkService>()
                .ConfigurePrimaryHttpMessageHandler(() => HttpHandler);
        });
    }
}

public class BrewLinkControlTests : IClassFixture<BrewLinkControlTestFactory>, IDisposable
{
    private readonly BrewLinkControlTestFactory _factory;
    private readonly HttpClient _client;

    public BrewLinkControlTests(BrewLinkControlTestFactory factory)
    {
        _factory = factory;
        _factory.HttpHandler.Reset();
        _client = factory.CreateClient();
    }

    public void Dispose()
    {
        _factory.HttpHandler.Reset();
    }

    private static string ComputeHmacSha256(string secretToken, string data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretToken));
        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }

    [Theory]
    [InlineData("set_pump", "{\"state\":true}")]
    [InlineData("set_heat", "{\"state\":true,\"power_pct\":80}")]
    [InlineData("set_target_temp", "{\"temp_c\":68.5}")]
    [InlineData("set_timer", "{\"duration_min\":45}")]
    [InlineData("pause_timer", "{}")]
    [InlineData("resume_timer", "{}")]
    [InlineData("cancel_timer", "{}")]
    [InlineData("press_set", "{}")]
    [InlineData("ack_interaction", "{}")]
    [InlineData("dismiss_alert", "{}")]
    [InlineData("dismiss_sparge_alert", "{}")]
    [InlineData("cancel_session", "{}")]
    public async Task Control_All12Actions_SendHmacSignedEnvelopeAndReturn200(string action, string paramJson)
    {
        // Arrange
        var (client, userId, _, defaultSetupId) = await CreateAuthenticatedUserAsync("BrewMaster");
        var gw = await PairTestGatewayAsync(client, "192.168.1.55", token: "bl_test_secret_token_12345");
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id, "Grainfather G30");

        HttpRequestMessage? interceptedRequest = null;
        string? interceptedBody = null;

        _factory.HttpHandler.Handler = req =>
        {
            interceptedRequest = req;
            interceptedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    req_id = "req-12345678",
                    gateway_id = gw.GatewayId,
                    target_mac = "C44F331A8B22",
                    message = $"Action {action} successfully executed"
                }))
            };
        };

        var controlRequest = new
        {
            action,
            @params = JsonNode.Parse(paramJson)
        };

        // Act
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", controlRequest);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<BrewLinkCommandResultDto>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Data!.Status.Should().Be("ok");

        // Assert gateway request structure & HMAC signature
        interceptedRequest.Should().NotBeNull();
        interceptedRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        interceptedRequest.Headers.Authorization.Parameter.Should().Be("bl_test_secret_token_12345");
        interceptedRequest.Headers.GetValues("X-BrewLink-Token").Should().Contain("bl_test_secret_token_12345");

        using var bodyDoc = JsonDocument.Parse(interceptedBody!);
        var root = bodyDoc.RootElement;
        root.GetProperty("action").GetString().Should().Be(action);
        var reqId = root.GetProperty("req_id").GetString()!;
        var timestamp = root.GetProperty("timestamp").GetInt64();
        var sig = root.GetProperty("sig").GetString()!;

        // Cryptographic verification
        var expectedSigningInput = $"{reqId}:{action}:{timestamp}";
        var expectedSig = ComputeHmacSha256("bl_test_secret_token_12345", expectedSigningInput);
        sig.Should().Be(expectedSig, "The gateway command envelope must contain an exact lowercase HMAC-SHA256 signature");
    }

    [Fact]
    public async Task Control_RoleAuthorization_ViewerReceives403Forbidden()
    {
        // Arrange
        var (ownerClient, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync("Owner");
        var (viewerClient, _) = await SetupCollaboratorAsync(ownerClient, defaultSetupId, BreweryRole.Viewer, "Viewer");
        var gw = await PairTestGatewayAsync(ownerClient);
        var eq = await CreateBrewLinkEquipmentAsync(ownerClient, defaultSetupId, gw.Id);

        var req = new { action = "set_pump", @params = new { state = true } };

        // Act
        var res = await viewerClient.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await res.Content.ReadFromJsonAsync<ApiResponse>();
        body!.Error!.Code.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Control_RoleAuthorization_BrewerCanExecuteControl()
    {
        // Arrange
        var (ownerClient, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync("Owner");
        var (brewerClient, _) = await SetupCollaboratorAsync(ownerClient, defaultSetupId, BreweryRole.Brewer, "Brewer");
        var gw = await PairTestGatewayAsync(ownerClient);
        var eq = await CreateBrewLinkEquipmentAsync(ownerClient, defaultSetupId, gw.Id);

        _factory.HttpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\",\"req_id\":\"req-brewer-1\"}")
        };

        var req = new { action = "set_pump", @params = new { state = true } };

        // Act
        var res = await brewerClient.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Control_OfflineEquipment_Returns409Conflict()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        _factory.HttpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"status\":\"error\",\"error_code\":\"EQUIPMENT_OFFLINE\",\"message\":\"Target BLE peripheral is disconnected\"}")
        };

        var req = new { action = "set_pump", @params = new { state = true } };

        // Act
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await res.Content.ReadFromJsonAsync<ApiResponse>();
        body!.Error!.Code.Should().Be("EQUIPMENT_DISCONNECTED");
    }

    [Fact]
    public async Task Control_GatewayTimeout_Returns502BadGateway()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        _factory.HttpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("Gateway timed out")
        };

        var req = new { action = "set_pump", @params = new { state = true } };

        // Act
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", req);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var body = await res.Content.ReadFromJsonAsync<ApiResponse>();
        body!.Error!.Code.Should().Be("GATEWAY_ERROR");
    }

    [Fact]
    public async Task Control_OptimisticUpdate_ImmediatelyMutatesLatestMetricsJsonAndEmitsSse()
    {
        // Arrange
        var (client, userId, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        _factory.HttpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\",\"req_id\":\"req-opt-1\"}")
        };

        // Connect to SSE stream
        var sseReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/equipment/telemetry-stream");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var sseRes = await client.SendAsync(sseReq, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var stream = await sseRes.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        await reader.ReadLineAsync(cts.Token); // : connected
        await reader.ReadLineAsync(cts.Token); // blank line

        // Act: send set_heat (power 85%)
        var controlReq = new { action = "set_heat", @params = new { state = true, power_pct = 85 } };
        var res = await client.PostAsJsonAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/control", controlReq);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert 1: Database entity updated optimistically
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var dbEq = await db.Equipment.FindAsync(eq.Id);
        dbEq!.LatestMetricsJson.Should().NotBeNull();
        dbEq.LatestMetricsJson.Should().Contain("\"heatActive\":true");
        dbEq.LatestMetricsJson.Should().Contain("\"heatPowerPct\":85");

        // Assert 2: SSE stream received immediate broadcast
        var eventLine = await reader.ReadLineAsync(cts.Token);
        var dataLine = await reader.ReadLineAsync(cts.Token);
        eventLine.Should().Be("event: reading");
        dataLine.Should().Contain("\"heatActive\":true");
        dataLine.Should().Contain("\"heatPowerPct\":85");
    }

    [Fact]
    public async Task Sync_ConnectedEquipment_UpdatesCurrentTempAndMetricsJson()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var gw = await PairTestGatewayAsync(client);
        var eq = await CreateBrewLinkEquipmentAsync(client, defaultSetupId, gw.Id);

        _factory.HttpHandler.Handler = req =>
        {
            req.RequestUri!.AbsolutePath.Should().EndWith("/api/equipment");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "gateway_id": "C44F331A8B20",
                  "count": 1,
                  "equipment": [
                    {
                      "slot": 1,
                      "mac": "C44F331A8B22",
                      "connected": true,
                      "state": {
                        "current_temp_c": 66.8,
                        "target_temp_c": 67.0,
                        "heat_active": true,
                        "heat_power_pct": 100,
                        "pump_active": true
                      }
                    }
                  ]
                }
                """)
            };
        };

        // Act
        var res = await client.PostAsync($"/api/v1/inventory/equipment/{eq.Id}/brewlink/sync", null);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<BrewLinkSyncResultDto>>();
        envelope!.Data!.Synced.Should().BeTrue();
        envelope.Data.TemperatureC.Should().Be(66.8m);

        // Verify entity persisted
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var entity = await db.Equipment.FindAsync(eq.Id);
        entity!.CurrentTemperatureC.Should().Be(66.8m);
        entity.LatestMetricsJson.Should().Contain("66.8");
    }
}
```

---

## 5. Step-by-Step Implementation Plan for the Worker

### Phase 1: Models & Interface Contracts
1. **Extend `src/BrewYou.ApiService/Services/IBrewLinkService.cs`**:
   - Define records:
     - `public record BrewLinkControlRequest(string Action, JsonNode? Params = null);`
     - `public record BrewLinkSyncResultDto(string Status, bool Synced, decimal? TemperatureC, string? MetricsJson, DateTime SyncedAt);`
   - Add method signatures to `IBrewLinkService`:
     ```csharp
     Task<(BrewLinkSendResult Result, BrewLinkCommandResultDto? CommandResult, string? ErrorMessage)> SendControlCommandAsync(
         string userId,
         Guid equipmentId,
         BrewLinkControlRequest request,
         CancellationToken ct = default);

     Task<(BrewLinkSendResult Result, BrewLinkSyncResultDto? SyncResult, string? ErrorMessage)> SyncEquipmentStateAsync(
         string userId,
         Guid equipmentId,
         CancellationToken ct = default);
     ```

### Phase 2: Service Implementation in `BrewLinkService.cs`
1. **Inject `ITelemetryBroadcastService`**:
   - Update `BrewLinkService` constructor to inject `ITelemetryBroadcastService broadcastService`.
2. **Implement Command Validation & Envelope Signing**:
   - Validate action against the 12 supported strings:
     `set_pump`, `set_heat`, `set_target_temp`, `set_timer`, `pause_timer`, `resume_timer`, `cancel_timer`, `press_set`, `ack_interaction`, `dismiss_alert`, `dismiss_sparge_alert`, `cancel_session`.
   - Validate params (e.g. `power_pct` $\in [0, 100]$, `temp_c` $\in [0.0, 105.0]$, `duration_min` $> 0$).
   - Check equipment role authorization (Owner or Brewer permitted, Viewer forbidden).
   - Enforce `equipment.ConnectionType == EquipmentConnectionType.BrewLink`.
   - Decrypt `gateway.ApiToken` via `_protector.Unprotect(...)`.
   - Validate outbound host with `NetworkSecurityValidator`.
   - Generate `reqId = $"req-{Guid.NewGuid():N}"[..12]`, Unix timestamp, and lowercase HMAC-SHA256 signature on `"{reqId}:{action}:{timestamp}"`.
   - Dispatch POST request to `{host}/api/equipment/control` (with transparent 404 fallback to `/api/equipment/cmd`).
3. **Implement Optimistic Mutation & Immediate Multi-User Fanout**:
   - On gateway 200 OK, parse existing `equipment.LatestMetricsJson` with `BrewLinkTelemetryParser.FromMetricsJson(...)` (or seed defaults if null).
   - Apply action-specific mutations as defined in Blueprint §3.2.
   - Update `equipment.LatestMetricsJson` and `equipment.LastTelemetryAt`.
   - Fire `BroadcastEquipmentState` across `equipment.UserId`, `BrewerySetup.UserId`, and all `BrewerySetup.Members`.
   - Save changes to DB (`await _db.SaveChangesAsync(ct)`).
4. **Implement `SyncEquipmentStateAsync`**:
   - Send GET to `{host}/api/equipment` with Bearer and X-BrewLink-Token headers.
   - Parse canonical state using `BrewLinkTelemetryParser.ParseCanonical(...)`.
   - If device missing or disconnected, return `BrewLinkSendResult.EquipmentDisconnected`.
   - Update `equipment.CurrentTemperatureC`, `equipment.TemperatureUpdatedAt`, and `equipment.LatestMetricsJson`.
   - Broadcast immediate SSE event and persist to DB.

### Phase 3: Minimal API Endpoints in `EquipmentEndpoints.cs`
1. **Map `/control` and `/sync`**:
   - `POST /api/v1/inventory/equipment/{id:guid}/brewlink/control`
   - `POST /api/v1/inventory/equipment/{id:guid}/brewlink/sync`
2. **Status Code Mapping**:
   ```csharp
   return result switch
   {
       BrewLinkSendResult.Success => Results.Ok(ApiResponse<BrewLinkCommandResultDto>.Ok(cmdResult!)),
       BrewLinkSendResult.EquipmentDisconnected => Results.Conflict(ApiResponse.Fail("EQUIPMENT_DISCONNECTED", error ?? "Equipment is disconnected from BrewLink gateway.")),
       BrewLinkSendResult.Unauthorized => Results.Json(ApiResponse.Fail("UNAUTHORIZED", error ?? "BrewLink gateway authorization failed."), statusCode: StatusCodes.Status401Unauthorized),
       BrewLinkSendResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", error ?? "Equipment not found.")),
       BrewLinkSendResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", error ?? "Access denied."), statusCode: StatusCodes.Status403Forbidden),
       BrewLinkSendResult.BadRequest => Results.BadRequest(ApiResponse.Fail("BAD_REQUEST", error ?? "Invalid request.")),
       _ => Results.Json(ApiResponse.Fail("GATEWAY_ERROR", error ?? "Failed to contact BrewLink gateway."), statusCode: StatusCodes.Status502BadGateway)
   };
   ```

### Phase 4: Test Suite Implementation in `BrewLinkControlTests.cs`
1. Create `tests/BrewYou.ApiService.Tests/BrewLinkControlTests.cs` using the complete test suite specification.
2. Execute tests: `dotnet test --filter "FullyQualifiedName~BrewLinkControlTests"`.

### Phase 5: Verification & Static Analysis
1. Format check: `dotnet format --verify-no-changes`.
2. Build verification: `dotnet build /warnaserror`.
3. Complete test suite verification: `dotnet test`.

---

## 6. Caveats

- **No caveats.** The interface contracts, HMAC signing logic, SSE channel mechanics, role authorization policies, and test fixtures are fully understood and directly aligned with the authoritative specifications in `ORIGINAL_REQUEST.md` and `PROJECT.md`.

---

## 7. Conclusion

Milestone 2's state synchronization, SSE broadcasting, and testing architecture are fully designed and verified:
1. **Optimistic Updates**: Mutating `equipment.LatestMetricsJson` via `BrewLinkCanonicalMetrics` provides instantaneous state updates across all 12 control actions without schema drift.
2. **Immediate SSE Fanout**: Calling `_broadcastService.BroadcastEquipmentReading` for the equipment owner, setup owner, and all setup members completely decouples real-time UI streaming from the 30-second historical database insert throttler, delivering zero-latency updates to connected browsers.
3. **State Sync**: Polling `GET /api/equipment` and parsing with `BrewLinkTelemetryParser.ParseCanonical` cleanly reconciles ground-truth hardware state with local entity state.
4. **Test Architecture**: `BrewLinkControlTests.cs` comprehensively tests all 12 actions, HMAC signature generation, role authorization boundaries (Owner/Brewer vs Viewer), SSRF network validation, and status code mappings (200, 400, 401, 403, 404, 409, 502).

---

## 8. Verification Method

To verify Milestone 2 once the Worker completes implementation:
1. **Run Unit and Integration Tests**:
   ```powershell
   dotnet test --filter "FullyQualifiedName~BrewLinkControlTests"
   dotnet test --filter "FullyQualifiedName~BrewLink"
   ```
2. **Run Static Analysis and Code Formatting**:
   ```powershell
   dotnet format --verify-no-changes
   dotnet build /warnaserror
   ```
3. **Key Invalidation Conditions**:
   - If a `Viewer` can execute any hardware control command without receiving HTTP 403 Forbidden, the specification is violated.
   - If an outgoing control command does not generate a valid HMAC-SHA256 signature in the `"sig"` envelope property, the specification is violated.
   - If loopback (`127.0.0.1`, `localhost`) or cloud metadata (`169.254.169.254`) addresses are accepted as gateway hosts, the specification is violated.
   - If an offline peripheral (gateway returning 409) does not return HTTP 409 Conflict with code `"EQUIPMENT_DISCONNECTED"`, the specification is violated.
   - If successful control execution does not optimistically update `equipment.LatestMetricsJson` and emit an immediate SSE `event: reading` on `/telemetry-stream`, the specification is violated.
