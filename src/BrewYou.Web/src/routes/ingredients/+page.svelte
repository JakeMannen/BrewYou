<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '$lib/api/client';
  import type { IngredientDto, IngredientType } from '$lib/types/api';
  import { Layers, Search, Wheat, Sparkles, Plus, Loader2 } from '@lucide/svelte';
  import { srmToHexColor } from '$lib/calculators/brewing';

  let ingredients = $state<IngredientDto[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);
  let selectedType = $state<string>('All');
  let searchTerm = $state<string>('');

  async function loadIngredients() {
    loading = true;
    error = null;
    try {
      const typeFilter = selectedType === 'All' ? undefined : (selectedType as IngredientType);
      ingredients = await api.ingredients.list(typeFilter, searchTerm);
    } catch (err: any) {
      error = err.message || 'Failed to load ingredients catalog.';
    } finally {
      loading = false;
    }
  }

  onMount(() => {
    loadIngredients();
  });

  function onTypeChange(type: string) {
    selectedType = type;
    loadIngredients();
  }

  function onSearchInput() {
    loadIngredients();
  }
</script>

<div class="space-y-6">
  <!-- Header -->
  <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
    <div>
      <h1 class="text-2xl sm:text-3xl font-bold text-white flex items-center gap-2.5">
        <Layers class="w-7 h-7 text-amber-400" />
        Ingredient Catalog
      </h1>
      <p class="text-sm text-slate-400 mt-1">
        Browse calibrated fermentables, aroma & bittering hops, and yeast strains.
      </p>
    </div>
  </div>

  <!-- Filters & Search -->
  <div class="flex flex-col sm:flex-row items-center justify-between gap-4 bg-slate-900/60 p-4 rounded-xl border border-slate-800">
    <!-- Type Filter Tabs -->
    <div class="flex items-center gap-1.5 p-1 bg-slate-950 rounded-lg border border-slate-800 text-xs w-full sm:w-auto overflow-x-auto">
      {#each ['All', 'Fermentable', 'Hop', 'Yeast'] as type}
        <button
          onclick={() => onTypeChange(type)}
          class="px-3 py-1.5 rounded-md font-medium transition-colors {selectedType === type ? 'bg-amber-500 text-slate-950 font-bold' : 'text-slate-400 hover:text-white'}"
        >
          {type === 'All' ? 'All Ingredients' : `${type}s`}
        </button>
      {/each}
    </div>

    <!-- Search Bar -->
    <div class="relative w-full sm:w-72">
      <Search class="w-4 h-4 text-slate-500 absolute left-3 top-1/2 -translate-y-1/2" />
      <input
        type="text"
        placeholder="Search ingredients..."
        bind:value={searchTerm}
        oninput={onSearchInput}
        class="w-full pl-9 pr-4 py-2 bg-slate-950 border border-slate-800 rounded-lg text-sm text-white placeholder-slate-500 focus:outline-none focus:border-amber-500/50"
      />
    </div>
  </div>

  <!-- Ingredient Grid -->
  {#if loading}
    <div class="py-16 flex flex-col items-center justify-center text-slate-500 gap-3">
      <Loader2 class="w-8 h-8 animate-spin text-amber-400" />
      <span class="text-sm">Fetching catalog ingredients...</span>
    </div>
  {:else if error}
    <div class="p-4 rounded-xl bg-red-950/40 border border-red-800 text-red-300 text-sm">
      {error}
    </div>
  {:else if ingredients.length === 0}
    <div class="py-12 text-center text-slate-500 text-sm bg-slate-900/30 rounded-xl border border-slate-800/60">
      No ingredients match the selected criteria.
    </div>
  {:else}
    <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
      {#each ingredients as item}
        <div class="p-5 rounded-xl bg-slate-900/60 border border-slate-800/80 hover:border-slate-700 transition-colors flex flex-col justify-between gap-4">
          <div class="space-y-2">
            <div class="flex items-center justify-between gap-2">
              <h3 class="font-bold text-white text-base leading-snug">{item.name}</h3>
              <span
                class="px-2 py-0.5 rounded text-[11px] font-semibold uppercase tracking-wider
                {item.type === 'Fermentable' ? 'bg-amber-500/10 text-amber-400 border border-amber-500/20' : ''}
                {item.type === 'Hop' ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20' : ''}
                {item.type === 'Yeast' ? 'bg-sky-500/10 text-sky-400 border border-sky-500/20' : ''}
                {item.type === 'Other' ? 'bg-purple-500/10 text-purple-400 border border-purple-500/20' : ''}"
              >
                {item.type}
              </span>
            </div>
            {#if item.description}
              <p class="text-xs text-slate-400 line-clamp-2 leading-relaxed">{item.description}</p>
            {/if}
          </div>

          <!-- Specs Badge Matrix -->
          <div class="pt-3 border-t border-slate-800/80 grid grid-cols-2 gap-2 text-xs">
            {#if item.type === 'Fermentable'}
              <div class="bg-slate-950/60 p-2 rounded-lg border border-slate-800/50">
                <span class="text-slate-500 block text-[10px]">Potential</span>
                <span class="font-mono font-medium text-slate-200">
                  {item.potentialGravity ? item.potentialGravity.toFixed(3) : '1.037'}
                </span>
              </div>
              <div class="bg-slate-950/60 p-2 rounded-lg border border-slate-800/50 flex items-center justify-between">
                <div>
                  <span class="text-slate-500 block text-[10px]">Color</span>
                  <span class="font-mono font-medium text-slate-200">{item.colorSrm ?? 0} SRM</span>
                </div>
                {#if item.colorSrm}
                  <div
                    class="w-4 h-4 rounded-full border border-slate-600 shadow-sm"
                    style="background-color: {srmToHexColor(item.colorSrm)}"
                    title="{item.colorSrm} SRM"
                  ></div>
                {/if}
              </div>
            {:else if item.type === 'Hop'}
              <div class="bg-slate-950/60 p-2 rounded-lg border border-slate-800/50">
                <span class="text-slate-500 block text-[10px]">Alpha Acids</span>
                <span class="font-mono font-medium text-emerald-400">{item.alphaAcidPercent}%</span>
              </div>
              <div class="bg-slate-950/60 p-2 rounded-lg border border-slate-800/50">
                <span class="text-slate-500 block text-[10px]">Purpose</span>
                <span class="font-medium text-slate-200">
                  {(item.alphaAcidPercent ?? 0) > 10 ? 'Bittering / Dual' : 'Aroma / Flavor'}
                </span>
              </div>
            {:else if item.type === 'Yeast'}
              <div class="bg-slate-950/60 p-2 rounded-lg border border-slate-800/50 col-span-2">
                <span class="text-slate-500 block text-[10px]">Typical Attenuation</span>
                <span class="font-mono font-medium text-sky-400">{item.attenuationPercent}%</span>
              </div>
            {/if}
          </div>
        </div>
      {/each}
    </div>
  {/if}
</div>
