<script lang="ts">
  import { onMount } from 'svelte';
  import { goto } from '$app/navigation';
  import { api } from '$lib/api/client';
  import { auth } from '$lib/stores/auth.svelte';
  import {
    calculateBrewMetrics,
    srmToHexColor,
    type CalculatorItem
  } from '$lib/calculators/brewing';
  import type { IngredientDto, IngredientUsage } from '$lib/types/api';
  import {
    Plus,
    Trash2,
    Save,
    Sparkles,
    Gauge,
    Droplet,
    Flame,
    Layers,
    Info,
    CheckCircle2,
    AlertCircle
  } from '@lucide/svelte';

  // Form state
  let name = $state('Citra & Centennial IPA');
  let description = $state('Crisp, refreshing American IPA packed with citrus and pine aromas.');
  let beerStyle = $state('American IPA');
  let batchSizeLiters = $state(20.0);
  let boilTimeMinutes = $state(60);
  let efficiencyPercent = $state(72.0);
  let isPublic = $state(true);

  // Available ingredients from catalog
  let allIngredients = $state<IngredientDto[]>([]);
  let loadingIngredients = $state(true);

  // Recipe items
  interface FormIngredientItem {
    id: string;
    ingredientId: string;
    amount: number;
    unit: string;
    durationMinutes: number;
    usage: IngredientUsage;
  }

  let items = $state<FormIngredientItem[]>([]);
  let saving = $state(false);
  let saveError = $state<string | null>(null);

  onMount(async () => {
    try {
      allIngredients = await api.ingredients.list();

      // Seed with initial template recipe
      const paleMalt = allIngredients.find((i) => i.name.includes('Pale Malt'));
      const munich = allIngredients.find((i) => i.name.includes('Munich'));
      const citra = allIngredients.find((i) => i.name === 'Citra');
      const centennial = allIngredients.find((i) => i.name === 'Centennial');
      const yeast = allIngredients.find((i) => i.name.includes('SafAle US-05'));

      items = [
        {
          id: '1',
          ingredientId: paleMalt?.id || allIngredients[0]?.id || '',
          amount: 4.5,
          unit: 'kg',
          durationMinutes: 60,
          usage: 'Mash' as IngredientUsage
        },
        {
          id: '2',
          ingredientId: munich?.id || allIngredients[1]?.id || '',
          amount: 0.5,
          unit: 'kg',
          durationMinutes: 60,
          usage: 'Mash' as IngredientUsage
        },
        {
          id: '3',
          ingredientId: centennial?.id || allIngredients.find((i) => i.type === 'Hop')?.id || '',
          amount: 25,
          unit: 'g',
          durationMinutes: 60,
          usage: 'Boil' as IngredientUsage
        },
        {
          id: '4',
          ingredientId: citra?.id || allIngredients.find((i) => i.type === 'Hop')?.id || '',
          amount: 35,
          unit: 'g',
          durationMinutes: 10,
          usage: 'Boil' as IngredientUsage
        },
        {
          id: '5',
          ingredientId: yeast?.id || allIngredients.find((i) => i.type === 'Yeast')?.id || '',
          amount: 11.5,
          unit: 'g',
          durationMinutes: 0,
          usage: 'Primary' as IngredientUsage
        }
      ].filter((i) => i.ingredientId !== '');
    } catch (e) {
      console.error(e);
    } finally {
      loadingIngredients = false;
    }
  });

  // Reactive live calculations
  let calculatedMetrics = $derived.by(() => {
    const calcItems: CalculatorItem[] = items.map((item) => {
      const ing = allIngredients.find((i) => i.id === item.ingredientId);
      return {
        ingredient: ing!,
        amount: item.amount,
        durationMinutes: item.durationMinutes,
        usage: item.usage
      };
    });

    return calculateBrewMetrics(
      batchSizeLiters,
      efficiencyPercent,
      boilTimeMinutes,
      calcItems
    );
  });

  function addItem(type: 'Fermentable' | 'Hop' | 'Yeast') {
    const defaultIng = allIngredients.find((i) => i.type === type);
    if (!defaultIng) return;

    items.push({
      id: Math.random().toString(36).substring(2, 9),
      ingredientId: defaultIng.id,
      amount: type === 'Fermentable' ? 1.0 : type === 'Hop' ? 20 : 11.5,
      unit: type === 'Fermentable' ? 'kg' : 'g',
      durationMinutes: type === 'Hop' ? 15 : 60,
      usage: type === 'Fermentable' ? 'Mash' : type === 'Hop' ? 'Boil' : 'Primary'
    });
  }

  function removeItem(id: string) {
    items = items.filter((i) => i.id !== id);
  }

  async function handleSave() {
    if (!auth.isAuthenticated) {
      goto('/login');
      return;
    }

    saving = true;
    saveError = null;

    try {
      await api.recipes.create({
        name,
        description,
        beerStyle,
        batchSizeLiters,
        boilTimeMinutes,
        efficiencyPercent,
        isPublic,
        ingredients: items.map((item) => ({
          ingredientId: item.ingredientId,
          amount: item.amount,
          unit: item.unit,
          durationMinutes: item.durationMinutes,
          usage: item.usage
        }))
      });

      goto('/recipes');
    } catch (err: any) {
      saveError = err.message || 'Failed to save recipe.';
    } finally {
      saving = false;
    }
  }
</script>

<div class="space-y-8">
  <!-- Title & Save Action -->
  <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-slate-800 pb-6">
    <div>
      <h1 class="text-2xl sm:text-3xl font-bold text-white flex items-center gap-2.5">
        <Sparkles class="w-7 h-7 text-amber-400" />
        Recipe Formulator
      </h1>
      <p class="text-sm text-slate-400 mt-1">
        Design and calibrate your grain bill, hop schedule, and yeast profile with live telemetry.
      </p>
    </div>

    <div class="flex items-center gap-3">
      <button
        onclick={handleSave}
        disabled={saving}
        class="px-5 py-2.5 rounded-xl bg-amber-500 hover:bg-amber-400 text-slate-950 font-bold text-sm flex items-center gap-2 shadow-lg shadow-amber-500/20 transition-all disabled:opacity-50 cursor-pointer"
      >
        <Save class="w-4 h-4" />
        <span>{saving ? 'Saving...' : auth.isAuthenticated ? 'Save Recipe' : 'Sign In to Save'}</span>
      </button>
    </div>
  </div>

  {#if saveError}
    <div class="p-4 rounded-xl bg-red-950/50 border border-red-800 text-red-300 text-sm flex items-center gap-2">
      <AlertCircle class="w-5 h-5 flex-shrink-0" />
      <span>{saveError}</span>
    </div>
  {/if}

  <!-- Live Metrics HUD -->
  <div class="grid grid-cols-2 sm:grid-cols-5 gap-3 p-4 rounded-2xl bg-slate-900/80 border border-slate-800 sticky top-20 z-40 backdrop-blur shadow-xl">
    <!-- OG -->
    <div class="p-3 bg-slate-950/70 rounded-xl border border-slate-800/80 flex flex-col">
      <span class="text-xs text-slate-400 font-medium">Original Gravity</span>
      <span class="text-xl sm:text-2xl font-mono font-bold text-amber-400 mt-1">
        {calculatedMetrics.originalGravity.toFixed(3)}
      </span>
      <span class="text-[10px] text-slate-500 mt-0.5">Target pre-ferment</span>
    </div>

    <!-- FG -->
    <div class="p-3 bg-slate-950/70 rounded-xl border border-slate-800/80 flex flex-col">
      <span class="text-xs text-slate-400 font-medium">Final Gravity</span>
      <span class="text-xl sm:text-2xl font-mono font-bold text-slate-200 mt-1">
        {calculatedMetrics.finalGravity.toFixed(3)}
      </span>
      <span class="text-[10px] text-slate-500 mt-0.5">Estimated finish</span>
    </div>

    <!-- ABV -->
    <div class="p-3 bg-slate-950/70 rounded-xl border border-slate-800/80 flex flex-col">
      <span class="text-xs text-slate-400 font-medium">Est. ABV</span>
      <span class="text-xl sm:text-2xl font-mono font-bold text-emerald-400 mt-1">
        {calculatedMetrics.alcoholByVolume.toFixed(2)}%
      </span>
      <span class="text-[10px] text-slate-500 mt-0.5">Alcohol by volume</span>
    </div>

    <!-- IBU -->
    <div class="p-3 bg-slate-950/70 rounded-xl border border-slate-800/80 flex flex-col">
      <span class="text-xs text-slate-400 font-medium">Bitterness</span>
      <span class="text-xl sm:text-2xl font-mono font-bold text-sky-400 mt-1">
        {calculatedMetrics.bitternessIbu.toFixed(1)} <span class="text-xs font-normal text-slate-400">IBU</span>
      </span>
      <span class="text-[10px] text-slate-500 mt-0.5">Tinseth method</span>
    </div>

    <!-- SRM Color -->
    <div class="p-3 bg-slate-950/70 rounded-xl border border-slate-800/80 col-span-2 sm:col-span-1 flex items-center justify-between">
      <div>
        <span class="text-xs text-slate-400 font-medium block">Color</span>
        <span class="text-xl sm:text-2xl font-mono font-bold text-white mt-1 block">
          {calculatedMetrics.colorSrm.toFixed(1)} <span class="text-xs font-normal text-slate-400">SRM</span>
        </span>
        <span class="text-[10px] text-slate-500">Morey formula</span>
      </div>
      <div
        class="w-9 h-9 rounded-xl border-2 border-slate-600 shadow-inner"
        style="background-color: {srmToHexColor(calculatedMetrics.colorSrm)}"
        title="{calculatedMetrics.colorSrm} SRM"
      ></div>
    </div>
  </div>

  <!-- Recipe Parameters & Batch Specs -->
  <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
    <div class="lg:col-span-2 space-y-6">
      <!-- General Specs Card -->
      <div class="p-6 rounded-2xl bg-slate-900/50 border border-slate-800 space-y-4">
        <h2 class="text-base font-bold text-white flex items-center gap-2">
          <Info class="w-4 h-4 text-amber-400" />
          Recipe Overview
        </h2>

        <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div>
            <label for="recipe-name" class="block text-xs font-medium text-slate-400 mb-1.5">Recipe Name</label>
            <input
              id="recipe-name"
              type="text"
              bind:value={name}
              class="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-lg text-sm text-white focus:outline-none focus:border-amber-500/50"
            />
          </div>

          <div>
            <label for="beer-style" class="block text-xs font-medium text-slate-400 mb-1.5">Beer Style (BJCP)</label>
            <input
              id="beer-style"
              type="text"
              bind:value={beerStyle}
              class="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-lg text-sm text-white focus:outline-none focus:border-amber-500/50"
            />
          </div>

          <div class="sm:col-span-2">
            <label for="recipe-description" class="block text-xs font-medium text-slate-400 mb-1.5">Description & Brewer's Notes</label>
            <textarea
              id="recipe-description"
              bind:value={description}
              rows="2"
              class="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-lg text-sm text-white focus:outline-none focus:border-amber-500/50 resize-none"
            ></textarea>
          </div>
        </div>
      </div>

      <!-- Ingredient Schedule Card -->
      <div class="p-6 rounded-2xl bg-slate-900/50 border border-slate-800 space-y-4">
        <div class="flex items-center justify-between">
          <h2 class="text-base font-bold text-white flex items-center gap-2">
            <Layers class="w-4 h-4 text-amber-400" />
            Ingredients Bill
          </h2>

          <div class="flex items-center gap-2">
            <button
              onclick={() => addItem('Fermentable')}
              class="px-2.5 py-1.5 rounded-lg bg-amber-500/10 hover:bg-amber-500/20 text-amber-400 border border-amber-500/20 text-xs font-medium flex items-center gap-1 transition-colors"
            >
              <Plus class="w-3.5 h-3.5" /> +Grain
            </button>
            <button
              onclick={() => addItem('Hop')}
              class="px-2.5 py-1.5 rounded-lg bg-emerald-500/10 hover:bg-emerald-500/20 text-emerald-400 border border-emerald-500/20 text-xs font-medium flex items-center gap-1 transition-colors"
            >
              <Plus class="w-3.5 h-3.5" /> +Hop
            </button>
            <button
              onclick={() => addItem('Yeast')}
              class="px-2.5 py-1.5 rounded-lg bg-sky-500/10 hover:bg-sky-500/20 text-sky-400 border border-sky-500/20 text-xs font-medium flex items-center gap-1 transition-colors"
            >
              <Plus class="w-3.5 h-3.5" /> +Yeast
            </button>
          </div>
        </div>

        {#if loadingIngredients}
          <div class="py-8 text-center text-xs text-slate-500">Loading ingredient catalog...</div>
        {:else}
          <div class="space-y-3">
            {#each items as item (item.id)}
              {@const selectedIng = allIngredients.find((i) => i.id === item.ingredientId)}
              <div class="p-3.5 rounded-xl bg-slate-950/80 border border-slate-800/80 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
                <div class="flex-1 w-full sm:w-auto">
                  <select
                    bind:value={item.ingredientId}
                    class="w-full px-3 py-1.5 bg-slate-900 border border-slate-800 rounded-lg text-sm text-white font-medium focus:outline-none focus:border-amber-500/50"
                  >
                    {#each allIngredients as ing}
                      <option value={ing.id}>
                        [{ing.type}] {ing.name}
                      </option>
                    {/each}
                  </select>
                </div>

                <div class="flex items-center gap-2 w-full sm:w-auto justify-end">
                  <div class="flex items-center gap-1">
                    <input
                      type="number"
                      step={selectedIng?.type === 'Fermentable' ? '0.1' : '1'}
                      min="0"
                      bind:value={item.amount}
                      class="w-20 px-2 py-1.5 bg-slate-900 border border-slate-800 rounded-lg text-sm text-right text-white font-mono focus:outline-none"
                    />
                    <span class="text-xs text-slate-400 w-6">{item.unit}</span>
                  </div>

                  {#if selectedIng?.type === 'Hop'}
                    <div class="flex items-center gap-1">
                      <input
                        type="number"
                        min="0"
                        bind:value={item.durationMinutes}
                        class="w-16 px-2 py-1.5 bg-slate-900 border border-slate-800 rounded-lg text-sm text-right text-white font-mono focus:outline-none"
                      />
                      <span class="text-xs text-slate-400">min</span>
                    </div>
                  {/if}

                  <button
                    onclick={() => removeItem(item.id)}
                    class="p-1.5 text-slate-500 hover:text-red-400 rounded-lg hover:bg-slate-900 transition-colors"
                  >
                    <Trash2 class="w-4 h-4" />
                  </button>
                </div>
              </div>
            {/each}
          </div>
        {/if}
      </div>
    </div>

    <!-- Batch Equipment & Calibration Sidebar -->
    <div class="space-y-6">
      <div class="p-6 rounded-2xl bg-slate-900/50 border border-slate-800 space-y-4">
        <h2 class="text-base font-bold text-white flex items-center gap-2">
          <Gauge class="w-4 h-4 text-amber-400" />
          Batch Calibration
        </h2>

        <div class="space-y-4">
          <div>
            <div class="flex items-center justify-between text-xs font-medium text-slate-400 mb-1.5">
              <span>Target Batch Size</span>
              <span class="text-white font-mono">{batchSizeLiters} Liters</span>
            </div>
            <input
              type="range"
              min="5"
              max="100"
              step="1"
              bind:value={batchSizeLiters}
              class="w-full accent-amber-500 cursor-pointer"
            />
          </div>

          <div>
            <div class="flex items-center justify-between text-xs font-medium text-slate-400 mb-1.5">
              <span>Boil Duration</span>
              <span class="text-white font-mono">{boilTimeMinutes} Minutes</span>
            </div>
            <input
              type="range"
              min="30"
              max="120"
              step="5"
              bind:value={boilTimeMinutes}
              class="w-full accent-amber-500 cursor-pointer"
            />
          </div>

          <div>
            <div class="flex items-center justify-between text-xs font-medium text-slate-400 mb-1.5">
              <span>Brewhouse Efficiency</span>
              <span class="text-white font-mono">{efficiencyPercent}%</span>
            </div>
            <input
              type="range"
              min="50"
              max="90"
              step="1"
              bind:value={efficiencyPercent}
              class="w-full accent-amber-500 cursor-pointer"
            />
          </div>
        </div>
      </div>
    </div>
  </div>
</div>
