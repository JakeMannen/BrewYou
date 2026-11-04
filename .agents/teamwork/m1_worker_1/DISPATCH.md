## 2026-11-04T08:22:51Z

You are teamwork_worker instance 1 implementing Milestone 1: Equipment Cascading Integrity & Sensor Decoupling.
Working directory: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_worker_1\

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

MANDATORY: Read ORIGINAL_REQUEST.md first at:
c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\ORIGINAL_REQUEST.md
Also read PROJECT.md at:
c:\Users\jocke\AgentWorkspaces\BrewYou\PROJECT.md
Read the 3 M1 Explorer reports:
1. Backend Domain & Services: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_explorer_1\handoff.md
2. Frontend UI & SvelteKit: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_explorer_2\handoff.md
3. Test Specifications: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_explorer_3\handoff.md

Armed Skills:
- build-and-test: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\skills\build-and-test\SKILL.md
- api-contract-sync: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\skills\api-contract-sync\SKILL.md

Exclusive Write Ownership:
- Backend:
  * src/BrewYou.ApiService/Services/IEquipmentService.cs
  * src/BrewYou.ApiService/Services/EquipmentService.cs
  * src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs
  * src/BrewYou.ApiService/Services/IBrewerySetupService.cs
  * src/BrewYou.ApiService/Services/BrewerySetupService.cs
  * src/BrewYou.ApiService/Endpoints/BrewerySetupEndpoints.cs
  * tests/BrewYou.ApiService.Tests/EquipmentCascadingTests.cs
  * tests/BrewYou.ApiService.Tests/EquipmentEndpointTests.cs
  * tests/BrewYou.ApiService.Tests/BrewerySetupEndpointTests.cs
- Frontend:
  * src/BrewYou.Web/src/lib/types/api.ts
  * src/BrewYou.Web/src/lib/api/client.ts
  * src/BrewYou.Web/src/lib/components/inventory/DeleteEquipmentModal.svelte
  * src/BrewYou.Web/src/routes/batches/[id]/+page.svelte
  * src/BrewYou.Web/src/lib/components/batches/StepTemperatureChart.svelte
  * src/BrewYou.Web/src/lib/components/batches/LogStepTemperatureModal.svelte
  * src/BrewYou.Web/src/routes/inventory/equipment/+page.svelte
  * src/BrewYou.Web/src/lib/components/batches/StageMeasurementsCard.svelte
  * src/BrewYou.Web/src/lib/i18n/locales/en.json
  * src/BrewYou.Web/src/lib/i18n/locales/sv.json

Your tasks:
1. Implement Backend Changes:
   - In `EquipmentService.cs`, decouple all 5 batch vessels (`BoilerId`, `FermenterId`, `PackagingVesselId`, `HltId`, `MashTunId`).
   - Pre-clean `BatchSensorAssignments` for `equipmentId` prior to entity removal.
   - Decouple `AssignedKegId` on Taps and clear tap batch links.
   - Add active batch guard in `DeleteEquipmentAsync` returning `EquipmentAccessResult.Conflict` (`409 Conflict`, `EQUIPMENT_IN_USE`) unless `force == true`.
   - Update `GetActiveBatchesForEquipmentAsync` to check all 5 vessels and sensors.
   - In `BrewerySetupService.cs`, pre-clean `BatchSensorAssignments` for all equipment in setup prior to removing setup.
   - Wrap `SaveChangesAsync()` in `try/catch (DbUpdateException)` returning `Conflict`.
   - Update endpoints in `EquipmentEndpoints.cs` and `BrewerySetupEndpoints.cs`.

2. Implement Frontend Changes:
   - Update `api.equipment.delete(id, force = false)` in `client.ts` and `EquipmentActiveBatchDto` in `types/api.ts`.
   - In `DeleteEquipmentModal.svelte`: handle 409 conflict, active batch usage/role badges, force delete toggle.
   - In `batches/[id]/+page.svelte`: remove arbitrary `equipmentList[0]` fallback, pass `currentStageEquipmentName`.
   - In `StepTemperatureChart.svelte` and `LogStepTemperatureModal.svelte`: render unassigned placeholder and eliminate arbitrary defaults.
   - In `inventory/equipment/+page.svelte`: clear `assignedKegId` and `assignedKeg` on Tap cards upon keg deletion.
   - In `StageMeasurementsCard.svelte`: safely handle deleted vessel in `<select>` and submit.
   - In `en.json` and `sv.json`: add all new keys with 100% key parity.

3. Implement Automated Tests:
   - Create `tests/BrewYou.ApiService.Tests/EquipmentCascadingTests.cs` per `m1_explorer_3` specifications.
   - Update `EquipmentEndpointTests.cs` and `BrewerySetupEndpointTests.cs` to pass `?force=true` where appropriate and assert 409 conflict when unforced.

4. Run Builds and Tests:
   - Run `dotnet build` and `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~Equipment|FullyQualifiedName~BrewerySetup"`.
   - In `src/BrewYou.Web`: run `npm run check` and `npm run test:unit`.
   - Ensure all tests pass with zero warnings and zero errors.

5. Write detailed handoff report to:
c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_worker_1\handoff.md
Update progress.md in your working directory.
When finished, send a completion message back.
