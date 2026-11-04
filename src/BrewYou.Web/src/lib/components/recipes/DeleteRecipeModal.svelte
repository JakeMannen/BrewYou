<script lang="ts">
	import { api, ApiClientError } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import type { RecipeSummaryDto, RecipeActiveBatchDto } from '$lib/types/api';
	import {
		AlertTriangle,
		Loader2,
		X,
		Trash2,
		Info,
		ExternalLink,
		ShieldAlert
	} from '@lucide/svelte';

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
