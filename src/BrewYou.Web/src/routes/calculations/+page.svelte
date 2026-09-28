<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import WaterPhCard from '$lib/components/calculators/WaterPhCard.svelte';
	import StrikeWaterCard from '$lib/components/calculators/StrikeWaterCard.svelte';
	import AbvCalculatorCard from '$lib/components/calculators/AbvCalculatorCard.svelte';
	import HydrometerCorrectionCard from '$lib/components/calculators/HydrometerCorrectionCard.svelte';
	import RefractometerCard from '$lib/components/calculators/RefractometerCard.svelte';
	import DilutionBoiloffCard from '$lib/components/calculators/DilutionBoiloffCard.svelte';
	import CarbonationCard from '$lib/components/calculators/CarbonationCard.svelte';
	import { Calculator, Search } from '@lucide/svelte';

	type Category = 'all' | 'water' | 'gravity' | 'finishing';

	let activeCategory = $state<Category>('all');
	let searchQuery = $state('');

	const calculators = [
		{
			id: 'water-ph',
			category: 'water' as Category,
			titleKey: 'calculations.water_ph.title',
			descKey: 'calculations.water_ph.description',
			component: WaterPhCard
		},
		{
			id: 'strike-water',
			category: 'water' as Category,
			titleKey: 'calculations.strike_water.title',
			descKey: 'calculations.strike_water.description',
			component: StrikeWaterCard
		},
		{
			id: 'abv-attenuation',
			category: 'gravity' as Category,
			titleKey: 'calculations.abv.title',
			descKey: 'calculations.abv.description',
			component: AbvCalculatorCard
		},
		{
			id: 'hydrometer-correction',
			category: 'gravity' as Category,
			titleKey: 'calculations.hydrometer.title',
			descKey: 'calculations.hydrometer.description',
			component: HydrometerCorrectionCard
		},
		{
			id: 'refractometer',
			category: 'gravity' as Category,
			titleKey: 'calculations.refractometer.title',
			descKey: 'calculations.refractometer.description',
			component: RefractometerCard
		},
		{
			id: 'dilution-boiloff',
			category: 'gravity' as Category,
			titleKey: 'calculations.dilution.title',
			descKey: 'calculations.dilution.description',
			component: DilutionBoiloffCard
		},
		{
			id: 'carbonation-priming',
			category: 'finishing' as Category,
			titleKey: 'calculations.carbonation.title',
			descKey: 'calculations.carbonation.description',
			component: CarbonationCard
		}
	];

	let filteredCalculators = $derived(
		calculators.filter((calc) => {
			const matchesCategory = activeCategory === 'all' || calc.category === activeCategory;
			if (!matchesCategory) return false;

			const query = searchQuery.toLowerCase().trim();
			if (!query) return true;

			const title = t(calc.titleKey).toLowerCase();
			const desc = t(calc.descKey).toLowerCase();
			return title.includes(query) || desc.includes(query);
		})
	);
</script>

<svelte:head>
	<title>BrewYou — {t('calculations.title')}</title>
</svelte:head>

<div class="flex flex-col gap-6">
	<!-- Page Header -->
	<div class="flex flex-col justify-between gap-4 md:flex-row md:items-end">
		<div>
			<div class="flex items-center gap-2.5">
				<div
					class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-500"
				>
					<Calculator class="h-5 w-5" />
				</div>
				<h1
					class="font-editorial text-3xl font-bold tracking-tight text-zinc-900 sm:text-4xl dark:text-white"
				>
					{t('calculations.title')}
				</h1>
			</div>
			<p class="mt-1 text-sm text-zinc-600 dark:text-zinc-400">
				{t('calculations.subtitle')}
			</p>
		</div>

		<!-- Search Input -->
		<div class="relative w-full sm:w-72">
			<Search class="absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-zinc-400" />
			<input
				type="text"
				placeholder={t('calculations.search_placeholder')}
				bind:value={searchQuery}
				class="w-full rounded-xl border border-zinc-200/80 bg-white/80 py-2 pr-4 pl-9 text-xs text-zinc-900 placeholder-zinc-400 shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-900/80 dark:text-white dark:placeholder-zinc-500"
			/>
		</div>
	</div>

	<!-- Category Filter Tabs -->
	<div
		class="flex flex-wrap items-center gap-2 border-b border-zinc-200/80 pb-4 dark:border-white/[0.08]"
	>
		<button
			type="button"
			onclick={() => (activeCategory = 'all')}
			class="rounded-xl px-3.5 py-1.5 text-xs font-semibold transition-all {activeCategory === 'all'
				? 'border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400'
				: 'text-zinc-600 hover:bg-zinc-100 dark:text-zinc-400 dark:hover:bg-zinc-800'}"
		>
			{t('calculations.categories.all')}
		</button>
		<button
			type="button"
			onclick={() => (activeCategory = 'water')}
			class="rounded-xl px-3.5 py-1.5 text-xs font-semibold transition-all {activeCategory ===
			'water'
				? 'border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400'
				: 'text-zinc-600 hover:bg-zinc-100 dark:text-zinc-400 dark:hover:bg-zinc-800'}"
		>
			{t('calculations.categories.water')}
		</button>
		<button
			type="button"
			onclick={() => (activeCategory = 'gravity')}
			class="rounded-xl px-3.5 py-1.5 text-xs font-semibold transition-all {activeCategory ===
			'gravity'
				? 'border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400'
				: 'text-zinc-600 hover:bg-zinc-100 dark:text-zinc-400 dark:hover:bg-zinc-800'}"
		>
			{t('calculations.categories.gravity')}
		</button>
		<button
			type="button"
			onclick={() => (activeCategory = 'finishing')}
			class="rounded-xl px-3.5 py-1.5 text-xs font-semibold transition-all {activeCategory ===
			'finishing'
				? 'border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400'
				: 'text-zinc-600 hover:bg-zinc-100 dark:text-zinc-400 dark:hover:bg-zinc-800'}"
		>
			{t('calculations.categories.finishing')}
		</button>
	</div>

	<!-- Calculators Cards Grid -->
	<div class="grid grid-cols-1 gap-6 lg:grid-cols-2">
		{#each filteredCalculators as calc (calc.id)}
			{@const Component = calc.component}
			<Component />
		{/each}
	</div>
</div>
