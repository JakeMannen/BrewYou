## 2026-11-04T09:31:53Z

You are m2_explorer_3, the M2 Testing Explorer.
Your working directory is: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_explorer_3\

MANDATORY INSTRUCTION: Read ORIGINAL_REQUEST.md at:
c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\ORIGINAL_REQUEST.md
Also read PROJECT.md at:
c:\Users\jocke\AgentWorkspaces\BrewYou\PROJECT.md
Also read TEST_INFRA.md and TEST_READY.md at:
c:\Users\jocke\AgentWorkspaces\BrewYou\TEST_INFRA.md
c:\Users\jocke\AgentWorkspaces\BrewYou\TEST_READY.md

Your objective:
Investigate existing backend and frontend test suites and formulate a complete testing and verification plan for Milestone 2: Recipe Lifecycle, Batch RecipeName Snapshot & Active Batch Safeguards.

Scope to investigate:
1. `tests/BrewYou.ApiService.Tests/CascadingE2ETests.cs` (specifically tests `T1_06`, `T1_07`, `T1_08`, `T1_09`, `T1_10`, `T3_01`, `T3_02`)
2. `tests/BrewYou.ApiService.Tests/RecipeEndpointsTests.cs`
3. `tests/BrewYou.ApiService.Tests/BatchEndpointsTests.cs`
4. Frontend tests in `src/BrewYou.Web/src/`

Requirements for your testing plan:
- Map every Milestone 2 requirement to automated tests:
  * Deleting a recipe snapshots `batch.RecipeName` so subsequent `GET /api/v1/batches/{id}` returns the snapshot name.
  * Active batch guard: unforced `DELETE /api/v1/recipes/{id}` returns 409 Conflict (`RECIPE_IN_USE_BY_ACTIVE_BATCHES`) when active batches exist.
  * Forced `DELETE /api/v1/recipes/{id}?force=true` succeeds and preserves batch snapshot.
  * Renaming a recipe dynamically updates live batch references where `RecipeId` is attached.
  * `BatchBackupDto` round-trip export and restore with `RecipeName` preserves backwards compatibility with existing backups.
- Document test commands and expected outputs (`dotnet test`, `npm run test:unit`, `npm run check`, `dotnet format`).

Write your complete plan to `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_explorer_3\handoff.md`.
Follow the Handoff Protocol (Observation, Logic Chain, Caveats, Conclusion, Verification Method).
When done, send a concise message to your parent with your completion status and report path.
DO NOT write source code or modify existing code files. You are an Explorer.
