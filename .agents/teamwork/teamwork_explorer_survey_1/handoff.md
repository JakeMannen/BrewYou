# Investigation Report: Cascading Effects of Deleting and Renaming Equipment, Recipes, and Ingredients

**Author**: `teamwork_explorer` instance 1 (`teamwork_explorer_survey_1`)  
**Scope**: Data Layer & Backend Domain/Services (EF Core, Models, DB Schema/Constraints, Foreign Keys, OnDelete behaviors, Cascades, Transactions, API Endpoints, Domain Services, Failure Scenarios, Proposed Solutions).  
**Workspace**: `BrewYou`  
**Date**: 2026-11-04  

---

## 1. Observation

Direct observations from source code inspection and analysis across `src/BrewYou.ApiService` and `src/BrewYou.Web`:

### A. EF Core Configuration & Entity Relationships (`BrewYouDbContext.cs`)

1. **Equipment Foreign Keys & OnDelete Behaviors**:
   - `EquipmentReading.EquipmentId`: Required `Guid`, `DeleteBehavior.Cascade` (`BrewYouDbContext.cs`:315-316).
   - `BatchSensorAssignment.EquipmentId`: Required `Guid`, `DeleteBehavior.Restrict` (`BrewYouDbContext.cs`:554-555):
     ```csharp
     entity.HasOne(sa => sa.Equipment)
           .WithMany()
           .HasForeignKey(sa => sa.EquipmentId)
           .OnDelete(DeleteBehavior.Restrict);
     ```
   - `Batch.BoilerId`: Nullable `Guid?`, `DeleteBehavior.SetNull` (`BrewYouDbContext.cs`:388-389).
   - `Batch.FermenterId`: Nullable `Guid?`, `DeleteBehavior.SetNull` (`BrewYouDbContext.cs`:393-394).
   - `Batch.PackagingVesselId`: Nullable `Guid?`, `DeleteBehavior.SetNull` (`BrewYouDbContext.cs`:398-399).
   - `Batch.HltId`: Nullable `Guid?`, `DeleteBehavior.SetNull` (`BrewYouDbContext.cs`:377-379).
   - `Batch.MashTunId`: Nullable `Guid?`, `DeleteBehavior.SetNull` (`BrewYouDbContext.cs`:382-384).
   - `Equipment.AssignedKegId` (Tap referencing Keg): Nullable `Guid?`, `DeleteBehavior.SetNull` (`BrewYouDbContext.cs`:244-245).
   - `Equipment.AssignedBatchId` (Tap referencing Batch): Nullable `Guid?`, `DeleteBehavior.SetNull` (`BrewYouDbContext.cs`:239-240).
   - `BrewerySetup.Equipment`: Required `Guid BrewerySetupId`, `DeleteBehavior.Cascade` (`BrewYouDbContext.cs`:234-235).

2. **Recipe Foreign Keys & OnDelete Behaviors**:
   - `RecipeIngredient.RecipeId`: Required `Guid`, `DeleteBehavior.Cascade` (`BrewYouDbContext.cs`:193-194).
   - `RecipeMashStep.RecipeId`: Required `Guid`, `DeleteBehavior.Cascade` (`BrewYouDbContext.cs`:483-484).
   - `RecipeFermentationStep.RecipeId`: Required `Guid`, `DeleteBehavior.Cascade` (`BrewYouDbContext.cs`:518-519).
   - `RecipeLabel.RecipeId`: Required `Guid`, `DeleteBehavior.Cascade` (`BrewYouDbContext.cs`:271-272).
   - `Batch.RecipeId`: Nullable `Guid?`, `DeleteBehavior.SetNull` (`BrewYouDbContext.cs`:368-369).
   - `Batch` entity model (`Batch.cs`:26-98): **Does not contain a `RecipeName` property**. It only possesses `Guid? RecipeId` and navigation property `Recipe? Recipe`.

3. **Ingredient Foreign Keys & OnDelete Behaviors**:
   - `RecipeIngredient.IngredientId`: Required `Guid`, `DeleteBehavior.Restrict` (`BrewYouDbContext.cs`:198-199):
     ```csharp
     entity.HasOne(ri => ri.Ingredient)
           .WithMany(i => i.RecipeIngredients)
           .HasForeignKey(ri => ri.IngredientId)
           .OnDelete(DeleteBehavior.Restrict);
     ```
   - `IngredientStock.IngredientId`: Required `Guid`, `DeleteBehavior.Cascade` (`BrewYouDbContext.cs`:178-179).
   - `BatchIngredient.SourceIngredientId` (`BatchIngredient.cs`:10): Nullable `Guid?`, **unconstrained column with no foreign key constraint configured in EF Core**.
   - `BatchIngredient.Name` (`BatchIngredient.cs`:12): Required `string Name`—**denormalized snapshot of ingredient name** at the time of batch creation.

4. **Unique Constraints & Indexes**:
   - Equipment: Unique index on `(UserId, BrewerySetupId, Name)` (`BrewYouDbContext.cs`:248).
   - Recipe: Unique index on `(UserId, Name)` (`BrewYouDbContext.cs`:144).
   - Ingredient: Index on `(CreatedByUserId, Name)` (`BrewYouDbContext.cs`:161).
   - Batch: Unique index on `(UserId, BatchCode)` (`BrewYouDbContext.cs`:401).

---

### B. Service Logic & Deletion Behaviors

1. **Equipment Deletion (`EquipmentService.cs`:405-447)**:
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
   *Observations*:
   - Missing decoupling for `b.HltId` and `b.MashTunId`.
   - Completely ignores `BatchSensorAssignment` rows pointing to this equipment.
   - Completely ignores `Equipment.AssignedKegId` (taps assigned to this keg).
   - No `try/catch` block for `DbUpdateException`.

2. **Active Batches for Equipment (`EquipmentService.cs`:462-466)**:
   ```csharp
   var activeBatches = await _db.Batches.AsNoTracking()
       .Where(b => (b.UserId == userId || (b.BrewerySetupId != null && b.BrewerySetupId == equipment.BrewerySetupId)) &&
                   (b.BoilerId == equipmentId || b.FermenterId == equipmentId || b.PackagingVesselId == equipmentId) &&
                   b.Status != BatchStatus.Completed &&
                   b.Status != BatchStatus.Archived)
   ```
   *Observations*:
   - Only checks `BoilerId`, `FermenterId`, `PackagingVesselId`.
   - Omits `HltId`, `MashTunId`, and `sa.EquipmentId` from `BatchSensorAssignment`.
   - Front-end modal `DeleteEquipmentModal.svelte` reports "0 active batches" for an actively assigned sensor, allowing the brewer to confirm delete, leading to an immediate crash.

3. **BrewerySetup Deletion (`BrewerySetupService.cs`:321-337)**:
   ```csharp
   _db.BrewerySetups.Remove(setup);
   ...
   await _db.SaveChangesAsync();
   ```
   *Observations*:
   - Cascades delete to all `Equipment` in the setup (`BrewYouDbContext.cs`:235).
   - If any equipment in the setup is assigned to `BatchSensorAssignment`, delete fails with foreign key violation `DbUpdateException` (`DeleteBehavior.Restrict`).
   - No check or cleanup of `BatchSensorAssignment` or batch equipment references.

4. **Recipe Deletion (`RecipeService.cs`:500-524)**:
   ```csharp
   // Decouple linked batches explicitly so In-Memory database & cached entities stay synchronized
   var linkedBatches = await db.Batches.Where(b => b.RecipeId == id).ToListAsync();
   foreach (var batch in linkedBatches)
   {
       batch.RecipeId = null;
   }

   db.Recipes.Remove(recipe);
   await db.SaveChangesAsync();
   ```
   *Observations*:
   - Decouples all batches by setting `batch.RecipeId = null`.
   - `BatchService.MapToDetailDto` (`BatchService.cs`:1730) maps `RecipeName: b.Recipe?.Name`.
   - When `b.RecipeId` is set to `null`, `b.Recipe` is null. `RecipeName` becomes `null` across all batches.
   - There is no active batch check (a recipe can be deleted while currently mid-boil or actively fermenting).

5. **Ingredient Deletion (`IngredientService.cs`:362-445)**:
   ```csharp
   // 1. Find all RecipeIngredients referencing this ingredient
   var affectedRecipeIngredients = await db.RecipeIngredients
       .Where(ri => ri.IngredientId == id)
       .ToListAsync();
   var affectedRecipeIds = affectedRecipeIngredients.Select(ri => ri.RecipeId).Distinct().ToList();

   // 2. Remove the RecipeIngredient entries
   if (affectedRecipeIngredients.Count > 0)
   {
       db.RecipeIngredients.RemoveRange(affectedRecipeIngredients);
   }

   // 3. For each affected recipe, recalculate metrics using remaining ingredients
   if (affectedRecipeIds.Count > 0)
   {
       var affectedRecipes = await db.Recipes
           .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
           .Where(r => affectedRecipeIds.Contains(r.Id))
           .ToListAsync();

       foreach (var recipe in affectedRecipes)
       {
           var remainingIngredients = recipe.Ingredients.Where(ri => ri.IngredientId != id).ToList();
           var calc = BrewingCalculator.Calculate(recipe.BatchSizeLiters, recipe.EfficiencyPercent, recipe.BoilTimeMinutes, remainingIngredients);
           recipe.OriginalGravity = calc.OriginalGravity;
           recipe.FinalGravity = calc.FinalGravity;
           recipe.AlcoholByVolume = calc.AlcoholByVolume;
           recipe.BitternessIbu = calc.BitternessIbu;
           recipe.ColorSrm = calc.ColorSrm;
           recipe.UpdatedAt = DateTime.UtcNow;
       }
   }

   // 4. Remove any IngredientStock for this ingredient
   var stocks = await db.IngredientStocks.Where(s => s.IngredientId == id).ToListAsync();
   if (stocks.Count > 0)
   {
       db.IngredientStocks.RemoveRange(stocks);
   }

   // 5. Remove the ingredient itself
   db.Ingredients.Remove(ingredient);
   await db.SaveChangesAsync();
   ```
   *Observations*:
   - Deleting an ingredient strips it out of all recipes and silently recalculates recipes down (altering OG, FG, ABV, IBU, SRM without explicit user confirmation of the recipe mutations).
   - Does not check `BatchIngredient` at all.
   - `BatchIngredient.SourceIngredientId` is left dangling.
   - `BatchService.DeductIngredientStockAsync` (`BatchService.cs`:2258-2260) and `RefundIngredientStockAsync` (`BatchService.cs`:2300-2302):
     ```csharp
     var ingredient = item.SourceIngredientId.HasValue
         ? await _db.Ingredients.FindAsync(item.SourceIngredientId.Value)
         : await _db.Ingredients.FirstOrDefaultAsync(i => i.Name.ToLower() == item.Name.ToLower());
     ```
     When `SourceIngredientId` points to a deleted ingredient, `FindAsync` returns `null` and it never falls back to name matching, completely breaking stock deduction and refunds.
   - `IngredientService.GetIngredientUsageAsync` (`IngredientService.cs`:345-350) only queries `db.RecipeIngredients`; it completely ignores batches.

6. **Ingredient Renaming / Updating**:
   - `IngredientEndpoints.cs` and `IngredientService.cs`: **Zero endpoints or service methods exist to edit or rename an ingredient**.
   - If an ingredient name changes:
     - `RecipeIngredient`: Navigates via `ri.Ingredient.Name` dynamically.
     - `BatchIngredient`: Denormalizes `Name`. Retains old snapshotted name. If `SourceIngredientId` is null (custom ingredient), `DeductIngredientStockAsync` name matching fails.

7. **Global Exception Handling (`GlobalExceptionHandler.cs`:46-55)**:
   - Any unhandled `DbUpdateException` logs `logger.LogError` and returns:
     `StatusCodes.Status500InternalServerError` with JSON `{ "success": false, "error": { "code": "INTERNAL_SERVER_ERROR", "message": "An unexpected error occurred. Please try again later." } }`.

---

## 2. Logic Chain

From the direct observations above, the following logical failure chains are derived:

```
Observation: BatchSensorAssignment.EquipmentId has DeleteBehavior.Restrict (BrewYouDbContext.cs:555)
Observation: EquipmentService.DeleteEquipmentAsync does not remove or decouple BatchSensorAssignments (EquipmentService.cs:429-443)
Observation: GlobalExceptionHandler returns 500 on unhandled DbUpdateException (GlobalExceptionHandler.cs:46-55)
──> Logic Step 1: When equipment assigned to a batch as a sensor is deleted, SaveChangesAsync throws DbUpdateException (Postgres FK violation 23503), resulting in an unhandled HTTP 500 error instead of a clean domain error or cascade.

Observation: BrewerySetupService.DeleteSetupAsync calls _db.BrewerySetups.Remove(setup) (BrewerySetupService.cs:321)
Observation: EF Core cascades setup deletion to all equipment in that setup (BrewYouDbContext.cs:235)
──> Logic Step 2: Deleting a BrewerySetup containing an assigned sensor equipment cascades to Equipment deletion, triggers the same FK Restrict violation on BatchSensorAssignment, and crashes with an unhandled HTTP 500 error.

Observation: EquipmentService.GetActiveBatchesForEquipmentAsync omits HltId, MashTunId, and SensorAssignments (EquipmentService.cs:462-466)
Observation: DeleteEquipmentModal relies on getActiveBatches to warn user (DeleteEquipmentModal.svelte:38)
──> Logic Step 3: Frontend UI falsely informs the brewer that an equipment is not in use by any active batch when it is assigned as an HLT, Mash Tun, or Sensor, misleading the user into executing a breaking deletion.

Observation: Batch entity does not store RecipeName (Batch.cs:26-98)
Observation: RecipeService.DeleteRecipeAsync sets batch.RecipeId = null on all linked batches (RecipeService.cs:517)
Observation: BatchService.MapToDetailDto computes RecipeName via b.Recipe?.Name (BatchService.cs:1730)
──> Logic Step 4: Deleting a recipe permanently erases the recipe name and recipe identity across all historical and active batches. The printed labels and batch screens display blank/null for recipe name.

Observation: BatchIngredient snapshots ingredient name and stores SourceIngredientId (BatchService.cs:369-370)
Observation: IngredientService.DeleteIngredientAsync removes the Ingredient entity and all its IngredientStock rows, but does not touch BatchIngredient (IngredientService.cs:431-442)
Observation: BatchService.DeductIngredientStockAsync uses SourceIngredientId without fallback if FindAsync returns null (BatchService.cs:2258-2260)
──> Logic Step 5: Deleting an ingredient leaves dangling SourceIngredientIds in BatchIngredients. Subsequent inventory deductions or refunds silently fail to deduct or credit inventory.

Observation: IngredientService.DeleteIngredientAsync removes RecipeIngredient and recalculates recipe metrics down without user confirmation (IngredientService.cs:393-428)
──> Logic Step 6: Deleting a custom ingredient silently corrupts recipe formulations, dropping original gravity, ABV, SRM, and IBU of recipes created by the brewer.

Observation: IngredientEndpoints and IngredientService have no PUT or update endpoint for custom ingredients
──> Logic Step 7: Users cannot fix typos or rename custom ingredients without deleting them, forcing users into the destructive deletion cascade described in Step 6.
```

---

## 3. Concrete Failure Scenarios Summary

| ID | Entity & Action | Root Cause & File Location | Impact & Result |
| :--- | :--- | :--- | :--- |
| **FS-01** | **Delete Equipment** (Assigned Sensor) | `BatchSensorAssignment.EquipmentId` has `DeleteBehavior.Restrict` (`BrewYouDbContext.cs`:555); not decoupled in `DeleteEquipmentAsync` (`EquipmentService.cs`:429) | `DbUpdateException` (Postgres 23503) -> **Unhandled 500 Internal Server Error**. Sensor cannot be deleted. |
| **FS-02** | **Delete Equipment** (Assigned HLT or Mash Tun) | `DeleteEquipmentAsync` only decouples `BoilerId`, `FermenterId`, `PackagingVesselId` (`EquipmentService.cs`:431); misses `HltId`, `MashTunId` | In-memory tracker retains stale references; EF Core `SetNull` not applied in unit test harnesses or in-memory DB. |
| **FS-03** | **Delete Equipment** (Keg assigned to Tap) | `DeleteEquipmentAsync` does not null `tap.AssignedKegId` (`Equipment.cs`:188) | Tap displays stale or broken keg references until hard reloaded. |
| **FS-04** | **Delete BrewerySetup** (with assigned sensors) | `DeleteSetupAsync` (`BrewerySetupService.cs`:321) cascades to `Equipment`, hitting FK Restrict on `BatchSensorAssignment` | `DbUpdateException` -> **Unhandled 500 Internal Server Error**. Brewery setup cannot be deleted. |
| **FS-05** | **Check Active Batches for Equipment** | `GetActiveBatchesForEquipmentAsync` (`EquipmentService.cs`:462-466) omits HLT, Mash Tun, and Sensors | UI warns that equipment has "0 active batches" when it is in active use. |
| **FS-06** | **Delete Recipe** (with existing batches) | `Batch.RecipeName` is not persisted; `DeleteRecipeAsync` sets `batch.RecipeId = null` (`RecipeService.cs`:517) | **Permanent data loss**: All batches lose their recipe name on batch detail pages and printed labels (`+page.svelte`:254). |
| **FS-07** | **Delete Recipe** (with in-progress batches) | `DeleteRecipeAsync` has no active batch check | Recipes can be deleted while active batches are mid-boil or fermenting without warning. |
| **FS-08** | **Delete Ingredient** (used in batches) | Dangling `BatchIngredient.SourceIngredientId`; `DeductIngredientStockAsync` fails lookup (`BatchService.cs`:2258) | Stock deduction and restitution silently fail on batches. |
| **FS-09** | **Delete Ingredient** (used in recipes) | `DeleteIngredientAsync` silently deletes `RecipeIngredients` and recalculates recipe metrics down (`IngredientService.cs`:393-428) | Recipes are altered and damaged without user approval. |
| **FS-10** | **Rename Ingredient** | No API endpoint exists for editing/renaming custom ingredients in `IngredientEndpoints.cs` | Users cannot rename ingredients; forced to delete and re-create. |

---

## 4. Proposed Solutions

### 1. Equipment Deletion & Active Batch Safeguards

1. **Decouple All Batch Equipment References & Assignments**:
   Update `EquipmentService.DeleteEquipmentAsync` (`src/BrewYou.ApiService/Services/EquipmentService.cs`):
   - Decouple all 5 batch vessel references: `b.BoilerId`, `b.FermenterId`, `b.PackagingVesselId`, `b.HltId`, and `b.MashTunId`.
   - Decouple keg assignments from taps:
     ```csharp
     var tapsWithKeg = await _db.Equipment.Where(e => e.AssignedKegId == id).ToListAsync();
     foreach (var tap in tapsWithKeg)
     {
         tap.AssignedKegId = null;
         tap.AssignedKeg = null;
         tap.UpdatedAt = DateTime.UtcNow;
     }
     ```
   - Decouple tap assignments from batches:
     ```csharp
     if (equipment.Type == EquipmentType.Tap && equipment.AssignedBatchId != null)
     {
         equipment.AssignedBatchId = null;
         equipment.AssignedBatch = null;
         equipment.AssignedAt = null;
         equipment.CurrentVolume = 0m;
         equipment.CurrentVolumeLiters = 0m;
     }
     ```
   - Decouple or remove `BatchSensorAssignment` rows for the equipment:
     ```csharp
     var sensorAssignments = await _db.BatchSensorAssignments
         .Where(sa => sa.EquipmentId == id)
         .ToListAsync();
     if (sensorAssignments.Count > 0)
     {
         _db.BatchSensorAssignments.RemoveRange(sensorAssignments);
     }
     ```
   - Alternatively, update `BrewYouDbContext.cs`:555 to configure `DeleteBehavior.Cascade` on `BatchSensorAssignment.EquipmentId` (or domain-level unassignment).
   - Wrap `_db.SaveChangesAsync()` with `try/catch (DbUpdateException)` to return clean domain errors instead of 500s if constraints are violated.

2. **Complete Active Batch Detection in `GetActiveBatchesForEquipmentAsync`**:
   Update `EquipmentService.GetActiveBatchesForEquipmentAsync` (`EquipmentService.cs`:462-466):
   - Check `(b.BoilerId == equipmentId || b.FermenterId == equipmentId || b.PackagingVesselId == equipmentId || b.HltId == equipmentId || b.MashTunId == equipmentId || b.SensorAssignments.Any(sa => sa.EquipmentId == equipmentId))`.
   - Prevent deletion if equipment is assigned to an **active** batch (`Planned`, `Brewing`, `Fermenting`, `Conditioning`), returning `EquipmentAccessResult.Conflict` / `409 Conflict`: *"Cannot delete equipment while assigned to an active batch. Please finish or unassign the batch first."*

3. **Protect BrewerySetup Deletion**:
   In `BrewerySetupService.DeleteSetupAsync` (`BrewerySetupService.cs`:321):
   - Pre-clean `BatchSensorAssignments` for all equipment in `setup.Equipment` before calling `_db.BrewerySetups.Remove(setup)`.

---

### 2. Recipe Deletion & Renaming Integrity

1. **Snapshot `RecipeName` on `Batch` Entity**:
   - Add `public string? RecipeName { get; set; }` to `Batch` (`Batch.cs`).
   - In `BatchService.CreateBatchAsync` (`BatchService.cs`:329):
     `RecipeName = recipe?.Name,`
   - In `BatchService.MapToDetailDto` (`BatchService.cs`:1730):
     `RecipeName: b.Recipe?.Name ?? b.RecipeName,`
   - In `RecipeService.DeleteRecipeAsync` (`RecipeService.cs`:516):
     ```csharp
     foreach (var batch in linkedBatches)
     {
         batch.RecipeName ??= recipe.Name;
         batch.RecipeId = null;
         batch.UpdatedAt = DateTime.UtcNow;
     }
     ```
   - This ensures batches retain their recipe name permanently after recipe deletion.
   - Update `BackupContracts.cs` (`BatchBackupDto`), `BackupService.cs`, and `api.ts` to include optional `string? RecipeName` for backwards-compatible backup/restore.

2. **Active Batch Recipe Deletion Safeguard**:
   - In `RecipeService.DeleteRecipeAsync`:
     Check if any linked batches are active (`b.Status != BatchStatus.Completed && b.Status != BatchStatus.Archived`).
     If active batches exist, return `RecipeAccessResult.ActiveBatchesInProgress` (HTTP 409 Conflict): *"Cannot delete recipe while active batches are in progress."*

---

### 3. Ingredient Deletion & Renaming Integrity

1. **Guard Against Silent Recipe Corruption**:
   - In `IngredientService.DeleteIngredientAsync` (`IngredientService.cs`:383):
     If `affectedRecipeIngredients.Count > 0`, return `(IngredientOperationResult.InUseByRecipes, "Cannot delete ingredient because it is in use by {count} recipe(s). Please remove it from recipes first.")`.
     This prevents unexpected automatic recipe dismantling and metric recalculation.

2. **Check Batches in `GetIngredientUsageAsync`**:
   - Update `IngredientUsageDto` to include `int BatchCount` and `List<IngredientUsageBatchDto> Batches`.
   - Query `db.BatchIngredients.Where(bi => bi.SourceIngredientId == id)` to provide accurate usage visibility to the user.

3. **Safe Fallback in Inventory Deduction / Restitution**:
   - In `BatchService.DeductIngredientStockAsync` and `RefundIngredientStockAsync`:
     ```csharp
     var ingredient = item.SourceIngredientId.HasValue
         ? await _db.Ingredients.FindAsync(item.SourceIngredientId.Value)
         : null;

     if (ingredient == null && !string.IsNullOrWhiteSpace(item.Name))
     {
         ingredient = await _db.Ingredients.FirstOrDefaultAsync(i => i.Name.ToLower() == item.Name.ToLower());
     }
     ```
   - If `SourceIngredientId` points to a deleted entity, fallback to name matching so inventory deduction continues to operate seamlessly.

4. **Implement Missing Ingredient Update Endpoint**:
   - Add `UpdateIngredientRequest` DTO and endpoint `PUT /api/v1/ingredients/{id}` in `IngredientEndpoints.cs`.
   - Implement `UpdateIngredientAsync` in `IngredientService.cs`, verifying `CreatedByUserId == userId` and name uniqueness among user's custom ingredients.

---

## 5. Caveats

1. **In-Memory vs PostgreSQL Provider Discrepancies**:
   BrewYou tests execute using `UseInMemoryDatabase("BrewYouFallbackDb")` (`Program.cs`:38), while production uses PostgreSQL via Aspire. In-Memory EF Core does **not** enforce foreign key constraint cascades (`ON DELETE SET NULL`) unless explicitly tracked in the change tracker. Explicit service-level decoupling is required to ensure consistent behavior across both test and production environments.
2. **Catalog Ingredients**:
   Catalog ingredients are global and immutable (`IsCatalogItem == true`). The deletion rules above apply to user-created custom ingredients.
3. **Historical Telemetry Data**:
   `EquipmentReading` has `OnDelete(DeleteBehavior.Cascade)` on `EquipmentId`. Deleting an equipment deletes its historical sensor readings. If users wish to preserve telemetry readings after decommissioning equipment, a soft-delete/archived flag on `Equipment` (`IsArchived = true`) should be considered as a future enhancement.

---

## 6. Conclusion

The investigation revealed critical cascading failure modes:
1. Deleting equipment with assigned sensors throws an unhandled `DbUpdateException` (Postgres FK violation error 23503) resulting in an HTTP 500 server crash because `BatchSensorAssignment.EquipmentId` is configured with `DeleteBehavior.Restrict` and is not decoupled in `DeleteEquipmentAsync`.
2. Deleting a recipe wipes out the recipe identity and name across all batches because `Batch` does not snapshot `RecipeName` and relies solely on dynamic navigation through `RecipeId` (which gets set to null).
3. Deleting a custom ingredient silently strips ingredients from existing recipes, mutating and recalculating their brewing metrics without confirmation, while leaving dangling references in `BatchIngredient` that break inventory deductions.
4. Custom ingredients cannot be updated or renamed due to missing API endpoints.

Implementing the concrete solutions detailed in Section 4 will resolve these issues, guarantee data integrity, eliminate 500 errors, and preserve full backwards compatibility across the stack.

---

## 7. Verification Method

To independently verify these findings and future fixes:

1. **Sensor Equipment Deletion Verification**:
   - Create an equipment of type `Sensor`.
   - Create a batch and assign the sensor to the batch via `PUT /api/v1/batches/{batchId}/sensors`.
   - Issue `DELETE /api/v1/inventory/equipment/{sensorId}`.
   - *Current failure*: HTTP 500 Internal Server Error (`DbUpdateException`).
   - *Expected fix*: Successfully decouples or returns a 409 Conflict if active.

2. **Recipe Deletion Batch Name Snapshot Verification**:
   - Create a recipe "West Coast IPA".
   - Create a batch from this recipe.
   - Issue `DELETE /api/v1/recipes/{recipeId}`.
   - Fetch the batch via `GET /api/v1/batches/{batchId}`.
   - *Current failure*: `recipeName` is `null`.
   - *Expected fix*: `recipeName` preserves `"West Coast IPA"`.

3. **Ingredient Deletion Recipe Preservation Verification**:
   - Create a custom ingredient "Special Roasted Malt".
   - Add it to a recipe.
   - Issue `DELETE /api/v1/ingredients/{ingredientId}`.
   - *Current failure*: Recipe is silently modified and metrics recalculated.
   - *Expected fix*: Returns HTTP 400/409 error indicating ingredient is in use by recipes.

4. **Automated Test Suite Execution**:
   - Run:
     ```powershell
     dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~EquipmentEndpointTests|FullyQualifiedName~RecipeEndpointTests|FullyQualifiedName~IngredientEndpointTests|FullyQualifiedName~BatchEndpointTests"
     ```
