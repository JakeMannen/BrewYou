<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import { calculateHydrometerCorrection } from '$lib/calculators/calculators';
	import { Gauge, RotateCcw } from '@lucide/svelte';

	let tempUnit = $derived(settings.temperatureUnit);

	let measuredSg = $state<number>(1.05);
	let displaySampleTemp = $state<number>(50.0);
	let displayCalTemp = $state<number>(20.0);

	let effectiveSampleTempC = $derived(
		tempUnit === 'Fahrenheit' ? ((displaySampleTemp - 32) * 5) / 9 : displaySampleTemp
	);
	let effectiveCalTempC = $derived(
		tempUnit === 'Fahrenheit' ? ((displayCalTemp - 32) * 5) / 9 : displayCalTemp
	);

	let result = $derived(
		calculateHydrometerCorrection({
			measuredSg,
			sampleTempC: effectiveSampleTempC,
			calibrationTempC: effectiveCalTempC
		})
	);

	function resetDefaults() {
		measuredSg = 1.05;
		displaySampleTemp = tempUnit === 'Fahrenheit' ? 122.0 : 50.0;
		displayCalTemp = tempUnit === 'Fahrenheit' ? 68.0 : 20.0;
	}
</script>

<div
	id="hydrometer-correction"
	class="glass-panel relative flex flex-col justify-between overflow-hidden rounded-2xl border border-zinc-200/80 p-5 shadow-sm transition-all duration-200 hover:border-amber-500/30 hover:shadow-md dark:border-white/[0.08]"
>
	<!-- Header -->
	<div class="flex items-start justify-between gap-3">
		<div class="flex items-center gap-3">
			<div
				class="flex h-10 w-10 items-center justify-center rounded-xl border border-cyan-500/30 bg-cyan-500/10 text-cyan-600 dark:text-cyan-400"
			>
				<Gauge class="h-5 w-5" />
			</div>
			<div>
				<h3 class="text-base font-bold text-zinc-900 dark:text-white">
					{t('calculations.hydrometer.title')}
				</h3>
				<p class="text-xs text-zinc-500 dark:text-zinc-400">
					{t('calculations.hydrometer.description')}
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
		<!-- Measured SG -->
		<div>
			<label
				for="calc-measured-sg"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.hydrometer.measured_sg')}
			</label>
			<input
				id="calc-measured-sg"
				type="number"
				step="0.001"
				min="1.000"
				max="1.200"
				bind:value={measuredSg}
				class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
			/>
		</div>

		<!-- Sample Temp -->
		<div>
			<label
				for="calc-sample-temp"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.hydrometer.sample_temp')} ({tempUnit === 'Fahrenheit' ? '°F' : '°C'})
			</label>
			<input
				id="calc-sample-temp"
				type="number"
				step="0.5"
				bind:value={displaySampleTemp}
				class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
			/>
		</div>

		<!-- Calibration Temp -->
		<div>
			<label
				for="calc-cal-temp"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.hydrometer.calibration_temp')} ({tempUnit === 'Fahrenheit' ? '°F' : '°C'})
			</label>
			<input
				id="calc-cal-temp"
				type="number"
				step="0.5"
				bind:value={displayCalTemp}
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
					{t('calculations.hydrometer.corrected_sg')}
				</span>
				<div class="mt-0.5 flex items-baseline gap-1">
					<span class="font-mono text-3xl font-extrabold text-zinc-900 dark:text-white">
						{result.correctedSg.toFixed(3)}
					</span>
				</div>
			</div>

			<div class="flex flex-col text-left sm:text-right">
				<span class="text-[11px] font-medium text-zinc-500 dark:text-zinc-400">
					{t('calculations.hydrometer.difference')}
				</span>
				<span
					class="font-mono text-xs font-bold {result.deltaPoints >= 0
						? 'text-amber-600 dark:text-amber-400'
						: 'text-zinc-600 dark:text-zinc-300'}"
				>
					{result.deltaPoints >= 0 ? `+${result.deltaPoints}` : result.deltaPoints} gravity points
				</span>
			</div>
		</div>
	</div>
</div>
