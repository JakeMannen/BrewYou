<script lang="ts">
	import { onMount } from 'svelte';
	import { SvelteSet } from 'svelte/reactivity';
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { auth } from '$lib/stores/auth.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import { t } from '$lib/i18n/index.svelte';
	import { getLocalizedIngredientName, getLocalizedIngredientForm } from '$lib/i18n/ingredients';
	import { formatNumber } from '$lib/utils/formatNumber';
	import { srmToHexColor, tinsethIbu, formatPotentialGravity } from '$lib/calculators/brewing';
	import type {
		IngredientDto,
		IngredientUsage,
		MashStepType,
		FermentationStepType,
		RecipeDetailDto,
		RecipeIngredientOutputDto
	} from '$lib/types/api';
	import BeerGlass from '$lib/components/brewery/BeerGlass.svelte';
	import HopScheduleVisualizer, {
		type HopVisualItem
	} from '$lib/components/brewery/HopScheduleVisualizer.svelte';
	import MashEnzymeVisualizer from '$lib/components/brewery/MashEnzymeVisualizer.svelte';
	import FermentationTimelineVisualizer from '$lib/components/brewery/FermentationTimelineVisualizer.svelte';
	import DeleteRecipeModal from '$lib/components/recipes/DeleteRecipeModal.svelte';
	import RecipeExportModal from '$lib/components/recipes/RecipeExportModal.svelte';
	import {
		ArrowLeft,
		BookOpen,
		Pencil,
		Copy,
		Download,
		Trash2,
		Play,
		Loader2,
		UserCheck,
		Globe,
		User,
		FileText,
		Sliders,
		Wheat,
		Flower2,
		Dna,
		Sparkles,
		Flame,
		Activity,
		Clock,
		Thermometer
	} from '@lucide/svelte';

	let recipeId = $derived(page.params.id);
	let recipe = $state<RecipeDetailDto | null>(null);
	let catalogIngredients = $state<IngredientDto[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);

	let isExportModalOpen = $state(false);
	let isDeleteModalOpen = $state(false);
	let cloning = $state(false);

	function isRecipeOwner(r: RecipeDetailDto): boolean {
		if (typeof r.isOwner === 'boolean') return r.isOwner;
		return !r.isPublic;
	}

	async function loadRecipe(id: string) {
		loading = true;
		error = null;
		try {
			await auth.ready();
			const [loadedRecipe, loadedIngredients] = await Promise.all([
				api.recipes.getById(id),
				api.ingredients.list()
			]);
			recipe = loadedRecipe;
			catalogIngredients = loadedIngredients;
		} catch (err: unknown) {
			error = (err as Error).message || t('formulator.load_error');
		} finally {
			loading = false;
		}
	}

	onMount(() => {
		if (!recipeId) {
			error = t('formulator.invalid_id');
			loading = false;
			return;
		}
		void loadRecipe(recipeId);
	});

	let fermentables = $derived(
		(recipe?.ingredients ?? []).filter((i) => i.ingredientType === 'Fermentable')
	);
	let hops = $derived((recipe?.ingredients ?? []).filter((i) => i.ingredientType === 'Hop'));
	let yeasts = $derived((recipe?.ingredients ?? []).filter((i) => i.ingredientType === 'Yeast'));
	let miscs = $derived((recipe?.ingredients ?? []).filter((i) => i.ingredientType === 'Other'));

	let totalGrainWeight = $derived(
		fermentables.reduce((acc, curr) => acc + (Number(curr.amount) || 0), 0)
	);
	let totalHopWeight = $derived(hops.reduce((acc, curr) => acc + (Number(curr.amount) || 0), 0));

	let visualHops = $derived<HopVisualItem[]>(
		hops.map((h) => ({
			id: h.id,
			ingredientId: h.ingredientId,
			amount: h.amount,
			durationMinutes: h.durationMinutes ?? 0,
			usage: h.usage,
			notes: h.notes ?? undefined,
			form: h.form ?? null
		}))
	);

	// Ensure HopScheduleVisualizer can resolve ingredient names & alpha acids even for custom ingredients not in catalog list
	let enrichedCatalogIngredients = $derived.by<IngredientDto[]>(() => {
		if (!recipe) return catalogIngredients;
		const existingIds = new SvelteSet(catalogIngredients.map((c) => c.id));
		const synthetic: IngredientDto[] = [];
		for (const ing of recipe.ingredients) {
			if (!existingIds.has(ing.ingredientId)) {
				existingIds.add(ing.ingredientId);
				synthetic.push({
					id: ing.ingredientId,
					name: ing.ingredientName,
					type: ing.ingredientType,
					potentialGravity: ing.potentialGravity ?? null,
					colorSrm: ing.colorSrm ?? null,
					alphaAcidPercent: ing.alphaAcidPercent ?? null,
					attenuationPercent: ing.attenuationPercent ?? null,
					isCatalogItem: false,
					form: ing.form ?? null
				});
			}
		}
		return [...catalogIngredients, ...synthetic];
	});

	function getUsageLabel(usage: IngredientUsage | string): string {
		switch (usage) {
			case 'Mash':
				return t('formulator.usages.mash');
			case 'Boil':
				return t('formulator.usages.boil');
			case 'FirstWort':
				return t('formulator.usages.first_wort');
			case 'Whirlpool':
				return t('formulator.usages.whirlpool');
			case 'Primary':
				return t('formulator.usages.primary');
			case 'Secondary':
				return t('formulator.usages.secondary');
			case 'DryHop':
				return t('formulator.usages.dry_hop');
			case 'Bottling':
				return t('formulator.usages.bottling');
			default:
				return t('formulator.usages.aroma');
		}
	}

	function getMashStepTypeLabel(type: MashStepType): string {
		switch (type) {
			case 'Infusion':
				return t('mash_profile.type_infusion');
			case 'Temperature':
				return t('mash_profile.type_temperature');
			case 'Decoction':
				return t('mash_profile.type_decoction');
		}
	}

	function getFermentationStepTypeLabel(type: FermentationStepType): string {
		switch (type) {
			case 'Primary':
				return t('fermentation_profile.type_primary');
			case 'Secondary':
				return t('fermentation_profile.type_secondary');
			case 'Ramp':
			case 'DiacetylRest':
				return t('fermentation_profile.type_ramp');
			case 'FreeRise':
				return t('fermentation_profile.type_freerise');
			case 'ColdCrash':
				return t('fermentation_profile.type_coldcrash');
			case 'Conditioning':
				return t('fermentation_profile.type_conditioning');
		}
	}

	function getHopIbuContribution(item: RecipeIngredientOutputDto): number {
		if (!recipe) return 0;
		if (item.usage === 'DryHop' || item.usage === 'Secondary' || item.usage === 'Bottling') {
			return 0;
		}
		const alpha = item.alphaAcidPercent ?? 5.0;
		return tinsethIbu(
			Number(item.amount) || 0,
			alpha,
			Number(item.durationMinutes) || 0,
			recipe.originalGravity,
			recipe.batchSizeLiters
		);
	}

	async function handleCloneRecipe() {
		if (!recipe) return;
		cloning = true;
		try {
			const copySuffix = t('recipes.copy_suffix');
			const created = await api.recipes.create({
				name: `${recipe.name} (${copySuffix})`,
				description: recipe.description ?? undefined,
				beerStyle: recipe.beerStyle,
				batchSizeLiters: recipe.batchSizeLiters,
				boilTimeMinutes: recipe.boilTimeMinutes,
				efficiencyPercent: recipe.efficiencyPercent,
				isPublic: false,
				ingredients: recipe.ingredients.map((i) => ({
					ingredientId: i.ingredientId,
					amount: i.amount,
					unit: i.unit,
					durationMinutes: i.durationMinutes,
					usage: i.usage,
					notes: i.notes ?? undefined,
					form: i.form ?? undefined
				})),
				mashSteps: recipe.mashSteps.map((m) => ({
					stepOrder: m.stepOrder,
					name: m.name,
					type: m.type,
					temperatureC: m.temperatureC,
					durationMinutes: m.durationMinutes,
					rampTimeMinutes: m.rampTimeMinutes ?? undefined,
					infuseAmountLiters: m.infuseAmountLiters ?? undefined,
					notes: m.notes ?? undefined
				})),
				fermentationSteps: recipe.fermentationSteps.map((f) => ({
					stepOrder: f.stepOrder,
					name: f.name,
					type: f.type,
					targetTemperatureC: f.targetTemperatureC,
					durationDays: f.durationDays,
					rampTimeHours: f.rampTimeHours ?? undefined,
					triggerGravity: f.triggerGravity ?? undefined,
					notes: f.notes ?? undefined
				}))
			});
			goto(`/recipes/${created.id}/edit`);
		} catch (err: unknown) {
			error = (err as Error).message || t('recipes.clone_error');
		} finally {
			cloning = false;
		}
	}
</script>

<svelte:head>
	<title>{recipe?.name ? `${recipe.name} — ` : ''}{t('recipes.title')} — BrewYou</title>
</svelte:head>

{#if loading}
	<div class="flex flex-col items-center justify-center gap-3 py-24 text-zinc-400">
		<Loader2 class="h-8 w-8 animate-spin text-amber-500" />
		<span class="text-sm text-zinc-600 dark:text-zinc-400">{t('common.loading')}</span>
	</div>
{:else if error || !recipe}
	<div class="space-y-4">
		<a
			href="/recipes"
			class="inline-flex items-center gap-1.5 text-xs font-semibold text-zinc-600 transition-colors hover:text-amber-600 dark:text-zinc-400 dark:hover:text-amber-400"
		>
			<ArrowLeft class="h-4 w-4" />
			<span>{t('recipes.title')}</span>
		</a>
		<div
			class="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
		>
			{error || t('formulator.not_found')}
		</div>
	</div>
{:else}
	<div class="space-y-6 pb-12">
		<!-- Back Navigation -->
		<div>
			<a
				href="/recipes"
				class="inline-flex items-center gap-1.5 text-xs font-semibold text-zinc-600 transition-colors hover:text-amber-600 dark:text-zinc-400 dark:hover:text-amber-400"
			>
				<ArrowLeft class="h-4 w-4" />
				<span>{t('recipes.title')}</span>
			</a>
		</div>

		<!-- Recipe Header & Action Toolbar -->
		<div
			class="flex flex-col justify-between gap-4 border-b border-zinc-200/80 pb-6 lg:flex-row lg:items-start dark:border-white/5"
		>
			<div class="space-y-2.5">
				<div class="flex items-start gap-3">
					<div
						class="mt-0.5 flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-500"
					>
						<BookOpen class="h-5 w-5" />
					</div>
					<div>
						<h1
							class="font-editorial text-2xl font-bold tracking-tight text-zinc-900 sm:text-3xl dark:text-white"
						>
							{recipe.name}
						</h1>
						<div class="mt-2 flex flex-wrap items-center gap-2 text-xs">
							{#if recipe.beerStyle}
								<span
									class="inline-flex items-center rounded-md border border-amber-500/25 bg-amber-500/10 px-2.5 py-0.5 text-xs font-semibold text-amber-800 dark:text-amber-300"
								>
									{recipe.beerStyle}
								</span>
							{/if}
							<span
								class="rounded-md border border-zinc-200/80 bg-zinc-100/80 px-2 py-0.5 font-mono text-xs text-zinc-600 dark:border-white/10 dark:bg-zinc-800/80 dark:text-zinc-300"
							>
								{settings.formatVolume(recipe.batchSizeLiters)}
							</span>

							{#if isRecipeOwner(recipe)}
								<span
									class="inline-flex items-center gap-1 rounded-md border border-amber-500/30 bg-amber-500/10 px-2 py-0.5 text-[10px] font-bold tracking-wider text-amber-700 uppercase dark:text-amber-400"
								>
									<UserCheck class="h-3 w-3" />
									{t('recipes.badges.my_recipe')}
								</span>
							{:else}
								<span
									class="inline-flex items-center gap-1 rounded-md border border-indigo-500/30 bg-indigo-500/10 px-2 py-0.5 text-[10px] font-bold tracking-wider text-indigo-700 uppercase dark:text-indigo-400"
								>
									<Globe class="h-3 w-3" />
									{t('recipes.badges.shared')}
								</span>
							{/if}

							{#if recipe.authorName}
								<span class="flex items-center gap-1 text-xs text-zinc-500 dark:text-zinc-400">
									<User class="h-3 w-3" />
									{t('recipes.by_author', { author: recipe.authorName })}
								</span>
							{/if}
						</div>
					</div>
				</div>
			</div>

			<!-- Actions -->
			<div class="flex flex-wrap items-center gap-2.5 self-start">
				{#if isRecipeOwner(recipe)}
					<a
						href="/recipes/{recipe.id}/edit"
						class="flex min-h-[42px] items-center gap-2 rounded-xl border border-zinc-200/80 bg-white px-3.5 py-2 text-xs font-semibold text-zinc-700 shadow-xs transition-all hover:border-amber-500/40 hover:bg-amber-50 hover:text-amber-700 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-200 dark:hover:bg-zinc-800 dark:hover:text-amber-400"
					>
						<Pencil class="h-4 w-4 text-amber-500" />
						<span>{t('recipes.edit_recipe')}</span>
					</a>
				{/if}

				<button
					type="button"
					onclick={handleCloneRecipe}
					disabled={cloning}
					class="flex min-h-[42px] cursor-pointer items-center gap-2 rounded-xl border border-zinc-200/80 bg-white px-3.5 py-2 text-xs font-semibold text-zinc-700 shadow-xs transition-all hover:border-indigo-500/40 hover:bg-indigo-50 hover:text-indigo-700 disabled:opacity-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-200 dark:hover:bg-indigo-950/40 dark:hover:text-indigo-400"
				>
					{#if cloning}
						<Loader2 class="h-4 w-4 animate-spin text-indigo-500" />
					{:else}
						<Copy class="h-4 w-4 text-indigo-500" />
					{/if}
					<span>{t('recipes.clone_recipe')}</span>
				</button>

				<button
					type="button"
					onclick={() => (isExportModalOpen = true)}
					class="flex min-h-[42px] cursor-pointer items-center gap-2 rounded-xl border border-zinc-200/80 bg-white px-3.5 py-2 text-xs font-semibold text-zinc-700 shadow-xs transition-all hover:border-amber-500/40 hover:bg-amber-50 hover:text-amber-700 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-200 dark:hover:bg-zinc-800 dark:hover:text-amber-400"
				>
					<Download class="h-4 w-4 text-amber-500" />
					<span>{t('recipes.export_recipe')}</span>
				</button>

				{#if isRecipeOwner(recipe)}
					<button
						type="button"
						onclick={() => (isDeleteModalOpen = true)}
						class="flex min-h-[42px] cursor-pointer items-center gap-2 rounded-xl border border-zinc-200/80 bg-white px-3.5 py-2 text-xs font-semibold text-zinc-600 shadow-xs transition-all hover:border-red-500/40 hover:bg-red-50 hover:text-red-600 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300 dark:hover:bg-red-950/40 dark:hover:text-red-400"
					>
						<Trash2 class="h-4 w-4" />
						<span>{t('recipes.delete_recipe')}</span>
					</button>
				{/if}

				<a
					href="/batches/new?recipeId={recipe.id}"
					class="flex min-h-[42px] items-center gap-2 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-4 py-2 text-xs font-bold text-zinc-950 shadow-md transition-all hover:from-amber-400 hover:to-amber-500 active:scale-[0.98]"
				>
					<Play class="h-3.5 w-3.5 fill-current" />
					<span>{t('recipes.start_batch')}</span>
				</a>
			</div>
		</div>

		<!-- Vital Brewing Metrics HUD -->
		<div
			class="glass-panel grid grid-cols-2 gap-3 rounded-2xl border border-zinc-200/80 p-4 shadow-md sm:grid-cols-3 md:grid-cols-5 dark:border-white/[0.08]"
		>
			<!-- OG -->
			<div
				class="flex flex-col rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-3 dark:border-zinc-800/80 dark:bg-zinc-900/60"
			>
				<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400"
					>{settings.gravityUnit === 'Plato' ? t('metrics.plato') : t('metrics.og')}</span
				>
				<span
					class="mt-1 font-mono text-xl font-bold text-amber-600 lg:text-2xl dark:text-amber-400"
				>
					{settings.formatGravity(recipe.originalGravity)}
				</span>
				<span class="mt-0.5 text-[10px] text-zinc-400 dark:text-zinc-500"
					>{t('metrics.og_desc')}</span
				>
			</div>

			<!-- FG -->
			<div
				class="flex flex-col rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-3 dark:border-zinc-800/80 dark:bg-zinc-900/60"
			>
				<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400"
					>{settings.gravityUnit === 'Plato' ? t('metrics.fg_plato') : t('metrics.fg')}</span
				>
				<span class="mt-1 font-mono text-xl font-bold text-zinc-800 lg:text-2xl dark:text-zinc-200">
					{settings.formatGravity(recipe.finalGravity)}
				</span>
				<span class="mt-0.5 text-[10px] text-zinc-400 dark:text-zinc-500"
					>{t('metrics.fg_desc')}</span
				>
			</div>

			<!-- ABV -->
			<div
				class="flex flex-col rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-3 dark:border-zinc-800/80 dark:bg-zinc-900/60"
			>
				<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400"
					>{t('metrics.abv')}</span
				>
				<span
					class="mt-1 font-mono text-xl font-bold text-emerald-600 lg:text-2xl dark:text-emerald-400"
				>
					{formatNumber(recipe.alcoholByVolume, 2)}%
				</span>
				<span class="mt-0.5 text-[10px] text-zinc-400 dark:text-zinc-500"
					>{t('metrics.abv_desc')}</span
				>
			</div>

			<!-- IBU -->
			<div
				class="flex flex-col rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-3 dark:border-zinc-800/80 dark:bg-zinc-900/60"
			>
				<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400"
					>{t('metrics.ibu')}</span
				>
				<span class="mt-1 font-mono text-xl font-bold text-sky-600 lg:text-2xl dark:text-sky-400">
					{formatNumber(recipe.bitternessIbu, 1)}
				</span>
				<span class="mt-0.5 text-[10px] text-zinc-400 dark:text-zinc-500"
					>{t('metrics.ibu_desc')}</span
				>
			</div>

			<!-- Color SRM / EBC -->
			<div
				class="col-span-2 flex items-stretch justify-between rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-3 sm:col-span-1 dark:border-zinc-800/80 dark:bg-zinc-900/60"
			>
				<div class="flex flex-col justify-between">
					<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400"
						>{t('metrics.color', { unit: settings.colorShort })}</span
					>
					<span class="mt-1 font-mono text-xl font-bold text-zinc-900 lg:text-2xl dark:text-white">
						{formatNumber(settings.toDisplayColor(recipe.colorSrm), 1)}
					</span>
					<span class="mt-0.5 text-[10px] text-zinc-400 dark:text-zinc-500"
						>{t('metrics.color_desc')}</span
					>
				</div>
				<div class="flex items-center justify-end">
					<BeerGlass srm={recipe.colorSrm} size="full" class="h-full shrink-0" />
				</div>
			</div>
		</div>

		<!-- Description & Brewhouse Calibration -->
		<div class="grid grid-cols-1 gap-6 lg:grid-cols-3">
			<div
				class="glass-panel space-y-3 rounded-2xl border border-zinc-200/80 p-6 lg:col-span-2 dark:border-white/[0.08]"
			>
				<h2 class="flex items-center gap-2 text-base font-bold text-zinc-900 dark:text-white">
					<FileText class="h-4 w-4 text-amber-500" />
					{t('formulator.description')}
				</h2>
				<p class="text-sm leading-relaxed whitespace-pre-line text-zinc-600 dark:text-zinc-300">
					{recipe.description || t('recipes.subtitle')}
				</p>
			</div>

			<div
				class="glass-panel space-y-4 rounded-2xl border border-zinc-200/80 p-6 dark:border-white/[0.08]"
			>
				<h2 class="flex items-center gap-2 text-base font-bold text-zinc-900 dark:text-white">
					<Sliders class="h-4 w-4 text-amber-500" />
					{t('formulator.brewhouse_specs')}
				</h2>
				<div class="space-y-3 text-xs">
					<div
						class="flex items-center justify-between border-b border-zinc-200/60 pb-2.5 dark:border-white/5"
					>
						<span class="font-semibold text-zinc-500 dark:text-zinc-400"
							>{t('formulator.batch_size')}</span
						>
						<span class="font-mono text-sm font-bold text-zinc-900 dark:text-white"
							>{settings.formatVolume(recipe.batchSizeLiters)}</span
						>
					</div>
					<div
						class="flex items-center justify-between border-b border-zinc-200/60 pb-2.5 dark:border-white/5"
					>
						<span class="font-semibold text-zinc-500 dark:text-zinc-400"
							>{t('formulator.boil_duration')}</span
						>
						<span class="font-mono text-sm font-bold text-zinc-900 dark:text-white"
							>{recipe.boilTimeMinutes} {t('common.minutes')}</span
						>
					</div>
					<div class="flex items-center justify-between">
						<span class="font-semibold text-zinc-500 dark:text-zinc-400"
							>{t('formulator.efficiency')}</span
						>
						<span class="font-mono text-sm font-bold text-zinc-900 dark:text-white"
							>{recipe.efficiencyPercent}%</span
						>
					</div>
				</div>
			</div>
		</div>

		<!-- Ingredients Bill -->
		<div class="space-y-6">
			<div class="border-b border-zinc-200/80 pb-3 dark:border-white/5">
				<h2 class="text-xl font-bold tracking-tight text-zinc-900 dark:text-white">
					{t('formulator.ingredients_bill')}
				</h2>
			</div>

			<!-- Fermentables & Malts -->
			<section
				class="glass-panel overflow-hidden rounded-2xl border border-amber-500/20 bg-amber-500/[0.02] dark:border-amber-500/15 dark:bg-zinc-900/40"
			>
				<div
					class="flex items-center justify-between border-b border-zinc-200/80 bg-zinc-50/70 px-5 py-3.5 dark:border-white/5 dark:bg-zinc-900/70"
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
				</div>

				{#if fermentables.length === 0}
					<div class="py-8 text-center text-xs text-zinc-400">
						{t('formulator.empty.fermentables')}
					</div>
				{:else}
					<div class="divide-y divide-zinc-200/60 dark:divide-white/5">
						{#each fermentables as item (item.id)}
							{@const gristPercent =
								totalGrainWeight > 0 ? ((Number(item.amount) || 0) / totalGrainWeight) * 100 : 0}
							<div
								class="flex flex-col items-stretch justify-between gap-3 p-4 sm:flex-row sm:items-center"
							>
								<div class="flex flex-1 items-center gap-3">
									<div
										class="h-6 w-6 shrink-0 rounded-md border border-zinc-300 shadow-inner dark:border-zinc-700"
										style="background-color: {srmToHexColor(item.colorSrm ?? 2.0)}"
										title={settings.formatColor(item.colorSrm ?? 2.0)}
									></div>
									<div>
										<div class="text-sm font-semibold text-zinc-900 dark:text-white">
											{getLocalizedIngredientName({ name: item.ingredientName })}
										</div>
										<div class="text-xs text-zinc-500 dark:text-zinc-400">
											{settings.formatColor(item.colorSrm ?? 2.0)} •
											{formatPotentialGravity(item.potentialGravity)}
										</div>
									</div>
								</div>

								<div class="flex items-center gap-4 sm:w-80">
									<div
										class="w-24 text-right font-mono text-sm font-bold text-zinc-900 dark:text-white"
									>
										{settings.formatGrain(item.amount)}
									</div>
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
												class="h-full rounded-full bg-amber-500"
												style="width: {Math.min(100, Math.max(0, gristPercent))}%"
											></div>
										</div>
									</div>
									<span
										class="rounded-lg border border-zinc-200/80 bg-zinc-100 px-2.5 py-1 text-xs font-medium text-zinc-700 dark:border-zinc-800 dark:bg-zinc-800 dark:text-zinc-300"
									>
										{getUsageLabel(item.usage)}
									</span>
								</div>
							</div>
						{/each}
					</div>
				{/if}
			</section>

			<!-- Hops & Bitterness -->
			<section
				class="glass-panel overflow-hidden rounded-2xl border border-emerald-500/20 bg-emerald-500/[0.02] dark:border-emerald-500/15 dark:bg-zinc-900/40"
			>
				<div
					class="flex items-center justify-between border-b border-zinc-200/80 bg-zinc-50/70 px-5 py-3.5 dark:border-white/5 dark:bg-zinc-900/70"
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
								{t('formulator.summaries.total_hops', {
									weight: settings.formatHop(totalHopWeight)
								})} •
								{t('formulator.summaries.total_ibu', {
									ibu: formatNumber(recipe.bitternessIbu, 1)
								})}
							</span>
						</div>
					</div>
				</div>

				{#if visualHops.length > 0}
					<div class="px-5 pt-4">
						<HopScheduleVisualizer
							hops={visualHops}
							catalogIngredients={enrichedCatalogIngredients}
							estimatedOg={recipe.originalGravity}
							batchSizeLiters={recipe.batchSizeLiters}
						/>
					</div>
				{/if}

				{#if hops.length === 0}
					<div class="py-8 text-center text-xs text-zinc-400">
						{t('formulator.empty.hops')}
					</div>
				{:else}
					<div class="divide-y divide-zinc-200/60 dark:divide-white/5">
						{#each hops as item (item.id)}
							{@const estIbu = getHopIbuContribution(item)}
							<div
								class="flex flex-col items-stretch justify-between gap-3 p-4 sm:flex-row sm:items-center"
							>
								<div class="flex-1">
									<div class="text-sm font-semibold text-zinc-900 dark:text-white">
										{getLocalizedIngredientName({ name: item.ingredientName })}
									</div>
									<div class="text-xs text-zinc-500 dark:text-zinc-400">
										{item.alphaAcidPercent ?? 5}% AA
										{#if item.form}
											• {getLocalizedIngredientForm(item.form)}
										{/if}
									</div>
								</div>

								<div class="flex flex-wrap items-center gap-3">
									<span class="font-mono text-sm font-bold text-zinc-900 dark:text-white">
										{settings.formatHop(item.amount)}
									</span>
									<span
										class="rounded-lg border border-zinc-200/80 bg-zinc-100 px-2.5 py-1 font-mono text-xs font-medium text-zinc-700 dark:border-zinc-800 dark:bg-zinc-800 dark:text-zinc-300"
									>
										{item.durationMinutes ?? 0}
										{t('common.minutes')}
									</span>
									<span
										class="rounded-lg border border-zinc-200/80 bg-zinc-100 px-2.5 py-1 text-xs font-medium text-zinc-700 dark:border-zinc-800 dark:bg-zinc-800 dark:text-zinc-300"
									>
										{getUsageLabel(item.usage)}
									</span>
									<span
										class="rounded-xl border border-emerald-500/20 bg-emerald-500/5 px-2.5 py-1 font-mono text-xs font-bold text-emerald-600 dark:text-emerald-400"
									>
										{formatNumber(estIbu, 1)} IBU
									</span>
								</div>
							</div>
						{/each}
					</div>
				{/if}
			</section>

			<!-- Yeasts & Fermentation -->
			<section
				class="glass-panel overflow-hidden rounded-2xl border border-sky-500/20 bg-sky-500/[0.02] dark:border-sky-500/15 dark:bg-zinc-900/40"
			>
				<div
					class="flex items-center justify-between border-b border-zinc-200/80 bg-zinc-50/70 px-5 py-3.5 dark:border-white/5 dark:bg-zinc-900/70"
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
				</div>

				{#if yeasts.length === 0}
					<div class="py-8 text-center text-xs text-zinc-400">
						{t('formulator.empty.yeasts')}
					</div>
				{:else}
					<div class="divide-y divide-zinc-200/60 dark:divide-white/5">
						{#each yeasts as item (item.id)}
							<div
								class="flex flex-col items-stretch justify-between gap-3 p-4 sm:flex-row sm:items-center"
							>
								<div class="flex-1">
									<div class="text-sm font-semibold text-zinc-900 dark:text-white">
										{getLocalizedIngredientName({ name: item.ingredientName })}
									</div>
									{#if item.form}
										<div class="text-xs text-zinc-500 dark:text-zinc-400">
											{getLocalizedIngredientForm(item.form)}
										</div>
									{/if}
								</div>

								<div class="flex flex-wrap items-center gap-3">
									<span class="font-mono text-sm font-bold text-zinc-900 dark:text-white">
										{item.amount}
										{item.unit}
									</span>
									<span
										class="rounded-xl border border-sky-500/20 bg-sky-500/5 px-2.5 py-1 font-mono text-xs font-bold text-sky-600 dark:text-sky-400"
									>
										{t('formulator.summaries.attenuation', {
											atten: item.attenuationPercent ?? 75
										})}
									</span>
									<span
										class="rounded-lg border border-zinc-200/80 bg-zinc-100 px-2.5 py-1 text-xs font-medium text-zinc-700 dark:border-zinc-800 dark:bg-zinc-800 dark:text-zinc-300"
									>
										{getUsageLabel(item.usage)}
									</span>
								</div>
							</div>
						{/each}
					</div>
				{/if}
			</section>

			<!-- Miscs / Water Agents (if present) -->
			{#if miscs.length > 0}
				<section
					class="glass-panel overflow-hidden rounded-2xl border border-purple-500/20 bg-purple-500/[0.02] dark:border-purple-500/15 dark:bg-zinc-900/40"
				>
					<div
						class="flex items-center justify-between border-b border-zinc-200/80 bg-zinc-50/70 px-5 py-3.5 dark:border-white/5 dark:bg-zinc-900/70"
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
					</div>
					<div class="divide-y divide-zinc-200/60 dark:divide-white/5">
						{#each miscs as item (item.id)}
							<div
								class="flex flex-col items-stretch justify-between gap-3 p-4 sm:flex-row sm:items-center"
							>
								<div class="text-sm font-semibold text-zinc-900 dark:text-white">
									{getLocalizedIngredientName({ name: item.ingredientName })}
								</div>
								<div class="flex items-center gap-3">
									<span class="font-mono text-sm font-bold text-zinc-900 dark:text-white">
										{item.amount}
										{item.unit}
									</span>
									{#if item.durationMinutes}
										<span
											class="rounded-lg border border-zinc-200/80 bg-zinc-100 px-2.5 py-1 font-mono text-xs text-zinc-700 dark:border-zinc-800 dark:bg-zinc-800 dark:text-zinc-300"
										>
											{item.durationMinutes}
											{t('common.minutes')}
										</span>
									{/if}
									<span
										class="rounded-lg border border-zinc-200/80 bg-zinc-100 px-2.5 py-1 text-xs font-medium text-zinc-700 dark:border-zinc-800 dark:bg-zinc-800 dark:text-zinc-300"
									>
										{getUsageLabel(item.usage)}
									</span>
								</div>
							</div>
						{/each}
					</div>
				</section>
			{/if}
		</div>

		<!-- Mash Profile & Steps -->
		<div
			class="overflow-hidden rounded-2xl border border-zinc-200/80 bg-white/70 shadow-sm backdrop-blur-md dark:border-zinc-800/80 dark:bg-zinc-900/60"
		>
			<div
				class="flex items-center gap-3 border-b border-zinc-200/80 px-6 py-4 dark:border-zinc-800/80"
			>
				<div
					class="flex h-10 w-10 items-center justify-center rounded-xl bg-gradient-to-br from-amber-500/20 to-copper-600/20 text-copper-600 dark:text-copper-400"
				>
					<Flame class="h-5 w-5" />
				</div>
				<div>
					<h3 class="font-editorial text-lg font-bold text-zinc-900 dark:text-white">
						{t('mash_profile.title')}
					</h3>
					<p class="text-xs text-zinc-500 dark:text-zinc-400">
						{t('mash_profile.subtitle')}
					</p>
				</div>
			</div>

			{#if recipe.mashSteps && recipe.mashSteps.length > 0}
				<div class="px-4 pt-4 sm:px-6 sm:pt-6">
					<MashEnzymeVisualizer steps={recipe.mashSteps} />
				</div>
				<div class="divide-y divide-zinc-200/60 p-4 sm:p-6 dark:divide-zinc-800/60">
					{#each recipe.mashSteps as step (step.id || step.stepOrder)}
						<div
							class="flex flex-wrap items-center justify-between gap-3 py-3 first:pt-0 last:pb-0"
						>
							<div class="flex items-center gap-3">
								<span
									class="rounded-md bg-amber-500/15 px-2 py-0.5 font-mono text-xs font-bold text-amber-700 dark:text-amber-300"
								>
									{t('mash_profile.step_order_badge', { order: step.stepOrder })}
								</span>
								<span class="text-sm font-semibold text-zinc-900 dark:text-white">
									{step.name}
								</span>
								<span
									class="rounded-md border border-zinc-200/80 bg-zinc-100 px-2 py-0.5 text-xs text-zinc-600 dark:border-zinc-800 dark:bg-zinc-800 dark:text-zinc-300"
								>
									{getMashStepTypeLabel(step.type)}
								</span>
							</div>
							<div class="flex items-center gap-4 font-mono text-xs font-bold">
								<span class="flex items-center gap-1 text-amber-600 dark:text-amber-400">
									<Thermometer class="h-3.5 w-3.5" />
									{settings.formatTemperature(step.temperatureC)}
								</span>
								<span class="flex items-center gap-1 text-zinc-700 dark:text-zinc-300">
									<Clock class="h-3.5 w-3.5" />
									{step.durationMinutes}
									{t('common.minutes')}
								</span>
							</div>
						</div>
					{/each}
				</div>
			{:else}
				<div class="p-6 text-center text-sm text-zinc-500 dark:text-zinc-400">
					{t('mash_profile.no_steps')}
				</div>
			{/if}
		</div>

		<!-- Fermentation Profile & Stages -->
		<div
			class="overflow-hidden rounded-2xl border border-zinc-200/80 bg-white/70 shadow-sm backdrop-blur-md dark:border-zinc-800/80 dark:bg-zinc-900/60"
		>
			<div
				class="flex items-center gap-3 border-b border-zinc-200/80 px-6 py-4 dark:border-zinc-800/80"
			>
				<div
					class="flex h-10 w-10 items-center justify-center rounded-xl bg-gradient-to-br from-hops-500/20 to-emerald-600/20 text-hops-600 dark:text-hops-400"
				>
					<Activity class="h-5 w-5" />
				</div>
				<div>
					<h3 class="font-editorial text-lg font-bold text-zinc-900 dark:text-white">
						{t('fermentation_profile.title')}
					</h3>
					<p class="text-xs text-zinc-500 dark:text-zinc-400">
						{t('fermentation_profile.subtitle')}
					</p>
				</div>
			</div>

			{#if recipe.fermentationSteps && recipe.fermentationSteps.length > 0}
				<div class="px-4 pt-4 sm:px-6 sm:pt-6">
					<FermentationTimelineVisualizer steps={recipe.fermentationSteps} />
				</div>
				<div class="divide-y divide-zinc-200/60 p-4 sm:p-6 dark:divide-zinc-800/60">
					{#each recipe.fermentationSteps as step (step.id || step.stepOrder)}
						<div
							class="flex flex-wrap items-center justify-between gap-3 py-3 first:pt-0 last:pb-0"
						>
							<div class="flex items-center gap-3">
								<span
									class="rounded-md bg-emerald-500/15 px-2 py-0.5 font-mono text-xs font-bold text-emerald-700 dark:text-emerald-300"
								>
									{t('mash_profile.step_order_badge', { order: step.stepOrder })}
								</span>
								<span class="text-sm font-semibold text-zinc-900 dark:text-white">
									{step.name}
								</span>
								<span
									class="rounded-md border border-zinc-200/80 bg-zinc-100 px-2 py-0.5 text-xs text-zinc-600 dark:border-zinc-800 dark:bg-zinc-800 dark:text-zinc-300"
								>
									{getFermentationStepTypeLabel(step.type)}
								</span>
							</div>
							<div class="flex items-center gap-4 font-mono text-xs font-bold">
								<span class="flex items-center gap-1 text-emerald-600 dark:text-emerald-400">
									<Thermometer class="h-3.5 w-3.5" />
									{settings.formatTemperature(step.targetTemperatureC)}
								</span>
								<span class="flex items-center gap-1 text-zinc-700 dark:text-zinc-300">
									<Clock class="h-3.5 w-3.5" />
									{step.durationDays}
									{t('fermentation_profile.days')}
								</span>
							</div>
						</div>
					{/each}
				</div>
			{:else}
				<div class="p-6 text-center text-sm text-zinc-500 dark:text-zinc-400">
					{t('fermentation_profile.no_steps')}
				</div>
			{/if}
		</div>
	</div>

	<DeleteRecipeModal
		open={isDeleteModalOpen}
		{recipe}
		onClose={() => (isDeleteModalOpen = false)}
		onDeleted={() => goto('/recipes')}
	/>

	<RecipeExportModal
		open={isExportModalOpen}
		{recipe}
		onClose={() => (isExportModalOpen = false)}
	/>
{/if}
