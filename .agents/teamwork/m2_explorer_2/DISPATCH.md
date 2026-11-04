## 2026-11-04T09:31:45Z
You are m2_explorer_2, the M2 Frontend UI Explorer.
Your working directory is: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_explorer_2\

MANDATORY INSTRUCTION: Read ORIGINAL_REQUEST.md at:
c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\ORIGINAL_REQUEST.md
Also read PROJECT.md at:
c:\Users\jocke\AgentWorkspaces\BrewYou\PROJECT.md
Also read investigation_report.md at:
c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\investigation_report.md (specifically Section 2.2, FS-10, FS-11)

Your objective:
Investigate the frontend SvelteKit UI and formulate a complete, concrete technical implementation plan for Milestone 2: Bottle Label Fallback, Batch Creation Wizard Stale Recipe Handling, and Recipe Deletion Modal Active Batch Safeguards.

Scope to investigate:
1. `src/BrewYou.Web/src/routes/batches/[id]/labels/print/+page.svelte`
2. `src/BrewYou.Web/src/routes/batches/new/+page.svelte`
3. `src/BrewYou.Web/src/lib/components/recipes/DeleteRecipeModal.svelte` (or check how recipe deletion is handled in `src/BrewYou.Web/src/routes/recipes/`)
4. `src/BrewYou.Web/src/lib/api/client.ts`
5. `src/BrewYou.Web/src/lib/types/api.ts`
6. `src/BrewYou.Web/src/lib/i18n/locales/en.json` and `sv.json`

Requirements for your implementation plan:
- FS-10: Bottle Label Printing Graceful Fallback:
  - Inspect `src/BrewYou.Web/src/routes/batches/[id]/labels/print/+page.svelte` around line 223.
  - When `label` is null/missing (or recipe deleted), design an `{:else}` branch rendering a clean default bottle label with batch name, recipe name fallback (`batch.recipeName ?? batch.recipe?.name`), ABV, date, and batch number, instead of returning an empty blank page.
- FS-11: Batch Creation Wizard Stale Recipe Handling:
  - Inspect `src/BrewYou.Web/src/routes/batches/new/+page.svelte`.
  - Handle 404 on deleted `recipeId` gracefully (e.g. show a clear warning toast and allow user to pick a different recipe or proceed with custom parameters, rather than crashing).
- Active Batch Recipe Deletion Modal:
  - In `DeleteRecipeModal.svelte` (or the recipe deletion modal component): query `/api/v1/recipes/{id}/active-batches` or handle 409 Conflict (`RECIPE_IN_USE_BY_ACTIVE_BATCHES`), showing active batch count/names and providing a force-delete confirmation checkbox.
- i18n Localization:
  - Enumerate all required translation keys across `en.json`, `sv.json`, `de.json`, `fr.json`, `es.json` with 100% key parity.

Write your complete plan to `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_explorer_2\handoff.md`.
Follow the Handoff Protocol (Observation, Logic Chain, Caveats, Conclusion, Verification Method).
When done, send a concise message to your parent with your completion status and report path.
DO NOT write source code or modify existing code files. You are an Explorer.
