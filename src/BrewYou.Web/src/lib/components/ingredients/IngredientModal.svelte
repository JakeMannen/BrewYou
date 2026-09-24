<script lang="ts">
	import { api, ApiClientError } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import { srmToHexColor, sgToPpg, ppgToSg } from '$lib/calculators/brewing';
	import type { IngredientDto, IngredientType, CreateIngredientRequest } from '$lib/types/api';
	import { X, Loader2, Wheat, Flower2, Dna, Sparkles, Check } from '@lucide/svelte';

	interface Props {
		open: boolean;
		ingredient?: IngredientDto | null;
		initialType?: IngredientType;
		existingIngredients?: IngredientDto[];
		onClose: () => void;
		onSaved: (item: IngredientDto) => void;
	}

	let {
		open,
		ingredient = null,
		initialType = 'Fermentable',
		existingIngredients = [],
		onClose,
		onSaved
	}: Props = $props();

	let name = $state('');
	let type = $state<IngredientType>('Fermentable');
	let potentialUnit = $state<'SG' | 'PPG'>('SG');
	let potentialValue = $state<number | ''>(1.037);
	let colorSrm = $state<number | ''>(2.0);
	let alphaAcidPercent = $state<number | ''>(5.5);
	let attenuationPercent = $state<number | ''>(75);
	let description = $state('');
	let initialStock = $state<number | ''>('');
	let stockUnit = $state<string>('kg');
	let form = $state<string>('');

	let isSubmitting = $state(false);
	let errorMessage = $state<string | null>(null);
	let fieldErrors = $state<Record<string, string>>({});

	let isDuplicateName = $derived.by(() => {
		const trimmed = name.trim().toLowerCase();
		if (!trimmed) return false;
		return existingIngredients.some((item) => {
			if (ingredient && item.id === ingredient.id) return false;
			return item.name.trim().toLowerCase() === trimmed;
		});
	});

	let srmHexColor = $derived(
		srmToHexColor(typeof colorSrm === 'number' && colorSrm >= 0 ? colorSrm : 0)
	);

	let hopPurposeHint = $derived(
		typeof alphaAcidPercent === 'number' && alphaAcidPercent > 10
			? t('ingredients.bittering_dual')
			: t('ingredients.aroma_flavor')
	);

	let yeastSensoryHint = $derived.by(() => {
		if (typeof attenuationPercent !== 'number') return null;
		if (attenuationPercent < 72) return t('ingredients.sensory_finish_malt');
		if (attenuationPercent <= 78) return t('ingredients.sensory_finish_balanced');
		if (attenuationPercent <= 85) return t('ingredients.sensory_finish_crisp');
		return t('ingredients.sensory_finish_very_dry');
	});

	$effect(() => {
		if (open) {
			if (ingredient) {
				name = ingredient.name;
				type = ingredient.type;
				potentialUnit = 'SG';
				potentialValue = ingredient.potentialGravity ?? '';
				colorSrm = ingredient.colorSrm ?? '';
				alphaAcidPercent = ingredient.alphaAcidPercent ?? '';
				attenuationPercent = ingredient.attenuationPercent ?? '';
				description = ingredient.description ?? '';
				form =
					ingredient.form ??
					(ingredient.type === 'Hop' ? 'Pellet' : ingredient.type === 'Yeast' ? 'Dry' : '');
			} else {
				name = '';
				type = initialType;
				potentialUnit = 'SG';
				potentialValue = initialType === 'Fermentable' ? 1.037 : '';
				colorSrm = initialType === 'Fermentable' ? 2.0 : '';
				alphaAcidPercent = initialType === 'Hop' ? 5.5 : '';
				attenuationPercent = initialType === 'Yeast' ? 75 : '';
				description = '';
				initialStock = '';
				stockUnit = initialType === 'Hop' ? 'g' : initialType === 'Yeast' ? 'pkg' : 'kg';
				form = initialType === 'Hop' ? 'Pellet' : initialType === 'Yeast' ? 'Dry' : '';
			}
			isSubmitting = false;
			errorMessage = null;
			fieldErrors = {};
		}
	});

	function setPotentialUnit(unit: 'SG' | 'PPG') {
		if (potentialUnit === unit) return;
		if (typeof potentialValue === 'number' && !isNaN(potentialValue)) {
			if (unit === 'PPG') {
				potentialValue = sgToPpg(potentialValue);
			} else {
				potentialValue = ppgToSg(potentialValue);
			}
		}
		potentialUnit = unit;
		fieldErrors = { ...fieldErrors, potentialgravity: '' };
	}

	function handlePotentialBlur() {
		if (typeof potentialValue !== 'number' || isNaN(potentialValue)) return;
		if (potentialUnit === 'SG' && potentialValue >= 10 && potentialValue <= 150) {
			potentialValue = ppgToSg(potentialValue);
		} else if (potentialUnit === 'PPG' && potentialValue >= 1.0 && potentialValue <= 1.2) {
			potentialValue = sgToPpg(potentialValue);
		}
	}

	function handleTypeChange(newType: IngredientType) {
		type = newType;
		if (!ingredient) {
			if (newType === 'Fermentable') {
				if (potentialValue === '') potentialValue = potentialUnit === 'SG' ? 1.037 : 37;
				if (colorSrm === '') colorSrm = 2.0;
				stockUnit = 'kg';
				form = '';
			} else if (newType === 'Hop') {
				if (alphaAcidPercent === '') alphaAcidPercent = 5.5;
				stockUnit = 'g';
				form = 'Pellet';
			} else if (newType === 'Yeast') {
				if (attenuationPercent === '') attenuationPercent = 75;
				stockUnit = 'pkg';
				form = 'Dry';
			} else {
				stockUnit = 'g';
				form = '';
			}
		}
		fieldErrors = {};
		errorMessage = null;
	}

	function handleKeydown(event: KeyboardEvent) {
		if (event.key === 'Escape' && open && !isSubmitting) {
			onClose();
		}
	}

	function validate(): boolean {
		const errors: Record<string, string> = {};
		const trimmedName = name.trim();

		if (!trimmedName) {
			errors.name = t('ingredients.err_name_required');
		} else if (trimmedName.length < 2) {
			errors.name = t('ingredients.err_name_min');
		} else if (trimmedName.length > 100) {
			errors.name = t('ingredients.err_name_max');
		} else if (isDuplicateName) {
			errors.name = t('ingredients.err_duplicate');
		}

		if (description.length > 1000) {
			errors.description = t('ingredients.err_desc_max');
		}

		if (type === 'Fermentable') {
			if (typeof potentialValue === 'number' && !isNaN(potentialValue)) {
				if (potentialUnit === 'SG') {
					if (potentialValue >= 10 && potentialValue <= 150) {
						potentialValue = ppgToSg(potentialValue);
					} else if (potentialValue < 1.0 || potentialValue > 1.2) {
						errors.potentialgravity = t('ingredients.err_potential_range');
					}
				} else {
					if (potentialValue >= 1.0 && potentialValue <= 1.2) {
						potentialValue = sgToPpg(potentialValue);
					} else if (potentialValue < 1 || potentialValue > 150) {
						errors.potentialgravity = t('ingredients.err_ppg_range');
					}
				}
			}
			if (typeof colorSrm === 'number') {
				if (colorSrm < 0 || colorSrm > 1000) {
					errors.colorSrm = t('ingredients.err_srm_range');
				}
			}
		} else if (type === 'Hop') {
			if (typeof alphaAcidPercent === 'number') {
				if (alphaAcidPercent < 0 || alphaAcidPercent > 100) {
					errors.alphaAcidPercent = t('ingredients.err_alpha_range');
				}
			}
		} else if (type === 'Yeast') {
			if (typeof attenuationPercent === 'number') {
				if (attenuationPercent < 0 || attenuationPercent > 100) {
					errors.attenuationPercent = t('ingredients.err_attenuation_range');
				}
			}
		}

		fieldErrors = errors;
		return Object.keys(errors).length === 0;
	}

	async function handleSubmit(e: SubmitEvent) {
		e.preventDefault();
		if (!validate()) return;

		isSubmitting = true;
		errorMessage = null;

		let reqPotentialGravity: number | null | undefined = undefined;
		let reqColorSrm: number | null | undefined = undefined;
		let reqAlphaAcidPercent: number | null | undefined = undefined;
		let reqAttenuationPercent: number | null | undefined = undefined;

		if (type === 'Fermentable') {
			if (typeof potentialValue === 'number' && !isNaN(potentialValue)) {
				reqPotentialGravity =
					potentialUnit === 'PPG'
						? ppgToSg(potentialValue)
						: potentialValue >= 10 && potentialValue <= 150
							? ppgToSg(potentialValue)
							: potentialValue;
			} else {
				reqPotentialGravity = null;
			}
			reqColorSrm = typeof colorSrm === 'number' ? colorSrm : null;
		} else if (type === 'Hop') {
			reqAlphaAcidPercent = typeof alphaAcidPercent === 'number' ? alphaAcidPercent : null;
		} else if (type === 'Yeast') {
			reqAttenuationPercent = typeof attenuationPercent === 'number' ? attenuationPercent : null;
		}

		const payload: CreateIngredientRequest = {
			name: name.trim(),
			type,
			potentialGravity: reqPotentialGravity,
			colorSrm: reqColorSrm,
			alphaAcidPercent: reqAlphaAcidPercent,
			attenuationPercent: reqAttenuationPercent,
			description: description.trim() ? description.trim() : null,
			initialStock: typeof initialStock === 'number' && initialStock > 0 ? initialStock : null,
			stockUnit: typeof initialStock === 'number' && initialStock > 0 ? stockUnit : null,
			form: (type === 'Hop' || type === 'Yeast') && form ? form : null
		};

		try {
			const saved = await api.ingredients.create(payload);
			onSaved(saved);
			onClose();
		} catch (err: unknown) {
			if (err instanceof ApiClientError) {
				if (err.status === 409) {
					errorMessage = t('ingredients.err_duplicate');
					fieldErrors.name = t('ingredients.err_duplicate');
				} else if (err.details && err.details.length > 0) {
					const map: Record<string, string> = {};
					for (const d of err.details) {
						map[d.field.toLowerCase()] = d.issue;
					}
					fieldErrors = map;
					errorMessage = err.message;
				} else {
					errorMessage = err.message || t('ingredients.err_save_failed');
				}
			} else {
				errorMessage = (err as Error).message || t('ingredients.err_save_failed');
			}
		} finally {
			isSubmitting = false;
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4 sm:p-6"
		role="dialog"
		aria-modal="true"
		aria-labelledby="ingredient-modal-title"
	>
		<!-- Backdrop -->
		<button
			type="button"
			class="fixed inset-0 bg-black/60 backdrop-blur-xs transition-opacity"
			onclick={() => {
				if (!isSubmitting) onClose();
			}}
			aria-label={t('common.close')}
		></button>

		<!-- Dialog Body -->
		<div
			class="glass-panel relative z-10 max-h-[90vh] w-full max-w-lg overflow-y-auto rounded-3xl border border-zinc-200/80 bg-white/95 p-6 shadow-2xl transition-all sm:p-8 dark:border-white/10 dark:bg-zinc-950/95"
		>
			<!-- Header -->
			<div
				class="flex items-start justify-between border-b border-zinc-200/80 pb-4 dark:border-white/10"
			>
				<div>
					<h2
						id="ingredient-modal-title"
						class="text-xl font-bold tracking-tight text-zinc-900 sm:text-2xl dark:text-white"
					>
						{t('ingredients.modal_add_title')}
					</h2>
					<p class="mt-1 text-xs text-zinc-600 dark:text-zinc-400">
						{t('ingredients.modal_add_subtitle')}
					</p>
				</div>
				<button
					type="button"
					onclick={onClose}
					disabled={isSubmitting}
					class="rounded-xl p-1.5 text-zinc-400 hover:bg-zinc-100 hover:text-zinc-600 disabled:opacity-50 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('common.close')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<form onsubmit={handleSubmit} class="mt-5 space-y-5">
				{#if errorMessage}
					<div
						data-testid="ingredient-error-banner"
						class="rounded-xl border border-red-200 bg-red-50 p-3.5 text-xs text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
					>
						{errorMessage}
					</div>
				{/if}

				<!-- Category Tabs -->
				<div>
					<span class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300">
						{t('ingredients.type_label')} <span class="text-amber-500">*</span>
					</span>
					<div class="mt-1.5 grid grid-cols-4 gap-2">
						{#each ['Fermentable', 'Hop', 'Yeast', 'Other'] as const as opt}
							<button
								type="button"
								onclick={() => handleTypeChange(opt)}
								class="flex flex-col items-center gap-1 rounded-xl border px-2 py-2.5 text-xs font-medium transition-all {type ===
								opt
									? 'border-amber-500 bg-amber-500/10 font-bold text-amber-700 shadow-xs dark:text-amber-400'
									: 'border-zinc-200/80 bg-zinc-50/70 text-zinc-600 hover:bg-zinc-100 dark:border-zinc-800 dark:bg-zinc-900/60 dark:text-zinc-400 dark:hover:bg-zinc-800'}"
							>
								{#if opt === 'Fermentable'}
									<Wheat class="h-4 w-4" />
									<span>{t('ingredients.type_fermentable')}</span>
								{:else if opt === 'Hop'}
									<Flower2 class="h-4 w-4" />
									<span>{t('ingredients.type_hop')}</span>
								{:else if opt === 'Yeast'}
									<Dna class="h-4 w-4" />
									<span>{t('ingredients.type_yeast')}</span>
								{:else}
									<Sparkles class="h-4 w-4" />
									<span>{t('ingredients.type_other')}</span>
								{/if}
							</button>
						{/each}
					</div>
				</div>

				<!-- Name -->
				<div>
					<label
						for="ingredient-name"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('ingredients.name_label')} <span class="text-amber-500">*</span>
					</label>
					<input
						id="ingredient-name"
						type="text"
						bind:value={name}
						placeholder={t('ingredients.name_placeholder')}
						maxlength="100"
						required
						aria-invalid={!!fieldErrors.name}
						aria-describedby={fieldErrors.name ? 'ingredient-name-error' : undefined}
						class="mt-1.5 h-10 w-full rounded-xl border bg-white px-3 text-sm text-zinc-900 placeholder-zinc-400 transition-colors focus:outline-none dark:bg-zinc-900 dark:text-zinc-100 dark:placeholder-zinc-600 {fieldErrors.name
							? 'border-red-500 focus:border-red-500 focus:ring-1 focus:ring-red-500/30 dark:border-red-500'
							: 'border-zinc-300 focus:border-amber-500 dark:border-zinc-800'}"
					/>
					{#if fieldErrors.name}
						<p
							id="ingredient-name-error"
							class="mt-1 text-xs text-red-600 dark:text-red-400"
							role="alert"
						>
							{fieldErrors.name}
						</p>
					{/if}
				</div>

				<!-- Dynamic Metric Fields -->
				{#if type === 'Fermentable'}
					<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
						<!-- Potential Gravity / PPG -->
						<div>
							<div class="flex items-center justify-between">
								<label
									for="ingredient-potential"
									class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
								>
									{potentialUnit === 'SG'
										? t('ingredients.potential_sg_label')
										: t('ingredients.potential_ppg_label')}
								</label>
								<div
									class="flex rounded-lg border border-zinc-200 bg-zinc-100 p-0.5 text-[10px] font-semibold dark:border-zinc-800 dark:bg-zinc-800/80"
								>
									<button
										type="button"
										onclick={() => setPotentialUnit('SG')}
										class="rounded-md px-1.5 py-0.5 transition-colors {potentialUnit === 'SG'
											? 'bg-white font-bold text-amber-600 shadow-xs dark:bg-zinc-700 dark:text-amber-400'
											: 'text-zinc-500 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-200'}"
									>
										{t('ingredients.unit_sg')}
									</button>
									<button
										type="button"
										onclick={() => setPotentialUnit('PPG')}
										class="rounded-md px-1.5 py-0.5 transition-colors {potentialUnit === 'PPG'
											? 'bg-white font-bold text-amber-600 shadow-xs dark:bg-zinc-700 dark:text-amber-400'
											: 'text-zinc-500 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-200'}"
									>
										{t('ingredients.unit_ppg')}
									</button>
								</div>
							</div>

							<div class="relative mt-1.5">
								<input
									id="ingredient-potential"
									type="number"
									step={potentialUnit === 'SG' ? '0.001' : '1'}
									min={potentialUnit === 'SG' ? '1.000' : '1'}
									max={potentialUnit === 'SG' ? '1.200' : '150'}
									bind:value={potentialValue}
									onblur={handlePotentialBlur}
									placeholder={potentialUnit === 'SG'
										? t('ingredients.potential_placeholder')
										: t('ingredients.potential_ppg_placeholder')}
									class="h-10 w-full rounded-xl border border-zinc-300 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
								/>
							</div>

							{#if typeof potentialValue === 'number' && !isNaN(potentialValue) && !fieldErrors.potentialgravity}
								<p class="mt-1 font-mono text-[11px] text-zinc-500 dark:text-zinc-400">
									{#if potentialUnit === 'SG'}
										{t('ingredients.ppg_equivalent', { ppg: sgToPpg(potentialValue) })}
									{:else}
										{t('ingredients.sg_equivalent', { sg: ppgToSg(potentialValue).toFixed(3) })}
									{/if}
								</p>
							{/if}

							{#if fieldErrors.potentialgravity}
								<p class="mt-1 text-xs text-red-600 dark:text-red-400" role="alert">
									{fieldErrors.potentialgravity}
								</p>
							{/if}
						</div>

						<!-- Color (SRM) with Live Preview -->
						<div>
							<label
								for="ingredient-srm"
								class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('ingredients.color_label')}
							</label>
							<div class="mt-1.5 flex items-center gap-2">
								<input
									id="ingredient-srm"
									type="number"
									step="0.1"
									min="0"
									max="1000"
									bind:value={colorSrm}
									placeholder={t('ingredients.color_placeholder')}
									class="h-10 flex-1 rounded-xl border border-zinc-300 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
								/>
								<div
									class="flex h-10 w-16 items-center justify-center rounded-xl border border-zinc-300 text-[11px] font-bold shadow-xs transition-colors dark:border-zinc-700"
									style="background-color: {srmHexColor}; color: {(Number(colorSrm) || 0) > 15
										? '#ffffff'
										: '#18181b'};"
									title={t('ingredients.color_swatch_label', { srm: colorSrm || 0 })}
									aria-label={t('ingredients.color_swatch_label', { srm: colorSrm || 0 })}
								>
									{colorSrm || 0}°
								</div>
							</div>
							{#if fieldErrors.colorsrm}
								<p class="mt-1 text-xs text-red-600 dark:text-red-400" role="alert">
									{fieldErrors.colorsrm}
								</p>
							{/if}
						</div>
					</div>
				{:else if type === 'Hop'}
					<div>
						<div class="flex items-center justify-between">
							<label
								for="ingredient-alpha"
								class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('ingredients.alpha_acid_label')}
							</label>
							<span
								class="rounded-md border border-emerald-500/30 bg-emerald-500/10 px-2 py-0.5 text-[10px] font-bold text-emerald-700 dark:text-emerald-400"
							>
								{hopPurposeHint}
							</span>
						</div>
						<input
							id="ingredient-alpha"
							type="number"
							step="0.1"
							min="0"
							max="100"
							bind:value={alphaAcidPercent}
							placeholder={t('ingredients.alpha_acid_placeholder')}
							class="mt-1.5 h-10 w-full rounded-xl border border-zinc-300 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
						/>
						{#if fieldErrors.alphaacidpercent}
							<p class="mt-1 text-xs text-red-600 dark:text-red-400" role="alert">
								{fieldErrors.alphaacidpercent}
							</p>
						{/if}

						<div class="mt-3">
							<label
								for="ingredient-hop-form"
								class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('ingredients.form_label')}
							</label>
							<select
								id="ingredient-hop-form"
								bind:value={form}
								class="mt-1.5 h-10 w-full rounded-xl border border-zinc-300 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							>
								<option value="Pellet">{t('ingredients.forms.pellet')}</option>
								<option value="Leaf">{t('ingredients.forms.leaf')}</option>
								<option value="Plug">{t('ingredients.forms.plug')}</option>
							</select>
						</div>
					</div>
				{:else if type === 'Yeast'}
					<div>
						<div class="flex items-center justify-between">
							<label
								for="ingredient-attenuation"
								class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('ingredients.attenuation_label')}
							</label>
							{#if yeastSensoryHint}
								<span
									class="rounded-md border border-sky-500/30 bg-sky-500/10 px-2 py-0.5 text-[10px] font-bold text-sky-700 dark:text-sky-400"
								>
									{yeastSensoryHint}
								</span>
							{/if}
						</div>
						<input
							id="ingredient-attenuation"
							type="number"
							step="0.5"
							min="0"
							max="100"
							bind:value={attenuationPercent}
							placeholder={t('ingredients.attenuation_placeholder')}
							class="mt-1.5 h-10 w-full rounded-xl border border-zinc-300 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
						/>
						{#if fieldErrors.attenuationpercent}
							<p class="mt-1 text-xs text-red-600 dark:text-red-400" role="alert">
								{fieldErrors.attenuationpercent}
							</p>
						{/if}

						<div class="mt-3">
							<label
								for="ingredient-yeast-form"
								class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('ingredients.form_label')}
							</label>
							<select
								id="ingredient-yeast-form"
								bind:value={form}
								onchange={() => {
									if (form === 'Liquid') {
										stockUnit = 'pkg';
									} else if (form === 'Dry') {
										stockUnit = 'pkg';
									}
								}}
								class="mt-1.5 h-10 w-full rounded-xl border border-zinc-300 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							>
								<option value="Dry">{t('ingredients.forms.dry')}</option>
								<option value="Liquid">{t('ingredients.forms.liquid')}</option>
								<option value="Slant">{t('ingredients.forms.slant')}</option>
								<option value="Culture">{t('ingredients.forms.culture')}</option>
							</select>
						</div>
					</div>
				{/if}

				<!-- Initial Stock (Optional) -->
				<div class="grid grid-cols-2 gap-3">
					<div>
						<label
							for="ingredient-initial-stock"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('ingredients.initial_stock_label')}
						</label>
						<input
							id="ingredient-initial-stock"
							type="number"
							step="0.01"
							min="0"
							bind:value={initialStock}
							placeholder="0"
							class="mt-1.5 h-10 w-full rounded-xl border border-zinc-300 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>
					<div>
						<label
							for="ingredient-stock-unit"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('ingredients.initial_stock_unit_label')}
						</label>
						<select
							id="ingredient-stock-unit"
							bind:value={stockUnit}
							class="mt-1.5 h-10 w-full rounded-xl border border-zinc-300 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
						>
							<option value="kg">kg</option>
							<option value="g">g</option>
							<option value="pkg">pkg</option>
							<option value="items">items</option>
							<option value="ml">ml</option>
							<option value="oz">oz</option>
							<option value="lbs">lbs</option>
						</select>
					</div>
				</div>

				<!-- Description -->
				<div>
					<label
						for="ingredient-desc"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('ingredients.desc_label')}
					</label>
					<textarea
						id="ingredient-desc"
						bind:value={description}
						rows="3"
						maxlength="1000"
						placeholder={t('ingredients.desc_placeholder')}
						class="mt-1.5 w-full rounded-xl border border-zinc-300 bg-white p-3 text-sm text-zinc-900 placeholder-zinc-400 transition-colors focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100 dark:placeholder-zinc-600"
					></textarea>
					{#if fieldErrors.description}
						<p class="mt-1 text-xs text-red-600 dark:text-red-400" role="alert">
							{fieldErrors.description}
						</p>
					{/if}
				</div>

				<!-- Actions Footer -->
				<div
					class="flex items-center justify-end gap-3 border-t border-zinc-200/80 pt-4 dark:border-white/10"
				>
					<button
						type="button"
						onclick={onClose}
						disabled={isSubmitting}
						class="rounded-xl border border-zinc-200/80 bg-zinc-50/80 px-4 py-2 text-xs font-semibold text-zinc-700 transition-colors hover:bg-zinc-100 disabled:opacity-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800"
					>
						{t('ingredients.cancel')}
					</button>

					<button
						type="submit"
						disabled={isSubmitting}
						class="flex items-center gap-2 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-5 py-2 text-xs font-bold text-zinc-950 shadow-md transition-all hover:from-amber-400 hover:to-amber-500 active:scale-98 disabled:opacity-50"
					>
						{#if isSubmitting}
							<Loader2 class="h-3.5 w-3.5 animate-spin" />
							<span>{t('ingredients.saving')}</span>
						{:else}
							<Check class="h-3.5 w-3.5 stroke-[2.5]" />
							<span>{t('ingredients.save_ingredient')}</span>
						{/if}
					</button>
				</div>
			</form>
		</div>
	</div>
{/if}
