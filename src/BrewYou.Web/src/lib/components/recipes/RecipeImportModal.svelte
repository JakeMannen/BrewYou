<script lang="ts">
	import { SvelteSet } from 'svelte/reactivity';
	import { t } from '$lib/i18n/index.svelte';
	import { api } from '$lib/api/client';
	import type { IngredientDto, CreateIngredientRequest, IngredientType } from '$lib/types/api';
	import type { ParsedRecipe } from '$lib/parsers/types';
	import { settings } from '$lib/stores/settings.svelte';
	import { formatPotentialGravity, smartPotentialToSg } from '$lib/calculators/brewing';
	import { getLocalizedIngredientForm } from '$lib/i18n/ingredients';
	import { parseBeerXml } from '$lib/parsers/beerxml';
	import { parseBeerJson } from '$lib/parsers/beerjson';
	import RecipeRenameModal from '$lib/components/recipes/RecipeRenameModal.svelte';
	import {
		UploadCloud,
		FileText,
		CheckCircle2,
		AlertCircle,
		AlertTriangle,
		X,
		Loader2,
		PlusCircle,
		Ban
	} from '@lucide/svelte';

	interface Props {
		open: boolean;
		catalogIngredients: IngredientDto[];
		existingRecipeNames?: string[];
		checkNameConflictFn?: (name: string) => Promise<boolean> | boolean;
		onClose: () => void;
		onImportRecipe: (recipe: ParsedRecipe, updatedCatalog?: IngredientDto[]) => void;
		onIngredientsCreated?: (newIngredients: IngredientDto[]) => void;
	}

	let {
		open,
		catalogIngredients,
		existingRecipeNames,
		checkNameConflictFn,
		onClose,
		onImportRecipe,
		onIngredientsCreated
	}: Props = $props();

	let fileInput: HTMLInputElement | null = $state(null);
	let isDragging = $state(false);
	let isParsing = $state(false);
	let isCreatingIngredients = $state(false);
	let isCheckingName = $state(false);
	let isRenameModalOpen = $state(false);
	let parseError = $state<string | null>(null);
	let createError = $state<string | null>(null);
	let parsedRecipe = $state<ParsedRecipe | null>(null);

	$effect(() => {
		if (!open) {
			reset();
		}
	});

	function reset() {
		isDragging = false;
		isParsing = false;
		isCreatingIngredients = false;
		isCheckingName = false;
		isRenameModalOpen = false;
		parseError = null;
		createError = null;
		parsedRecipe = null;
		if (fileInput) fileInput.value = '';
	}

	async function handleFile(file: File) {
		reset();
		if (file.size > 2 * 1024 * 1024) {
			parseError = 'File size exceeds maximum limit of 2MB.';
			return;
		}

		isParsing = true;
		try {
			const text = await file.text();
			const lowerName = file.name.toLowerCase();

			let recipes: ParsedRecipe[];
			if (lowerName.endsWith('.json')) {
				recipes = parseBeerJson(text);
			} else if (
				lowerName.endsWith('.xml') ||
				lowerName.endsWith('.beerxml') ||
				text.trim().startsWith('<')
			) {
				recipes = parseBeerXml(text);
			} else {
				// Try JSON first, fallback to XML
				try {
					recipes = parseBeerJson(text);
				} catch {
					recipes = parseBeerXml(text);
				}
			}

			if (!recipes || recipes.length === 0 || !recipes[0].name) {
				parseError = t('recipes.import_modal.empty_recipe');
			} else {
				parsedRecipe = recipes[0];
			}
		} catch (err: unknown) {
			parseError = (err as Error).message || t('recipes.import_modal.invalid_file');
		} finally {
			isParsing = false;
		}
	}

	function handleDrop(e: DragEvent) {
		e.preventDefault();
		isDragging = false;
		if (e.dataTransfer?.files && e.dataTransfer.files.length > 0) {
			handleFile(e.dataTransfer.files[0]);
		}
	}

	function handleDragOver(e: DragEvent) {
		e.preventDefault();
		isDragging = true;
	}

	function handleDragLeave() {
		isDragging = false;
	}

	function handleInputChange(e: Event) {
		const target = e.target as HTMLInputElement;
		if (target.files && target.files.length > 0) {
			handleFile(target.files[0]);
		}
	}

	interface MissingIngredientItem {
		name: string;
		type: IngredientType;
		potentialGravity?: number;
		colorSrm?: number;
		alphaAcidPercent?: number;
		attenuationPercent?: number;
		description?: string;
		form?: string;
	}

	let missingIngredients = $derived.by<MissingIngredientItem[]>(() => {
		if (!parsedRecipe || !parsedRecipe.ingredients) return [];
		const missing: MissingIngredientItem[] = [];
		const seen = new SvelteSet<string>();

		for (const ing of parsedRecipe.ingredients) {
			const trimmedName = ing.name.trim();
			const lower = trimmedName.toLowerCase();
			if (!lower) continue;

			const match = catalogIngredients.some(
				(c) => c.name.toLowerCase() === lower || c.name.toLowerCase().includes(lower)
			);

			if (!match && !seen.has(lower)) {
				seen.add(lower);
				missing.push({
					name: trimmedName,
					type: ing.type,
					potentialGravity: ing.potentialGravity,
					colorSrm: ing.colorSrm,
					alphaAcidPercent: ing.alphaAcidPercent,
					attenuationPercent: ing.attenuationPercent,
					description: ing.notes,
					form: ing.form
				});
			}
		}

		return missing;
	});

	let matchedCount = $derived.by(() => {
		if (!parsedRecipe) return 0;
		return parsedRecipe.ingredients.filter((item) => {
			const lower = item.name.trim().toLowerCase();
			return catalogIngredients.some(
				(c) => c.name.toLowerCase() === lower || c.name.toLowerCase().includes(lower)
			);
		}).length;
	});

	function sanitizeString(str: string): string {
		return str.replace(/[<>]/g, '').trim();
	}

	function sanitizeNumber(
		val: number | undefined | null,
		min: number,
		max: number,
		fallback?: number
	): number | undefined {
		if (val === undefined || val === null || isNaN(val)) return fallback;
		return Math.min(Math.max(val, min), max);
	}

	async function handleCreateMissingAndImport() {
		if (!parsedRecipe || missingIngredients.length === 0) return;

		isCreatingIngredients = true;
		createError = null;

		try {
			const newlyCreated: IngredientDto[] = [];

			for (const missing of missingIngredients) {
				const sanitizedName = sanitizeString(missing.name) || 'Unnamed Ingredient';
				const sanitizedDesc = missing.description
					? sanitizeString(missing.description).substring(0, 1000)
					: null;

				// Master brewer calibrated defaults
				let potentialGravity: number | undefined = undefined;
				let colorSrm: number | undefined = undefined;
				let alphaAcidPercent: number | undefined = undefined;
				let attenuationPercent: number | undefined = undefined;

				if (missing.type === 'Fermentable') {
					potentialGravity = sanitizeNumber(
						smartPotentialToSg(missing.potentialGravity),
						1.0,
						1.2,
						1.037
					);
					colorSrm = sanitizeNumber(missing.colorSrm, 0, 1000, 2.0);
				} else if (missing.type === 'Hop') {
					alphaAcidPercent = sanitizeNumber(missing.alphaAcidPercent, 0, 100, 5.0);
				} else if (missing.type === 'Yeast') {
					attenuationPercent = sanitizeNumber(missing.attenuationPercent, 0, 100, 75.0);
				}

				const requestPayload: CreateIngredientRequest = {
					name: sanitizedName,
					type: missing.type,
					potentialGravity,
					colorSrm,
					alphaAcidPercent,
					attenuationPercent,
					description: sanitizedDesc,
					form:
						missing.form ??
						(missing.type === 'Hop' ? 'Pellet' : missing.type === 'Yeast' ? 'Dry' : null)
				};

				const created = await api.ingredients.create(requestPayload);
				newlyCreated.push(created);
			}

			const updatedCatalog = [...catalogIngredients, ...newlyCreated];

			if (onIngredientsCreated && newlyCreated.length > 0) {
				onIngredientsCreated(newlyCreated);
			}

			onImportRecipe(parsedRecipe, updatedCatalog);
			onClose();
		} catch (err: unknown) {
			createError = (err as Error).message || t('recipes.import_modal.create_failed');
		} finally {
			isCreatingIngredients = false;
		}
	}

	function handleConfirmImport() {
		if (!parsedRecipe) return;
		onImportRecipe(parsedRecipe, catalogIngredients);
		onClose();
	}

	async function checkNameConflict(name: string): Promise<boolean> {
		const trimmed = name.trim().toLowerCase();
		if (!trimmed) return false;

		if (checkNameConflictFn) {
			return await checkNameConflictFn(name.trim());
		}

		if (existingRecipeNames && existingRecipeNames.length > 0) {
			if (existingRecipeNames.some((n) => n.trim().toLowerCase() === trimmed)) {
				return true;
			}
		}

		try {
			return await api.recipes.checkName(name.trim());
		} catch {
			if (existingRecipeNames) {
				return existingRecipeNames.some((n) => n.trim().toLowerCase() === trimmed);
			}
			return false;
		}
	}

	async function executeImport() {
		if (!parsedRecipe) return;

		if (missingIngredients.length > 0) {
			await handleCreateMissingAndImport();
		} else {
			handleConfirmImport();
		}
	}

	async function handleStartImport() {
		if (!parsedRecipe) return;

		isCheckingName = true;
		createError = null;

		try {
			const hasConflict = await checkNameConflict(parsedRecipe.name);
			if (hasConflict) {
				isRenameModalOpen = true;
				return;
			}

			await executeImport();
		} catch (err: unknown) {
			createError = (err as Error).message || t('recipes.import_modal.create_failed');
		} finally {
			isCheckingName = false;
		}
	}

	function handleAbort() {
		onClose();
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && open && !isRenameModalOpen) {
			onClose();
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4"
		role="dialog"
		aria-modal="true"
		aria-labelledby="import-recipe-modal-title"
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
						<UploadCloud class="h-5 w-5" />
					</div>
					<div>
						<h2
							id="import-recipe-modal-title"
							class="text-lg font-bold text-zinc-900 dark:text-white"
						>
							{t('recipes.import_modal.title')}
						</h2>
						<p class="text-xs text-zinc-500 dark:text-zinc-400">
							{t('recipes.import_modal.subtitle')}
						</p>
					</div>
				</div>

				<button
					type="button"
					onclick={onClose}
					class="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('recipes.import_modal.cancel')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<!-- Body -->
			<div class="space-y-4 py-4">
				<!-- Dropzone -->
				<div
					role="button"
					tabindex="0"
					class="relative flex cursor-pointer flex-col items-center justify-center rounded-2xl border-2 border-dashed p-6 text-center transition-colors {isDragging
						? 'border-amber-500 bg-amber-500/10'
						: 'border-zinc-300 hover:border-amber-500/60 dark:border-zinc-700 dark:hover:border-amber-500/60'}"
					ondrop={handleDrop}
					ondragover={handleDragOver}
					ondragleave={handleDragLeave}
					onclick={() => fileInput?.click()}
					onkeydown={(e) => {
						if (e.key === 'Enter' || e.key === ' ') fileInput?.click();
					}}
				>
					<input
						type="file"
						accept=".xml,.beerxml,.json"
						class="hidden"
						bind:this={fileInput}
						onchange={handleInputChange}
					/>

					<div
						class="flex h-12 w-12 items-center justify-center rounded-full bg-zinc-100 text-zinc-500 dark:bg-zinc-800 dark:text-zinc-400"
					>
						{#if isParsing}
							<Loader2 class="h-6 w-6 animate-spin text-amber-500" />
						{:else}
							<UploadCloud class="h-6 w-6" />
						{/if}
					</div>

					<p class="mt-3 text-sm font-semibold text-zinc-800 dark:text-zinc-200">
						{t('recipes.import_modal.dropzone_text')}
					</p>
					<p class="mt-1 text-xs text-zinc-500 dark:text-zinc-400">
						{t('recipes.import_modal.dropzone_hint')}
					</p>
				</div>

				{#if parseError}
					<div
						class="flex items-start gap-2 rounded-xl border border-red-200 bg-red-50 p-3 text-xs text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
					>
						<AlertCircle class="mt-0.5 h-4 w-4 shrink-0 text-red-500" />
						<span>{parseError}</span>
					</div>
				{/if}

				<!-- Recipe Preview & Ingredients Reconciliation -->
				{#if parsedRecipe}
					<div
						class="space-y-3 rounded-xl border border-emerald-500/20 bg-emerald-500/5 p-4 dark:border-emerald-500/15"
					>
						<div class="flex items-center gap-2 text-emerald-700 dark:text-emerald-400">
							<CheckCircle2 class="h-4 w-4 shrink-0" />
							<span class="text-xs font-bold tracking-wider uppercase">
								{t('recipes.import_modal.preview_title')}
							</span>
						</div>

						<div class="grid grid-cols-2 gap-3 text-xs">
							<div>
								<span class="text-zinc-500 dark:text-zinc-400"
									>{t('recipes.import_modal.name')}:</span
								>
								<p class="font-bold text-zinc-900 dark:text-white">{parsedRecipe.name}</p>
							</div>
							<div>
								<span class="text-zinc-500 dark:text-zinc-400"
									>{t('recipes.import_modal.style')}:</span
								>
								<p class="font-bold text-zinc-900 dark:text-white">
									{parsedRecipe.beerStyle || 'Standard'}
								</p>
							</div>
							<div>
								<span class="text-zinc-500 dark:text-zinc-400"
									>{t('recipes.import_modal.batch_size')}:</span
								>
								<p class="font-mono font-bold text-zinc-900 dark:text-white">
									{settings.format(parsedRecipe.batchSizeLiters)}
								</p>
							</div>
							<div>
								<span class="text-zinc-500 dark:text-zinc-400"
									>{t('recipes.import_modal.boil_time')}:</span
								>
								<p class="font-mono font-bold text-zinc-900 dark:text-white">
									{parsedRecipe.boilTimeMinutes} min
								</p>
							</div>
						</div>

						<div
							class="space-y-1 border-t border-emerald-500/10 pt-2 text-[11px] text-zinc-600 dark:text-zinc-400"
						>
							<p>
								{t('recipes.import_modal.ingredients_found', {
									count: parsedRecipe.ingredients.length
								})} •
								{t('recipes.import_modal.ingredients_matched', {
									matched: matchedCount,
									total: parsedRecipe.ingredients.length
								})}
							</p>
							{#if parsedRecipe.mashSteps && parsedRecipe.mashSteps.length > 0}
								<p>
									{t('recipes.import_modal.mash_steps_found', {
										count: parsedRecipe.mashSteps.length,
										temp: settings.formatTemperature(parsedRecipe.mashSteps[0].temperatureC)
									})}
								</p>
							{/if}
							{#if parsedRecipe.fermentationSteps && parsedRecipe.fermentationSteps.length > 0}
								<p>
									{t('recipes.import_modal.fermentation_steps_found', {
										count: parsedRecipe.fermentationSteps.length,
										temp: settings.formatTemperature(
											parsedRecipe.fermentationSteps[0].targetTemperatureC
										)
									})}
								</p>
							{/if}
						</div>
					</div>

					<!-- Missing Ingredients Alert & Decision Panel -->
					{#if missingIngredients.length > 0}
						<div
							class="space-y-3 rounded-xl border border-amber-500/30 bg-amber-500/10 p-4 dark:border-amber-500/20 dark:bg-amber-500/5"
						>
							<div class="flex items-start gap-2.5 text-amber-700 dark:text-amber-400">
								<AlertTriangle class="mt-0.5 h-4 w-4 shrink-0" />
								<div>
									<h4 class="text-xs font-bold tracking-wider uppercase">
										{t('recipes.import_modal.missing_title')}
									</h4>
									<p class="mt-0.5 text-xs text-zinc-600 dark:text-zinc-400">
										{t('recipes.import_modal.missing_prompt')}
									</p>
								</div>
							</div>

							<!-- Missing Ingredients List -->
							<div class="max-h-40 space-y-1.5 overflow-y-auto pr-1">
								{#each missingIngredients as item}
									<div
										class="flex items-center justify-between rounded-lg border border-amber-500/20 bg-white/60 p-2 text-xs shadow-xs dark:border-zinc-800 dark:bg-zinc-900/60"
									>
										<div class="flex items-center gap-2">
											<span
												class="inline-flex items-center rounded-md px-1.5 py-0.5 text-[10px] font-semibold {item.type ===
												'Fermentable'
													? 'bg-amber-100 text-amber-800 dark:bg-amber-900/40 dark:text-amber-300'
													: item.type === 'Hop'
														? 'bg-emerald-100 text-emerald-800 dark:bg-emerald-900/40 dark:text-emerald-300'
														: item.type === 'Yeast'
															? 'bg-blue-100 text-blue-800 dark:bg-blue-900/40 dark:text-blue-300'
															: 'bg-zinc-100 text-zinc-800 dark:bg-zinc-800 dark:text-zinc-300'}"
											>
												{item.type}
											</span>
											<span class="font-medium text-zinc-900 dark:text-white">{item.name}</span>
										</div>

										<div class="text-[11px] text-zinc-500 dark:text-zinc-400">
											{#if item.form}
												<span
													class="mr-1 inline-block rounded border border-zinc-200 bg-zinc-100 px-1 py-0.5 text-[9px] font-semibold text-zinc-600 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
												>
													{getLocalizedIngredientForm(item.form)}
												</span>
											{/if}
											{#if item.type === 'Fermentable'}
												<span>
													{settings.formatColor(item.colorSrm ?? 2.0)}
													{#if item.potentialGravity}
														• {formatPotentialGravity(item.potentialGravity)}
													{/if}
												</span>
											{:else if item.type === 'Hop' && item.alphaAcidPercent}
												<span>{item.alphaAcidPercent}% AA</span>
											{:else if item.type === 'Yeast' && item.attenuationPercent}
												<span>{item.attenuationPercent}% Atten</span>
											{/if}
										</div>
									</div>
								{/each}
							</div>

							{#if createError}
								<div
									class="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 p-2.5 text-xs text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
								>
									<AlertCircle class="mt-0.5 h-4 w-4 shrink-0 text-red-500" />
									<span>{createError}</span>
								</div>
							{/if}
						</div>
					{/if}
				{/if}
			</div>

			<!-- Footer -->
			<div
				class="flex items-center justify-end gap-3 border-t border-zinc-200 pt-4 dark:border-zinc-800"
			>
				{#if parsedRecipe && missingIngredients.length > 0}
					<button
						type="button"
						onclick={handleAbort}
						disabled={isCreatingIngredients || isCheckingName}
						class="flex min-h-[42px] items-center gap-2 rounded-xl border border-red-300 bg-white px-4 py-2.5 text-sm font-semibold text-red-700 transition-colors hover:bg-red-50 disabled:opacity-50 dark:border-red-900/50 dark:bg-zinc-900 dark:text-red-400 dark:hover:bg-red-950/30"
					>
						<Ban class="h-4 w-4" />
						<span>{t('recipes.import_modal.abort_import')}</span>
					</button>

					<button
						type="button"
						onclick={handleStartImport}
						disabled={isCreatingIngredients || isCheckingName}
						class="flex min-h-[42px] items-center gap-2 rounded-xl bg-amber-500 px-5 py-2.5 text-sm font-bold text-zinc-950 shadow-sm transition-all hover:bg-amber-400 disabled:opacity-50"
						data-testid="import-create-missing-btn"
					>
						{#if isCreatingIngredients}
							<Loader2 class="h-4 w-4 animate-spin" />
							<span>{t('recipes.import_modal.creating_ingredients')}</span>
						{:else if isCheckingName}
							<Loader2 class="h-4 w-4 animate-spin" />
							<span>{t('recipes.import_modal.checking_name')}</span>
						{:else}
							<PlusCircle class="h-4 w-4" />
							<span>{t('recipes.import_modal.create_and_import')}</span>
						{/if}
					</button>
				{:else}
					<button
						type="button"
						onclick={onClose}
						disabled={isCheckingName}
						class="rounded-xl border border-zinc-200 px-4 py-2.5 text-sm font-semibold text-zinc-700 transition-colors hover:bg-zinc-100 disabled:opacity-50 dark:border-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-900"
					>
						{t('recipes.import_modal.cancel')}
					</button>

					<button
						type="button"
						onclick={handleStartImport}
						disabled={!parsedRecipe || isCheckingName}
						class="flex min-h-[42px] items-center gap-2 rounded-xl bg-amber-500 px-5 py-2.5 text-sm font-bold text-zinc-950 shadow-sm transition-all hover:bg-amber-400 disabled:opacity-50"
						data-testid="import-confirm-btn"
					>
						{#if isCheckingName}
							<Loader2 class="h-4 w-4 animate-spin" />
							<span>{t('recipes.import_modal.checking_name')}</span>
						{:else}
							<FileText class="h-4 w-4" />
							<span>{t('recipes.import_modal.import_button')}</span>
						{/if}
					</button>
				{/if}
			</div>
		</div>
	</div>
{/if}

{#if isRenameModalOpen && parsedRecipe}
	<RecipeRenameModal
		open={isRenameModalOpen}
		currentName={parsedRecipe.name}
		checkConflict={checkNameConflict}
		onRename={async (newName) => {
			if (!parsedRecipe) return;
			parsedRecipe.name = newName;
			isRenameModalOpen = false;
			await executeImport();
		}}
		onCancel={() => {
			isRenameModalOpen = false;
		}}
	/>
{/if}
