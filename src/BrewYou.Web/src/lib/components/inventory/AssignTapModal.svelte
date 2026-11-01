<script lang="ts">
	import { api, ApiClientError } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import type { EquipmentDto, BatchSummaryDto } from '$lib/types/api';
	import { X, Beer, Loader2, Check, Search, Unlink } from '@lucide/svelte';

	interface Props {
		open: boolean;
		tap: EquipmentDto | null;
		onClose: () => void;
		onSaved: (updated: EquipmentDto) => void;
	}

	let { open, tap, onClose, onSaved }: Props = $props();

	let batches = $state<BatchSummaryDto[]>([]);
	let loading = $state(true);
	let saving = $state(false);
	let unassigning = $state(false);
	let errorMessage = $state<string | null>(null);
	let searchQuery = $state('');
	let selectedBatchId = $state<string | null>(null);

	$effect(() => {
		if (open && tap) {
			selectedBatchId = tap.assignedBatch?.id ?? null;
			errorMessage = null;
			searchQuery = '';
			loadBatches();
		}
	});

	async function loadBatches() {
		loading = true;
		errorMessage = null;
		try {
			// Fetch batches - we'll filter for servable batches (Condition or Package stage)
			const all = await api.batches.list({ limit: 100 });
			batches = all.filter((b) => b.currentStage === 'Condition' || b.currentStage === 'Package');
		} catch (err) {
			if (err instanceof ApiClientError) {
				errorMessage = err.message;
			} else {
				errorMessage = err instanceof Error ? err.message : t('common.error_generic');
			}
		} finally {
			loading = false;
		}
	}

	let filteredBatches = $derived(
		batches.filter((b) => {
			if (!searchQuery.trim()) return true;
			const q = searchQuery.toLowerCase().trim();
			return (
				b.batchCode.toLowerCase().includes(q) ||
				b.name.toLowerCase().includes(q) ||
				b.beerStyle.toLowerCase().includes(q)
			);
		})
	);

	async function handleAssign() {
		if (!tap || !selectedBatchId) return;
		saving = true;
		errorMessage = null;
		try {
			const updated = await api.equipment.assignTap(tap.id, selectedBatchId);
			onSaved(updated);
			onClose();
		} catch (err) {
			if (err instanceof ApiClientError) {
				errorMessage = err.message;
			} else {
				errorMessage = err instanceof Error ? err.message : t('common.error_generic');
			}
		} finally {
			saving = false;
		}
	}

	async function handleUnassign() {
		if (!tap) return;
		unassigning = true;
		errorMessage = null;
		try {
			const updated = await api.equipment.unassignTap(tap.id);
			onSaved(updated);
			onClose();
		} catch (err) {
			if (err instanceof ApiClientError) {
				errorMessage = err.message;
			} else {
				errorMessage = err instanceof Error ? err.message : t('common.error_generic');
			}
		} finally {
			unassigning = false;
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && open) {
			onClose();
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open && tap}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4 sm:p-6"
		role="dialog"
		aria-modal="true"
		aria-labelledby="assign-tap-title"
	>
		<!-- Backdrop -->
		<button
			type="button"
			class="fixed inset-0 bg-black/70 backdrop-blur-sm transition-opacity"
			onclick={onClose}
			aria-label={t('common.cancel')}
		></button>

		<!-- Modal Container -->
		<div
			class="glass-panel relative flex max-h-[85vh] w-full max-w-lg flex-col overflow-hidden rounded-3xl border border-zinc-200/80 shadow-2xl dark:border-white/[0.08]"
		>
			<!-- Header -->
			<div
				class="flex items-center justify-between border-b border-zinc-200/80 px-6 py-4 dark:border-white/[0.08]"
			>
				<div class="flex items-center gap-3">
					<div
						class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-500"
					>
						<Beer class="h-5 w-5" />
					</div>
					<div>
						<h2
							id="assign-tap-title"
							class="font-editorial text-lg font-bold text-zinc-900 dark:text-white"
						>
							{t('taps.assign_batch_title', { tapName: tap.name })}
						</h2>
						<p class="text-xs text-zinc-500 dark:text-zinc-400">
							{t('taps.assign_batch_desc')}
						</p>
					</div>
				</div>

				<button
					type="button"
					onclick={onClose}
					class="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('common.cancel')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<!-- Search -->
			<div class="border-b border-zinc-200/80 p-4 dark:border-white/[0.08]">
				<div class="relative">
					<Search class="absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-zinc-400" />
					<input
						type="text"
						bind:value={searchQuery}
						placeholder={t('taps.search_batches')}
						class="h-9 w-full rounded-xl border border-zinc-300 bg-white pr-3 pl-9 text-xs text-zinc-900 placeholder-zinc-400 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100 dark:placeholder-zinc-600"
					/>
				</div>
			</div>

			<!-- Batch Selection List -->
			<div class="flex-1 space-y-2 overflow-y-auto p-4">
				{#if errorMessage}
					<div
						class="rounded-xl border border-red-500/20 bg-red-500/10 p-3 text-xs text-red-600 dark:text-red-400"
					>
						{errorMessage}
					</div>
				{/if}

				{#if loading}
					<div class="flex h-36 items-center justify-center">
						<Loader2 class="h-6 w-6 animate-spin text-amber-500" />
					</div>
				{:else if filteredBatches.length === 0}
					<div class="flex flex-col items-center justify-center py-10 text-center">
						<Beer class="h-8 w-8 text-zinc-400 opacity-60" />
						<p class="mt-2 text-xs font-semibold text-zinc-600 dark:text-zinc-400">
							{t('taps.no_servable_batches')}
						</p>
						<p class="mt-0.5 text-[11px] text-zinc-400 dark:text-zinc-500">
							{t('taps.servable_batches_hint')}
						</p>
					</div>
				{:else}
					{#each filteredBatches as batch (batch.id)}
						{@const isSelected = selectedBatchId === batch.id}
						<button
							type="button"
							onclick={() => (selectedBatchId = batch.id)}
							class="flex w-full items-center justify-between rounded-2xl border p-3.5 text-left transition-all {isSelected
								? 'border-amber-500 bg-amber-500/10 shadow-sm'
								: 'border-zinc-200/80 bg-white hover:border-zinc-300 dark:border-zinc-800 dark:bg-zinc-900/50 dark:hover:border-zinc-700'}"
						>
							<div class="min-w-0 flex-1">
								<div class="flex items-center gap-2">
									<span class="font-mono text-xs font-bold text-amber-600 dark:text-amber-400">
										{batch.batchCode}
									</span>
									<span
										class="rounded bg-zinc-100 px-1.5 py-0.5 text-[10px] font-semibold text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400"
									>
										{batch.currentStage}
									</span>
								</div>
								<h4 class="mt-0.5 truncate text-sm font-bold text-zinc-900 dark:text-white">
									{batch.name}
								</h4>
								<div
									class="mt-1 flex items-center gap-2 text-[11px] text-zinc-500 dark:text-zinc-400"
								>
									<span>{batch.beerStyle}</span>
									{#if batch.alcoholByVolume != null}
										<span>•</span>
										<span class="font-mono font-bold text-emerald-600 dark:text-emerald-400">
											{batch.alcoholByVolume}% ABV
										</span>
									{/if}
								</div>
							</div>

							<div
								class="flex h-6 w-6 shrink-0 items-center justify-center rounded-full border transition-colors {isSelected
									? 'border-amber-500 bg-amber-500 text-zinc-950'
									: 'border-zinc-300 dark:border-zinc-700'}"
							>
								{#if isSelected}
									<Check class="h-3.5 w-3.5 stroke-[3]" />
								{/if}
							</div>
						</button>
					{/each}
				{/if}
			</div>

			<!-- Footer -->
			<div
				class="flex items-center justify-between border-t border-zinc-200/80 px-6 py-4 dark:border-white/[0.08]"
			>
				<div>
					{#if tap.assignedBatch}
						<button
							type="button"
							onclick={handleUnassign}
							disabled={unassigning || saving}
							class="flex items-center gap-1.5 text-xs font-semibold text-red-600 hover:text-red-500 dark:text-red-400"
						>
							{#if unassigning}
								<Loader2 class="h-3.5 w-3.5 animate-spin" />
							{:else}
								<Unlink class="h-3.5 w-3.5" />
							{/if}
							<span>{t('taps.unassign_tap_btn')}</span>
						</button>
					{/if}
				</div>

				<div class="flex items-center gap-3">
					<button
						type="button"
						onclick={onClose}
						disabled={saving || unassigning}
						class="rounded-xl border border-zinc-200/80 bg-white px-4 py-2 text-xs font-semibold text-zinc-700 transition-colors hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800"
					>
						{t('common.cancel')}
					</button>

					<button
						type="button"
						onclick={handleAssign}
						disabled={saving ||
							unassigning ||
							!selectedBatchId ||
							selectedBatchId === tap.assignedBatch?.id}
						class="flex items-center gap-2 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-5 py-2 text-xs font-bold text-zinc-950 shadow-md transition-all hover:from-amber-400 hover:to-amber-500 disabled:opacity-50"
					>
						{#if saving}
							<Loader2 class="h-4 w-4 animate-spin" />
							<span>{t('common.saving')}</span>
						{:else}
							<Check class="h-4 w-4" />
							<span>{t('taps.confirm_assignment')}</span>
						{/if}
					</button>
				</div>
			</div>
		</div>
	</div>
{/if}
