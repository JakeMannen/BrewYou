# BRIEFING — 2026-11-04T09:19:30Z

## Mission
Provide the definitive technical implementation plan for Backend Domain Services and EF Core for Milestone 1 (Equipment Cascading Integrity & Sensor Decoupling).

## 🔒 My Identity
- Archetype: teamwork_explorer
- Roles: explorer, technical investigator, backend architect
- Working directory: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_explorer_1\
- Original parent: d80eeb54-d338-4b53-876e-3933dc2f1932
- Milestone: Milestone 1 (Equipment Cascading Integrity & Sensor Decoupling)

## 🔒 Key Constraints
- Read-only investigation — do NOT implement
- All file operations must be in .agents/teamwork/m1_explorer_1/ or read-only across workspace
- Complete evidence chain and 5-component handoff report

## Current Parent
- Conversation ID: d80eeb54-d338-4b53-876e-3933dc2f1932
- Updated: not yet

## Investigation State
- **Explored paths**:
  - `src/BrewYou.ApiService/Data/BrewYouDbContext.cs`: lines 542-563 (`BatchSensorAssignment` with `DeleteBehavior.Restrict`), lines 202-250 (`Equipment`), lines 334-409 (`Batch`).
  - `src/BrewYou.ApiService/Services/EquipmentService.cs`: lines 405-479 (`DeleteEquipmentAsync`, `GetActiveBatchesForEquipmentAsync`), lines 800-990 (`AssignTapAsync`, `AssignKegBatchAsync`).
  - `src/BrewYou.ApiService/Services/BrewerySetupService.cs`: lines 300-341 (`DeleteSetupAsync`).
  - `src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs` & `BrewerySetupEndpoints.cs`.
  - `tests/BrewYou.ApiService.Tests/EquipmentEndpointTests.cs` (line 710 test) and `BrewerySetupEndpointTests.cs`.
- **Key findings**:
  1. `BatchSensorAssignment.EquipmentId` has non-nullable foreign key with `DeleteBehavior.Restrict`. Deleting an equipment used as a sensor fails with PostgreSQL error 23503 (`DbUpdateException`) causing HTTP 500.
  2. `EquipmentService.DeleteEquipmentAsync` currently only decouples `BoilerId`, `FermenterId`, `PackagingVesselId`, leaving `HltId` and `MashTunId` untouched.
  3. Taps referencing deleted kegs via `AssignedKegId` are not cleared on equipment deletion.
  4. Deleting a tap/keg assigned to a batch does not clear the batch assignment prior to removal.
  5. `DeleteEquipmentAsync` has no active batch guard, allowing deletion of vessels mid-brew without warning or force parameter.
  6. `GetActiveBatchesForEquipmentAsync` omits `HltId`, `MashTunId`, sensor assignments, and direct batch assignments.
  7. `BrewerySetupService.DeleteSetupAsync` deletes setup with cascading equipment deletion, which crashes against `BatchSensorAssignment` FK Restrict.
  8. `SaveChangesAsync()` is uncaught in both deletion flows, turning constraint conflicts into 500 Internal Server Errors.
- **Unexplored areas**: None for Milestone 1 backend domain services and EF Core.

## Key Decisions Made
- Architecture adopts Clean Service-Level Decoupling in domain services (`EquipmentService` and `BrewerySetupService`) so database schema FK Restrict remains an invariant safety net.
- Add `Conflict` to `EquipmentAccessResult` and `BrewerySetupAccessResult`.
- Update `DeleteEquipmentAsync` to take `bool force = false` and reject active batch equipment with `Conflict` (`409 Conflict` `EQUIPMENT_IN_USE`) unless `force = true`.
- Pre-clean `BatchSensorAssignments` in both `EquipmentService.DeleteEquipmentAsync` and `BrewerySetupService.DeleteSetupAsync`.
- Decouple all 5 vessels (`BoilerId`, `FermenterId`, `PackagingVesselId`, `HltId`, `MashTunId`) in both services.
- Decouple `AssignedKegId` from any taps referencing the deleted keg.
- Wrap `SaveChangesAsync()` in both services to catch `DbUpdateException` and return `Conflict` instead of unhandled 500s.

## Artifact Index
- DISPATCH.md — Log of incoming dispatches
- progress.md — Liveness heartbeat and task progress
- BRIEFING.md — Persistent situational awareness memory
- handoff.md — Definitive 5-component technical implementation plan for Milestone 1 backend
