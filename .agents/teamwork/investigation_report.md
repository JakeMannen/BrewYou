# Fullstack Investigation Report: Cascading Effects of Deleting and Renaming Equipment, Recipes, and Ingredients

**Project**: BrewYou  
**Status**: Completed  
**Authors**: Project Orchestrator (Synthesized from Phase 0 Explorers 1, 2, and 3)  
**Date**: 2026-11-04  

---

## Executive Summary

An exhaustive fullstack audit was conducted across the database schema, EF Core DbContext, backend domain services and endpoints, frontend SvelteKit components and client state, and the User Backup & Restore subsystem (`src/BrewYou.ApiService` and `src/BrewYou.Web`).

The audit revealed critical structural vulnerabilities when equipment, recipes, or ingredients are deleted or renamed:
1. **Equipment Deletion**: Deleting equipment configured as a sensor triggers an unhandled `DbUpdateException` (`Npgsql.PostgresException 23503`: foreign key restrict violation on `BatchSensorAssignment.EquipmentId`), crashing the server with an HTTP 500 error. Equipment deletion also fails to decouple `HltId`, `MashTunId`, and `AssignedKegId`, causing stale references. In the UI, deleted vessels cause batch detail to silently default to arbitrary vessels (`equipmentList[0]`), erroneously recording telemetry.
2. **Recipe Deletion**: `Batch` entities do not persist `RecipeName`, relying solely on the dynamic navigation `b.Recipe?.Name`. When a recipe is deleted, `b.RecipeId` is decoupled to `null`, permanently erasing recipe names from all historical and active batches. In the UI, the batch label printing route (`/batches/[id]/labels/print`) crashes with a blank screen when `recipeId` is null.
3. **Ingredient Deletion**: Custom ingredient deletion silently purges `RecipeIngredients` and recalculates brewing metrics (OG, FG, ABV, IBU, SRM) down without user consent. Deleting the sole grain/fermentable reduces a recipe to OG 1.000 / 0% ABV. In batches, dangling `BatchIngredient.SourceIngredientId` references cause pantry stock deductions to silently fail.
4. **Renaming Deficiencies**: No endpoint or UI capability exists to edit or rename custom ingredients (`PUT /api/v1/ingredients/{id}` is missing), forcing users into destructive delete-and-recreate cycles. Equipment renaming fails to update batch displays due to snapshot precedence, and keg renaming does not update referencing tap cards in client state.
5. **Backup & Restore Vulnerabilities**: Restored batches retain dangling `SourceIngredientId` Guids pointing to non-existent ingredients when the source was deleted on the target database. Community recipes referenced by batches in backups are permanently lost upon restore if deleted on the target server, despite complete recipe snapshots existing in `ReferencedExternalRecipes`.

---

## 1. Concrete Failure Scenarios & Root Causes

| ID | Domain | Operation | Root Cause & File Location | Impact & Result |
| :--- | :--- | :--- | :--- | :--- |
| **FS-01** | Equipment | **Delete Equipment** (Assigned Sensor) | `BatchSensorAssignment.EquipmentId` has `DeleteBehavior.Restrict` (`BrewYouDbContext.cs:555`); not removed or decoupled in `DeleteEquipmentAsync` (`EquipmentService.cs:429-443`). | Unhandled `DbUpdateException` (Postgres 23503) -> **HTTP 500 Internal Server Error**. Sensor cannot be deleted. |
| **FS-02** | Equipment | **Delete Equipment** (HLT or Mash Tun) | `DeleteEquipmentAsync` only zeroes `BoilerId`, `FermenterId`, `PackagingVesselId` (`EquipmentService.cs:431`); omits `HltId` and `MashTunId`. | Stale references in EF Core change tracker / in-memory database test harnesses. |
| **FS-03** | Equipment | **Delete Equipment** (Keg assigned to Tap) | `DeleteEquipmentAsync` does not clear `tap.AssignedKegId` (`Equipment.cs:188`). Frontend `handleDeleted` (`inventory/equipment/+page.svelte:338`) does not clear nested `assignedKeg` on Tap cards. | Taps display stale/ghost keg fill levels until hard reload. |
| **FS-04** | Equipment | **Delete BrewerySetup** (with sensors) | `DeleteSetupAsync` (`BrewerySetupService.cs:321`) cascades deletion to all `Equipment`, hitting the FK Restrict on `BatchSensorAssignment`. | Unhandled `DbUpdateException` -> **HTTP 500 Internal Server Error**. Setup cannot be deleted. |
| **FS-05** | Equipment | **Check Active Batches** | `GetActiveBatchesForEquipmentAsync` (`EquipmentService.cs:462-466`) checks only Boiler, Fermenter, and Packaging vessel; omits HLT, Mash Tun, and Sensors. | `DeleteEquipmentModal.svelte` reports "0 active batches" when equipment is in active use as an HLT, Mash Tun, or Probe. |
| **FS-06** | Equipment | **UI Vessel Fallback** | `batches/[id]/+page.svelte:294-299` unconditionally falls back to `equipmentList[0]` when a stage vessel ID is null. | UI displays arbitrary hardware (e.g. Grain Mill or Tap) as active vessel; manual temperature logs are recorded against the wrong equipment. |
| **FS-07** | Equipment | **UI Packaging Vessel Select** | `StageMeasurementsCard.svelte:634` renders empty `<select>` when batch packaging vessel was deleted; saving sets vessel to Guid.Empty (`00000000-0000-0000-0000-000000000000`). | Corrupted vessel reference saved to batch. |
| **FS-08** | Recipe | **Delete Recipe** (with batches) | `Batch` entity lacks `RecipeName` property (`Batch.cs:26-98`); `DeleteRecipeAsync` sets `batch.RecipeId = null` (`RecipeService.cs:517`); `BatchService.MapToDetailDto` resolves `b.Recipe?.Name`. | **Permanent Data Loss**: All batches lose recipe identity and display null/blank recipe name on batch cards and labels. |
| **FS-09** | Recipe | **Delete Recipe** (active batch) | `DeleteRecipeAsync` has no active batch check or restriction. `DeleteRecipeModal.svelte` displays a generic notice with no batch count or list. | In-progress batches (mid-boil, fermenting) have their recipe unlinked without warning. |
| **FS-10** | Recipe | **Label Print Route Crash** | `batches/[id]/labels/print/+page.svelte:223` has `{:else if batch && label}` with no fallback branch when `recipeId` is null. | **Blank screen crash**: Users cannot view or print bottle labels for decoupled batches. |
| **FS-11** | Recipe | **Batch Creation Wizard Backlink** | `/batches/new?recipeId=<deleted-id>` (`batches/new/+page.svelte:376-382`) fails with unhandled 404 error. | Wizard halts with error banner; invalid ID remains bound. |
| **FS-12** | Ingredient | **Delete Ingredient** (used in recipes) | `IngredientService.DeleteIngredientAsync` (`IngredientService.cs:382-428`) silently purges `RecipeIngredients` and recalculates recipe metrics down without user approval. | Recipe formulations corrupted; deleting sole fermentable results in OG 1.000 / 0% ABV. |
| **FS-13** | Ingredient | **Delete Ingredient** (used in batches) | `BatchIngredient.SourceIngredientId` left dangling. `BatchService.DeductIngredientStockAsync` (`BatchService.cs:2258-2260`) checks `SourceIngredientId.HasValue` without fallback to name matching. | Inventory deductions and refunds silently fail during brewing sessions. |
| **FS-14** | Ingredient | **Missing Usage in Delete Modal** | `IngredientService.GetIngredientUsageAsync` (`IngredientService.cs:345`) only queries `RecipeIngredients`, ignoring `BatchIngredients`. | Modal reports 0 usages even when active batches use the ingredient. |
| **FS-15** | Ingredient | **Recipe Editor Raw UUID** | `IngredientsCard.svelte:232` renders raw UUID strings when ingredient is missing; `RecipeFormulator.svelte:525` silently excludes it from calculations; saving fails with generic 400 Bad Request (`RecipeService.cs:320`). | Confusing UI, skewed live metrics, and unhelpful save error. |
| **FS-16** | Ingredient | **Rename Ingredient** | No endpoint or service method exists for updating/renaming custom ingredients (`PUT /api/v1/ingredients/{id}` missing in `IngredientEndpoints.cs`). | Users cannot rename ingredients or fix typo/alpha acid values without destructive delete and recreate. |
| **FS-17** | Backup | **Dangling Ingredient Guids on Restore** | `BackupService.cs:2439-2441` falls back to `bi.SourceIngredientId` when not found in `ingredientIdMap`. | Stale Guids pointing to non-existent ingredients written to database. |
| **FS-18** | Backup | **Lost Community Recipes on Restore** | `BackupService.cs:1203-1212` nulls `batch.RecipeId` if referenced external community recipe was deleted on target server, ignoring the full snapshot in `ReferencedExternalRecipes`. | Batches lose recipe association upon backup restore. |

---

## 2. Comprehensive Solution Proposals

### Solution Architecture 1: Equipment Deletion & Active Safeguards
1. **Clean Service-Level Decoupling (`EquipmentService.DeleteEquipmentAsync`)**:
   - Decouple all batch vessels: `BoilerId`, `FermenterId`, `PackagingVesselId`, `HltId`, and `MashTunId`.
   - Remove or decouple `BatchSensorAssignments` for `equipmentId` prior to removing the equipment entity, preventing FK 23503 `DbUpdateException`.
   - Clear `AssignedKegId` from any taps referencing the deleted equipment (`_db.Equipment.Where(e => e.AssignedKegId == id)`).
   - If deleting a Tap assigned to a batch (`equipment.AssignedBatchId != null`), clear the batch assignment and reset tap volume.
   - Wrap `SaveChangesAsync()` in a try/catch for `DbUpdateException` to return clean domain errors (`EquipmentAccessResult.Conflict`) rather than propagating 500s.
2. **Comprehensive Active Batch Detection (`EquipmentService.GetActiveBatchesForEquipmentAsync`)**:
   - Query must inspect all 5 vessel foreign keys (`BoilerId`, `FermenterId`, `PackagingVesselId`, `HltId`, `MashTunId`) AND `sa.EquipmentId` in `BatchSensorAssignment`.
   - Restrict deletion if assigned to an active batch (`Planned`, `Brewing`, `Fermenting`, `Conditioning`), returning `409 Conflict` with the active batch list unless forced.
3. **BrewerySetup Deletion Guard (`BrewerySetupService.DeleteSetupAsync`)**:
   - Clean up `BatchSensorAssignments` for all equipment in the setup prior to removing `BrewerySetup`.
4. **Frontend UI Fixes**:
   - `DeleteEquipmentModal.svelte`: Display complete active batch and sensor assignment warnings.
   - `batches/[id]/+page.svelte`: Remove arbitrary fallback to `equipmentList[0]`. Render explicit unassigned state (`t('batches.workspace.no_vessel_assigned')`).
   - `inventory/equipment/+page.svelte`: In `handleDeleted`, clear `assignedKeg` and `assignedKegId` on any Tap cards in local state.
   - `StageMeasurementsCard.svelte`: Gracefully handle missing/deleted packaging vessel ID.

### Solution Architecture 2: Recipe Deletion & Renaming Integrity
1. **Snapshot `RecipeName` on `Batch` Entity**:
   - Add `public string? RecipeName { get; set; }` to `Batch` (`Batch.cs`).
   - Populate `RecipeName = recipe?.Name` in `BatchService.CreateBatchAsync`.
   - In `RecipeService.DeleteRecipeAsync`, persist `batch.RecipeName ??= recipe.Name` before setting `batch.RecipeId = null`.
   - In `BatchService.MapToDetailDto`, resolve `RecipeName: b.Recipe?.Name ?? b.RecipeName`.
   - Update `BackupContracts.cs` (`BatchBackupDto`), `BackupService.cs`, and `api.ts` to include optional `RecipeName` preserving full backwards compatibility (AGENTS.md Section 3.J).
2. **Active Batch Recipe Deletion Restriction**:
   - In `RecipeService.DeleteRecipeAsync`, check if linked batches are active. Return `409 Conflict` (`RecipeAccessResult.ActiveBatchesInProgress`) if active batches exist.
   - In `DeleteRecipeModal.svelte`, query linked batches and display referencing active/completed batch count.
3. **Frontend Label Printing Fallback**:
   - In `batches/[id]/labels/print/+page.svelte`, add an `{:else}` branch under line 223 rendering a clean default bottle label from batch metadata (`batch.name`, `batch.batchCode`, `batch.beerStyle`, `batch.targetAbv`, `batch.recipeName`) when `label` is null.
4. **Batch Creation Wizard Graceful Fallback**:
   - In `batches/new/+page.svelte`, catch 404 when loading `recipeId` from URL query parameter, clear `selectedRecipeId`, and display a non-blocking toast.

### Solution Architecture 3: Ingredient Deletion & Renaming Integrity
1. **Implement Custom Ingredient Edit/Rename Endpoint**:
   - Add `UpdateIngredientRequest` DTO in `src/BrewYou.ApiService/DTOs/` and endpoint `PUT /api/v1/ingredients/{id}` in `IngredientEndpoints.cs`.
   - Implement `IngredientService.UpdateIngredientAsync`: validate user ownership, name uniqueness, update properties, and recalculate dependent recipes if gravity/alpha acid changed.
   - Add frontend API client method `api.ingredients.update(id, payload)` and an Edit Ingredient modal in `src/BrewYou.Web/src/lib/components/ingredients/`.
2. **Deletion Restrictions & Recipe Protection**:
   - In `IngredientService.DeleteIngredientAsync`, restrict deletion if ingredient is used in any recipe (`affectedRecipeIngredients.Count > 0`), returning `(IngredientOperationResult.InUseByRecipes, "Cannot delete ingredient in use by {count} recipe(s)")` instead of silently gutting recipes.
   - Check batch usages (`db.BatchIngredients`) and prevent deletion if active batches reference it.
   - Update `IngredientService.GetIngredientUsageAsync` to include batch usages.
3. **Robust Stock Deduction Fallback**:
   - In `BatchService.DeductIngredientStockAsync` and `RefundIngredientStockAsync`, if `SourceIngredientId` does not find a live ingredient, fall back to matching by name (`FirstOrDefaultAsync(i => i.Name.ToLower() == item.Name.ToLower())`).
4. **Frontend Editor Fallback**:
   - In `IngredientsCard.svelte`, when `ing` is undefined, render localized placeholder `t('formulator.deleted_or_missing_ingredient')` with warning badge rather than raw UUIDs.

### Solution Architecture 4: Backup & Restore Backwards Compatibility & Dangling Reference Cleanup
1. **Dangling Guid Elimination (`BackupService.cs`)**:
   - In `PopulateBatchChildren`, if `bi.SourceIngredientId` is not found in `ingredientIdMap`, check if it exists in `_db.Ingredients`. If not found, assign `null` rather than writing a dangling archive Guid to the database.
2. **Community Recipe Fallback Clone on Restore**:
   - When restoring batches referencing external community recipes that no longer exist on the server, clone the recipe snapshot from `ReferencedExternalRecipes` as a personal recipe (`"{recipe.Name} (Restored)"`) and link the batch to the cloned recipe.
3. **Backwards Compatibility Fixtures**:
   - Maintain `v0`, `v1`, and `v2` backup test coverage in `BackupEndpointsTests.cs`. Verify that backups with and without `RecipeName` restore cleanly.

---

## 3. Verification & Acceptance Criteria Alignment

| Acceptance Criterion | Verification Method |
| :--- | :--- |
| **Deleting equipment, recipes, ingredients leaves no orphaned records, broken FKs, or 500 errors** | Automated tests asserting: (a) deleting sensor decouples `BatchSensorAssignment` with 200 OK, (b) deleting equipment decouples HLT/MashTun/Keg cleanly, (c) deleting ingredient is restricted if in use or decouples batch references cleanly. |
| **Renaming equipment, recipes, ingredients updates referencing views consistently** | Automated tests asserting: (a) `PUT /api/v1/ingredients/{id}` renames custom ingredient and updates recipe vitals, (b) renaming equipment updates live batch detail, (c) recipe rename updates batch detail `RecipeName`. |
| **User Backup & Restore backwards compatibility & round-trip consistency** | Automated tests in `BackupEndpointsTests.cs` verifying round-trip export/restore with `RecipeName`, dangling Guid sanitization, and backwards compatibility with legacy schema fixtures. |
| **Full test suite passes with zero failures** | Complete execution of backend tests (`dotnet test`) and frontend tests (`npm run test:unit`, `npm run check`) with 0 errors. |
