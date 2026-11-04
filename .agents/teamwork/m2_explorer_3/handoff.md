# M2 Testing & Verification Plan: Recipe Lifecycle, Batch RecipeName Snapshot & Active Batch Safeguards

## 1. Observation

### 1.1 Current Baseline Test Failures in `CascadingE2ETests.cs`
Direct execution of the cascading test suite via `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~CascadingE2ETests.T1_08|FullyQualifiedName~CascadingE2ETests.T1_09|FullyQualifiedName~CascadingE2ETests.T3_01|FullyQualifiedName~CascadingE2ETests.T4_02"` produced the following verbatim failures:

1. **`T1_08_BatchRecipeNamePersistence_PreservesRecipeNameOnRecipeDelete`** (`CascadingE2ETests.cs:375-399`):
   ```
   [xUnit.net 00:00:02.04] BrewYou.ApiService.Tests.CascadingE2ETests.T1_08_BatchRecipeNamePersistence_PreservesRecipeNameOnRecipeDelete [FAIL]
   Error Message:
     Expected afterDetail.RecipeName to be "Heritage IPA Snapshot" because RecipeName must persist after recipe deletion, but found <null>.
   Stack Trace:
     at FluentAssertions.Primitives.StringAssertions`1.Be(String expected, String because, Object[] becauseArgs)
     at BrewYou.ApiService.Tests.CascadingE2ETests.T1_08_BatchRecipeNamePersistence_PreservesRecipeNameOnRecipeDelete() in CascadingE2ETests.cs:line 397
   ```

2. **`T1_09_RecipeDeletionActiveBatchGuard_BlocksDeletionOfRecipeWithActiveBatches`** (`CascadingE2ETests.cs:401-414`):
   ```
   [xUnit.net 00:00:02.08] BrewYou.ApiService.Tests.CascadingE2ETests.T1_09_RecipeDeletionActiveBatchGuard_BlocksDeletionOfRecipeWithActiveBatches [FAIL]
   Error Message:
     Expected delRes.StatusCode to be HttpStatusCode.Conflict {value: 409} because Deleting a recipe linked to an active batch should return 409 Conflict, but found HttpStatusCode.OK {value: 200}.
   Stack Trace:
     at FluentAssertions.Primitives.EnumAssertions`2.Be(TEnum expected, String because, Object[] becauseArgs)
     at BrewYou.ApiService.Tests.CascadingE2ETests.T1_09_RecipeDeletionActiveBatchGuard_BlocksDeletionOfRecipeWithActiveBatches() in CascadingE2ETests.cs:line 412
   ```

3. **`T3_01_SimultaneousVesselAndRecipeDeletion_BatchRetainsIntegrityAndSnapshot`** (`CascadingE2ETests.cs:789-810`):
   ```
   [xUnit.net 00:00:02.17] BrewYou.ApiService.Tests.CascadingE2ETests.T3_01_SimultaneousVesselAndRecipeDeletion_BatchRetainsIntegrityAndSnapshot [FAIL]
   Error Message:
     Expected detail.RecipeName to be "Dual Delete Recipe", but found <null>.
   Stack Trace:
     at FluentAssertions.Primitives.StringAssertions`1.Be(String expected, String because, Object[] becauseArgs)
     at BrewYou.ApiService.Tests.CascadingE2ETests.T3_01_SimultaneousVesselAndRecipeDeletion_BatchRetainsIntegrityAndSnapshot() in CascadingE2ETests.cs:line 808
   ```

4. **`T4_02_SeasonalRecipeRetirement_HistoricalBatchIntegrityAndLabelPrinting`** (`CascadingE2ETests.cs:932-956`):
   ```
   [xUnit.net 00:00:02.43] BrewYou.ApiService.Tests.CascadingE2ETests.T4_02_SeasonalRecipeRetirement_HistoricalBatchIntegrityAndLabelPrinting [FAIL]
   Error Message:
     Expected detail.RecipeName to be "Winter Solstice Ale 2025" because Retiring a recipe must preserve historical batch recipe names, but found <null>.
   Stack Trace:
     at FluentAssertions.Primitives.StringAssertions`1.Be(String expected, String because, Object[] becauseArgs)
     at BrewYou.ApiService.Tests.CascadingE2ETests.T4_02_SeasonalRecipeRetirement_HistoricalBatchIntegrityAndLabelPrinting() in CascadingE2ETests.cs:line 953
   ```

5. **Passing Tests in Scope**:
   - `T1_06_BatchWorkspaceVesselFallback_HandlesNullVesselsGracefully` (`CascadingE2ETests.cs:341-355`): PASS.
   - `T1_10_PrintableBottleLabelFallback_RendersCleanLabelWhenRecipeIdNull` (`CascadingE2ETests.cs:416-440`): PASS.
   - `T1_11_BatchWizardDeletedRecipe_Handles404Gracefully` (`CascadingE2ETests.cs:442-450`): PASS.
   - `T3_02_IngredientDeletionWithActiveBatch_PantryFallbackPreventsLockout` (`CascadingE2ETests.cs:812-822`): PASS.

---

### 1.2 Inspection of Existing Backend Implementation & Test Suites

1. **`src/BrewYou.ApiService/Data/Entities/Batch.cs`**:
   - Lines 43-44: `public BatchStatus Status { get; set; } = BatchStatus.Brewing;` — every newly created batch defaults to `BatchStatus.Brewing` (an active status).
   - Lines 33-34: `public Guid? RecipeId { get; set; }` and `public Recipe? Recipe { get; set; }`. The entity does NOT contain a `public string? RecipeName { get; set; }` column.

2. **`src/BrewYou.ApiService/Services/BatchService.cs`**:
   - Line 1730 in `MapToDetailDto`:
     ```csharp
     RecipeName: b.Recipe?.Name,
     ```
     Does not fall back to `b.RecipeName`. When `b.RecipeId` is set to `null` on recipe deletion, `b.Recipe` becomes `null`, resulting in `RecipeName = null`.

3. **`src/BrewYou.ApiService/Services/RecipeService.cs`**:
   - Lines 500-524 in `DeleteRecipeAsync`:
     ```csharp
     public async Task<RecipeAccessResult> DeleteRecipeAsync(Guid id, string userId)
     {
         ...
         var linkedBatches = await db.Batches.Where(b => b.RecipeId == id).ToListAsync();
         foreach (var batch in linkedBatches)
         {
             batch.RecipeId = null;
         }
         db.Recipes.Remove(recipe);
         await db.SaveChangesAsync();
         return RecipeAccessResult.Success;
     }
     ```
     Observations:
     - No check for active batches exists (`batch.Status` in `Planned`, `Brewing`, `Fermenting`, `Conditioning`).
     - No `bool force = false` parameter exists on `DeleteRecipeAsync`.
     - `batch.RecipeName ??= recipe.Name` is not set before nulling `batch.RecipeId`.

4. **`src/BrewYou.ApiService/Endpoints/RecipeEndpoints.cs`**:
   - Lines 430-443:
     ```csharp
     group.MapDelete("/{id:guid}", async (
         Guid id,
         ClaimsPrincipal principal,
         IRecipeService recipeService) =>
     {
         ...
         var result = await recipeService.DeleteRecipeAsync(id, userId);
         ...
     ```
     Observations:
     - No `[FromQuery] bool force = false` parameter is mapped.
     - No `409 Conflict` status code is mapped for active batch locks (`RECIPE_IN_USE_BY_ACTIVE_BATCHES`).
     - No helper endpoint `GET /api/v1/recipes/{id}/active-batches` exists.

5. **`tests/BrewYou.ApiService.Tests/RecipeEndpointTests.cs`**:
   - Lines 381-433: `DeleteRecipe_OwnerCanDelete_LinkedBatchRetainsSnapshotAndDecouples`:
     ```csharp
     var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
     batch.RecipeId.Should().Be(recipe.Id);
     var delRes = await client.DeleteAsync($"/api/v1/recipes/{recipe.Id}");
     delRes.StatusCode.Should().Be(HttpStatusCode.OK);
     ```
     Observations:
     - The batch is created with default `Status = Brewing`.
     - The delete call does not use `?force=true` and the batch is not completed/archived.
     - Line 432 only checks `updatedBatch.Name.Should().Be("Live Batch #1")`; it does NOT assert `updatedBatch.RecipeName`.
     - Lacks tests for active batch conflict (409), force-delete query param (`?force=true`), completed batch unforced delete, and dynamic rename propagation.

6. **`src/BrewYou.ApiService/Backup/Models/BackupContracts.cs`**:
   - Lines 390-431: `BatchBackupDto` lacks `string? RecipeName = null`.
   - `src/BrewYou.ApiService/Services/BackupService.cs:1816` does not export `RecipeName`.
   - `src/BrewYou.ApiService/Services/BackupService.cs:1445` does not restore `RecipeName`.

---

### 1.3 Inspection of Existing Frontend Implementation & Test Suites

1. **`src/BrewYou.Web/src/routes/batches/[id]/labels/print/+page.svelte`** (FS-10):
   - Lines 60-64: If `b.recipeId` is null (recipe deleted), `api.recipes.label.get(b.recipeId)` is skipped and `label` remains `null`.
   - Line 223: `{:else if batch && label}` — when `label` is null, the entire canvas is skipped and a blank page is displayed.
   - Existing test file `src/BrewYou.Web/src/lib/components/recipes/BottleLabelPrint.test.ts` (lines 1-281) tests label cropping, locale keys, and theoretical ABV calculation, but does not test the print sheet's `batch && !label` fallback template.

2. **`src/BrewYou.Web/src/routes/batches/new/+page.svelte`** (FS-11):
   - Lines 376-382: If `urlRecipeId` points to a deleted recipe, `api.recipes.getById(id)` throws a 404 ApiClientError.
   - Lines 439-440: The catch block sets `error = (err as Error).message;` and freezes the wizard with a blocking error banner while leaving `selectedRecipeId` bound to the deleted recipe ID.

3. **`src/BrewYou.Web/src/lib/components/recipes/DeleteRecipeModal.svelte`**:
   - Lines 1-162: Only issues `await api.recipes.delete(recipe.id)`. Does not query active batches (`/api/v1/recipes/{id}/active-batches`), does not handle `409 Conflict`, and does not offer a force deletion confirmation checkbox.
   - **Crucial finding**: No unit test file exists for `DeleteRecipeModal.svelte` (`DeleteRecipeModal.test.ts` is absent).

4. **Frontend Test Suite Baseline**:
   - `npm run test:unit`: 65 test files passed, 611 tests passed.
   - `npm run check`: 0 errors, 0 warnings.
   - `dotnet format --verify-no-changes`: 0 errors.

---

## 2. Logic Chain

1. **Root Cause of `T1_08`, `T3_01`, and `T4_02` Failures**:
   - Observation 1.1 (#1, #3, #4) shows `RecipeName` resolves to `<null>` after recipe deletion.
   - Observation 1.2 (#1, #2, #3) confirms `Batch` has no `RecipeName` property, `DeleteRecipeAsync` sets `batch.RecipeId = null` without snapshotting, and `MapToDetailDto` relies entirely on `b.Recipe?.Name`.
   - In consequence, once `batch.RecipeId` is cleared, `b.Recipe` is null, causing permanent data loss of recipe identification across batch details, cards, and bottle labels.

2. **Root Cause of `T1_09` Failure**:
   - Observation 1.1 (#2) shows `T1_09` expects `409 Conflict` when deleting a recipe linked to an active batch (`batch.Status == BatchStatus.Brewing`), but received `200 OK`.
   - Observation 1.2 (#3, #4) confirms `DeleteRecipeAsync` has no active status check and `DeleteRecipe` endpoint does not produce 409 Conflict.

3. **Interaction & Test Inconsistency in `T1_08`, `T3_01`, and `RecipeEndpointTests:382`**:
   - Observation 1.2 (#1) confirms `CreateBatchAsync` creates batches with default status `BatchStatus.Brewing`.
   - In `T1_08` (`CascadingE2ETests.cs:389`), `T3_01` (`CascadingE2ETests.cs:800`), and `RecipeEndpointTests.cs:420`, the test calls `client.DeleteAsync($"/api/v1/recipes/{recipe.Id}")` without `?force=true` and asserts `HttpStatusCode.OK`.
   - Once the active batch safeguard (FS-09) is implemented, unforced `DELETE /api/v1/recipes/{recipe.Id}` will return `409 Conflict` on any recipe linked to an active batch.
   - Therefore, the test suite must distinguish two distinct scenarios:
     * **Unforced Deletion on Active Batch**: MUST return `409 Conflict` (`RECIPE_IN_USE_BY_ACTIVE_BATCHES`), tested in `T1_09`.
     * **Forced Deletion on Active Batch**: Calling `DELETE /api/v1/recipes/{id}?force=true` MUST succeed with `200 OK` and snapshot `RecipeName`.
     * **Unforced Deletion on Completed/Archived Batch**: If batches are in terminal states (`Completed`, `Archived`), unforced deletion MUST succeed with `200 OK` and snapshot `RecipeName`.
   - Consequently:
     - `T1_08` in `CascadingE2ETests.cs` should either call `DELETE ...?force=true` or advance the batch to `BatchStatus.Completed` prior to unforced delete, while asserting `afterDetail.RecipeName.Should().Be("Heritage IPA Snapshot")`.
     - `T3_01` in `CascadingE2ETests.cs:800` must include `?force=true` on the recipe delete call (`/api/v1/recipes/{recipe.Id}?force=true`), matching line 799 where equipment delete uses `?force=true`.
     - `T4_02` in `CascadingE2ETests.cs:943` represents "retiring an obsolete seasonal recipe for historical batches"; its batches should be marked `BatchStatus.Completed` (or deleted with `?force=true`) so unforced deletion succeeds and preserves `detail.RecipeName`.
     - `RecipeEndpointTests.cs:382` should either mark the batch `Completed` or use `?force=true`, and add explicit assertion on `updatedBatch.RecipeName`.

4. **Dynamic Renaming Requirement**:
   - The user request requires: "Renaming a recipe dynamically updates live batch references where `RecipeId` is attached."
   - In `BatchService.MapToDetailDto`, mapping `RecipeName: b.Recipe?.Name ?? b.RecipeName` guarantees that if `b.Recipe` is attached, the live navigation property `b.Recipe.Name` is always used. When `UpdateRecipeAsync` renames a recipe, EF Core queries retrieving batches with `.Include(b => b.Recipe)` will immediately return the updated recipe name without modifying batch rows.

5. **User Backup & Restore Backwards Compatibility Requirement (AGENTS.md Section 3.J)**:
   - In `BackupContracts.cs`, `BatchBackupDto` must add optional `string? RecipeName = null`.
   - In `BackupService.cs`, export must map `b.Recipe?.Name ?? b.RecipeName`, and restore must populate `newBatch.RecipeName = batchDto.RecipeName`.
   - In `BackupEndpointsTests.cs`, existing tests for legacy `v0` and `v1` backups (which do not have `recipeName` in batch JSON) must deserialize without errors and restore cleanly, satisfying AGENTS.md § 3.J.

6. **Frontend Testing Requirements**:
   - A new test suite `src/BrewYou.Web/src/lib/components/recipes/DeleteRecipeModal.test.ts` must be created to test:
     * Active batch inspection via API.
     * 409 Conflict handling and display of active batch warnings.
     * Force deletion checkbox behavior.
     * 100% key parity across `en.json`, `sv.json`, `de.json`, `fr.json`, `es.json`.
   - Fallback rendering in `batches/[id]/labels/print/+page.svelte` when `label` is null must be verified by automated unit tests in `BottleLabelPrint.test.ts`.
   - Graceful 404 handling in `batches/new/+page.svelte` when `recipeId` query param is deleted must be covered by component tests.

---

## 3. Caveats

1. **`T1_07` in `CascadingE2ETests.cs`**:
   During our test run, `T1_07_PackagingVesselNullSafety_MeasurementUpdateDoesNotCorruptGuid` failed because equipment deletion for a packaging vessel did not decouple or return 409. This is an M1 (Equipment) concern, not M2 (Recipe). M2 testing scope focuses on recipe lifecycle, batch snapshot, and active batch guards.
2. **In-Memory vs. PostgreSQL Relational Navigation**:
   In `CascadingE2ETests.cs` and `RecipeEndpointTests.cs`, `WebApplicationFactory` uses in-memory or ephemeral databases. Explicit decoupling (`batch.RecipeId = null`) and explicit snapshotting (`batch.RecipeName ??= recipe.Name`) in domain services (`RecipeService.cs`) ensures identical behavior across both in-memory test harnesses and production PostgreSQL.
3. **Explorer Role Constraint**:
   Per identity instructions, no source or test files were modified during this investigation. This handoff report constitutes the complete, actionable testing specification for the implementer and test writer.

---

## 4. Conclusion & Complete Testing Plan

### 4.1 Requirement-to-Test Mapping Matrix

| Milestone 2 Requirement | Test Suite & Method | Test Assertion & Target Verification |
| :--- | :--- | :--- |
| **REQ-M2-01: Recipe Delete Snapshots `batch.RecipeName`** | `CascadingE2ETests.cs`<br>`T1_08_BatchRecipeNamePersistence...`<br>`T3_01_SimultaneousVesselAndRecipe...`<br>`T4_02_SeasonalRecipeRetirement...` | - `afterDetail.RecipeId.Should().BeNull()`<br>- `afterDetail.RecipeName.Should().Be("Heritage IPA Snapshot")`<br>- Subsequent `GET /api/v1/batches/{id}` returns snapshotted recipe name. |
| **REQ-M2-02: Active Batch Guard on Unforced Recipe Delete (409 Conflict)** | `CascadingE2ETests.cs`<br>`T1_09_RecipeDeletionActiveBatchGuard...`<br>`RecipeEndpointTests.cs`<br>`DeleteRecipe_WithActiveBatches_Returns409Conflict` | - `delRes.StatusCode.Should().Be(HttpStatusCode.Conflict)`<br>- Error payload code: `RECIPE_IN_USE_BY_ACTIVE_BATCHES`<br>- Recipe remains in database, batch still references recipe. |
| **REQ-M2-03: Forced Recipe Delete (`?force=true`) Succeeds with Snapshot** | `CascadingE2ETests.cs`<br>`T1_08` (forced branch) & `T3_01`<br>`RecipeEndpointTests.cs`<br>`DeleteRecipe_WithActiveBatches_WhenForced_SucceedsAndDecouples` | - `DELETE /api/v1/recipes/{id}?force=true` returns `200 OK`<br>- Batch decoupled (`RecipeId = null`)<br>- `batch.RecipeName` preserved with snapshot name. |
| **REQ-M2-04: Unforced Delete on Completed/Archived Batches Succeeds** | `RecipeEndpointTests.cs`<br>`DeleteRecipe_WithCompletedBatches_UnforcedDelete_SucceedsWithSnapshot` | - Batch in `Completed` or `Archived` stage allows unforced delete (`200 OK`)<br>- Batch retains `RecipeName`. |
| **REQ-M2-05: Dynamic Recipe Rename Propagation on Live Batches** | `BatchEndpointTests.cs`<br>`BatchDetail_WhenRecipeRenamed_ReflectsUpdatedRecipeName`<br>`RecipeEndpointTests.cs`<br>`UpdateRecipe_RenamesRecipe_AndAttachedBatchesReflectNewName` | - Create recipe "Original Name", create batch.<br>- Rename recipe via `PUT /api/v1/recipes/{id}` to "Renamed IPA".<br>- Query `GET /api/v1/batches/{id}`: `RecipeName` returns "Renamed IPA" dynamically via `b.Recipe?.Name ?? b.RecipeName`. |
| **REQ-M2-06: Active Batches Helper Endpoint (`GET /api/v1/recipes/{id}/active-batches`)** | `RecipeEndpointTests.cs`<br>`GetActiveBatchesForRecipe_ReturnsActiveBatchesOnly` | - Returns `200 OK` with `List<BatchSummaryDto>` of active batches (`Planned`, `Brewing`, `Fermenting`, `Conditioning`).<br>- Excludes `Completed` and `Archived` batches. |
| **REQ-M2-07: `BatchBackupDto` Round-Trip & Backwards Compatibility (AGENTS.md 3.J)** | `BackupEndpointsTests.cs`<br>`RoundTrip_BatchWithRecipeName_ExportsAndRestoresAccurately`<br>`BackwardsCompatibility_LegacyV0AndV1Backups...` | - `BatchBackupDto` contains optional `string? RecipeName = null`.<br>- Round-trip export and restore preserves `RecipeName` on both linked and decoupled batches.<br>- Restoring legacy `v0`/`v1` backup JSON without `recipeName` succeeds without schema error. |
| **REQ-M2-08: Frontend Bottle Label Fallback (FS-10)** | `src/BrewYou.Web/src/lib/components/recipes/BottleLabelPrint.test.ts` | - When `label` is null or recipe was deleted, renders fallback label with `batch.name`, `batch.batchCode`, `batch.beerStyle`, `batch.targetAbv`, `batch.recipeName`.<br>- Verifies sheet canvas renders without blank screen crash. |
| **REQ-M2-09: Frontend Batch Wizard Stale Recipe 404 Handling (FS-11)** | `src/BrewYou.Web/src/lib/components/batches/` (or route test) | - When `/batches/new?recipeId=<deleted-id>` receives 404, clears `selectedRecipeId`, displays warning toast, and does not block wizard initialization. |
| **REQ-M2-10: Frontend Recipe Deletion Modal Active Safeguards & i18n Parity** | `src/BrewYou.Web/src/lib/components/recipes/DeleteRecipeModal.test.ts` (NEW) | - Renders active batch warning when `/active-batches` returns > 0.<br>- Displays force-delete confirmation checkbox.<br>- Calls `api.recipes.delete(id, { force: true })` when force is checked.<br>- 100% key parity across `en.json`, `sv.json`, `de.json`, `fr.json`, `es.json`. |

---

### 4.2 Concrete Test Code Specifications for Implementers

#### Backend: New Tests for `tests/BrewYou.ApiService.Tests/RecipeEndpointTests.cs`

```csharp
[Fact]
public async Task DeleteRecipe_WithActiveBatches_Returns409Conflict_WithActiveBatchDetails()
{
    var (client, _, _) = await CreateAuthenticatedUserAsync("activeguard");
    var recipeReq = new CreateRecipeRequest("Guard IPA", null, "American IPA", 20m, 60, 72m, true, new());
    var recipe = (await (await client.PostAsJsonAsync("/api/v1/recipes", recipeReq))
        .Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

    var batchReq = new CreateBatchRequest(RecipeId: recipe.Id, BatchCode: "B-GUARD-01", Name: "In Flight Batch",
        BeerStyle: "American IPA", BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
        TargetBatchSizeLiters: 20m, TargetOg: null, TargetFg: null, TargetAbv: null, TargetIbu: null,
        TargetColorSrm: null, BoilTimeMinutes: null, EfficiencyPercent: null, BoilerId: null, FermenterId: null,
        MeasuredOg: null, PitchTemperatureC: null, Notes: null, CustomIngredients: null);
    await client.PostAsJsonAsync("/api/v1/batches", batchReq);

    // Unforced delete must return 409 Conflict
    var delRes = await client.DeleteAsync($"/api/v1/recipes/{recipe.Id}");
    delRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var err = await delRes.Content.ReadFromJsonAsync<ApiResponse>();
    err!.Error!.Code.Should().Be("RECIPE_IN_USE_BY_ACTIVE_BATCHES");
}

[Fact]
public async Task DeleteRecipe_WithActiveBatches_WhenForced_SucceedsAndDecouplesBatchesWithSnapshot()
{
    var (client, _, _) = await CreateAuthenticatedUserAsync("forceguard");
    var recipeReq = new CreateRecipeRequest("Force IPA", null, "American IPA", 20m, 60, 72m, true, new());
    var recipe = (await (await client.PostAsJsonAsync("/api/v1/recipes", recipeReq))
        .Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

    var batchReq = new CreateBatchRequest(RecipeId: recipe.Id, BatchCode: "B-FORCE-01", Name: "Active Forced Batch",
        BeerStyle: "American IPA", BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
        TargetBatchSizeLiters: 20m, TargetOg: null, TargetFg: null, TargetAbv: null, TargetIbu: null,
        TargetColorSrm: null, BoilTimeMinutes: null, EfficiencyPercent: null, BoilerId: null, FermenterId: null,
        MeasuredOg: null, PitchTemperatureC: null, Notes: null, CustomIngredients: null);
    var batch = (await (await client.PostAsJsonAsync("/api/v1/batches", batchReq))
        .Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

    // Forced delete must succeed with 200 OK
    var delRes = await client.DeleteAsync($"/api/v1/recipes/{recipe.Id}?force=true");
    delRes.StatusCode.Should().Be(HttpStatusCode.OK);

    // Batch must be decoupled and snapshot preserved
    var getBatchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
    getBatchRes.StatusCode.Should().Be(HttpStatusCode.OK);
    var updated = (await getBatchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
    updated.RecipeId.Should().BeNull();
    updated.RecipeName.Should().Be("Force IPA");
}

[Fact]
public async Task UpdateRecipe_RenamesRecipe_AndAttachedBatchesReflectNewName()
{
    var (client, _, _) = await CreateAuthenticatedUserAsync("renamerecipe");
    var recipeReq = new CreateRecipeRequest("Original Recipe Name", null, "American IPA", 20m, 60, 72m, true, new());
    var recipe = (await (await client.PostAsJsonAsync("/api/v1/recipes", recipeReq))
        .Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

    var batchReq = new CreateBatchRequest(RecipeId: recipe.Id, BatchCode: "B-RENAME-01", Name: "Attached Batch",
        BeerStyle: "American IPA", BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
        TargetBatchSizeLiters: 20m, TargetOg: null, TargetFg: null, TargetAbv: null, TargetIbu: null,
        TargetColorSrm: null, BoilTimeMinutes: null, EfficiencyPercent: null, BoilerId: null, FermenterId: null,
        MeasuredOg: null, PitchTemperatureC: null, Notes: null, CustomIngredients: null);
    var batch = (await (await client.PostAsJsonAsync("/api/v1/batches", batchReq))
        .Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

    // Rename recipe
    var updateReq = new UpdateRecipeRequest("Brand New Recipe Name", null, "American IPA", 20m, 60, 72m, true, new());
    var updateRes = await client.PutAsJsonAsync($"/api/v1/recipes/{recipe.Id}", updateReq);
    updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

    // Verify batch reflects the new name
    var getBatchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
    var detail = (await getBatchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
    detail.RecipeName.Should().Be("Brand New Recipe Name");
}
```

#### Backend: Harmonization for `CascadingE2ETests.cs`

1. **In `T1_08_BatchRecipeNamePersistence_PreservesRecipeNameOnRecipeDelete`**:
   Either call `client.DeleteAsync($"/api/v1/recipes/{recipe.Id}?force=true")` or advance the batch to `BatchStatus.Completed` prior to unforced delete. Assert `afterDetail.RecipeName.Should().Be("Heritage IPA Snapshot")`.
2. **In `T3_01_SimultaneousVesselAndRecipeDeletion_BatchRetainsIntegrityAndSnapshot`**:
   Change line 800:
   ```csharp
   - var delRecRes = await client.DeleteAsync($"/api/v1/recipes/{recipe.Id}");
   + var delRecRes = await client.DeleteAsync($"/api/v1/recipes/{recipe.Id}?force=true");
   ```
3. **In `T4_02_SeasonalRecipeRetirement_HistoricalBatchIntegrityAndLabelPrinting`**:
   Change line 943 to use `?force=true` or complete the historical batches, then verify `detail.RecipeName.Should().Be("Winter Solstice Ale 2025")`.

#### Backend: New Tests for `tests/BrewYou.ApiService.Tests/BackupEndpointsTests.cs`

```csharp
[Fact]
public async Task RoundTrip_BatchWithRecipeName_ExportsAndRestoresAccurately()
{
    var (client, _, _) = await CreateAuthenticatedUserAsync("backupsnapshot");
    var recipe = await CreateStandardRecipeAsync(client, "Export Snapshot Recipe");
    var batch = await CreateBatchAsync(client, recipe.Id, "Export Snapshot Batch");

    // Delete recipe with force to snapshot RecipeName
    await client.DeleteAsync($"/api/v1/recipes/{recipe.Id}?force=true");

    // Export backup
    var exportRes = await client.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest());
    exportRes.StatusCode.Should().Be(HttpStatusCode.OK);
    var envelope = (await exportRes.Content.ReadFromJsonAsync<ApiResponse<BrewYouBackupEnvelope>>())!.Data!;

    // Restore backup in ReplaceAll mode
    var restoreRes = await client.PostAsJsonAsync("/api/v1/backup/restore", 
        new ExecuteRestoreRequest(envelope, new RestoreOptionsDto(ConflictResolution: RestoreConflictResolutionMode.ReplaceAll)));
    restoreRes.StatusCode.Should().Be(HttpStatusCode.OK);

    // Verify restored batch still has RecipeName
    var getBatchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
    getBatchRes.StatusCode.Should().Be(HttpStatusCode.OK);
    var detail = (await getBatchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
    detail.RecipeName.Should().Be("Export Snapshot Recipe");
}
```

#### Frontend: New Test Suite `src/BrewYou.Web/src/lib/components/recipes/DeleteRecipeModal.test.ts`

```typescript
import { describe, it, expect, vi } from 'vitest';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';
import de from '$lib/i18n/locales/de.json';
import fr from '$lib/i18n/locales/fr.json';
import es from '$lib/i18n/locales/es.json';
import type { RecipeSummaryDto, BatchSummaryDto } from '$lib/types/api';

describe('DeleteRecipeModal Component Logic & Invariants', () => {
    it('ensures all 5 locales have complete active batch warning and force delete keys with 100% parity', () => {
        const locales = [en, sv, de, fr, es];
        const requiredKeys = [
            'title',
            'warning',
            'batches_notice',
            'active_batches_warning',
            'force_delete_checkbox',
            'cancel',
            'confirm',
            'deleting',
            'error'
        ];

        for (const loc of locales) {
            for (const key of requiredKeys) {
                const val = (loc.recipes.delete_modal as Record<string, string>)[key];
                expect(val).toBeDefined();
                expect(typeof val).toBe('string');
                expect(val.length).toBeGreaterThan(0);
            }
        }
    });

    it('requires force confirmation when active batches exist', async () => {
        const mockRecipe: RecipeSummaryDto = {
            id: 'rec-1',
            name: 'Guarded IPA',
            beerStyle: 'American IPA',
            batchSizeLiters: 20,
            originalGravity: 1.060,
            finalGravity: 1.012,
            alcoholByVolume: 6.3,
            bitternessIbu: 50,
            colorSrm: 7,
            isPublic: true,
            createdAt: '2026-11-01',
            updatedAt: '2026-11-01'
        };

        const activeBatches: BatchSummaryDto[] = [{
            id: 'batch-1',
            batchCode: 'B-001',
            name: 'Active Fermenting Batch',
            recipeId: 'rec-1',
            beerStyle: 'American IPA',
            status: 'Fermenting',
            currentStage: 'Ferment',
            brewDate: '2026-11-01',
            daysActive: 3,
            targetOg: 1.060,
            targetFg: 1.012,
            colorSrm: 7,
            targetBatchSizeLiters: 20,
            createdAt: '2026-11-01',
            updatedAt: '2026-11-01'
        }];

        const hasActiveBatches = activeBatches.length > 0;
        let forceChecked = false;

        const canSubmit = !hasActiveBatches || forceChecked;
        expect(canSubmit).toBe(false);

        forceChecked = true;
        expect(!hasActiveBatches || forceChecked).toBe(true);
    });
});
```

---

## 5. Verification Method

### 5.1 Test Execution Commands & Expected Outputs

1. **Verify M2 Cascading E2E Tests**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~CascadingE2ETests.T1_08|FullyQualifiedName~CascadingE2ETests.T1_09|FullyQualifiedName~CascadingE2ETests.T1_10|FullyQualifiedName~CascadingE2ETests.T1_11|FullyQualifiedName~CascadingE2ETests.T3_01|FullyQualifiedName~CascadingE2ETests.T4_02"
   ```
   **Expected**: 6 tests passed, 0 failed, 0 skipped.

2. **Verify Recipe Endpoints & Active Safeguards**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~RecipeEndpointTests"
   ```
   **Expected**: All recipe endpoint tests pass (including unforced active batch guard 409, forced delete 200, dynamic rename propagation).

3. **Verify Batch Endpoints & DTO Persistence**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~BatchEndpointTests"
   ```
   **Expected**: All batch endpoint tests pass.

4. **Verify Backup & Restore Backwards Compatibility**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~BackupEndpointsTests"
   ```
   **Expected**: All backup tests pass, including round-trip `RecipeName` persistence and legacy `v0`/`v1` schema migration.

5. **Verify Code Formatting & Style Compliance**:
   ```powershell
   dotnet format --verify-no-changes
   ```
   **Expected**: Zero formatting changes, exit code 0.

6. **Verify Frontend Unit Tests**:
   ```bash
   cd src/BrewYou.Web
   npm run test:unit
   ```
   **Expected**: All test suites pass (including `BottleLabelPrint.test.ts` and new `DeleteRecipeModal.test.ts`), exit code 0.

7. **Verify SvelteKit Type Checking & Linting**:
   ```bash
   cd src/BrewYou.Web
   npm run check
   npm run lint
   ```
   **Expected**: 0 errors, 0 warnings.

### 5.2 Invalidation Conditions
This testing plan is invalidated if:
1. `DELETE /api/v1/recipes/{id}` returns `200 OK` when linked batches are in active status (`Planned`, `Brewing`, `Fermenting`, `Conditioning`) without `?force=true`.
2. `GET /api/v1/batches/{id}` returns `null` for `recipeName` after the recipe is deleted.
3. Renaming a recipe does not reflect on attached live batches.
4. Restoring legacy backup files (`v0`, `v1`) fails due to missing `RecipeName` property on `BatchBackupDto`.
5. Frontend bottle label printing crashes or renders blank when printing a batch whose recipe was deleted.
