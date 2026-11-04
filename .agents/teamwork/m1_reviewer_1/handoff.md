# Milestone 1: Equipment Cascading Integrity & Sensor Decoupling — Review & Adversarial Challenge Report

**Reviewer**: `m1_reviewer_1` (Teamwork Reviewer & Adversarial Critic Instance 1)  
**Roles**: reviewer, critic  
**Target Milestone**: Milestone 1: Equipment Cascading Integrity & Sensor Decoupling  
**Reviewed Artifacts**:
- `src/BrewYou.ApiService/Services/IEquipmentService.cs`
- `src/BrewYou.ApiService/Services/EquipmentService.cs`
- `src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs`
- `src/BrewYou.ApiService/Services/IBrewerySetupService.cs`
- `src/BrewYou.ApiService/Services/BrewerySetupService.cs`
- `src/BrewYou.ApiService/Endpoints/BrewerySetupEndpoints.cs`
- `tests/BrewYou.ApiService.Tests/EquipmentCascadingTests.cs`
- `tests/BrewYou.ApiService.Tests/EquipmentEndpointTests.cs`
- `tests/BrewYou.ApiService.Tests/BrewerySetupEndpointTests.cs`
- Worker Handoff: `.agents/teamwork/m1_worker_1/handoff.md`

**Final Verdict**: **APPROVE**

---

## 1. Observation

Direct examination of the codebase, entity relationships, endpoints, and test execution verified the following concrete observations:

### A. Decoupling of All 5 Batch Vessels
- In `src/BrewYou.ApiService/Services/EquipmentService.cs:451-468`:
  ```csharp
  var batchesWithEquipment = await _db.Batches
      .Where(b => b.BoilerId == id ||
                  b.FermenterId == id ||
                  b.PackagingVesselId == id ||
                  b.HltId == id ||
                  b.MashTunId == id)
      .ToListAsync();

  foreach (var b in batchesWithEquipment)
  {
      if (b.BoilerId == id) b.BoilerId = null;
      if (b.FermenterId == id) b.FermenterId = null;
      if (b.PackagingVesselId == id) b.PackagingVesselId = null;
      if (b.HltId == id) b.HltId = null;
      if (b.MashTunId == id) b.MashTunId = null;
      b.UpdatedAt = DateTime.UtcNow;
  }
  ```
  All 5 vessel slots (`BoilerId`, `FermenterId`, `PackagingVesselId`, `HltId`, `MashTunId`) are queried and cleared to `null`.
- In `src/BrewYou.ApiService/Services/BrewerySetupService.cs:338-356`, setup deletion similarly queries and decouples all 5 vessel foreign keys across all equipment belonging to the deleted setup.

### B. Pre-cleaning `BatchSensorAssignments` on Equipment & Setup Deletion
- PostgreSQL model constraint in `src/BrewYou.ApiService/Data/BrewYouDbContext.cs:552-556` enforces `DeleteBehavior.Restrict` on `sa.EquipmentId`.
- In `src/BrewYou.ApiService/Services/EquipmentService.cs:470-478`:
  ```csharp
  var sensorAssignments = await _db.BatchSensorAssignments
      .Where(sa => sa.EquipmentId == id)
      .ToListAsync();

  if (sensorAssignments.Count > 0)
  {
      _db.BatchSensorAssignments.RemoveRange(sensorAssignments);
  }
  ```
- In `src/BrewYou.ApiService/Services/BrewerySetupService.cs:328-336`:
  ```csharp
  var sensorAssignments = await _db.BatchSensorAssignments
      .Where(sa => setupEquipmentIds.Contains(sa.EquipmentId))
      .ToListAsync();

  if (sensorAssignments.Count > 0)
  {
      _db.BatchSensorAssignments.RemoveRange(sensorAssignments);
  }
  ```
  Both deletion routines remove referencing sensor assignments prior to removing the equipment, eliminating FK 23503 `DbUpdateException` (HTTP 500).

### C. Decoupling of `AssignedKegId` on Taps
- In `src/BrewYou.ApiService/Services/EquipmentService.cs:480-490`:
  ```csharp
  var referencingTaps = await _db.Equipment
      .Where(e => e.AssignedKegId == id)
      .ToListAsync();

  foreach (var tap in referencingTaps)
  {
      tap.AssignedKegId = null;
      tap.AssignedKeg = null;
      tap.UpdatedAt = DateTime.UtcNow;
  }
  ```
- In `src/BrewYou.ApiService/Services/BrewerySetupService.cs:357-368`, taps referencing kegs in the brewery setup being deleted are similarly cleared.
- In `src/BrewYou.Web/src/routes/inventory/equipment/+page.svelte:337-350`, `handleDeleted` updates reactive client state to set `assignedKegId: null` and `assignedKeg: null` on any tap referencing the deleted keg.

### D. Active Batch Guard & Conflict Flow
- In `src/BrewYou.ApiService/Services/EquipmentService.cs:429-449`:
  ```csharp
  if (!force)
  {
      var hasActiveBatches = await _db.Batches.AsNoTracking().AnyAsync(b =>
          (b.UserId == userId || (b.BrewerySetupId != null && b.BrewerySetupId == equipment.BrewerySetupId)) &&
          (b.BoilerId == id ||
           b.FermenterId == id ||
           b.PackagingVesselId == id ||
           b.HltId == id ||
           b.MashTunId == id ||
           (equipment.AssignedBatchId != null && b.Id == equipment.AssignedBatchId) ||
           b.SensorAssignments.Any(sa => sa.EquipmentId == id)) &&
          b.Status != BatchStatus.Completed &&
          b.Status != BatchStatus.Archived);

      if (hasActiveBatches)
      {
          _logger.LogWarning("Rejecting deletion of equipment {EquipmentId} as it is in use by active batches and force flag was false", id);
          return EquipmentAccessResult.Conflict;
      }
  }
  ```
- In `src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs:259-295`:
  `[FromQuery] bool force = false` is mapped. `EquipmentAccessResult.Conflict` yields `Results.Conflict(ApiResponse.Fail("EQUIPMENT_IN_USE", ...))` with HTTP 409 status code.

### E. `SaveChangesAsync` Exception Handling
- In `EquipmentService.cs:510-518`:
  ```csharp
  _db.Equipment.Remove(equipment);
  try
  {
      await _db.SaveChangesAsync();
  }
  catch (DbUpdateException ex)
  {
      _logger.LogError(ex, "Database conflict occurred while deleting equipment {EquipmentId}", id);
      return EquipmentAccessResult.Conflict;
  }
  ```
- In `BrewerySetupService.cs:398-406`:
  ```csharp
  try
  {
      await _db.SaveChangesAsync();
  }
  catch (DbUpdateException ex)
  {
      _logger.LogError(ex, "Database conflict occurred while deleting brewery setup {SetupId}", id);
      return (BrewerySetupAccessResult.Conflict, "Cannot delete brewery setup due to database dependency conflict.");
  }
  ```
  Both persist operations safely catch `DbUpdateException` and return conflict results rather than throwing unhandled 500 exceptions.

### F. Verification Tool Outputs
1. **.NET Backend Tests**:
   - Command: `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~EquipmentEndpointTests|FullyQualifiedName~EquipmentCascadingTests|FullyQualifiedName~BrewerySetupEndpointTests"`
   - Output: `Passed! - Failed: 0, Passed: 69, Skipped: 0, Total: 69, Duration: 3 s`
2. **.NET Code Formatting & Lint**:
   - Command: `dotnet format --verify-no-changes`
   - Output: Exited with code 0.
3. **Frontend Diagnostics**:
   - `npm run check`: `svelte-check found 0 errors and 0 warnings`.
   - `npm run test:unit`: `Test Files 65 passed (65), Tests 611 passed (611)`.
   - `npm run lint`: `All matched files use Prettier code style!`, ESLint passed with 0 errors and 0 warnings.
4. **i18n Parity**:
   - New keys (`no_vessel_assigned`, `deleted_vessel`, `delete_modal.*`) verified across `en.json`, `sv.json`, `de.json`, `fr.json`, `es.json` with 100% key parity.

---

## 2. Logic Chain

1. **Integrity & Authenticity Check**:
   - We verified that the tests in `EquipmentCascadingTests.cs` and `EquipmentEndpointTests.cs` perform genuine round-trip HTTP calls and direct PostgreSQL/EF database queries.
   - There are no hardcoded responses, mock facades, test shortcuts, or synthetic bypasses.
   - The deletion logic in `EquipmentService` and `BrewerySetupService` contains full entity relationship traversal, role authorization checks, and cascading decoupling.

2. **Adversarial Stress-Testing**:
   - *Scenario A: Restrict violation on delete.* Verified that because `BatchSensorAssignment.EquipmentId` has `DeleteBehavior.Restrict`, removing the referencing rows in the same transaction prior to `_db.Equipment.Remove` completely resolves error 23503.
   - *Scenario B: Dual-role equipment.* If a vessel serves multiple roles (e.g. multipurpose kettle used as both HLT and Boiler), the decoupled query sets all matching fields to null without throwing null-reference or state inconsistency errors.
   - *Scenario C: Deleting in-use equipment.* Unforced delete attempts consistently return `409 Conflict` (`EQUIPMENT_IN_USE`) while preserving database state. When `force == true` is passed, the guard is bypassed and clean decoupling occurs as intended.
   - *Scenario D: Keg-Tap relationships.* Taps referencing a deleted keg are cleanly updated so `assignedKegId` and `assignedKeg` are set to `null` both in the database and in reactive frontend state.
   - *Scenario E: Setup deletion with sensors.* Deleting a multi-equipment brewery setup with assigned sensors decouples batches, clears referencing taps, removes sensor assignments, and safely reassigns the default setup if the deleted setup was default.

---

## 3. Caveats

- **Cross-Milestone Integration Tests**:
  As noted in the worker report, `CascadingE2ETests.cs` contains tests spanning later milestones (M2 Recipe snapshotting, M3 Ingredient restrictions, M4 Backup sanitization). Those later tests will naturally fail until M2-M4 are completed by their respective workers.
  All tests scoped to Milestone 1 passed 100%.
- No other caveats.

---

## 4. Conclusion

**Verdict: APPROVE**

Milestone 1 successfully fulfills all requirements:
1. All 5 batch vessels (`BoilerId`, `FermenterId`, `PackagingVesselId`, `HltId`, `MashTunId`) decouple cleanly on equipment and setup deletion.
2. `BatchSensorAssignments` are pre-cleaned, completely eliminating PostgreSQL FK 23503 500 errors.
3. `AssignedKegId` on Taps is decoupled in both the backend database and the frontend client state.
4. Active batch guard correctly checks all 5 vessels and sensors, returning `409 Conflict` (`EQUIPMENT_IN_USE`) unless `force == true`.
5. All `SaveChangesAsync()` calls in equipment and setup deletion are guarded with `try / catch (DbUpdateException)`.
6. Sensor assignments in `BrewerySetupService.DeleteSetupAsync` are pre-cleaned prior to setup removal.
7. Backend tests (69/69 passed), frontend tests (611/611 passed), `dotnet format`, and `eslint/prettier` all pass with zero warnings and zero errors.

---

## 5. Verification Method

To independently verify this evaluation:

1. Run the targeted backend test suite:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~EquipmentEndpointTests|FullyQualifiedName~EquipmentCascadingTests|FullyQualifiedName~BrewerySetupEndpointTests"
   ```
   *Expected output*: `Passed! - Failed: 0, Passed: 69, Skipped: 0, Total: 69`.

2. Run backend code formatting verification:
   ```powershell
   dotnet format --verify-no-changes
   ```
   *Expected output*: Exits with code 0 (zero changes needed).

3. Run frontend verification:
   ```powershell
   cd src/BrewYou.Web
   npm run check
   npm run test:unit
   npm run lint
   ```
   *Expected output*: 0 errors, 0 warnings, 65 test files passed (611 tests passed).
