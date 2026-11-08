## 2026-11-07T21:08:58Z
You are the Implementation Worker for Milestone 2: Hardware Control API, State Sync & Security in BrewYou.

## Working Directory
`c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_worker_1\`

## Authoritative User Request
Read `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\ORIGINAL_REQUEST.md` before starting work.

## MANDATORY INTEGRITY WARNING
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

## Objective
Implement real-time hardware control and on-demand state synchronization for BrewLink-connected equipment:
1. `POST /api/v1/inventory/equipment/{id}/brewlink/control`:
   - Validates all 12 supported commands (`set_pump`, `set_heat`, `set_target_temp`, `set_timer`, `pause_timer`, `resume_timer`, `cancel_timer`, `press_set`, `ack_interaction`, `dismiss_alert`, `dismiss_sparge_alert`, `cancel_session`).
   - Generates signed HMAC-SHA256 envelopes using `api_token` hashed over `"{req_id}:{action}:{timestamp}"` (64 lowercase hex chars).
   - Validates gateway host via `NetworkSecurityValidator` (permits local LAN, rejects loopback/cloud metadata).
   - Transmits HTTP POST to primary `/api/equipment/control` with fallback to legacy `/api/equipment/cmd` on 404.
   - Dual headers: `Authorization: Bearer <token>` and `X-BrewLink-Token: <token>`.
   - Role authorization: Owner and Brewer permitted; Viewer rejected with HTTP 403 Forbidden.
   - Appropriate status codes: 200 (Success), 400 (Invalid action/params), 401 (Unauthenticated), 403 (Forbidden), 404 (Not found), 409 (Offline/not BrewLink), 502 (Gateway unreachable).
   - Optimistic state update: Immediately mutates `Equipment.LatestMetricsJson` and broadcasts live SSE telemetry via `ITelemetryBroadcastService.BroadcastEquipmentReading`.
2. `POST /api/v1/inventory/equipment/{id}/brewlink/sync`:
   - Fetches live gateway state (`GET /api/equipment`), normalizes via `BrewLinkTelemetryParser`, updates `Equipment` record (`LatestMetricsJson`, `CurrentTemperatureC`), and broadcasts live SSE update.
3. Unit & Integration Tests:
   - Create `tests/BrewYou.ApiService.Tests/BrewLinkControlTests.cs` verifying all 12 actions, signature verification, Viewer 403 Forbidden rejection, invalid host rejection, offline 409 Conflict, and sync state updates.

## Write Ownership
You have exclusive write ownership of:
- `src/BrewYou.ApiService/Services/IBrewLinkService.cs`
- `src/BrewYou.ApiService/Services/BrewLinkService.cs`
- `src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs`
- `src/BrewYou.ApiService/DTOs/` or `src/BrewYou.ApiService/Models/` (new DTOs if placed in separate file)
- `tests/BrewYou.ApiService.Tests/BrewLinkControlTests.cs`

## Blueprint Sources
Read the 3 detailed Explorer handoffs:
1. `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_exp_1\handoff.md` (Endpoints & DTOs)
2. `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_exp_2\handoff.md` (Gateway Transport & HMAC Signing)
3. `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_exp_3\handoff.md` (Optimistic State, SSE Fanout & Tests)
And read `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\orchestrator_1\PROJECT.md`.
