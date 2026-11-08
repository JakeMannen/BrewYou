## 2026-11-07T21:00:54Z

You are Explorer 2 for Milestone 2: Hardware Control API, State Sync & Security in BrewYou.

## Working Directory
`c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_exp_2\`

## Authoritative User Request
Read `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\ORIGINAL_REQUEST.md` before starting work.

## Context & Inputs
- Project specification: `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\orchestrator_1\PROJECT.md`
- Survey report: `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\survey_spec_miner_1\handoff.md`
- Existing service: `src/BrewYou.ApiService/Services/BrewLinkService.cs`
- Network security validator: `src/BrewYou.ApiService/Security/NetworkSecurityValidator.cs`

## Investigation Scope
Investigate the Gateway transport layer and HMAC signing in `BrewLinkService.cs`:
1. Envelope construction & HMAC-SHA256 signing:
   - Envelope fields: `req_id`, `action`, `equipment_type`, `slot`, `target_name`, `target_mac`, `timestamp`, `sig`, `params`.
   - Signature algorithm: lowercase hex string of HMAC-SHA256 computed over `"{req_id}:{action}:{timestamp}"` using the equipment's `api_token` (as UTF-8 key bytes).
2. Gateway HTTP communication:
   - Primary endpoint: `POST http://<gateway_ip>:<port>/api/equipment/control`
   - Fallback legacy endpoint: `POST http://<gateway_ip>:<port>/api/equipment/cmd`
   - Headers: `Authorization: Bearer <api_token>` and `X-BrewLink-Token: <api_token>`
   - Sync endpoint: `GET http://<gateway_ip>:<port>/api/equipment`
3. Network security validation:
   - Validate gateway IP/hostname using `NetworkSecurityValidator.ValidateHost` before initiating HTTP client calls to prevent SSRF against internal/forbidden subnets.
4. All 12 supported commands:
   - `set_pump` (`enabled`: bool)
   - `set_heat` (`enabled`: bool, `power`: int? / `power_pct`: int?)
   - `set_target_temp` (`temp`: decimal / `target_temp`: decimal)
   - `set_timer` (`seconds`: int / `duration`: int)
   - `pause_timer`
   - `resume_timer`
   - `cancel_timer`
   - `press_set`
   - `ack_interaction` (`id`: string?, `choice`: string?)
   - `dismiss_alert` (`id`: string?)
   - `dismiss_sparge_alert`
   - `cancel_session`

## Output
Write a structured report to `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_exp_2\handoff.md` with:
- Exact signing implementation and HTTP request/fallback logic.
- Clear error handling for gateway timeouts, network exceptions, and error response codes.
- Step-by-step implementation plan for the Worker.
Send a completion message back to the orchestrator.
