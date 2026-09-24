<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { dashboard } from '$lib/stores/dashboard.svelte';
	import type { BatchSummaryDto, BrewStage } from '$lib/types/api';
	import { X, Sparkles, Loader2, ArrowRight } from '@lucide/svelte';

	interface Props {
		isOpen: boolean;
		batch: BatchSummaryDto | null;
		onClose: () => void;
	}

	let { isOpen = $bindable(false), batch, onClose }: Props = $props();

	let isSubmitting = $state(false);
	let errorMessage = $state<string | null>(null);

	const stageOrder: BrewStage[] = ['Mash', 'Boil', 'Ferment', 'Condition', 'Package'];

	const nextStage = $derived.by<BrewStage | null>(() => {
		if (!batch) return null;
		const currentIdx = stageOrder.indexOf(batch.currentStage);
		if (currentIdx >= 0 && currentIdx < stageOrder.length - 1) {
			return stageOrder[currentIdx + 1];
		}
		return null;
	});

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && isOpen) {
			onClose();
		}
	}

	async function handleAdvance() {
		if (!batch || !nextStage) return;

		isSubmitting = true;
		errorMessage = null;

		try {
			await dashboard.advanceStage(batch.id, nextStage);
			onClose();
		} catch (err: unknown) {
			errorMessage = (err as Error).message || 'Failed to advance stage';
		} finally {
			isSubmitting = false;
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if isOpen && batch && nextStage}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4"
		role="dialog"
		aria-modal="true"
		aria-labelledby="advance-modal-title"
	>
		<!-- Backdrop -->
		<button
			type="button"
			class="fixed inset-0 bg-black/60 backdrop-blur-sm transition-opacity"
			onclick={onClose}
			tabindex="-1"
			aria-label={t('common.close_dialog')}
		></button>

		<!-- Modal Dialog Box -->
		<div
			class="glass-panel relative z-10 w-full max-w-md overflow-hidden rounded-3xl border border-zinc-200/80 p-6 shadow-2xl sm:p-7 dark:border-white/10"
		>
			<div
				class="flex items-center justify-between border-b border-zinc-200/80 pb-4 dark:border-white/10"
			>
				<div class="flex items-center gap-2.5">
					<div
						class="flex h-9 w-9 items-center justify-center rounded-xl bg-amber-500/15 text-amber-500 dark:text-amber-400"
					>
						<Sparkles class="h-5 w-5" />
					</div>
					<h3 id="advance-modal-title" class="text-lg font-bold text-zinc-900 dark:text-white">
						{t('dashboard.advance_stage_modal_title')}
					</h3>
				</div>
				<button
					type="button"
					onclick={onClose}
					class="rounded-xl p-1.5 text-zinc-600 transition hover:bg-zinc-100 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800 dark:hover:text-white"
					aria-label={t('common.close_dialog')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			{#if errorMessage}
				<div
					class="mt-4 rounded-xl border border-rose-500/30 bg-rose-500/10 p-3 text-xs text-rose-600 dark:text-rose-400"
				>
					{errorMessage}
				</div>
			{/if}

			<div class="my-5 space-y-4">
				<!-- Stage Transition Visual -->
				<div
					class="flex items-center justify-center gap-3 rounded-2xl bg-zinc-100/70 p-4 dark:bg-zinc-900/50"
				>
					<span
						class="rounded-xl border border-zinc-300 bg-white px-3 py-1.5 font-mono text-xs font-bold text-zinc-700 shadow-sm dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
					>
						{batch.currentStage}
					</span>
					<ArrowRight class="h-4 w-4 text-amber-500" />
					<span
						class="rounded-xl border border-amber-500/40 bg-amber-500/15 px-3 py-1.5 font-mono text-xs font-bold text-amber-700 shadow-sm dark:text-amber-300"
					>
						{nextStage}
					</span>
				</div>

				<p class="text-center text-xs text-zinc-600 sm:text-sm dark:text-zinc-400">
					{t('dashboard.advance_stage_confirm', {
						batch: batch.name,
						current: batch.currentStage,
						next: nextStage
					})}
				</p>
			</div>

			<!-- Modal Actions -->
			<div
				class="flex items-center justify-end gap-3 border-t border-zinc-200/80 pt-2 dark:border-white/10"
			>
				<button
					type="button"
					onclick={onClose}
					class="rounded-xl border border-zinc-200 bg-white px-4 py-2 text-xs font-semibold text-zinc-800 transition hover:bg-zinc-100 dark:border-white/10 dark:bg-zinc-800 dark:text-zinc-200 dark:hover:bg-zinc-700"
				>
					{t('common.cancel')}
				</button>

				<button
					type="button"
					onclick={handleAdvance}
					disabled={isSubmitting}
					class="inline-flex items-center gap-1.5 rounded-xl border border-amber-500/50 bg-amber-500 px-4 py-2 text-xs font-bold text-zinc-950 transition hover:bg-amber-400 disabled:opacity-40"
				>
					{#if isSubmitting}
						<Loader2 class="h-3.5 w-3.5 animate-spin" />
						<span>{t('dashboard.advancing')}</span>
					{:else}
						<Sparkles class="h-3.5 w-3.5" />
						<span>{t('dashboard.advance_stage_btn')}</span>
					{/if}
				</button>
			</div>
		</div>
	</div>
{/if}
