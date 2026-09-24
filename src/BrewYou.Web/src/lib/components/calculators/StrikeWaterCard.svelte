<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import { calculateStrikeWater } from '$lib/calculators/calculators';
	import { Flame, RotateCcw } from '@lucide/svelte';

	let tempUnit = $derived(settings.temperatureUnit);
	let weightUnit = $derived(settings.weightUnit);

	let displayGrainWeight = $state<number>(5.0);
	let displayGrainTemp = $state<number>(20.0);
	let displayMashTemp = $state<number>(65.0);
	let ratio = $state<number>(3.0); // L/kg

	let effectiveGrainWeightKg = $derived(
		weightUnit === 'Imperial' ? displayGrainWeight * 0.453592 : displayGrainWeight
	);

	let effectiveGrainTempC = $derived(
		tempUnit === 'Fahrenheit' ? ((displayGrainTemp - 32) * 5) / 9 : displayGrainTemp
	);

	let effectiveMashTempC = $derived(
		tempUnit === 'Fahrenheit' ? ((displayMashTemp - 32) * 5) / 9 : displayMashTemp
	);

	let result = $derived(
		calculateStrikeWater({
			grainWeightKg: effectiveGrainWeightKg,
			grainTempC: effectiveGrainTempC,
			targetMashTempC: effectiveMashTempC,
			ratioLitersPerKg: ratio
		})
	);

	function resetDefaults() {
		ratio = 3.0;
		displayGrainWeight = weightUnit === 'Imperial' ? Number((5.0 * 2.20462).toFixed(1)) : 5.0;
		displayGrainTemp = tempUnit === 'Fahrenheit' ? 68.0 : 20.0;
		displayMashTemp = tempUnit === 'Fahrenheit' ? 149.0 : 65.0;
	}
</script>

<div
	id="strike-water"
	class="glass-panel relative flex flex-col justify-between overflow-hidden rounded-2xl border border-zinc-200/80 p-5 shadow-sm transition-all duration-200 hover:border-amber-500/30 hover:shadow-md dark:border-white/[0.08]"
>
	<!-- Header -->
	<div class="flex items-start justify-between gap-3">
		<div class="flex items-center gap-3">
			<div
				class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
			>
				<Flame class="h-5 w-5" />
			</div>
			<div>
				<h3 class="text-base font-bold text-zinc-900 dark:text-white">
					{t('calculations.strike_water.title')}
				</h3>
				<p class="text-xs text-zinc-500 dark:text-zinc-400">
					{t('calculations.strike_water.description')}
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
	<div class="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">
		<!-- Grain Weight -->
		<div>
			<label
				for="calc-grain-weight"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.strike_water.grain_weight')} ({weightUnit === 'Imperial' ? 'lb' : 'kg'})
			</label>
			<input
				id="calc-grain-weight"
				type="number"
				step="0.1"
				min="0.1"
				bind:value={displayGrainWeight}
				class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
			/>
		</div>

		<!-- Ratio (L/kg or qt/lb) -->
		<div>
			<label
				for="calc-mash-ratio"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.strike_water.ratio')} (L/kg)
			</label>
			<input
				id="calc-mash-ratio"
				type="number"
				step="0.1"
				min="1.5"
				max="6.0"
				bind:value={ratio}
				class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
			/>
		</div>

		<!-- Grain Temperature -->
		<div>
			<label
				for="calc-grain-temp"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.strike_water.grain_temp')} ({tempUnit === 'Fahrenheit' ? '°F' : '°C'})
			</label>
			<input
				id="calc-grain-temp"
				type="number"
				step="0.5"
				bind:value={displayGrainTemp}
				class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
			/>
		</div>

		<!-- Target Mash Temperature -->
		<div>
			<label
				for="calc-mash-temp"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.strike_water.target_mash_temp')} ({tempUnit === 'Fahrenheit'
					? '°F'
					: '°C'})
			</label>
			<input
				id="calc-mash-temp"
				type="number"
				step="0.5"
				bind:value={displayMashTemp}
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
					{t('calculations.strike_water.strike_temp')}
				</span>
				<div class="mt-0.5 flex items-baseline gap-1.5">
					<span class="font-mono text-3xl font-extrabold text-zinc-900 dark:text-white">
						{tempUnit === 'Fahrenheit' ? result.strikeTempF : result.strikeTempC}
					</span>
					<span class="font-semibold text-zinc-500 dark:text-zinc-400">
						{tempUnit === 'Fahrenheit' ? '°F' : '°C'}
					</span>
				</div>
			</div>

			<div class="flex flex-col text-left sm:text-right">
				<span class="text-[11px] font-medium text-zinc-500 dark:text-zinc-400">
					{t('calculations.strike_water.total_water')}
				</span>
				<span class="font-mono text-xs font-bold text-zinc-800 dark:text-zinc-200">
					{settings.volumeUnit === 'Gallons'
						? `${result.totalWaterGallons} gal`
						: `${result.totalWaterLiters} L`}
				</span>
			</div>
		</div>
	</div>
</div>
