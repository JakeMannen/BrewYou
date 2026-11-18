<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import {
		settings,
		celsiusToFahrenheit,
		fahrenheitToCelsius,
		volumesToGramsPerLiter,
		gramsPerLiterToVolumes
	} from '$lib/stores/settings.svelte';
	import { calculateCarbonation } from '$lib/calculators/calculators';
	import { CARBONATION_STYLE_GUIDE } from '$lib/calculators/carbonation-cheat-sheet';
	import { formatNumber } from '$lib/utils/formatNumber';
	import { Sparkles, RotateCcw, AlertTriangle, BookOpen, Info } from '@lucide/svelte';

	interface Props {
		targetCo2Volumes?: number;
		kegTempC?: number;
		onJumpToCheatSheet?: () => void;
		class?: string;
	}

	let {
		targetCo2Volumes = $bindable(2.4),
		kegTempC = $bindable(4.0),
		onJumpToCheatSheet,
		class: className = ''
	}: Props = $props();

	let volumeUnit = $derived(settings.volumeUnit);
	let tempUnit = $derived(settings.temperatureUnit);
	let carbUnit = $derived(settings.carbonationUnit);

	let displayVol = $state<number>(settings.defaultBatchSize || 20);
	let beerTempC = $state<number>(20.0);

	// Derived display wrappers that convert seamlessly when user toggles units in Settings
	let displayBeerTemp = $derived(
		tempUnit === 'Fahrenheit' ? celsiusToFahrenheit(beerTempC) : Number(beerTempC.toFixed(1))
	);
	let displayKegTemp = $derived(
		tempUnit === 'Fahrenheit' ? celsiusToFahrenheit(kegTempC) : Number(kegTempC.toFixed(1))
	);
	let displayTargetCarb = $derived(
		carbUnit === 'GramsPerLiter'
			? volumesToGramsPerLiter(targetCo2Volumes)
			: Number(targetCo2Volumes.toFixed(2))
	);

	let effectiveVolLiters = $derived(
		volumeUnit === 'Gallons' ? displayVol * 3.785411784 : displayVol
	);

	let result = $derived(
		calculateCarbonation({
			beerVolumeLiters: effectiveVolLiters,
			targetCo2Volumes,
			beerTempC,
			kegTempC
		})
	);

	let activeStylePresetId = $derived(
		CARBONATION_STYLE_GUIDE.find((s) => Math.abs(s.defaultVol - targetCo2Volumes) < 0.01)?.id ?? ''
	);

	function handleTargetCarbInput(e: Event) {
		const raw = Number((e.currentTarget as HTMLInputElement).value);
		if (!Number.isFinite(raw)) return;
		const vols = carbUnit === 'GramsPerLiter' ? gramsPerLiterToVolumes(raw) : raw;
		targetCo2Volumes = Math.min(4.5, Math.max(1.0, Number(vols.toFixed(2))));
	}

	function handleBeerTempInput(e: Event) {
		const raw = Number((e.currentTarget as HTMLInputElement).value);
		if (!Number.isFinite(raw)) return;
		beerTempC = tempUnit === 'Fahrenheit' ? fahrenheitToCelsius(raw) : raw;
	}

	function handleKegTempInput(e: Event) {
		const raw = Number((e.currentTarget as HTMLInputElement).value);
		if (!Number.isFinite(raw)) return;
		kegTempC = tempUnit === 'Fahrenheit' ? fahrenheitToCelsius(raw) : raw;
	}

	function handleStylePresetChange(e: Event) {
		const id = (e.currentTarget as HTMLSelectElement).value;
		const style = CARBONATION_STYLE_GUIDE.find((s) => s.id === id);
		if (style) {
			targetCo2Volumes = style.defaultVol;
		}
	}

	function resetDefaults() {
		displayVol = settings.defaultBatchSize || 20;
		targetCo2Volumes = 2.4;
		beerTempC = 20.0;
		kegTempC = 4.0;
	}
</script>

<div
	id="carbonation-priming"
	class="glass-panel relative flex flex-col justify-between overflow-hidden rounded-2xl border border-zinc-200/80 p-5 shadow-sm transition-all duration-200 hover:border-amber-500/30 hover:shadow-md dark:border-white/[0.08] {className}"
>
	<!-- Header -->
	<div class="flex flex-wrap items-start justify-between gap-3">
		<div class="flex items-center gap-3">
			<div
				class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
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

		<div class="flex items-center gap-2">
			{#if onJumpToCheatSheet}
				<button
					type="button"
					onclick={onJumpToCheatSheet}
					class="inline-flex items-center gap-1.5 rounded-xl border border-amber-500/30 bg-amber-500/10 px-3 py-1.5 text-xs font-semibold text-amber-700 transition-colors hover:bg-amber-500/20 dark:text-amber-300"
				>
					<BookOpen class="h-3.5 w-3.5" />
					<span>{t('calculations.carbonation.jump_to_cheat_sheet')}</span>
				</button>
			{/if}

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
	</div>

	<!-- Body Grid: Inputs (Left) + Results (Right) -->
	<div class="mt-5 grid grid-cols-1 gap-5 lg:grid-cols-12">
		<!-- Inputs Panel -->
		<div class="space-y-3 lg:col-span-6">
			<!-- BJCP Style Preset Selector -->
			<div>
				<label
					for="calc-carb-style-preset"
					class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
				>
					{t('calculations.carbonation.style_preset')}
				</label>
				<select
					id="calc-carb-style-preset"
					value={activeStylePresetId}
					onchange={handleStylePresetChange}
					class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
				>
					<option value="">{t('calculations.carbonation.style_preset_custom')}</option>
					{#each CARBONATION_STYLE_GUIDE as style (style.id)}
						<option value={style.id}>
							{t(style.nameKey)} ({formatNumber(style.minVol, 1)}–{formatNumber(style.maxVol, 1)} vol)
						</option>
					{/each}
				</select>
			</div>

			<div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
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
						class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 font-mono text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
					/>
				</div>

				<!-- Target CO2 -->
				<div>
					<div class="flex items-center justify-between gap-2">
						<label
							for="calc-target-co2"
							class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
						>
							{t('calculations.carbonation.target_co2')} ({settings.carbShort})
						</label>
						<span class="font-mono text-[10px] text-zinc-400 dark:text-zinc-500">
							{carbUnit === 'GramsPerLiter'
								? `${formatNumber(targetCo2Volumes, 2)} vol`
								: `${formatNumber(result.targetCo2GramsPerLiter, 2)} g/L`}
						</span>
					</div>
					<input
						id="calc-target-co2"
						type="number"
						step="0.1"
						min={carbUnit === 'GramsPerLiter' ? '2.0' : '1.0'}
						max={carbUnit === 'GramsPerLiter' ? '8.8' : '4.5'}
						value={displayTargetCarb}
						oninput={handleTargetCarbInput}
						class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 font-mono text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
					/>
				</div>

				<!-- Beer Temp (Peak Post-Fermentation) -->
				<div>
					<label
						for="calc-carb-temp"
						class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
					>
						{t('calculations.carbonation.beer_temp')} ({settings.tempShort})
					</label>
					<input
						id="calc-carb-temp"
						type="number"
						step="0.5"
						value={displayBeerTemp}
						oninput={handleBeerTempInput}
						class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 font-mono text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
					/>
					<p class="mt-1 flex items-center gap-1 text-[10px] text-zinc-500 dark:text-zinc-400">
						<Info class="h-3 w-3 shrink-0 text-amber-500" />
						<span>{t('calculations.carbonation.peak_temp_hint')}</span>
					</p>
				</div>

				<!-- Keg / Serving Temp -->
				<div>
					<label
						for="calc-keg-temp"
						class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
					>
						{t('calculations.carbonation.keg_temp')} ({settings.tempShort})
					</label>
					<input
						id="calc-keg-temp"
						type="number"
						step="0.5"
						value={displayKegTemp}
						oninput={handleKegTempInput}
						class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 font-mono text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
					/>
					<p class="mt-1 font-mono text-[10px] text-zinc-500 dark:text-zinc-400">
						{t('calculations.carbonation.residual_co2')}:
						{settings.formatCarbonation(result.residualCo2Volumes, 2)}
					</p>
				</div>
			</div>
		</div>

		<!-- Results Panel -->
		<div
			class="flex flex-col justify-between rounded-xl border border-amber-500/20 bg-amber-500/5 p-4 lg:col-span-6 dark:border-amber-400/20 dark:bg-amber-400/5"
		>
			<div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
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
						{t('calculations.carbonation.keg_pressure', {
							temp: settings.formatTemperature(kegTempC, 1)
						})}
					</span>
					<span class="font-mono text-base font-extrabold text-zinc-900 dark:text-white">
						{formatNumber(result.kegPsi, 1)} PSI
						<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400">
							({formatNumber(result.kegBar, 2)} bar)
						</span>
					</span>
				</div>
			</div>

			<!-- Alternative Priming Sugar Options Subgrid -->
			<div
				class="mt-4 grid grid-cols-2 gap-2 border-t border-zinc-200/60 pt-3 text-center sm:grid-cols-4 dark:border-white/5"
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
				<div>
					<span class="block text-[10px] text-zinc-500 dark:text-zinc-400">
						{t('calculations.carbonation.candi_syrup')}
					</span>
					<span class="font-mono text-xs font-semibold text-zinc-800 dark:text-zinc-200">
						{result.candiSyrupGrams} g
					</span>
				</div>
			</div>

			<!-- Bottle Safety Alert -->
			{#if result.bottleSafetyLevel === 'heavy_glass_required'}
				<div
					role="alert"
					class="mt-3 flex items-start gap-2 rounded-lg border border-rose-500/40 bg-rose-500/10 p-2.5 text-xs text-rose-800 dark:text-rose-200"
				>
					<AlertTriangle class="mt-0.5 h-4 w-4 shrink-0 text-rose-600 dark:text-rose-400" />
					<span class="leading-snug">{t('calculations.carbonation.bottle_heavy_glass')}</span>
				</div>
			{:else if result.bottleSafetyLevel === 'caution'}
				<div
					role="status"
					class="mt-3 flex items-start gap-2 rounded-lg border border-amber-500/40 bg-amber-500/15 p-2.5 text-xs text-amber-800 dark:text-amber-200"
				>
					<AlertTriangle class="mt-0.5 h-4 w-4 shrink-0 text-amber-600 dark:text-amber-400" />
					<span class="leading-snug">{t('calculations.carbonation.bottle_caution')}</span>
				</div>
			{/if}
		</div>
	</div>
</div>
