<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { api } from '$lib/api/client';
	import type { IngredientDto, RecipeDetailDto } from '$lib/types/api';
	import { t } from '$lib/i18n/index.svelte';
	import RecipeFormulator from '$lib/components/recipes/RecipeFormulator.svelte';
	import { Loader2 } from '@lucide/svelte';

	let recipeId = $derived(page.params.id);
	let recipe = $state<RecipeDetailDto | null>(null);
	let catalogIngredients = $state<IngredientDto[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);

	onMount(async () => {
		if (!recipeId) {
			error = t('formulator.invalid_id');
			loading = false;
			return;
		}

		try {
			const [loadedRecipe, loadedIngredients] = await Promise.all([
				api.recipes.getById(recipeId),
				api.ingredients.list()
			]);
			recipe = loadedRecipe;
			catalogIngredients = loadedIngredients;
		} catch (err: unknown) {
			error = (err as Error).message || t('formulator.load_error');
		} finally {
			loading = false;
		}
	});
</script>

<svelte:head>
	<title>{recipe?.name ? `${recipe.name} — ` : ''}{t('formulator.title_edit')} — BrewYou</title>
</svelte:head>

{#if loading}
	<div class="flex flex-col items-center justify-center gap-3 py-24 text-zinc-400">
		<Loader2 class="h-8 w-8 animate-spin text-amber-500" />
		<span class="text-sm text-zinc-600 dark:text-zinc-400">{t('common.loading')}</span>
	</div>
{:else if error || !recipe}
	<div
		class="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
	>
		{error || t('formulator.not_found')}
	</div>
{:else}
	<RecipeFormulator mode="edit" initialRecipe={recipe} {catalogIngredients} />
{/if}
