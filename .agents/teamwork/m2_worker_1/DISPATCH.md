## 2026-11-04T09:48:19Z
You are m2_worker_1, the Milestone 2 Implementation Worker.
Your working directory is: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_worker_1\

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

MANDATORY INSTRUCTION: Read ORIGINAL_REQUEST.md at:
c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\ORIGINAL_REQUEST.md
Also read PROJECT.md at:
c:\Users\jocke\AgentWorkspaces\BrewYou\PROJECT.md

Read the handoff reports from the 3 Milestone 2 Explorers:
- Backend implementation plan: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_explorer_1\handoff.md
- Frontend UI & i18n plan: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_explorer_2\handoff.md
- Testing & verification plan: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_explorer_3\handoff.md

Your objective:
Implement the complete, fullstack solution for Milestone 2: Recipe Lifecycle, Batch RecipeName Snapshot & Active Batch Safeguards.

Scope of changes:
1. Backend Entity & DbContext:
   - In `src/BrewYou.ApiService/Data/Entities/Batch.cs`: add `public string? RecipeName { get; set; }`
   - In `src/BrewYou.ApiService/Data/BrewYouDbContext.cs`: add `entity.Property(b => b.RecipeName).HasMaxLength(150);`
2. Backend Services & DTOs:
   - In `src/BrewYou.ApiService/Services/BatchService.cs`:
     * In `CreateBatchAsync`: populate `batch.RecipeName = recipe?.Name;`
     * In `MapToDetailDto`: map `RecipeName: b.Recipe?.Name ?? b.RecipeName`
     * In `GetBatchesAsync`: include `.Include(b => b.Recipe)` and map `RecipeName: b.Recipe?.Name ?? b.RecipeName`
   - In `src/BrewYou.ApiService/Endpoints/BatchDtos.cs`: update `BatchSummaryDto` to declare optional `string? RecipeName = null`
   - In `src/BrewYou.ApiService/Services/RecipeService.cs`:
     * Add `Conflict` to `RecipeAccessResult` enum
     * Update `DeleteRecipeAsync(Guid id, string userId, bool force = false)`:
       - Active batch guard: check if any linked batches have active status (`b.RecipeId == id && b.Status != BatchStatus.Completed && b.Status != BatchStatus.Archived`). If active batches exist and `!force`, return `RecipeAccessResult.Conflict`.
       - If `force == true` or no active batches exist: for all linked batches, snapshot `batch.RecipeName ??= recipe.Name`, set `batch.RecipeId = null`, `batch.UpdatedAt = DateTime.UtcNow`.
     * Add `GetActiveBatchesForRecipeAsync(Guid recipeId, string userId)` returning active batches with ownership authorization.
   - In `src/BrewYou.ApiService/Services/IRecipeService.cs`: update method signatures.
   - In `src/BrewYou.ApiService/Endpoints/RecipeDtos.cs`: add `RecipeActiveBatchDto(Guid Id, string BatchCode, string Name, BatchStatus Status, BrewStage CurrentStage)`.
   - In `src/BrewYou.ApiService/Endpoints/RecipeEndpoints.cs`:
     * Support `[FromQuery] bool force = false` on `DELETE /{id:guid}`, returning 409 Conflict with code `RECIPE_IN_USE_BY_ACTIVE_BATCHES`.
     * Add `GET /{id:guid}/active-batches`.
3. User Backup & Restore (AGENTS.md Section 3.J):
   - In `src/BrewYou.ApiService/Backup/Models/BackupContracts.cs`: add optional `string? RecipeName = null` to `BatchBackupDto` with safe default.
   - In `src/BrewYou.ApiService/Services/BackupService.cs`:
     * In `ExportUserBackupAsync`: include `.Include(b => b.Recipe)` and map `RecipeName: b.Recipe?.Name ?? b.RecipeName`
     * In `ExecuteRestoreAsync`: restore `RecipeName = batchDto.RecipeName ?? mappedRecipe?.Name` on both `MergeOverwriteExisting` and creation of new batches.
4. Frontend Types & Client:
   - In `src/BrewYou.Web/src/lib/types/api.ts`: add `RecipeActiveBatchDto` and add `recipeName?: string | null` to `BatchSummaryDto`.
   - In `src/BrewYou.Web/src/lib/api/client.ts`: update `api.recipes.delete(id, force = false)` and add `api.recipes.getActiveBatches(id)`.
5. Frontend UI:
   - In `src/BrewYou.Web/src/routes/batches/[id]/labels/print/+page.svelte`:
     * In `onMount`: wrap `api.recipes.label.get(b.recipeId)` in try/catch to gracefully handle 404.
     * At line 223: replace `{:else if batch && label}` with fallback handling so when `label` is null but `batch` exists, render a clean bottle label fallback with batch name, batch code, beer style, recipe name fallback (`batch.recipeName ?? batch.recipe?.name`), ABV, date, and dashed guidelines matching sheet presets.
   - In `src/BrewYou.Web/src/routes/batches/new/+page.svelte`:
     * Catch 404 in `api.recipes.getById` gracefully, fall back to default recipe or custom entry, and display non-blocking dismissible warning banner.
   - In `src/BrewYou.Web/src/lib/components/recipes/DeleteRecipeModal.svelte`:
     * Query `/api/v1/recipes/{id}/active-batches` on open.
     * Render active batch warning card with stage badges and force-delete checkbox.
     * Call `api.recipes.delete(recipe.id, force)`.
6. Localization (100% Key Parity):
   - Ensure all new keys are added across `en.json`, `sv.json`, `de.json`, `fr.json`, `es.json`.
7. Tests & Verification:
   - Update `tests/BrewYou.ApiService.Tests/CascadingE2ETests.cs`:
     * `T1_08`: use `?force=true` or complete batch, assert `afterDetail.RecipeName.Should().Be("Heritage IPA Snapshot")`
     * `T3_01`: line 800 use `?force=true`
     * `T4_02`: line 943 use `?force=true`
   - Add new tests in `RecipeEndpointTests.cs` for active batch 409 conflict, forced delete 200, dynamic recipe renaming propagation.
   - Add test in `BackupEndpointsTests.cs` for round-trip `RecipeName` export and restore.
   - Add frontend unit test suite: `src/BrewYou.Web/src/lib/components/recipes/DeleteRecipeModal.test.ts`.
   - Run verification commands:
     * `dotnet build src/BrewYou.ApiService/BrewYou.ApiService.csproj`
     * `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~T1_08|FullyQualifiedName~T1_09|FullyQualifiedName~T1_10|FullyQualifiedName~T1_11|FullyQualifiedName~T3_01|FullyQualifiedName~T4_02"`
     * `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~RecipeEndpointTests"`
     * `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~BackupEndpointsTests"`
     * `dotnet format --verify-no-changes`
     * `npm run test:unit` in `src/BrewYou.Web`
     * `npm run check` and `npm run lint` in `src/BrewYou.Web`
