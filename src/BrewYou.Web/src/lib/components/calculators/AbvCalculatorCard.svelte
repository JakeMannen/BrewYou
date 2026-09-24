<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import { calculateAbv } from '$lib/calculators/calculators';
	import { RotateCcw, Percent } from '@lucide/svelte';

	let gravityUnit = $derived(settings.gravityUnit);

	// Display inputs
	let displayOg = $state<number>(1.05);
	let displayFg = $state<number>(1.01);

	let effectiveOg = $derived(gravityUnit === 'Plato' ? settings.platoToSg(displayOg) : displayOg);
	let effectiveFg = $derived(gravityUnit === 'Plato' ? settings.platoToSg(displayFg) : displayFg);

	let result = $derived(
		calculateAbv({
			originalGravity: effectiveOg,
			finalGravity: effectiveFg
		})
	);

	function resetDefaults() {
		if (gravityUnit === 'Plato') {
			displayOg = 12.4;
			displayFg = 2.6;
		} else {
			displayOg = 1.05;
			displayFg = 1.01;
		}
	}
</script>

<div
	id="abv-attenuation"
	class="glass-panel relative flex flex-col justify-between overflow-hidden rounded-2xl border border-zinc-200/80 p-5 shadow-sm transition-all duration-200 hover:border-amber-500/30 hover:shadow-md dark:border-white/[0.08]"
>
	<!-- Header -->
	<div class="flex items-start justify-between gap-3">
		<div class="flex items-center gap-3">
			<div
				class="flex h-10 w-10 items-center justify-center rounded-xl border border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400"
			>
				<Percent class="h-5 w-5" />
			</div>
			<div>
				<h3 class="text-base font-bold text-zinc-900 dark:text-white">
					{t('calculations.abv.title')}
				</h3>
				<p class="text-xs text-zinc-500 dark:text-zinc-400">
					{t('calculations.abv.description')}
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
		<!-- OG -->
		<div>
			<label for="calc-og" class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400">
				{t('calculations.abv.og')} ({gravityUnit === 'Plato' ? '°P' : 'SG'})
			</label>
			<input
				id="calc-og"
				type="number"
				step={gravityUnit === 'Plato' ? '0.1' : '0.001'}
				min={gravityUnit === 'Plato' ? '0' : '1.000'}
				bind:value={displayOg}
				class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-sm font-semibold text-zinc-900 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
			/>
		</div>

		<!-- FG -->
		<div>
			<label for="calc-fg" class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400">
				{t('calculations.abv.fg')} ({gravityUnit === 'Plato' ? '°P' : 'SG'})
			</label>
			<input
				id="calc-fg"
				type="number"
				step={gravityUnit === 'Plato' ? '0.1' : '0.001'}
				min={gravityUnit === 'Plato' ? '0' : '0.980'}
				bind:value={displayFg}
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
					{t('calculations.abv.standard_abv')}
				</span>
				<div class="mt-0.5 flex items-baseline gap-1">
					<span class="font-mono text-3xl font-extrabold text-zinc-900 dark:text-white">
						{result.abvStandard}%
					</span>
				</div>
			</div>

			<div class="flex flex-col text-left sm:text-right">
				<span class="text-[11px] font-medium text-zinc-500 dark:text-zinc-400">
					{t('calculations.abv.apparent_att')}
				</span>
				<span class="font-mono text-xs font-bold text-zinc-800 dark:text-zinc-200">
					{result.apparentAttenuation}%
				</span>
			</div>
		</div>

		<!-- Detailed Metrics Subgrid -->
		<div
			class="mt-3 grid grid-cols-3 gap-2 border-t border-zinc-200/60 pt-3 text-center dark:border-white/5"
		>
			<div>
				<span class="block text-[10px] text-zinc-500 dark:text-zinc-400">
					{t('calculations.abv.alternate_abv')}
				</span>
				<span class="font-mono text-xs font-semibold text-zinc-800 dark:text-zinc-200">
					{result.abvAlternate}%
				</span>
			</div>
			<div>
				<span class="block text-[10px] text-zinc-500 dark:text-zinc-400">
					{t('calculations.abv.real_att')}
				</span>
				<span class="font-mono text-xs font-semibold text-zinc-800 dark:text-zinc-200">
					{result.realAttenuation}%
				</span>
			</div>
			<div>
				<span class="block text-[10px] text-zinc-500 dark:text-zinc-400">
					{t('calculations.abv.calories')}
				</span>
				<span class="font-mono text-xs font-semibold text-zinc-800 dark:text-zinc-200">
					~{result.caloriesPer330ml} kcal
				</span>
			</div>
		</div>
	</div>
</div>
