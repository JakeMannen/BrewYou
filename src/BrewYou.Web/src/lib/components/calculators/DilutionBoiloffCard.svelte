<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import { calculateDilutionBoiloff } from '$lib/calculators/calculators';
	import { Scale, RotateCcw } from '@lucide/svelte';

	let currentVol = $state<number>(settings.defaultBatchSize || 20);
	let currentSg = $state<number>(1.06);
	let targetSg = $state<number>(1.05);

	let volumeUnit = $derived(settings.volumeUnit);
	let displayVol = $state<number>(currentVol);

	let effectiveVolLiters = $derived(
		volumeUnit === 'Gallons' ? displayVol * 3.785411784 : displayVol
	);

	let result = $derived(
		calculateDilutionBoiloff({
			currentVolumeLiters: effectiveVolLiters,
			currentSg,
			targetSg
		})
	);

	function resetDefaults() {
		displayVol = settings.defaultBatchSize || 20;
		currentSg = 1.06;
		targetSg = 1.05;
	}
</script>

<div
	id="dilution-boiloff"
	class="glass-panel relative flex flex-col justify-between overflow-hidden rounded-2xl border border-zinc-200/80 p-5 shadow-sm transition-all duration-200 hover:border-amber-500/30 hover:shadow-md dark:border-white/[0.08]"
>
	<!-- Header -->
	<div class="flex items-start justify-between gap-3">
		<div class="flex items-center gap-3">
			<div
				class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
			>
				<Scale class="h-5 w-5" />
			</div>
			<div>
				<h3 class="text-base font-bold text-zinc-900 dark:text-white">
					{t('calculations.dilution.title')}
				</h3>
				<p class="text-xs text-zinc-500 dark:text-zinc-400">
					{t('calculations.dilution.description')}
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
		<!-- Current Volume -->
		<div>
			<label
				for="calc-dilution-vol"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.dilution.current_volume')} ({volumeUnit === 'Gallons' ? 'gal' : 'L'})
			</label>
			<input
				id="calc-dilution-vol"
				type="number"
				step="0.5"
				min="0.1"
				bind:value={displayVol}
				class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
			/>
		</div>

		<!-- Current SG -->
		<div>
			<label
				for="calc-dilution-cur-sg"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.dilution.current_sg')}
			</label>
			<input
				id="calc-dilution-cur-sg"
				type="number"
				step="0.001"
				min="1.000"
				max="1.200"
				bind:value={currentSg}
				class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
			/>
		</div>

		<!-- Target SG -->
		<div>
			<label
				for="calc-dilution-tgt-sg"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.dilution.target_sg')}
			</label>
			<input
				id="calc-dilution-tgt-sg"
				type="number"
				step="0.001"
				min="1.000"
				max="1.200"
				bind:value={targetSg}
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
					{result.type === 'dilution'
						? t('calculations.dilution.add_water')
						: t('calculations.dilution.boil_off')}
				</span>
				<div class="mt-0.5 flex items-baseline gap-1.5">
					<span class="font-mono text-3xl font-extrabold text-zinc-900 dark:text-white">
						{result.type === 'dilution'
							? volumeUnit === 'Gallons'
								? result.waterToAddGallons
								: result.waterToAddLiters
							: volumeUnit === 'Gallons'
								? result.volumeToBoilOffGallons
								: result.volumeToBoilOffLiters}
					</span>
					<span class="font-semibold text-zinc-500 dark:text-zinc-400">
						{volumeUnit === 'Gallons' ? 'gal' : 'L'}
					</span>
				</div>
			</div>

			<div class="flex flex-col text-left sm:text-right">
				<span class="text-[11px] font-medium text-zinc-500 dark:text-zinc-400">
					{t('calculations.dilution.final_volume')}
				</span>
				<span class="font-mono text-xs font-bold text-zinc-800 dark:text-zinc-200">
					{volumeUnit === 'Gallons' ? result.finalVolumeGallons : result.finalVolumeLiters}
					{volumeUnit === 'Gallons' ? 'gal' : 'L'}
				</span>
			</div>
		</div>
	</div>
</div>
