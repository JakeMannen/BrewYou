# Handoff Report: Milestone 1 Backend Domain Services & EF Core Implementation Plan

**Target Milestone**: Milestone 1 (Equipment Cascading Integrity & Sensor Decoupling)  
**Author**: `m1_explorer_1` (Teamwork Explorer Instance 1)  
**Target Recipient**: Orchestrator (`d80eeb54-d338-4b53-876e-3933dc2f1932`) & Milestone 1 Implementer  
**Timestamp**: 2026-11-04T09:20:00Z  

---

## 1. Observation

Direct code inspections across the BrewYou backend revealed eight critical referential and behavioral vulnerabilities during equipment and brewery setup deletions:

### O-1: BatchSensorAssignment FK Restrict Causes 500 on Equipment Deletion
- **Location**: `src/BrewYou.ApiService/Data/BrewYouDbContext.cs:542-563`
- **Verbatim Model Configuration**:
  ```csharp
  builder.Entity<BatchSensorAssignment>(entity =>
  {
      entity.HasKey(sa => sa.Id);
      entity.Property(sa => sa.Stage).HasConversion<string>().HasMaxLength(30);

      entity.HasOne(sa => sa.Batch)
            .WithMany(b => b.SensorAssignments)
            .HasForeignKey(sa => sa.BatchId)
            .OnDelete(DeleteBehavior.Cascade);

      entity.HasOne(sa => sa.Equipment)
            .WithMany()
            .HasForeignKey(sa => sa.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);

      entity.HasOne(sa => sa.BatchMashStep)
            .WithMany()
            .HasForeignKey(sa => sa.BatchMashStepId)
            .OnDelete(DeleteBehavior.SetNull);

      entity.HasIndex(sa => new { sa.BatchId, sa.EquipmentId });
  });
  ```
- **Entity**: `src/BrewYou.ApiService/Data/Entities/BatchSensorAssignment.cs:10` defines `public Guid EquipmentId { get; set; }` (non-nullable).
- In PostgreSQL, executing `DELETE FROM "Equipment" WHERE "Id" = ...` when referenced by `BatchSensorAssignment` triggers `Npgsql.PostgresException (0x80004005): 23503: update or delete on table "Equipment" violates foreign key constraint "FK_BatchSensorAssignments_Equipment_EquipmentId" on table "BatchSensorAssignments"`. Because `EquipmentService.DeleteEquipmentAsync` does not touch `BatchSensorAssignments`, this throws an unhandled `DbUpdateException` resulting in HTTP 500.

### O-2: EquipmentService Omission of HLT and Mash Tun Decoupling
- **Location**: `src/BrewYou.ApiService/Services/EquipmentService.cs:429-443`
- **Verbatim Code**:
  ```csharp
  // Decouple from any batches using this equipment
  var batchesWithEquipment = await _db.Batches
      .Where(b => b.BoilerId == id || b.FermenterId == id || b.PackagingVesselId == id)
      .ToListAsync();

  foreach (var b in batchesWithEquipment)
  {
      if (b.BoilerId == id) b.BoilerId = null;
      if (b.FermenterId == id) b.FermenterId = null;
      if (b.PackagingVesselId == id) b.PackagingVesselId = null;
      b.UpdatedAt = DateTime.UtcNow;
  }

  _db.Equipment.Remove(equipment);
  await _db.SaveChangesAsync();
  ```
- `Batch` (`src/BrewYou.ApiService/Data/Entities/Batch.cs:64-77`) defines 5 vessel foreign keys: `HltId`, `MashTunId`, `BoilerId`, `FermenterId`, `PackagingVesselId`.
- Current code zeroes only 3 (`BoilerId`, `FermenterId`, `PackagingVesselId`). `HltId` and `MashTunId` are completely omitted, leaving stale IDs in memory and test harnesses.

### O-3: Stale Tap References When Deleting Kegs
- **Location**: `src/BrewYou.ApiService/Data/Entities/Equipment.cs:188` defines `public Guid? AssignedKegId { get; set; }`.
- In `EquipmentService.DeleteEquipmentAsync`, no lookup or update is performed on `_db.Equipment.Where(e => e.AssignedKegId == id)`. Taps referencing the deleted keg retain ghost/stale references in the database and change tracker until modified or reloaded.

### O-4: Dangling Batch Assignments When Deleting Taps or Kegs
- **Location**: `src/BrewYou.ApiService/Data/Entities/Equipment.cs:191` defines `public Guid? AssignedBatchId { get; set; }`.
- If a tap or keg currently linked to a batch (`equipment.AssignedBatchId != null`) is deleted, `DeleteEquipmentAsync` does not clear `AssignedBatchId`, `AssignedKegId`, or `CurrentVolume` before calling `_db.Equipment.Remove(equipment)`.

### O-5: Uncaught DbUpdateException Producing HTTP 500
- **Location**: `src/BrewYou.ApiService/Services/EquipmentService.cs:443` and `src/BrewYou.ApiService/Services/BrewerySetupService.cs:337`.
- `await _db.SaveChangesAsync()` is invoked without a `try/catch` block for `DbUpdateException`. Any relational constraint failure bubbles up as an unhandled server crash rather than a structured HTTP 409 Conflict.

### O-6: Incomplete Active Batch Detection
- **Location**: `src/BrewYou.ApiService/Services/EquipmentService.cs:462-466`
- **Verbatim Code**:
  ```csharp
  var activeBatches = await _db.Batches.AsNoTracking()
      .Where(b => (b.UserId == userId || (b.BrewerySetupId != null && b.BrewerySetupId == equipment.BrewerySetupId)) &&
                  (b.BoilerId == equipmentId || b.FermenterId == equipmentId || b.PackagingVesselId == equipmentId) &&
                  b.Status != BatchStatus.Completed &&
                  b.Status != BatchStatus.Archived)
  ```
- Only Boiler, Fermenter, and Packaging vessels are checked. If equipment is in active use as an `HltId`, `MashTunId`, or configured as a sensor in `BatchSensorAssignment`, `GetActiveBatchesForEquipmentAsync` returns an empty list `[]`, deceiving brewers into believing equipment is idle.

### O-7: Missing Active Batch Guard on Deletion
- Currently, `DeleteEquipmentAsync` has no active batch check. Equipment actively mid-boil or mid-fermentation can be deleted without warning or prevention, silently nullifying vessel references on in-flight batches.
- Per `PROJECT.md` Interface Contracts:
  - `DELETE /api/v1/inventory/equipment/{id}` must return `409 Conflict` (`EQUIPMENT_IN_USE`) if assigned to an active batch without the `force` flag.

### O-8: Cascade Deletion Crash in BrewerySetupService
- **Location**: `src/BrewYou.ApiService/Services/BrewerySetupService.cs:321`
- `_db.BrewerySetups.Remove(setup);` cascades deletion to all `Equipment` in the setup. Because `BatchSensorAssignment.EquipmentId` has `DeleteBehavior.Restrict`, deleting a brewery setup containing any equipment used as a sensor immediately crashes with Postgres 23503.

---

## 2. Logic Chain

1. **Premise**: In PostgreSQL and EF Core, non-nullable foreign keys with `DeleteBehavior.Restrict` (`BatchSensorAssignment.EquipmentId`) reject entity deletion unless all referencing rows are deleted beforehand (O-1).
2. **Inference 1**: To prevent FK 23503 exceptions without altering database schema migrations, `EquipmentService.DeleteEquipmentAsync` must explicitly remove all `BatchSensorAssignments` where `sa.EquipmentId == id` prior to `_db.Equipment.Remove(equipment)`.
3. **Inference 2**: In `BrewerySetupService.DeleteSetupAsync`, deleting the parent setup cascades to all equipment in that setup. Therefore, `DeleteSetupAsync` must query all equipment IDs belonging to that setup and remove their `BatchSensorAssignments` prior to deleting the setup (O-8).
4. **Inference 3**: Batches support 5 distinct hardware vessels: HLT, Mash Tun, Boiler, Fermenter, and Packaging Vessel (O-2). When any vessel is deleted, all 5 foreign key properties (`b.HltId`, `b.MashTunId`, `b.BoilerId`, `b.FermenterId`, `b.PackagingVesselId`) must be checked and set to `null` to ensure consistent decoupling across all stages.
5. **Inference 4**: A Keg may be assigned to one or more Taps (`tap.AssignedKegId`). Deleting the Keg without decoupling leaves `AssignedKegId` pointing to a deleted entity (O-3). Querying `_db.Equipment.Where(e => e.AssignedKegId == id)` and setting `AssignedKegId = null` ensures tap cards immediately reflect an unassigned state.
6. **Inference 5**: Taps and Kegs can be directly assigned to batches (`equipment.AssignedBatchId != null`) (O-4). Clearing these assignments and resetting volume before removal guarantees clean state cleanup.
7. **Inference 6**: Deleting equipment actively brewing, mashing, fermenting, or monitoring is hazardous (O-7). Adding an active batch guard in `DeleteEquipmentAsync` that inspects all 5 vessels, sensor assignments, and tap/keg batch links returns `EquipmentAccessResult.Conflict` (`409 Conflict` `EQUIPMENT_IN_USE`) unless `force = true` is supplied.
8. **Inference 7**: `GetActiveBatchesForEquipmentAsync` must use the exact same comprehensive predicate (all 5 vessels + sensor assignments + tap/keg batch assignment) to ensure the UI modal accurately displays all active batches prior to deletion (O-6).
9. **Inference 8**: Wrapping `_db.SaveChangesAsync()` in a `try/catch (DbUpdateException)` block in both `EquipmentService` and `BrewerySetupService` guarantees that unexpected concurrency or referential conflicts return clean `Conflict` domain results rather than unhandled HTTP 500 crashes (O-5).

---

## 3. Concrete Implementation Strategy (Line-by-Line Code Proposals)

### 3.1. `src/BrewYou.ApiService/Services/IEquipmentService.cs`

1. Add `Conflict` to `EquipmentAccessResult` enum:
```csharp
public enum EquipmentAccessResult
{
    Success,
    NotFound,
    Forbidden,
    QuotaExceeded,
    DuplicateName,
    InvalidTapAssignment,
    Conflict
}
```

2. Update `DeleteEquipmentAsync` signature to support optional `force` flag:
```csharp
Task<EquipmentAccessResult> DeleteEquipmentAsync(Guid id, string userId, bool force = false);
```

---

### 3.2. `src/BrewYou.ApiService/Services/EquipmentService.cs`

Replace lines 405 to 479 of `EquipmentService.cs` with:

```csharp
    public async Task<EquipmentAccessResult> DeleteEquipmentAsync(Guid id, string userId, bool force = false)
    {
        var equipment = await _db.Equipment
            .Include(e => e.BrewerySetup)
                .ThenInclude(s => s!.Members)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (equipment == null)
        {
            return EquipmentAccessResult.NotFound;
        }

        var isMember = equipment.UserId == userId || (equipment.BrewerySetup != null && (equipment.BrewerySetup.UserId == userId || equipment.BrewerySetup.Members.Any(m => m.UserId == userId)));
        if (!isMember)
        {
            return EquipmentAccessResult.NotFound;
        }

        var isOwnerOrCreator = equipment.UserId == userId || (equipment.BrewerySetup != null && (equipment.BrewerySetup.UserId == userId || equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner)));
        if (!isOwnerOrCreator)
        {
            return EquipmentAccessResult.Forbidden;
        }

        // Active batch guard: Check if assigned to active batches unless force flag is set
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

        // 1. Decouple all 5 batch vessels using this equipment
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

        // 2. Pre-clean/remove BatchSensorAssignments referencing this equipment to prevent FK 23503 DbUpdateException
        var sensorAssignments = await _db.BatchSensorAssignments
            .Where(sa => sa.EquipmentId == id)
            .ToListAsync();

        if (sensorAssignments.Count > 0)
        {
            _db.BatchSensorAssignments.RemoveRange(sensorAssignments);
        }

        // 3. Decouple any taps referencing this equipment (if this equipment is an assigned keg)
        var referencingTaps = await _db.Equipment
            .Where(e => e.AssignedKegId == id)
            .ToListAsync();

        foreach (var tap in referencingTaps)
        {
            tap.AssignedKegId = null;
            tap.AssignedKeg = null;
            tap.UpdatedAt = DateTime.UtcNow;
        }

        // 4. Decouple batch or keg assignments if deleting a Tap or Keg
        if (equipment.AssignedBatchId != null)
        {
            equipment.AssignedBatchId = null;
            equipment.AssignedBatch = null;
            equipment.AssignedAt = null;
            equipment.CurrentVolume = 0m;
            equipment.CurrentVolumeLiters = 0m;
        }

        if (equipment.AssignedKegId != null)
        {
            equipment.AssignedKegId = null;
            equipment.AssignedKeg = null;
        }

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

        _logger.LogInformation("Equipment {EquipmentId} deleted by user {UserId}", id, userId);
        return EquipmentAccessResult.Success;
    }

    public async Task<(EquipmentAccessResult Result, List<EquipmentActiveBatchDto>? Batches)> GetActiveBatchesForEquipmentAsync(
        Guid equipmentId, string userId)
    {
        var equipment = await _db.Equipment.AsNoTracking()
            .Include(e => e.BrewerySetup)
                .ThenInclude(s => s!.Members)
            .FirstOrDefaultAsync(e => e.Id == equipmentId && (e.UserId == userId || (e.BrewerySetup != null && (e.BrewerySetup.UserId == userId || e.BrewerySetup.Members.Any(m => m.UserId == userId)))));

        if (equipment == null)
        {
            return (EquipmentAccessResult.NotFound, null);
        }

        var activeBatches = await _db.Batches.AsNoTracking()
            .Where(b => (b.UserId == userId || (b.BrewerySetupId != null && b.BrewerySetupId == equipment.BrewerySetupId)) &&
                        (b.BoilerId == equipmentId ||
                         b.FermenterId == equipmentId ||
                         b.PackagingVesselId == equipmentId ||
                         b.HltId == equipmentId ||
                         b.MashTunId == equipmentId ||
                         (equipment.AssignedBatchId != null && b.Id == equipment.AssignedBatchId) ||
                         b.SensorAssignments.Any(sa => sa.EquipmentId == equipmentId)) &&
                        b.Status != BatchStatus.Completed &&
                        b.Status != BatchStatus.Archived)
            .OrderByDescending(b => b.BrewDate)
            .ThenByDescending(b => b.CreatedAt)
            .Select(b => new EquipmentActiveBatchDto(
                b.Id,
                b.BatchCode,
                b.Name,
                b.Status,
                b.CurrentStage
            ))
            .ToListAsync();

        return (EquipmentAccessResult.Success, activeBatches);
    }
```

---

### 3.3. `src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs`

Update the `MapDelete` handler (lines 259 to 287):

```csharp
        group.MapDelete("/{id:guid}", async (
            Guid id,
            [FromQuery] bool force,
            ClaimsPrincipal principal,
            IEquipmentService equipmentService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await equipmentService.DeleteEquipmentAsync(id, userId, force);
            if (result == EquipmentAccessResult.Forbidden)
            {
                return Results.Json(ApiResponse.Fail("FORBIDDEN", "Viewers cannot delete equipment."), statusCode: StatusCodes.Status403Forbidden);
            }

            if (result == EquipmentAccessResult.NotFound)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Equipment not found."));
            }

            if (result == EquipmentAccessResult.Conflict)
            {
                return Results.Conflict(ApiResponse.Fail("EQUIPMENT_IN_USE", "Cannot delete equipment currently assigned to active batches without force."));
            }

            return Results.Ok(ApiResponse.Ok());
        })
        .WithName("DeleteEquipment")
        .Produces<ApiResponse>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict);
```

---

### 3.4. `src/BrewYou.ApiService/Services/IBrewerySetupService.cs`

Add `Conflict` to `BrewerySetupAccessResult` enum:

```csharp
public enum BrewerySetupAccessResult
{
    Success,
    NotFound,
    Forbidden,
    CannotDeleteLastSetup,
    QuotaExceeded,
    Conflict
}
```

---

### 3.5. `src/BrewYou.ApiService/Services/BrewerySetupService.cs`

Replace `DeleteSetupAsync` (lines 300 to 341) with:

```csharp
    public async Task<(BrewerySetupAccessResult Result, string? ErrorMessage)> DeleteSetupAsync(Guid id, string userId)
    {
        var setup = await _db.BrewerySetups
            .Include(s => s.Members)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (setup == null)
        {
            return (BrewerySetupAccessResult.NotFound, "Brewery setup not found.");
        }

        var isPrimaryOwner = setup.UserId == userId;
        var isCoOwner = setup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner);
        if (!isPrimaryOwner && !isCoOwner)
        {
            var isMember = setup.Members.Any(m => m.UserId == userId);
            if (isMember)
            {
                return (BrewerySetupAccessResult.Forbidden, "Only an owner can delete a brewery setup.");
            }
            return (BrewerySetupAccessResult.NotFound, "Brewery setup not found.");
        }

        var totalSetups = await _db.BrewerySetups.CountAsync(s => s.UserId == userId);
        if (isPrimaryOwner && totalSetups <= 1)
        {
            return (BrewerySetupAccessResult.CannotDeleteLastSetup, "Cannot delete the last remaining brewery setup.");
        }

        // 1. Identify all equipment in this setup
        var setupEquipmentIds = await _db.Equipment
            .Where(e => e.BrewerySetupId == id)
            .Select(e => e.Id)
            .ToListAsync();

        if (setupEquipmentIds.Count > 0)
        {
            // Pre-clean BatchSensorAssignments for all equipment in the setup to prevent FK 23503 DbUpdateException
            var sensorAssignments = await _db.BatchSensorAssignments
                .Where(sa => setupEquipmentIds.Contains(sa.EquipmentId))
                .ToListAsync();

            if (sensorAssignments.Count > 0)
            {
                _db.BatchSensorAssignments.RemoveRange(sensorAssignments);
            }

            // Decouple any batches using equipment from this setup across all 5 vessels
            var batchesWithSetupEquipment = await _db.Batches
                .Where(b => (b.BoilerId != null && setupEquipmentIds.Contains(b.BoilerId.Value)) ||
                            (b.FermenterId != null && setupEquipmentIds.Contains(b.FermenterId.Value)) ||
                            (b.PackagingVesselId != null && setupEquipmentIds.Contains(b.PackagingVesselId.Value)) ||
                            (b.HltId != null && setupEquipmentIds.Contains(b.HltId.Value)) ||
                            (b.MashTunId != null && setupEquipmentIds.Contains(b.MashTunId.Value)))
                .ToListAsync();

            foreach (var b in batchesWithSetupEquipment)
            {
                if (b.BoilerId != null && setupEquipmentIds.Contains(b.BoilerId.Value)) b.BoilerId = null;
                if (b.FermenterId != null && setupEquipmentIds.Contains(b.FermenterId.Value)) b.FermenterId = null;
                if (b.PackagingVesselId != null && setupEquipmentIds.Contains(b.PackagingVesselId.Value)) b.PackagingVesselId = null;
                if (b.HltId != null && setupEquipmentIds.Contains(b.HltId.Value)) b.HltId = null;
                if (b.MashTunId != null && setupEquipmentIds.Contains(b.MashTunId.Value)) b.MashTunId = null;
                b.UpdatedAt = DateTime.UtcNow;
            }

            // Decouple any external taps referencing kegs in this setup
            var tapsWithKegs = await _db.Equipment
                .Where(e => e.AssignedKegId != null && setupEquipmentIds.Contains(e.AssignedKegId.Value))
                .ToListAsync();

            foreach (var tap in tapsWithKegs)
            {
                tap.AssignedKegId = null;
                tap.AssignedKeg = null;
                tap.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Decouple any batches referencing this brewery setup
        var batchesWithSetup = await _db.Batches
            .Where(b => b.BrewerySetupId == id)
            .ToListAsync();

        foreach (var b in batchesWithSetup)
        {
            b.BrewerySetupId = null;
            b.UpdatedAt = DateTime.UtcNow;
        }

        bool wasDefault = setup.IsDefault;
        _db.BrewerySetups.Remove(setup);

        if (wasDefault && isPrimaryOwner)
        {
            var nextDefault = await _db.BrewerySetups
                .Where(s => s.UserId == userId && s.Id != id)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            if (nextDefault != null)
            {
                nextDefault.IsDefault = true;
                nextDefault.UpdatedAt = DateTime.UtcNow;
            }
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database conflict occurred while deleting brewery setup {SetupId}", id);
            return (BrewerySetupAccessResult.Conflict, "Cannot delete brewery setup due to database dependency conflict.");
        }

        _logger.LogInformation("Brewery setup {SetupId} deleted by user {UserId}", id, userId);
        return (BrewerySetupAccessResult.Success, null);
    }
```

---

### 3.6. `src/BrewYou.ApiService/Endpoints/BrewerySetupEndpoints.cs`

In `BrewerySetupEndpoints.cs` around line 150:

```csharp
            if (result == BrewerySetupAccessResult.Conflict)
            {
                return Results.Conflict(ApiResponse.Fail("SETUP_IN_USE", errorMessage ?? "Cannot delete brewery setup in use or constrained by dependent records."));
            }
```
Add `.Produces<ApiResponse>(StatusCodes.Status409Conflict)`.

---

### 3.7. Automated Test Suites Alignment

In `tests/BrewYou.ApiService.Tests/EquipmentEndpointTests.cs`:
1. Update existing test `DeleteEquipment_WhenAssignedToActiveBatch_DecouplesForeignKeyViaSetNull`:
   Change delete URL to pass `?force=true`:
   ```csharp
   var delRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{boiler.Id}?force=true");
   delRes.StatusCode.Should().Be(HttpStatusCode.OK);
   ```
2. Add new test: `DeleteEquipment_WhenAssignedToActiveBatch_WithoutForce_Returns409Conflict()`:
   Asserts status code is `409 Conflict` and error code is `"EQUIPMENT_IN_USE"`.
3. Add new test: `DeleteEquipment_WhenUsedAsSensorInBatch_DecouplesSensorAssignmentAndDeletesCleanly()`:
   Creates a sensor equipment, links via `BatchSensorAssignment`, deletes equipment with `?force=true`, asserts `200 OK` and zero remaining `BatchSensorAssignments` for that equipment.
4. Add new test: `DeleteEquipment_WhenKegAssignedToTap_DecouplesTapAssignedKegId()`:
   Creates a keg, assigns to tap, deletes keg, asserts tap's `AssignedKegId` is null.
5. Add new test: `GetActiveBatches_WhenAssignedAsHltOrMashTunOrSensor_ReturnsActiveBatches()`:
   Verifies that equipment assigned as HLT, Mash Tun, or Sensor appears in `/api/v1/inventory/equipment/{id}/active-batches`.

In `tests/BrewYou.ApiService.Tests/BrewerySetupEndpointTests.cs`:
1. Add test: `DeleteBrewerySetup_WhenEquipmentHasSensorAssignments_PreCleansAndDeletesSuccessfully()`:
   Verifies deleting a setup with sensors linked to batches succeeds without throwing FK 23503.

---

## 4. Caveats

1. **Database-Level vs. Service-Level Cascade**:
   `BatchSensorAssignment.EquipmentId` retains `DeleteBehavior.Restrict` in `BrewYouDbContext.cs`. This was deliberately preserved so that database-level integrity is maintained without requiring an EF Core schema migration. The domain services take explicit responsibility for pre-cleaning sensor assignments.
2. **Batch Telemetry Readings Retention**:
   When equipment is deleted, existing historical `EquipmentReadings` (`src/BrewYou.ApiService/Data/Entities/EquipmentReading.cs`) cascade delete in PostgreSQL per `BrewYouDbContext.cs:316` (`OnDelete(DeleteBehavior.Cascade)`). Batch manual gravity/temperature readings (`BatchReading`) are untouched as they reference only `BatchId`.
3. **Frontend Coordination**:
   The frontend API client (`src/BrewYou.Web/src/lib/api/client.ts`) should be updated to accept an optional `force?: boolean` argument on `api.equipment.delete(id, force)` to pass `?force=true` when user confirms in `DeleteEquipmentModal.svelte`.

---

## 5. Conclusion

Milestone 1's backend vulnerabilities (O-1 through O-8) are completely resolved by implementing **Clean Service-Level Decoupling** in `EquipmentService` and `BrewerySetupService`:
- All 5 batch vessels (`BoilerId`, `FermenterId`, `PackagingVesselId`, `HltId`, `MashTunId`) are cleanly decoupled.
- `BatchSensorAssignments` are pre-cleaned prior to equipment removal, permanently eliminating the PostgreSQL 23503 `DbUpdateException` crash.
- `AssignedKegId` references on Taps are cleanly decoupled when a keg is removed.
- In-progress batches are protected from accidental equipment deletion via the 409 Conflict `EQUIPMENT_IN_USE` check unless `force = true` is supplied.
- `GetActiveBatchesForEquipmentAsync` detects all 5 vessels, sensor assignments, and direct batch links.
- All database operations are wrapped in `try/catch (DbUpdateException)` returning structured 409 Conflict responses.

---

## 6. Verification Method

To verify the implementation once coded:
1. Build test and service projects:
   ```powershell
   dotnet build tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj
   ```
2. Run equipment and setup test suites:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~EquipmentEndpointTests"
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~BrewerySetupEndpointTests"
   ```
3. Run full backend test suite:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj
   ```
4. Invalidation condition:
   If deleting equipment configured in `BatchSensorAssignment` throws `DbUpdateException` or returns HTTP 500, or if `GetActiveBatchesForEquipmentAsync` returns 0 batches when equipment is an HLT or Mash Tun, the verification fails.
