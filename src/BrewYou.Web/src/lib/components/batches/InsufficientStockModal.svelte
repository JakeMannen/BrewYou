<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import type { IngredientShortageDto } from '$lib/types/api';
	import { AlertTriangle, X, CheckCircle2 } from '@lucide/svelte';
	import { formatNumber } from '$lib/utils/formatNumber';

	interface Props {
		open: boolean;
		shortages: IngredientShortageDto[];
		onCancel: () => void;
		onContinue: () => void;
	}

	let { open, shortages = [], onCancel, onContinue }: Props = $props();

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && open) {
			onCancel();
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4"
		role="dialog"
		aria-modal="true"
		aria-labelledby="stock-warning-title"
	>
		<!-- Backdrop -->
		<button
			type="button"
			class="fixed inset-0 bg-black/60 backdrop-blur-sm transition-opacity"
			onclick={onCancel}
			aria-label={t('common.close_dialog')}
		></button>

		<!-- Dialog Panel -->
		<div
			class="relative w-full max-w-lg rounded-2xl border border-amber-500/30 bg-white p-6 shadow-2xl transition-all dark:border-amber-500/20 dark:bg-zinc-900"
		>
			<div class="flex items-start gap-4">
				<div
					class="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-500 dark:border-amber-500/20 dark:bg-amber-500/10"
				>
					<AlertTriangle class="h-6 w-6 text-amber-500" />
				</div>
				<div class="flex-1">
					<h3
						id="stock-warning-title"
						class="text-lg font-bold tracking-tight text-zinc-900 dark:text-white"
					>
						{t('batches.wizard.stock_warning.title')}
					</h3>
					<p class="mt-1 text-xs leading-relaxed text-zinc-600 dark:text-zinc-400">
						{t('batches.wizard.stock_warning.desc')}
					</p>
				</div>
				<button
					type="button"
					onclick={onCancel}
					class="rounded-lg p-1.5 text-zinc-400 hover:bg-zinc-100 hover:text-zinc-600 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('common.close_dialog')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<!-- Shortages Table -->
			<div
				class="mt-5 max-h-60 overflow-y-auto rounded-xl border border-zinc-200 bg-zinc-50/50 p-1 dark:border-zinc-800 dark:bg-zinc-950/40"
			>
				<table class="w-full text-left text-xs">
					<thead>
						<tr
							class="border-b border-zinc-200 text-[11px] font-semibold text-zinc-500 dark:border-zinc-800 dark:text-zinc-400"
						>
							<th class="px-3 py-2">{t('batches.wizard.stock_warning.ingredient')}</th>
							<th class="px-2 py-2 text-right">{t('batches.wizard.stock_warning.required')}</th>
							<th class="px-2 py-2 text-right">{t('batches.wizard.stock_warning.in_stock')}</th>
							<th class="px-3 py-2 text-right text-rose-600 dark:text-rose-400"
								>{t('batches.wizard.stock_warning.shortage')}</th
							>
						</tr>
					</thead>
					<tbody class="divide-y divide-zinc-200/60 dark:divide-zinc-800/60">
						{#each shortages as item, idx (`${item.ingredientId}-${idx}`)}
							<tr>
								<td class="px-3 py-2 font-medium text-zinc-900 dark:text-zinc-100">{item.name}</td>
								<td class="px-2 py-2 text-right font-mono text-zinc-600 dark:text-zinc-400">
									{formatNumber(item.requiredAmount, 2)}
									{item.unit}
								</td>
								<td class="px-2 py-2 text-right font-mono text-zinc-600 dark:text-zinc-400">
									{formatNumber(item.stockAmount, 2)}
									{item.unit}
								</td>
								<td
									class="px-3 py-2 text-right font-mono font-bold text-rose-600 dark:text-rose-400"
								>
									-{formatNumber(item.deficit, 2)}
									{item.unit}
								</td>
							</tr>
						{/each}
					</tbody>
				</table>
			</div>

			<!-- Actions -->
			<div class="mt-6 flex items-center justify-end gap-3">
				<button
					type="button"
					onclick={onCancel}
					class="rounded-xl border border-zinc-200 bg-white px-4 py-2 text-xs font-semibold text-zinc-700 transition-colors hover:bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-700"
				>
					{t('batches.wizard.stock_warning.cancel')}
				</button>
				<button
					type="button"
					onclick={onContinue}
					class="flex items-center gap-1.5 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-4 py-2 text-xs font-bold text-zinc-950 shadow-sm transition-all hover:from-amber-400 hover:to-amber-500 active:scale-98"
				>
					<CheckCircle2 class="h-4 w-4" />
					<span>{t('batches.wizard.stock_warning.continue_anyway')}</span>
				</button>
			</div>
		</div>
	</div>
{/if}
