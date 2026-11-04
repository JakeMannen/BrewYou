## 2026-11-04T08:08:21Z
You are teamwork_explorer instance 1 for Milestone 1 (Equipment Cascading Integrity & Sensor Decoupling).
Working directory: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_explorer_1\

MANDATORY: Read ORIGINAL_REQUEST.md first at:
c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\ORIGINAL_REQUEST.md
Also read PROJECT.md at:
c:\Users\jocke\AgentWorkspaces\BrewYou\PROJECT.md
And investigation report at:
c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\investigation_report.md

Your mission:
Provide the definitive technical implementation plan for Backend Domain Services and EF Core for Milestone 1:
1. Inspect `src/BrewYou.ApiService/Services/EquipmentService.cs` (especially `DeleteEquipmentAsync` and `GetActiveBatchesForEquipmentAsync`).
2. Inspect `src/BrewYou.ApiService/Services/BrewerySetupService.cs` (`DeleteSetupAsync`).
3. Inspect `src/BrewYou.ApiService/Data/BrewYouDbContext.cs` (`BatchSensorAssignment` and foreign keys).
4. Detail the exact, line-by-line implementation strategy for:
   - Decoupling all 5 batch vessels (`BoilerId`, `FermenterId`, `PackagingVesselId`, `HltId`, `MashTunId`) on equipment delete.
   - Decoupling or deleting `BatchSensorAssignments` referencing the equipment before entity removal to prevent FK 23503 `DbUpdateException`.
   - Decoupling `AssignedKegId` on any taps referencing the deleted keg.
   - Decoupling tap assignments from batches (`equipment.AssignedBatchId != null`).
   - Wrapping `SaveChangesAsync()` in try/catch to return clean domain errors instead of 500s.
   - Updating `GetActiveBatchesForEquipmentAsync` to check HLT, Mash Tun, and sensor assignments.
   - Returning `409 Conflict` (`EQUIPMENT_IN_USE`) if equipment is assigned to active batches.
   - Pre-cleaning `BatchSensorAssignments` in `BrewerySetupService.DeleteSetupAsync`.
5. Write your detailed handoff report with exact code proposals to:
c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_explorer_1\handoff.md
Update progress.md in your working directory.
When finished, send a completion message back.
