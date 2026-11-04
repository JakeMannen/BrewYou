## 2026-11-04T09:31:28Z
You are m2_explorer_1, the M2 Backend Domain Explorer.
Your working directory is: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_explorer_1\

MANDATORY INSTRUCTION: Read ORIGINAL_REQUEST.md at:
c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\ORIGINAL_REQUEST.md
Also read PROJECT.md at:
c:\Users\jocke\AgentWorkspaces\BrewYou\PROJECT.md
Also read investigation_report.md at:
c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\investigation_report.md (specifically Section 2.2, FS-08, FS-09)

Your objective:
Investigate the backend code and formulate a complete, concrete technical implementation plan for Milestone 2: Recipe Lifecycle, Batch RecipeName Snapshot & Active Batch Safeguards.

Scope to investigate:
1. `src/BrewYou.ApiService/Models/Batch.cs`
2. `src/BrewYou.ApiService/Services/BatchService.cs`
3. `src/BrewYou.ApiService/Services/RecipeService.cs`
4. `src/BrewYou.ApiService/Endpoints/RecipeEndpoints.cs`
5. `src/BrewYou.ApiService/Endpoints/BatchEndpoints.cs`
6. `src/BrewYou.ApiService/DTOs/`
7. `src/BrewYou.ApiService/Backup/Models/BackupContracts.cs`
8. `src/BrewYou.ApiService/Services/BackupService.cs`

Requirements for your implementation plan:
- FS-08: Add `public string? RecipeName { get; set; }` to `Batch.cs`. Populate it on batch creation in `CreateBatchAsync` (`batch.RecipeName = recipe?.Name`).
- On `RecipeService.DeleteRecipeAsync`: snapshot `batch.RecipeName ??= recipe.Name` before setting `batch.RecipeId = null`.
- FS-09: Active batch protection on recipe deletion:
  - Check if any linked batches have active status (`Planned`, `Brewing`, `Fermenting`, `Conditioning`).
  - If active batches exist and `!force`, return 409 Conflict with code `RECIPE_IN_USE_BY_ACTIVE_BATCHES` and list/count of active batches.
  - If `force == true`, allow deletion with snapshotting.
  - Add `force` query parameter support in `DELETE /api/v1/recipes/{id}`.
  - Add helper endpoint `GET /api/v1/recipes/{id}/active-batches` returning active batches for frontend deletion warning modal.
- Ensure `BatchService.MapToDetailDto` and related mapping methods return `b.Recipe?.Name ?? b.RecipeName`.
- Update `BackupContracts.cs` (`BatchBackupDto`), `BackupService.cs`, and `src/BrewYou.Web/src/lib/types/api.ts` with optional `string? RecipeName` to satisfy AGENTS.md Section 3.J backwards compatibility.

Write your complete plan to `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_explorer_1\handoff.md`.
Follow the Handoff Protocol (Observation, Logic Chain, Caveats, Conclusion, Verification Method).
When done, send a concise message to your parent with your completion status and report path.
DO NOT write source code or modify existing code files. You are an Explorer.
