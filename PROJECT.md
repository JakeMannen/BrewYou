# Project: Fullstack Cascading Effects Resolution for Equipment, Recipes, and Ingredients

## Architecture
- **Data & Backend Tier**: ASP.NET Core Web API (`src/BrewYou.ApiService`), EF Core 9 with PostgreSQL (`BrewYouDbContext`), Domain Services (`EquipmentService`, `RecipeService`, `IngredientService`, `BatchService`, `BrewerySetupService`, `BackupService`).
- **Frontend Client Tier**: SvelteKit 2 SPA client (`src/BrewYou.Web`), component hierarchy (`src/lib/components/`), reactive stores, localized UI (`src/lib/i18n/locales/en.json`, `sv.json`).
- **Backup & Restore Pipeline**: Schema serialization, versioning, conflict resolution (`MergeSkipExisting`, `MergeOverwriteExisting`, `ReplaceAll`), backwards compatibility (`BackupContracts.cs`, `BackupService.cs`).
- **Testing Tier**: Unit and integration test suites in `tests/BrewYou.ApiService.Tests/` and `src/BrewYou.Web/src/`.

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Equipment Sensor Decoupling | Decouple `BatchSensorAssignment` on equipment delete to eliminate FK 23503 500 error | M1 | FS-01 |
| 2 | Complete Batch Vessel Decoupling | Decouple HLT, Mash Tun, Boiler, Fermenter, Packaging Vessel in `DeleteEquipmentAsync` | M1 | FS-02 |
| 3 | Tap-to-Keg Decoupling | Clear `AssignedKegId` on Taps on keg deletion in backend and client state | M1 | FS-03 |
| 4 | BrewerySetup Deletion Guard | Pre-clean `BatchSensorAssignments` on setup delete | M1 | FS-04 |
| 5 | Active Batch Equipment Detection | Check all 5 vessels and sensors in `GetActiveBatchesForEquipmentAsync` and warn in modal | M1 | FS-05 |
| 6 | Batch Workspace Vessel Fallback | Remove arbitrary `equipmentList[0]` fallback; render unassigned placeholder | M1 | FS-06 |
| 7 | Packaging Vessel Select Null Safety | Gracefully handle deleted vessel in `StageMeasurementsCard.svelte` | M1 | FS-07 |
| 8 | Batch RecipeName Persistence | Snapshot `RecipeName` on `Batch` entity and DTOs; preserve name on recipe delete | M2 | FS-08 |
| 9 | Recipe Deletion Active Batch Guard | Prevent/warn recipe deletion if active batches are in progress | M2 | FS-09 |
| 10 | Printable Bottle Label Fallback | Render default label fallback in `labels/print/+page.svelte` when recipeId is null | M2 | FS-10 |
| 11 | Batch Wizard Deleted Recipe Handling | Catch 404 gracefully in `/batches/new?recipeId=...` | M2 | FS-11 |
| 12 | Custom Ingredient Update API | Implement `PUT /api/v1/ingredients/{id}` and `UpdateIngredientAsync` for renaming and editing | M3 | FS-16 |
| 13 | Ingredient Deletion Recipe Guard | Restrict deletion of ingredients in use by recipes instead of silent corruption | M3 | FS-12 |
| 14 | Ingredient Batch Usage Visibility | Track and display batch usages in `GetIngredientUsageAsync` and delete modal | M3 | FS-14 |
| 15 | Stock Deduction Name Fallback | Fallback to name matching in `DeductIngredientStockAsync` when `SourceIngredientId` is deleted | M3 | FS-13 |
| 16 | Recipe Editor Missing Ingredient UI | Render localized placeholder instead of raw UUIDs in `IngredientsCard.svelte` | M3 | FS-15 |
| 17 | Backup Dangling Guid Sanitization | Set dangling `SourceIngredientId` to null on restore if ingredient missing | M4 | FS-17 |
| 18 | Backup Community Recipe Cloning | Clone deleted community recipes on restore to preserve batch recipe associations | M4 | FS-18 |
| 19 | Backup Backwards Compatibility | Ensure schema v0, v1, v2 and `RecipeName` compatibility | M4 | AGENTS.md 3.J |
| 20 | Full E2E Cascading Test Suite | Comprehensive automated tests across all deletion and renaming scenarios | M5 | Acceptance Criteria |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Equipment Cascading Integrity & Sensor Decoupling | Decouple BatchSensorAssignments, HLT, MashTun, Keg-to-Tap; complete active batch check; fix UI vessel fallbacks | none | DONE |
| M2 | Recipe Lifecycle, Batch RecipeName Snapshot & Label Fallback | Snapshot RecipeName on Batch; active batch check; label print fallback; wizard 404 guard | none | IN_PROGRESS |
| M3 | Ingredient Lifecycle, Custom Ingredient Update API & Stock Decoupling | Add PUT /api/v1/ingredients/{id}; restrict delete if in use; stock deduction name fallback; UI UUID fix | none | PLANNED |
| M4 | User Backup & Restore Dangling Reference Sanitization & Recipe Cloning | Sanitize dangling Guids; clone community recipes on restore; verify schema backwards compatibility | M2, M3 | PLANNED |
| M5 | Final E2E Test Suite Pass & Adversarial Hardening | Verify 100% test pass on full suite (Tiers 1-4) + Tier 5 adversarial testing & forensic audit | M1, M2, M3, M4 | PLANNED |

## Interface Contracts

### Backend ↔ Frontend Equipment Contracts
- `DELETE /api/v1/inventory/equipment/{id}`:
  - Returns `200 OK` on clean decoupling.
  - Returns `409 Conflict` (`EQUIPMENT_IN_USE`) if assigned to an active batch without force flag.
- `GET /api/v1/inventory/equipment/{id}/active-batches`:
  - Returns `List<BatchSummaryDto>` checking `BoilerId`, `FermenterId`, `PackagingVesselId`, `HltId`, `MashTunId`, and `BatchSensorAssignments`.

### Backend ↔ Frontend Recipe Contracts
- `DELETE /api/v1/recipes/{id}`:
  - Sets `batch.RecipeName ??= recipe.Name` on linked batches, then decouples `batch.RecipeId = null`.
  - Returns `409 Conflict` (`RECIPE_IN_USE_BY_ACTIVE_BATCHES`) if active batches are in progress.
- `GET /api/v1/batches/{id}`:
  - Returns `BatchDetailDto` with `RecipeName: b.Recipe?.Name ?? b.RecipeName`.

### Backend ↔ Frontend Ingredient Contracts
- `PUT /api/v1/ingredients/{id}`:
  - Request: `UpdateIngredientRequest { Name, Type, PotentialGravity, ColorSrm, AlphaAcidPercent, AttenuationPercent, Description, Form }`
  - Returns `200 OK` with updated `IngredientDto`.
- `DELETE /api/v1/ingredients/{id}`:
  - If ingredient is used in recipes: returns `409 Conflict` (`INGREDIENT_IN_USE_BY_RECIPES`).
  - If ingredient is used in active batches: returns `409 Conflict` (`INGREDIENT_IN_USE_BY_BATCHES`).
- `GET /api/v1/ingredients/{id}/usage`:
  - Returns `IngredientUsageDto { RecipeCount, Recipes, BatchCount, Batches }`.

### User Backup & Restore Contracts
- `BatchBackupDto`:
  - Adds optional `string? RecipeName { get; set; }` with safe default `null`.
- `RestorePayload`:
  - `SourceIngredientId` sanitized to `null` if not found in live DB or mapping.
  - `ReferencedExternalRecipes` cloned as personal recipe if deleted on target server.

## Code Layout
- Backend Models & Context: `src/BrewYou.ApiService/Models/`, `src/BrewYou.ApiService/Data/BrewYouDbContext.cs`
- Backend DTOs & Endpoints: `src/BrewYou.ApiService/DTOs/`, `src/BrewYou.ApiService/Endpoints/`
- Backend Services: `src/BrewYou.ApiService/Services/`
- Backend Backup Subsystem: `src/BrewYou.ApiService/Backup/`
- Frontend Components: `src/BrewYou.Web/src/lib/components/`
- Frontend Routes: `src/BrewYou.Web/src/routes/`
- Frontend API Client & Types: `src/BrewYou.Web/src/lib/api/`, `src/BrewYou.Web/src/lib/types/`
- Localization: `src/BrewYou.Web/src/lib/i18n/locales/en.json`, `sv.json`
- Tests: `tests/BrewYou.ApiService.Tests/`, `src/BrewYou.Web/src/`
