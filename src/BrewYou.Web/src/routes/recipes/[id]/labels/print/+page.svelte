<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { api, ApiClientError, resolveApiUrl } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import { type RecipeDetailDto, type RecipeLabelDto, getLabelImageUrl } from '$lib/types/api';
	import { Printer, ArrowLeft, Loader2 } from '@lucide/svelte';

	let recipeId = $derived(page.params.id);
	let recipe = $state<RecipeDetailDto | null>(null);
	let label = $state<RecipeLabelDto | null>(null);
	let loading = $state(true);
	let errorMessage = $state<string | null>(null);

	// Print settings
	let labelWidthMm = $state(65);
	let labelHeightMm = $state(90);
	let labelCount = $state(8);
	let showCutLines = $state(true);

	const PRESETS = [
		{ name: 'Small (50x70mm)', w: 50, h: 70, count: 12 },
		{ name: 'Standard (65x90mm)', w: 65, h: 90, count: 8 },
		{ name: 'Large (80x110mm)', w: 80, h: 110, count: 4 }
	];

	function applyPreset(p: (typeof PRESETS)[0]) {
		labelWidthMm = p.w;
		labelHeightMm = p.h;
		labelCount = p.count;
	}

	onMount(async () => {
		const currentRecipeId = recipeId;
		if (!currentRecipeId) {
			loading = false;
			return;
		}
		try {
			const [rec, lbl] = await Promise.all([
				api.recipes.getById(currentRecipeId),
				api.recipes.label.get(currentRecipeId)
			]);
			recipe = rec;
			label = lbl;
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

	function handlePrint() {
		if (typeof window !== 'undefined') {
			window.print();
		}
	}
</script>

<svelte:head>
	<title
		>{recipe ? `${recipe.name} — ${t('labels.print_sheet_title')}` : 'Print Labels'} | BrewYou</title
	>
</svelte:head>

<div
	class="print-container min-h-screen bg-zinc-100 p-4 font-sans text-zinc-900 sm:p-8 dark:bg-zinc-950 dark:text-zinc-100"
>
	<!-- Screen-only Controls Header -->
	<header
		class="no-print glass-panel mb-6 rounded-2xl border border-zinc-200/80 p-4 shadow-sm dark:border-white/[0.08]"
	>
		<div class="flex flex-wrap items-center justify-between gap-4">
			<div class="flex items-center gap-3">
				<a
					href="/recipes/{recipeId}"
					class="flex h-9 w-9 items-center justify-center rounded-xl border border-zinc-200 bg-white text-zinc-600 hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300"
					aria-label={t('common.back')}
				>
					<ArrowLeft class="h-4 w-4" />
				</a>
				<div>
					<h1 class="text-base font-bold text-zinc-900 dark:text-white">
						{recipe?.name ?? 'Recipe'} — {t('labels.print_sheet_title')}
					</h1>
					<p class="text-xs text-zinc-500 dark:text-zinc-400">
						{t('labels.print_sheet_desc')}
					</p>
				</div>
			</div>

			<!-- Print Action -->
			<button
				type="button"
				onclick={handlePrint}
				class="flex items-center gap-2 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-5 py-2.5 text-xs font-bold text-zinc-950 shadow-md transition-all hover:from-amber-400 hover:to-amber-500 active:scale-95"
			>
				<Printer class="h-4 w-4" />
				<span>{t('labels.print_action_btn')}</span>
			</button>
		</div>

		<!-- Print Configuration Bar -->
		<div
			class="mt-4 flex flex-wrap items-center gap-4 border-t border-zinc-200/60 pt-3 text-xs dark:border-white/5"
		>
			<!-- Presets -->
			<div class="flex items-center gap-1.5">
				<span class="font-semibold text-zinc-500">{t('labels.preset_sizes')}:</span>
				{#each PRESETS as p}
					<button
						type="button"
						onclick={() => applyPreset(p)}
						class="rounded-lg border border-zinc-200 bg-white px-2.5 py-1 text-[11px] font-medium text-zinc-700 hover:border-amber-500 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300"
					>
						{p.name}
					</button>
				{/each}
			</div>

			<!-- Width / Height Inputs -->
			<div class="flex items-center gap-2">
				<label for="label-w" class="font-semibold text-zinc-500">{t('labels.width_mm')}:</label>
				<input
					id="label-w"
					type="number"
					min="30"
					max="200"
					bind:value={labelWidthMm}
					class="h-7 w-16 rounded-md border border-zinc-300 bg-white px-2 font-mono text-xs dark:border-zinc-700 dark:bg-zinc-900"
				/>
				<label for="label-h" class="font-semibold text-zinc-500">{t('labels.height_mm')}:</label>
				<input
					id="label-h"
					type="number"
					min="30"
					max="300"
					bind:value={labelHeightMm}
					class="h-7 w-16 rounded-md border border-zinc-300 bg-white px-2 font-mono text-xs dark:border-zinc-700 dark:bg-zinc-900"
				/>
			</div>

			<!-- Count Input -->
			<div class="flex items-center gap-2">
				<label for="label-count" class="font-semibold text-zinc-500"
					>{t('labels.label_count')}:</label
				>
				<input
					id="label-count"
					type="number"
					min="1"
					max="40"
					bind:value={labelCount}
					class="h-7 w-16 rounded-md border border-zinc-300 bg-white px-2 font-mono text-xs dark:border-zinc-700 dark:bg-zinc-900"
				/>
			</div>

			<!-- Cut lines toggle -->
			<label class="flex cursor-pointer items-center gap-1.5">
				<input
					type="checkbox"
					bind:checked={showCutLines}
					class="rounded border-zinc-300 text-amber-500 focus:ring-amber-500"
				/>
				<span class="text-zinc-600 dark:text-zinc-300">{t('labels.show_cut_lines')}</span>
			</label>
		</div>
	</header>

	{#if loading}
		<div class="flex h-64 items-center justify-center">
			<Loader2 class="h-8 w-8 animate-spin text-amber-500" />
		</div>
	{:else if errorMessage}
		<div
			class="rounded-2xl border border-red-500/20 bg-red-500/10 p-4 text-xs text-red-600 dark:text-red-400"
		>
			{errorMessage}
		</div>
	{:else if recipe && label}
		<!-- Printable Labels Sheet Canvas -->
		<div
			class="sheet-canvas mx-auto flex flex-wrap justify-center gap-4 bg-white p-6 shadow-md print:m-0 print:gap-2 print:p-0 print:shadow-none"
		>
			{#each Array.from({ length: labelCount }, (_, i) => i) as idx}
				<div
					class="bottle-label-item relative overflow-hidden bg-white {showCutLines
						? 'border border-dashed border-zinc-300 print:border-zinc-400'
						: ''}"
					style="width: {labelWidthMm}mm; height: {labelHeightMm}mm;"
				>
					<img
						src={resolveApiUrl(getLabelImageUrl(label.imageUrls, 'bottle'))}
						alt="{recipe.name} label #{idx + 1}"
						class="h-full w-full object-cover"
					/>
				</div>
			{/each}
		</div>
	{/if}
</div>

<style>
	@media print {
		:global(body) {
			background: white !important;
			color: black !important;
			margin: 0 !important;
			padding: 0 !important;
		}

		.no-print {
			display: none !important;
		}

		.print-container {
			background: white !important;
			padding: 0 !important;
			min-height: auto !important;
		}

		.sheet-canvas {
			background: white !important;
			box-shadow: none !important;
			padding: 0 !important;
			gap: 4mm !important;
		}

		.bottle-label-item {
			page-break-inside: avoid;
			break-inside: avoid;
		}

		@page {
			size: A4 portrait;
			margin: 10mm;
		}
	}
</style>
