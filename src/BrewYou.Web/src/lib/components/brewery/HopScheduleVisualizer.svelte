<script module lang="ts">
	import type { IngredientUsage } from '$lib/types/api';

	export interface HopVisualItem {
		id: string;
		ingredientId: string;
		amount: number;
		durationMinutes: number;
		usage: IngredientUsage;
		notes?: string;
		form?: string | null;
	}

	export function getHopPhase(
		usage: IngredientUsage,
		duration: number
	): {
		key: string;
		barColor: string;
		badgeClass: string;
	} {
		if (usage === 'Boil' && duration >= 45) {
			return {
				key: 'formulator.phase_bittering',
				barColor: '#ea580c',
				badgeClass: 'bg-orange-600/15 text-orange-700 dark:text-orange-300 border-orange-600/30'
			};
		}
		if (usage === 'Boil' && duration >= 20) {
			return {
				key: 'formulator.phase_flavor',
				barColor: '#f59e0b',
				badgeClass: 'bg-amber-500/15 text-amber-700 dark:text-amber-300 border-amber-500/30'
			};
		}
		if (usage === 'Boil') {
			return {
				key: 'formulator.phase_aroma',
				barColor: '#10b981',
				badgeClass: 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-300 border-emerald-500/30'
			};
		}
		if (usage === 'Whirlpool') {
			return {
				key: 'formulator.phase_whirlpool',
				barColor: '#059669',
				badgeClass: 'bg-teal-500/15 text-teal-700 dark:text-teal-300 border-teal-500/30'
			};
		}
		if (usage === 'DryHop') {
			return {
				key: 'formulator.phase_dry_hop',
				barColor: '#6366f1',
				badgeClass: 'bg-indigo-500/15 text-indigo-700 dark:text-indigo-300 border-indigo-500/30'
			};
		}
		return {
			key: 'formulator.phase_aroma',
			barColor: '#84cc16',
			badgeClass: 'bg-lime-500/15 text-lime-700 dark:text-lime-300 border-lime-500/30'
		};
	}
</script>

<script lang="ts">
	import type { IngredientDto } from '$lib/types/api';
	import { t } from '$lib/i18n/index.svelte';
	import { getLocalizedIngredientName } from '$lib/i18n/ingredients';
	import { tinsethIbu } from '$lib/calculators/brewing';
	import { formatNumber } from '$lib/utils/formatNumber';
	import { Flower2, Sparkles, Clock } from '@lucide/svelte';

	interface Props {
		hops: HopVisualItem[];
		catalogIngredients: IngredientDto[];
		estimatedOg: number;
		batchSizeLiters: number;
		class?: string;
	}

	let {
		hops,
		catalogIngredients,
		estimatedOg,
		batchSizeLiters,
		class: className = ''
	}: Props = $props();

	// Compute sorted hops and individual IBU contributions
	const processedHops = $derived.by(() => {
		const list = hops.map((h) => {
			const ing = catalogIngredients.find((i) => i.id === h.ingredientId);
			const alpha = ing?.alphaAcidPercent ?? 5.0;
			const weightGrams = Number(h.amount) || 0;
			const time = Number(h.durationMinutes) || 0;
			const ibu =
				h.usage === 'DryHop' || h.usage === 'Secondary' || h.usage === 'Bottling'
					? 0
					: tinsethIbu(weightGrams, alpha, time, estimatedOg, batchSizeLiters);
			const phase = getHopPhase(h.usage, time);
			return {
				...h,
				ing,
				alpha,
				ibu,
				phase
			};
		});

		// Sort by boil timing: descending boil minutes, then Whirlpool, then DryHop
		return list.sort((a, b) => {
			if (a.usage === 'DryHop' && b.usage !== 'DryHop') return 1;
			if (b.usage === 'DryHop' && a.usage !== 'DryHop') return -1;
			return (b.durationMinutes || 0) - (a.durationMinutes || 0);
		});
	});

	const totalIbu = $derived(processedHops.reduce((sum, h) => sum + h.ibu, 0));
</script>

{#if hops && hops.length > 0}
	<div
		data-testid="hop-schedule-visualizer"
		class="flex flex-col gap-3 rounded-2xl border border-zinc-200/80 bg-zinc-50/60 p-4 transition-all dark:border-zinc-800/80 dark:bg-zinc-900/40 {className}"
	>
		<!-- Header Row -->
		<div class="flex items-center justify-between gap-2">
			<div class="flex items-center gap-2">
				<Flower2 class="h-4 w-4 text-hops-500 dark:text-hops-400" />
				<span class="font-editorial text-sm font-bold text-zinc-900 dark:text-white">
					{t('formulator.hop_timeline_title')}
				</span>
			</div>
			<div
				class="flex items-center gap-1.5 rounded-full border border-amber-500/30 bg-amber-500/10 px-2.5 py-0.5 font-mono text-[11px] font-bold text-amber-700 shadow-2xs dark:text-amber-400"
			>
				<Sparkles class="h-3 w-3" />
				<span>{t('formulator.total_bitterness', { ibu: formatNumber(totalIbu, 1) })}</span>
			</div>
		</div>

		<!-- Hop Timeline Sequence Ribbon -->
		<div
			class="grid grid-cols-2 gap-2 sm:grid-cols-3 md:grid-cols-4"
			role="group"
			aria-label={t('formulator.hop_timeline_title')}
		>
			{#each processedHops as hop (hop.id)}
				<div
					class="flex flex-col justify-between rounded-xl border border-zinc-200/80 bg-white/90 p-2.5 shadow-2xs transition-all hover:border-amber-500/40 dark:border-zinc-800 dark:bg-zinc-800/90"
				>
					<div class="flex items-start justify-between gap-1.5">
						<span class="truncate text-xs font-bold text-zinc-900 dark:text-zinc-100">
							{hop.ing ? getLocalizedIngredientName(hop.ing) : hop.notes || 'Hop'}
						</span>
						<span
							class="py-0.2 shrink-0 rounded-md border px-1.5 font-mono text-[9px] font-semibold {hop
								.phase.badgeClass}"
						>
							{t(hop.phase.key)}
						</span>
					</div>

					<div
						class="mt-2 flex items-center justify-between text-[11px] text-zinc-500 dark:text-zinc-400"
					>
						<span class="font-mono font-bold text-zinc-700 dark:text-zinc-200">{hop.amount}g</span>
						<span class="flex items-center gap-0.5 font-mono">
							<Clock class="h-2.5 w-2.5" />
							{hop.usage === 'DryHop' ? 'Dry' : `${hop.durationMinutes}m`}
						</span>
					</div>

					<!-- Micro IBU Contribution Bar -->
					<div class="mt-1.5 flex items-center gap-1.5">
						<div class="h-1.5 flex-1 overflow-hidden rounded-full bg-zinc-100 dark:bg-zinc-700">
							<div
								class="h-full rounded-full transition-all"
								style="width: {totalIbu > 0
									? Math.min(100, Math.max(5, (hop.ibu / totalIbu) * 100))
									: 0}%; background-color: {hop.phase.barColor};"
							></div>
						</div>
						<span class="font-mono text-[10px] font-bold text-zinc-600 dark:text-zinc-300">
							{hop.ibu > 0 ? `${formatNumber(hop.ibu, 1)}` : '0'}
						</span>
					</div>
				</div>
			{/each}
		</div>
	</div>
{/if}
