<script lang="ts">
	import type { IngredientDto, IngredientType, IngredientUsage } from '$lib/types/api';
	import { t } from '$lib/i18n/index.svelte';
	import { getLocalizedIngredientName } from '$lib/i18n/ingredients';
	import { srmToHexColor, tinsethIbu, formatPotentialGravity } from '$lib/calculators/brewing';
	import { settings } from '$lib/stores/settings.svelte';
	import { formatNumber } from '$lib/utils/formatNumber';
	import { Plus, Trash2, Wheat, Flower2, Dna, Sparkles } from '@lucide/svelte';
	import HopScheduleVisualizer from '$lib/components/brewery/HopScheduleVisualizer.svelte';

	export interface FormIngredientItem {
		id: string;
		ingredientId: string;
		type?: IngredientType;
		amount: number;
		unit: string;
		durationMinutes: number;
		usage: IngredientUsage;
		notes?: string;
		form?: string | null;
	}

	interface Props {
		items: FormIngredientItem[];
		catalogIngredients: IngredientDto[];
		batchSizeLiters: number;
		efficiencyPercent: number;
		onAddItem: (type: IngredientType) => void;
		onRemoveItem: (id: string) => void;
	}

	let {
		items = $bindable(),
		catalogIngredients,
		batchSizeLiters,
		efficiencyPercent,
		onAddItem,
		onRemoveItem
	}: Props = $props();

	// Group items by ingredient type
	let fermentables = $derived(
		items.filter((item) => {
			const ing = catalogIngredients.find((i) => i.id === item.ingredientId);
			const type = ing?.type ?? item.type ?? 'Fermentable';
			return type === 'Fermentable';
		})
	);

	let hops = $derived(
		items.filter((item) => {
			const ing = catalogIngredients.find((i) => i.id === item.ingredientId);
			const type = ing?.type ?? item.type;
			return type === 'Hop';
		})
	);

	let yeasts = $derived(
		items.filter((item) => {
			const ing = catalogIngredients.find((i) => i.id === item.ingredientId);
			const type = ing?.type ?? item.type;
			return type === 'Yeast';
		})
	);

	let miscs = $derived(
		items.filter((item) => {
			const ing = catalogIngredients.find((i) => i.id === item.ingredientId);
			const type = ing?.type ?? item.type;
			return type === 'Other';
		})
	);

	// Totals
	let totalGrainWeight = $derived(
		fermentables.reduce((acc, curr) => acc + (Number(curr.amount) || 0), 0)
	);

	let totalHopWeight = $derived(hops.reduce((acc, curr) => acc + (Number(curr.amount) || 0), 0));

	// Rough OG estimate for hop IBU calculation
	let estimatedOg = $derived.by(() => {
		const totalPts = fermentables.reduce((acc, curr) => {
			const ing = catalogIngredients.find((i) => i.id === curr.ingredientId);
			const ppg = ing?.potentialGravity ? (ing.potentialGravity - 1.0) * 1000 : 36;
			const weightLbs = (Number(curr.amount) || 0) * 2.20462;
			return acc + weightLbs * ppg * (efficiencyPercent / 100);
		}, 0);
		const volGal = Math.max(0.1, batchSizeLiters * 0.264172);
		return 1.0 + totalPts / volGal / 1000;
	});

	let totalCalculatedIbu = $derived.by(() => {
		return hops.reduce((acc, curr) => {
			const ing = catalogIngredients.find((i) => i.id === curr.ingredientId);
			const alpha = ing?.alphaAcidPercent ?? 5.0;
			const weightGrams = Number(curr.amount) || 0;
			const time = Number(curr.durationMinutes) || 0;
			if (curr.usage === 'DryHop' || curr.usage === 'Secondary' || curr.usage === 'Bottling')
				return acc;
			return acc + tinsethIbu(weightGrams, alpha, time, estimatedOg, batchSizeLiters);
		}, 0);
	});

	let availableFermentables = $derived(catalogIngredients.filter((i) => i.type === 'Fermentable'));
	let availableHops = $derived(catalogIngredients.filter((i) => i.type === 'Hop'));
	let availableYeasts = $derived(catalogIngredients.filter((i) => i.type === 'Yeast'));
	let availableMiscs = $derived(catalogIngredients.filter((i) => i.type === 'Other'));
</script>

<div class="w-full space-y-6">
	<div class="border-b border-zinc-200/80 pb-3 dark:border-white/5">
		<h2 class="text-xl font-bold tracking-tight text-zinc-900 dark:text-white">
			{t('formulator.ingredients_bill')}
		</h2>
		<p class="text-xs text-zinc-500 dark:text-zinc-400">
			{t('formulator.subtitle')}
		</p>
	</div>

	<!-- SECTION 1: FERMENTABLES & MALTS (FULL WIDTH) -->
	<section
		class="glass-panel overflow-hidden rounded-2xl border border-amber-500/20 bg-amber-500/[0.02] dark:border-amber-500/15 dark:bg-zinc-900/40"
	>
		<!-- Section Header -->
		<div
			class="flex flex-col gap-3 border-b border-zinc-200/80 bg-zinc-50/70 px-5 py-3.5 sm:flex-row sm:items-center sm:justify-between dark:border-white/5 dark:bg-zinc-900/70"
		>
			<div class="flex items-center gap-2.5">
				<div
					class="flex h-8 w-8 items-center justify-center rounded-lg border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
				>
					<Wheat class="h-4 w-4" />
				</div>
				<div>
					<h3 class="font-editorial text-base font-bold text-zinc-900 dark:text-white">
						{t('formulator.sections.fermentables')}
					</h3>
					<span class="text-[11px] text-zinc-500 dark:text-zinc-400">
						{t('formulator.summaries.total_grain', {
							weight: settings.formatGrain(totalGrainWeight)
						})}
					</span>
				</div>
			</div>

			<button
				type="button"
				onclick={() => onAddItem('Fermentable')}
				class="flex h-9 cursor-pointer items-center gap-1.5 self-start rounded-xl border border-amber-500/30 bg-amber-500/10 px-3 text-xs font-bold text-amber-700 transition-colors hover:bg-amber-500/20 active:scale-95 sm:self-auto dark:text-amber-400"
			>
				<Plus class="h-3.5 w-3.5" />
				<span>{t('formulator.buttons.add_fermentable')}</span>
			</button>
		</div>

		<!-- Fermentables List -->
		{#if fermentables.length === 0}
			<div class="py-8 text-center text-xs text-zinc-400">
				{t('formulator.empty.fermentables')}
			</div>
		{:else}
			<div class="divide-y divide-zinc-200/60 dark:divide-white/5">
				{#each fermentables as item (item.id)}
					{@const ing = catalogIngredients.find((i) => i.id === item.ingredientId)}
					{@const gristPercent =
						totalGrainWeight > 0 ? ((Number(item.amount) || 0) / totalGrainWeight) * 100 : 0}
					<div
						class="flex flex-col items-stretch gap-3 p-4 transition-colors hover:bg-zinc-50/50 sm:flex-row sm:items-center dark:hover:bg-zinc-800/20"
					>
						<!-- Ingredient Select with Color Swatch -->
						<div class="flex flex-1 items-center gap-2">
							<div
								class="h-6 w-6 flex-shrink-0 rounded-md border border-zinc-300 shadow-inner dark:border-zinc-700"
								style="background-color: {srmToHexColor(ing?.colorSrm ?? 2.0)}"
								title={settings.formatColor(ing?.colorSrm ?? 2.0)}
							></div>
							<select
								bind:value={item.ingredientId}
								class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm font-medium text-zinc-900 transition-colors focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							>
								{#if item.ingredientId && !availableFermentables.some((f) => f.id === item.ingredientId)}
									<option value={item.ingredientId}>
										{ing ? getLocalizedIngredientName(ing) : item.notes || item.ingredientId}
									</option>
								{/if}
								{#each availableFermentables as f}
									<option value={f.id}>
										{getLocalizedIngredientName(f)} ({settings.formatColor(f.colorSrm)} • {formatPotentialGravity(
											f.potentialGravity
										)})
									</option>
								{/each}
							</select>
						</div>

						<!-- Amount Input & Grist % Bar -->
						<div class="flex items-center gap-3 sm:w-64">
							<div class="flex items-center gap-1.5">
								<input
									type="number"
									step="0.05"
									min="0"
									bind:value={item.amount}
									class="h-10 w-24 rounded-xl border border-zinc-200/80 bg-white px-2.5 text-right font-mono text-sm font-bold text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
								/>
								<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400">kg</span>
							</div>

							<!-- Visual Grist % Progress -->
							<div class="flex-1">
								<div class="flex justify-between text-[11px] font-semibold">
									<span class="text-zinc-400">{t('formulator.columns.grist_share')}</span>
									<span class="font-mono text-amber-600 dark:text-amber-400"
										>{formatNumber(gristPercent, 1)}%</span
									>
								</div>
								<div
									class="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-zinc-200 dark:bg-zinc-800"
								>
									<div
										class="h-full rounded-full bg-amber-500 transition-all duration-300"
										style="width: {Math.min(100, Math.max(0, gristPercent))}%"
									></div>
								</div>
							</div>
						</div>

						<!-- Usage Select -->
						<div class="w-full sm:w-32">
							<select
								bind:value={item.usage}
								class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-2.5 text-xs font-medium text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							>
								<option value="Mash">{t('formulator.usages.mash')}</option>
								<option value="Boil">{t('formulator.usages.boil')}</option>
							</select>
						</div>

						<!-- Delete Action -->
						<button
							type="button"
							onclick={() => onRemoveItem(item.id)}
							class="flex h-10 w-10 flex-shrink-0 cursor-pointer items-center justify-center rounded-xl border border-zinc-200/80 bg-white text-zinc-400 transition-colors hover:border-red-500/40 hover:bg-red-50 hover:text-red-600 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:hover:bg-red-950/50 dark:hover:text-red-400"
							aria-label={t('common.delete')}
						>
							<Trash2 class="h-4 w-4" />
						</button>
					</div>
				{/each}
			</div>
		{/if}
	</section>

	<!-- SECTION 2: HOPS & BITTERNESS (FULL WIDTH) -->
	<section
		class="glass-panel overflow-hidden rounded-2xl border border-emerald-500/20 bg-emerald-500/[0.02] dark:border-emerald-500/15 dark:bg-zinc-900/40"
	>
		<!-- Section Header -->
		<div
			class="flex flex-col gap-3 border-b border-zinc-200/80 bg-zinc-50/70 px-5 py-3.5 sm:flex-row sm:items-center sm:justify-between dark:border-white/5 dark:bg-zinc-900/70"
		>
			<div class="flex items-center gap-2.5">
				<div
					class="flex h-8 w-8 items-center justify-center rounded-lg border border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400"
				>
					<Flower2 class="h-4 w-4" />
				</div>
				<div>
					<h3 class="font-editorial text-base font-bold text-zinc-900 dark:text-white">
						{t('formulator.sections.hops')}
					</h3>
					<span class="text-[11px] text-zinc-500 dark:text-zinc-400">
						{t('formulator.summaries.total_hops', { weight: settings.formatHop(totalHopWeight) })} •
						{t('formulator.summaries.total_ibu', { ibu: formatNumber(totalCalculatedIbu, 1) })}
					</span>
				</div>
			</div>

			<button
				type="button"
				onclick={() => onAddItem('Hop')}
				class="flex h-9 cursor-pointer items-center gap-1.5 self-start rounded-xl border border-emerald-500/30 bg-emerald-500/10 px-3 text-xs font-bold text-emerald-700 transition-colors hover:bg-emerald-500/20 active:scale-95 sm:self-auto dark:text-emerald-400"
			>
				<Plus class="h-3.5 w-3.5" />
				<span>{t('formulator.buttons.add_hop')}</span>
			</button>
		</div>

		<!-- Chronological Boil & Hop Schedule Visualizer -->
		{#if hops.length > 0}
			<div class="px-5 pt-4">
				<HopScheduleVisualizer {hops} {catalogIngredients} {estimatedOg} {batchSizeLiters} />
			</div>
		{/if}

		<!-- Hops List -->
		{#if hops.length === 0}
			<div class="py-8 text-center text-xs text-zinc-400">
				{t('formulator.empty.hops')}
			</div>
		{:else}
			<div class="divide-y divide-zinc-200/60 dark:divide-white/5">
				{#each hops as item (item.id)}
					{@const ing = catalogIngredients.find((i) => i.id === item.ingredientId)}
					{@const alpha = ing?.alphaAcidPercent ?? 5.0}
					{@const estIbu =
						item.usage === 'DryHop' || item.usage === 'Secondary' || item.usage === 'Bottling'
							? 0
							: tinsethIbu(
									Number(item.amount) || 0,
									alpha,
									Number(item.durationMinutes) || 0,
									estimatedOg,
									batchSizeLiters
								)}
					<div
						class="flex flex-col items-stretch gap-3 p-4 transition-colors hover:bg-zinc-50/50 sm:flex-row sm:items-center dark:hover:bg-zinc-800/20"
					>
						<!-- Hop Select -->
						<div class="flex-1">
							<select
								bind:value={item.ingredientId}
								onchange={() => {
									const selected = catalogIngredients.find((i) => i.id === item.ingredientId);
									if (selected?.form) {
										item.form = selected.form;
									}
								}}
								class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm font-medium text-zinc-900 transition-colors focus:border-emerald-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							>
								{#if item.ingredientId && !availableHops.some((h) => h.id === item.ingredientId)}
									<option value={item.ingredientId}>
										{ing ? getLocalizedIngredientName(ing) : item.notes || item.ingredientId}
									</option>
								{/if}
								{#each availableHops as h}
									<option value={h.id}>
										{getLocalizedIngredientName(h)} ({h.alphaAcidPercent ?? 5}% AA)
									</option>
								{/each}
							</select>
						</div>

						<!-- Form Select -->
						<div class="w-full sm:w-28">
							<select
								bind:value={item.form}
								class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-2.5 text-xs font-medium text-zinc-900 focus:border-emerald-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							>
								<option value="Pellet">{t('formulator.forms.pellet')}</option>
								<option value="Leaf">{t('formulator.forms.leaf')}</option>
								<option value="Plug">{t('formulator.forms.plug')}</option>
							</select>
						</div>

						<!-- Amount Input (Grams!) -->
						<div class="flex items-center gap-1.5 sm:w-28">
							<input
								type="number"
								step="1"
								min="0"
								bind:value={item.amount}
								class="h-10 w-20 rounded-xl border border-zinc-200/80 bg-white px-2.5 text-right font-mono text-sm font-bold text-zinc-900 focus:border-emerald-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							/>
							<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400">g</span>
						</div>

						<!-- Duration (Minutes) -->
						<div class="flex items-center gap-1.5 sm:w-28">
							<input
								type="number"
								step="5"
								min="0"
								bind:value={item.durationMinutes}
								class="h-10 w-18 rounded-xl border border-zinc-200/80 bg-white px-2.5 text-right font-mono text-sm font-bold text-zinc-900 focus:border-emerald-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							/>
							<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400">min</span>
						</div>

						<!-- Usage Select -->
						<div class="w-full sm:w-36">
							<select
								bind:value={item.usage}
								class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-2.5 text-xs font-medium text-zinc-900 focus:border-emerald-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							>
								<option value="Boil">{t('formulator.usages.boil')}</option>
								<option value="FirstWort">{t('formulator.usages.first_wort')}</option>
								<option value="Whirlpool">{t('formulator.usages.whirlpool')}</option>
								<option value="DryHop">{t('formulator.usages.dry_hop')}</option>
								<option value="Mash">{t('formulator.usages.mash')}</option>
							</select>
						</div>

						<!-- Calculated IBU Contribution Badge -->
						<div
							class="flex h-10 w-24 items-center justify-center rounded-xl border border-emerald-500/20 bg-emerald-500/5 px-2 font-mono text-xs font-bold text-emerald-600 dark:text-emerald-400"
						>
							{estIbu > 0 ? `${formatNumber(estIbu, 1)} IBU` : 'Aroma / 0 IBU'}
						</div>

						<!-- Delete Action -->
						<button
							type="button"
							onclick={() => onRemoveItem(item.id)}
							class="flex h-10 w-10 flex-shrink-0 cursor-pointer items-center justify-center rounded-xl border border-zinc-200/80 bg-white text-zinc-400 transition-colors hover:border-red-500/40 hover:bg-red-50 hover:text-red-600 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:hover:bg-red-950/50 dark:hover:text-red-400"
							aria-label={t('common.delete')}
						>
							<Trash2 class="h-4 w-4" />
						</button>
					</div>
				{/each}
			</div>
		{/if}
	</section>

	<!-- SECTION 3: YEASTS & FERMENTATION (FULL WIDTH) -->
	<section
		class="glass-panel overflow-hidden rounded-2xl border border-sky-500/20 bg-sky-500/[0.02] dark:border-sky-500/15 dark:bg-zinc-900/40"
	>
		<!-- Section Header -->
		<div
			class="flex flex-col gap-3 border-b border-zinc-200/80 bg-zinc-50/70 px-5 py-3.5 sm:flex-row sm:items-center sm:justify-between dark:border-white/5 dark:bg-zinc-900/70"
		>
			<div class="flex items-center gap-2.5">
				<div
					class="flex h-8 w-8 items-center justify-center rounded-lg border border-sky-500/30 bg-sky-500/10 text-sky-600 dark:text-sky-400"
				>
					<Dna class="h-4 w-4" />
				</div>
				<div>
					<h3 class="font-editorial text-base font-bold text-zinc-900 dark:text-white">
						{t('formulator.sections.yeasts')}
					</h3>
					<span class="text-[11px] text-zinc-500 dark:text-zinc-400">
						{t('formulator.summaries.yeast_desc')}
					</span>
				</div>
			</div>

			<button
				type="button"
				onclick={() => onAddItem('Yeast')}
				class="flex h-9 cursor-pointer items-center gap-1.5 self-start rounded-xl border border-sky-500/30 bg-sky-500/10 px-3 text-xs font-bold text-sky-700 transition-colors hover:bg-sky-500/20 active:scale-95 sm:self-auto dark:text-sky-400"
			>
				<Plus class="h-3.5 w-3.5" />
				<span>{t('formulator.buttons.add_yeast')}</span>
			</button>
		</div>

		<!-- Yeasts List -->
		{#if yeasts.length === 0}
			<div class="py-8 text-center text-xs text-zinc-400">
				{t('formulator.empty.yeasts')}
			</div>
		{:else}
			<div class="divide-y divide-zinc-200/60 dark:divide-white/5">
				{#each yeasts as item (item.id)}
					{@const ing = catalogIngredients.find((i) => i.id === item.ingredientId)}
					<div
						class="flex flex-col items-stretch gap-3 p-4 transition-colors hover:bg-zinc-50/50 sm:flex-row sm:items-center dark:hover:bg-zinc-800/20"
					>
						<!-- Yeast Select -->
						<div class="flex-1">
							<select
								bind:value={item.ingredientId}
								onchange={() => {
									const selected = catalogIngredients.find((i) => i.id === item.ingredientId);
									if (selected?.form) {
										item.form = selected.form;
										if (selected.form === 'Liquid') {
											item.unit = 'pkg';
											item.amount = 1;
										} else {
											item.unit = 'g';
											item.amount = 11.5;
										}
									}
								}}
								class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm font-medium text-zinc-900 transition-colors focus:border-sky-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							>
								{#if item.ingredientId && !availableYeasts.some((y) => y.id === item.ingredientId)}
									<option value={item.ingredientId}>
										{ing ? getLocalizedIngredientName(ing) : item.notes || item.ingredientId}
									</option>
								{/if}
								{#each availableYeasts as y}
									<option value={y.id}>
										{getLocalizedIngredientName(y)} ({y.attenuationPercent ?? 75}% atten)
									</option>
								{/each}
							</select>
						</div>

						<!-- Form Select -->
						<div class="w-full sm:w-28">
							<select
								bind:value={item.form}
								onchange={() => {
									if (item.form === 'Liquid' && item.unit === 'g') {
										item.unit = 'pkg';
										if (item.amount === 11.5) item.amount = 1;
									} else if (item.form === 'Dry' && (item.unit === 'pkg' || item.unit === 'ml')) {
										item.unit = 'g';
										if (item.amount === 1) item.amount = 11.5;
									}
								}}
								class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-2.5 text-xs font-medium text-zinc-900 focus:border-sky-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							>
								<option value="Dry">{t('formulator.forms.dry')}</option>
								<option value="Liquid">{t('formulator.forms.liquid')}</option>
								<option value="Slant">{t('formulator.forms.slant')}</option>
								<option value="Culture">{t('formulator.forms.culture')}</option>
							</select>
						</div>

						<!-- Amount Input & Unit -->
						<div class="flex items-center gap-1 sm:w-32">
							<input
								type="number"
								step={item.unit === 'pkg' ? '1' : '0.5'}
								min="0"
								bind:value={item.amount}
								class="h-10 w-16 rounded-xl border border-zinc-200/80 bg-white px-2 text-right font-mono text-sm font-bold text-zinc-900 focus:border-sky-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							/>
							<select
								bind:value={item.unit}
								class="h-10 w-14 rounded-xl border border-zinc-200/80 bg-white px-1 text-xs font-semibold text-zinc-700 focus:border-sky-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300"
							>
								<option value="g">g</option>
								<option value="pkg">pkg</option>
								<option value="ml">ml</option>
							</select>
						</div>

						<!-- Attenuation Badge -->
						<div
							class="flex h-10 w-32 items-center justify-center rounded-xl border border-sky-500/20 bg-sky-500/5 px-2 font-mono text-xs font-bold text-sky-600 dark:text-sky-400"
						>
							{ing?.attenuationPercent ?? 75}% Atten
						</div>

						<!-- Usage Stage -->
						<div class="w-full sm:w-36">
							<select
								bind:value={item.usage}
								class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-2.5 text-xs font-medium text-zinc-900 focus:border-sky-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							>
								<option value="Primary">{t('formulator.usages.primary')}</option>
								<option value="Secondary">{t('formulator.usages.secondary')}</option>
								<option value="Bottling">{t('formulator.usages.bottling')}</option>
							</select>
						</div>

						<!-- Delete Action -->
						<button
							type="button"
							onclick={() => onRemoveItem(item.id)}
							class="flex h-10 w-10 flex-shrink-0 cursor-pointer items-center justify-center rounded-xl border border-zinc-200/80 bg-white text-zinc-400 transition-colors hover:border-red-500/40 hover:bg-red-50 hover:text-red-600 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:hover:bg-red-950/50 dark:hover:text-red-400"
							aria-label={t('common.delete')}
						>
							<Trash2 class="h-4 w-4" />
						</button>
					</div>
				{/each}
			</div>
		{/if}
	</section>

	<!-- SECTION 4: MISCELLANEOUS & WATER AGENTS (FULL WIDTH) -->
	<section
		class="glass-panel overflow-hidden rounded-2xl border border-purple-500/20 bg-purple-500/[0.02] dark:border-purple-500/15 dark:bg-zinc-900/40"
	>
		<!-- Section Header -->
		<div
			class="flex flex-col gap-3 border-b border-zinc-200/80 bg-zinc-50/70 px-5 py-3.5 sm:flex-row sm:items-center sm:justify-between dark:border-white/5 dark:bg-zinc-900/70"
		>
			<div class="flex items-center gap-2.5">
				<div
					class="flex h-8 w-8 items-center justify-center rounded-lg border border-purple-500/30 bg-purple-500/10 text-purple-600 dark:text-purple-400"
				>
					<Sparkles class="h-4 w-4" />
				</div>
				<div>
					<h3 class="font-editorial text-base font-bold text-zinc-900 dark:text-white">
						{t('formulator.sections.miscs')}
					</h3>
					<span class="text-[11px] text-zinc-500 dark:text-zinc-400">
						{t('formulator.summaries.items_count', { count: miscs.length })}
					</span>
				</div>
			</div>

			<button
				type="button"
				onclick={() => onAddItem('Other')}
				class="flex h-9 cursor-pointer items-center gap-1.5 self-start rounded-xl border border-purple-500/30 bg-purple-500/10 px-3 text-xs font-bold text-purple-700 transition-colors hover:bg-purple-500/20 active:scale-95 sm:self-auto dark:text-purple-400"
			>
				<Plus class="h-3.5 w-3.5" />
				<span>{t('formulator.buttons.add_misc')}</span>
			</button>
		</div>

		<!-- Miscs List -->
		{#if miscs.length === 0}
			<div class="py-8 text-center text-xs text-zinc-400">
				{t('formulator.empty.miscs')}
			</div>
		{:else}
			<div class="divide-y divide-zinc-200/60 dark:divide-white/5">
				{#each miscs as item (item.id)}
					{@const ing = catalogIngredients.find((i) => i.id === item.ingredientId)}
					<div
						class="flex flex-col items-stretch gap-3 p-4 transition-colors hover:bg-zinc-50/50 sm:flex-row sm:items-center dark:hover:bg-zinc-800/20"
					>
						<!-- Misc Select -->
						<div class="flex-1">
							<select
								bind:value={item.ingredientId}
								class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm font-medium text-zinc-900 transition-colors focus:border-purple-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							>
								{#if item.ingredientId && !availableMiscs.some((m) => m.id === item.ingredientId)}
									<option value={item.ingredientId}>
										{ing ? getLocalizedIngredientName(ing) : item.notes || item.ingredientId}
									</option>
								{/if}
								{#each availableMiscs as m}
									<option value={m.id}>
										{getLocalizedIngredientName(m)}
									</option>
								{/each}
							</select>
						</div>

						<!-- Amount Input -->
						<div class="flex items-center gap-1.5 sm:w-28">
							<input
								type="number"
								step="1"
								min="0"
								bind:value={item.amount}
								class="h-10 w-20 rounded-xl border border-zinc-200/80 bg-white px-2.5 text-right font-mono text-sm font-bold text-zinc-900 focus:border-purple-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							/>
							<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400"
								>{item.unit || 'g'}</span
							>
						</div>

						<!-- Timing (Minutes) -->
						<div class="flex items-center gap-1.5 sm:w-28">
							<input
								type="number"
								step="5"
								min="0"
								bind:value={item.durationMinutes}
								class="h-10 w-18 rounded-xl border border-zinc-200/80 bg-white px-2.5 text-right font-mono text-sm font-bold text-zinc-900 focus:border-purple-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							/>
							<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400">min</span>
						</div>

						<!-- Usage Stage -->
						<div class="w-full sm:w-36">
							<select
								bind:value={item.usage}
								class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-2.5 text-xs font-medium text-zinc-900 focus:border-purple-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							>
								<option value="Boil">{t('formulator.usages.boil')}</option>
								<option value="Mash">{t('formulator.usages.mash')}</option>
								<option value="Primary">{t('formulator.usages.primary')}</option>
								<option value="Secondary">{t('formulator.usages.secondary')}</option>
								<option value="Bottling">{t('formulator.usages.bottling')}</option>
							</select>
						</div>

						<!-- Delete Action -->
						<button
							type="button"
							onclick={() => onRemoveItem(item.id)}
							class="flex h-10 w-10 flex-shrink-0 cursor-pointer items-center justify-center rounded-xl border border-zinc-200/80 bg-white text-zinc-400 transition-colors hover:border-red-500/40 hover:bg-red-50 hover:text-red-600 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:hover:bg-red-950/50 dark:hover:text-red-400"
							aria-label={t('common.delete')}
						>
							<Trash2 class="h-4 w-4" />
						</button>
					</div>
				{/each}
			</div>
		{/if}
	</section>
</div>
