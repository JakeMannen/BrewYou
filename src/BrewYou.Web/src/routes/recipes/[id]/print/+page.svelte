<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { api, ApiClientError } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import {
		getLocalizedFermentableCategory,
		getLocalizedIngredientName,
		getLocalizedIngredientForm
	} from '$lib/i18n/ingredients';
	import { formatNumber } from '$lib/utils/formatNumber';
	import { settings, toDisplayVolume, fromDisplayVolume } from '$lib/stores/settings.svelte';
	import {
		calculateApparentAttenuation,
		calculateBuGuRatio,
		isMashGrain,
		normalizeFermentableToKg,
		normalizeHopToGrams,
		resolveFermentableCategory
	} from '$lib/calculators/brewing';
	import { estimateEquipmentStrikeTempC } from '$lib/calculators/calculators';
	import {
		buildPrintableBoilSchedule,
		formatPrintableStageTiming,
		splitPrintableStageIngredients,
		type PrintableBoilRow,
		type PrintableTimingLabels
	} from '$lib/utils/recipePrint';
	import {
		calculateWaterRequirements,
		scaleGrainWeightKg,
		type TargetVolumeBasis
	} from '$lib/calculators/volume';
	import type { EquipmentDto, RecipeDetailDto, RecipeIngredientOutputDto } from '$lib/types/api';
	import { Printer, ArrowLeft, Play, Loader2, Info } from '@lucide/svelte';

	let recipeId = $derived(page.params.id);
	let recipe = $state<RecipeDetailDto | null>(null);
	let equipment = $state<EquipmentDto[]>([]);
	let loading = $state(true);
	let errorMessage = $state<string | null>(null);

	// Configurable scaling controls
	let targetBatchSizeLiters = $state<number>(20.0);
	let targetVolumeBasis = $state<TargetVolumeBasis>('fermenter');
	let selectedBoilerId = $state<string>('');

	let boilers = $derived(equipment.filter((e) => e.type === 'Boiler'));
	let fermenters = $derived(equipment.filter((e) => e.type === 'Fermenter'));
	let selectedBoiler = $derived(boilers.find((b) => b.id === selectedBoilerId) ?? null);
	let defaultFermenterLoss = $derived(fermenters[0]?.trubLossLiters ?? 1.5);

	let volumePresetsLiters = $derived(
		settings.volumeUnit === 'Gallons'
			? [
					{ label: '2.5 gal', liters: 9.46 },
					{ label: '5 gal', liters: 18.93 },
					{ label: '6 gal', liters: 22.71 },
					{ label: '10 gal', liters: 37.85 },
					{ label: '15 gal', liters: 56.78 }
				]
			: [
					{ label: '10 L', liters: 10 },
					{ label: '19 L', liters: 19 },
					{ label: '20 L', liters: 20 },
					{ label: '23 L', liters: 23 },
					{ label: '25 L', liters: 25 },
					{ label: '30 L', liters: 30 },
					{ label: '50 L', liters: 50 }
				]
	);

	let displayBatchSize = $derived(toDisplayVolume(targetBatchSizeLiters, settings.volumeUnit));

	function handleBatchSizeInput(e: Event) {
		const raw = Number((e.currentTarget as HTMLInputElement).value);
		if (!Number.isNaN(raw) && raw > 0) {
			targetBatchSizeLiters = Math.max(0.5, fromDisplayVolume(raw, settings.volumeUnit));
		}
	}

	let baseRecipeGrainKg = $derived.by(() => {
		if (!recipe) return 0;
		return recipe.ingredients
			.filter((i) =>
				isMashGrain(i.ingredientType, i.usage, i.fermentableCategory, i.ingredientName)
			)
			.reduce((sum, i) => sum + normalizeFermentableToKg(i.amount, i.unit), 0);
	});

	// Scale grain weight proportionally to the target fermenter volume so grain absorption matches the batch size
	let provisionalGrainKg = $derived(
		scaleGrainWeightKg(
			baseRecipeGrainKg,
			recipe?.batchSizeLiters ?? 20.0,
			targetBatchSizeLiters,
			targetVolumeBasis,
			defaultFermenterLoss,
			selectedBoiler?.packagingLossLiters ?? 0.5
		)
	);

	let isAllInOneBoiler = $derived(
		selectedBoiler !== null &&
			(selectedBoiler.subtype === 'AllInOne' || selectedBoiler.subtype === 'Pan')
	);

	let waterSchedule = $derived.by(() => {
		const boilTime = recipe?.boilTimeMinutes ?? 60;
		return calculateWaterRequirements({
			targetBatchSizeLiters,
			targetBasis: targetVolumeBasis,
			spargeEnabled: true,
			boilTimeMinutes: boilTime,
			totalGrainWeightKg: Math.max(0.1, provisionalGrainKg),
			boilOffRatePerHour: selectedBoiler?.boilOffRatePerHour ?? 3.0,
			kettleTrubLossLiters: selectedBoiler?.trubLossLiters ?? 1.5,
			mashTunDeadSpaceLiters: selectedBoiler?.mashTunDeadSpaceLiters ?? 0.0,
			fermenterLossLiters: defaultFermenterLoss,
			packagingLossLiters: selectedBoiler?.packagingLossLiters ?? 0.5,
			coolingShrinkagePercent: isAllInOneBoiler ? 0.0 : 4.0,
			grainAbsorptionRate: isAllInOneBoiler ? 0.8 : 0.96,
			mashThicknessLitersPerKg: isAllInOneBoiler ? 2.7 : 3.0,
			recoverMashTunDeadSpace: isAllInOneBoiler
		});
	});

	// Scale recipe ingredients to the target fermenter volume
	let scaleFactor = $derived.by(() => {
		if (!recipe || recipe.batchSizeLiters <= 0) return 1;
		return Math.max(0.05, waterSchedule.targetFermenterVolumeLiters / recipe.batchSizeLiters);
	});

	let totalScaledGrainKg = $derived(baseRecipeGrainKg * scaleFactor);

	let targetMashTempC = $derived.by(() => {
		if (recipe?.mashSteps && recipe.mashSteps.length > 0) {
			return [...recipe.mashSteps].sort((a, b) => a.stepOrder - b.stepOrder)[0].temperatureC;
		}
		return 67.0;
	});

	let targetSpargeTempC = $derived.by(() => {
		if (recipe?.mashSteps && recipe.mashSteps.length > 0) {
			const mashOut = [...recipe.mashSteps]
				.reverse()
				.find(
					(s) =>
						/mash\s*out|utmäsk|sparge|lak/i.test(s.name) ||
						(s.temperatureC >= 74 && s.temperatureC <= 80)
				);
			if (mashOut) return mashOut.temperatureC;
		}
		return 76.0;
	});

	let estimatedStrikeTempC = $derived.by(() => {
		const ratio =
			totalScaledGrainKg > 0 && waterSchedule.strikeWaterLiters > 0
				? waterSchedule.strikeWaterLiters / totalScaledGrainKg
				: 3.0;
		return estimateEquipmentStrikeTempC({
			targetMashTempC,
			liquorToGristRatio: ratio,
			mashVesselSubtype: selectedBoiler?.subtype
		});
	});

	let buGuRatio = $derived(
		recipe ? calculateBuGuRatio(recipe.originalGravity, recipe.bitternessIbu) : 0
	);

	let apparentAttenuation = $derived(
		recipe ? calculateApparentAttenuation(recipe.originalGravity, recipe.finalGravity) : 0
	);

	let timingLabels = $derived<PrintableTimingLabels>({
		rampLabel: t('batches.workspace.boil_step_ramp'),
		whirlpoolLabel: t('batches.workspace.boil_step_whirlpool'),
		flameoutLabel: t('batches.workspace.boil_step_flameout'),
		minutesShortLabel: t('common.minutes_short'),
		firstWortDurationText: t('formulator.usages.first_wort'),
		rampDurationText: 'Ramp',
		usages: {
			Mash: t('formulator.usages.mash'),
			FirstWort: t('formulator.usages.first_wort'),
			Boil: t('formulator.usages.boil'),
			Whirlpool: t('formulator.usages.whirlpool'),
			Primary: t('formulator.usages.primary'),
			Secondary: t('formulator.usages.secondary'),
			DryHop: t('formulator.usages.dry_hop'),
			Bottling: t('formulator.usages.bottling')
		}
	});

	// Stage-grouped ingredients
	let stageIngredients = $derived(
		recipe
			? splitPrintableStageIngredients(recipe.ingredients)
			: {
					fermentableIngredients: [],
					mashNonFermentableIngredients: [],
					fermentationIngredients: [],
					packagingIngredients: []
				}
	);

	let fermentableIngredients = $derived(stageIngredients.fermentableIngredients);
	let mashNonFermentableIngredients = $derived(stageIngredients.mashNonFermentableIngredients);
	let fermentationIngredients = $derived(stageIngredients.fermentationIngredients);

	let boilScheduleRows = $derived.by<PrintableBoilRow[]>(() => {
		if (!recipe) return [];
		return buildPrintableBoilSchedule(
			recipe.ingredients,
			recipe.boilTimeMinutes || 60,
			timingLabels
		);
	});

	function formatScaledAmount(item: RecipeIngredientOutputDto): string {
		if (item.ingredientType === 'Fermentable') {
			const kg = normalizeFermentableToKg(item.amount, item.unit) * scaleFactor;
			return settings.formatGrainWeight(kg, 2);
		}
		if (item.ingredientType === 'Hop') {
			const grams = normalizeHopToGrams(item.amount, item.unit) * scaleFactor;
			return settings.formatHopWeight(grams, 1);
		}
		const scaled = item.amount * scaleFactor;
		const decimals = scaled < 10 ? 2 : 1;
		return `${formatNumber(scaled, decimals)} ${item.unit}`;
	}

	function formatIngredientLabel(item: RecipeIngredientOutputDto): string {
		const name = getLocalizedIngredientName({ name: item.ingredientName });
		const form = item.form ? getLocalizedIngredientForm(item.form) : '';
		if (form) return `${name} (${form})`;
		if (item.ingredientType === 'Fermentable') {
			const category = resolveFermentableCategory(item.fermentableCategory, item.ingredientName);
			if (category !== 'Grain') {
				return `${name} (${getLocalizedFermentableCategory(category)})`;
			}
		}
		return name;
	}

	function handlePrint() {
		if (typeof window !== 'undefined') {
			window.print();
		}
	}

	const gravityLogRows = Array.from({ length: 8 }, (_, idx) => idx + 1);

	onMount(async () => {
		if (!recipeId) {
			loading = false;
			return;
		}
		try {
			const [loadedRecipe, loadedEquipment] = await Promise.all([
				api.recipes.getById(recipeId),
				api.equipment.list().catch(() => [] as EquipmentDto[])
			]);
			recipe = loadedRecipe;
			equipment = loadedEquipment;
			targetBatchSizeLiters = loadedRecipe.batchSizeLiters || 20.0;
			const loadedBoilers = loadedEquipment.filter((e) => e.type === 'Boiler');
			if (loadedBoilers.length > 0 && !selectedBoilerId) {
				selectedBoilerId = loadedBoilers[0].id;
			}
		} catch (err) {
			if (err instanceof ApiClientError) {
				errorMessage = err.message;
			} else {
				errorMessage = err instanceof Error ? err.message : t('common.error_generic');
			}
		} finally {
			loading = false;
		}
	});
</script>

<svelte:head>
	<title
		>{recipe
			? `${recipe.name} — ${t('recipes.print_sheet.title')}`
			: t('recipes.print_sheet.title')} | BrewYou</title
	>
</svelte:head>

<div
	class="print-container min-h-screen bg-zinc-100 p-4 font-sans text-zinc-900 sm:p-6 dark:bg-zinc-950 dark:text-zinc-100 print:min-h-0 print:bg-white print:p-0 print:text-zinc-950"
>
	<!-- Screen-Only Configuration Toolbar -->
	<header
		class="no-print glass-panel mx-auto mb-6 max-w-5xl rounded-2xl border border-zinc-200/80 p-4 shadow-sm dark:border-white/[0.08]"
	>
		<div class="flex flex-wrap items-center justify-between gap-4">
			<div class="flex items-center gap-3">
				<a
					href="/recipes/{recipeId}"
					data-testid="back-to-recipe-btn"
					class="flex h-9 w-9 items-center justify-center rounded-xl border border-zinc-200 bg-white text-zinc-600 hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300"
					aria-label={t('recipes.print_sheet.back_to_recipe')}
				>
					<ArrowLeft class="h-4 w-4" />
				</a>
				<div>
					<h1 class="text-base font-bold text-zinc-900 dark:text-white">
						{recipe?.name ?? ''} — {t('recipes.print_sheet.title')}
					</h1>
					<p class="text-xs text-zinc-500 dark:text-zinc-400">
						{t('recipes.print_sheet.subtitle')}
					</p>
				</div>
			</div>

			<div class="flex flex-wrap items-center gap-2.5">
				{#if recipe}
					<a
						href="/batches/new?recipeId={recipe.id}&batchSize={targetBatchSizeLiters.toFixed(
							2
						)}&basis={targetVolumeBasis}"
						data-testid="print-start-batch-btn"
						class="flex items-center gap-2 rounded-xl border border-amber-500/40 bg-amber-500/10 px-4 py-2 text-xs font-bold text-amber-800 transition-colors hover:bg-amber-500/20 dark:text-amber-300"
					>
						<Play class="h-3.5 w-3.5 fill-current" />
						<span>{t('recipes.print_sheet.start_batch_btn')}</span>
					</a>
				{/if}
				<button
					type="button"
					onclick={handlePrint}
					data-testid="trigger-print-btn"
					class="flex cursor-pointer items-center gap-2 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-5 py-2 text-xs font-bold text-zinc-950 shadow-md transition-all hover:from-amber-400 hover:to-amber-500 active:scale-95"
				>
					<Printer class="h-4 w-4" />
					<span>{t('recipes.print_sheet.print_btn')}</span>
				</button>
			</div>
		</div>

		{#if recipe}
			<div
				class="mt-4 grid grid-cols-1 gap-4 border-t border-zinc-200/70 pt-4 text-xs sm:grid-cols-3 dark:border-white/10"
			>
				<!-- Target Batch Size + Quick Presets -->
				<div class="space-y-2">
					<label
						for="print-batch-size"
						class="block font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('recipes.print_sheet.batch_size_label')} ({settings.unitShort})
					</label>
					<div class="flex items-center gap-2">
						<input
							id="print-batch-size"
							data-testid="print-batch-size-input"
							type="number"
							step="0.5"
							min="0.5"
							value={displayBatchSize}
							oninput={handleBatchSizeInput}
							class="w-28 rounded-xl border border-zinc-300 bg-white px-3 py-1.5 font-mono text-sm font-bold text-zinc-900 dark:border-zinc-700 dark:bg-zinc-900 dark:text-white"
						/>
						<span class="font-mono text-zinc-500 dark:text-zinc-400">{settings.unitShort}</span>
					</div>
					<div class="flex flex-wrap gap-1.5">
						{#each volumePresetsLiters as preset (preset.label)}
							<button
								type="button"
								onclick={() => (targetBatchSizeLiters = preset.liters)}
								class="cursor-pointer rounded-lg border px-2 py-0.5 font-mono text-[11px] font-semibold transition-colors {Math.abs(
									targetBatchSizeLiters - preset.liters
								) < 0.2
									? 'border-amber-500 bg-amber-500/15 text-amber-800 dark:text-amber-300'
									: 'border-zinc-200 bg-white text-zinc-600 hover:border-zinc-400 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-300'}"
							>
								{preset.label}
							</button>
						{/each}
					</div>
				</div>

				<!-- Volume Basis -->
				<div class="space-y-2">
					<label
						for="print-volume-basis"
						class="block font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('recipes.print_sheet.volume_basis_label')}
					</label>
					<select
						id="print-volume-basis"
						data-testid="print-volume-basis-select"
						bind:value={targetVolumeBasis}
						class="w-full rounded-xl border border-zinc-300 bg-white px-3 py-1.5 text-xs font-medium text-zinc-900 dark:border-zinc-700 dark:bg-zinc-900 dark:text-white"
					>
						<option value="fermenter">{t('recipes.print_sheet.fermenter_basis')}</option>
						<option value="packaged">{t('recipes.print_sheet.packaged_basis')}</option>
					</select>
					{#if Math.abs(scaleFactor - 1) > 0.01}
						<p class="font-mono text-[11px] font-semibold text-amber-700 dark:text-amber-400">
							{t('recipes.print_sheet.scaled_notice', {
								original: settings.formatVolume(recipe.batchSizeLiters),
								target: settings.formatVolume(waterSchedule.targetFermenterVolumeLiters),
								factor: formatNumber(scaleFactor, 2)
							})}
						</p>
					{/if}
				</div>

				<!-- Optional Equipment Profile -->
				<div class="space-y-2">
					<label
						for="print-equipment-select"
						class="block font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('recipes.print_sheet.equipment_label')}
					</label>
					<select
						id="print-equipment-select"
						data-testid="print-equipment-select"
						bind:value={selectedBoilerId}
						class="w-full rounded-xl border border-zinc-300 bg-white px-3 py-1.5 text-xs font-medium text-zinc-900 dark:border-zinc-700 dark:bg-zinc-900 dark:text-white"
					>
						<option value="">{t('recipes.print_sheet.equipment_default')}</option>
						{#each boilers as b (b.id)}
							<option value={b.id}>{b.name} ({settings.formatVolume(b.capacityLiters, 0)})</option>
						{/each}
					</select>
				</div>
			</div>

			<div
				class="mt-3 flex items-center gap-2 rounded-xl border border-sky-500/25 bg-sky-500/10 px-3 py-2 text-[11px] text-sky-900 dark:text-sky-200"
			>
				<Info class="h-3.5 w-3.5 shrink-0 text-sky-600 dark:text-sky-400" />
				<span>{t('recipes.print_sheet.retroactive_tip')}</span>
			</div>
		{/if}
	</header>

	{#if loading}
		<div class="flex h-64 items-center justify-center">
			<Loader2 class="h-8 w-8 animate-spin text-amber-500" />
		</div>
	{:else if errorMessage}
		<div
			class="mx-auto max-w-5xl rounded-2xl border border-red-500/20 bg-red-500/10 p-4 text-xs text-red-600 dark:text-red-400"
		>
			{errorMessage}
		</div>
	{:else if recipe}
		<!-- High-Contrast Printable Brew Sheet -->
		<main
			data-testid="printable-brew-sheet"
			class="brew-sheet-canvas mx-auto max-w-5xl space-y-5 rounded-2xl border border-zinc-300 bg-white p-6 text-zinc-950 shadow-md print:max-w-none print:space-y-1 print:rounded-none print:border-0 print:p-0 print:shadow-none"
		>
			<!-- Sheet Header -->
			<section class="sheet-section border-b-2 border-zinc-900 pb-3 print:pb-1.5">
				<div class="flex flex-wrap items-start justify-between gap-4 print:gap-2">
					<div>
						<div class="text-[10px] font-bold tracking-widest text-zinc-500 uppercase">
							BrewYou — {t('recipes.print_sheet.title')}
						</div>
						<h2 class="mt-0.5 text-2xl font-extrabold text-zinc-950 print:text-lg">
							{recipe.name}
						</h2>
						<div
							class="mt-1 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-zinc-700 print:mt-0.5 print:text-[10px]"
						>
							<span><strong>{recipe.beerStyle}</strong></span>
							{#if recipe.authorName}
								<span>{t('recipes.by_author', { author: recipe.authorName })}</span>
							{/if}
							<span>
								{t('recipes.print_sheet.boil_time_label')}:
								<strong class="font-mono">{recipe.boilTimeMinutes} min</strong>
							</span>
							<span>
								{t('recipes.print_sheet.efficiency_label')}:
								<strong class="font-mono">{formatNumber(recipe.efficiencyPercent, 0)}%</strong>
							</span>
						</div>
					</div>
					<div
						class="rounded-lg border-2 border-zinc-900 px-3 py-1.5 text-right print:px-2 print:py-0.5"
					>
						<div class="text-[10px] font-bold tracking-wider text-zinc-600 uppercase">
							{t('recipes.print_sheet.batch_size_label')}
						</div>
						<div class="font-mono text-lg font-extrabold text-zinc-950 print:text-sm">
							{settings.formatVolume(waterSchedule.targetFermenterVolumeLiters, 1)}
						</div>
						{#if targetVolumeBasis === 'packaged'}
							<div class="font-mono text-[10px] text-zinc-600">
								{t('recipes.print_sheet.packaged_volume')}:
								{settings.formatVolume(waterSchedule.targetPackagedVolumeLiters, 1)}
							</div>
						{/if}
					</div>
				</div>

				<!-- Write-in Batch Identity Fields -->
				<div
					data-testid="print-identity-grid"
					class="mt-3 grid grid-cols-2 gap-3 border-t border-zinc-300 pt-3 text-xs sm:grid-cols-4 print:mt-1.5 print:grid-cols-4 print:gap-2 print:pt-1.5"
				>
					<div>
						<span class="block text-[10px] font-bold tracking-wider text-zinc-500 uppercase">
							{t('recipes.print_sheet.brew_date_label')}
						</span>
						<div class="writein-line mt-1 h-6 border-b border-dashed border-zinc-500"></div>
					</div>
					<div>
						<span class="block text-[10px] font-bold tracking-wider text-zinc-500 uppercase">
							{t('recipes.print_sheet.batch_code_label')}
						</span>
						<div class="writein-line mt-1 h-6 border-b border-dashed border-zinc-500"></div>
					</div>
					<div>
						<span class="block text-[10px] font-bold tracking-wider text-zinc-500 uppercase">
							{t('recipes.print_sheet.brewer_label')}
						</span>
						<div class="writein-line mt-1 h-6 border-b border-dashed border-zinc-500"></div>
					</div>
					<div>
						<span class="block text-[10px] font-bold tracking-wider text-zinc-500 uppercase">
							{t('recipes.print_sheet.equipment_used_label')}
						</span>
						<div
							class="writein-line mt-1 flex h-6 items-end border-b border-dashed border-zinc-500 text-xs"
						>
							{selectedBoiler ? selectedBoiler.name : ''}
						</div>
					</div>
				</div>
			</section>

			<!-- Vitals + Water & Volume Milestones Side-by-Side -->
			<section
				class="sheet-section grid grid-cols-1 gap-4 md:grid-cols-2 print:grid-cols-2 print:gap-2"
			>
				<!-- Vital Targets vs Measured -->
				<div
					data-testid="print-vitals-section"
					class="rounded-xl border border-zinc-300 p-3 print:rounded-none print:p-1.5"
				>
					<h3 class="mb-2 border-b border-zinc-200 pb-1 text-xs font-bold tracking-wider uppercase">
						{t('recipes.print_sheet.vital_targets_title')}
					</h3>
					<table class="w-full text-xs">
						<thead>
							<tr class="border-b border-zinc-200 text-left text-[10px] text-zinc-500 uppercase">
								<th class="py-1">Metric</th>
								<th class="py-1">{t('recipes.print_sheet.target_col')}</th>
								<th class="w-32 py-1">{t('recipes.print_sheet.measured_col')}</th>
							</tr>
						</thead>
						<tbody class="divide-y divide-zinc-200">
							<tr>
								<td class="py-1.5 font-medium">{t('recipes.print_sheet.pre_boil_gravity')}</td>
								<td class="py-1.5 font-mono text-zinc-600">—</td>
								<td class="py-1.5"
									><div class="h-4 border-b border-dashed border-zinc-400"></div></td
								>
							</tr>
							<tr>
								<td class="py-1.5 font-medium">{t('recipes.print_sheet.og_label')}</td>
								<td class="py-1.5 font-mono font-bold"
									>{settings.formatGravity(recipe.originalGravity)}</td
								>
								<td class="py-1.5"
									><div class="h-4 border-b border-dashed border-zinc-400"></div></td
								>
							</tr>
							<tr>
								<td class="py-1.5 font-medium">{t('recipes.print_sheet.fg_label')}</td>
								<td class="py-1.5 font-mono font-bold"
									>{settings.formatGravity(recipe.finalGravity)}</td
								>
								<td class="py-1.5"
									><div class="h-4 border-b border-dashed border-zinc-400"></div></td
								>
							</tr>
							<tr>
								<td class="py-1.5 font-medium">{t('recipes.print_sheet.abv_label')}</td>
								<td class="py-1.5 font-mono font-bold"
									>{formatNumber(recipe.alcoholByVolume, 1)}%</td
								>
								<td class="py-1.5"
									><div class="h-4 border-b border-dashed border-zinc-400"></div></td
								>
							</tr>
							<tr>
								<td class="py-1.5 font-medium"
									>{t('recipes.print_sheet.ibu_label')} / {t('recipes.print_sheet.bugu_label')}</td
								>
								<td class="py-1.5 font-mono font-bold">
									{formatNumber(recipe.bitternessIbu, 0)} IBU ({formatNumber(buGuRatio, 2)})
								</td>
								<td class="py-1.5 font-mono text-[11px] text-zinc-500">
									AA: {formatNumber(apparentAttenuation, 0)}%
								</td>
							</tr>
							<tr>
								<td class="py-1.5 font-medium">{t('recipes.print_sheet.color_label')}</td>
								<td class="py-1.5 font-mono font-bold"
									>{settings.formatColor(recipe.colorSrm, 1)}</td
								>
								<td class="py-1.5"
									><div class="h-4 border-b border-dashed border-zinc-400"></div></td
								>
							</tr>
						</tbody>
					</table>
				</div>

				<!-- Water & Volume Schedule -->
				<div
					data-testid="print-volumes-section"
					class="rounded-xl border border-zinc-300 p-3 print:rounded-none print:p-1.5"
				>
					<h3 class="mb-2 border-b border-zinc-200 pb-1 text-xs font-bold tracking-wider uppercase">
						{t('recipes.print_sheet.water_volumes_title')}
					</h3>
					<table class="w-full text-xs">
						<thead>
							<tr class="border-b border-zinc-200 text-left text-[10px] text-zinc-500 uppercase">
								<th class="py-1">Stage / Water</th>
								<th class="py-1">{t('recipes.print_sheet.target_col')}</th>
								<th class="w-32 py-1">{t('recipes.print_sheet.measured_col')}</th>
							</tr>
						</thead>
						<tbody class="divide-y divide-zinc-200">
							<tr>
								<td class="py-1.5 font-medium">
									{t('recipes.print_sheet.strike_water')} (@ {t('recipes.print_sheet.strike_temp')})
								</td>
								<td class="py-1.5 font-mono font-bold">
									{settings.formatVolume(waterSchedule.strikeWaterLiters, 1)} @
									{settings.formatTemperature(estimatedStrikeTempC, 1)}
								</td>
								<td class="py-1.5"
									><div class="h-4 border-b border-dashed border-zinc-400"></div></td
								>
							</tr>
							<tr>
								<td class="py-1.5 font-medium">
									{t('recipes.print_sheet.sparge_water')} (@ {t('recipes.print_sheet.sparge_temp')})
								</td>
								<td class="py-1.5 font-mono font-bold">
									{settings.formatVolume(waterSchedule.spargeWaterLiters, 1)} @
									{settings.formatTemperature(targetSpargeTempC, 1)}
								</td>
								<td class="py-1.5"
									><div class="h-4 border-b border-dashed border-zinc-400"></div></td
								>
							</tr>
							<tr>
								<td class="py-1.5 font-medium">{t('recipes.print_sheet.total_water')}</td>
								<td class="py-1.5 font-mono font-bold">
									{settings.formatVolume(waterSchedule.totalWaterLiters, 1)}
								</td>
								<td class="py-1.5"
									><div class="h-4 border-b border-dashed border-zinc-400"></div></td
								>
							</tr>
							<tr>
								<td class="py-1.5 font-medium">{t('recipes.print_sheet.pre_boil_volume')}</td>
								<td class="py-1.5 font-mono font-bold">
									{settings.formatVolume(waterSchedule.targetPreBoilVolumeLiters, 1)}
								</td>
								<td class="py-1.5"
									><div class="h-4 border-b border-dashed border-zinc-400"></div></td
								>
							</tr>
							<tr>
								<td class="py-1.5 font-medium">{t('recipes.print_sheet.post_boil_volume')}</td>
								<td class="py-1.5 font-mono font-bold">
									{settings.formatVolume(waterSchedule.targetPostBoilVolumeLiters, 1)}
								</td>
								<td class="py-1.5"
									><div class="h-4 border-b border-dashed border-zinc-400"></div></td
								>
							</tr>
							<tr>
								<td class="py-1.5 font-medium">
									{t('recipes.print_sheet.fermenter_volume')} / {t(
										'recipes.print_sheet.pitch_temp'
									)}
								</td>
								<td class="py-1.5 font-mono font-bold">
									{settings.formatVolume(waterSchedule.targetFermenterVolumeLiters, 1)}
								</td>
								<td class="py-1.5"
									><div class="h-4 border-b border-dashed border-zinc-400"></div></td
								>
							</tr>
						</tbody>
					</table>
				</div>
			</section>

			<!-- Stage 1: Mash Schedule & Grain Bill -->
			<section
				data-testid="print-mash-section"
				class="sheet-section rounded-xl border border-zinc-300 p-3 print:rounded-none print:p-1.5"
			>
				<h3 class="mb-2 border-b border-zinc-200 pb-1 text-xs font-bold tracking-wider uppercase">
					{t('recipes.print_sheet.mash_section_title')}
				</h3>

				<!-- Fermentables & Grain Bill Checklist (includes Mash Grains, Boil Sugars, Extracts & Priming) -->
				<h4 class="mb-1 text-[11px] font-bold text-zinc-700">
					{t('recipes.print_sheet.fermentables_bill_title')}
				</h4>
				{#if fermentableIngredients.length === 0 && mashNonFermentableIngredients.length === 0}
					<p class="mb-3 text-xs text-zinc-500 italic">{t('recipes.print_sheet.no_ingredients')}</p>
				{:else}
					{#if fermentableIngredients.length > 0}
						<table
							data-testid="print-fermentables-table"
							class="mb-3 w-full border-collapse text-xs"
						>
							<thead>
								<tr
									class="border-b border-zinc-300 bg-zinc-50 text-left text-[10px] text-zinc-600 uppercase"
								>
									<th class="w-8 py-1 text-center">{t('recipes.print_sheet.done_col')}</th>
									<th class="py-1">{t('recipes.print_sheet.ingredient_col')}</th>
									<th class="py-1">{t('recipes.print_sheet.amount_col')}</th>
									<th class="py-1">{t('recipes.print_sheet.stage_col')}</th>
								</tr>
							</thead>
							<tbody class="divide-y divide-zinc-200">
								{#each fermentableIngredients as item (item.id)}
									<tr>
										<td class="py-1.5 text-center">
											<span class="inline-block h-3.5 w-3.5 border border-zinc-700 align-middle"
											></span>
										</td>
										<td class="py-1.5 font-medium">{formatIngredientLabel(item)}</td>
										<td class="py-1.5 font-mono font-bold">{formatScaledAmount(item)}</td>
										<td class="py-1.5 text-zinc-600">
											{formatPrintableStageTiming(item, timingLabels)}
										</td>
									</tr>
								{/each}
							</tbody>
						</table>
					{/if}

					{#if mashNonFermentableIngredients.length > 0}
						<h4 class="mb-1 text-[11px] font-bold text-zinc-700">
							{t('recipes.print_sheet.mash_additions_title')}
						</h4>
						<table
							data-testid="print-mash-additions-table"
							class="mb-3 w-full border-collapse text-xs"
						>
							<thead>
								<tr
									class="border-b border-zinc-300 bg-zinc-50 text-left text-[10px] text-zinc-600 uppercase"
								>
									<th class="w-8 py-1 text-center">{t('recipes.print_sheet.done_col')}</th>
									<th class="py-1">{t('recipes.print_sheet.ingredient_col')}</th>
									<th class="py-1">{t('recipes.print_sheet.amount_col')}</th>
									<th class="py-1">{t('recipes.print_sheet.stage_col')}</th>
								</tr>
							</thead>
							<tbody class="divide-y divide-zinc-200">
								{#each mashNonFermentableIngredients as item (item.id)}
									<tr>
										<td class="py-1.5 text-center">
											<span class="inline-block h-3.5 w-3.5 border border-zinc-700 align-middle"
											></span>
										</td>
										<td class="py-1.5 font-medium">{formatIngredientLabel(item)}</td>
										<td class="py-1.5 font-mono font-bold">{formatScaledAmount(item)}</td>
										<td class="py-1.5 text-zinc-600">
											{formatPrintableStageTiming(item, timingLabels)}
										</td>
									</tr>
								{/each}
							</tbody>
						</table>
					{/if}
				{/if}

				<!-- Mash Steps Table -->
				<h4 class="mb-1 text-[11px] font-bold text-zinc-700">
					{t('recipes.print_sheet.mash_steps_title')}
				</h4>
				<table data-testid="print-mash-steps-table" class="w-full border-collapse text-xs">
					<thead>
						<tr
							class="border-b border-zinc-300 bg-zinc-50 text-left text-[10px] text-zinc-600 uppercase"
						>
							<th class="py-1">#</th>
							<th class="py-1">{t('recipes.print_sheet.step_col')}</th>
							<th class="py-1">{t('recipes.print_sheet.type_col')}</th>
							<th class="py-1">{t('recipes.print_sheet.target_temp_col')}</th>
							<th class="py-1">{t('recipes.print_sheet.planned_duration_col')}</th>
							<th class="py-1">{t('recipes.print_sheet.water_vol_col')}</th>
							<th class="w-20 py-1">{t('recipes.print_sheet.actual_temp_col')}</th>
							<th class="w-20 py-1">{t('recipes.print_sheet.actual_duration_col')}</th>
							<th class="w-28 py-1">{t('recipes.print_sheet.notes_col')}</th>
							<th class="w-8 py-1 text-center">{t('recipes.print_sheet.done_col')}</th>
						</tr>
					</thead>
					<tbody class="divide-y divide-zinc-200">
						{#if recipe.mashSteps && recipe.mashSteps.length > 0}
							{#each [...recipe.mashSteps].sort((a, b) => a.stepOrder - b.stepOrder) as step (step.id)}
								<tr>
									<td class="py-2 font-mono">{step.stepOrder}</td>
									<td class="py-2 font-semibold">{step.name}</td>
									<td class="py-2 text-zinc-600">{step.type}</td>
									<td class="py-2 font-mono font-bold"
										>{settings.formatTemperature(step.temperatureC)}</td
									>
									<td class="py-2 font-mono">{step.durationMinutes} min</td>
									<td class="py-2 font-mono">
										{step.infuseAmountLiters
											? settings.formatVolume(step.infuseAmountLiters * scaleFactor, 1)
											: step.stepOrder === 1
												? settings.formatVolume(waterSchedule.strikeWaterLiters, 1)
												: '—'}
									</td>
									<td class="py-2"
										><div class="h-4 border-b border-dashed border-zinc-400"></div></td
									>
									<td class="py-2"
										><div class="h-4 border-b border-dashed border-zinc-400"></div></td
									>
									<td class="py-2"
										><div class="h-4 border-b border-dashed border-zinc-400"></div></td
									>
									<td class="py-2 text-center">
										<span class="inline-block h-3.5 w-3.5 border border-zinc-700 align-middle"
										></span>
									</td>
								</tr>
							{/each}
						{:else}
							<tr>
								<td class="py-2 font-mono">1</td>
								<td class="py-2 font-semibold"
									>{t('recipes.print_sheet.default_single_infusion')}</td
								>
								<td class="py-2 text-zinc-600">Infusion</td>
								<td class="py-2 font-mono font-bold"
									>{settings.formatTemperature(targetMashTempC)}</td
								>
								<td class="py-2 font-mono">60 min</td>
								<td class="py-2 font-mono">
									{settings.formatVolume(waterSchedule.strikeWaterLiters, 1)}
								</td>
								<td class="py-2"><div class="h-4 border-b border-dashed border-zinc-400"></div></td>
								<td class="py-2"><div class="h-4 border-b border-dashed border-zinc-400"></div></td>
								<td class="py-2"><div class="h-4 border-b border-dashed border-zinc-400"></div></td>
								<td class="py-2 text-center">
									<span class="inline-block h-3.5 w-3.5 border border-zinc-700 align-middle"></span>
								</td>
							</tr>
						{/if}
					</tbody>
				</table>
			</section>

			<!-- Stage 2: Boil Schedule & Additions -->
			<section
				data-testid="print-boil-section"
				class="sheet-section rounded-xl border border-zinc-300 p-3 print:rounded-none print:p-1.5"
			>
				<h3 class="mb-2 border-b border-zinc-200 pb-1 text-xs font-bold tracking-wider uppercase">
					{t('recipes.print_sheet.boil_section_title')}
				</h3>
				<table class="w-full border-collapse text-xs">
					<thead>
						<tr
							class="border-b border-zinc-300 bg-zinc-50 text-left text-[10px] text-zinc-600 uppercase"
						>
							<th class="py-1">{t('recipes.print_sheet.addition_time_col')}</th>
							<th class="py-1">{t('recipes.print_sheet.ingredients_col')}</th>
							<th class="py-1">{t('recipes.print_sheet.target_temp_col')}</th>
							<th class="w-20 py-1">{t('recipes.print_sheet.actual_temp_col')}</th>
							<th class="w-24 py-1">{t('recipes.print_sheet.notes_col')}</th>
							<th class="w-8 py-1 text-center">{t('recipes.print_sheet.done_col')}</th>
						</tr>
					</thead>
					<tbody class="divide-y divide-zinc-200">
						{#each boilScheduleRows as row (row.key)}
							<tr>
								<td class="py-2 font-mono font-bold">{row.label}</td>
								<td class="py-2">
									{#if row.items.length === 0}
										<span class="text-zinc-500">—</span>
									{:else}
										<ul class="space-y-0.5">
											{#each row.items as item (item.id)}
												<li>
													<span class="font-mono font-bold">{formatScaledAmount(item)}</span>
													<span> — {formatIngredientLabel(item)}</span>
												</li>
											{/each}
										</ul>
									{/if}
								</td>
								<td class="py-2 font-mono">{settings.formatTemperature(row.targetTempC)}</td>
								<td class="py-2"><div class="h-4 border-b border-dashed border-zinc-400"></div></td>
								<td class="py-2"><div class="h-4 border-b border-dashed border-zinc-400"></div></td>
								<td class="py-2 text-center">
									<span class="inline-block h-3.5 w-3.5 border border-zinc-700 align-middle"></span>
								</td>
							</tr>
						{/each}
					</tbody>
				</table>
			</section>

			<!-- Stage 3 & 4: Fermentation, Conditioning & Gravity Log -->
			<section
				data-testid="print-fermentation-section"
				class="sheet-section rounded-xl border border-zinc-300 p-3 print:rounded-none print:p-1.5"
			>
				<h3 class="mb-2 border-b border-zinc-200 pb-1 text-xs font-bold tracking-wider uppercase">
					{t('recipes.print_sheet.fermentation_section_title')}
				</h3>

				<!-- Yeast & Dry Hop Additions -->
				<h4 class="mb-1 text-[11px] font-bold text-zinc-700">
					{t('recipes.print_sheet.fermentation_ingredients_title')}
				</h4>
				{#if fermentationIngredients.length === 0}
					<p class="mb-3 text-xs text-zinc-500 italic">{t('recipes.print_sheet.no_ingredients')}</p>
				{:else}
					<table class="mb-3 w-full border-collapse text-xs">
						<thead>
							<tr
								class="border-b border-zinc-300 bg-zinc-50 text-left text-[10px] text-zinc-600 uppercase"
							>
								<th class="w-8 py-1 text-center">{t('recipes.print_sheet.done_col')}</th>
								<th class="py-1">{t('recipes.print_sheet.ingredient_col')}</th>
								<th class="py-1">{t('recipes.print_sheet.amount_col')}</th>
								<th class="py-1">{t('recipes.print_sheet.stage_col')}</th>
							</tr>
						</thead>
						<tbody class="divide-y divide-zinc-200">
							{#each fermentationIngredients as item (item.id)}
								<tr>
									<td class="py-1.5 text-center">
										<span class="inline-block h-3.5 w-3.5 border border-zinc-700 align-middle"
										></span>
									</td>
									<td class="py-1.5 font-medium">{formatIngredientLabel(item)}</td>
									<td class="py-1.5 font-mono font-bold">{formatScaledAmount(item)}</td>
									<td class="py-1.5 text-zinc-600">
										{item.usage}{item.durationMinutes ? ` (${item.durationMinutes} min/days)` : ''}
									</td>
								</tr>
							{/each}
						</tbody>
					</table>
				{/if}

				<!-- Fermentation Steps Schedule -->
				{#if recipe.fermentationSteps && recipe.fermentationSteps.length > 0}
					<h4 class="mb-1 text-[11px] font-bold text-zinc-700">
						{t('recipes.print_sheet.fermentation_steps_title')}
					</h4>
					<table
						data-testid="print-fermentation-steps-table"
						class="mb-4 w-full border-collapse text-xs"
					>
						<thead>
							<tr
								class="border-b border-zinc-300 bg-zinc-50 text-left text-[10px] text-zinc-600 uppercase"
							>
								<th class="py-1">#</th>
								<th class="py-1">{t('recipes.print_sheet.step_col')}</th>
								<th class="py-1">{t('recipes.print_sheet.type_col')}</th>
								<th class="py-1">{t('recipes.print_sheet.target_temp_col')}</th>
								<th class="py-1">{t('recipes.print_sheet.planned_duration_col')}</th>
								<th class="py-1">{t('recipes.print_sheet.trigger_sg_col')}</th>
								<th class="w-20 py-1">{t('recipes.print_sheet.start_date_col')}</th>
								<th class="w-20 py-1">{t('recipes.print_sheet.end_date_col')}</th>
								<th class="w-20 py-1">{t('recipes.print_sheet.actual_temp_col')}</th>
								<th class="w-8 py-1 text-center">{t('recipes.print_sheet.done_col')}</th>
							</tr>
						</thead>
						<tbody class="divide-y divide-zinc-200">
							{#each [...recipe.fermentationSteps].sort((a, b) => a.stepOrder - b.stepOrder) as fstep (fstep.id)}
								<tr>
									<td class="py-2 font-mono">{fstep.stepOrder}</td>
									<td class="py-2 font-semibold">{fstep.name}</td>
									<td class="py-2 text-zinc-600">{fstep.type}</td>
									<td class="py-2 font-mono font-bold">
										{settings.formatTemperature(fstep.targetTemperatureC)}
									</td>
									<td class="py-2 font-mono">{fstep.durationDays} d</td>
									<td class="py-2 font-mono">
										{fstep.triggerGravity ? settings.formatGravity(fstep.triggerGravity) : '—'}
									</td>
									<td class="py-2"
										><div class="h-4 border-b border-dashed border-zinc-400"></div></td
									>
									<td class="py-2"
										><div class="h-4 border-b border-dashed border-zinc-400"></div></td
									>
									<td class="py-2"
										><div class="h-4 border-b border-dashed border-zinc-400"></div></td
									>
									<td class="py-2 text-center">
										<span class="inline-block h-3.5 w-3.5 border border-zinc-700 align-middle"
										></span>
									</td>
								</tr>
							{/each}
						</tbody>
					</table>
				{/if}

				<!-- Manual Fermentation & Gravity Log Grid -->
				<h4 class="mb-1 text-[11px] font-bold text-zinc-700">
					{t('recipes.print_sheet.gravity_log_title')}
				</h4>
				<table data-testid="print-gravity-log-table" class="w-full border-collapse text-xs">
					<thead>
						<tr
							class="border-b border-zinc-300 bg-zinc-50 text-left text-[10px] text-zinc-600 uppercase"
						>
							<th class="w-28 py-1">{t('recipes.print_sheet.date_time_col')}</th>
							<th class="w-12 py-1">{t('recipes.print_sheet.day_col')}</th>
							<th class="w-28 py-1">{t('recipes.print_sheet.stage_step_col')}</th>
							<th class="w-20 py-1">{t('recipes.print_sheet.temp_col')}</th>
							<th class="w-24 py-1">{t('recipes.print_sheet.gravity_col')}</th>
							<th class="w-20 py-1">{t('recipes.print_sheet.est_abv_col')}</th>
							<th class="py-1">{t('recipes.print_sheet.notes_col')}</th>
						</tr>
					</thead>
					<tbody class="divide-y divide-zinc-200">
						{#each gravityLogRows as rowNum, idx (rowNum)}
							<tr class={idx >= 4 ? 'print:hidden' : ''}>
								<td class="py-2.5"
									><div class="h-4 border-b border-dashed border-zinc-300"></div></td
								>
								<td class="py-2.5"
									><div class="h-4 border-b border-dashed border-zinc-300"></div></td
								>
								<td class="py-2.5"
									><div class="h-4 border-b border-dashed border-zinc-300"></div></td
								>
								<td class="py-2.5"
									><div class="h-4 border-b border-dashed border-zinc-300"></div></td
								>
								<td class="py-2.5"
									><div class="h-4 border-b border-dashed border-zinc-300"></div></td
								>
								<td class="py-2.5"
									><div class="h-4 border-b border-dashed border-zinc-300"></div></td
								>
								<td class="py-2.5"
									><div class="h-4 border-b border-dashed border-zinc-300"></div></td
								>
							</tr>
						{/each}
					</tbody>
				</table>
			</section>
		</main>
	{/if}
</div>

<style>
	@media print {
		:global(body) {
			background: #ffffff !important;
			color: #09090b !important;
			margin: 0 !important;
			padding: 0 !important;
		}

		.no-print {
			display: none !important;
		}

		.print-container {
			background: #ffffff !important;
			padding: 0 !important;
			min-height: 0 !important;
		}

		.brew-sheet-canvas {
			background: #ffffff !important;
			color: #09090b !important;
			box-shadow: none !important;
			padding: 0 !important;
			font-size: 9px !important;
			line-height: 1.15 !important;
			-webkit-print-color-adjust: exact;
			print-color-adjust: exact;
		}

		.brew-sheet-canvas :global(h3) {
			font-size: 9.5px !important;
			margin-bottom: 2px !important;
			padding-bottom: 1px !important;
			break-after: avoid;
			page-break-after: avoid;
		}

		.brew-sheet-canvas :global(h4) {
			font-size: 9px !important;
			margin-bottom: 1px !important;
			break-after: avoid;
			page-break-after: avoid;
		}

		.brew-sheet-canvas :global(table) {
			font-size: 9px !important;
			margin-bottom: 3px !important;
		}

		.brew-sheet-canvas :global(th),
		.brew-sheet-canvas :global(td) {
			padding-top: 1.5px !important;
			padding-bottom: 1.5px !important;
			line-height: 1.15 !important;
		}

		.brew-sheet-canvas :global(tr) {
			break-inside: avoid;
			page-break-inside: avoid;
		}

		.brew-sheet-canvas :global(.writein-line),
		.brew-sheet-canvas :global(td > div.border-b) {
			height: 9px !important;
			margin-top: 1px !important;
		}

		.brew-sheet-canvas :global(td span.border) {
			height: 9px !important;
			width: 9px !important;
		}

		.sheet-section {
			padding: 4px 6px !important;
		}

		@page {
			size: A4 portrait;
			margin: 5mm 7mm;
		}
	}
</style>
