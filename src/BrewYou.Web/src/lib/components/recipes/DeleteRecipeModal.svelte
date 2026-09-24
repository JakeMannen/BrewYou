<script lang="ts">
	import { api } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import type { RecipeSummaryDto } from '$lib/types/api';
	import { AlertTriangle, Loader2, X, Trash2, Info } from '@lucide/svelte';

	interface Props {
		open: boolean;
		recipe: RecipeSummaryDto | null;
		onClose: () => void;
		onDeleted: (recipeId: string) => void;
	}

	let { open, recipe, onClose, onDeleted }: Props = $props();

	let isDeleting = $state(false);
	let errorMessage = $state<string | null>(null);

	$effect(() => {
		if (open && recipe) {
			errorMessage = null;
			isDeleting = false;
		}
	});

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
			class="relative z-10 max-h-[90vh] w-full max-w-md overflow-y-auto rounded-2xl border border-zinc-200 bg-white p-6 shadow-2xl transition-all dark:border-zinc-800 dark:bg-zinc-950"
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

				<!-- Batch Preservation Notice -->
				<div
					class="flex items-start gap-3 rounded-xl border border-amber-500/20 bg-amber-500/5 p-3.5 text-xs text-amber-800 dark:border-amber-500/15 dark:text-amber-300"
				>
					<Info class="mt-0.5 h-4 w-4 shrink-0 text-amber-600 dark:text-amber-400" />
					<p class="leading-relaxed">
						{t('recipes.delete_modal.batches_notice')}
					</p>
				</div>

				{#if errorMessage}
					<div
						class="flex items-start gap-2 rounded-xl border border-red-200 bg-red-50 p-3 text-xs text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
					>
						<AlertTriangle class="h-4 w-4 shrink-0 text-red-500" />
						<span>{errorMessage}</span>
					</div>
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
					disabled={isDeleting}
					class="flex min-h-[42px] items-center gap-2 rounded-xl bg-red-600 px-4 py-2.5 text-sm font-bold text-white shadow-sm transition-all hover:bg-red-500 disabled:opacity-50"
				>
					{#if isDeleting}
						<Loader2 class="h-4 w-4 animate-spin" />
						<span>{t('recipes.delete_modal.deleting')}</span>
					{:else}
						<Trash2 class="h-4 w-4" />
						<span>{t('recipes.delete_modal.confirm')}</span>
					{/if}
				</button>
			</div>
		</div>
	</div>
{/if}
