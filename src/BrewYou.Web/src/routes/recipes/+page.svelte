<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { auth } from '$lib/stores/auth.svelte';
	import type {
		RecipeSummaryDto,
		RecipeDetailDto,
		IngredientDto,
		RecipeScope
	} from '$lib/types/api';
	import type { ParsedRecipe } from '$lib/parsers/types';
	import { t } from '$lib/i18n/index.svelte';
	import { formatNumber } from '$lib/utils/formatNumber';
	import BeerGlass from '$lib/components/brewery/BeerGlass.svelte';
	import DeleteRecipeModal from '$lib/components/recipes/DeleteRecipeModal.svelte';
	import RecipeImportModal from '$lib/components/recipes/RecipeImportModal.svelte';
	import RecipeExportModal from '$lib/components/recipes/RecipeExportModal.svelte';
	import {
		BookOpen,
		Plus,
		Search,
		Beer,
		Loader2,
		Play,
		Pencil,
		Trash2,
		UploadCloud,
		Download,
		Copy,
		UserCheck,
		Globe,
		User
	} from '@lucide/svelte';

	type RecipeTab = RecipeScope;

	let activeTab = $state<RecipeTab>('all');
	let allRecipes = $state<RecipeSummaryDto[]>([]);
	let recipes = $state<RecipeSummaryDto[]>([]);
	let catalogIngredients = $state<IngredientDto[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);
	let searchTerm = $state('');

	let recipeToDelete = $state<RecipeSummaryDto | null>(null);
	let isImportModalOpen = $state(false);
	let recipeToExport = $state<RecipeDetailDto | null>(null);
	let isExportModalOpen = $state(false);
	let exportLoadingId = $state<string | null>(null);
	let cloningRecipeId = $state<string | null>(null);

	function isRecipeOwner(recipe: RecipeSummaryDto): boolean {
		if (typeof recipe.isOwner === 'boolean') return recipe.isOwner;
		return !recipe.isPublic;
	}

	let tabCounts = $derived({
		all: allRecipes.length,
		mine: allRecipes.filter((r) => isRecipeOwner(r)).length,
		shared: allRecipes.filter((r) => !isRecipeOwner(r)).length
	});

	let filteredRecipes = $derived(
		recipes.filter((r) => {
			const term = searchTerm.toLowerCase().trim();
			if (!term) return true;
			return (
				r.name.toLowerCase().includes(term) ||
				r.beerStyle.toLowerCase().includes(term) ||
				(r.authorName && r.authorName.toLowerCase().includes(term))
			);
		})
	);

	async function handleOpenExport(recipe: RecipeSummaryDto) {
		exportLoadingId = recipe.id;
		try {
			const fullRecipe = await api.recipes.getById(recipe.id);
			recipeToExport = fullRecipe;
			isExportModalOpen = true;
		} catch (err: unknown) {
			error = (err as Error).message || t('recipes.export_modal.error_load');
		} finally {
			exportLoadingId = null;
		}
	}

	async function setTab(tab: RecipeTab) {
		activeTab = tab;
		if (typeof window !== 'undefined') {
			const url = new URL(window.location.href);
			if (tab === 'all') {
				url.searchParams.delete('tab');
			} else {
				url.searchParams.set('tab', tab);
			}
			window.history.replaceState({}, '', url);
		}

		loading = true;
		error = null;
		try {
			recipes = await api.recipes.list(1, 100, tab);
		} catch (err: unknown) {
			error = (err as Error).message || 'Failed to fetch recipes.';
		} finally {
			loading = false;
		}
	}

	async function handleCloneRecipe(recipe: RecipeSummaryDto) {
		cloningRecipeId = recipe.id;
		try {
			const full = await api.recipes.getById(recipe.id);
			const copySuffix = t('recipes.copy_suffix');
			const created = await api.recipes.create({
				name: `${full.name} (${copySuffix})`,
				description: full.description ?? undefined,
				beerStyle: full.beerStyle,
				batchSizeLiters: full.batchSizeLiters,
				boilTimeMinutes: full.boilTimeMinutes,
				efficiencyPercent: full.efficiencyPercent,
				isPublic: false,
				ingredients: full.ingredients.map((i) => ({
					ingredientId: i.ingredientId,
					amount: i.amount,
					unit: i.unit,
					durationMinutes: i.durationMinutes,
					usage: i.usage,
					notes: i.notes ?? undefined,
					form: i.form ?? undefined
				})),
				mashSteps: full.mashSteps.map((m) => ({
					stepOrder: m.stepOrder,
					name: m.name,
					type: m.type,
					temperatureC: m.temperatureC,
					durationMinutes: m.durationMinutes,
					rampTimeMinutes: m.rampTimeMinutes ?? undefined,
					infuseAmountLiters: m.infuseAmountLiters ?? undefined,
					notes: m.notes ?? undefined
				})),
				fermentationSteps: full.fermentationSteps.map((f) => ({
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
			allRecipes = [created, ...allRecipes];
			recipes = [created, ...recipes];
			await setTab('mine');
			goto(`/recipes/${created.id}/edit`);
		} catch (err: unknown) {
			error = (err as Error).message || t('recipes.clone_error');
		} finally {
			cloningRecipeId = null;
		}
	}

	let isInitialized = false;

	async function loadInitialRecipes() {
		const isAuth = await auth.ready();
		if (!isAuth) {
			loading = false;
			return;
		}

		if (isInitialized) return;
		isInitialized = true;

		loading = true;
		error = null;
		try {
			const [allList, scopedList, ingredientsList] = await Promise.all([
				api.recipes.list(1, 100, 'all'),
				activeTab === 'all' ? Promise.resolve(null) : api.recipes.list(1, 100, activeTab),
				api.ingredients.list()
			]);
			allRecipes = allList;
			recipes = scopedList ?? allList;
			catalogIngredients = ingredientsList;
		} catch (err: unknown) {
			error = (err as Error).message || 'Failed to fetch recipes.';
		} finally {
			loading = false;
		}
	}

	onMount(() => {
		const tabParam = page.url.searchParams.get('tab');
		if (tabParam === 'mine' || tabParam === 'shared') {
			activeTab = tabParam;
		}
		void loadInitialRecipes();
	});

	$effect(() => {
		if (auth.isAuthenticated && !isInitialized && !auth.isLoading) {
			void loadInitialRecipes();
		}
	});

	function handleRecipeDeleted(id: string) {
		recipes = recipes.filter((r) => r.id !== id);
		allRecipes = allRecipes.filter((r) => r.id !== id);
	}

	function handleIngredientsCreated(newIngredients: IngredientDto[]) {
		catalogIngredients = [...catalogIngredients, ...newIngredients];
	}

	async function handleImportRecipe(parsed: ParsedRecipe, updatedCatalog?: IngredientDto[]) {
		try {
			loading = true;
			const activeCatalog =
				updatedCatalog && updatedCatalog.length > 0
					? [...catalogIngredients, ...updatedCatalog]
					: catalogIngredients;

			if (updatedCatalog && updatedCatalog.length > 0) {
				catalogIngredients = activeCatalog;
			}

			const ingredients = parsed.ingredients.map((ing) => {
				const lower = ing.name.trim().toLowerCase();
				const match =
					activeCatalog.find(
						(c) =>
							c.name.toLowerCase() === lower ||
							c.name.toLowerCase().includes(lower) ||
							lower.includes(c.name.toLowerCase())
					) || activeCatalog.find((c) => c.type === ing.type);

				return {
					ingredientId: match ? match.id : activeCatalog[0]?.id || '',
					amount: ing.amount,
					unit: ing.unit,
					durationMinutes: ing.durationMinutes,
					usage: ing.usage,
					form: ing.form ?? match?.form ?? undefined
				};
			});

			const created = await api.recipes.create({
				name: parsed.name,
				description: parsed.description || '',
				beerStyle: parsed.beerStyle || 'Standard',
				batchSizeLiters: parsed.batchSizeLiters || 20,
				boilTimeMinutes: parsed.boilTimeMinutes || 60,
				efficiencyPercent: parsed.efficiencyPercent || 72,
				isPublic: true,
				ingredients: ingredients.filter((i) => i.ingredientId !== ''),
				mashSteps: parsed.mashSteps?.map((s) => ({
					stepOrder: s.stepOrder,
					name: s.name,
					type: s.type,
					temperatureC: s.temperatureC,
					durationMinutes: s.durationMinutes,
					rampTimeMinutes: s.rampTimeMinutes ?? null,
					infuseAmountLiters: s.infuseAmountLiters ?? null,
					notes: s.notes ?? null
				})),
				fermentationSteps: parsed.fermentationSteps?.map((s) => ({
					stepOrder: s.stepOrder,
					name: s.name,
					type: s.type,
					targetTemperatureC: s.targetTemperatureC,
					durationDays: s.durationDays,
					rampTimeHours: s.rampTimeHours ?? null,
					triggerGravity: s.triggerGravity ?? null,
					notes: s.notes ?? null
				}))
			});

			allRecipes = [created, ...allRecipes];
			recipes = [created, ...recipes];
			goto(`/recipes/${created.id}/edit`);
		} catch (err: unknown) {
			error = (err as Error).message || 'Failed to import recipe.';
			loading = false;
		}
	}
</script>

<div class="space-y-6">
	<!-- Header -->
	<div class="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
		<div>
			<div class="flex items-center gap-2.5">
				<div
					class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-500"
				>
					<BookOpen class="h-5 w-5" />
				</div>
				<h1 class="text-2xl font-bold tracking-tight text-zinc-900 sm:text-3xl dark:text-white">
					{t('recipes.title')}
				</h1>
			</div>
			<p class="mt-1 text-sm text-zinc-600 dark:text-zinc-400">
				{t('recipes.subtitle')}
			</p>
		</div>

		<div class="flex flex-wrap items-center gap-3 self-start sm:self-auto">
			<button
				type="button"
				onclick={() => (isImportModalOpen = true)}
				class="flex min-h-[44px] cursor-pointer items-center gap-2 rounded-xl border border-zinc-200/80 bg-white px-4 py-2.5 text-sm font-semibold text-zinc-700 shadow-sm transition-all hover:bg-zinc-50 active:scale-[0.98] dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-200 dark:hover:bg-zinc-800"
			>
				<UploadCloud class="h-4 w-4 text-amber-500" />
				<span>{t('recipes.import_recipe')}</span>
			</button>

			<a
				href="/recipes/new"
				class="flex min-h-[44px] items-center gap-2 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-4 py-2.5 text-sm font-bold text-zinc-950 shadow-md transition-all hover:from-amber-400 hover:to-amber-500 active:scale-[0.98]"
			>
				<Plus class="h-4 w-4 stroke-[2.5]" />
				<span>{t('recipes.new_recipe')}</span>
			</a>
		</div>
	</div>

	<!-- Filters & Search Toolbar -->
	<div
		class="glass-panel flex flex-col items-stretch justify-between gap-4 rounded-2xl border border-zinc-200/80 p-3 sm:flex-row sm:items-center dark:border-white/[0.08]"
	>
		<!-- Accessible Recipe Filter Tabs -->
		<div
			role="tablist"
			aria-label={t('recipes.tabs.aria_label')}
			class="flex items-center gap-1.5 overflow-x-auto rounded-xl border border-zinc-200/80 bg-zinc-100/70 p-1 text-xs dark:border-white/5 dark:bg-zinc-900/60"
		>
			{#each [{ id: 'all', label: t('recipes.tabs.all'), count: tabCounts.all }, { id: 'mine', label: t('recipes.tabs.mine'), count: tabCounts.mine }, { id: 'shared', label: t('recipes.tabs.shared'), count: tabCounts.shared }] as tab}
				<button
					type="button"
					role="tab"
					aria-selected={activeTab === tab.id}
					onclick={() => setTab(tab.id as RecipeTab)}
					class="flex min-h-[38px] cursor-pointer items-center gap-2 rounded-lg px-3.5 py-1.5 font-semibold whitespace-nowrap transition-all {activeTab ===
					tab.id
						? 'bg-white text-zinc-950 shadow-xs dark:bg-zinc-800 dark:text-white'
						: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-200'}"
				>
					<span>{tab.label}</span>
					<span
						class="rounded-full px-2 py-0.5 font-mono text-[10px] {activeTab === tab.id
							? 'bg-amber-500/20 text-amber-700 dark:text-amber-300'
							: 'bg-zinc-200/60 text-zinc-600 dark:bg-zinc-700/60 dark:text-zinc-400'}"
					>
						{tab.count}
					</span>
				</button>
			{/each}
		</div>

		<!-- Search Bar -->
		<div class="relative w-full sm:w-72">
			<Search
				class="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-zinc-400"
			/>
			<input
				type="text"
				placeholder={t('recipes.search_placeholder')}
				bind:value={searchTerm}
				aria-label={t('recipes.search_placeholder')}
				class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white pr-3 pl-9 text-sm text-zinc-900 placeholder-zinc-400 transition-colors focus:border-amber-500/60 focus:ring-2 focus:ring-amber-500/20 focus:outline-none dark:border-white/10 dark:bg-zinc-900/80 dark:text-zinc-100 dark:placeholder-zinc-500"
			/>
		</div>
	</div>

	<!-- Content -->
	{#if loading}
		<div class="flex flex-col items-center justify-center gap-3 py-16 text-zinc-400">
			<Loader2 class="h-8 w-8 animate-spin text-amber-500" />
			<span class="text-sm text-zinc-600 dark:text-zinc-400">{t('common.loading')}</span>
		</div>
	{:else if error}
		<div
			class="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
		>
			{error}
		</div>
	{:else if filteredRecipes.length === 0}
		<div
			class="glass-panel flex flex-col items-center justify-center rounded-3xl border border-zinc-200/80 py-16 text-center dark:border-white/[0.08]"
		>
			<div
				class="flex h-12 w-12 items-center justify-center rounded-2xl border border-zinc-200 bg-zinc-100 text-zinc-400 dark:border-zinc-800 dark:bg-zinc-900"
			>
				<Beer class="h-6 w-6" />
			</div>
			<div class="mt-4 space-y-1">
				{#if searchTerm.trim()}
					<h3 class="text-base font-bold text-zinc-900 dark:text-white">
						{t('recipes.empty_search_title')}
					</h3>
					<p class="text-xs text-zinc-500 dark:text-zinc-400">
						{t('recipes.empty_search_desc', { query: searchTerm.trim() })}
					</p>
				{:else if activeTab === 'mine'}
					<h3 class="text-base font-bold text-zinc-900 dark:text-white">
						{t('recipes.empty_tab_mine_title')}
					</h3>
					<p class="text-xs text-zinc-500 dark:text-zinc-400">
						{t('recipes.empty_tab_mine_desc')}
					</p>
				{:else if activeTab === 'shared'}
					<h3 class="text-base font-bold text-zinc-900 dark:text-white">
						{t('recipes.empty_tab_shared_title')}
					</h3>
					<p class="text-xs text-zinc-500 dark:text-zinc-400">
						{t('recipes.empty_tab_shared_desc')}
					</p>
				{:else}
					<h3 class="text-base font-bold text-zinc-900 dark:text-white">
						{t('recipes.empty_tab_all_title')}
					</h3>
					<p class="text-xs text-zinc-500 dark:text-zinc-400">
						{t('recipes.empty_tab_all_desc')}
					</p>
				{/if}
			</div>
			{#if !searchTerm.trim() && activeTab !== 'shared'}
				<a
					href="/recipes/new"
					class="mt-5 flex items-center gap-2 rounded-xl bg-amber-500 px-4 py-2 text-xs font-bold text-zinc-950 shadow-md transition-all hover:bg-amber-400"
				>
					<Plus class="h-4 w-4 stroke-[2.5]" />
					<span>{t('recipes.create_first')}</span>
				</a>
			{/if}
		</div>
	{:else}
		<div class="grid grid-cols-1 gap-5 md:grid-cols-2 lg:grid-cols-3">
			{#each filteredRecipes as recipe}
				<article
					class="glass-panel group relative flex flex-col justify-between rounded-2xl border border-zinc-200/80 p-5 transition-all duration-200 hover:border-amber-500/40 hover:shadow-lg dark:border-white/[0.08]"
				>
					<div class="space-y-2">
						<div
							class="flex items-center justify-between gap-2 border-b border-zinc-200/60 pb-3 dark:border-white/5"
						>
							<div class="min-w-0 flex-1">
								<div class="flex flex-wrap items-center gap-2">
									<h3
										class="truncate text-base leading-snug font-bold text-zinc-900 dark:text-white"
									>
										{recipe.name}
									</h3>
									<span class="text-xs font-semibold text-amber-600 dark:text-amber-400">
										{recipe.beerStyle}
									</span>
								</div>
								<div class="mt-1 flex flex-wrap items-center gap-2 text-xs">
									{#if isRecipeOwner(recipe)}
										<span
											class="inline-flex items-center gap-1 rounded-md border border-amber-500/30 bg-amber-500/10 px-1.5 py-0.5 text-[10px] font-bold tracking-wider text-amber-700 uppercase dark:text-amber-400"
										>
											<UserCheck class="h-3 w-3" />
											{t('recipes.badges.my_recipe')}
										</span>
									{:else}
										<span
											class="inline-flex items-center gap-1 rounded-md border border-indigo-500/30 bg-indigo-500/10 px-1.5 py-0.5 text-[10px] font-bold tracking-wider text-indigo-700 uppercase dark:text-indigo-400"
										>
											<Globe class="h-3 w-3" />
											{t('recipes.badges.shared')}
										</span>
										{#if recipe.authorName}
											<span class="text-zinc-300 dark:text-zinc-700">•</span>
											<span
												class="flex items-center gap-1 text-[11px] text-zinc-500 dark:text-zinc-400"
											>
												<User class="h-3 w-3" />
												{t('recipes.by_author', { author: recipe.authorName })}
											</span>
										{/if}
									{/if}
								</div>
							</div>
							<BeerGlass srm={recipe.colorSrm} size="sm" showLabel class="shrink-0 self-center" />
						</div>

						{#if recipe.description}
							<p class="line-clamp-2 text-xs leading-relaxed text-zinc-600 dark:text-zinc-400">
								{recipe.description}
							</p>
						{/if}
					</div>

					<!-- Specs Metrics Matrix -->
					<div
						class="grid grid-cols-3 gap-2 border-t border-zinc-200/60 pt-3 text-center text-xs dark:border-white/5"
					>
						<div
							class="rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-2 dark:border-zinc-800/80 dark:bg-zinc-900/60"
						>
							<span class="block text-[10px] font-medium text-zinc-500 dark:text-zinc-400">ABV</span
							>
							<span class="font-mono font-bold text-emerald-600 dark:text-emerald-400"
								>{recipe.alcoholByVolume}%</span
							>
						</div>
						<div
							class="rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-2 dark:border-zinc-800/80 dark:bg-zinc-900/60"
						>
							<span class="block text-[10px] font-medium text-zinc-500 dark:text-zinc-400">IBU</span
							>
							<span class="font-mono font-bold text-sky-600 dark:text-sky-400"
								>{recipe.bitternessIbu}</span
							>
						</div>
						<div
							class="rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-2 dark:border-zinc-800/80 dark:bg-zinc-900/60"
						>
							<span class="block text-[10px] font-medium text-zinc-500 dark:text-zinc-400">OG</span>
							<span class="font-mono font-bold text-amber-600 dark:text-amber-400"
								>{formatNumber(recipe.originalGravity, 3)}</span
							>
						</div>
					</div>

					<!-- Card Actions: Edit/Clone, Export, Delete, Start Batch -->
					<div
						class="mt-4 flex items-center justify-between border-t border-zinc-200/60 pt-3 dark:border-white/5"
					>
						<div class="flex items-center gap-1.5">
							{#if isRecipeOwner(recipe)}
								<!-- Edit (Owner only) -->
								<a
									href="/recipes/{recipe.id}/edit"
									class="flex h-9 w-9 items-center justify-center rounded-xl border border-zinc-200/80 bg-white text-zinc-600 transition-colors hover:border-amber-500/40 hover:bg-amber-50 hover:text-amber-600 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800 dark:hover:text-amber-400"
									title={t('recipes.edit_recipe')}
									aria-label={t('recipes.edit_recipe')}
								>
									<Pencil class="h-4 w-4" />
								</a>
							{:else}
								<!-- Clone / Fork (Shared recipes) -->
								<button
									type="button"
									onclick={() => handleCloneRecipe(recipe)}
									disabled={cloningRecipeId === recipe.id}
									class="flex h-9 w-9 cursor-pointer items-center justify-center rounded-xl border border-zinc-200/80 bg-white text-zinc-600 transition-colors hover:border-indigo-500/40 hover:bg-indigo-50 hover:text-indigo-600 disabled:opacity-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300 dark:hover:bg-indigo-950/40 dark:hover:text-indigo-400"
									title={t('recipes.clone_recipe')}
									aria-label={t('recipes.clone_recipe')}
								>
									{#if cloningRecipeId === recipe.id}
										<Loader2 class="h-4 w-4 animate-spin text-indigo-500" />
									{:else}
										<Copy class="h-4 w-4" />
									{/if}
								</button>
							{/if}

							<!-- Export (available to all) -->
							<button
								type="button"
								onclick={() => handleOpenExport(recipe)}
								disabled={exportLoadingId === recipe.id}
								class="flex h-9 w-9 cursor-pointer items-center justify-center rounded-xl border border-zinc-200/80 bg-white text-zinc-600 transition-colors hover:border-amber-500/40 hover:bg-amber-50 hover:text-amber-600 disabled:opacity-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800 dark:hover:text-amber-400"
								title={t('recipes.export_recipe')}
								aria-label={t('recipes.export_recipe')}
							>
								{#if exportLoadingId === recipe.id}
									<Loader2 class="h-4 w-4 animate-spin text-amber-500" />
								{:else}
									<Download class="h-4 w-4" />
								{/if}
							</button>

							<!-- Delete (Owner only) -->
							{#if isRecipeOwner(recipe)}
								<button
									type="button"
									onclick={() => (recipeToDelete = recipe)}
									class="flex h-9 w-9 cursor-pointer items-center justify-center rounded-xl border border-zinc-200/80 bg-white text-zinc-400 transition-colors hover:border-red-500/40 hover:bg-red-50 hover:text-red-600 dark:border-zinc-800 dark:bg-zinc-900 dark:hover:bg-red-950/40 dark:hover:text-red-400"
									title={t('recipes.delete_recipe')}
									aria-label={t('recipes.delete_recipe')}
								>
									<Trash2 class="h-4 w-4" />
								</button>
							{/if}
						</div>

						<!-- Start Batch (available for all recipes) -->
						<a
							href="/batches/new?recipeId={recipe.id}"
							class="flex min-h-[36px] items-center gap-1.5 rounded-xl bg-amber-500/10 px-3 py-1.5 text-xs font-bold text-amber-600 transition-colors hover:bg-amber-500 hover:text-zinc-950 dark:bg-amber-500/15 dark:text-amber-400 dark:hover:bg-amber-400 dark:hover:text-zinc-950"
						>
							<Play class="h-3 w-3 fill-current" />
							<span>{t('recipes.start_batch')}</span>
						</a>
					</div>
				</article>
			{/each}
		</div>
	{/if}
</div>

<!-- Delete Recipe Confirmation Modal -->
<DeleteRecipeModal
	open={recipeToDelete !== null}
	recipe={recipeToDelete}
	onClose={() => (recipeToDelete = null)}
	onDeleted={handleRecipeDeleted}
/>

<!-- Import Recipe Modal -->
<RecipeImportModal
	open={isImportModalOpen}
	{catalogIngredients}
	existingRecipeNames={allRecipes.map((r) => r.name)}
	onClose={() => (isImportModalOpen = false)}
	onImportRecipe={handleImportRecipe}
	onIngredientsCreated={handleIngredientsCreated}
/>

<!-- Export Recipe Modal -->
<RecipeExportModal
	open={isExportModalOpen}
	recipe={recipeToExport}
	onClose={() => {
		isExportModalOpen = false;
		recipeToExport = null;
	}}
/>
