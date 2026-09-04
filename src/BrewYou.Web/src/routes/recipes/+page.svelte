<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '$lib/api/client';
  import type { RecipeSummaryDto } from '$lib/types/api';
  import { srmToHexColor } from '$lib/calculators/brewing';
  import { t } from '$lib/i18n/index.svelte';
  import { BookOpen, PlusCircle, Search, Beer, Loader2 } from '@lucide/svelte';

  let recipes = $state<RecipeSummaryDto[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);
  let searchTerm = $state('');

  onMount(async () => {
    loading = true;
    error = null;
    try {
      recipes = await api.recipes.list();
    } catch (err: any) {
      error = err.message || 'Failed to fetch recipes.';
    } finally {
      loading = false;
    }
  });

  let filteredRecipes = $derived(
    recipes.filter((r) => {
      const term = searchTerm.toLowerCase();
      return r.name.toLowerCase().includes(term) || r.beerStyle.toLowerCase().includes(term);
    })
  );
</script>

<div class="space-y-6">
  <!-- Header -->
  <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
    <div>
      <h1 class="text-2xl sm:text-3xl font-bold text-white flex items-center gap-2.5">
        <BookOpen class="w-7 h-7 text-amber-400" />
        {t('recipes.title')}
      </h1>
      <p class="text-sm text-slate-400 mt-1">
        {t('recipes.subtitle')}
      </p>
    </div>

    <a
      href="/recipes/new"
      class="px-5 py-2.5 rounded-xl bg-amber-500 hover:bg-amber-400 text-slate-950 font-bold text-sm flex items-center gap-2 shadow-lg shadow-amber-500/20 transition-all cursor-pointer self-start sm:self-auto"
    >
      <PlusCircle class="w-4 h-4" />
      <span>{t('recipes.new_recipe')}</span>
    </a>
  </div>

  <!-- Search Bar -->
  <div class="relative max-w-md">
    <Search class="w-4 h-4 text-slate-500 absolute left-3 top-1/2 -translate-y-1/2" />
    <input
      type="text"
      placeholder={t('recipes.search_placeholder')}
      bind:value={searchTerm}
      class="w-full pl-9 pr-4 py-2 bg-slate-900 border border-slate-800 rounded-lg text-sm text-white placeholder-slate-500 focus:outline-none focus:border-amber-500/50"
    />
  </div>

  <!-- Content -->
  {#if loading}
    <div class="py-16 flex flex-col items-center justify-center text-slate-500 gap-3">
      <Loader2 class="w-8 h-8 animate-spin text-amber-400" />
      <span class="text-sm">{t('common.loading')}</span>
    </div>
  {:else if error}
    <div class="p-4 rounded-xl bg-red-950/40 border border-red-800 text-red-300 text-sm">
      {error}
    </div>
  {:else if filteredRecipes.length === 0}
    <div class="py-16 text-center text-slate-400 space-y-4 bg-slate-900/30 rounded-2xl border border-slate-800/80 p-8">
      <div class="w-12 h-12 rounded-full bg-slate-800 mx-auto flex items-center justify-center text-amber-400">
        <Beer class="w-6 h-6" />
      </div>
      <div class="space-y-1">
        <h3 class="text-base font-bold text-white">{t('recipes.empty_title')}</h3>
        <p class="text-xs text-slate-500">{t('recipes.empty_desc')}</p>
      </div>
      <a
        href="/recipes/new"
        class="inline-flex items-center gap-1.5 px-4 py-2 rounded-lg bg-amber-500 text-slate-950 font-bold text-xs hover:bg-amber-400 transition-colors"
      >
        <PlusCircle class="w-4 h-4" />
        <span>{t('recipes.create_first')}</span>
      </a>
    </div>
  {:else}
    <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
      {#each filteredRecipes as recipe}
        <div class="p-5 rounded-2xl bg-slate-900/60 border border-slate-800/80 hover:border-slate-700 transition-all flex flex-col justify-between gap-4">
          <div class="space-y-2">
            <div class="flex items-start justify-between gap-2">
              <div>
                <h3 class="font-bold text-white text-base leading-snug">{recipe.name}</h3>
                <span class="text-xs font-medium text-amber-400">{recipe.beerStyle}</span>
              </div>
              <div
                class="w-5 h-5 rounded-md border border-slate-600 flex-shrink-0 shadow-sm"
                style="background-color: {srmToHexColor(recipe.colorSrm)}"
                title="{recipe.colorSrm} SRM"
              ></div>
            </div>

            {#if recipe.description}
              <p class="text-xs text-slate-400 line-clamp-2 leading-relaxed">{recipe.description}</p>
            {/if}
          </div>

          <!-- Specs Metrics Matrix -->
          <div class="pt-3 border-t border-slate-800/80 grid grid-cols-3 gap-2 text-center text-xs">
            <div class="bg-slate-950/60 p-2 rounded-lg border border-slate-800/50">
              <span class="text-slate-500 block text-[10px]">ABV</span>
              <span class="font-mono font-bold text-emerald-400">{recipe.alcoholByVolume}%</span>
            </div>
            <div class="bg-slate-950/60 p-2 rounded-lg border border-slate-800/50">
              <span class="text-slate-500 block text-[10px]">IBU</span>
              <span class="font-mono font-bold text-sky-400">{recipe.bitternessIbu}</span>
            </div>
            <div class="bg-slate-950/60 p-2 rounded-lg border border-slate-800/50">
              <span class="text-slate-500 block text-[10px]">OG</span>
              <span class="font-mono font-bold text-amber-300">{recipe.originalGravity.toFixed(3)}</span>
            </div>
          </div>
        </div>
      {/each}
    </div>
  {/if}
</div>
