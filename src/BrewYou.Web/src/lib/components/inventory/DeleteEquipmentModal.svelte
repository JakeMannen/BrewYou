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
			if (
				err instanceof ApiClientError &&
				(err.status === 409 || err.code === 'EQUIPMENT_IN_USE')
			) {
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
							<div class="w-full space-y-2">
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
						<ShieldAlert class="mt-0.5 h-4 w-4 shrink-0" />
						<div>{errorMessage}</div>
					</div>
				{/if}

				<!-- Force Delete Option -->
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
