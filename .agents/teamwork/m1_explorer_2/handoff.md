# Milestone 1 Handoff Report: Frontend UI & SvelteKit Client Implementation Plan
**Agent**: `m1_explorer_2` (Frontend UI & SvelteKit Client Architecture)  
**Date**: 2026-11-04  
**Milestone**: Milestone 1 (Equipment Cascading Integrity & Sensor Decoupling)  
**Status**: Investigation Complete — Implementation Ready  

---

## 1. Observation

Direct observations and evidence gathered from the BrewYou codebase:

### Obs-1: `DeleteEquipmentModal.svelte` Lacks 409 Conflict Handling and Sensor Assignment Visibility
- **File**: `src/BrewYou.Web/src/lib/components/inventory/DeleteEquipmentModal.svelte:48-62`
- **Code Observation**:
  ```svelte
  async function handleConfirmDelete() {
      if (!equipment || isDeleting) return;

      isDeleting = true;
      errorMessage = null;
      try {
          await api.equipment.delete(equipment.id);
          onDeleted(equipment.id);
          onClose();
      } catch (err: unknown) {
          errorMessage = (err as Error).message || t('equipment.delete_failed');
      } finally {
          isDeleting = false;
      }
  }
  ```
- **Finding**: When the backend returns HTTP 409 Conflict (`EQUIPMENT_IN_USE`), the modal simply dumps `(err as Error).message` into a generic error div without structured explanation or an option for forced deletion. In addition, the modal only displays `batch.name` and `batch.batchCode` (lines 158-179); it fails to show the specific equipment usage role (e.g. Boiler, Mash Tun, Fermenter, Packaging Vessel, or Sensor Assignment).
- In `client.ts:461-463`, `api.equipment.delete(id: string)` does not support the `force` flag parameter (`?force=true`), preventing the UI from issuing forced cascading deletions even when intentional.

### Obs-2: Arbitrary First-Equipment Fallback in `batches/[id]/+page.svelte`
- **File**: `src/BrewYou.Web/src/routes/batches/[id]/+page.svelte:293-302`
- **Code Observation**:
  ```svelte
  // 5. Fallback: first equipment in list if any
  if (equipmentList.length > 0) {
      return {
          id: equipmentList[0].id,
          name: equipmentList[0].name
      };
  }

  return null;
  ```
- **Finding**: When an equipment assigned to a stage is deleted and decoupled to `null` (e.g. `boilerId = null`), `currentStageEquipment` falls back unconditionally to `equipmentList[0]`. If the first equipment in the user's inventory is a Grain Mill or Tap, the UI falsely indicates that the Grain Mill is the active stage vessel. Furthermore, on lines 1754 and 2010:
  ```svelte
  equipmentName={currentStageEquipmentName ?? batch.boilerName}
  ```
  even if `currentStageEquipmentName` is null, it displays `batch.boilerName` (which may be a stale name of a deleted boiler) even during Mash stage.
- In `StepTemperatureChart.svelte:209-213`:
  ```svelte
  {#if equipmentName}
      <p class="text-xs text-zinc-500 dark:text-zinc-400">
          {equipmentName}
      </p>
  {/if}
  ```
  It has no `{:else}` placeholder for unassigned vessels.
- In `LogStepTemperatureModal.svelte:56` and `70`:
  Line 56: `(equipmentList.length > 0 ? equipmentList[0].name : '')`
  Line 70: `defaultEquipmentId ?? (equipmentList.length > 0 ? equipmentList[0].id : '')`
  When no vessel is assigned, manual temperature readings are silently logged against `equipmentList[0]`.

### Obs-3: Missing Decoupling of Tap Cards in `inventory/equipment/+page.svelte`
- **File**: `src/BrewYou.Web/src/routes/inventory/equipment/+page.svelte:337-339`
- **Code Observation**:
  ```svelte
  function handleDeleted(equipmentId: string) {
      equipmentList = equipmentList.filter((e) => e.id !== equipmentId);
  }
  ```
- **Finding**: When a Keg is deleted, `equipmentList` only filters out the keg record itself. Any Tap cards in `equipmentList` referencing `tap.assignedKegId === equipmentId` or `tap.assignedKeg.id === equipmentId` remain untouched in local state. As a result, the Tap continues rendering the deleted keg, its beer style, and fill gauge until the user executes a hard page reload.

### Obs-4: Packaging Vessel Selection and Empty Guid in `StageMeasurementsCard.svelte`
- **File**: `src/BrewYou.Web/src/lib/components/batches/StageMeasurementsCard.svelte:143-147` & `634-644`
- **Code Observation**:
  ```svelte
  req.packagingVesselId = draftPackagingVesselId
      ? draftPackagingVesselId
      : batch.packagingVesselId
          ? '00000000-0000-0000-0000-000000000000'
          : null;
  ```
  ```svelte
  <select
      id="edit-packaging-vessel"
      bind:value={draftPackagingVesselId}
      class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
  >
      <option value="">{t('batches.workspace.bottled_cellared_option')}</option>
      {#each equipmentList.filter((e) => e.type === 'Keg') as keg}
          <option value={keg.id}>{keg.name} ({keg.capacity}L)</option>
      {/each}
  </select>
  ```
- **Finding**: When a batch's packaging vessel is deleted from equipment inventory, `batch.packagingVesselId` is absent from `equipmentList.filter(e => e.type === 'Keg')`. In the browser, `<select>` finds no matching `<option>`, rendering an empty or desynchronized state. Moreover, when submitting stage edits for unrelated fields, line 146 prematurely injects `00000000-0000-0000-0000-000000000000` (`Guid.Empty`) even if the user never intended to change the packaging vessel.

### Obs-5: Missing i18n Keys Across English and Swedish Locales
- **Files**: `src/BrewYou.Web/src/lib/i18n/locales/en.json` & `sv.json`
- **Finding**: Neither locale defines `batches.workspace.no_vessel_assigned` or `batches.workspace.deleted_vessel`. Neither locale provides keys for `conflict_error`, `force_delete_checkbox`, `force_confirm_button`, or equipment deletion role badges under `equipment.delete_modal`.

---

## 2. Logic Chain

1. **Active Batch Conflict & Sensor Visibility (Obs-1)**:
   - When deleting equipment, `EquipmentService.DeleteEquipmentAsync` will return HTTP 409 Conflict (`EQUIPMENT_IN_USE`) if active batches reference the vessel without `?force=true`.
   - In `client.ts`, updating `api.equipment.delete(id: string, force = false)` to pass `?force=true` allows the client to satisfy the backend contract.
   - In `types/api.ts`, augmenting `EquipmentActiveBatchDto` with optional `role?: string | null` provides the UI with contextual awareness (e.g. whether the item is used as Boiler, Fermenter, HLT, Mash Tun, Packaging Vessel, or Sensor).
   - In `DeleteEquipmentModal.svelte`, detecting `err instanceof ApiClientError && (err.status === 409 || err.code === 'EQUIPMENT_IN_USE')` enables setting `isConflict = true`, showing a clear explanation, and requiring the brewer to check an explicit "Force delete anyway" acknowledgement before enabling the delete action.

2. **Eliminating Arbitrary Fallback (Obs-2)**:
   - Removing lines 293-299 in `batches/[id]/+page.svelte` ensures `currentStageEquipment` returns `null` when no vessel or sensor is assigned to the active stage.
   - In `StepTemperatureChart.svelte`, adding `{:else}` renders `t('batches.workspace.no_vessel_assigned')` as a clean italicized placeholder rather than an empty element.
   - In `LogStepTemperatureModal.svelte`, removing `(equipmentList.length > 0 ? equipmentList[0].name : '')` and initializing `selectedEquipmentId = defaultEquipmentId ?? ''` prevents stealth telemetry logging to arbitrary equipment.

3. **Reactive Tap Card Decoupling (Obs-3)**:
   - Modifying `handleDeleted` in `inventory/equipment/+page.svelte` to map over `equipmentList` and set `assignedKegId: null` and `assignedKeg: null` for any equipment where `assignedKegId === equipmentId` immediately reactivates clean empty tap UI without a full reload.

4. **Graceful Handling of Deleted Packaging Vessel (Obs-4)**:
   - In `StageMeasurementsCard.svelte`, checking `batch.packagingVesselId && !equipmentList.some(e => e.id === batch.packagingVesselId)` allows rendering a disabled option indicating that the vessel was deleted (`{batch.packagingVesselName} (Deleted)`).
   - In `handleSubmit`, comparing `draftPackagingVesselId !== (batch.packagingVesselId ?? '')` guarantees that `packagingVesselId` is only sent to the server when the user deliberately modifies the selection.

5. **Strict i18n Key Parity (Obs-5)**:
   - Defining all new keys in lockstep across `en.json` and `sv.json` preserves 100% key parity and adheres to AGENTS.md Section 3.H.

---

## 3. Caveats

1. **Backend Endpoint Parity**: The frontend forced-deletion capability assumes the backend implementation in Milestone 1 (`m1_explorer_1`) supports `DELETE /api/v1/inventory/equipment/{id}?force=true`. If the backend does not accept `?force=true`, the modal will simply block deletion when active batches exist until they are decoupled.
2. **Read-Only Investigation Constraint**: In accordance with explorer rules, this report contains exact code proposals and diffs; implementation will be carried out by the orchestrator/teamwork workers.

---

## 4. Conclusion & Concrete Code Proposals

Below are the exact, line-by-line implementation proposals for all affected files.

---

### Proposal 1: `src/BrewYou.Web/src/lib/types/api.ts`
**Change**: Add `role?: string | null` to `EquipmentActiveBatchDto`.

```typescript
// Line 727:
export interface EquipmentActiveBatchDto {
	id: string;
	batchCode: string;
	name: string;
	status: BatchStatus;
	currentStage: BrewStage;
	role?: string | null;
}
```

---

### Proposal 2: `src/BrewYou.Web/src/lib/api/client.ts`
**Change**: Support optional `force` deletion query parameter in `api.equipment.delete`.

```typescript
// Lines 461-463:
<<<< BEFORE
		async delete(id: string): Promise<void> {
			return request<void>(`/api/v1/inventory/equipment/${id}`, { method: 'DELETE' });
		},
==== AFTER
		async delete(id: string, force = false): Promise<void> {
			const query = force ? '?force=true' : '';
			return request<void>(`/api/v1/inventory/equipment/${id}${query}`, { method: 'DELETE' });
		},
>>>>
```

---

### Proposal 3: `src/BrewYou.Web/src/lib/components/inventory/DeleteEquipmentModal.svelte`
**Change**: Full replacement of `DeleteEquipmentModal.svelte` to support:
1. Active batch roles and sensor assignments display.
2. Catching 409 Conflict (`EQUIPMENT_IN_USE`) from backend.
3. Explicit `forceDelete` checkbox and forced delete confirmation.

```svelte
<script lang="ts">
	import { api, ApiClientError } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import type { EquipmentDto, EquipmentActiveBatchDto } from '$lib/types/api';
	import { AlertTriangle, Loader2, X, ExternalLink, Trash2, ShieldAlert } from '@lucide/svelte';

	interface Props {
		open: boolean;
		equipment: EquipmentDto | null;
		onClose: () => void;
		onDeleted: (equipmentId: string) => void;
	}

	let { open, equipment, onClose, onDeleted }: Props = $props();

	let activeBatches = $state<EquipmentActiveBatchDto[]>([]);
	let isLoadingBatches = $state(false);
	let isDeleting = $state(false);
	let errorMessage = $state<string | null>(null);
	let isConflict = $state(false);
	let forceDelete = $state(false);

	$effect(() => {
		if (open && equipment) {
			errorMessage = null;
			isConflict = false;
			forceDelete = false;
			activeBatches = [];
			loadActiveBatches(equipment.id);
		} else {
			activeBatches = [];
			isLoadingBatches = false;
			isDeleting = false;
			errorMessage = null;
			isConflict = false;
			forceDelete = false;
		}
	});

	async function loadActiveBatches(equipmentId: string) {
		isLoadingBatches = true;
		errorMessage = null;
		try {
			const batches = await api.equipment.getActiveBatches(equipmentId);
			activeBatches = batches;
		} catch (err: unknown) {
			console.error('Failed to load active batches for equipment:', err);
		} finally {
			isLoadingBatches = false;
		}
	}

	async function handleConfirmDelete() {
		if (!equipment || isDeleting) return;

		isDeleting = true;
		errorMessage = null;
		try {
			await api.equipment.delete(equipment.id, forceDelete);
			onDeleted(equipment.id);
			onClose();
		} catch (err: unknown) {
			if (err instanceof ApiClientError && (err.status === 409 || err.code === 'EQUIPMENT_IN_USE')) {
				isConflict = true;
				errorMessage = t('equipment.delete_modal.conflict_error');
				loadActiveBatches(equipment.id);
			} else {
				errorMessage = (err as Error).message || t('equipment.delete_failed');
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

	function formatRoleLabel(role?: string | null): string | null {
		if (!role) return null;
		const key = `equipment.delete_modal.role_${role.toLowerCase().replace(/[^a-z]/g, '')}`;
		const localized = t(key);
		return localized !== key ? localized : role;
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open && equipment}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4"
		role="dialog"
		aria-modal="true"
		aria-labelledby="delete-equipment-modal-title"
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
							id="delete-equipment-modal-title"
							class="text-lg font-bold text-zinc-900 dark:text-zinc-100"
						>
							{t('equipment.delete_modal.title')}
						</h2>
						<p class="text-xs text-zinc-500 dark:text-zinc-400">
							{t('equipment.delete_modal.desc', { name: equipment.name })}
						</p>
					</div>
				</div>
				<button
					type="button"
					onclick={onClose}
					disabled={isDeleting}
					class="rounded-lg p-1 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 disabled:opacity-50 dark:hover:bg-zinc-900 dark:hover:text-zinc-200"
					aria-label={t('common.cancel')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<!-- Body -->
			<div class="mt-4 space-y-4">
				<!-- Confirmation Prompt -->
				<p class="text-sm text-zinc-600 dark:text-zinc-300">
					{t('equipment.delete_modal.confirm_prompt', { name: equipment.name })}
				</p>

				<!-- Loading Active Batches -->
				{#if isLoadingBatches}
					<div
						class="flex items-center gap-2.5 rounded-xl border border-zinc-200 bg-zinc-50 p-3 text-xs text-zinc-500 dark:border-zinc-800 dark:bg-zinc-900/50 dark:text-zinc-400"
					>
						<Loader2 class="h-4 w-4 animate-spin text-amber-500" />
						<span>{t('equipment.delete_modal.checking_batches')}</span>
					</div>
				{/if}

				<!-- Active Batches Warning Banner -->
				{#if !isLoadingBatches && activeBatches.length > 0}
					<div
						class="rounded-xl border border-amber-500/40 bg-amber-500/10 p-4 dark:border-amber-500/30 dark:bg-amber-500/5"
					>
						<div class="flex items-start gap-3">
							<AlertTriangle class="h-5 w-5 shrink-0 text-amber-600 dark:text-amber-400" />
							<div class="space-y-2 w-full">
								<h3 class="text-sm font-semibold text-amber-900 dark:text-amber-200">
									{t('equipment.delete_modal.in_use_warning_title')}
								</h3>
								<p class="text-xs text-amber-800/90 dark:text-amber-300/80">
									{t('equipment.delete_modal.in_use_warning_desc')}
								</p>
								<div class="mt-2 space-y-1.5 pt-1">
									{#each activeBatches as batch}
										<div
											class="flex items-center justify-between rounded-lg border border-amber-500/20 bg-amber-500/10 px-3 py-1.5 text-xs"
										>
											<div class="flex flex-wrap items-center gap-2">
												<span class="font-medium text-zinc-800 dark:text-zinc-200">
													{batch.name}
													<span class="font-mono text-[11px] text-amber-700 dark:text-amber-400"
														>({batch.batchCode})</span
													>
												</span>
												{#if batch.role}
													<span
														class="rounded-md border border-amber-500/30 bg-amber-500/20 px-1.5 py-0.5 text-[10px] font-semibold text-amber-900 dark:text-amber-200"
													>
														{formatRoleLabel(batch.role)}
													</span>
												{/if}
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
												<span>{t('equipment.delete_modal.view_batch')}</span>
												<ExternalLink class="h-3 w-3" />
											</a>
										</div>
									{/each}
								</div>
							</div>
						</div>
					</div>
				{/if}

				<!-- Conflict / Error Banner -->
				{#if errorMessage}
					<div
						class="flex items-start gap-2.5 rounded-xl border border-red-500/30 bg-red-500/10 p-3 text-xs font-medium text-red-600 dark:text-red-400"
						role="alert"
					>
						<ShieldAlert class="h-4 w-4 shrink-0 mt-0.5" />
						<div>{errorMessage}</div>
					</div>
				{/if}

				<!-- Force Delete Option -->
				{#if activeBatches.length > 0 || isConflict}
					<label
						class="flex items-start gap-2.5 rounded-xl border border-red-500/20 bg-red-500/5 p-3 cursor-pointer text-xs text-red-900 dark:text-red-200"
					>
						<input
							type="checkbox"
							bind:checked={forceDelete}
							class="mt-0.5 h-4 w-4 rounded border-red-300 text-red-600 focus:ring-red-500 dark:border-red-700 dark:bg-zinc-800"
						/>
						<span class="font-medium">
							{t('equipment.delete_modal.force_delete_checkbox')}
						</span>
					</label>
				{/if}
			</div>

			<!-- Footer -->
			<div
				class="mt-6 flex items-center justify-end gap-3 border-t border-zinc-200 pt-4 dark:border-zinc-800"
			>
				<button
					type="button"
					data-testid="cancel-delete-btn"
					onclick={onClose}
					disabled={isDeleting}
					class="rounded-xl border border-zinc-300 bg-white px-4 py-2 text-xs font-semibold text-zinc-700 shadow-sm transition hover:bg-zinc-50 disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-700"
				>
					{t('common.cancel')}
				</button>
				<button
					type="button"
					onclick={handleConfirmDelete}
					disabled={isDeleting || ((activeBatches.length > 0 || isConflict) && !forceDelete)}
					class="inline-flex items-center gap-1.5 rounded-xl bg-red-600 px-4 py-2 text-xs font-semibold text-white shadow-sm transition hover:bg-red-500 disabled:opacity-50"
				>
					{#if isDeleting}
						<Loader2 class="h-4 w-4 animate-spin" />
						<span>{t('equipment.delete_modal.deleting')}</span>
					{:else if forceDelete}
						<Trash2 class="h-4 w-4" />
						<span>{t('equipment.delete_modal.force_confirm_button')}</span>
					{:else}
						<Trash2 class="h-4 w-4" />
						<span>{t('equipment.delete_modal.confirm_button')}</span>
					{/if}
				</button>
			</div>
		</div>
	</div>
{/if}
```

---

### Proposal 4: `src/BrewYou.Web/src/routes/batches/[id]/+page.svelte`
**Changes**:
1. Remove lines 293-299 (`if (equipmentList.length > 0) return { id: equipmentList[0].id, ... }`).
2. Update lines 1754 and 2010 to pass `currentStageEquipmentName` directly.

```svelte
<<<< BEFORE (lines 293-302)
		// 5. Fallback: first equipment in list if any
		if (equipmentList.length > 0) {
			return {
				id: equipmentList[0].id,
				name: equipmentList[0].name
			};
		}

		return null;
	});
==== AFTER
		// 5. Explicit unassigned fallback (no arbitrary equipmentList[0] assignment)
		return null;
	});
>>>>
```

```svelte
<<<< BEFORE (line 1754)
						equipmentName={currentStageEquipmentName ?? batch.boilerName}
==== AFTER
						equipmentName={currentStageEquipmentName}
>>>>
```

```svelte
<<<< BEFORE (line 2010)
							equipmentName={currentStageEquipmentName ?? batch.boilerName}
==== AFTER
							equipmentName={currentStageEquipmentName}
>>>>
```

---

### Proposal 5: `src/BrewYou.Web/src/lib/components/batches/StepTemperatureChart.svelte`
**Change**: Render unassigned placeholder `t('batches.workspace.no_vessel_assigned')` when `equipmentName` is falsy.

```svelte
<<<< BEFORE (lines 209-213)
				{#if equipmentName}
					<p class="text-xs text-zinc-500 dark:text-zinc-400">
						{equipmentName}
					</p>
				{/if}
==== AFTER
				{#if equipmentName}
					<p class="text-xs text-zinc-500 dark:text-zinc-400">
						{equipmentName}
					</p>
				{:else}
					<p class="text-xs italic text-zinc-400 dark:text-zinc-500">
						{t('batches.workspace.no_vessel_assigned')}
					</p>
				{/if}
>>>>
```

---

### Proposal 6: `src/BrewYou.Web/src/lib/components/batches/LogStepTemperatureModal.svelte`
**Change**: Remove arbitrary fallback to `equipmentList[0]` in resolved equipment name and default selection.

```svelte
<<<< BEFORE (lines 52-57)
	let resolvedEquipmentName = $derived(
		resolvedEquipment?.name ??
			equipmentName ??
			(defaultEquipmentId ? equipmentList.find((e) => e.id === defaultEquipmentId)?.name : null) ??
			(equipmentList.length > 0 ? equipmentList[0].name : '')
	);
==== AFTER
	let resolvedEquipmentName = $derived(
		resolvedEquipment?.name ??
			equipmentName ??
			(defaultEquipmentId ? equipmentList.find((e) => e.id === defaultEquipmentId)?.name : null) ??
			null
	);
>>>>
```

```svelte
<<<< BEFORE (lines 69-70)
			selectedEquipmentId =
				defaultEquipmentId ?? (equipmentList.length > 0 ? equipmentList[0].id : '');
==== AFTER
			selectedEquipmentId = defaultEquipmentId ?? '';
>>>>
```

```svelte
<<<< BEFORE (line 260)
							{:else if equipmentList.length > 0}
								<select
									id="equipment-select"
									bind:value={selectedEquipmentId}
									class="mt-1 w-full rounded-lg border border-zinc-200 bg-white px-2 py-1 text-xs text-zinc-900 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-100"
								>
									{#each equipmentList as eq (eq.id)}
										<option value={eq.id}>{eq.name}</option>
									{/each}
								</select>
==== AFTER
							{:else if equipmentList.length > 0}
								<select
									id="equipment-select"
									bind:value={selectedEquipmentId}
									class="mt-1 w-full rounded-lg border border-zinc-200 bg-white px-2 py-1 text-xs text-zinc-900 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-100"
								>
									<option value="">{t('batches.workspace.no_vessel_assigned')}</option>
									{#each equipmentList as eq (eq.id)}
										<option value={eq.id}>{eq.name}</option>
									{/each}
								</select>
>>>>
```

---

### Proposal 7: `src/BrewYou.Web/src/routes/inventory/equipment/+page.svelte`
**Change**: Clear `assignedKegId` and `assignedKeg` on Tap cards in `equipmentList` when a Keg is deleted.

```svelte
<<<< BEFORE (lines 337-339)
	function handleDeleted(equipmentId: string) {
		equipmentList = equipmentList.filter((e) => e.id !== equipmentId);
	}
==== AFTER
	function handleDeleted(equipmentId: string) {
		equipmentList = equipmentList
			.filter((e) => e.id !== equipmentId)
			.map((e) => {
				if (e.assignedKegId === equipmentId || e.assignedKeg?.id === equipmentId) {
					return {
						...e,
						assignedKegId: null,
						assignedKeg: null
					};
				}
				return e;
			});
	}
>>>>
```

---

### Proposal 8: `src/BrewYou.Web/src/lib/components/batches/StageMeasurementsCard.svelte`
**Changes**:
1. Safely handle deleted packaging vessel in `<select>` with a disabled option.
2. Only send `packagingVesselId` in `handleSubmit` if changed.
3. Show clean placeholder in view mode if packaging vessel was deleted.

```svelte
<<<< BEFORE (lines 143-148)
			} else if (stage === 'Package') {
				req.measuredFg = draftMeasuredFg;
				req.measuredPackagedVolumeLiters = draftPackagedVol;
				req.packagingVesselId = draftPackagingVesselId
					? draftPackagingVesselId
					: batch.packagingVesselId
						? '00000000-0000-0000-0000-000000000000'
						: null;
			}
==== AFTER
			} else if (stage === 'Package') {
				req.measuredFg = draftMeasuredFg;
				req.measuredPackagedVolumeLiters = draftPackagedVol;
				if (draftPackagingVesselId !== (batch.packagingVesselId ?? '')) {
					req.packagingVesselId = draftPackagingVesselId
						? draftPackagingVesselId
						: '00000000-0000-0000-0000-000000000000';
				}
			}
>>>>
```

```svelte
<<<< BEFORE (lines 416-418)
					<div class="mt-1 text-xs font-medium text-zinc-900 dark:text-white">
						{batch.packagingVesselName || t('batches.workspace.bottled_cellared_option')}
					</div>
==== AFTER
					<div class="mt-1 text-xs font-medium text-zinc-900 dark:text-white">
						{#if batch.packagingVesselId && !equipmentList.some((e) => e.id === batch.packagingVesselId)}
							<span class="text-zinc-500 italic">
								{batch.packagingVesselName ? `${batch.packagingVesselName} (${t('common.deleted')})` : t('batches.workspace.deleted_vessel')}
							</span>
						{:else}
							{batch.packagingVesselName || t('batches.workspace.bottled_cellared_option')}
						{/if}
					</div>
>>>>
```

```svelte
<<<< BEFORE (lines 634-644)
						<select
							id="edit-packaging-vessel"
							bind:value={draftPackagingVesselId}
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						>
							<option value="">{t('batches.workspace.bottled_cellared_option')}</option>
							{#each equipmentList.filter((e) => e.type === 'Keg') as keg}
								<option value={keg.id}>{keg.name} ({keg.capacity}L)</option>
							{/each}
						</select>
==== AFTER
						<select
							id="edit-packaging-vessel"
							bind:value={draftPackagingVesselId}
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						>
							<option value="">{t('batches.workspace.bottled_cellared_option')}</option>
							{#if batch.packagingVesselId && !equipmentList.some((e) => e.id === batch.packagingVesselId)}
								<option value={batch.packagingVesselId} disabled>
									{batch.packagingVesselName ? `${batch.packagingVesselName} (${t('common.deleted')})` : t('batches.workspace.deleted_vessel')}
								</option>
							{/if}
							{#each equipmentList.filter((e) => e.type === 'Keg') as keg}
								<option value={keg.id}>{keg.name} ({keg.capacity}L)</option>
							{/each}
						</select>
>>>>
```

---

### Proposal 9: Localization Keys Parity (`en.json` and `sv.json`)

#### In `src/BrewYou.Web/src/lib/i18n/locales/en.json`:
1. Under `"common"`:
```json
		"deleted": "Deleted",
		"unavailable": "Unavailable",
```
2. Under `"batches"` -> `"workspace"`:
```json
			"no_vessel_assigned": "No vessel assigned",
			"deleted_vessel": "Deleted vessel",
```
3. Under `"equipment"` -> `"delete_modal"`:
```json
			"conflict_error": "Cannot delete equipment while it is in use by active batches. Please decouple or complete the active batches, or enable force delete.",
			"force_delete_checkbox": "Force delete anyway (decouples from all active batches and sensor assignments)",
			"force_confirm_button": "Force Delete Equipment",
			"role_boiler": "Boiler",
			"role_fermenter": "Fermenter",
			"role_hlt": "Hot Liquor Tank (HLT)",
			"role_mashtun": "Mash Tun",
			"role_packaging": "Packaging Vessel",
			"role_sensor": "Sensor / Probe",
```

#### In `src/BrewYou.Web/src/lib/i18n/locales/sv.json`:
1. Under `"common"`:
```json
		"deleted": "Borttagen",
		"unavailable": "Otillgänglig",
```
2. Under `"batches"` -> `"workspace"`:
```json
			"no_vessel_assigned": "Inget kärl tilldelat",
			"deleted_vessel": "Borttaget kärl",
```
3. Under `"equipment"` -> `"delete_modal"`:
```json
			"conflict_error": "Kan inte ta bort utrustning som används av aktiva batcher. Koppla bort eller slutför de aktiva batcherna, eller aktivera tvångsradering.",
			"force_delete_checkbox": "Tvångsradera ändå (kopplar från alla aktiva batcher och sensoruppdrag)",
			"force_confirm_button": "Tvångsradera utrustning",
			"role_boiler": "Kokkärl",
			"role_fermenter": "Jäskärl",
			"role_hlt": "Varmvattentank (HLT)",
			"role_mashtun": "Mäskkärl",
			"role_packaging": "Tappningskärl",
			"role_sensor": "Sensor / Sond",
```

---

## 5. Verification Method

To independently verify these frontend implementations:

### Test Commands
1. **Frontend Unit & Component Tests**:
   ```powershell
   cd c:\Users\jocke\AgentWorkspaces\BrewYou\src\BrewYou.Web
   npm run test:unit
   ```
2. **TypeScript & Svelte Diagnostics**:
   ```powershell
   cd c:\Users\jocke\AgentWorkspaces\BrewYou\src\BrewYou.Web
   npm run check
   ```
3. **i18n Key Parity Check**:
   ```powershell
   npm run test:unit -- lib/i18n/i18n.test.ts
   ```
4. **Backend Integration (Full Stack Verification)**:
   ```powershell
   cd c:\Users\jocke\AgentWorkspaces\BrewYou
   dotnet test tests/BrewYou.ApiService.Tests --filter FullyQualifiedName~Equipment
   ```

### Files to Inspect
- `src/BrewYou.Web/src/lib/components/inventory/DeleteEquipmentModal.svelte`
- `src/BrewYou.Web/src/routes/batches/[id]/+page.svelte`
- `src/BrewYou.Web/src/routes/inventory/equipment/+page.svelte`
- `src/BrewYou.Web/src/lib/components/batches/StageMeasurementsCard.svelte`
- `src/BrewYou.Web/src/lib/components/batches/StepTemperatureChart.svelte`
- `src/BrewYou.Web/src/lib/components/batches/LogStepTemperatureModal.svelte`
- `src/BrewYou.Web/src/lib/i18n/locales/en.json`
- `src/BrewYou.Web/src/lib/i18n/locales/sv.json`
- `src/BrewYou.Web/src/lib/types/api.ts`
- `src/BrewYou.Web/src/lib/api/client.ts`

### Invalidation Conditions
- If the batch workspace renders an arbitrary inventory equipment name (e.g. Grain Mill) when a batch vessel is deleted/null.
- If deleting a Keg leaves any Tap card with a non-null `assignedKegId` in client state.
- If `DeleteEquipmentModal` fails to display active batch usages or fails to surface a structured conflict error when the backend returns 409 Conflict.
- If `StageMeasurementsCard` crashes or writes `Guid.Empty` when saving packaging measurements where the vessel was previously deleted.
- If `en.json` and `sv.json` diverge in keys.
