## 2026-11-07T21:00:47Z
You are Explorer 1 for Milestone 2: Hardware Control API, State Sync & Security in BrewYou.

## Working Directory
`c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_exp_1\`

## Authoritative User Request
Read `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\ORIGINAL_REQUEST.md` before starting work.

## Context & Inputs
- Project specification: `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\orchestrator_1\PROJECT.md`
- Survey report: `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\survey_spec_miner_1\handoff.md`
- Existing endpoints: `src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs`
- Existing service: `src/BrewYou.ApiService/Services/BrewLinkService.cs`

## Investigation Scope
Investigate the endpoint architecture and DTO modeling for Milestone 2:
1. `POST /api/v1/inventory/equipment/{id}/brewlink/control`:
   - Endpoint routing, parameters, and action validation.
   - DTOs needed (`BrewLinkControlRequest`, action parameter DTOs/dictionaries, `BrewLinkCommandResultDto`).
   - Authorization rules: Allow Owner and Brewer roles; reject Viewer with HTTP 403 Forbidden.
   - Status code mappings: 200 (Success), 400 (Invalid action/params), 401 (Unauthenticated), 403 (Unauthorized/Viewer role), 404 (Equipment not found), 409 (Equipment offline / not connected / not BrewLink), 502 (Gateway unreachable / network error).
2. `POST /api/v1/inventory/equipment/{id}/brewlink/sync`:
   - Endpoint routing, parameters, response DTO (`BrewLinkSyncResultDto`).
   - Triggering full gateway sync and returning current equipment state.

## Output
Write a structured report to `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_exp_1\handoff.md` with:
- Exact method signatures, route definitions, and DTO definitions.
- Step-by-step implementation plan for the Worker.
Send a completion message back to the orchestrator.
