<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import { calculateCarbonation } from '$lib/calculators/calculators';
	import { Sparkles, RotateCcw } from '@lucide/svelte';

	let volumeUnit = $derived(settings.volumeUnit);
	let tempUnit = $derived(settings.temperatureUnit);

	let displayVol = $state<number>(settings.defaultBatchSize || 20);
	let targetCo2 = $state<number>(2.4);
	let displayTemp = $state<number>(20.0);

	let effectiveVolLiters = $derived(
		volumeUnit === 'Gallons' ? displayVol * 3.785411784 : displayVol
	);
	let effectiveTempC = $derived(
		tempUnit === 'Fahrenheit' ? ((displayTemp - 32) * 5) / 9 : displayTemp
	);

	let result = $derived(
		calculateCarbonation({
			beerVolumeLiters: effectiveVolLiters,
			targetCo2Volumes: targetCo2,
			beerTempC: effectiveTempC
		})
	);

	function resetDefaults() {
		displayVol = settings.defaultBatchSize || 20;
		targetCo2 = 2.4;
		displayTemp = tempUnit === 'Fahrenheit' ? 68.0 : 20.0;
	}
</script>

<div
	id="carbonation-priming"
	class="glass-panel relative flex flex-col justify-between overflow-hidden rounded-2xl border border-zinc-200/80 p-5 shadow-sm transition-all duration-200 hover:border-amber-500/30 hover:shadow-md dark:border-white/[0.08]"
>
	<!-- Header -->
	<div class="flex items-start justify-between gap-3">
		<div class="flex items-center gap-3">
			<div
				class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
			>
				<Sparkles class="h-5 w-5" />
			</div>
			<div>
				<h3 class="text-base font-bold text-zinc-900 dark:text-white">
					{t('calculations.carbonation.title')}
				</h3>
				<p class="text-xs text-zinc-500 dark:text-zinc-400">
					{t('calculations.carbonation.description')}
				</p>
			</div>
		</div>

		<button
			type="button"
			onclick={resetDefaults}
			class="flex h-8 w-8 items-center justify-center rounded-lg text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-600 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
			title={t('calculations.reset')}
			aria-label={t('calculations.reset')}
		>
			<RotateCcw class="h-3.5 w-3.5" />
		</button>
	</div>

	<!-- Inputs Grid -->
	<div class="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-3">
		<!-- Beer Volume -->
		<div>
			<label
				for="calc-carb-vol"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.carbonation.beer_volume')} ({volumeUnit === 'Gallons' ? 'gal' : 'L'})
			</label>
			<input
				id="calc-carb-vol"
				type="number"
				step="0.5"
				min="0.1"
				bind:value={displayVol}
				class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
			/>
		</div>

		<!-- Target CO2 Volumes -->
		<div>
			<label
				for="calc-target-co2"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.carbonation.target_co2')}
			</label>
			<input
				id="calc-target-co2"
				type="number"
				step="0.1"
				min="1.0"
				max="4.5"
				bind:value={targetCo2}
				class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
			/>
		</div>

		<!-- Beer Temp -->
		<div>
			<label
				for="calc-carb-temp"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.carbonation.beer_temp')} ({tempUnit === 'Fahrenheit' ? '°F' : '°C'})
			</label>
			<input
				id="calc-carb-temp"
				type="number"
				step="0.5"
				bind:value={displayTemp}
				class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
			/>
		</div>
	</div>

	<!-- Results Panel -->
	<div
		class="mt-5 rounded-xl border border-amber-500/20 bg-amber-500/5 p-4 dark:border-amber-400/20 dark:bg-amber-400/5"
	>
		<div class="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
			<div>
				<span
					class="text-[11px] font-bold tracking-wider text-amber-600 uppercase dark:text-amber-400"
				>
					{t('calculations.carbonation.table_sugar')}
				</span>
				<div class="mt-0.5 flex items-baseline gap-1.5">
					<span class="font-mono text-3xl font-extrabold text-zinc-900 dark:text-white">
						{result.tableSugarGrams}
					</span>
					<span class="font-semibold text-zinc-500 dark:text-zinc-400">g</span>
				</div>
			</div>

			<div class="flex flex-col text-left sm:text-right">
				<span class="text-[11px] font-medium text-zinc-500 dark:text-zinc-400">
					{t('calculations.carbonation.keg_psi')}
				</span>
				<span class="font-mono text-xs font-bold text-zinc-800 dark:text-zinc-200">
					{result.kegPsiAt4C} PSI
				</span>
			</div>
		</div>

		<!-- Alternative Priming Sugar Options Subgrid -->
		<div
			class="mt-3 grid grid-cols-3 gap-2 border-t border-zinc-200/60 pt-3 text-center dark:border-white/5"
		>
			<div>
				<span class="block text-[10px] text-zinc-500 dark:text-zinc-400">
					{t('calculations.carbonation.corn_sugar')}
				</span>
				<span class="font-mono text-xs font-semibold text-zinc-800 dark:text-zinc-200">
					{result.cornSugarGrams} g
				</span>
			</div>
			<div>
				<span class="block text-[10px] text-zinc-500 dark:text-zinc-400">
					{t('calculations.carbonation.dme')}
				</span>
				<span class="font-mono text-xs font-semibold text-zinc-800 dark:text-zinc-200">
					{result.dmeGrams} g
				</span>
			</div>
			<div>
				<span class="block text-[10px] text-zinc-500 dark:text-zinc-400">
					{t('calculations.carbonation.honey')}
				</span>
				<span class="font-mono text-xs font-semibold text-zinc-800 dark:text-zinc-200">
					{result.honeyGrams} g
				</span>
			</div>
		</div>
	</div>
</div>
