<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { api, ApiClientError, resolveApiUrl } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import { type BatchDetailDto, type RecipeLabelDto, getLabelImageUrl } from '$lib/types/api';
	import { Printer, ArrowLeft, Loader2 } from '@lucide/svelte';

	let batchId = $derived(page.params.id);
	let batch = $state<BatchDetailDto | null>(null);
	let label = $state<RecipeLabelDto | null>(null);
	let loading = $state(true);
	let errorMessage = $state<string | null>(null);

	// Print settings
	let labelWidthMm = $state(65);
	let labelHeightMm = $state(90);
	let labelCount = $state(8);
	let showCutLines = $state(true);
	let showBatchMeta = $state(true);
	let showAbv = $state(true);

	function computeBatchAbv(b: BatchDetailDto | null): number | null {
		if (!b) return null;
		if (b.alcoholByVolume != null) return b.alcoholByVolume;
		const og = b.measuredOg ?? (b.targetOg > 1.0 ? b.targetOg : null);
		const fg = b.measuredFg ?? b.currentGravity;
		if (og != null && fg != null && og > fg) {
			return Math.round((og - fg) * 131.25 * 10) / 10;
		}
		return b.targetAbv ?? null;
	}

	let displayAbv = $derived(computeBatchAbv(batch));

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
		const currentBatchId = batchId;
		if (!currentBatchId) {
			loading = false;
			return;
		}
		try {
			const b = await api.batches.getById(currentBatchId);
			batch = b;
			if (b.recipeId) {
				label = await api.recipes.label.get(b.recipeId);
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
		>{batch
			? `${batch.name} (${batch.batchCode}) — ${t('labels.print_sheet_title')}`
			: 'Print Batch Labels'} | BrewYou</title
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
					href="/batches/{batchId}"
					class="flex h-9 w-9 items-center justify-center rounded-xl border border-zinc-200 bg-white text-zinc-600 hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300"
					aria-label={t('common.back')}
				>
					<ArrowLeft class="h-4 w-4" />
				</a>
				<div>
					<h1 class="text-base font-bold text-zinc-900 dark:text-white">
						{batch?.name ?? 'Batch'} ({batch?.batchCode}) — {t('labels.print_sheet_title')}
					</h1>
					<p class="text-xs text-zinc-500 dark:text-zinc-400">
						{t('labels.print_batch_sheet_desc')}
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

			<!-- Toggles -->
			<label class="flex cursor-pointer items-center gap-1.5">
				<input
					type="checkbox"
					bind:checked={showCutLines}
					class="rounded border-zinc-300 text-amber-500 focus:ring-amber-500"
				/>
				<span class="text-zinc-600 dark:text-zinc-300">{t('labels.show_cut_lines')}</span>
			</label>

			<label class="flex cursor-pointer items-center gap-1.5">
				<input
					type="checkbox"
					bind:checked={showAbv}
					class="rounded border-zinc-300 text-amber-500 focus:ring-amber-500"
				/>
				<span class="text-zinc-600 dark:text-zinc-300">{t('labels.include_abv')}</span>
			</label>

			<label class="flex cursor-pointer items-center gap-1.5">
				<input
					type="checkbox"
					bind:checked={showBatchMeta}
					class="rounded border-zinc-300 text-amber-500 focus:ring-amber-500"
				/>
				<span class="text-zinc-600 dark:text-zinc-300">{t('labels.show_batch_strip')}</span>
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
	{:else if batch && label}
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
					<!-- Bottle Artwork -->
					<div class="relative flex-1 overflow-hidden">
						<img
							src={resolveApiUrl(getLabelImageUrl(label.imageUrls, 'bottle'))}
							alt="{batch.name} label #{idx + 1}"
							class="h-full w-full object-cover"
						/>
					</div>

					<!-- Batch Meta Strip Footer -->
					{#if showBatchMeta}
						<div class="border-t border-zinc-200 bg-white px-2 py-1 text-zinc-900">
							<div class="flex items-center justify-between font-mono text-[8px]">
								<span class="font-bold text-amber-800">{batch.batchCode}</span>
								{#if showAbv && displayAbv != null}
									<span class="font-bold text-emerald-800">{displayAbv}%</span>
								{/if}
							</div>
							<div class="flex items-center justify-between text-[7px] text-zinc-600">
								<span class="truncate">{batch.recipeName}</span>
								<span>{new Date(batch.brewDate).toLocaleDateString()}</span>
							</div>
						</div>
					{:else if showAbv && displayAbv != null}
						<div
							class="border-t border-zinc-200 bg-white px-2 py-1 text-center font-mono text-[9px] font-bold text-zinc-900"
						>
							<span>{displayAbv}%</span>
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
