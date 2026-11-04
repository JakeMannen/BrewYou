# BRIEFING — 2026-11-04T07:58:00Z

## Mission
Investigate cascading effects of deleting/renaming equipment, recipes, and ingredients on Data Layer & Backend Domain/Services (EF Core, Models, Schema/Migrations, DB Constraints, Foreign Keys, OnDelete, Transactions, Domain Events).

## 🔒 My Identity
- Archetype: teamwork_explorer
- Roles: explorer, investigator
- Working directory: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\teamwork_explorer_survey_1
- Original parent: d80eeb54-d338-4b53-876e-3933dc2f1932
- Milestone: Phase 0 Survey

## 🔒 Key Constraints
- Read-only investigation — do NOT implement
- Deep focus on Data Layer & Backend Domain/Services: EF Core, Models, Schema, Migrations, Constraints, FKs, OnDelete behaviors, cascading effects, API endpoints, failure scenarios, proposed solutions.

## Current Parent
- Conversation ID: d80eeb54-d338-4b53-876e-3933dc2f1932
- Updated: 2026-11-04T07:58:00Z

## Investigation State
- **Explored paths**:
  - `BrewYouDbContext.cs` entity model configurations, relationships, OnDelete behaviors, unique constraints.
  - Entities: `Equipment`, `Recipe`, `RecipeIngredient`, `Ingredient`, `IngredientStock`, `Batch`, `BatchIngredient`, `BatchSensorAssignment`, `BrewerySetup`, `EquipmentReading`.
  - Services: `EquipmentService`, `RecipeService`, `IngredientService`, `BatchService`, `BrewerySetupService`, `BackupService`.
  - Endpoints & Middleware: `EquipmentEndpoints`, `RecipeEndpoints`, `IngredientEndpoints`, `BatchEndpoints`, `BrewerySetupEndpoints`, `GlobalExceptionHandler`.
  - Frontend components & modals: `DeleteEquipmentModal.svelte`, `DeleteRecipeModal.svelte`, `DeleteIngredientModal.svelte`.
- **Key findings**:
  1. Equipment deletion throws unhandled `DbUpdateException` -> 500 Error when assigned as a sensor to a batch because `BatchSensorAssignment.EquipmentId` has `DeleteBehavior.Restrict` and is not decoupled.
  2. `EquipmentService.GetActiveBatchesForEquipmentAsync` omits HLT, Mash Tun, and Sensors, misleading the UI into thinking equipment is not in use.
  3. Deleting a recipe wipes out `RecipeName` permanently across all batches because `Batch` does not snapshot `RecipeName` and sets `RecipeId = null`.
  4. Deleting an ingredient silently strips it from recipes and recalculates their gravities/ABV down without user consent, and breaks `DeductIngredientStockAsync` due to dangling `SourceIngredientId`.
  5. Missing API endpoint for updating/renaming custom ingredients.
- **Unexplored areas**: None within the backend data layer scope.

## Key Decisions Made
- Fully documented 10 concrete failure scenarios with file paths, line numbers, and proposed architectural solutions in `handoff.md`.

## Artifact Index
- DISPATCH.md — Stored dispatch instructions
- progress.md — Liveness heartbeat
- handoff.md — Comprehensive Phase 0 survey handoff report
