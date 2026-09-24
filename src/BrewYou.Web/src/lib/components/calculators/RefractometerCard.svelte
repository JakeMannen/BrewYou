<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { calculateRefractometerFg } from '$lib/calculators/calculators';
	import { Eye, RotateCcw } from '@lucide/svelte';

	let originalBrix = $state<number>(12.5);
	let currentBrix = $state<number>(6.2);
	let wortCorrectionFactor = $state<number>(1.0);

	let result = $derived(
		calculateRefractometerFg({
			originalBrix,
			currentBrix,
			wortCorrectionFactor
		})
	);

	function resetDefaults() {
		originalBrix = 12.5;
		currentBrix = 6.2;
		wortCorrectionFactor = 1.0;
	}
</script>

<div
	id="refractometer"
	class="glass-panel relative flex flex-col justify-between overflow-hidden rounded-2xl border border-zinc-200/80 p-5 shadow-sm transition-all duration-200 hover:border-amber-500/30 hover:shadow-md dark:border-white/[0.08]"
>
	<!-- Header -->
	<div class="flex items-start justify-between gap-3">
		<div class="flex items-center gap-3">
			<div
				class="flex h-10 w-10 items-center justify-center rounded-xl border border-indigo-500/30 bg-indigo-500/10 text-indigo-600 dark:text-indigo-400"
			>
				<Eye class="h-5 w-5" />
			</div>
			<div>
				<h3 class="text-base font-bold text-zinc-900 dark:text-white">
					{t('calculations.refractometer.title')}
				</h3>
				<p class="text-xs text-zinc-500 dark:text-zinc-400">
					{t('calculations.refractometer.description')}
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
		<!-- Original Brix -->
		<div>
			<label
				for="calc-og-brix"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.refractometer.original_brix')}
			</label>
			<input
				id="calc-og-brix"
				type="number"
				step="0.1"
				min="1.0"
				max="40.0"
				bind:value={originalBrix}
				class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
			/>
		</div>

		<!-- Current Brix -->
		<div>
			<label
				for="calc-fg-brix"
				class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
			>
				{t('calculations.refractometer.current_brix')}
			</label>
			<input
				id="calc-fg-brix"
				type="number"
				step="0.1"
				min="0.0"
				max="40.0"
				bind:value={currentBrix}
				class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
			/>
		</div>

		<!-- WCF -->
		<div>
			<label for="calc-wcf" class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400">
				{t('calculations.refractometer.wcf')}
			</label>
			<input
				id="calc-wcf"
				type="number"
				step="0.01"
				min="0.90"
				max="1.15"
				bind:value={wortCorrectionFactor}
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
					{t('calculations.refractometer.corrected_fg')}
				</span>
				<div class="mt-0.5 flex items-baseline gap-2">
					<span class="font-mono text-3xl font-extrabold text-zinc-900 dark:text-white">
						{result.correctedFg.toFixed(3)}
					</span>
					<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400">
						({result.correctedFgPlato} °P)
					</span>
				</div>
			</div>

			<div class="flex flex-col text-left sm:text-right">
				<span class="text-[11px] font-medium text-zinc-500 dark:text-zinc-400">
					{t('calculations.refractometer.calculated_abv')}
				</span>
				<span class="font-mono text-xs font-bold text-zinc-800 dark:text-zinc-200">
					{result.abv}%
				</span>
			</div>
		</div>
	</div>
</div>
