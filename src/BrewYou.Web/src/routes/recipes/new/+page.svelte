<script lang="ts">
	import { onMount } from 'svelte';
	import { api } from '$lib/api/client';
	import type { IngredientDto } from '$lib/types/api';
	import { t } from '$lib/i18n/index.svelte';
	import RecipeFormulator from '$lib/components/recipes/RecipeFormulator.svelte';
	import { Loader2 } from '@lucide/svelte';

	let catalogIngredients = $state<IngredientDto[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);

	onMount(async () => {
		try {
			catalogIngredients = await api.ingredients.list();
		} catch (err: unknown) {
			error = (err as Error).message || t('ingredients.load_error');
		} finally {
			loading = false;
		}
	});
</script>

<svelte:head>
	<title>{t('formulator.title_new')} — BrewYou</title>
</svelte:head>

{#if loading}
	<div class="flex flex-col items-center justify-center gap-3 py-24 text-zinc-400">
		<Loader2 class="h-8 w-8 animate-spin text-amber-500" />
		<span class="text-sm text-zinc-600 dark:text-zinc-400">{t('common.loading')}</span>
	</div>
{:else if error}
	<div
		class="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
	>
		{error}
	</div>
{:else}
	<RecipeFormulator mode="new" {catalogIngredients} />
{/if}
