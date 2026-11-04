# Handoff Report: Cascading Effects of Deleting and Renaming Equipment, Recipes, and Ingredients

**Survey Area**: User Backup & Restore System, Data Serialization, Versioning, Backwards Compatibility, BeerXML/BeerJSON Interchange, and Automated Test Coverage  
**Working Directory**: `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\teamwork_explorer_survey_3\`  
**Milestone**: Phase 0 Survey — Explorer Instance 3  
**Status**: Complete  

---

## 1. Observation

### 1.1 User Backup & Restore Subsystem (`src/BrewYou.ApiService/Backup/` and `src/BrewYou.Web/src/lib/types/api.ts`)

1. **Backup Schema Constants and Enums** (`BackupContracts.cs:8-36`):
   ```csharp
   public enum RestoreConflictResolutionMode
   {
       MergeSkipExisting,
       MergeOverwriteExisting,
       ReplaceAll
   }

   public static class BackupSchemaConstants
   {
       public const string FormatIdentifier = "brewyou-user-backup";
       public const int CurrentSchemaVersion = 2;
       public const string CurrentAppVersion = "1.6.1";
   }
   ```
   - Schema version is currently `2`.
   - AGENTS.md Section 3.J mandates backwards compatibility with `v0`, `v1`, and forward compatibility with unrecognized fields (`ExtensionData`).

2. **Dangling Guid Retention in Batch Ingredients During Restore** (`BackupService.cs:2435-2442`):
   ```csharp
   batch.Ingredients = (dto.Ingredients ?? []).Select(bi => new BatchIngredient
   {
       Id = Guid.NewGuid(),
       BatchId = batch.Id,
       SourceIngredientId = bi.SourceIngredientId.HasValue && ingredientIdMap.TryGetValue(bi.SourceIngredientId.Value, out var sIngId)
           ? sIngId
           : bi.SourceIngredientId,
       Name = string.IsNullOrWhiteSpace(bi.Name) ? "Ingredient" : bi.Name.Trim(),
       ...
   ```
   - **Observation**: If `bi.SourceIngredientId` was NOT mapped in `ingredientIdMap` (e.g. the ingredient was deleted from the target database, or was custom to another instance, or was deleted before backup), the ternary expression falls back to `bi.SourceIngredientId`.
   - **Result**: `BatchIngredient.SourceIngredientId` is written to the database with a stale, non-existent foreign Guid from the archive.

3. **Silent Loss of Referenced Community Recipes on Restore** (`BackupService.cs:1203-1212` and `1345-1347`):
   ```csharp
   foreach (var extRecipe in payload.ReferencedExternalRecipes)
   {
       var liveExt = await _db.Recipes
           .AsNoTracking()
           .FirstOrDefaultAsync(r => r.Id == extRecipe.Id && (r.IsPublic || r.UserId == userId));
       if (liveExt != null)
       {
           recipeIdMap[extRecipe.Id] = liveExt.Id;
       }
   }
   ```
   - **Observation**: If a batch in the backup references a community recipe that was deleted or made private on the target server (`liveExt == null`), `recipeIdMap` does NOT record an entry for `extRecipe.Id`.
   - In lines 1345-1347:
     ```csharp
     Guid? mappedRecipeId = batchDto.RecipeId.HasValue && recipeIdMap.TryGetValue(batchDto.RecipeId.Value, out var rId)
         ? rId
         : null;
     ```
     `mappedRecipeId` becomes `null`.
   - **Discrepancy**: Unlike shared setups—which are cloned as personal setups under `SharedSetupFallbackMode.CloneAsPersonal` (`BackupService.cs:938-963`)—there is no cloning fallback for `ReferencedExternalRecipes`. The full snapshot stored in the backup is ignored, and the batch's `RecipeId` is decoupled to `null`.

4. **Equipment Reference Resolution in Batches During Restore** (`BackupService.cs:1351-1365`):
   ```csharp
   Guid? mappedBoilerId = batchDto.BoilerId.HasValue && equipmentIdMap.TryGetValue(batchDto.BoilerId.Value, out var bId) ? bId : null;
   Guid? mappedFermenterId = batchDto.FermenterId.HasValue && equipmentIdMap.TryGetValue(batchDto.FermenterId.Value, out var fId) ? fId : null;
   Guid? mappedPkgId = batchDto.PackagingVesselId.HasValue && equipmentIdMap.TryGetValue(batchDto.PackagingVesselId.Value, out var pId) ? pId : null;
   Guid? mappedHltId = batchDto.HltId.HasValue && equipmentIdMap.TryGetValue(batchDto.HltId.Value, out var hId) ? hId : null;
   Guid? mappedMashTunId = batchDto.MashTunId.HasValue && equipmentIdMap.TryGetValue(batchDto.MashTunId.Value, out var mtId) ? mtId : null;
   ```
   - If equipment was deleted or not restored, references safely fall back to `null`.
   - For `BrewerySetupId` (`BackupService.cs:1348-1350`):
     ```csharp
     Guid? mappedSetupId = batchDto.BrewerySetupId.HasValue && setupIdMap.TryGetValue(batchDto.BrewerySetupId.Value, out var sId)
         ? sId
         : defaultSetupId;
     ```
     Falls back to `defaultSetupId`, preserving foreign key integrity.

5. **Conflict Resolution Matching Semantics** (`BackupService.cs:480-494`):
   ```csharp
   int matchingSetups = payload.OwnedBrewerySetups.Count(s =>
       existingSetups.Any(es => es.Id == s.Id || es.NameLower == s.Name.Trim().ToLowerInvariant()));
   int matchingEquipment = payload.Equipment.Count(e =>
       existingEquipment.Any(ee => ee.Id == e.Id || ee.NameLower == e.Name.Trim().ToLowerInvariant()));
   int matchingIngredients = payload.CustomIngredients.Count(i =>
       existingCustomIngredients.Any(ei => ei.Id == i.Id || (ei.NameLower == i.Name.Trim().ToLowerInvariant() && ei.Type == i.Type)));
   int matchingRecipes = payload.Recipes.Count(r =>
       existingRecipes.Any(er => er.Id == r.Id || er.NameLower == r.Name.Trim().ToLowerInvariant()));
   int matchingBatches = payload.Batches.Count(b =>
       existingBatches.Any(eb => eb.Id == b.Id || eb.CodeLower == b.BatchCode.Trim().ToLowerInvariant()));
   ```
   - Matches by `Id OR Normalized Name`.
   - **Renamed entities in DB**: If an entity was renamed in the DB, its `Id` matches.
     - In `MergeSkipExisting`: the entity in DB is preserved with its new name.
     - In `MergeOverwriteExisting`: the entity in DB is overwritten with backup data, reverting its name to the archive name.
     - In `ReplaceAll`: existing entities are purged and recreated from backup.

---

### 1.2 BeerXML and BeerJSON Import and Export (`src/BrewYou.Web/src/lib/`)

1. **Absence of Equipment Profile Model in Recipe Importers** (`src/BrewYou.Web/src/lib/parsers/`):
   - `ParsedRecipe` (`types.ts:45-56`) extracts: `name`, `beerStyle`, `description`, `batchSizeLiters`, `boilTimeMinutes`, `efficiencyPercent`, `ingredients`, `mashSteps`, `fermentationSteps`.
   - BeerXML `<EQUIPMENT>` tag and BeerJSON `equipments` are ignored by `parseBeerXml` (`beerxml.ts`) and `parseBeerJson` (`beerjson.ts`).
   - Imported recipes do not link or create equipment profiles in BrewYou; they rely solely on scalar batch metrics (`batchSizeLiters`, `boilTimeMinutes`, `efficiencyPercent`).

2. **Ingredient Matching and Missing Ingredient Resolution** (`ingredient-matching.ts:195-263` and `RecipeImportModal.svelte:189-346`):
   - `findCatalogIngredientMatch` evaluates candidates of the same `IngredientType` across 5 tiers:
     - Tier 1: Exact case-insensitive match.
     - Tier 2: Normalized match (diacritics, compound suffixes, punctuation removed).
     - Tier 3: Yeast product code regex (`WLP001`, `US-05`, `W-34/70`, etc.).
     - Tier 4: Manufacturer prefix/suffix stripped match.
     - Tier 5: Significant token exact match (excluding brewing stopwords).
   - **Behavior when ingredient was deleted**:
     - `findCatalogIngredientMatch` returns `null`.
     - Ingredient is flagged in `missingIngredients`.
     - `RecipeImportModal.svelte` presents options: pick an existing catalog ingredient via fuzzy matching (`findCloseCatalogIngredientMatches`) or create a custom ingredient (`api.ingredients.create`).
   - **Behavior when ingredient was renamed**:
     - If the new name differs in significant tokens (e.g. "Centennial" renamed to "Centennial 2024 Crop"), `findCatalogIngredientMatch` fails.
     - Unless the user manually maps it to the renamed item in the modal, a duplicate custom ingredient is created via `api.ingredients.create`.

3. **Exporters Handling Missing or Deleted Attributes** (`src/BrewYou.Web/src/lib/exporters/beerxml.ts:10-16`):
   - `getIngName(i)` falls back to `i.name || i.ingredientName || 'Unnamed Ingredient'`.
   - `getIngType(i)` falls back to `i.type || i.ingredientType || 'Other'`.
   - `potentialToYieldPercent` falls back to `78.0%` if potential gravity is missing.

---

### 1.3 Database Constraints and Domain Service Cascading Flaws

1. **Foreign Key Restrict Exceptions on Sensor Equipment Deletion**:
   - `BrewYouDbContext.cs:555`:
     ```csharp
     entity.HasOne(sa => sa.Equipment)
           .WithMany()
           .HasForeignKey(sa => sa.EquipmentId)
           .OnDelete(DeleteBehavior.Restrict);
     ```
   - In `EquipmentService.cs:430-443` (`DeleteEquipmentAsync`):
     ```csharp
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
   - **Observation**:
     - `EquipmentService.DeleteEquipmentAsync` does NOT check or remove `BatchSensorAssignments` referencing `id`.
     - If an equipment of type `Sensor` is assigned to any batch stage or step, calling `DELETE /api/v1/inventory/equipment/{id}` executes `_db.Equipment.Remove(equipment)`, which triggers:
       `Microsoft.EntityFrameworkCore.DbUpdateException: update or delete on table "Equipment" violates foreign key constraint "FK_BatchSensorAssignments_Equipment_EquipmentId" on table "BatchSensorAssignments"`
     - This causes an unhandled HTTP 500 server crash.
     - In contrast, `ExecuteReplaceAllCleanupAsync` in `BackupService.cs:2101-2105` explicitly removed `BatchSensorAssignments` before removing equipment.

2. **Omission of HLT and Mash Tun in Equipment Decoupling** (`EquipmentService.cs:431`):
   - `batchesWithEquipment` checks only `b.BoilerId == id || b.FermenterId == id || b.PackagingVesselId == id`.
   - It omits `b.HltId == id` and `b.MashTunId == id`.
   - While PostgreSQL schema has `OnDelete(DeleteBehavior.SetNull)`, in EF Core in-memory database test harnesses, `batch.HltId` and `batch.MashTunId` remain pointing to the deleted entity because EF Core does not execute relational engine triggers.

3. **Omission of Tap Keg Assignment Decoupling** (`EquipmentService.cs:430-444`):
   - When a Keg is deleted, `DeleteEquipmentAsync` does not query or decouple taps referencing the keg (`_db.Equipment.Where(e => e.AssignedKegId == id)`).

4. **Absence of Custom Ingredient Edit/Rename Endpoint**:
   - `IngredientEndpoints.cs:56-234` defines:
     - `GET /api/v1/ingredients/`
     - `GET /api/v1/ingredients/{id}`
     - `POST /api/v1/ingredients/`
     - `PUT /api/v1/ingredients/{id}/stock`
     - `GET /api/v1/ingredients/{id}/usage`
     - `DELETE /api/v1/ingredients/{id}`
   - **Observation**: There is NO `PUT /api/v1/ingredients/{id}` endpoint to update or rename custom ingredients. Users cannot fix typos or rename ingredients without deleting and re-creating them.

5. **Recipe Corruption on Ingredient Deletion** (`IngredientService.cs:383-428`):
   - When a custom ingredient is deleted:
     - It is removed from all `RecipeIngredients` (`db.RecipeIngredients.RemoveRange(affectedRecipeIngredients)`).
     - Recipe metrics are recalculated (`BrewingCalculator.Calculate`).
     - If the deleted ingredient was the only grain/fermentable in the recipe, the recipe is left with 0 fermentables:
       `OriginalGravity = 1.000`, `FinalGravity = 1.000`, `AlcoholByVolume = 0.00%`, `ColorSrm = 0.0`.
     - The recipe is rendered invalid/corrupt without warning.

6. **Broken Pantry Deduction on Active Batches After Ingredient Deletion** (`BatchService.cs:2247-2287`):
   - When an ingredient is deleted, its `IngredientStock` records are deleted (`IngredientService.cs:431-437`).
   - In active batches using that ingredient, `BatchIngredient.SourceIngredientId` points to the deleted ID.
   - During brewing stage transitions, `DeductIngredientStockAsync` attempts:
     ```csharp
     var ingredient = item.SourceIngredientId.HasValue
         ? await _db.Ingredients.FindAsync(item.SourceIngredientId.Value)
         : ...
     ```
   - Because `ingredient` is `null`, stock deduction is silently skipped with no alert or fallback.

---

### 1.4 Existing Automated Test Coverage (`tests/BrewYou.ApiService.Tests/`)

1. **Tests Covering Equipment Deletion**:
   - `EquipmentEndpointTests.cs:312`: `DeleteEquipment_RemovesItemFromList` (isolated equipment with no batch).
   - `EquipmentEndpointTests.cs:744`: `CreateEquipment_AndLinkToBatch_DeleteEquipment_DecouplesBatch` (checks `BoilerId` set to `null`).
   - `BreweryCollaborationEndpointTests.cs:208, 395`: verifies role permissions (Viewer cannot delete, Brewer can delete).
2. **Tests Covering Recipe Deletion**:
   - `RecipeEndpointTests.cs:420`: `DeleteRecipe_WhenOwned_ReturnsSuccessAndRemovesRecipe` (checks batch decoupling).
   - `BackupEndpointsTests.cs:631`: `ExportDeleteRecipeAndRestore_PreservesChecksum...`.
3. **Tests Covering Ingredient Deletion**:
   - `IngredientEndpointTests.cs:615`: `DeleteIngredient_WhenNotUsed_RemovesIngredientAndStock`.
   - `IngredientEndpointTests.cs:624`: `DeleteIngredient_WhenInUseByRecipe_RemovesFromRecipeAndRecalculatesRecipeAbv` (tests 1 recipe with 2 fermentables where 1 is deleted).
   - `IngredientEndpointTests.cs:703`: `DeleteIngredient_WhenCatalogItem_ReturnsBadRequest`.
   - `IngredientEndpointTests.cs:725`: `DeleteIngredient_WhenOwnedByOtherUser_ReturnsForbidden`.
4. **Completely Uncovered Edge Cases**:
   - Deleting a Sensor equipment assigned to a batch via `BatchSensorAssignment` (triggers `DeleteBehavior.Restrict` exception).
   - Deleting equipment assigned as `HltId` or `MashTunId`.
   - Deleting a Keg currently assigned to a Tap (`tap.AssignedKegId`).
   - Deleting equipment while a batch is in active status (`BatchStatus.Brewing`, `BatchStatus.Fermenting`).
   - Deleting a custom ingredient referenced by active batches (`BatchIngredient.SourceIngredientId`).
   - Deleting the sole fermentable or sole hop from a recipe.
   - Deleting an ingredient used across multiple recipes simultaneously.
   - Renaming an ingredient (no service method, endpoint, or test exists).
   - Renaming a recipe while a batch is active.
   - Restoring a backup containing dangling `SourceIngredientId` values on `BatchIngredient`.
   - Restoring a backup referencing external community recipes that were deleted on the target server.

---

## 2. Logic Chain

1. **From Observation 1.1.2 to Dangling Foreign Keys**:
   - `BatchIngredient.SourceIngredientId` is set to `bi.SourceIngredientId` if `ingredientIdMap` does not have the key (`BackupService.cs:2441`).
   - Because `ingredientIdMap` only contains custom ingredients created/matched during the current restore session and catalog ingredients, any ingredient ID that was deleted from the target database is not present in `ingredientIdMap`.
   - Therefore, the restored batch ingredient receives a non-existent Guid, creating a dangling reference in the database.

2. **From Observation 1.1.3 to Lost Recipe Details on Batches**:
   - `BackupPayload` exports full copies of external community recipes in `ReferencedExternalRecipes` (`BackupService.cs:309-323`).
   - On restore, line 1205 only checks if the external recipe already exists live in the target database (`_db.Recipes.FirstOrDefaultAsync(r => r.Id == extRecipe.Id)`).
   - If the community recipe was deleted from the server, `liveExt` is null.
   - Line 1210 does not execute, so `recipeIdMap` does not contain `extRecipe.Id`.
   - Lines 1345-1347 set `mappedRecipeId = null`.
   - Therefore, the batch loses its recipe association even though the backup contained the complete recipe data in `ReferencedExternalRecipes`.

3. **From Observation 1.3.1 to Server 500 Crash on Sensor Deletion**:
   - `BrewYouDbContext.cs:555` establishes `DeleteBehavior.Restrict` between `BatchSensorAssignment.EquipmentId` and `Equipment.Id`.
   - `EquipmentService.DeleteEquipmentAsync` (`EquipmentService.cs:430-443`) decouples `BoilerId`, `FermenterId`, and `PackagingVesselId`, but does not query `BatchSensorAssignments`.
   - When a user deletes a Sensor equipment that is assigned to any batch, EF Core emits `DELETE FROM "Equipment" WHERE "Id" = @p0`.
   - The PostgreSQL relational engine rejects the deletion due to the foreign key constraint restriction.
   - `DeleteEquipmentAsync` has no try/catch for `DbUpdateException`, propagating an unhandled 500 error to the client.

4. **From Observation 1.3.2 & 1.3.3 to Incomplete Equipment Decoupling**:
   - Batches support multi-vessel brewing configurations including `HltId` and `MashTunId` (`Batch.cs:68-72`).
   - Taps support keg dispense via `AssignedKegId` (`Equipment.cs:244`).
   - Because `DeleteEquipmentAsync` only zeroes `BoilerId`, `FermenterId`, and `PackagingVesselId`, `HltId`, `MashTunId`, and `AssignedKegId` are not decoupled in the EF Core context.
   - In in-memory unit tests or detached entity graphs, stale entity associations persist.

5. **From Observation 1.3.5 & 1.3.6 to Recipe Corruption and Broken Deductions**:
   - `IngredientService.DeleteIngredientAsync` (`IngredientService.cs:395`) removes `RecipeIngredient` rows without checking if other fermentables remain.
   - Recalculation sets OG to 1.000.
   - Because `BatchIngredient` items on existing batches are not updated or decoupled, `SourceIngredientId` points to the deleted ingredient.
   - When the brewer attempts to deduct pantry stock during mash or boil, `DeductIngredientStockAsync` (`BatchService.cs:2258-2260`) fails to find the ingredient, silently skipping stock reduction.

---

## 3. Caveats

1. **Relational vs. In-Memory Database Behavior**:
   - The test suite uses the ASP.NET Core `WebApplicationFactory` with EF Core In-Memory database provider (`ApiIntegrationTests.cs`). In-Memory databases do not enforce relational foreign key constraints (`ON DELETE RESTRICT` or `ON DELETE SET NULL`) unless explicitly simulated or tested against PostgreSQL via Testcontainers.
2. **Historical Batch Immutability**:
   - Brewing domain convention suggests that completed historical batches (`BatchStatus.Completed` or `BatchStatus.Archived`) should preserve historical snapshots (ingredient names, grain bill weights, equipment names) as brewed, rather than dynamically mutating when catalog items are renamed. Active batches (`BatchStatus.Brewing`, `BatchStatus.Fermenting`), however, require synchronized references.
3. **BeerXML Specification Limits**:
   - BeerXML 1.0 does not define an independent entity for custom pantry inventory or equipment sensor linkages; equipment profiles in BeerXML are embedded within `<RECIPE>` tags. BrewYou's separation of recipes from equipment setups is architecturally superior, but requires crosswalk adaptations during interchange.

---

## 4. Conclusion

The investigation revealed 5 concrete structural defects, 2 data integrity gaps in the User Backup & Restore pipeline, and significant test coverage voids:

| ID | Domain | Defect / Vulnerability | Severity | Exact Location |
| :--- | :--- | :--- | :--- | :--- |
| **D1** | Equipment Deletion | Foreign key restriction crash (`DbUpdateException` 500) when deleting a Sensor assigned to a batch via `BatchSensorAssignment`. | **High** | `EquipmentService.cs:405-447`, `BrewYouDbContext.cs:555` |
| **D2** | Equipment Deletion | `DeleteEquipmentAsync` fails to decouple `HltId`, `MashTunId`, and `AssignedKegId`, causing stale references. | **Medium** | `EquipmentService.cs:430-440` |
| **D3** | Backup Restore | Restored `BatchIngredient.SourceIngredientId` retains stale archive Guids when the ingredient does not exist in the target DB, creating dangling references. | **High** | `BackupService.cs:2439-2441` |
| **D4** | Backup Restore | Restored batches lose community recipe linkage (`RecipeId` set to `null`) when the recipe was deleted on the target server, ignoring the full snapshot in `ReferencedExternalRecipes`. | **Medium** | `BackupService.cs:1203-1212`, `1345-1347` |
| **D5** | Ingredient Lifecycle | No endpoint exists to edit/rename custom ingredients (`PUT /api/v1/ingredients/{id}` missing). Deleting an ingredient silently empties recipes (reducing OG to 1.000) and breaks batch pantry stock deductions. | **High** | `IngredientEndpoints.cs:56-234`, `IngredientService.cs:362-445` |
| **D6** | Active Batch Guard | Deleting equipment assigned to active batches (`Brewing`, `Fermenting`) is permitted by the backend without restriction. | **Medium** | `EquipmentService.cs:405-447` |

---

## 5. Proposed Solutions & Architecture Plan

### 5.1 User Backup & Restore Remediation

1. **Dangling Guid Elimination in Batch Ingredients** (`BackupService.cs`):
   - In `PopulateBatchChildren` (`BackupService.cs:2439`):
     ```csharp
     Guid? targetSourceId = null;
     if (bi.SourceIngredientId.HasValue && ingredientIdMap.TryGetValue(bi.SourceIngredientId.Value, out var sIngId))
     {
         targetSourceId = sIngId;
     }
     else if (bi.SourceIngredientId.HasValue)
     {
         // Verify whether the Guid exists in the live database before writing
         var liveExists = await _db.Ingredients.AnyAsync(i => i.Id == bi.SourceIngredientId.Value);
         targetSourceId = liveExists ? bi.SourceIngredientId.Value : null;
     }

     batch.Ingredients.Add(new BatchIngredient
     {
         Id = Guid.NewGuid(),
         BatchId = batch.Id,
         SourceIngredientId = targetSourceId,
         Name = string.IsNullOrWhiteSpace(bi.Name) ? "Ingredient" : bi.Name.Trim(),
         ...
     ```
   - If the ingredient cannot be verified, `SourceIngredientId` is set to `null` while preserving the snapshot `Name`.

2. **Cloning Fallback for `ReferencedExternalRecipes`** (`BackupService.cs`):
   - When `liveExt == null` for an item in `payload.ReferencedExternalRecipes`:
     - Clone the recipe snapshot as a private recipe owned by `userId` (named `"{recipe.Name} (Restored)"`), add to `_db.Recipes`, and map `recipeIdMap[extRecipe.Id] = cloned.Id`.
     - Link the batch to the cloned recipe so the brewer retains access to mash profiles, fermentation schedules, and ingredients.

3. **Backup Schema Evolution & Backwards Compatibility** (AGENTS.md Section 3.J):
   - If adding optional metadata (e.g. `RecipeSnapshotDto` on `BatchBackupDto`), mark fields optional/nullable with safe defaults (`null`).
   - Add unit/regression tests in `BackupEndpointsTests.cs` validating `v0`, `v1`, and `v2` fixtures against the new resolution pipeline.

---

### 5.2 Equipment Deletion & Decoupling Remediation

1. **Complete Decoupling in `EquipmentService.DeleteEquipmentAsync`**:
   ```csharp
   // 1. Decouple from all batch equipment assignments (Boiler, Fermenter, Packaging, HLT, Mash Tun)
   var batchesWithEquipment = await _db.Batches
       .Where(b => b.BoilerId == id || b.FermenterId == id || b.PackagingVesselId == id || b.HltId == id || b.MashTunId == id)
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

   // 2. Remove BatchSensorAssignments referencing this equipment (resolves D1 500 error)
   var sensorAssignments = await _db.BatchSensorAssignments
       .Where(sa => sa.EquipmentId == id)
       .ToListAsync();
   if (sensorAssignments.Count > 0)
   {
       _db.BatchSensorAssignments.RemoveRange(sensorAssignments);
   }

   // 3. Decouple keg assignments on any taps
   var tapsWithKeg = await _db.Equipment
       .Where(e => e.AssignedKegId == id)
       .ToListAsync();
   foreach (var tap in tapsWithKeg)
   {
       tap.AssignedKegId = null;
       tap.UpdatedAt = DateTime.UtcNow;
   }
   ```

2. **Active Batch Protection**:
   - Check if the equipment is assigned to any batch where `Status != BatchStatus.Completed && Status != BatchStatus.Archived`.
   - Provide an optional `force` query parameter. Without `force=true`, return `EquipmentAccessResult.InUseByActiveBatch` (HTTP 409 Conflict) with the list of active batch codes, allowing the user to make an informed decision.

---

### 5.3 Ingredient Deletion & Renaming Remediation

1. **Add Custom Ingredient Edit/Rename Endpoint**:
   - Add `UpdateIngredientRequest` DTO and `PUT /api/v1/ingredients/{id}` endpoint in `IngredientEndpoints.cs`.
   - In `IngredientService.UpdateIngredientAsync`:
     - Allow updating `Name`, `Type`, `PotentialGravity`, `ColorSrm`, `AlphaAcidPercent`, `AttenuationPercent`, `Description`, `Form`.
     - Check duplicate name constraints within the user's custom catalog.
     - When renamed: update `Ingredient.Name`.
     - Update all referencing recipes: trigger `BrewingCalculator.Calculate` to refresh metrics if potential gravity or alpha acid changed.
2. **Sole Fermentable Guard in Ingredient Deletion**:
   - In `DeleteIngredientAsync`: check if removing the ingredient would leave any recipe with 0 fermentables. If so, reject deletion with `IngredientOperationResult.WouldCorruptRecipe` ("Cannot delete ingredient: Recipe '{recipe.Name}' would have no fermentables") unless forced or alternative ingredient provided.
3. **Decouple Active Batch Ingredients**:
   - For any active batches referencing the ingredient, set `bi.SourceIngredientId = null` while preserving `bi.Name`.

---

### 5.4 Test Suite Design Plan

Create comprehensive test fixtures and test classes in `tests/BrewYou.ApiService.Tests/`:

1. **`EquipmentCascadingTests.cs`**:
   - `DeleteSensorEquipment_WithBatchSensorAssignment_DecouplesCleanlyWithout500Error`
   - `DeleteEquipment_UsedAsHltOrMashTun_DecouplesBothIds`
   - `DeleteKeg_AssignedToTap_DecouplesTapAssignedKegId`
   - `DeleteEquipment_WithActiveBatch_ReturnsConflictUnlessForced`
   - `RenameEquipment_UpdatesBatchDetailEquipmentNameDynamically`
2. **`IngredientCascadingTests.cs`**:
   - `UpdateIngredient_RenamesCustomIngredient_UpdatesRecipeCalculations`
   - `DeleteIngredient_WhenSoleFermentableInRecipe_ReturnsBadRequest`
   - `DeleteIngredient_ReferencedInActiveBatch_DecouplesSourceIdAndPreservesBatchIntegrity`
3. **`BackupDanglingReferenceTests.cs`**:
   - `RestoreBackup_WithDanglingBatchIngredientSourceId_SetsSourceIdNullWithoutDanglingGuid`
   - `RestoreBackup_WithDeletedExternalCommunityRecipe_ClonesRecipeAndPreservesBatchLink`
   - `RestoreBackup_WithRenamedEquipment_PreservesIdMappingAcrossConflictModes`

---

## 6. Verification Method

To independently verify all findings and validate proposed solutions:

1. **Backend Test Suite Execution**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~BackupEndpointsTests"
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~EquipmentEndpointTests"
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~IngredientEndpointTests"
   ```
2. **Frontend Vitest Suite Execution**:
   ```powershell
   cd src/BrewYou.Web
   npm run test:unit -- src/lib/components/inventory/DeleteEquipmentModal.svelte
   npm run test:unit -- src/lib/components/recipes/RecipeRenameModal.test.ts
   npm run test:unit -- src/lib/components/ingredients/DeleteIngredientModal.test.ts
   npm run test:unit -- src/lib/utils/ingredient-matching.test.ts
   ```
3. **Files to Inspect for Independent Review**:
   - `src/BrewYou.ApiService/Services/EquipmentService.cs` (lines 405-447)
   - `src/BrewYou.ApiService/Data/BrewYouDbContext.cs` (lines 196-200, 552-556)
   - `src/BrewYou.ApiService/Services/BackupService.cs` (lines 1203-1212, 1345-1365, 2439-2441, 2526-2550)
   - `src/BrewYou.ApiService/Services/IngredientService.cs` (lines 362-445)
   - `src/BrewYou.Web/src/lib/components/inventory/DeleteEquipmentModal.svelte` (lines 34-62)
   - `src/BrewYou.Web/src/lib/utils/ingredient-matching.ts` (lines 195-263)
