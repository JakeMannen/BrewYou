# Cascading Effects Survey: Frontend UI, Client State & API Contracts

## 1. Observation

This survey directly audited all frontend routes, components, client stores, and API contracts in `src/BrewYou.Web/`, in conjunction with database foreign keys in `BrewYouDbContext.cs` and service endpoints in `src/BrewYou.ApiService/`.

### A. Equipment Deletion Observations

1. **DeleteEquipmentModal Active Batches Filter Incompleteness**:
   - Location: `src/BrewYou.Web/src/lib/components/inventory/DeleteEquipmentModal.svelte:38-39` calls `api.equipment.getActiveBatches(equipmentId)`.
   - Backend implementation: `src/BrewYou.ApiService/Services/EquipmentService.cs:463-465`:
     ```csharp
     .Where(b => (b.UserId == userId || (b.BrewerySetupId != null && b.BrewerySetupId == equipment.BrewerySetupId)) &&
                 (b.BoilerId == equipmentId || b.FermenterId == equipmentId || b.PackagingVesselId == equipmentId) &&
                 b.Status != BatchStatus.Completed &&
                 b.Status != BatchStatus.Archived)
     ```
   - **Observation**: The query only checks `BoilerId`, `FermenterId`, and `PackagingVesselId`. It omits `b.HltId` and `b.MashTunId`. Furthermore, it completely ignores `BatchSensorAssignment` (`BrewYouDbContext.cs:552-556`).
   - If a brewer attempts to delete an equipment profile configured as an HLT, a separate Mash Tun, or an active Probe/Sensor, `activeBatches` returns empty `[]`. No warning banner is displayed (`DeleteEquipmentModal.svelte:144`), leading the user to believe the equipment is not in use.

2. **Foreign Key Restrict Violation & Unhandled 500 Error on Sensor Delete**:
   - Location: `src/BrewYou.ApiService/Data/BrewYouDbContext.cs:552-556`:
     ```csharp
     entity.HasOne(sa => sa.Equipment)
           .WithMany()
           .HasForeignKey(sa => sa.EquipmentId)
           .OnDelete(DeleteBehavior.Restrict);
     ```
   - Backend: `src/BrewYou.ApiService/Services/EquipmentService.cs:429-443`:
     `DeleteEquipmentAsync` decouples `b.BoilerId`, `b.FermenterId`, `b.PackagingVesselId`, but does NOT remove or decouple `BatchSensorAssignment`.
   - Frontend handling: `src/BrewYou.Web/src/lib/components/inventory/DeleteEquipmentModal.svelte:57-59`:
     ```svelte
     } catch (err: unknown) {
         errorMessage = (err as Error).message || t('equipment.delete_failed');
     }
     ```
   - **Observation**: Deleting an equipment assigned to a batch sensor triggers a database foreign key constraint violation (`Npgsql.PostgresException: 23503: update or delete on table "Equipment" violates foreign key constraint "FK_BatchSensorAssignments_Equipment_EquipmentId"`), causing the backend to throw a 500 Internal Server Error. The client modal displays a raw `HTTP error 500` error banner with no actionable explanation.

3. **Silent Arbitrary Equipment Fallback in Batch Workspace**:
   - Location: `src/BrewYou.Web/src/routes/batches/[id]/+page.svelte:293-299`:
     ```typescript
     // 5. Fallback: first equipment in list if any
     if (equipmentList.length > 0) {
         return {
             id: equipmentList[0].id,
             name: equipmentList[0].name
         };
     }
     ```
   - **Observation**: If an equipment assigned to a batch stage is deleted (so `currentBatch.mashTunId`, `boilerId`, `fermenterId`, or `packagingVesselId` becomes null), `currentStageEquipment` falls back to `equipmentList[0]`. If `equipmentList[0]` is a Grain Mill or a Tap or an unrelated vessel, the UI displays that arbitrary equipment name as the active vessel, and any manual temperature reading or logging action records telemetry against `equipmentList[0]`.

4. **Stale Tap-to-Keg Relationships in Client State**:
   - Location: `src/BrewYou.Web/src/routes/inventory/equipment/+page.svelte:337-339`:
     ```typescript
     function handleDeleted(equipmentId: string) {
         equipmentList = equipmentList.filter((e) => e.id !== equipmentId);
     }
     ```
   - **Observation**: When a Keg equipment is deleted, `handleDeleted` removes the keg from `equipmentList`. However, if any Tap equipment in `equipmentList` was assigned to that keg (`tap.assignedKegId === equipmentId`), `tap.assignedKeg` is NOT cleared or updated. The Tap card continues to render the deleted keg's fill level and name until a hard browser reload.

5. **Packaging Vessel Select Missing Option**:
   - Location: `src/BrewYou.Web/src/lib/components/batches/StageMeasurementsCard.svelte:634-644`:
     ```svelte
     <select id="edit-packaging-vessel" bind:value={draftPackagingVesselId} ...>
         <option value="">{t('batches.workspace.bottled_cellared_option')}</option>
         {#each equipmentList.filter((e) => e.type === 'Keg') as keg}
             <option value={keg.id}>{keg.name} ({keg.capacity}L)</option>
         {/each}
     </select>
     ```
   - **Observation**: If `batch.packagingVesselId` references a deleted keg, `equipmentList` does not contain it. The `<select>` element has no `<option>` matching `draftPackagingVesselId`, rendering an empty select box. If saved, line 146 converts it to `'00000000-0000-0000-0000-000000000000'`.

---

### B. Recipe Deletion Observations

1. **DeleteRecipeModal Lacks Reference Counts & Impact Warning**:
   - Location: `src/BrewYou.Web/src/lib/components/recipes/DeleteRecipeModal.svelte:111-119`:
     ```svelte
     <!-- Batch Preservation Notice -->
     <div class="flex items-start gap-3 rounded-xl border border-amber-500/20 bg-amber-500/5 p-3.5 text-xs text-amber-800 dark:border-amber-500/15 dark:text-amber-300">
         <Info class="mt-0.5 h-4 w-4 shrink-0 text-amber-600 dark:text-amber-400" />
         <p class="leading-relaxed">
             {t('recipes.delete_modal.batches_notice')}
         </p>
     </div>
     ```
   - **Observation**: Unlike `DeleteEquipmentModal` and `DeleteIngredientModal`, `DeleteRecipeModal` does not check for referencing batches (`api.batches.list` or a usage endpoint). The user is not informed which active, planned, or completed batches were brewed from this recipe.

2. **Blank Screen Crash on Batch Label Print Route**:
   - Location: `src/BrewYou.Web/src/routes/batches/[id]/labels/print/+page.svelte:62-64, 217-224`:
     ```typescript
     if (b.recipeId) {
         label = await api.recipes.label.get(b.recipeId);
     }
     ```
     ```svelte
     {#if loading}
         ...
     {:else if errorMessage}
         ...
     {:else if batch && label}
         <!-- Printable Labels Sheet Canvas -->
     {/if}
     ```
   - **Observation**: When a batch's linked recipe is deleted, `b.recipeId` is decoupled to `null` by `RecipeService.cs:517`. In `labels/print/+page.svelte`, `b.recipeId` is null, so `label` remains `null`. The template has `{:else if batch && label}` with **NO `{:else}` branch**. The user sees a completely blank page with no canvas, no message, and no option to generate a standalone or default batch label.

3. **Stale Backlink in Batch Wizard Route**:
   - Location: `src/BrewYou.Web/src/routes/batches/new/+page.svelte:376-382, 400-440`:
     ```typescript
     const urlRecipeId = page.url.searchParams.get('recipeId');
     if (urlRecipeId) {
         selectedRecipeId = urlRecipeId;
         await handleRecipeSelect(urlRecipeId);
     }
     ```
   - **Observation**: If a user navigates to `/batches/new?recipeId=<deletedId>` (via bookmark or browser history), `api.recipes.getById` throws an `ApiClientError` (404 Not Found), triggering line 440 `error = (err as Error).message`. The batch wizard halts on step 1 with an error banner, and `selectedRecipeId` remains set to the invalid ID.

---

### C. Ingredient Deletion Observations

1. **Silent Recalculation and Cascading Destruction in Recipes**:
   - Location: `src/BrewYou.ApiService/Services/IngredientService.cs:382-428`:
     When an ingredient is deleted, the backend permanently deletes all `RecipeIngredient` rows across all recipes referencing that ingredient (`db.RecipeIngredients.RemoveRange(affectedRecipeIngredients)`), and silently recalculates the recipes' OG, FG, ABV, IBU, and SRM metrics (`BrewingCalculator.Calculate`).
   - Modal UI: `src/BrewYou.Web/src/lib/components/ingredients/DeleteIngredientModal.svelte:116-142` warns "This ingredient is used in X recipes" and changes button text to "Remove from recipes & Delete", but does NOT warn the user that their recipes' gravity, bitterness, and SRM calculations will change permanently without their review.

2. **Batch Ingredient Usage Missing in DeleteIngredientModal**:
   - Location: `src/BrewYou.ApiService/Services/IngredientService.cs:345-357`:
     `GetIngredientUsageAsync` only queries `db.RecipeIngredients`.
   - **Observation**: It does NOT check `db.BatchIngredients` or active batches in fermentation/brewing. If an ingredient is used in active batches but not in any recipe, `DeleteIngredientModal` reports 0 usages.

3. **Broken Recipe Editor & Raw UUID Display**:
   - Location: `src/BrewYou.Web/src/lib/components/recipes/IngredientsCard.svelte:230-234, 380-384, 536-540`:
     ```svelte
     {#if item.ingredientId && !availableFermentables.some((f) => f.id === item.ingredientId)}
         <option value={item.ingredientId}>
             {ing ? getLocalizedIngredientName(ing) : item.notes || item.ingredientId}
         </option>
     {/if}
     ```
   - Location: `src/BrewYou.Web/src/lib/components/recipes/RecipeFormulator.svelte:513-526`:
     ```typescript
     const calcItems: CalculatorItem[] = items
         .map((item) => {
             const ing = currentCatalogIngredients.find((i) => i.id === item.ingredientId);
             return { ingredient: ing!, ... };
         })
         .filter((ci) => Boolean(ci.ingredient));
     ```
   - **Observation**: If an ingredient is deleted, `ing` is undefined:
     - In `IngredientsCard.svelte`, the dropdown renders raw GUIDs (e.g., `3fa85f64-5717-...`).
     - In `RecipeFormulator.svelte`, `filter(ci => Boolean(ci.ingredient))` silently excludes the deleted ingredient from live metric calculation. The brewer sees unexpected drops in OG, SRM, or IBU with no indication why.
     - When saving the recipe, line 775 sends the invalid `ingredientId` in the payload, causing the backend `RecipeService.cs:320` to reject the update with `400 Bad Request` ("One or more ingredients do not exist or are not accessible."). The formulator displays `saveError` without identifying which ingredient row is causing the failure.

4. **Batch Inventory Stock Deduction Failure**:
   - Location: `src/BrewYou.ApiService/Services/BatchService.cs:2258-2260, 2300-2302`:
     ```csharp
     var ingredient = item.SourceIngredientId.HasValue
         ? await _db.Ingredients.FindAsync(item.SourceIngredientId.Value)
         : await _db.Ingredients.FirstOrDefaultAsync(i => i.Name.ToLower() == item.Name.ToLower());
     ```
   - **Observation**: If `SourceIngredientId` references a deleted ingredient, `FindAsync` returns `null`. The code does NOT fall back to name matching because `item.SourceIngredientId.HasValue` is true. Consequently, no inventory stock deduction or refund occurs when checking off ingredients in the batch brew view.

---

### D. Renaming Operations Observations

1. **Equipment Renaming — Stale Snapshots in Batch Views**:
   - Location: `src/BrewYou.Web/src/routes/batches/[id]/+page.svelte:548, 560, 572, 596`:
     ```typescript
     equipmentName = batch!.mashTunName ?? batch!.boilerName ?? mashVessel?.name;
     equipmentName = batch!.boilerName ?? boiler?.name;
     equipmentName = batch!.fermenterName ?? fermenter?.name;
     equipmentName = batch!.packagingVesselName ?? pkgVessel?.name;
     ```
   - **Observation**: Priority is given to `batch.fermenterName` (the snapshotted name at batch creation) over `fermenter?.name` (the live equipment name from `equipmentList`). Therefore, renaming an equipment profile in `/inventory/equipment` does NOT update the vessel names displayed in `BrewStageTracker` or stage banners for existing batches.

2. **Equipment Renaming — Stale Keg Name on Tap Cards**:
   - Location: `src/BrewYou.Web/src/routes/inventory/equipment/+page.svelte:317-330`:
     `handleSaved(saved)` updates `equipmentList[index] = saved`. If a Keg is renamed, the Keg card updates, but any Tap card in `equipmentList` referencing that keg via `tap.assignedKeg.name` retains the old name until page reload.

3. **Recipe Renaming — Propagation and Behavior**:
   - Location: `src/BrewYou.ApiService/Services/BatchService.cs:1730`:
     `RecipeName: b.Recipe?.Name`
   - Location: `src/BrewYou.Web/src/routes/batches/[id]/labels/print/+page.svelte:254`:
     `<span class="truncate">{batch.recipeName}</span>`
   - **Observation**: When a recipe is renamed, the backend dynamic mapping `b.Recipe?.Name` resolves to the new name on subsequent fetches. However, `b.Name` (the batch name, e.g. "Citra IPA") is not modified, which is expected domain behavior since batch names are independent instances.

4. **Ingredient Renaming — Missing System Capability**:
   - Location: `src/BrewYou.ApiService/Endpoints/IngredientEndpoints.cs:141-168` and `src/BrewYou.Web/src/lib/api/client.ts:409-415`:
   - **Observation**: The system only provides `PUT /api/v1/inventory/ingredients/{id}/stock` (`UpdateIngredientStock`). There is NO endpoint or UI modal to update an ingredient's name, type, or brewing properties (`potentialGravity`, `colorSrm`, `alphaAcidPercent`). If a user misspells an ingredient name or needs to update its alpha acid content, their only option is to delete and recreate it, triggering the cascading deletions described in Section C.

---

## 2. Logic Chain

1. **From Observation A.1 & A.2 to Restrict Deletion Failure**:
   - Step 1: `BatchSensorAssignment` enforces `DeleteBehavior.Restrict` on `EquipmentId` (`BrewYouDbContext.cs:555`).
   - Step 2: `EquipmentService.DeleteEquipmentAsync` attempts `_db.Equipment.Remove(equipment)` without clearing referencing `BatchSensorAssignment` entities (`EquipmentService.cs:442`).
   - Step 3: `DeleteEquipmentModal` queries `getActiveBatches`, which ignores `BatchSensorAssignment` (`EquipmentService.cs:463-465`), falsely reassuring the user.
   - Step 4: The database throws a foreign key constraint violation. The backend returns 500. The frontend displays an unhandled `HTTP error 500` banner (`DeleteEquipmentModal.svelte:58`).

2. **From Observation A.3 to Incorrect Telemetry Recording**:
   - Step 1: When equipment is deleted, batch foreign keys (`HltId`, `MashTunId`, etc.) are nulled (`BrewYouDbContext.cs:376-399`).
   - Step 2: `batches/[id]/+page.svelte` derives `currentStageEquipment`. When the specific stage vessel ID is null, it falls through cases 1–4 to case 5 (`batches/[id]/+page.svelte:294`).
   - Step 3: Case 5 unconditionally returns `equipmentList[0]` as a fallback.
   - Step 4: Temperature logging modals (`LogStepTemperatureModal.svelte:70`) and HUD cards default to `equipmentList[0]`. Measurements are recorded against the wrong hardware vessel.

3. **From Observation B.2 to Printable Label Route Crash**:
   - Step 1: Deleting a recipe nulls `Batch.RecipeId` (`RecipeService.cs:517`).
   - Step 2: `batches/[id]/labels/print/+page.svelte:62` skips `label` retrieval when `b.recipeId` is null.
   - Step 3: `label` remains `null`.
   - Step 4: Line 223 `{:else if batch && label}` evaluates to false, with no trailing `{:else}` block.
   - Step 5: The print preview sheet renders completely blank, locking the user out of bottle label printing for standalone or decoupled batches.

4. **From Observation C.1 & C.3 to Broken Recipe Formulator**:
   - Step 1: Deleting an ingredient removes all `RecipeIngredient` rows across all recipes (`IngredientService.cs:395`).
   - Step 2: If a recipe is open in `RecipeFormulator`, or if an ingredient is deleted concurrently, `catalogIngredients.find` fails (`RecipeFormulator.svelte:515`).
   - Step 3: IngredientsCard renders raw UUID strings in `<option>` elements (`IngredientsCard.svelte:232`).
   - Step 4: Calculation excludes the missing ingredient (`RecipeFormulator.svelte:525`), silently skewing recipe vitals.
   - Step 5: Saving sends the stale `ingredientId`, failing with `400 Bad Request` (`RecipeService.cs:320`) and showing a generic error that does not point out which row failed.

5. **From Observation D.4 to Forced Destructive Workflow**:
   - Step 1: The API lacks an endpoint to update ingredient metadata.
   - Step 2: Users needing to rename an ingredient or correct an alpha acid rating must delete the ingredient.
   - Step 3: Deleting cascades destructively into all recipes using that ingredient (Observation C.1), silently altering their vitals without an explicit user intent to alter those recipes.

---

## 3. Caveats

1. **Browser Cache Behavior**: The analysis assumes standard SvelteKit SPA navigation. Client stores (`dashboard.svelte.ts`) reload on mount/destroy cycles, but single-page navigation within `/inventory/equipment` relies on local reactive arrays (`equipmentList`).
2. **Catalog vs. Custom Ingredients**: Catalog ingredients are protected from deletion (`IsCatalogItem` check in `IngredientService.cs:372`). The failure modes in Section C apply specifically to custom user-created ingredients.
3. **Multi-User / Brewery Setup Sync**: If one brewery collaborator deletes or renames equipment while another collaborator has an active batch open, WebSocket/SSE telemetry streams update readings, but do not broadcast entity schema invalidation events.

---

## 4. Conclusion

The fullstack investigation reveals four critical categories of cascading failure modes:

| Category | Primary Failure Mode | User Impact |
| :--- | :--- | :--- |
| **Equipment Deletion** | FK Restrict violation in `BatchSensorAssignment` & incomplete active batch check | 500 Internal Server Error when deleting probes; silent fallback to arbitrary `equipmentList[0]` in batch detail |
| **Recipe Deletion** | Blank screen in `batches/[id]/labels/print` | Batches whose parent recipe was deleted cannot render or print bottle labels |
| **Ingredient Deletion** | Silent removal from recipes with vital recalculation; raw UUID display in editor; broken stock deduction | Recipe vitals altered without brewer intent; recipe editor locked with 400 error; inventory deduction disabled |
| **Renaming** | Precedence of snapshot names over live equipment names; missing ingredient update API | Renamed vessels do not reflect on batches; ingredients cannot be renamed without deletion |

### Concrete Proposed Solutions

1. **Equipment Deletion & Sensor Decoupling**:
   - **Backend**: In `EquipmentService.DeleteEquipmentAsync`, explicitly decouple or remove `BatchSensorAssignment` referencing the equipment before removing the equipment, or update `GetActiveBatchesForEquipmentAsync` to check `HltId`, `MashTunId`, and `BatchSensorAssignment`. Return `409 Conflict` with a user-friendly code (`EQUIPMENT_IN_USE`) if active batches or sensors reference the equipment.
   - **Frontend**: Update `DeleteEquipmentModal.svelte` to display all usages (including HLT, Mash Tun, and active sensor assignments). In `batches/[id]/+page.svelte:294`, remove the arbitrary fallback to `equipmentList[0]` and instead render an unassigned vessel placeholder (`t('batches.workspace.no_vessel_assigned')`).

2. **Recipe Deletion & Graceful Label Fallback**:
   - **Modal Warning**: Extend `DeleteRecipeModal.svelte` to query referencing batches and display the batch count and list.
   - **Label Printing Fallback**: In `batches/[id]/labels/print/+page.svelte`, add an `{:else}` branch under line 223 that renders a clean, stylized default batch label using `batch.name`, `batch.batchCode`, `batch.beerStyle`, `batch.targetAbv`, and `srmToHexColor(batch.colorSrm)` whenever `label` is null.

3. **Ingredient Deletion Protection & Restriction**:
   - **Restrict Deletion**: In `IngredientService.DeleteIngredientAsync`, restrict deletion if the ingredient is used in any recipe or batch, returning `409 Conflict` (`INGREDIENT_IN_USE`) with the list of affected recipes, OR provide an explicit replacement ingredient selector.
   - **UI Graceful Fallback**: In `IngredientsCard.svelte`, when `ing` is undefined, render `t('formulator.deleted_ingredient', { id: item.ingredientId.slice(0, 8) })` with a warning icon, allowing the brewer to pick a replacement.
   - **Batch Stock Deduction**: In `BatchService.cs:2260`, if `SourceIngredientId` is deleted, fall back to matching by ingredient name.

4. **Ingredient Renaming Endpoint**:
   - Implement `PUT /api/v1/inventory/ingredients/{id}` (`UpdateIngredientRequest`) in `IngredientEndpoints.cs` and `IngredientService.cs`, enabling brewers to rename custom ingredients and update properties without deleting and breaking recipes.

5. **Client Store & Nested State Invalidation**:
   - In `inventory/equipment/+page.svelte:handleDeleted`, when a Keg is deleted, iterate through `equipmentList` to clear `assignedKeg` and `assignedKegId` on any Tap objects.
   - In `batches/[id]/+page.svelte:548-596`, use live equipment names when available (`fermenter?.name ?? batch!.fermenterName`).

---

## 5. Verification Method

### Local Independent Verification Steps

1. **Inspect Target Files**:
   - `src/BrewYou.Web/src/lib/components/inventory/DeleteEquipmentModal.svelte` (lines 38, 58, 144)
   - `src/BrewYou.ApiService/Services/EquipmentService.cs` (lines 429-444, 463-470)
   - `src/BrewYou.ApiService/Data/BrewYouDbContext.cs` (lines 199, 552-556)
   - `src/BrewYou.Web/src/routes/batches/[id]/+page.svelte` (lines 202, 211, 294-299, 548, 572)
   - `src/BrewYou.Web/src/routes/batches/[id]/labels/print/+page.svelte` (lines 62-64, 223)
   - `src/BrewYou.Web/src/lib/components/recipes/IngredientsCard.svelte` (lines 230-234, 380-384)
   - `src/BrewYou.Web/src/lib/components/recipes/RecipeFormulator.svelte` (lines 525, 775)
   - `src/BrewYou.ApiService/Services/IngredientService.cs` (lines 382-428)

2. **Automated Test Execution**:
   - Frontend unit test suite:
     ```powershell
     cd src\BrewYou.Web
     npm run test:unit
     ```
   - Frontend type check:
     ```powershell
     cd src\BrewYou.Web
     npm run check
     ```
   - Backend unit and integration test suites:
     ```powershell
     dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj
     ```

3. **Invalidation Conditions**:
   - If `EquipmentService.DeleteEquipmentAsync` is modified to cascade-delete or decouple `BatchSensorAssignment`, the 500 error will no longer reproduce.
   - If `batches/[id]/labels/print/+page.svelte` is given a default label fallback for null `recipeId`, the blank page crash will no longer occur.
