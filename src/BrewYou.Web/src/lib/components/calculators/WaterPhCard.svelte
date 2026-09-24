<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import {
		calculateWaterAcidification,
		calculateWaterAlkalinization,
		ACID_CATALOG,
		BASE_CATALOG,
		type AcidType,
		type BaseType
	} from '$lib/calculators/water-chemistry';
	import { Droplets, AlertTriangle, RotateCcw, Sparkles } from '@lucide/svelte';

	let mode = $state<'acid' | 'base'>('acid');
	let displayVolume = $state<number>(settings.defaultBatchSize || 20);
	let startingPh = $state<number>(7.6);
	let targetPh = $state<number>(5.4);
	let alkalinity = $state<number>(100);
	let selectedAcid = $state<AcidType>('lactic_80');
	let selectedBase = $state<BaseType>('baking_soda');

	let volumeUnit = $derived(settings.volumeUnit);

	// Convert display volume to liters for calculation
	let volumeLiters = $derived(
		volumeUnit === 'Gallons' ? displayVolume * 3.785411784 : displayVolume
	);

	let acidResult = $derived(
		calculateWaterAcidification(volumeLiters, startingPh, targetPh, alkalinity, selectedAcid)
	);

	let baseResult = $derived(
		calculateWaterAlkalinization(volumeLiters, startingPh, targetPh, selectedBase)
	);

	function resetDefaults() {
		mode = 'acid';
		displayVolume = settings.defaultBatchSize || 20;
		startingPh = 7.6;
		targetPh = 5.4;
		alkalinity = 100;
		selectedAcid = 'lactic_80';
		selectedBase = 'baking_soda';
	}

	function handleModeChange(newMode: 'acid' | 'base') {
		mode = newMode;
		if (newMode === 'acid') {
			startingPh = 7.6;
			targetPh = 5.4;
		} else {
			startingPh = 5.0;
			targetPh = 5.5;
		}
	}
</script>

<div
	id="water-ph"
	class="glass-panel relative flex flex-col justify-between overflow-hidden rounded-2xl border border-zinc-200/80 p-5 shadow-sm transition-all duration-200 hover:border-amber-500/30 hover:shadow-md dark:border-white/[0.08]"
>
	<!-- Card Header -->
	<div class="flex items-start justify-between gap-3">
		<div class="flex items-center gap-3">
			<div
				class="flex h-10 w-10 items-center justify-center rounded-xl border border-sky-500/30 bg-sky-500/10 text-sky-600 dark:text-sky-400"
			>
				<Droplets class="h-5 w-5" />
			</div>
			<div>
				<h3 class="text-base font-bold text-zinc-900 dark:text-white">
					{t('calculations.water_ph.title')}
				</h3>
				<p class="text-xs text-zinc-500 dark:text-zinc-400">
					{t('calculations.water_ph.description')}
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

	<!-- Mode Switcher (Acid / Base) -->
	<div
		class="mt-4 flex rounded-xl border border-zinc-200/80 bg-zinc-100/60 p-1 dark:border-white/5 dark:bg-zinc-900/60"
	>
		<button
			type="button"
			onclick={() => handleModeChange('acid')}
			class="flex-1 rounded-lg py-1.5 text-xs font-semibold transition-all {mode === 'acid'
				? 'bg-white text-amber-600 shadow-sm dark:bg-zinc-800 dark:text-amber-400'
				: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-white'}"
		>
			{t('calculations.water_ph.mode_acid')}
		</button>
		<button
			type="button"
			onclick={() => handleModeChange('base')}
			class="flex-1 rounded-lg py-1.5 text-xs font-semibold transition-all {mode === 'base'
				? 'bg-white text-amber-600 shadow-sm dark:bg-zinc-800 dark:text-amber-400'
				: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-white'}"
		>
			{t('calculations.water_ph.mode_base')}
		</button>
	</div>

	<!-- Inputs Grid -->
	<div class="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">
		<!-- Water Volume -->
		<div>
			<label
				for="calc-water-vol"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.water_ph.water_volume')} ({volumeUnit === 'Gallons' ? 'gal' : 'L'})
			</label>
			<input
				id="calc-water-vol"
				type="number"
				step="0.5"
				min="0.1"
				bind:value={displayVolume}
				class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
			/>
		</div>

		<!-- Additive Selector -->
		<div>
			<label
				for="calc-additive-select"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.water_ph.select_additive')}
			</label>
			{#if mode === 'acid'}
				<select
					id="calc-additive-select"
					bind:value={selectedAcid}
					class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
				>
					{#each Object.keys(ACID_CATALOG) as key}
						<option value={key}>{t(ACID_CATALOG[key as AcidType].nameKey)}</option>
					{/each}
				</select>
			{:else}
				<select
					id="calc-additive-select"
					bind:value={selectedBase}
					class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
				>
					{#each Object.keys(BASE_CATALOG) as key}
						<option value={key}>{t(BASE_CATALOG[key as BaseType].nameKey)}</option>
					{/each}
				</select>
			{/if}
		</div>

		<!-- Starting pH -->
		<div>
			<label
				for="calc-start-ph"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.water_ph.starting_ph')}
			</label>
			<input
				id="calc-start-ph"
				type="number"
				step="0.1"
				min="3.0"
				max="12.0"
				bind:value={startingPh}
				class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
			/>
		</div>

		<!-- Target pH -->
		<div>
			<label
				for="calc-target-ph"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.water_ph.target_ph')}
			</label>
			<input
				id="calc-target-ph"
				type="number"
				step="0.1"
				min="3.0"
				max="12.0"
				bind:value={targetPh}
				class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
			/>
		</div>

		{#if mode === 'acid'}
			<!-- Water Alkalinity -->
			<div class="sm:col-span-2">
				<div class="flex items-center justify-between">
					<label
						for="calc-alkalinity"
						class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
					>
						{t('calculations.water_ph.alkalinity')}
					</label>
					<span class="text-[10px] text-zinc-400 dark:text-zinc-500">
						{t('calculations.water_ph.alkalinity_help')}
					</span>
				</div>
				<input
					id="calc-alkalinity"
					type="number"
					step="5"
					min="0"
					max="600"
					bind:value={alkalinity}
					class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
				/>
			</div>
		{/if}
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
					{t('calculations.water_ph.required_dose')}
				</span>
				<div class="mt-0.5 flex items-baseline gap-1.5">
					<span class="font-mono text-3xl font-extrabold text-zinc-900 dark:text-white">
						{mode === 'acid' ? acidResult.requiredAmount : baseResult.requiredAmount}
					</span>
					<span class="font-semibold text-zinc-500 dark:text-zinc-400">
						{mode === 'acid' ? acidResult.unit : baseResult.unit}
					</span>
				</div>
			</div>

			<div class="flex flex-col text-left sm:text-right">
				<span class="text-[11px] font-medium text-zinc-500 dark:text-zinc-400">
					{t('calculations.water_ph.dosage_rate')}
				</span>
				<span class="font-mono text-xs font-bold text-zinc-800 dark:text-zinc-200">
					{#if volumeUnit === 'Gallons'}
						{mode === 'acid' ? acidResult.amountPerGallon : baseResult.amountPerGallon}
						{mode === 'acid' ? acidResult.unit : baseResult.unit}/gal
					{:else}
						{mode === 'acid' ? acidResult.amountPerLiter : baseResult.amountPerLiter}
						{mode === 'acid' ? acidResult.unit : baseResult.unit}/L
					{/if}
				</span>
			</div>
		</div>

		<!-- Flavor threshold warning -->
		{#if mode === 'acid' && acidResult.exceedsFlavorThreshold}
			<div
				class="mt-3 flex items-start gap-2 rounded-lg border border-amber-500/40 bg-amber-500/15 p-2.5 text-xs text-amber-800 dark:text-amber-200"
			>
				<AlertTriangle class="mt-0.5 h-4 w-4 flex-shrink-0 text-amber-600 dark:text-amber-400" />
				<span class="leading-snug">{t('calculations.water_ph.flavor_warning')}</span>
			</div>
		{/if}

		<!-- Chalk low solubility warning -->
		{#if mode === 'base' && selectedBase === 'chalk'}
			<div
				class="mt-3 flex items-start gap-2 rounded-lg border border-amber-500/40 bg-amber-500/15 p-2.5 text-xs text-amber-800 dark:text-amber-200"
			>
				<AlertTriangle class="mt-0.5 h-4 w-4 flex-shrink-0 text-amber-600 dark:text-amber-400" />
				<span class="leading-snug">{t('calculations.water_ph.chalk_solubility_warning')}</span>
			</div>
		{/if}

		<!-- Mineral contribution impact for base additions -->
		{#if mode === 'base' && (baseResult.addedCalciumMgL || baseResult.addedSodiumMgL)}
			<div
				class="mt-3 flex items-center gap-2 rounded-lg border border-sky-500/20 bg-sky-500/10 p-2 text-xs text-sky-800 dark:text-sky-300"
			>
				<Sparkles class="h-3.5 w-3.5 flex-shrink-0" />
				<span>
					{t('calculations.water_ph.mineral_impact')}:
					{#if baseResult.addedSodiumMgL}
						+{baseResult.addedSodiumMgL} mg/L Na⁺
					{/if}
					{#if baseResult.addedCalciumMgL}
						+{baseResult.addedCalciumMgL} mg/L Ca²⁺
					{/if}
				</span>
			</div>
		{/if}
	</div>
</div>
