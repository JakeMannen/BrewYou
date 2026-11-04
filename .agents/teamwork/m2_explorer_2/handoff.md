# Milestone 2: Frontend UI Technical Implementation Plan & Handoff Report

**Author**: `m2_explorer_2` (Teamwork Frontend UI Explorer)  
**Roles**: Investigator, Synthesizer  
**Status**: Completed  
**Date**: 2026-11-04  
**Target Milestone**: Milestone 2 (Frontend SvelteKit UI: Bottle Label Fallback, Wizard Stale Recipe Handling, Active Batch Recipe Safeguards)

---

## 1. Observation

Direct examination of the frontend and backend codebase, API contracts, translation dictionaries, and test suites revealed the following concrete file locations, line numbers, and failure behaviors:

### A. FS-10: Bottle Label Printing Route (`src/BrewYou.Web/src/routes/batches/[id]/labels/print/+page.svelte`)
1. **Unhandled Recipe Label Exception in `onMount` (Lines 53–74)**:
   ```svelte
   		try {
   			const b = await api.batches.getById(currentBatchId);
   			batch = b;
   			if (b.recipeId) {
   				label = await api.recipes.label.get(b.recipeId);
   			}
   		} catch (err) {
   			if (err instanceof ApiClientError) {
   				errorMessage = err.message;
   			} else {
   				errorMessage = err instanceof Error ? err.message : t('common.error_generic');
   			}
   		} finally {
   			loading = false;
   		}
   ```
   - When a recipe has been deleted or has no uploaded label, `api.recipes.label.get(b.recipeId)` throws an `ApiClientError` (HTTP 404).
   - This error is caught by the outer catch block, which sets `errorMessage = "Not Found"`.
   - The user is completely blocked from viewing or printing any label for their batch, even though all batch metadata (`batchCode`, `name`, `beerStyle`, `targetAbv`, `recipeName`) is loaded and valid.

2. **Missing Fallback Rendering Branch (Lines 213–268)**:
   ```svelte
   	{#if loading}
   		<div class="flex h-64 items-center justify-center">
   			<Loader2 class="h-8 w-8 animate-spin text-amber-500" />
   		</div>
   	{:else if errorMessage}
   		<div
   			class="rounded-2xl border border-red-500/20 bg-red-500/10 p-4 text-xs text-red-600 dark:text-red-400"
   		>
   			{errorMessage}
   		</div>
   	{:else if batch && label}
   		<!-- Printable Labels Sheet Canvas -->
   		<div
   			class="sheet-canvas mx-auto flex flex-wrap justify-center gap-4 bg-white p-6 shadow-md print:m-0 print:gap-2 print:p-0 print:shadow-none"
   		>
               ...
           </div>
   	{/if}
   ```
   - When `b.recipeId` is `null` (e.g. ad-hoc batch or a batch whose recipe was deleted and decoupled), `label` is `null`.
   - The condition `batch && label` evaluates to `false`.
   - The template contains NO `{:else}` or `{:else if batch}` branch.
   - **Result**: The page renders an empty white container with zero labels below the header controls.

3. **Recipe Name Fallback in Footer Strip (Line 254)**:
   ```svelte
   <span class="truncate">{batch.recipeName}</span>
   ```
   - If `batch.recipeName` is null (e.g. ad-hoc batch or legacy pre-snapshot batch), the field renders blank rather than gracefully displaying the beer style or a localized indicator.

---

### B. FS-11: Batch Creation Wizard Stale Recipe Handling (`src/BrewYou.Web/src/routes/batches/new/+page.svelte`)
1. **Fatal Unhandled 404 on URL Parameter `recipeId` (Lines 375–397)**:
   ```svelte
   			// Check URL query param
   			const urlRecipeId = page.url.searchParams.get('recipeId');
   			if (urlRecipeId) {
   				selectedRecipeId = urlRecipeId;
   				await handleRecipeSelect(urlRecipeId);
   			} else if (recipes.length > 0) {
   				selectedRecipeId = recipes[0].id;
   				await handleRecipeSelect(recipes[0].id);
   			} else {
   				isAdHoc = true;
   				batchName = t('batches.wizard.default_adhoc_name');
   				recipeMashSteps = [];
   			}
   ```
2. **`handleRecipeSelect` Halts on Exception (Lines 399–442)**:
   ```svelte
   	async function handleRecipeSelect(id: string) {
   		if (!id) return;
   		try {
   			const r = await api.recipes.getById(id);
               ...
   		} catch (err: unknown) {
   			error = (err as Error).message;
   		}
   	}
   ```
3. **Blocking UI State (Lines 666–674)**:
   ```svelte
   	<!-- Error Alert -->
   	{#if error}
   		<div
   			role="alert"
   			class="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
   		>
   			{error}
   		</div>
   	{/if}
   ```
   - If the user follows a backlink, bookmark, or history item `/batches/new?recipeId=<deleted-id>`:
     * `api.recipes.getById(urlRecipeId)` throws HTTP 404 `ApiClientError`.
     * `error` is assigned `"Recipe not found"`.
     * `selectedRecipeId` remains set to the invalid deleted UUID.
     * The `<select id="recipe-select">` dropdown does NOT contain this deleted ID (since `recipes` comes from `api.recipes.list()` which excludes deleted recipes).
     * The form vitals and steps are left empty.
     * A blocking red alert box is displayed, and attempting to submit sends the invalid ID, failing with another server error.

---

### C. Active Batch Recipe Deletion Modal (`src/BrewYou.Web/src/lib/components/recipes/DeleteRecipeModal.svelte`)
1. **Direct Unchecked Deletion (Lines 26–40)**:
   ```svelte
   	async function handleConfirmDelete() {
   		if (!recipe || isDeleting) return;

   		isDeleting = true;
   		errorMessage = null;
   		try {
   			await api.recipes.delete(recipe.id);
   			onDeleted(recipe.id);
   			onClose();
   		} catch (err: unknown) {
   			errorMessage = (err as Error).message || t('recipes.delete_modal.error');
   		} finally {
   			isDeleting = false;
   		}
   	}
   ```
   - Does not query or display active batches brewed from this recipe.
   - Does not support a `force` query parameter.
   - When backend implements FS-09 and returns `409 Conflict` (`RECIPE_IN_USE_BY_ACTIVE_BATCHES`), the modal catches it as a generic error, displaying raw error text without listing the conflicting active batches or offering a force-delete confirmation checkbox.
2. **Missing Architecture Parity with Equipment Deletion**:
   - `src/BrewYou.Web/src/lib/components/inventory/DeleteEquipmentModal.svelte:40–76` already implements:
     * `loadActiveBatches(id)`
     * `isConflict` flag
     * `forceDelete` boolean toggle
     * Active batch cards listing batch code, name, and current stage with link to `/batches/{id}`
     * Force delete confirmation checkbox.
   - `DeleteRecipeModal.svelte` lacks this safeguard.

---

### D. Contracts & Types Parity
1. **`src/BrewYou.Web/src/lib/types/api.ts`**:
   - `EquipmentActiveBatchDto` exists (line 727), but `RecipeActiveBatchDto` is missing.
   - `BatchSummaryDto` (line 830) currently lacks `recipeName?: string | null;` (needed when backend snapshots recipe name onto all batch summary DTOs).
2. **`src/BrewYou.Web/src/lib/api/client.ts`**:
   - `api.recipes.delete(id: string)` lacks the `force?: boolean` argument (line 742).
   - `api.recipes.getActiveBatches(id: string)` endpoint client method is missing (line 745).

---

### E. Localization Key Verification Across 5 Locales
- Verification of `src/BrewYou.Web/src/lib/i18n/locales/` (`en.json`, `sv.json`, `de.json`, `es.json`, `fr.json`) confirmed that all 5 files currently exist and have 100% key parity at 1922 lines each.
- New keys added for Milestone 2 must be populated simultaneously across all 5 files.

---

## 2. Logic Chain

1. **Premise 1 (FS-10 Observation A.1 & A.2)**: Decoupled batches have `recipeId == null` or an invalid/unlabelled recipe. Because `print/+page.svelte:223` only renders when `batch && label` is truthy, decoupled batches fail silently and display a blank page. Wrapping `api.recipes.label.get(b.recipeId)` in a localized try/catch and adding an `{:else if batch}` template branch will guarantee that every batch can print bottle labels regardless of recipe status.
2. **Premise 2 (BJCP and Artisan Label Standards)**: In the absence of a custom uploaded graphic, homebrewers need standard bottle labels containing the batch code, batch name, beer style, recipe name, ABV, and brew date. Providing a dedicated artisan card layout inside `{:else if batch}` fulfills FS-10 while matching the exact physical sheet dimensions (`labelWidthMm`, `labelHeightMm`, `labelCount`, and `showCutLines`).
3. **Premise 3 (FS-11 Observation B.1 & B.2)**: Stale or deleted recipe IDs passed via query parameters trigger a 404 from `api.recipes.getById`. Halting the wizard with a fatal error breaks user workflow. Handling 404 specifically by falling back to the first available recipe in `recipes` (or ad-hoc mode when no recipes exist) and setting a dismissible warning message allows the user to continue seamlessly without crashing.
4. **Premise 4 (Active Batch Safeguards Observation C.1 & C.2)**: Recipes in active production (stages: Mash, Boil, Ferment, Condition) should not be silently decoupled without user acknowledgment. Querying `/api/v1/recipes/{id}/active-batches` on modal open and catching 409 Conflict enables `DeleteRecipeModal.svelte` to display active batch details and require an explicit force-delete confirmation checkbox before proceeding.
5. **Premise 5 (API Contract Synchronization)**: Updating `client.ts` and `api.ts` with `RecipeActiveBatchDto`, `api.recipes.getActiveBatches`, and `api.recipes.delete(id, force)` ensures compile-time type safety (`npm run check`) and runtime parity with the backend Minimal API endpoints.

---

## 3. Caveats

1. **Backend Dependency Synchronization**: The active batch check in `DeleteRecipeModal.svelte` relies on backend endpoint `GET /api/v1/recipes/{id}/active-batches` and query parameter `DELETE /api/v1/recipes/{id}?force=true`. The frontend modal design is built to be resilient: if `getActiveBatches` returns 404 or an empty array initially, the modal falls back to intercepting `409 Conflict` (`RECIPE_IN_USE_BY_ACTIVE_BATCHES`) upon deletion attempt and activates the force-delete checkbox.
2. **Batch Detail vs. Summary DTO**: On the batch detail page (`batches/[id]/+page.svelte`), `batch.recipeName` is already typed on `BatchDetailDto`. In `BatchSummaryDto` (used on `batches/+page.svelte`), `recipeName` should be optional (`recipeName?: string | null`) to maintain compatibility with older payloads.
3. **Non-Destructive Styling**: The fallback bottle label inside `batches/[id]/labels/print/+page.svelte` strictly respects the `@media print` rules, avoiding dark inks on paper and ensuring crisp vector borders with high-contrast text.

---

## 4. Conclusion & Technical Implementation Plan

Below is the concrete, file-by-file implementation specification for Milestone 2 Frontend UI.

---

### Step 1: Types & API Client Synchronization

#### 1.1 `src/BrewYou.Web/src/lib/types/api.ts`
Add `RecipeActiveBatchDto` and update `BatchSummaryDto`:

```typescript
// Add after line 734 (following EquipmentActiveBatchDto)
export interface RecipeActiveBatchDto {
	id: string;
	batchCode: string;
	name: string;
	status: BatchStatus;
	currentStage: BrewStage;
}

// In BatchSummaryDto (line 830):
export interface BatchSummaryDto {
	id: string;
	batchCode: string;
	name: string;
	recipeId?: string | null;
	recipeName?: string | null; // <--- Add this property
	beerStyle: string;
    ...
```

#### 1.2 `src/BrewYou.Web/src/lib/api/client.ts`
Update `recipes.delete` and add `recipes.getActiveBatches`:

```typescript
// Replace lines 742-744 in src/BrewYou.Web/src/lib/api/client.ts:
<<<<
		async delete(id: string): Promise<void> {
			return request<void>(`/api/v1/recipes/${id}`, { method: 'DELETE' });
		},
====
		async delete(id: string, force = false): Promise<void> {
			const query = force ? '?force=true' : '';
			return request<void>(`/api/v1/recipes/${id}${query}`, { method: 'DELETE' });
		},

		async getActiveBatches(id: string): Promise<RecipeActiveBatchDto[]> {
			return request<RecipeActiveBatchDto[]>(`/api/v1/recipes/${id}/active-batches`);
		},
>>>>
```

---

### Step 2: FS-10 — Bottle Label Printing Graceful Fallback
**Target File**: `src/BrewYou.Web/src/routes/batches/[id]/labels/print/+page.svelte`

#### 2.1 Update `onMount` (Lines 53–74)
Safely swallow 404s when fetching recipe label so `label` falls back to `null` instead of setting `errorMessage`:

```svelte
		try {
			const b = await api.batches.getById(currentBatchId);
			batch = b;
			if (b.recipeId) {
				try {
					label = await api.recipes.label.get(b.recipeId);
				} catch {
					// Recipe may not have a custom label generated, or recipe was deleted/missing.
					// Gracefully fall back to null so default label fallback renders.
					label = null;
				}
			}
		} catch (err) {
			if (err instanceof ApiClientError) {
				errorMessage = err.message;
			} else {
				errorMessage = err instanceof Error ? err.message : t('common.error_generic');
			}
		} finally {
			loading = false;
		}
```

#### 2.2 Add Default Label Sheet Rendering (around Line 268)
Insert `{:else if batch}` branch rendering the default artisan bottle label when `label` is null:

```svelte
	{:else if batch && label}
		<!-- Printable Labels Sheet Canvas (Custom Label Graphic) -->
		<div
			class="sheet-canvas mx-auto flex flex-wrap justify-center gap-4 bg-white p-6 shadow-md print:m-0 print:gap-2 print:p-0 print:shadow-none"
		>
			{#each Array.from({ length: labelCount }, (_, i) => i) as idx}
				<div
					class="bottle-label-item relative flex flex-col justify-between overflow-hidden bg-white {showCutLines
						? 'border border-dashed border-zinc-300 print:border-zinc-400'
						: ''}"
					style="width: {labelWidthMm}mm; height: {labelHeightMm}mm;"
				>
					<!-- Bottle Artwork -->
					<div class="relative flex-1 overflow-hidden">
						<img
							src={resolveApiUrl(getLabelImageUrl(label.imageUrls, 'bottle'))}
							alt="{batch.name} label #{idx + 1}"
							class="h-full w-full object-cover"
						/>
					</div>

					<!-- Batch Meta Strip Footer -->
					{#if showBatchMeta}
						<div class="border-t border-zinc-200 bg-white px-2 py-1 text-zinc-900">
							<div class="flex items-center justify-between font-mono text-[8px]">
								<span class="font-bold text-amber-800">{batch.batchCode}</span>
								{#if showAbv && displayAbv != null}
									<span class="font-bold text-emerald-800">{displayAbv}%</span>
								{/if}
							</div>
							<div class="flex items-center justify-between text-[7px] text-zinc-600">
								<span class="truncate">{batch.recipeName ?? batch.beerStyle}</span>
								<span>{new Date(batch.brewDate).toLocaleDateString()}</span>
							</div>
						</div>
					{:else if showAbv && displayAbv != null}
						<div
							class="border-t border-zinc-200 bg-white px-2 py-1 text-center font-mono text-[9px] font-bold text-zinc-900"
						>
							<span>{displayAbv}%</span>
						</div>
					{/if}
				</div>
			{/each}
		</div>
	{:else if batch}
		<!-- Default Artisan Bottle Label Sheet Canvas (Fallback when label is null / missing / recipe decoupled) -->
		<div
			data-testid="default-bottle-labels-canvas"
			class="sheet-canvas mx-auto flex flex-wrap justify-center gap-4 bg-white p-6 shadow-md print:m-0 print:gap-2 print:p-0 print:shadow-none"
		>
			{#each Array.from({ length: labelCount }, (_, i) => i) as idx}
				<div
					class="bottle-label-item relative flex flex-col justify-between overflow-hidden bg-white p-3 text-zinc-900 {showCutLines
						? 'border border-dashed border-zinc-300 print:border-zinc-400'
						: ''}"
					style="width: {labelWidthMm}mm; height: {labelHeightMm}mm;"
				>
					<!-- Decorative Inner Border Frame -->
					<div
						class="relative flex h-full w-full flex-col justify-between rounded-lg border-2 border-amber-900/40 bg-gradient-to-b from-amber-50/40 via-white to-amber-50/20 p-3 shadow-inner"
					>
						<!-- Label Header -->
						<div class="text-center">
							<span class="font-mono text-[8px] font-bold tracking-widest text-amber-800 uppercase">
								{t('labels.default_craft_mark')}
							</span>
							<div class="mx-auto my-1 h-px w-10 bg-amber-800/30"></div>
							<h3 class="font-serif text-sm font-extrabold tracking-tight text-zinc-900 line-clamp-2 leading-tight">
								{batch.name}
							</h3>
							<p class="mt-0.5 text-[10px] font-medium text-amber-900/80">
								{batch.beerStyle}
							</p>
						</div>

						<!-- Label Body / Recipe Fallback -->
						<div class="my-auto py-2 text-center">
							{#if batch.recipeName}
								<div class="inline-block rounded border border-amber-800/20 bg-amber-100/60 px-2 py-0.5 text-[9px] font-medium text-amber-950">
									{t('labels.recipe')}: {batch.recipeName}
								</div>
							{/if}
							{#if showAbv && displayAbv != null}
								<div class="mt-1 font-mono text-base font-black tracking-tight text-emerald-900">
									<span>{displayAbv}%</span>
								</div>
							{/if}
						</div>

						<!-- Label Footer / Batch Meta -->
						<div class="border-t border-amber-900/20 pt-1 text-zinc-700">
							<div class="flex items-center justify-between font-mono text-[8px] font-semibold">
								<span class="text-amber-800">{batch.batchCode}</span>
								<span>{new Date(batch.brewDate).toLocaleDateString()}</span>
							</div>
						</div>
					</div>
				</div>
			{/each}
		</div>
	{/if}
```

---

### Step 3: FS-11 — Batch Creation Wizard Stale Recipe Handling
**Target File**: `src/BrewYou.Web/src/routes/batches/new/+page.svelte`

#### 3.1 State and `onMount` Graceful Handling
1. Add state variable:
   `let staleRecipeWarning = $state<string | null>(null);`
2. Update `onMount` (Lines 375–388):
```svelte
			// Check URL query param
			const urlRecipeId = page.url.searchParams.get('recipeId');
			if (urlRecipeId) {
				const recipeInList = recipes.find((r) => r.id === urlRecipeId);
				if (recipeInList) {
					selectedRecipeId = urlRecipeId;
					await handleRecipeSelect(urlRecipeId);
				} else {
					try {
						selectedRecipeId = urlRecipeId;
						await handleRecipeSelect(urlRecipeId);
					} catch {
						staleRecipeWarning = t('batches.wizard.recipe_not_found_warning');
						if (recipes.length > 0) {
							selectedRecipeId = recipes[0].id;
							await handleRecipeSelect(recipes[0].id);
						} else {
							isAdHoc = true;
							selectedRecipeId = '';
							batchName = t('batches.wizard.default_adhoc_name');
							recipeMashSteps = [];
						}
					}
				}
			} else if (recipes.length > 0) {
				selectedRecipeId = recipes[0].id;
				await handleRecipeSelect(recipes[0].id);
			} else {
				isAdHoc = true;
				batchName = t('batches.wizard.default_adhoc_name');
				recipeMashSteps = [];
			}
```
3. Update `handleRecipeSelect` (Lines 399–442):
```svelte
	async function handleRecipeSelect(id: string) {
		if (!id) return;
		try {
			const r = await api.recipes.getById(id);
			batchName = r.name;
			beerStyle = r.beerStyle;
			targetBatchSizeLiters = r.batchSizeLiters;
			targetOg = r.originalGravity;
			targetFg = r.finalGravity;
			targetAbv = r.alcoholByVolume;
			targetIbu = r.bitternessIbu;
			targetColorSrm = r.colorSrm;
			boilTimeMinutes = r.boilTimeMinutes;
			efficiencyPercent = r.efficiencyPercent;

			if (r.ingredients && r.ingredients.length > 0) {
				const grainSum = r.ingredients
					.filter((i) => i.ingredientType === 'Fermentable')
					.reduce(
						(sum, i) =>
							sum + (i.unit.toLowerCase() === 'kg' ? Number(i.amount) : Number(i.amount) / 1000),
						0
					);
				if (grainSum > 0) totalGrainKg = Math.round(grainSum * 10) / 10;
			}

			if (r.mashSteps && r.mashSteps.length > 0) {
				recipeMashSteps = r.mashSteps;
			} else {
				recipeMashSteps = [];
			}

			if (r.fermentationSteps && r.fermentationSteps.length > 0) {
				recipeFermentationSteps = r.fermentationSteps;
				if (pitchTemperatureC === null || pitchTemperatureC === undefined) {
					pitchTemperatureC = r.fermentationSteps[0].targetTemperatureC;
				}
			} else {
				recipeFermentationSteps = [];
			}
		} catch (err: unknown) {
			if (err instanceof ApiClientError && err.status === 404) {
				staleRecipeWarning = t('batches.wizard.recipe_not_found_warning');
				if (recipes.length > 0 && selectedRecipeId !== recipes[0].id) {
					selectedRecipeId = recipes[0].id;
					await handleRecipeSelect(recipes[0].id);
				} else {
					isAdHoc = true;
					selectedRecipeId = '';
					batchName = t('batches.wizard.default_adhoc_name');
					recipeMashSteps = [];
				}
			} else {
				error = (err as Error).message;
			}
			throw err;
		}
	}
```
4. Render Dismissible Warning Banner in Template (above `currentStep === 1`):
```svelte
	{#if staleRecipeWarning}
		<div
			role="status"
			class="flex items-start justify-between gap-3 rounded-2xl border border-amber-500/30 bg-amber-500/10 p-4 text-xs text-amber-800 dark:border-amber-500/20 dark:text-amber-300"
		>
			<div class="flex items-start gap-2.5">
				<AlertTriangle class="h-4 w-4 shrink-0 text-amber-600 dark:text-amber-400 mt-0.5" />
				<div>
					<p class="font-semibold text-amber-900 dark:text-amber-200">
						{t('batches.wizard.recipe_not_found_title')}
					</p>
					<p class="mt-0.5 leading-relaxed">{staleRecipeWarning}</p>
				</div>
			</div>
			<button
				type="button"
				onclick={() => (staleRecipeWarning = null)}
				class="rounded-lg p-1 text-amber-700 hover:bg-amber-500/20 dark:text-amber-300"
				aria-label={t('common.close')}
			>
				<X class="h-4 w-4" />
			</button>
		</div>
	{/if}
```

---

### Step 4: Active Batch Recipe Deletion Modal Safeguards
**Target File**: `src/BrewYou.Web/src/lib/components/recipes/DeleteRecipeModal.svelte`

#### Full Replacement Specification for `DeleteRecipeModal.svelte`:

```svelte
<script lang="ts">
	import { api, ApiClientError } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import type { RecipeSummaryDto, RecipeActiveBatchDto } from '$lib/types/api';
	import { AlertTriangle, Loader2, X, Trash2, Info, ExternalLink, ShieldAlert } from '@lucide/svelte';

	interface Props {
		open: boolean;
		recipe: RecipeSummaryDto | null;
		onClose: () => void;
		onDeleted: (recipeId: string) => void;
	}

	let { open, recipe, onClose, onDeleted }: Props = $props();

	let activeBatches = $state<RecipeActiveBatchDto[]>([]);
	let isLoadingBatches = $state(false);
	let isDeleting = $state(false);
	let errorMessage = $state<string | null>(null);
	let isConflict = $state(false);
	let forceDelete = $state(false);

	$effect(() => {
		if (open && recipe) {
			errorMessage = null;
			isConflict = false;
			forceDelete = false;
			activeBatches = [];
			loadActiveBatches(recipe.id);
		} else {
			activeBatches = [];
			isLoadingBatches = false;
			isDeleting = false;
			errorMessage = null;
			isConflict = false;
			forceDelete = false;
		}
	});

	async function loadActiveBatches(recipeId: string) {
		isLoadingBatches = true;
		errorMessage = null;
		try {
			const batches = await api.recipes.getActiveBatches(recipeId);
			activeBatches = batches;
		} catch (err: unknown) {
			console.error('Failed to query active batches for recipe:', err);
		} finally {
			isLoadingBatches = false;
		}
	}

	async function handleConfirmDelete() {
		if (!recipe || isDeleting) return;

		isDeleting = true;
		errorMessage = null;
		try {
			await api.recipes.delete(recipe.id, forceDelete);
			onDeleted(recipe.id);
			onClose();
		} catch (err: unknown) {
			if (
				err instanceof ApiClientError &&
				(err.status === 409 || err.code === 'RECIPE_IN_USE_BY_ACTIVE_BATCHES')
			) {
				isConflict = true;
				errorMessage = t('recipes.delete_modal.conflict_error');
				loadActiveBatches(recipe.id);
			} else {
				errorMessage = (err as Error).message || t('recipes.delete_modal.error');
			}
		} finally {
			isDeleting = false;
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && open && !isDeleting) {
			onClose();
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open && recipe}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4"
		role="dialog"
		aria-modal="true"
		aria-labelledby="delete-recipe-modal-title"
	>
		<!-- Backdrop -->
		<div
			class="fixed inset-0 bg-black/70 backdrop-blur-sm transition-opacity"
			onclick={() => {
				if (!isDeleting) onClose();
			}}
			aria-hidden="true"
		></div>

		<!-- Dialog Panel -->
		<div
			class="relative z-10 max-h-[90vh] w-full max-w-lg overflow-y-auto rounded-2xl border border-zinc-200 bg-white p-6 shadow-2xl transition-all dark:border-zinc-800 dark:bg-zinc-950"
		>
			<!-- Header -->
			<div
				class="flex items-start justify-between border-b border-zinc-200 pb-4 dark:border-zinc-800"
			>
				<div class="flex items-center gap-3">
					<div
						class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-red-500/30 bg-red-500/10 text-red-600 dark:text-red-400"
					>
						<Trash2 class="h-5 w-5" />
					</div>
					<div>
						<h2
							id="delete-recipe-modal-title"
							class="text-lg font-bold text-zinc-900 dark:text-white"
						>
							{t('recipes.delete_modal.title')}
						</h2>
						<p class="text-xs text-zinc-500 dark:text-zinc-400">
							{recipe.beerStyle}
						</p>
					</div>
				</div>

				<button
					type="button"
					onclick={onClose}
					disabled={isDeleting}
					class="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 disabled:opacity-50 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('recipes.delete_modal.cancel')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<!-- Body -->
			<div class="space-y-4 py-4">
				<p class="text-sm text-zinc-600 dark:text-zinc-300">
					{t('recipes.delete_modal.warning', { name: recipe.name })}
				</p>

				<!-- Loading Active Batches Indicator -->
				{#if isLoadingBatches}
					<div
						class="flex items-center gap-2.5 rounded-xl border border-zinc-200 bg-zinc-50 p-3 text-xs text-zinc-500 dark:border-zinc-800 dark:bg-zinc-900/50 dark:text-zinc-400"
					>
						<Loader2 class="h-4 w-4 animate-spin text-amber-500" />
						<span>{t('recipes.delete_modal.checking_batches')}</span>
					</div>
				{/if}

				<!-- Active Batches Warning Banner -->
				{#if !isLoadingBatches && activeBatches.length > 0}
					<div
						class="rounded-xl border border-amber-500/40 bg-amber-500/10 p-4 dark:border-amber-500/30 dark:bg-amber-500/5"
					>
						<div class="flex items-start gap-3">
							<AlertTriangle class="h-5 w-5 shrink-0 text-amber-600 dark:text-amber-400" />
							<div class="w-full space-y-2">
								<h3 class="text-sm font-semibold text-amber-900 dark:text-amber-200">
									{t('recipes.delete_modal.in_use_warning_title')}
								</h3>
								<p class="text-xs text-amber-800/90 dark:text-amber-300/80">
									{t('recipes.delete_modal.in_use_warning_desc', { count: activeBatches.length })}
								</p>
								<div class="mt-2 space-y-1.5 pt-1">
									{#each activeBatches as batch}
										<div
											class="flex items-center justify-between rounded-lg border border-amber-500/20 bg-amber-500/10 px-3 py-1.5 text-xs"
										>
											<div class="flex flex-wrap items-center gap-2">
												<span class="font-medium text-zinc-800 dark:text-zinc-200">
													{batch.name}
													<span class="font-mono text-[11px] text-amber-700 dark:text-amber-400">
														({batch.batchCode})
													</span>
												</span>
												<span
													class="rounded-md bg-zinc-200/70 px-1.5 py-0.5 text-[10px] font-medium text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
												>
													{t(`batches.stages.${batch.currentStage.toLowerCase()}`)}
												</span>
											</div>
											<a
												href="/batches/{batch.id}"
												target="_blank"
												rel="noopener noreferrer"
												class="inline-flex items-center gap-1 font-semibold text-amber-700 underline transition hover:text-amber-800 dark:text-amber-400 dark:hover:text-amber-300"
											>
												<span>{t('recipes.delete_modal.view_batch')}</span>
												<ExternalLink class="h-3 w-3" />
											</a>
										</div>
									{/each}
								</div>
							</div>
						</div>
					</div>
				{/if}

				<!-- Batch Preservation Notice -->
				<div
					class="flex items-start gap-3 rounded-xl border border-amber-500/20 bg-amber-500/5 p-3.5 text-xs text-amber-800 dark:border-amber-500/15 dark:text-amber-300"
				>
					<Info class="mt-0.5 h-4 w-4 shrink-0 text-amber-600 dark:text-amber-400" />
					<p class="leading-relaxed">
						{t('recipes.delete_modal.batches_notice')}
					</p>
				</div>

				<!-- Error / Conflict Banner -->
				{#if errorMessage}
					<div
						class="flex items-start gap-2.5 rounded-xl border border-red-500/30 bg-red-500/10 p-3 text-xs font-medium text-red-600 dark:text-red-400"
						role="alert"
					>
						<ShieldAlert class="mt-0.5 h-4 w-4 shrink-0" />
						<div>{errorMessage}</div>
					</div>
				{/if}

				<!-- Force Delete Checkbox Option -->
				{#if activeBatches.length > 0 || isConflict}
					<label
						class="flex cursor-pointer items-start gap-2.5 rounded-xl border border-red-500/20 bg-red-500/5 p-3 text-xs text-red-900 dark:text-red-200"
					>
						<input
							type="checkbox"
							bind:checked={forceDelete}
							class="mt-0.5 h-4 w-4 rounded border-red-300 text-red-600 focus:ring-red-500 dark:border-red-700 dark:bg-zinc-800"
						/>
						<span class="font-medium">
							{t('recipes.delete_modal.force_delete_checkbox')}
						</span>
					</label>
				{/if}
			</div>

			<!-- Footer -->
			<div
				class="flex items-center justify-end gap-3 border-t border-zinc-200 pt-4 dark:border-zinc-800"
			>
				<button
					type="button"
					onclick={onClose}
					disabled={isDeleting}
					class="rounded-xl border border-zinc-200 px-4 py-2.5 text-sm font-semibold text-zinc-700 transition-colors hover:bg-zinc-100 disabled:opacity-50 dark:border-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-900"
				>
					{t('recipes.delete_modal.cancel')}
				</button>

				<button
					type="button"
					onclick={handleConfirmDelete}
					disabled={isDeleting || ((activeBatches.length > 0 || isConflict) && !forceDelete)}
					class="flex min-h-[42px] items-center gap-2 rounded-xl bg-red-600 px-4 py-2.5 text-sm font-bold text-white shadow-sm transition-all hover:bg-red-500 disabled:opacity-50"
				>
					{#if isDeleting}
						<Loader2 class="h-4 w-4 animate-spin" />
						<span>{t('recipes.delete_modal.deleting')}</span>
					{:else if forceDelete}
						<Trash2 class="h-4 w-4" />
						<span>{t('recipes.delete_modal.force_confirm_button')}</span>
					{:else}
						<Trash2 class="h-4 w-4" />
						<span>{t('recipes.delete_modal.confirm')}</span>
					{/if}
				</button>
			</div>
		</div>
	</div>
{/if}
```

---

### Step 5: Full 100% Key Parity i18n Localization Dictionaries

The following new keys MUST be added to all 5 locale files:

| Key Path | English (`en.json`) | Swedish (`sv.json`) | German (`de.json`) | Spanish (`es.json`) | French (`fr.json`) |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `labels.recipe` | `"Recipe"` | `"Recept"` | `"Rezept"` | `"Receta"` | `"Recette"` |
| `labels.default_craft_mark` | `"Artisan Craft Brew"` | `"Hantverksbryggd öl"` | `"Handwerklich gebrautes Bier"` | `"Cerveza artesanal"` | `"Bière artisanale"` |
| `labels.using_default_label` | `"Default Bottle Label (No custom artwork uploaded)"` | `"Standardetikett för flaska (Ingen egen grafik uppladdad)"` | `"Standard-Flaschenetikett (Keine Grafik hochgeladen)"` | `"Etiqueta de botella estándar (Sin diseño personalizado)"` | `"Étiquette standard (Aucune illustration importée)"` |
| `labels.default_label_desc` | `"Standard artisan bottle label formatted with batch metadata, style, ABV, and brew date."` | `"Standard flasketikett formaterad med batchdetaljer, ölstil, alkoholhalt och bryggdatum."` | `"Standard-Flaschenetikett mit Sud-Metadaten, Bierstil, Alkoholgehalt und Braudatum."` | `"Etiqueta de botella estándar con metadatos del lote, estilo, graduación y fecha."` | `"Étiquette standard mise en forme avec les métadonnées du brassin, le style et l'alcool."` |
| `batches.wizard.recipe_not_found_title` | `"Recipe Not Found"` | `"Receptet kunde inte hittas"` | `"Rezept nicht gefunden"` | `"Receta no encontrada"` | `"Recette introuvable"` |
| `batches.wizard.recipe_not_found_warning` | `"The recipe linked to this new batch could not be found (it may have been deleted). You can choose another recipe or formulate an ad-hoc batch."` | `"Receptet som länkats till denna nya batch kunde inte hittas (det kan ha tagits bort). Du kan välja ett annat recept eller skapa en ad-hoc-batch."` | `"Das mit diesem neuen Sud verknüpfte Rezept wurde nicht gefunden (möglicherweise wurde es gelöscht). Sie können ein anderes Rezept wählen oder einen Ad-hoc-Sud erstellen."` | `"La receta vinculada a este nuevo lote no se pudo encontrar (puede haber sido eliminada). Puede elegir otra receta o formular un lote personalizado."` | `"La recette associée à ce nouveau brassin est introuvable (elle a peut-être été supprimée). Vous pouvez choisir une autre recette ou formuler un brassin ponctuel."` |
| `recipes.delete_modal.checking_batches` | `"Checking for active batches brewed from this recipe..."` | `"Kontrollerar aktiva bryggningar från detta recept..."` | `"Aktive Sude dieses Rezepts werden überprüft..."` | `"Comprobando lotes activos vinculados a esta receta..."` | `"Vérification des brassins actifs issus de cette recette..."` |
| `recipes.delete_modal.in_use_warning_title` | `"Active Batches in Progress"` | `"Aktiva bryggningar pågår"` | `"Aktive Sude in Bearbeitung"` | `"Lotes activos en progreso"` | `"Brassins actifs en cours"` |
| `recipes.delete_modal.in_use_warning_desc` | `"This recipe is currently linked to {count} active batch(es). Deleting it will decouple the recipe definition from these in-progress batches while preserving their logged metrics and frozen recipe name."` | `"Detta recept är för närvarande kopplat till {count} aktiv(a) bryggning(ar). Om du tar bort det kopplas receptdefinitionen bort från de pågående bryggningarna, medan deras loggade mätvärden och sparade receptnamn bevaras."` | `"Dieses Rezept ist derzeit mit {count} aktiven Sud(en) verknüpft. Beim Löschen wird die Rezeptdefinition von diesen laufenden Suden entkoppelt, während Messwerte und der gesicherte Rezeptname erhalten bleiben."` | `"Esta receta está vinculada actualmente a {count} lote(s) activo(s). Si la elimina, se desacoplará la definición de receta conservando sus mediciones y el nombre de receta guardado."` | `"Cette recette est actuellement liée à {count} brassin(s) actif(s). Sa suppression dissociera la recette tout en conservant l'historique des mesures et le nom figé de la recette."` |
| `recipes.delete_modal.view_batch` | `"View Batch"` | `"Visa bryggning"` | `"Sud ansehen"` | `"Ver lote"` | `"Voir le brassin"` |
| `recipes.delete_modal.conflict_error` | `"Cannot delete recipe while active batches are in progress. Please complete or decouple active batches, or enable force delete."` | `"Kan inte ta bort recept medan aktiva bryggningar pågår. Vänligen slutför eller koppla bort aktiva bryggningar, eller aktivera tvångsradering."` | `"Rezept kann nicht gelöscht werden, während aktive Sude laufen. Bitte schließen Sie die aktiven Sude ab oder aktivieren Sie das erzwungene Löschen."` | `"No se puede eliminar la receta mientras haya lotes activos en curso. Complete o desvincule los lotes activos, o active el borrado forzado."` | `"Impossible de supprimer la recette tant que des brassins actifs sont en cours. Veuillez terminer ou dissocier les brassins actifs, ou forcer la suppression."` |
| `recipes.delete_modal.force_delete_checkbox` | `"Force delete anyway (preserves batch history and recipe name snapshot, detaches recipe link)"` | `"Tvångsradera ändå (bevarar batchhistorik och receptnamn, kopplar bort receptlänken)"` | `"Trotzdem erzwingen (erhält Sud-Verlauf und Rezeptnamen, trennt Rezeptverknüpfung)"` | `"Forzar eliminación de todos modos (conserva el historial del lote y el nombre, desvincula la receta)"` | `"Forcer la suppression malgré tout (conserve l'historique et le nom du brassin, dissocie le lien de recette)"` |
| `recipes.delete_modal.force_confirm_button` | `"Force Delete Recipe"` | `"Tvångsradera recept"` | `"Rezept erzwingen löschen"` | `"Forzar eliminación de receta"` | `"Forcer la suppression de la recette"` |

---

## 5. Verification Method

To independently verify the implementation, execute the following commands and check the specified artifacts:

### 1. Frontend Type Safety & Compile Check
```powershell
$env:PATH = "C:\Users\jocke\AppData\Local\nvm\v22.21.1;$env:PATH"
cd src/BrewYou.Web
npm run check
```
- **Expected Outcome**: `svelte-check found 0 errors and 0 warnings`.
- **Invalidation Condition**: Any missing property on `BatchSummaryDto` or type mismatch with `RecipeActiveBatchDto`.

### 2. Frontend Vitest Unit Tests
```powershell
$env:PATH = "C:\Users\jocke\AppData\Local\nvm\v22.21.1;$env:PATH"
cd src/BrewYou.Web
npm run test:unit
```
- **Expected Outcome**: All test suites pass (including existing `BottleLabelPrint.test.ts` and new `DeleteRecipeModal.test.ts`).
- **Target Invariant**: Assert that `BottleLabelPrint.test.ts` passes its 100% key parity assertions across all 5 locales (`en`, `sv`, `de`, `fr`, `es`).

### 3. Frontend Formatting & Linter Enforcement
```powershell
$env:PATH = "C:\Users\jocke\AppData\Local\nvm\v22.21.1;$env:PATH"
cd src/BrewYou.Web
npm run lint
```
- **Expected Outcome**: Prettier checks pass (`All matched files use Prettier code style!`) and ESLint reports zero errors and zero warnings.

### 4. Integration Test Verification against Backend
```powershell
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~CascadingE2ETests.T1_08|FullyQualifiedName~CascadingE2ETests.T1_09|FullyQualifiedName~CascadingE2ETests.T1_10|FullyQualifiedName~CascadingE2ETests.T1_11|FullyQualifiedName~CascadingE2ETests.T3_01"
```
- **Expected Outcome**: `T1_08` (RecipeName persistence), `T1_09` (Active batch guard 409 Conflict), `T1_10` (Bottle label fallback metadata), `T1_11` (Wizard 404 guard), and `T3_01` (Dual delete recipe & vessel) all pass cleanly.
