## 2026-11-07T21:57:33Z
You are the Implementation Worker for Milestone 3: Equipment Model Expansion, Backup Compatibility & Documentation in BrewYou.

## Working Directory
`c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m3_worker_1\`

## Authoritative User Request
Read `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\ORIGINAL_REQUEST.md` before starting work.

## MANDATORY INTEGRITY WARNING
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

## Objectives
1. **Equipment Validation Expansion (R3)**:
   In `src/BrewYou.ApiService/Validation/Validators.cs`:
   - Update `CreateEquipmentRequestValidator` and `UpdateEquipmentRequestValidator` to allow `ConnectionType = EquipmentConnectionType.BrewLink` on ALL `EquipmentType.Boiler` and `EquipmentType.Fermenter` subtypes (e.g. `x.Type == EquipmentType.Boiler || x.Type == EquipmentType.Fermenter`).
   - Update the validation error message to: `"BrewLink connectivity is only supported on Boilers and Fermenters."` (or equivalent).
2. **Recipe Upload Scope Restriction Preservation (R3)**:
   In `src/BrewYou.ApiService/Services/BrewLinkService.cs`:
   - Ensure recipe uploads (`UploadRecipeAsync`) remain strictly restricted to `Boiler` with `AllInOne` subtype:
     `if (equipment.Type != EquipmentType.Boiler || equipment.Subtype != EquipmentSubtype.AllInOne || equipment.ConnectionType != EquipmentConnectionType.BrewLink)`
     returning `BadRequest` with appropriate message.
3. **User Backup & Restore Backwards Compatibility & Test (R5, AGENTS.md 3.J)**:
   In `tests/BrewYou.ApiService.Tests/BackupEndpointsTests.cs`:
   - Add a test verifying that equipment with `ConnectionType.BrewLink` on Boilers and Fermenters exports cleanly to backup DTOs, round-trips correctly, and restores onto existing profiles without error.
   - Verify that legacy backup schemas without BrewLink equipment restore without regressions.
4. **Root README.md Update (R5, AGENTS.md 3.I)**:
   In `README.md`:
   - Document the new endpoints:
     - `POST /api/v1/inventory/equipment/{id}/brewlink/control`
     - `POST /api/v1/inventory/equipment/{id}/brewlink/sync`
   - Document the real-time telemetry streaming (`GET /api/v1/inventory/equipment/telemetry-stream` SSE).
   - Document the expanded hardware control capabilities (12 signed actions, SSRF protection, RBAC).
   - Document that BrewLink connectivity is supported on all Boilers and Fermenters, while recipe upload is restricted to All-in-One Boilers.

## Write Ownership
You have exclusive write ownership of:
- `src/BrewYou.ApiService/Validation/Validators.cs`
- `src/BrewYou.ApiService/Services/BrewLinkService.cs`
- `tests/BrewYou.ApiService.Tests/BackupEndpointsTests.cs`
- `README.md`

## Verification Commands
1. `dotnet format BrewYou.slnx --verify-no-changes`
2. `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BackupEndpointsTests`
3. `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLink`
4. `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1` (Full backend test suite)

Write your report to `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m3_worker_1\handoff.md` and send a message when done.
