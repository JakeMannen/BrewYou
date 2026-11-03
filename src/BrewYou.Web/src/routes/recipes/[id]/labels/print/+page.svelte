<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { api, ApiClientError, resolveApiUrl } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import {
		type RecipeDetailDto,
		type RecipeLabelDto,
		type BatchSummaryDto,
		getLabelImageUrl
	} from '$lib/types/api';
	import { Printer, ArrowLeft, Loader2, Info, CheckCircle2 } from '@lucide/svelte';

	let recipeId = $derived(page.params.id);
	let recipe = $state<RecipeDetailDto | null>(null);
	let label = $state<RecipeLabelDto | null>(null);
	let batches = $state<BatchSummaryDto[]>([]);
	let selectedBatchId = $state<string>('');
	let showAbv = $state(true);
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

	function computeBatchAbv(b: BatchSummaryDto | null): number | null {
		if (!b) return null;
		if (b.alcoholByVolume != null) return b.alcoholByVolume;
		const og = b.measuredOg ?? (b.targetOg > 1.0 ? b.targetOg : null);
		const fg = b.measuredFg ?? b.currentGravity;
		if (og != null && fg != null && og > fg) {
			return Math.round((og - fg) * 131.25 * 10) / 10;
		}
		return b.targetOg && b.targetFg
			? Math.round((b.targetOg - b.targetFg) * 131.25 * 10) / 10
			: null;
	}

	function formatAbv(val: number | null | undefined): string | null {
		if (val == null || isNaN(val)) return null;
		return val.toFixed(1);
	}

	let selectedBatch = $derived(batches.find((b) => b.id === selectedBatchId) ?? null);

	let displayAbv = $derived.by(() => {
		if (selectedBatch) {
			return formatAbv(computeBatchAbv(selectedBatch) ?? recipe?.alcoholByVolume ?? null);
		}
		return formatAbv(recipe?.alcoholByVolume ?? null);
	});

	let isTheoretical = $derived(!selectedBatch);

	onMount(async () => {
		const currentRecipeId = recipeId;
		if (!currentRecipeId) {
			loading = false;
			return;
		}
		try {
			const [rec, lbl, allBatches] = await Promise.all([
				api.recipes.getById(currentRecipeId),
				api.recipes.label.get(currentRecipeId),
				api.batches.list({ limit: 100 })
			]);
			recipe = rec;
			label = lbl;
			batches = allBatches.filter((b) => b.recipeId === currentRecipeId);
			const urlBatchId = page.url.searchParams.get('batchId');
			if (urlBatchId && batches.some((b) => b.id === urlBatchId)) {
				selectedBatchId = urlBatchId;
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

			<!-- Include ABV Toggle -->
			<label class="flex cursor-pointer items-center gap-1.5">
				<input
					type="checkbox"
					bind:checked={showAbv}
					class="rounded border-zinc-300 text-amber-500 focus:ring-amber-500"
				/>
				<span class="text-zinc-600 dark:text-zinc-300">{t('labels.include_abv')}</span>
			</label>

			<!-- Batch Selector Dropdown -->
			<div class="flex items-center gap-2">
				<label for="recipe-batch-select" class="font-semibold whitespace-nowrap text-zinc-500">
					{t('labels.select_batch_source')}:
				</label>
				<select
					id="recipe-batch-select"
					bind:value={selectedBatchId}
					class="h-7 rounded-md border border-zinc-300 bg-white px-2 text-xs text-zinc-800 shadow-xs dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-200"
				>
					<option value="">{t('labels.no_batch_theoretical')}</option>
					{#each batches as b}
						<option value={b.id}>
							{b.batchCode} — {b.name} ({b.alcoholByVolume != null
								? `${formatAbv(b.alcoholByVolume)}%`
								: computeBatchAbv(b) != null
									? `${formatAbv(computeBatchAbv(b))}%`
									: t('batches.workspace.stage_status_in_progress')})
						</option>
					{/each}
				</select>
			</div>
		</div>

		<!-- Note highlighting theoretical vs actual ABV -->
		{#if showAbv}
			<div class="mt-3">
				{#if !selectedBatch}
					<div
						class="flex items-center gap-2 rounded-xl border border-amber-500/30 bg-amber-500/10 px-3.5 py-2 text-xs text-amber-800 dark:border-amber-500/20 dark:bg-amber-500/15 dark:text-amber-300"
					>
						<Info class="h-4 w-4 shrink-0 text-amber-600 dark:text-amber-400" />
						<span>
							{t('labels.theoretical_abv_notice', {
								abv: displayAbv ?? formatAbv(recipe?.alcoholByVolume) ?? '0.0'
							})}
						</span>
					</div>
				{:else}
					<div
						class="flex items-center gap-2 rounded-xl border border-emerald-500/30 bg-emerald-500/10 px-3.5 py-2 text-xs text-emerald-800 dark:border-emerald-500/20 dark:bg-emerald-500/15 dark:text-emerald-300"
					>
						<CheckCircle2 class="h-4 w-4 shrink-0 text-emerald-600 dark:text-emerald-400" />
						<span>
							{t('labels.actual_batch_abv_notice', {
								code: selectedBatch.batchCode,
								abv: displayAbv ?? '0.0'
							})}
						</span>
					</div>
				{/if}
			</div>
		{/if}
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
					class="bottle-label-item relative flex flex-col justify-between overflow-hidden bg-white {showCutLines
						? 'border border-dashed border-zinc-300 print:border-zinc-400'
						: ''}"
					style="width: {labelWidthMm}mm; height: {labelHeightMm}mm;"
				>
					<div class="relative flex-1 overflow-hidden">
						<img
							src={resolveApiUrl(getLabelImageUrl(label.imageUrls, 'bottle'))}
							alt="{recipe.name} label #{idx + 1}"
							class="h-full w-full object-cover"
						/>
					</div>

					{#if showAbv && displayAbv != null}
						<div
							class="border-t border-zinc-200 bg-white px-2 py-1 text-center font-mono text-[9px] font-bold text-zinc-900"
						>
							<span>{displayAbv}%</span>
							{#if isTheoretical}
								<span class="ml-1 text-[7px] font-medium text-amber-800">
									({t('labels.theoretical_indicator')})
								</span>
							{/if}
						</div>
					{/if}
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
			-webkit-print-color-adjust: exact;
			print-color-adjust: exact;
		}

		@page {
			size: A4 portrait;
			margin: 10mm;
		}
	}
</style>
