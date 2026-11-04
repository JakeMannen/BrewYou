# Technical Implementation Plan: Milestone 2 — Recipe Lifecycle, Batch RecipeName Snapshot & Active Batch Safeguards

**Milestone**: M2 (Backend Domain)  
**Author**: `m2_explorer_1` (M2 Backend Domain Explorer)  
**Date**: 2026-11-04  
**Target Files**:
1. `src/BrewYou.ApiService/Data/Entities/Batch.cs`
2. `src/BrewYou.ApiService/Data/BrewYouDbContext.cs`
3. `src/BrewYou.ApiService/Services/BatchService.cs`
4. `src/BrewYou.ApiService/Services/RecipeService.cs`
5. `src/BrewYou.ApiService/Services/IRecipeService.cs`
6. `src/BrewYou.ApiService/Endpoints/RecipeEndpoints.cs`
7. `src/BrewYou.ApiService/Endpoints/BatchDtos.cs`
8. `src/BrewYou.ApiService/Backup/Models/BackupContracts.cs`
9. `src/BrewYou.ApiService/Services/BackupService.cs`
10. `src/BrewYou.Web/src/lib/types/api.ts`
11. `src/BrewYou.Web/src/lib/api/client.ts`
12. `tests/BrewYou.ApiService.Tests/RecipeEndpointTests.cs`
13. `tests/BrewYou.ApiService.Tests/CascadingE2ETests.cs`

---

## 1. Observation

Direct code observations across the codebase:

### Obs 1. `Batch.cs` Lacks `RecipeName` Field
- In `src/BrewYou.ApiService/Data/Entities/Batch.cs:33-35`:
  ```csharp
  public Guid? RecipeId { get; set; }
  public Recipe? Recipe { get; set; }
  ```
  The entity model stores only a foreign key navigation `RecipeId` and `Recipe`. There is no persistent column or property for snapshotting the recipe's name at batch creation or recipe deletion.

### Obs 2. `BrewYouDbContext.cs` Fluent Configuration
- In `src/BrewYou.ApiService/Data/BrewYouDbContext.cs:334-340`:
  ```csharp
  builder.Entity<Batch>(entity =>
  {
      entity.HasKey(b => b.Id);
      entity.Property(b => b.BatchCode).HasMaxLength(30).IsRequired();
      entity.Property(b => b.Name).HasMaxLength(150).IsRequired();
      entity.Property(b => b.BeerStyle).HasMaxLength(100).IsRequired();
  ...
  ```
  `Recipe.Name` has `HasMaxLength(150)`. Currently, `Batch` has no configuration for `RecipeName`.

### Obs 3. Batch Creation Does Not Snapshot Recipe Name
- In `src/BrewYou.ApiService/Services/BatchService.cs:326-335`:
  ```csharp
  var batch = new Batch
  {
      UserId = userId,
      RecipeId = recipe?.Id,
      BatchCode = batchCode,
      Name = request.Name.Trim(),
      BeerStyle = request.BeerStyle?.Trim() ?? recipe?.BeerStyle ?? "American IPA",
      Status = BatchStatus.Brewing,
      CurrentStage = BrewStage.Mash,
      BrewDate = brewDate,
  ...
  ```
  `recipe?.Name` is available from step 2 (`recipe = await _db.Recipes...FirstOrDefaultAsync(...)`), but is not stored on `batch`.

### Obs 4. Batch Detail DTO Mapping Resolves Only Navigation Property
- In `src/BrewYou.ApiService/Services/BatchService.cs:1726-1731`:
  ```csharp
  return new BatchDetailDto(
      Id: b.Id,
      UserId: b.UserId,
      RecipeId: b.RecipeId,
      RecipeName: b.Recipe?.Name,
      BatchCode: b.BatchCode,
  ...
  ```
  When `RecipeId` is decoupled or null, `b.Recipe` is null, so `RecipeName` resolves to `null`.
  In `src/BrewYou.ApiService/Endpoints/BatchDtos.cs:165-170`, `BatchDetailDto` already defines `string? RecipeName`:
  ```csharp
  public record BatchDetailDto(
      Guid Id,
      string UserId,
      Guid? RecipeId,
      string? RecipeName,
  ...
  ```
  However, `BatchSummaryDto` in `BatchDtos.cs:7-37` currently lacks `string? RecipeName`.

### Obs 5. Recipe Deletion Decouples Without Snapshotting or Active Batch Checks
- In `src/BrewYou.ApiService/Services/RecipeService.cs:500-524`:
  ```csharp
  public async Task<RecipeAccessResult> DeleteRecipeAsync(Guid id, string userId)
  {
      var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == id);
      if (recipe == null)
      {
          return RecipeAccessResult.NotFound;
      }

      if (recipe.UserId != userId)
      {
          return RecipeAccessResult.Forbidden;
      }

      // Decouple linked batches explicitly so In-Memory database & cached entities stay synchronized
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
  Line 517 unconditionally sets `batch.RecipeId = null` without checking if any batches are actively in progress (`Planned`, `Brewing`, `Fermenting`, `Conditioning`). It also fails to persist `batch.RecipeName = recipe.Name`, erasing the recipe name permanently from all batches.

### Obs 6. `RecipeAccessResult` Enum Lacks Conflict State
- In `src/BrewYou.ApiService/Services/RecipeService.cs:10-17`:
  ```csharp
  public enum RecipeAccessResult
  {
      Success,
      NotFound,
      Forbidden,
      DuplicateName,
      InvalidIngredient
  }
  ```
  There is no `Conflict` member to represent active batch restriction.

### Obs 7. `RecipeEndpoints.cs` Has No `force` Parameter or Active Batches Endpoint
- In `src/BrewYou.ApiService/Endpoints/RecipeEndpoints.cs:424-449`:
  ```csharp
  group.MapDelete("/{id:guid}", async (
      Guid id,
      ClaimsPrincipal principal,
      IRecipeService recipeService) =>
  ```
  There is no `[FromQuery] bool force = false` query parameter, no 409 Conflict return (`RECIPE_IN_USE_BY_ACTIVE_BATCHES`), and no helper endpoint `GET /{id:guid}/active-batches`.

### Obs 8. User Backup & Restore Ignores `RecipeName` on Batches
- In `src/BrewYou.ApiService/Backup/Models/BackupContracts.cs:390-430`:
  `BatchBackupDto` does not declare `string? RecipeName`.
- In `src/BrewYou.ApiService/Services/BackupService.cs:1816-1848`:
  `MapBatchToBackupDto` does not pass `RecipeName` to `BatchBackupDto`.
- In `src/BrewYou.ApiService/Services/BackupService.cs:1372-1481`:
  `ExecuteRestoreAsync` does not populate `existingBatch.RecipeName` on `MergeOverwriteExisting` or `newBatch.RecipeName` on creation.

---

## 2. Logic Chain

1. **Permanent Data Loss on Deletion (FS-08)**:
   - From Obs 1 & Obs 4: `Batch` relies on dynamic EF Core navigation `b.Recipe?.Name` to populate `RecipeName` on `BatchDetailDto`.
   - From Obs 5: `DeleteRecipeAsync` sets `batch.RecipeId = null`.
   - In consequence, once a recipe is deleted, all historical and active batches lose their recipe name permanently, rendering null/blank labels and empty recipe banners.
   - **Resolution**: Adding `public string? RecipeName { get; set; }` to `Batch` (max length 150), populating it at batch creation (`batch.RecipeName = recipe?.Name`), and snapshotting `batch.RecipeName ??= recipe.Name` on recipe deletion guarantees data preservation across all batch lifecycles.

2. **Dangling In-Progress Operations on Deletion (FS-09)**:
   - From Obs 5: `DeleteRecipeAsync` executes immediately regardless of whether linked batches are mid-mash, boiling, or actively fermenting.
   - In craft brewing domain modeling, an active batch (`Planned`, `Brewing`, `Fermenting`, `Conditioning`) requires recipe formulations for mash step temperatures, hop additions, target gravities, and ABV tracking.
   - **Resolution**:
     - Check if linked batches are active (`b.Status != BatchStatus.Completed && b.Status != BatchStatus.Archived`).
     - If active batches exist and `!force`, return `RecipeAccessResult.Conflict`.
     - In `RecipeEndpoints.cs`, return `409 Conflict` with JSON error code `RECIPE_IN_USE_BY_ACTIVE_BATCHES`.
     - If `force == true`, allow deletion with recipe name snapshotting.
     - Add `GET /api/v1/recipes/{id}/active-batches` returning `List<RecipeActiveBatchDto>` for frontend confirmation modals.

3. **User Backup & Restore Backwards Compatibility (AGENTS.md Section 3.J)**:
   - From Obs 8: `BatchBackupDto` lacks `RecipeName`.
   - If backups are restored without `RecipeName`, historical batches whose recipes were not restored or deleted on target will have empty recipe names.
   - **Resolution**:
     - Add `string? RecipeName = null` as an optional parameter to `BatchBackupDto`.
     - In `ExportUserBackupAsync`, query batches with `.Include(b => b.Recipe)` and map `RecipeName: b.Recipe?.Name ?? b.RecipeName`.
     - In `ExecuteRestoreAsync`, restore `RecipeName = batchDto.RecipeName ?? mappedRecipe?.Name`.
     - Because `RecipeName = null` is defaulted, all legacy backup files (`v0`, `v1`, `v2`) deserialize without errors and restore cleanly.

---

## 3. Caveats

1. **Existing Test Expectations in `CascadingE2ETests.cs` and `RecipeEndpointTests.cs`**:
   - `CascadingE2ETests.cs:389` (`T1_08`) and `RecipeEndpointTests.cs:420` currently invoke `DELETE /api/v1/recipes/{id}` on recipes linked to batches with default status `Brewing`.
   - When FS-09 active batch protection is active, deleting an active batch without `?force=true` will now return `409 Conflict` (which `T1_09` explicitly expects!).
   - In `T1_08` and `DeleteRecipe_OwnerCanDelete_LinkedBatchRetainsSnapshotAndDecouples`, the test should pass `?force=true` (`/api/v1/recipes/{recipe.Id}?force=true`) or set the batch to `Completed` to test deletion.
2. **BatchSummaryDto**:
   - `BatchSummaryDto` in `BatchDtos.cs` and `api.ts` does not strictly require `RecipeName` by existing UI contracts, but adding optional `string? RecipeName = null` allows batch card overviews to display recipe names after recipe deletion without extra queries.
3. **Multi-tenant / Ownership Boundary**:
   - `GetActiveBatchesForRecipeAsync` must enforce user isolation: only the recipe owner may query active batches for their recipe, returning `Forbidden` or `NotFound` for unauthorized callers.

---

## 4. Conclusion & Concrete Implementation Specifications

### Step 1. Entity & DbContext Changes

#### `src/BrewYou.ApiService/Data/Entities/Batch.cs`
Add `public string? RecipeName { get; set; }` directly below `Recipe`:
```csharp
    public Guid? RecipeId { get; set; }
    public Recipe? Recipe { get; set; }

    public string? RecipeName { get; set; }
```

#### `src/BrewYou.ApiService/Data/BrewYouDbContext.cs`
In `OnModelCreating`, under `builder.Entity<Batch>(entity => { ... })`:
```csharp
    builder.Entity<Batch>(entity =>
    {
        entity.HasKey(b => b.Id);
        entity.Property(b => b.BatchCode).HasMaxLength(30).IsRequired();
        entity.Property(b => b.Name).HasMaxLength(150).IsRequired();
        entity.Property(b => b.RecipeName).HasMaxLength(150);
        entity.Property(b => b.BeerStyle).HasMaxLength(100).IsRequired();
```

---

### Step 2. Batch Creation & Mapping Updates

#### `src/BrewYou.ApiService/Services/BatchService.cs`
1. In `CreateBatchAsync` (around line 326):
```csharp
    // 5. Build Batch Entity
    var batch = new Batch
    {
        UserId = userId,
        RecipeId = recipe?.Id,
        RecipeName = recipe?.Name,
        BatchCode = batchCode,
        Name = request.Name.Trim(),
        BeerStyle = request.BeerStyle?.Trim() ?? recipe?.BeerStyle ?? "American IPA",
```

2. In `MapToDetailDto` (around line 1726):
```csharp
    return new BatchDetailDto(
        Id: b.Id,
        UserId: b.UserId,
        RecipeId: b.RecipeId,
        RecipeName: b.Recipe?.Name ?? b.RecipeName,
        BatchCode: b.BatchCode,
```

3. In `GetBatchesAsync` (around line 43):
Add `.Include(b => b.Recipe)` to query:
```csharp
    var query = _db.Batches
        .AsNoTracking()
        .Include(b => b.Recipe)
        .Include(b => b.Hlt)
        .Include(b => b.MashTun)
        .Include(b => b.Boiler)
        .Include(b => b.Fermenter)
        .Where(b => b.UserId == userId);
```
And in `BatchSummaryDto` projection:
```csharp
        RecipeName: b.Recipe?.Name ?? b.RecipeName
```

#### `src/BrewYou.ApiService/Endpoints/BatchDtos.cs`
Update `BatchSummaryDto` to include optional `string? RecipeName = null`:
```csharp
public record BatchSummaryDto(
    Guid Id,
    string BatchCode,
    string Name,
    Guid? RecipeId,
    string BeerStyle,
    BatchStatus Status,
    BrewStage CurrentStage,
    DateOnly BrewDate,
    int DaysActive,
    decimal TargetOg,
    decimal? MeasuredOg,
    decimal? CurrentGravity,
    decimal TargetFg,
    decimal? MeasuredFg,
    decimal? AlcoholByVolume,
    decimal ColorSrm,
    decimal? VesselTempC,
    Guid? FermenterId,
    string? FermenterName,
    decimal TargetBatchSizeLiters,
    decimal? MeasuredBatchSizeLiters,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? HltId = null,
    string? HltName = null,
    Guid? MashTunId = null,
    string? MashTunName = null,
    Guid? BoilerId = null,
    string? BoilerName = null,
    string? RecipeName = null
);
```

---

### Step 3. Recipe Deletion Safeguards & Active Batch Query

#### `src/BrewYou.ApiService/Services/RecipeService.cs`
1. Update `RecipeAccessResult` enum:
```csharp
public enum RecipeAccessResult
{
    Success,
    NotFound,
    Forbidden,
    Conflict,
    DuplicateName,
    InvalidIngredient
}
```

2. Update `IRecipeService` interface:
```csharp
public interface IRecipeService
{
    Task<(List<RecipeSummaryDto> Items, PaginationMeta Pagination)> GetRecipesAsync(string? userId, int page, int limit, string? scope = null);
    Task<(RecipeAccessResult Result, RecipeDetailDto? Recipe)> GetRecipeByIdAsync(Guid id, string? userId);
    Task<(RecipeAccessResult Result, RecipeDetailDto? Recipe, string? Error)> CreateRecipeAsync(CreateRecipeRequest request, string userId);
    Task<CalculateRecipeResponse> CalculateRecipeAsync(CalculateRecipeRequest request, string? userId = null);
    Task<(RecipeAccessResult Result, RecipeDetailDto? Recipe, string? Error)> UpdateRecipeAsync(Guid id, UpdateRecipeRequest request, string userId);
    Task<RecipeAccessResult> DeleteRecipeAsync(Guid id, string userId, bool force = false);
    Task<(RecipeAccessResult Result, List<RecipeActiveBatchDto>? Batches)> GetActiveBatchesForRecipeAsync(Guid recipeId, string userId);
    Task<bool> RecipeNameExistsAsync(string name, string userId);
}
```

3. Update `DeleteRecipeAsync`:
```csharp
    public async Task<RecipeAccessResult> DeleteRecipeAsync(Guid id, string userId, bool force = false)
    {
        var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == id);
        if (recipe == null)
        {
            return RecipeAccessResult.NotFound;
        }

        if (recipe.UserId != userId)
        {
            return RecipeAccessResult.Forbidden;
        }

        // Active batch guard: Check if recipe is in use by active batches unless force flag is set
        if (!force)
        {
            var hasActiveBatches = await db.Batches.AsNoTracking().AnyAsync(b =>
                b.RecipeId == id &&
                b.Status != BatchStatus.Completed &&
                b.Status != BatchStatus.Archived);

            if (hasActiveBatches)
            {
                return RecipeAccessResult.Conflict;
            }
        }

        // Decouple linked batches explicitly and snapshot RecipeName
        var linkedBatches = await db.Batches.Where(b => b.RecipeId == id).ToListAsync();
        foreach (var batch in linkedBatches)
        {
            batch.RecipeName ??= recipe.Name;
            batch.RecipeId = null;
            batch.UpdatedAt = DateTime.UtcNow;
        }

        db.Recipes.Remove(recipe);
        await db.SaveChangesAsync();

        return RecipeAccessResult.Success;
    }
```

4. Add `GetActiveBatchesForRecipeAsync`:
```csharp
    public async Task<(RecipeAccessResult Result, List<RecipeActiveBatchDto>? Batches)> GetActiveBatchesForRecipeAsync(Guid recipeId, string userId)
    {
        var recipe = await db.Recipes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == recipeId);
        if (recipe == null)
        {
            return (RecipeAccessResult.NotFound, null);
        }

        if (recipe.UserId != userId)
        {
            return (RecipeAccessResult.Forbidden, null);
        }

        var activeBatches = await db.Batches.AsNoTracking()
            .Where(b => b.RecipeId == recipeId &&
                        b.Status != BatchStatus.Completed &&
                        b.Status != BatchStatus.Archived)
            .OrderByDescending(b => b.BrewDate)
            .ThenByDescending(b => b.CreatedAt)
            .Select(b => new RecipeActiveBatchDto(
                b.Id,
                b.BatchCode,
                b.Name,
                b.Status,
                b.CurrentStage
            ))
            .ToListAsync();

        return (RecipeAccessResult.Success, activeBatches);
    }
```

#### `src/BrewYou.ApiService/Endpoints/RecipeDtos.cs` or `IRecipeService.cs`
Define `RecipeActiveBatchDto`:
```csharp
public record RecipeActiveBatchDto(
    Guid Id,
    string BatchCode,
    string Name,
    BatchStatus Status,
    BrewStage CurrentStage
);
```

#### `src/BrewYou.ApiService/Endpoints/RecipeEndpoints.cs`
1. Update `DELETE /{id:guid}`:
```csharp
        group.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IRecipeService recipeService,
            [FromQuery] bool force = false) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await recipeService.DeleteRecipeAsync(id, userId, force);
            return result switch
            {
                RecipeAccessResult.Success => Results.Ok(ApiResponse.Ok()),
                RecipeAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Recipe not found.")),
                RecipeAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", "You do not have permission to delete this recipe."), statusCode: StatusCodes.Status403Forbidden),
                RecipeAccessResult.Conflict => Results.Conflict(ApiResponse.Fail("RECIPE_IN_USE_BY_ACTIVE_BATCHES", "Cannot delete recipe currently linked to active batches without force.")),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .RequireAuthorization()
        .WithName("DeleteRecipe")
        .Produces<ApiResponse>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict);
```

2. Add `GET /{id:guid}/active-batches`:
```csharp
        group.MapGet("/{id:guid}/active-batches", async (
            Guid id,
            ClaimsPrincipal principal,
            IRecipeService recipeService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, batches) = await recipeService.GetActiveBatchesForRecipeAsync(id, userId);
            return result switch
            {
                RecipeAccessResult.Success => Results.Ok(ApiResponse<List<RecipeActiveBatchDto>>.Ok(batches!)),
                RecipeAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Recipe not found.")),
                RecipeAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", "You do not have access to this recipe."), statusCode: StatusCodes.Status403Forbidden),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .RequireAuthorization()
        .WithName("GetRecipeActiveBatches")
        .Produces<ApiResponse<List<RecipeActiveBatchDto>>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);
```

---

### Step 4. Backup & Restore Backwards Compatibility

#### `src/BrewYou.ApiService/Backup/Models/BackupContracts.cs`
In `BatchBackupDto`, add `string? RecipeName = null`:
```csharp
public record BatchBackupDto(
    Guid Id,
    Guid? RecipeId,
    Guid? BrewerySetupId,
    string BatchCode,
    string Name,
    string BeerStyle = "American IPA",
    BatchStatus Status = BatchStatus.Brewing,
    BrewStage CurrentStage = BrewStage.Mash,
    DateOnly BrewDate = default,
    decimal TargetOg = 1.050m,
    decimal TargetFg = 1.010m,
    decimal TargetAbv = 5.25m,
    decimal TargetIbu = 35.0m,
    decimal TargetColorSrm = 6.0m,
    decimal TargetBatchSizeLiters = 20.0m,
    int BoilTimeMinutes = 60,
    decimal EfficiencyPercent = 72.0m,
    decimal? MeasuredOg = null,
    decimal? CurrentGravity = null,
    decimal? MeasuredFg = null,
    decimal? AlcoholByVolume = null,
    decimal? BrewhouseEfficiency = null,
    decimal? MeasuredBatchSizeLiters = null,
    decimal? PitchTemperatureC = null,
    Guid? BoilerId = null,
    Guid? FermenterId = null,
    Guid? PackagingVesselId = null,
    Guid? HltId = null,
    Guid? MashTunId = null,
    string? Notes = null,
    DateTime CreatedAt = default,
    DateTime UpdatedAt = default,
    DateTime? CompletedAt = null,
    BatchVolumeProfileBackupDto? VolumeProfile = null,
    List<BatchIngredientBackupDto>? Ingredients = null,
    List<BatchMashStepBackupDto>? MashSteps = null,
    List<BatchFermentationStepBackupDto>? FermentationSteps = null,
    List<BatchStageHistoryBackupDto>? StageHistory = null,
    List<BatchReadingBackupDto>? Readings = null,
    List<BatchSensorAssignmentBackupDto>? SensorAssignments = null,
    string? RecipeName = null
);
```

#### `src/BrewYou.ApiService/Services/BackupService.cs`
1. In `ExportUserBackupAsync` (around line 285):
Add `.Include(b => b.Recipe)` to `batchEntities` query:
```csharp
        var batchEntities = await _db.Batches
            .AsNoTracking()
            .Include(b => b.Recipe)
            .Include(b => b.VolumeProfile)
            ...
```

2. In `MapBatchToBackupDto` (around line 1816):
Map `RecipeName`:
```csharp
        return new BatchBackupDto(
            b.Id,
            b.RecipeId,
            b.BrewerySetupId,
            ...
            RecipeName: b.Recipe?.Name ?? b.RecipeName
        );
```

3. In `ExecuteRestoreAsync`:
- On `MergeOverwriteExisting` (around line 1374):
```csharp
    existingBatch.RecipeId = mappedRecipeId;
    existingBatch.RecipeName = batchDto.RecipeName ?? existingBatch.RecipeName;
```
- On creating `new Batch` (around line 1445):
```csharp
    var mappedRecipe = existingUserRecipes.FirstOrDefault(r => r.Id == mappedRecipeId);
    var newBatch = new Batch
    {
        Id = newBatchId,
        UserId = userId,
        RecipeId = mappedRecipeId,
        RecipeName = batchDto.RecipeName ?? mappedRecipe?.Name,
        BrewerySetupId = mappedSetupId,
        ...
```

---

### Step 5. Frontend Types & Client Sync

#### `src/BrewYou.Web/src/lib/types/api.ts`
1. Add `RecipeActiveBatchDto`:
```typescript
export interface RecipeActiveBatchDto {
	id: string;
	batchCode: string;
	name: string;
	status: BatchStatus;
	currentStage: BrewStage;
}
```
2. Update `BatchSummaryDto` (around line 830):
```typescript
export interface BatchSummaryDto {
	id: string;
	batchCode: string;
	name: string;
	recipeId?: string | null;
	recipeName?: string | null;
	beerStyle: string;
	status: BatchStatus;
    ...
```

#### `src/BrewYou.Web/src/lib/api/client.ts`
Update `api.recipes`:
```typescript
	recipes: {
		...
		async delete(id: string, force = false): Promise<void> {
			const query = force ? '?force=true' : '';
			return request<void>(`/api/v1/recipes/${id}${query}`, { method: 'DELETE' });
		},

		async getActiveBatches(id: string): Promise<RecipeActiveBatchDto[]> {
			return request<RecipeActiveBatchDto[]>(`/api/v1/recipes/${id}/active-batches`);
		},
```

---

## 5. Verification Method

### Test Plan & Verification Commands

1. **Build Verification**:
   ```powershell
   dotnet build src/BrewYou.ApiService/BrewYou.ApiService.csproj
   ```
   Must compile with 0 errors and 0 warnings.

2. **Milestone 2 Acceptance Tests**:
   Execute the dedicated M2 tests in `CascadingE2ETests.cs`:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~T1_08"
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~T1_09"
   ```

3. **Backup Regression & Backwards Compatibility**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~BackupEndpointsTests"
   ```
   Verifies that:
   - Round-trip export and restore preserves `RecipeName` on restored batches.
   - Restoring legacy schemas (`v0`, `v1`, `v2`) without `RecipeName` completes without errors and populates `RecipeName` from restored recipe.

4. **Full Recipe and Batch Test Suites**:
   ```powershell
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~RecipeEndpointTests"
   dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~BatchEndpointTests"
   ```

5. **Frontend Type Check & Verification**:
   ```bash
   npm run check
   ```
   Verifies TypeScript contract alignment across `api.ts`, `client.ts`, and consumer components.
