<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import type { ExportFormat, ExportableRecipe } from '$lib/exporters/types';
	import { analyzeBeerXmlCompatibility, serializeBeerXml } from '$lib/exporters/beerxml';
	import { analyzeBeerJsonCompatibility, serializeBeerJson } from '$lib/exporters/beerjson';
	import { sanitizeFilename, triggerFileDownload } from '$lib/utils/downloadFile';
	import {
		Download,
		X,
		FileCode2,
		FileText,
		CheckCircle2,
		AlertTriangle,
		Loader2
	} from '@lucide/svelte';

	interface Props {
		open: boolean;
		recipe: ExportableRecipe | null;
		onClose: () => void;
	}

	let { open, recipe, onClose }: Props = $props();

	let selectedFormat = $state<ExportFormat>('beerxml');
	let isExporting = $state(false);

	let compatibilityReport = $derived.by(() => {
		if (!recipe) {
			return {
				format: selectedFormat,
				isCompatible: true,
				warnings: []
			};
		}
		if (selectedFormat === 'beerxml') {
			return analyzeBeerXmlCompatibility(recipe);
		} else {
			return analyzeBeerJsonCompatibility(recipe);
		}
	});

	let filenamePreview = $derived.by(() => {
		if (!recipe) return '';
		return sanitizeFilename(recipe.name, selectedFormat === 'beerxml' ? 'xml' : 'json');
	});

	function handleExport() {
		if (!recipe) return;
		isExporting = true;

		try {
			const result =
				selectedFormat === 'beerxml' ? serializeBeerXml(recipe) : serializeBeerJson(recipe);

			triggerFileDownload(result.content, result.filename, result.mimeType);
			onClose();
		} finally {
			isExporting = false;
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && open) {
			onClose();
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open && recipe}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4"
		role="dialog"
		aria-modal="true"
		aria-labelledby="export-recipe-modal-title"
	>
		<!-- Backdrop -->
		<div
			class="fixed inset-0 bg-black/70 backdrop-blur-sm transition-opacity"
			onclick={onClose}
			aria-hidden="true"
		></div>

		<!-- Dialog Panel -->
		<div
			class="relative z-10 max-h-[90vh] w-full max-w-lg overflow-y-auto rounded-2xl border border-zinc-200 bg-white p-6 shadow-2xl transition-all dark:border-zinc-800 dark:bg-zinc-950"
		>
			<!-- Header -->
			<div
				class="flex items-start justify-between border-b border-zinc-200 pb-4 dark:border-zinc-800"
			>
				<div class="flex items-center gap-3">
					<div
						class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
					>
						<Download class="h-5 w-5" />
					</div>
					<div>
						<h2
							id="export-recipe-modal-title"
							class="text-lg font-bold text-zinc-900 dark:text-white"
						>
							{t('recipes.export_modal.title')}
						</h2>
						<p class="text-xs text-zinc-500 dark:text-zinc-400">
							{t('recipes.export_modal.subtitle', { name: recipe.name })}
						</p>
					</div>
				</div>

				<button
					type="button"
					onclick={onClose}
					class="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('recipes.export_modal.cancel')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<!-- Body -->
			<div class="space-y-5 py-4">
				<!-- Format Selection -->
				<fieldset class="space-y-2">
					<legend
						class="text-xs font-bold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
					>
						{t('recipes.export_modal.format_legend')}
					</legend>

					<div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
						<!-- BeerXML Option -->
						<label
							class="relative flex cursor-pointer flex-col justify-between rounded-xl border p-3.5 transition-all {selectedFormat ===
							'beerxml'
								? 'border-amber-500 bg-amber-500/10 shadow-xs dark:bg-amber-500/10'
								: 'border-zinc-200 hover:border-amber-500/50 dark:border-zinc-800 dark:hover:border-amber-500/50'}"
						>
							<div class="flex items-start justify-between gap-2">
								<div class="flex items-center gap-2">
									<FileCode2 class="h-4 w-4 text-amber-500" />
									<span class="text-sm font-bold text-zinc-900 dark:text-white">
										{t('recipes.export_modal.format_beerxml')}
									</span>
								</div>
								<input
									type="radio"
									name="exportFormat"
									value="beerxml"
									checked={selectedFormat === 'beerxml'}
									onchange={() => (selectedFormat = 'beerxml')}
									class="text-amber-500 focus:ring-amber-500"
								/>
							</div>
							<p class="mt-2 text-xs leading-relaxed text-zinc-500 dark:text-zinc-400">
								{t('recipes.export_modal.format_beerxml_desc')}
							</p>
						</label>

						<!-- BeerJSON Option -->
						<label
							class="relative flex cursor-pointer flex-col justify-between rounded-xl border p-3.5 transition-all {selectedFormat ===
							'beerjson'
								? 'border-amber-500 bg-amber-500/10 shadow-xs dark:bg-amber-500/10'
								: 'border-zinc-200 hover:border-amber-500/50 dark:border-zinc-800 dark:hover:border-amber-500/50'}"
						>
							<div class="flex items-start justify-between gap-2">
								<div class="flex items-center gap-2">
									<FileText class="h-4 w-4 text-amber-500" />
									<span class="text-sm font-bold text-zinc-900 dark:text-white">
										{t('recipes.export_modal.format_beerjson')}
									</span>
								</div>
								<input
									type="radio"
									name="exportFormat"
									value="beerjson"
									checked={selectedFormat === 'beerjson'}
									onchange={() => (selectedFormat = 'beerjson')}
									class="text-amber-500 focus:ring-amber-500"
								/>
							</div>
							<p class="mt-2 text-xs leading-relaxed text-zinc-500 dark:text-zinc-400">
								{t('recipes.export_modal.format_beerjson_desc')}
							</p>
						</label>
					</div>
				</fieldset>

				<!-- Fidelity & Format Compatibility Panel -->
				<div aria-live="polite">
					{#if compatibilityReport.warnings.length > 0}
						<!-- Incompatibility / Data Loss Notification -->
						<div
							class="space-y-2.5 rounded-xl border border-amber-500/30 bg-amber-500/10 p-4 dark:border-amber-500/25 dark:bg-amber-500/5"
						>
							<div class="flex items-start gap-2.5 text-amber-700 dark:text-amber-400">
								<AlertTriangle class="mt-0.5 h-4 w-4 shrink-0" />
								<div>
									<h4 class="text-xs font-bold tracking-wider uppercase">
										{t('recipes.export_modal.fidelity_warning_title')}
									</h4>
									<p class="mt-0.5 text-xs text-zinc-600 dark:text-zinc-400">
										{t('recipes.export_modal.fidelity_warning_desc')}
									</p>
								</div>
							</div>

							<!-- List of Data Loss Items -->
							<ul class="space-y-1.5 pt-1">
								{#each compatibilityReport.warnings as warning}
									<li
										class="flex items-start gap-2 rounded-lg border border-amber-500/20 bg-white/70 p-2.5 text-xs text-zinc-800 shadow-2xs dark:border-zinc-800 dark:bg-zinc-900/70 dark:text-zinc-200"
									>
										<span
											class="mt-1 h-1.5 w-1.5 shrink-0 rounded-full {warning.severity === 'warning'
												? 'bg-amber-500'
												: 'bg-sky-500'}"
										></span>
										<span class="leading-relaxed">
											{t(warning.i18nKey, warning.params) || warning.fallbackText}
										</span>
									</li>
								{/each}
							</ul>
						</div>
					{:else}
						<!-- 100% Compatible / No Loss Notification -->
						<div
							class="flex items-start gap-3 rounded-xl border border-emerald-500/25 bg-emerald-500/10 p-4 text-emerald-800 dark:border-emerald-500/20 dark:bg-emerald-500/5 dark:text-emerald-300"
						>
							<CheckCircle2
								class="mt-0.5 h-5 w-5 shrink-0 text-emerald-600 dark:text-emerald-400"
							/>
							<div>
								<h4 class="text-xs font-bold tracking-wider uppercase">
									{t('recipes.export_modal.fidelity_perfect_title')}
								</h4>
								<p class="mt-0.5 text-xs text-zinc-600 dark:text-zinc-400">
									{t('recipes.export_modal.fidelity_perfect_desc')}
								</p>
							</div>
						</div>
					{/if}
				</div>

				<!-- Output File Preview -->
				<div
					class="flex items-center justify-between rounded-xl border border-zinc-200/80 bg-zinc-50/80 px-3.5 py-2.5 text-xs dark:border-zinc-800/80 dark:bg-zinc-900/60"
				>
					<span class="font-medium text-zinc-500 dark:text-zinc-400">
						{t('recipes.export_modal.file_name_label')}
					</span>
					<span class="font-mono font-bold text-zinc-900 dark:text-white">
						{filenamePreview}
					</span>
				</div>
			</div>

			<!-- Footer -->
			<div
				class="flex items-center justify-end gap-3 border-t border-zinc-200 pt-4 dark:border-zinc-800"
			>
				<button
					type="button"
					onclick={onClose}
					disabled={isExporting}
					class="rounded-xl border border-zinc-200 px-4 py-2.5 text-sm font-semibold text-zinc-700 transition-colors hover:bg-zinc-100 disabled:opacity-50 dark:border-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-900"
				>
					{t('recipes.export_modal.cancel')}
				</button>

				<button
					type="button"
					onclick={handleExport}
					disabled={isExporting}
					class="flex min-h-[42px] cursor-pointer items-center gap-2 rounded-xl bg-amber-500 px-5 py-2.5 text-sm font-bold text-zinc-950 shadow-sm transition-all hover:bg-amber-400 active:scale-[0.98] disabled:opacity-50"
				>
					{#if isExporting}
						<Loader2 class="h-4 w-4 animate-spin" />
						<span>{t('recipes.export_modal.generating')}</span>
					{:else}
						<Download class="h-4 w-4 stroke-[2.5]" />
						<span>
							{t('recipes.export_modal.export_button', {
								format: selectedFormat === 'beerxml' ? '.xml' : '.json'
							})}
						</span>
					{/if}
				</button>
			</div>
		</div>
	</div>
{/if}
