<script module lang="ts">
	export function getWaterBalance(
		so4: number,
		cl: number
	): {
		ratio: number;
		key: string;
		positionPercent: number;
		color: string;
		badgeClass: string;
	} {
		const safeCl = Math.max(1, cl);
		const safeSo4 = Math.max(0, so4);
		const ratio = Math.round((safeSo4 / safeCl) * 100) / 100;

		let positionPercent: number;
		if (ratio <= 1.0) {
			positionPercent = Math.max(5, (ratio / 1.0) * 50);
		} else {
			positionPercent = Math.min(95, 50 + ((ratio - 1.0) / 3.0) * 45);
		}

		if (ratio < 0.7) {
			return {
				ratio,
				key: 'calculations.water_balance.very_malty',
				positionPercent,
				color: '#c48b52',
				badgeClass: 'bg-amber-600/15 text-amber-800 dark:text-amber-200 border-amber-600/30'
			};
		}
		if (ratio <= 0.9) {
			return {
				ratio,
				key: 'calculations.water_balance.malty',
				positionPercent,
				color: '#d49b5e',
				badgeClass: 'bg-amber-500/15 text-amber-700 dark:text-amber-300 border-amber-500/30'
			};
		}
		if (ratio <= 1.3) {
			return {
				ratio,
				key: 'calculations.water_balance.balanced',
				positionPercent,
				color: '#f59e0b',
				badgeClass: 'bg-copper-500/15 text-copper-700 dark:text-copper-300 border-copper-500/30'
			};
		}
		if (ratio <= 2.5) {
			return {
				ratio,
				key: 'calculations.water_balance.bitter',
				positionPercent,
				color: '#10b981',
				badgeClass: 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-300 border-emerald-500/30'
			};
		}
		return {
			ratio,
			key: 'calculations.water_balance.very_bitter',
			positionPercent,
			color: '#059669',
			badgeClass: 'bg-teal-500/15 text-teal-700 dark:text-teal-300 border-teal-500/30'
		};
	}
</script>

<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { formatNumber } from '$lib/utils/formatNumber';
	import { Droplets, Sparkles } from '@lucide/svelte';

	interface Props {
		sulfatePpm: number;
		chloridePpm: number;
		class?: string;
	}

	let { sulfatePpm, chloridePpm, class: className = '' }: Props = $props();

	const balance = $derived(getWaterBalance(sulfatePpm, chloridePpm));
</script>

<div
	data-testid="water-balance-visualizer"
	class="flex flex-col gap-3 rounded-2xl border border-zinc-200/80 bg-zinc-50/60 p-4 transition-all dark:border-zinc-800/80 dark:bg-zinc-900/40 {className}"
>
	<!-- Header -->
	<div class="flex items-center justify-between gap-2">
		<div class="flex items-center gap-2">
			<Droplets class="h-4 w-4 text-sky-500 dark:text-sky-400" />
			<span class="font-editorial text-sm font-bold text-zinc-900 dark:text-white">
				{t('calculations.water_balance.title')}
			</span>
		</div>
		<div
			class="inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 font-mono text-[11px] font-bold shadow-2xs {balance.badgeClass}"
		>
			<Sparkles class="h-3 w-3" />
			<span>{formatNumber(balance.ratio, 2)} : 1 ({t(balance.key)})</span>
		</div>
	</div>

	<!-- Spectrum Gradient Bar -->
	<div class="relative mt-2">
		<!-- Track with Malt-to-Hop Gradient -->
		<div
			class="h-4 w-full rounded-full border border-zinc-200/80 shadow-inner dark:border-zinc-800"
			style="background: linear-gradient(90deg, #c48b52 0%, #f59e0b 50%, #10b981 100%);"
		></div>

		<!-- Animated Needle Pointer -->
		<div
			class="absolute -top-1.5 flex -translate-x-1/2 flex-col items-center transition-all duration-300"
			style="left: {balance.positionPercent}%;"
		>
			<div
				class="h-7 w-2 rounded-full border border-white bg-zinc-900 shadow-md dark:bg-white"
			></div>
		</div>
	</div>

	<!-- Spectrum Legend -->
	<div
		class="flex items-center justify-between pt-1 text-[10px] font-semibold text-zinc-500 dark:text-zinc-400"
	>
		<span class="text-amber-700 dark:text-amber-300"
			>{t('calculations.water_balance.very_malty')}</span
		>
		<span class="text-amber-500 dark:text-amber-400"
			>{t('calculations.water_balance.balanced')}</span
		>
		<span class="text-emerald-600 dark:text-emerald-400"
			>{t('calculations.water_balance.very_bitter')}</span
		>
	</div>
</div>
