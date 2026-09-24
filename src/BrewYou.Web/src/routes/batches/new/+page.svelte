<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import { formatNumber } from '$lib/utils/formatNumber';
	import type {
		RecipeSummaryDto,
		EquipmentDto,
		CreateBatchRequest,
		RecipeFermentationStepOutputDto,
		RecipeMashStepOutputDto
	} from '$lib/types/api';
	import { settings } from '$lib/stores/settings.svelte';
	import { calculateWaterRequirements } from '$lib/calculators/volume';
	import type { TargetVolumeBasis } from '$lib/calculators/volume';
	import { isVesselFillable } from '$lib/components/inventory/vessel-geometry';
	import InsufficientStockModal from '$lib/components/batches/InsufficientStockModal.svelte';
	import type { IngredientShortageDto } from '$lib/types/api';
	import {
		Activity,
		CheckCircle2,
		AlertTriangle,
		ArrowRight,
		ArrowLeft,
		Info,
		Loader2,
		Sparkles,
		Droplets,
		Settings2,
		RotateCcw,
		X
	} from '@lucide/svelte';

	type WizardStep = 1 | 2 | 3;

	let currentStep = $state<WizardStep>(1);
	let loading = $state(true);
	let submitting = $state(false);
	let error = $state<string | null>(null);

	let recipes = $state<RecipeSummaryDto[]>([]);
	let equipment = $state<EquipmentDto[]>([]);

	// Shortage Modal State
	let stockModalOpen = $state(false);
	let shortages = $state<IngredientShortageDto[]>([]);

	// Form State
	let isAdHoc = $state(false);
	let selectedRecipeId = $state<string>('');
	let recipeMashSteps = $state<RecipeMashStepOutputDto[]>([]);
	let recipeFermentationSteps = $state<RecipeFermentationStepOutputDto[]>([]);
	let batchCode = $state('');
	let batchName = $state('');
	let beerStyle = $state('American IPA');
	let brewDate = $state(new Date().toISOString().split('T')[0]);
	let targetBatchSizeLiters = $state(20.0);
	let targetVolumeBasis = $state<TargetVolumeBasis>('fermenter');
	let showWaterInfo = $state(false);
	let spargeEnabled = $state(true);

	// Equipment State
	let selectedBoilerId = $state<string>('');
	let selectedFermenterId = $state<string>('');
	let selectedPackagingVesselId = $state<string>('');
	let selectedFermentationSensorId = $state<string>('');

	// Targets & Settings
	let targetOg = $state(1.05);
	let targetFg = $state(1.01);
	let targetAbv = $state(5.25);
	let targetIbu = $state(35.0);
	let targetColorSrm = $state(6.0);
	let boilTimeMinutes = $state(60);
	let efficiencyPercent = $state(72.0);
	let measuredOg = $state<number | null>(null);
	let pitchTemperatureC = $state<number | null>(null);
	let notes = $state('');
	let totalGrainKg = $state(4.5);

	// Derived values
	let boilers = $derived(equipment.filter((e) => e.type === 'Boiler'));
	let fermenters = $derived(equipment.filter((e) => e.type === 'Fermenter'));
	let packagingVessels = $derived(
		equipment.filter((e) => e.type === 'Keg' || (e.type === 'Other' && isVesselFillable(e.subtype)))
	);
	let sensors = $derived(
		equipment.filter((e) => e.type === 'Sensor' || !isVesselFillable(e.subtype))
	);

	let selectedBoiler = $derived(boilers.find((b) => b.id === selectedBoilerId) || null);
	let selectedFermenter = $derived(fermenters.find((f) => f.id === selectedFermenterId) || null);
	let selectedPackagingVessel = $derived(
		packagingVessels.find((p) => p.id === selectedPackagingVesselId) || null
	);
	let selectedFermentationSensor = $derived(
		sensors.find((s) => s.id === selectedFermentationSensorId) || null
	);

	// Equipment loss overrides (defaulted from equipment, overridable by user)
	let customBoilOffRate = $state<number>(3.0);
	let customKettleTrub = $state<number>(1.5);
	let customMashTunDeadSpace = $state<number>(0.0);
	let customFermenterLoss = $state<number>(1.5);
	let customPackagingLoss = $state<number>(0.5);

	let lastBoilerId = $state<string>('');
	let lastFermenterId = $state<string>('');
	let lastPackagingVesselId = $state<string>('');

	$effect(() => {
		if (selectedBoilerId !== lastBoilerId) {
			lastBoilerId = selectedBoilerId;
			if (selectedBoiler) {
				customBoilOffRate = selectedBoiler.boilOffRatePerHour ?? 3.0;
				customKettleTrub = selectedBoiler.trubLossLiters ?? 1.5;
				customMashTunDeadSpace = selectedBoiler.mashTunDeadSpaceLiters ?? 0.0;
			}
		}
	});

	$effect(() => {
		if (selectedFermenterId !== lastFermenterId) {
			lastFermenterId = selectedFermenterId;
			if (selectedFermenter) {
				customFermenterLoss = selectedFermenter.trubLossLiters ?? 1.5;
			}
		}
	});

	$effect(() => {
		if (selectedPackagingVesselId !== lastPackagingVesselId) {
			lastPackagingVesselId = selectedPackagingVesselId;
			if (selectedPackagingVessel) {
				customPackagingLoss = selectedPackagingVessel.packagingLossLiters ?? 0.5;
			}
		}
	});

	function getSubtypeName(subtype: string): string {
		return t(`equipment.subtypes.${subtype}`) || subtype;
	}

	function getTypeName(type: string): string {
		return t(`equipment.types.${type}`) || type;
	}

	function resetEquipmentDefaults() {
		if (selectedBoiler) {
			customBoilOffRate = selectedBoiler.boilOffRatePerHour ?? 3.0;
			customKettleTrub = selectedBoiler.trubLossLiters ?? 1.5;
			customMashTunDeadSpace = selectedBoiler.mashTunDeadSpaceLiters ?? 0.0;
		} else {
			customBoilOffRate = 3.0;
			customKettleTrub = 1.5;
			customMashTunDeadSpace = 0.0;
		}

		if (selectedFermenter) {
			customFermenterLoss = selectedFermenter.trubLossLiters ?? 1.5;
		} else {
			customFermenterLoss = 1.5;
		}

		if (selectedPackagingVessel) {
			customPackagingLoss = selectedPackagingVessel.packagingLossLiters ?? 0.5;
		} else {
			customPackagingLoss = 0.5;
		}
	}

	let waterSchedule = $derived.by(() => {
		return calculateWaterRequirements({
			targetBatchSizeLiters,
			targetBasis: targetVolumeBasis,
			spargeEnabled,
			boilTimeMinutes,
			totalGrainWeightKg: totalGrainKg,
			boilOffRatePerHour: customBoilOffRate,
			kettleTrubLossLiters: customKettleTrub,
			mashTunDeadSpaceLiters: customMashTunDeadSpace,
			fermenterLossLiters: customFermenterLoss,
			packagingLossLiters: customPackagingLoss,
			coolingShrinkagePercent: 4.0,
			mashThicknessLitersPerKg: 3.0
		});
	});

	let isBoilerOverCapacity = $derived(
		selectedBoiler !== null &&
			selectedBoiler.capacityLiters > 0 &&
			waterSchedule.targetPreBoilVolumeLiters > selectedBoiler.capacityLiters
	);

	let fermenterHeadspacePercent = $derived.by(() => {
		if (!selectedFermenter || selectedFermenter.capacityLiters <= 0) return null;
		const fermenterVol = waterSchedule.targetFermenterVolumeLiters;
		const excess = selectedFermenter.capacityLiters - fermenterVol;
		return Math.round((excess / selectedFermenter.capacityLiters) * 100);
	});

	let isOverCapacity = $derived(
		selectedFermenter !== null &&
			waterSchedule.targetFermenterVolumeLiters > selectedFermenter.capacityLiters
	);

	let isKrausenRisk = $derived(
		selectedFermenter !== null &&
			waterSchedule.targetFermenterVolumeLiters <= selectedFermenter.capacityLiters &&
			waterSchedule.targetFermenterVolumeLiters > selectedFermenter.capacityLiters * 0.85
	);

	let isFermenterOccupied = $derived(
		selectedFermenter !== null && selectedFermenter.currentVolumeLiters > 0
	);

	let targetMashTempC = $derived.by(() => {
		if (recipeMashSteps.length > 0) {
			return recipeMashSteps[0].temperatureC;
		}
		return 65.0;
	});

	let targetSpargeTempC = $derived.by(() => {
		if (recipeMashSteps.length > 0) {
			const mashOutStep = [...recipeMashSteps]
				.reverse()
				.find(
					(s) =>
						/mash\s*out|utmäsk|sparge|lak/i.test(s.name) ||
						(s.temperatureC >= 74 && s.temperatureC <= 80)
				);
			if (mashOutStep) {
				return mashOutStep.temperatureC;
			}
		}
		return 76.0;
	});

	let liquorToGristRatio = $derived.by(() => {
		if (totalGrainKg > 0 && waterSchedule.strikeWaterLiters > 0) {
			return waterSchedule.strikeWaterLiters / totalGrainKg;
		}
		return 3.0;
	});

	// Strike water temp estimation: T_strike = T_mash + (0.41 / ratio) * (T_mash - T_grain) + T_tun_loss
	let calculatedStrikeTempC = $derived.by(() => {
		const targetMash = targetMashTempC;
		const grainTemp = 20.0;
		const ratio = Math.max(0.5, liquorToGristRatio);
		const tunLoss = 1.0;
		return formatNumber(targetMash + (0.41 / ratio) * (targetMash - grainTemp) + tunLoss, 1);
	});

	// Validations
	let isStep1Valid = $derived(
		Boolean(
			batchCode.trim() &&
			batchName.trim() &&
			targetBatchSizeLiters > 0 &&
			(isAdHoc || selectedRecipeId)
		)
	);

	let isStep2Valid = $derived(Boolean(selectedFermenterId && !isOverCapacity));

	onMount(async () => {
		loading = true;
		error = null;
		try {
			const [recipeList, equipList, codeRes] = await Promise.all([
				api.recipes.list(),
				api.equipment.list(),
				api.batches.getNextCode()
			]);
			recipes = recipeList;
			equipment = equipList;
			batchCode = codeRes.batchCode;

			// Check URL query param
			const urlRecipeId = page.url.searchParams.get('recipeId');
			if (urlRecipeId) {
				selectedRecipeId = urlRecipeId;
				await handleRecipeSelect(urlRecipeId);
			} else if (recipes.length > 0) {
				selectedRecipeId = recipes[0].id;
				await handleRecipeSelect(recipes[0].id);
			} else {
				isAdHoc = true;
				batchName = t('batches.wizard.default_adhoc_name');
				recipeMashSteps = [];
			}

			// Pre-select first fermenter and boiler if available
			if (fermenters.length > 0) selectedFermenterId = fermenters[0].id;
			if (boilers.length > 0) selectedBoilerId = boilers[0].id;
		} catch (err: unknown) {
			error = (err as Error).message || t('batches.wizard.err_init_failed');
		} finally {
			loading = false;
		}
	});

	async function handleRecipeSelect(id: string) {
		if (!id) return;
		try {
			const r = await api.recipes.getById(id);
			batchName = r.name;
			beerStyle = r.beerStyle;
			targetBatchSizeLiters = r.batchSizeLiters;
			targetOg = r.originalGravity;
			targetFg = r.finalGravity;
			targetAbv = r.alcoholByVolume;
			targetIbu = r.bitternessIbu;
			targetColorSrm = r.colorSrm;
			boilTimeMinutes = r.boilTimeMinutes;
			efficiencyPercent = r.efficiencyPercent;

			if (r.ingredients && r.ingredients.length > 0) {
				const grainSum = r.ingredients
					.filter((i) => i.ingredientType === 'Fermentable')
					.reduce(
						(sum, i) =>
							sum + (i.unit.toLowerCase() === 'kg' ? Number(i.amount) : Number(i.amount) / 1000),
						0
					);
				if (grainSum > 0) totalGrainKg = Math.round(grainSum * 10) / 10;
			}

			if (r.mashSteps && r.mashSteps.length > 0) {
				recipeMashSteps = r.mashSteps;
			} else {
				recipeMashSteps = [];
			}

			if (r.fermentationSteps && r.fermentationSteps.length > 0) {
				recipeFermentationSteps = r.fermentationSteps;
				if (pitchTemperatureC === null || pitchTemperatureC === undefined) {
					pitchTemperatureC = r.fermentationSteps[0].targetTemperatureC;
				}
			} else {
				recipeFermentationSteps = [];
			}
		} catch (err: unknown) {
			error = (err as Error).message;
		}
	}

	async function handleSubmit(bypassStockCheck = false) {
		if (!isStep1Valid || !isStep2Valid) return;
		submitting = true;
		error = null;

		try {
			if (!isAdHoc && selectedRecipeId && !bypassStockCheck) {
				try {
					const stockCheck = await api.batches.checkStock({
						recipeId: selectedRecipeId,
						targetBatchSizeLiters,
						targetVolumeBasis: targetVolumeBasis === 'packaged' ? 'Packaged' : 'Fermenter',
						fermenterLossLiters: customFermenterLoss,
						packagingLossLiters: customPackagingLoss
					});
					if (stockCheck.hasShortage && stockCheck.shortages.length > 0) {
						shortages = stockCheck.shortages;
						stockModalOpen = true;
						submitting = false;
						return;
					}
				} catch (err: unknown) {
					// If check-stock fails (e.g. offline/network), log and allow creation to proceed
					console.warn('Stock check failed:', err);
				}
			}

			const req: CreateBatchRequest = {
				recipeId: isAdHoc ? null : selectedRecipeId || null,
				batchCode: batchCode.trim(),
				name: batchName.trim(),
				beerStyle: beerStyle.trim(),
				brewDate,
				targetBatchSizeLiters,
				targetOg,
				targetFg,
				targetAbv,
				targetIbu,
				targetColorSrm,
				boilTimeMinutes,
				efficiencyPercent,
				boilerId: selectedBoilerId || null,
				fermenterId: selectedFermenterId || null,
				measuredOg: measuredOg,
				pitchTemperatureC: pitchTemperatureC,
				customMashSteps:
					recipeMashSteps.length > 0
						? recipeMashSteps.map((s) => ({
								stepOrder: s.stepOrder,
								name: s.name,
								type: s.type,
								targetTemperatureC: s.temperatureC,
								durationMinutes: s.durationMinutes,
								rampTimeMinutes: s.rampTimeMinutes,
								infuseAmountLiters: s.infuseAmountLiters,
								notes: s.notes
							}))
						: null,
				customFermentationSteps:
					recipeFermentationSteps.length > 0
						? recipeFermentationSteps.map((s) => ({
								stepOrder: s.stepOrder,
								name: s.name,
								type: s.type,
								targetTemperatureC: s.targetTemperatureC,
								durationDays: s.durationDays,
								rampTimeHours: s.rampTimeHours,
								triggerGravity: s.triggerGravity,
								notes: s.notes
							}))
						: null,
				notes: notes.trim() || null,
				boilOffRatePerHour: customBoilOffRate,
				kettleTrubLossLiters: customKettleTrub,
				mashTunDeadSpaceLiters: customMashTunDeadSpace,
				fermenterLossLiters: customFermenterLoss,
				spargeEnabled,
				packagingVesselId: selectedPackagingVesselId || null,
				targetVolumeBasis: targetVolumeBasis === 'packaged' ? 'Packaged' : 'Fermenter',
				packagingLossLiters: customPackagingLoss,
				sensorAssignments: selectedFermentationSensorId
					? [
							{
								equipmentId: selectedFermentationSensorId,
								stage: 'Ferment',
								batchMashStepId: null
							}
						]
					: null
			};

			const created = await api.batches.create(req);
			goto(`/batches/${created.id}`);
		} catch (err: unknown) {
			error = (err as Error).message || t('batches.wizard.err_create_failed');
			submitting = false;
		}
	}

	function handleContinueAnyway() {
		stockModalOpen = false;
		handleSubmit(true);
	}
</script>

<svelte:head>
	<title>BrewYou — {t('batches.start_batch')}</title>
</svelte:head>

<div class="mx-auto max-w-3xl space-y-6 sm:space-y-8">
	<!-- Page Header -->
	<div class="flex items-center gap-3">
		<div
			class="flex h-11 w-11 items-center justify-center rounded-2xl border border-amber-500/30 bg-amber-500/10 text-amber-500"
		>
			<Activity class="h-6 w-6" />
		</div>
		<div>
			<h1 class="text-2xl font-bold tracking-tight text-zinc-900 sm:text-3xl dark:text-white">
				{t('batches.start_batch')}
			</h1>
			<p class="text-sm text-zinc-600 dark:text-zinc-400">
				{t('batches.wizard.stepper_nav')}
			</p>
		</div>
	</div>

	<!-- Wizard Stepper Navigation -->
	<nav
		aria-label={t('batches.wizard.stepper_nav')}
		class="glass-panel flex items-center justify-between rounded-2xl border border-zinc-200/80 p-3 sm:p-4 dark:border-white/10"
	>
		<ol class="flex w-full items-center justify-between">
			<!-- Step 1 -->
			<li class="flex items-center gap-2">
				<button
					type="button"
					onclick={() => (currentStep = 1)}
					aria-current={currentStep === 1 ? 'step' : undefined}
					class="flex items-center gap-2 rounded-lg px-2 py-1 text-xs font-bold transition-colors {currentStep ===
					1
						? 'text-amber-600 dark:text-amber-400'
						: 'text-zinc-500 hover:text-zinc-800 dark:text-zinc-400 dark:hover:text-zinc-200'}"
				>
					<span
						class="flex h-6 w-6 items-center justify-center rounded-full {currentStep === 1
							? 'bg-amber-500 font-bold text-zinc-950'
							: 'bg-zinc-200 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400'}"
					>
						1
					</span>
					<span class="hidden sm:inline">{t('batches.wizard.step1_title')}</span>
				</button>
			</li>

			<div class="mx-2 h-0.5 flex-1 bg-zinc-200 dark:bg-zinc-800"></div>

			<!-- Step 2 -->
			<li class="flex items-center gap-2">
				<button
					type="button"
					onclick={() => isStep1Valid && (currentStep = 2)}
					disabled={!isStep1Valid}
					aria-current={currentStep === 2 ? 'step' : undefined}
					class="flex items-center gap-2 rounded-lg px-2 py-1 text-xs font-bold transition-colors disabled:opacity-40 {currentStep ===
					2
						? 'text-amber-600 dark:text-amber-400'
						: 'text-zinc-500 hover:text-zinc-800 dark:text-zinc-400 dark:hover:text-zinc-200'}"
				>
					<span
						class="flex h-6 w-6 items-center justify-center rounded-full {currentStep === 2
							? 'bg-amber-500 font-bold text-zinc-950'
							: 'bg-zinc-200 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400'}"
					>
						2
					</span>
					<span class="hidden sm:inline">{t('batches.wizard.step2_title')}</span>
				</button>
			</li>

			<div class="mx-2 h-0.5 flex-1 bg-zinc-200 dark:bg-zinc-800"></div>

			<!-- Step 3 -->
			<li class="flex items-center gap-2">
				<button
					type="button"
					onclick={() => isStep1Valid && isStep2Valid && (currentStep = 3)}
					disabled={!isStep1Valid || !isStep2Valid}
					aria-current={currentStep === 3 ? 'step' : undefined}
					class="flex items-center gap-2 rounded-lg px-2 py-1 text-xs font-bold transition-colors disabled:opacity-40 {currentStep ===
					3
						? 'text-amber-600 dark:text-amber-400'
						: 'text-zinc-500 hover:text-zinc-800 dark:text-zinc-400 dark:hover:text-zinc-200'}"
				>
					<span
						class="flex h-6 w-6 items-center justify-center rounded-full {currentStep === 3
							? 'bg-amber-500 font-bold text-zinc-950'
							: 'bg-zinc-200 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400'}"
					>
						3
					</span>
					<span class="hidden sm:inline">{t('batches.wizard.step3_title')}</span>
				</button>
			</li>
		</ol>
	</nav>

	<!-- Error Alert -->
	{#if error}
		<div
			role="alert"
			class="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
		>
			{error}
		</div>
	{/if}

	{#if loading}
		<div class="flex flex-col items-center justify-center gap-3 py-16 text-zinc-400">
			<Loader2 class="h-8 w-8 animate-spin text-amber-500" />
			<span class="text-sm">{t('common.loading')}</span>
		</div>
	{:else}
		<!-- STEP 1: Recipe & Identity -->
		{#if currentStep === 1}
			<div
				class="glass-panel space-y-6 rounded-3xl border border-zinc-200/80 p-6 sm:p-8 dark:border-white/10"
			>
				<div>
					<h2 class="text-lg font-bold text-zinc-900 dark:text-white">
						{t('batches.wizard.step1_title')}
					</h2>
					<p class="text-xs text-zinc-500 dark:text-zinc-400">
						{t('batches.wizard.step1_desc')}
					</p>
				</div>

				<!-- Mode Selector: From Recipe vs Ad-Hoc -->
				<div
					class="flex gap-2 rounded-xl border border-zinc-200 bg-zinc-100 p-1 dark:border-zinc-800 dark:bg-zinc-900"
				>
					<button
						type="button"
						aria-pressed={!isAdHoc}
						onclick={() => {
							isAdHoc = false;
							if (selectedRecipeId) handleRecipeSelect(selectedRecipeId);
						}}
						class="flex-1 rounded-lg py-2 text-xs font-semibold transition-colors {!isAdHoc
							? 'bg-white text-zinc-950 shadow-xs dark:bg-zinc-800 dark:text-white'
							: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400'}"
					>
						{t('batches.wizard.mode_recipe')}
					</button>
					<button
						type="button"
						aria-pressed={isAdHoc}
						onclick={() => {
							isAdHoc = true;
							selectedRecipeId = '';
						}}
						class="flex-1 rounded-lg py-2 text-xs font-semibold transition-colors {isAdHoc
							? 'bg-white text-zinc-950 shadow-xs dark:bg-zinc-800 dark:text-white'
							: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400'}"
					>
						{t('batches.wizard.mode_adhoc')}
					</button>
				</div>

				{#if !isAdHoc}
					<!-- Recipe Selector -->
					<div class="space-y-1.5">
						<label
							for="recipe-select"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.wizard.select_recipe')} *
						</label>
						<select
							id="recipe-select"
							bind:value={selectedRecipeId}
							onchange={(e) => handleRecipeSelect((e.target as HTMLSelectElement).value)}
							class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 transition-colors focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						>
							{#each recipes as r}
								<option value={r.id}>{r.name} ({r.beerStyle})</option>
							{/each}
						</select>
					</div>
				{/if}

				<!-- Batch Code & Batch Name -->
				<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
					<div class="space-y-1.5">
						<label
							for="batch-code"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.wizard.batch_code')} *
						</label>
						<input
							id="batch-code"
							type="text"
							bind:value={batchCode}
							class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
						<span class="block text-[11px] text-zinc-400">
							{t('batches.wizard.batch_code_help')}
						</span>
					</div>

					<div class="space-y-1.5">
						<label
							for="batch-name"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.wizard.batch_name')} *
						</label>
						<input
							id="batch-name"
							type="text"
							bind:value={batchName}
							class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>
				</div>

				<!-- Style & Brew Date & Volume -->
				<div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
					<div class="space-y-1.5">
						<label
							for="beer-style"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.wizard.beer_style')}
						</label>
						<input
							id="beer-style"
							type="text"
							bind:value={beerStyle}
							class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div class="space-y-1.5">
						<label
							for="brew-date"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.wizard.brew_date')}
						</label>
						<input
							id="brew-date"
							type="date"
							bind:value={brewDate}
							class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div class="space-y-1.5">
						<label
							for="batch-size"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.wizard.target_volume')} *
						</label>
						<input
							id="batch-size"
							type="number"
							step="0.5"
							min="1"
							bind:value={targetBatchSizeLiters}
							class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
						<div
							class="mt-1 inline-flex items-center rounded-lg bg-zinc-100 p-0.5 text-[11px] dark:bg-zinc-800"
						>
							<button
								type="button"
								aria-pressed={targetVolumeBasis === 'fermenter'}
								onclick={() => {
									targetVolumeBasis = 'fermenter';
								}}
								class="rounded-md px-2 py-0.5 font-medium transition-colors {targetVolumeBasis ===
								'fermenter'
									? 'bg-white text-zinc-950 shadow-xs dark:bg-zinc-700 dark:text-white'
									: 'text-zinc-500 hover:text-zinc-700 dark:text-zinc-400'}"
							>
								{t('batches.wizard.target_basis_fermenter')}
							</button>
							<button
								type="button"
								aria-pressed={targetVolumeBasis === 'packaged'}
								onclick={() => {
									targetVolumeBasis = 'packaged';
								}}
								class="rounded-md px-2 py-0.5 font-medium transition-colors {targetVolumeBasis ===
								'packaged'
									? 'bg-white text-zinc-950 shadow-xs dark:bg-zinc-700 dark:text-white'
									: 'text-zinc-500 hover:text-zinc-700 dark:text-zinc-400'}"
							>
								{t('batches.wizard.target_basis_packaged')}
							</button>
						</div>
						<p class="mt-1 text-[11px] text-zinc-500 dark:text-zinc-400">
							{#if targetVolumeBasis === 'fermenter'}
								{t('batches.wizard.target_basis_fermenter_desc', {
									fermenter: formatNumber(targetBatchSizeLiters, 1)
								})}
							{:else}
								{t('batches.wizard.target_basis_packaged_desc', {
									packaged: formatNumber(targetBatchSizeLiters, 1)
								})}
							{/if}
						</p>
					</div>
				</div>

				<!-- Step 1 Actions -->
				<div class="flex justify-end border-t border-zinc-200/60 pt-4 dark:border-white/5">
					<button
						type="button"
						onclick={() => (currentStep = 2)}
						disabled={!isStep1Valid}
						class="flex min-h-[44px] items-center gap-2 rounded-xl bg-amber-500 px-5 py-2.5 text-sm font-bold text-zinc-950 shadow-md transition-all hover:bg-amber-400 disabled:opacity-40"
					>
						<span>{t('batches.wizard.next')}</span>
						<ArrowRight class="h-4 w-4" />
					</button>
				</div>
			</div>

			<!-- STEP 2: Equipment Selection -->
		{:else if currentStep === 2}
			<div
				class="glass-panel space-y-6 rounded-3xl border border-zinc-200/80 p-6 sm:p-8 dark:border-white/10"
			>
				<div>
					<h2 class="text-lg font-bold text-zinc-900 dark:text-white">
						{t('batches.wizard.step2_title')}
					</h2>
					<p class="text-xs text-zinc-500 dark:text-zinc-400">
						{t('batches.wizard.step2_desc')}
					</p>
				</div>

				{#if equipment.length === 0}
					<div
						role="alert"
						class="flex items-center justify-between rounded-2xl border border-amber-300 bg-amber-50 p-4 text-xs text-amber-800 dark:border-amber-900/40 dark:bg-amber-950/30 dark:text-amber-300"
					>
						<div class="flex items-center gap-2">
							<Info class="h-4 w-4 shrink-0" />
							<span>{t('batches.equipment.no_equipment_notice')}</span>
						</div>
						<a
							href="/inventory/equipment"
							class="font-bold underline underline-offset-2 hover:text-amber-600 dark:hover:text-amber-200"
						>
							{t('equipment.title')}
						</a>
					</div>
				{/if}

				<!-- Boiler Selection -->
				<div class="space-y-1.5">
					<label
						for="boiler-select"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('batches.equipment.select_boiler')}
					</label>
					<select
						id="boiler-select"
						bind:value={selectedBoilerId}
						aria-describedby={selectedBoiler ? 'boiler-summary' : undefined}
						class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
					>
						<option value="">-- {t('batches.equipment.select_boiler_placeholder')} --</option>
						{#each boilers as b}
							<option value={b.id}>
								{b.name} [{getSubtypeName(b.subtype)}] ({b.capacityLiters}L){b.currentVolumeLiters >
								0
									? ` ${t('batches.equipment.in_use_suffix')}`
									: ''}
							</option>
						{/each}
					</select>
					{#if selectedBoiler}
						<div id="boiler-summary" class="mt-2.5 flex flex-wrap items-center gap-2 text-xs">
							<span
								class="inline-block rounded-md border border-orange-500/30 bg-orange-500/10 px-2 py-0.5 text-[10px] font-bold tracking-wider text-orange-600 uppercase dark:text-orange-400"
							>
								{getTypeName(selectedBoiler.type)}
							</span>
							<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400">
								{getSubtypeName(selectedBoiler.subtype)}
							</span>
							<span
								class="rounded-lg border border-zinc-200 bg-zinc-100 px-2.5 py-0.5 font-mono text-xs dark:border-zinc-800 dark:bg-zinc-900"
							>
								{t('batches.equipment.capacity', { liters: selectedBoiler.capacityLiters })}
							</span>
							{#if selectedBoiler.currentVolumeLiters > 0}
								<span
									class="rounded-md border border-amber-500/30 bg-amber-500/10 px-2 py-0.5 text-[10px] font-bold text-amber-600 dark:text-amber-400"
								>
									{t('equipment.in_use')}
								</span>
							{/if}
						</div>
					{/if}
					{#if selectedBoiler && isBoilerOverCapacity}
						<div
							role="alert"
							class="mt-2 flex items-center gap-2 rounded-xl border border-red-300 bg-red-50 p-3 text-xs text-red-700 dark:border-red-900/40 dark:bg-red-950/30 dark:text-red-300"
						>
							<AlertTriangle class="h-4 w-4 shrink-0" />
							<span>
								{t('batches.wizard.boiler_preboil_warning', {
									preBoil: formatNumber(waterSchedule.targetPreBoilVolumeLiters, 1),
									capacity: formatNumber(selectedBoiler.capacityLiters, 1)
								})}
							</span>
						</div>
					{/if}
				</div>

				<!-- Fermenter Selection -->
				<div class="space-y-1.5">
					<label
						for="fermenter-select"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('batches.equipment.select_fermenter')} *
					</label>
					<select
						id="fermenter-select"
						bind:value={selectedFermenterId}
						aria-describedby={selectedFermenter ? 'fermenter-summary' : undefined}
						class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
					>
						<option value="">-- {t('batches.equipment.select_fermenter_placeholder')} --</option>
						{#each fermenters as f}
							<option value={f.id}>
								{f.name} [{getSubtypeName(f.subtype)}] ({f.capacityLiters}L){f.currentVolumeLiters >
								0
									? ` ${t('batches.equipment.occupied_suffix')}`
									: ''}
							</option>
						{/each}
					</select>
				</div>

				<!-- Capacity & Warning Checks -->
				{#if selectedFermenter}
					<div class="space-y-3 pt-2">
						<!-- Volume Summary Pill & Equipment Tags -->
						<div id="fermenter-summary" class="flex flex-wrap items-center gap-2 text-xs">
							<span
								class="inline-block rounded-md border border-emerald-500/30 bg-emerald-500/10 px-2 py-0.5 text-[10px] font-bold tracking-wider text-emerald-600 uppercase dark:text-emerald-400"
							>
								{getTypeName(selectedFermenter.type)}
							</span>
							<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400">
								{getSubtypeName(selectedFermenter.subtype)}
							</span>
							<span
								class="rounded-lg border border-zinc-200 bg-zinc-100 px-2.5 py-0.5 font-mono text-xs dark:border-zinc-800 dark:bg-zinc-900"
							>
								{t('batches.equipment.capacity', { liters: selectedFermenter.capacityLiters })}
							</span>
							{#if fermenterHeadspacePercent !== null}
								<span
									class="rounded-lg border border-zinc-200 bg-zinc-100 px-2.5 py-0.5 font-mono text-xs dark:border-zinc-800 dark:bg-zinc-900"
								>
									{t('batches.equipment.headspace', { headspace: fermenterHeadspacePercent })}
								</span>
							{/if}
							{#if selectedFermenter.currentVolumeLiters > 0}
								<span
									class="rounded-md border border-amber-500/30 bg-amber-500/10 px-2 py-0.5 text-[10px] font-bold text-amber-600 dark:text-amber-400"
								>
									{t('equipment.occupied')}
								</span>
							{/if}
						</div>

						<!-- Danger: Over capacity -->
						{#if isOverCapacity}
							<div
								role="alert"
								class="flex items-center gap-2 rounded-xl border border-red-300 bg-red-50 p-3 text-xs text-red-700 dark:border-red-900/40 dark:bg-red-950/30 dark:text-red-300"
							>
								<AlertTriangle class="h-4 w-4 shrink-0" />
								<span>
									{t('batches.equipment.capacity_warning', {
										batchVol: targetBatchSizeLiters,
										vesselCap: selectedFermenter.capacityLiters
									})}
								</span>
							</div>
						{/if}

						<!-- Warning: Krausen headspace risk (>85%) -->
						{#if isKrausenRisk}
							<div
								role="alert"
								class="flex items-center gap-2 rounded-xl border border-amber-300 bg-amber-50 p-3 text-xs text-amber-700 dark:border-amber-900/40 dark:bg-amber-950/30 dark:text-amber-300"
							>
								<AlertTriangle class="h-4 w-4 shrink-0" />
								<span>
									{t('batches.equipment.headspace_warning', {
										headspace: fermenterHeadspacePercent ?? 0
									})}
								</span>
							</div>
						{/if}

						<!-- Occupancy alert -->
						{#if isFermenterOccupied}
							<div
								role="alert"
								class="flex items-center gap-2 rounded-xl border border-amber-300 bg-amber-50 p-3 text-xs text-amber-700 dark:border-amber-900/40 dark:bg-amber-950/30 dark:text-amber-300"
							>
								<AlertTriangle class="h-4 w-4 shrink-0" />
								<span>
									{t('batches.equipment.occupied_warning', {
										vesselName: selectedFermenter.name
									})}
								</span>
							</div>
						{/if}
					</div>
				{/if}

				<!-- Fermentation Sensor Selection -->
				<div class="space-y-1.5">
					<label
						for="fermentation-sensor-select"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('batches.equipment.select_fermentation_sensor')}
					</label>
					<select
						id="fermentation-sensor-select"
						bind:value={selectedFermentationSensorId}
						aria-describedby={selectedFermentationSensor
							? 'fermentation-sensor-summary'
							: undefined}
						class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
					>
						<option value=""
							>-- {t('batches.equipment.select_fermentation_sensor_placeholder')} --</option
						>
						{#each sensors as s}
							<option value={s.id}>
								{s.name} [{getSubtypeName(s.subtype)}]
							</option>
						{/each}
					</select>
					{#if selectedFermentationSensor}
						<div
							id="fermentation-sensor-summary"
							class="mt-2.5 flex flex-wrap items-center gap-2 text-xs"
						>
							<span
								class="inline-block rounded-md border border-cyan-500/30 bg-cyan-500/10 px-2 py-0.5 text-[10px] font-bold tracking-wider text-cyan-600 uppercase dark:text-cyan-400"
							>
								{getTypeName(selectedFermentationSensor.type)}
							</span>
							<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400">
								{getSubtypeName(selectedFermentationSensor.subtype)}
							</span>
							{#if selectedFermentationSensor.notes}
								<span class="text-xs text-zinc-500 italic dark:text-zinc-400">
									{selectedFermentationSensor.notes}
								</span>
							{/if}
						</div>
					{/if}
					<p class="text-[11px] text-zinc-500 dark:text-zinc-400">
						{t('batches.equipment.fermentation_sensor_help')}
					</p>
				</div>

				<!-- Packaging Vessel Selection -->
				<div class="space-y-1.5">
					<label
						for="packaging-vessel-select"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('batches.equipment.select_packaging_vessel')}
					</label>
					<select
						id="packaging-vessel-select"
						bind:value={selectedPackagingVesselId}
						aria-describedby={selectedPackagingVessel ? 'packaging-vessel-summary' : undefined}
						class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
					>
						<option value=""
							>-- {t('batches.equipment.select_packaging_vessel_placeholder')} --</option
						>
						{#each packagingVessels as p}
							<option value={p.id}>
								{p.name} [{getSubtypeName(p.subtype)}] ({p.capacityLiters}L){p.currentVolumeLiters >
								0
									? ` ${t('batches.equipment.in_use_suffix')}`
									: ''}
							</option>
						{/each}
					</select>
					{#if selectedPackagingVessel}
						<div
							id="packaging-vessel-summary"
							class="mt-2.5 flex flex-wrap items-center gap-2 text-xs"
						>
							<span
								class="inline-block rounded-md border border-purple-500/30 bg-purple-500/10 px-2 py-0.5 text-[10px] font-bold tracking-wider text-purple-600 uppercase dark:text-purple-400"
							>
								{getTypeName(selectedPackagingVessel.type)}
							</span>
							<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400">
								{getSubtypeName(selectedPackagingVessel.subtype)}
							</span>
							<span
								class="rounded-lg border border-zinc-200 bg-zinc-100 px-2.5 py-0.5 font-mono text-xs dark:border-zinc-800 dark:bg-zinc-900"
							>
								{t('batches.equipment.capacity', {
									liters: selectedPackagingVessel.capacityLiters
								})}
							</span>
							{#if selectedPackagingVessel.currentVolumeLiters > 0}
								<span
									class="rounded-md border border-amber-500/30 bg-amber-500/10 px-2 py-0.5 text-[10px] font-bold text-amber-600 dark:text-amber-400"
								>
									{t('equipment.in_use')}
								</span>
							{/if}
						</div>
					{/if}
				</div>

				<!-- Water Schedule & Volume Milestones Card -->
				<div
					class="rounded-2xl border border-amber-500/20 bg-amber-500/5 p-4 dark:border-amber-500/10 dark:bg-amber-500/5"
				>
					<div class="mb-3 flex items-center gap-2">
						<Droplets class="h-4 w-4 text-amber-500" />
						<h3
							class="text-xs font-bold tracking-wider text-amber-600 uppercase dark:text-amber-400"
						>
							{t('batches.wizard.water_schedule_title')}
						</h3>
						<button
							type="button"
							onclick={() => (showWaterInfo = !showWaterInfo)}
							class="ml-auto flex h-5 w-5 items-center justify-center rounded-full text-amber-500 transition-colors hover:bg-amber-500/10"
							aria-label={t('batches.wizard.water_info_title')}
							aria-expanded={showWaterInfo}
							aria-controls="water-info-panel"
						>
							{#if showWaterInfo}
								<X class="h-3.5 w-3.5" />
							{:else}
								<Info class="h-3.5 w-3.5" />
							{/if}
						</button>
					</div>

					{#if showWaterInfo}
						<div
							id="water-info-panel"
							class="mb-4 space-y-2.5 rounded-xl border border-amber-200/60 bg-amber-50/80 p-3 text-xs text-zinc-700 dark:border-amber-900/30 dark:bg-amber-950/20 dark:text-zinc-300"
						>
							<p class="font-medium text-amber-700 dark:text-amber-300">
								{t('batches.wizard.water_info_title')}
							</p>
							<p class="text-[11px] leading-relaxed">
								{t('batches.wizard.water_info_intro')}
							</p>
							<dl class="space-y-1.5 text-[11px]">
								<div>
									<dt class="font-semibold text-zinc-800 dark:text-zinc-200">
										{t('batches.wizard.total_water')}
									</dt>
									<dd class="text-zinc-600 dark:text-zinc-400">
										{t('batches.wizard.water_info_total_water')}
									</dd>
								</div>
								<div>
									<dt class="font-semibold text-zinc-800 dark:text-zinc-200">
										{t('batches.wizard.strike_water')}
									</dt>
									<dd class="text-zinc-600 dark:text-zinc-400">
										{t('batches.wizard.water_info_strike_water')}
									</dd>
								</div>
								<div>
									<dt class="font-semibold text-zinc-800 dark:text-zinc-200">
										{t('batches.wizard.sparge_water')}
									</dt>
									<dd class="text-zinc-600 dark:text-zinc-400">
										{t('batches.wizard.water_info_sparge_water')}
									</dd>
								</div>
								<div>
									<dt class="font-semibold text-zinc-800 dark:text-zinc-200">
										{t('batches.wizard.grain_absorption_loss')}
									</dt>
									<dd class="text-zinc-600 dark:text-zinc-400">
										{t('batches.wizard.water_info_grain_absorption')}
									</dd>
								</div>
								<div>
									<dt class="font-semibold text-zinc-800 dark:text-zinc-200">
										{t('batches.wizard.pre_boil_target')}
									</dt>
									<dd class="text-zinc-600 dark:text-zinc-400">
										{t('batches.wizard.water_info_pre_boil')}
									</dd>
								</div>
								<div>
									<dt class="font-semibold text-zinc-800 dark:text-zinc-200">
										{t('batches.wizard.post_boil_target')}
									</dt>
									<dd class="text-zinc-600 dark:text-zinc-400">
										{t('batches.wizard.water_info_post_boil')}
									</dd>
								</div>
								<div>
									<dt class="font-semibold text-zinc-800 dark:text-zinc-200">
										{t('batches.wizard.into_fermenter_target')}
									</dt>
									<dd class="text-zinc-600 dark:text-zinc-400">
										{t('batches.wizard.water_info_into_fermenter')}
									</dd>
								</div>
								<div>
									<dt class="font-semibold text-zinc-800 dark:text-zinc-200">
										{t('batches.wizard.packaged_target')}
									</dt>
									<dd class="text-zinc-600 dark:text-zinc-400">
										{t('batches.wizard.water_info_packaged')}
									</dd>
								</div>
							</dl>
						</div>
					{/if}

					<div class="grid grid-cols-2 gap-3 text-xs sm:grid-cols-4">
						<div
							class="rounded-xl border border-zinc-200/80 bg-white/80 p-2.5 dark:border-white/10 dark:bg-zinc-900/60"
						>
							<span class="block text-[11px] text-zinc-500 dark:text-zinc-400">
								{t('batches.wizard.total_water')}
							</span>
							<span class="font-mono text-sm font-bold text-zinc-900 dark:text-white">
								{formatNumber(waterSchedule.totalWaterLiters, 1)} L
							</span>
						</div>

						<div
							class="rounded-xl border border-zinc-200/80 bg-white/80 p-2.5 dark:border-white/10 dark:bg-zinc-900/60"
						>
							<span class="block text-[11px] text-zinc-500 dark:text-zinc-400">
								{t('batches.wizard.strike_water')}
							</span>
							<span class="font-mono text-sm font-bold text-amber-600 dark:text-amber-400">
								{formatNumber(waterSchedule.strikeWaterLiters, 1)} L
							</span>
							<span class="block text-[10px] font-medium text-amber-700/80 dark:text-amber-400/80">
								@ {settings.formatTemperature(parseFloat(calculatedStrikeTempC))}
							</span>
						</div>

						<div
							class="rounded-xl border border-zinc-200/80 bg-white/80 p-2.5 dark:border-white/10 dark:bg-zinc-900/60"
						>
							<span class="block text-[11px] text-zinc-500 dark:text-zinc-400">
								{t('batches.wizard.sparge_water')}
							</span>
							<span class="font-mono text-sm font-bold text-amber-600 dark:text-amber-400">
								{formatNumber(waterSchedule.spargeWaterLiters, 1)} L
							</span>
							{#if spargeEnabled}
								<span
									class="block text-[10px] font-medium text-amber-700/80 dark:text-amber-400/80"
								>
									@ {settings.formatTemperature(targetSpargeTempC)}
								</span>
							{:else}
								<span class="block text-[10px] font-medium text-zinc-400">
									{t('batches.wizard.sparge_disabled_label')}
								</span>
							{/if}
						</div>

						<div
							class="rounded-xl border border-zinc-200/80 bg-white/80 p-2.5 dark:border-white/10 dark:bg-zinc-900/60"
						>
							<span class="block text-[11px] text-zinc-500 dark:text-zinc-400">
								{t('batches.wizard.grain_absorption_loss')}
							</span>
							<span class="font-mono text-sm font-bold text-amber-700 dark:text-amber-300">
								~{formatNumber(waterSchedule.grainAbsorptionLossLiters, 1)} L
							</span>
						</div>

						<div
							class="rounded-xl border border-zinc-200/80 bg-white/80 p-2.5 dark:border-white/10 dark:bg-zinc-900/60"
						>
							<span class="block text-[11px] text-zinc-500 dark:text-zinc-400">
								{t('batches.wizard.pre_boil_target')}
							</span>
							<span class="font-mono text-sm font-bold text-zinc-900 dark:text-white">
								{formatNumber(waterSchedule.targetPreBoilVolumeLiters, 1)} L
							</span>
						</div>

						<div
							class="rounded-xl border border-zinc-200/80 bg-white/80 p-2.5 dark:border-white/10 dark:bg-zinc-900/60"
						>
							<span class="block text-[11px] text-zinc-500 dark:text-zinc-400">
								{t('batches.wizard.post_boil_target')}
							</span>
							<span class="font-mono text-sm font-bold text-zinc-900 dark:text-white">
								{formatNumber(waterSchedule.targetPostBoilVolumeLiters, 1)} L
							</span>
						</div>

						<div
							class="rounded-xl border border-zinc-200/80 bg-white/80 p-2.5 dark:border-white/10 dark:bg-zinc-900/60"
						>
							<span class="block text-[11px] text-zinc-500 dark:text-zinc-400">
								{t('batches.wizard.into_fermenter_target')}
							</span>
							<span class="font-mono text-sm font-bold text-zinc-900 dark:text-white">
								{formatNumber(waterSchedule.targetFermenterVolumeLiters, 1)} L
							</span>
						</div>

						<div
							class="rounded-xl border border-zinc-200/80 bg-white/80 p-2.5 dark:border-white/10 dark:bg-zinc-900/60"
						>
							<span class="block text-[11px] text-zinc-500 dark:text-zinc-400">
								{t('batches.wizard.packaged_target')}
							</span>
							<span class="font-mono text-sm font-bold text-emerald-600 dark:text-emerald-400">
								{formatNumber(waterSchedule.targetPackagedVolumeLiters, 1)} L
							</span>
						</div>
					</div>
				</div>

				<!-- Equipment Losses & Calibration Overrides Card -->
				<div
					class="rounded-2xl border border-zinc-200/80 bg-zinc-50/50 p-4 dark:border-white/10 dark:bg-zinc-900/30"
				>
					<div class="mb-3 flex flex-wrap items-center justify-between gap-2">
						<div class="flex items-center gap-2">
							<Settings2 class="h-4 w-4 text-zinc-500 dark:text-zinc-400" />
							<div>
								<h3
									class="text-xs font-bold tracking-wider text-zinc-700 uppercase dark:text-zinc-300"
								>
									{t('batches.wizard.equipment_losses_title')}
								</h3>
								<p class="text-[11px] text-zinc-500 dark:text-zinc-400">
									{t('batches.wizard.equipment_losses_desc')}
								</p>
							</div>
						</div>
						<button
							type="button"
							onclick={resetEquipmentDefaults}
							class="flex items-center gap-1.5 rounded-lg border border-zinc-200 bg-white px-2.5 py-1 text-xs font-medium text-zinc-600 shadow-sm transition-colors hover:bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-700"
						>
							<RotateCcw class="h-3 w-3" />
							<span>{t('batches.wizard.reset_equipment_defaults')}</span>
						</button>
					</div>

					<div class="mb-3 flex items-start gap-2">
						<input
							id="sparge-enabled"
							type="checkbox"
							bind:checked={spargeEnabled}
							class="mt-0.5 h-4 w-4 rounded border-zinc-300 text-amber-500 focus:ring-amber-500 dark:border-zinc-600"
						/>
						<div>
							<label
								for="sparge-enabled"
								class="text-xs font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('batches.wizard.sparge_enabled')}
							</label>
							<p class="text-[11px] text-zinc-400">
								{t('batches.wizard.sparge_enabled_help')}
							</p>
						</div>
					</div>

					<div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
						<!-- Boil-off rate -->
						<div>
							<div class="flex items-center justify-between">
								<label
									for="boiloff-override-input"
									class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
								>
									{t('equipment.boil_off_rate')} ({t('equipment.boil_off_rate_unit')})
								</label>
								<span class="text-[10px] text-zinc-400">
									{t('batches.wizard.default_from_equipment', {
										value: `${selectedBoiler?.boilOffRatePerHour ?? 3.0} L/h`
									})}
								</span>
							</div>
							<input
								id="boiloff-override-input"
								type="number"
								step="0.1"
								min="0"
								max="50"
								bind:value={customBoilOffRate}
								class="mt-1 h-9 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
							/>
						</div>

						<!-- Kettle trub loss -->
						<div>
							<div class="flex items-center justify-between">
								<label
									for="kettletrub-override-input"
									class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
								>
									{t('equipment.trub_loss')} (L)
								</label>
								<span class="text-[10px] text-zinc-400">
									{t('batches.wizard.default_from_equipment', {
										value: `${selectedBoiler?.trubLossLiters ?? 1.5} L`
									})}
								</span>
							</div>
							<input
								id="kettletrub-override-input"
								type="number"
								step="0.1"
								min="0"
								max="50"
								bind:value={customKettleTrub}
								class="mt-1 h-9 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
							/>
						</div>

						<!-- Mash tun dead space -->
						<div>
							<div class="flex items-center justify-between">
								<label
									for="deadspace-override-input"
									class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
								>
									{t('equipment.mash_tun_dead_space')} (L)
								</label>
								<span class="text-[10px] text-zinc-400">
									{t('batches.wizard.default_from_equipment', {
										value: `${selectedBoiler?.mashTunDeadSpaceLiters ?? 0.0} L`
									})}
								</span>
							</div>
							<input
								id="deadspace-override-input"
								type="number"
								step="0.1"
								min="0"
								max="50"
								bind:value={customMashTunDeadSpace}
								class="mt-1 h-9 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
							/>
						</div>

						<!-- Fermenter trub loss -->
						<div>
							<div class="flex items-center justify-between">
								<label
									for="fermenterloss-override-input"
									class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
								>
									{t('equipment.trub_loss_fermenter')} (L)
								</label>
								<span class="text-[10px] text-zinc-400">
									{t('batches.wizard.default_from_equipment', {
										value: `${selectedFermenter?.trubLossLiters ?? 1.5} L`
									})}
								</span>
							</div>
							<input
								id="fermenterloss-override-input"
								type="number"
								step="0.1"
								min="0"
								max="50"
								bind:value={customFermenterLoss}
								class="mt-1 h-9 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
							/>
						</div>

						<!-- Packaging / Transfer loss -->
						<div>
							<div class="flex items-center justify-between">
								<label
									for="packagingloss-override-input"
									class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
								>
									{t('equipment.packaging_loss')} (L)
								</label>
								<span class="text-[10px] text-zinc-400">
									{t('batches.wizard.default_packaging_loss', {
										value: `${selectedPackagingVessel?.packagingLossLiters ?? 0.5} L`
									})}
								</span>
							</div>
							<input
								id="packagingloss-override-input"
								type="number"
								step="0.1"
								min="0"
								max="50"
								bind:value={customPackagingLoss}
								class="mt-1 h-9 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
							/>
						</div>
					</div>
				</div>

				<!-- Step 2 Actions -->
				<div
					class="flex items-center justify-between border-t border-zinc-200/60 pt-4 dark:border-white/5"
				>
					<button
						type="button"
						onclick={() => (currentStep = 1)}
						class="flex min-h-[44px] items-center gap-2 rounded-xl border border-zinc-200 px-4 py-2.5 text-sm font-semibold text-zinc-700 transition-colors hover:bg-zinc-100 dark:border-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-900"
					>
						<ArrowLeft class="h-4 w-4" />
						<span>{t('batches.wizard.prev')}</span>
					</button>

					<button
						type="button"
						onclick={() => (currentStep = 3)}
						disabled={!isStep2Valid}
						class="flex min-h-[44px] items-center gap-2 rounded-xl bg-amber-500 px-5 py-2.5 text-sm font-bold text-zinc-950 shadow-md transition-all hover:bg-amber-400 disabled:opacity-40"
					>
						<span>{t('batches.wizard.next')}</span>
						<ArrowRight class="h-4 w-4" />
					</button>
				</div>
			</div>

			<!-- STEP 3: Targets & Settings -->
		{:else if currentStep === 3}
			<div
				class="glass-panel space-y-6 rounded-3xl border border-zinc-200/80 p-6 sm:p-8 dark:border-white/10"
			>
				<div>
					<h2 class="text-lg font-bold text-zinc-900 dark:text-white">
						{t('batches.wizard.step3_title')}
					</h2>
					<p class="text-xs text-zinc-500 dark:text-zinc-400">
						{t('batches.wizard.step3_desc')}
					</p>
				</div>

				<!-- Target Metrics Matrix -->
				<div class="grid grid-cols-2 gap-3 sm:grid-cols-4">
					<div
						class="rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-3 text-center dark:border-zinc-800/80 dark:bg-zinc-900/60"
					>
						<span class="block text-[10px] font-medium text-zinc-500 dark:text-zinc-400">
							{t('batches.telemetry.target_og')}
						</span>
						<input
							type="number"
							step="0.001"
							bind:value={targetOg}
							class="w-full bg-transparent text-center font-mono font-bold text-amber-600 focus:outline-none dark:text-amber-400"
						/>
					</div>

					<div
						class="rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-3 text-center dark:border-zinc-800/80 dark:bg-zinc-900/60"
					>
						<span class="block text-[10px] font-medium text-zinc-500 dark:text-zinc-400">
							{t('batches.telemetry.target_fg')}
						</span>
						<input
							type="number"
							step="0.001"
							bind:value={targetFg}
							class="w-full bg-transparent text-center font-mono font-bold text-emerald-600 focus:outline-none dark:text-emerald-400"
						/>
					</div>

					<div
						class="rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-3 text-center dark:border-zinc-800/80 dark:bg-zinc-900/60"
					>
						<span class="block text-[10px] font-medium text-zinc-500 dark:text-zinc-400">
							{t('batches.target_abv')}
						</span>
						<span class="font-mono font-bold text-zinc-800 dark:text-zinc-200">
							{targetAbv}%
						</span>
					</div>

					<div
						class="rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-3 text-center dark:border-zinc-800/80 dark:bg-zinc-900/60"
					>
						<span class="block text-[10px] font-medium text-zinc-500 dark:text-zinc-400">
							{t('batches.telemetry.target_ibu')}
						</span>
						<span class="font-mono font-bold text-sky-600 dark:text-sky-400">
							{targetIbu}
						</span>
					</div>
				</div>

				<!-- Strike & Sparge Water Calculation Widget -->
				<div class="space-y-3 rounded-2xl border border-amber-500/20 bg-amber-500/5 p-4">
					<div class="flex items-center gap-2 text-xs font-bold text-amber-600 dark:text-amber-400">
						<Sparkles class="h-4 w-4" />
						<span>{t('batches.wizard.strike_and_sparge_title')}</span>
					</div>
					<div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
						<div
							class="rounded-xl border border-amber-500/20 bg-white/60 p-3 dark:border-white/5 dark:bg-zinc-900/60"
						>
							<span
								class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400"
							>
								{t('batches.wizard.strike_temp')}
							</span>
							<span class="font-mono text-base font-bold text-amber-600 dark:text-amber-400">
								{settings.formatTemperature(parseFloat(calculatedStrikeTempC))}
							</span>
							<p class="mt-1 text-[11px] text-zinc-500 dark:text-zinc-400">
								{t('batches.wizard.strike_temp_help', {
									targetTemp: settings.formatTemperature(targetMashTempC)
								})}
							</p>
						</div>

						{#if spargeEnabled}
							<div
								class="rounded-xl border border-amber-500/20 bg-white/60 p-3 dark:border-white/5 dark:bg-zinc-900/60"
							>
								<span
									class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400"
								>
									{t('batches.wizard.sparge_temp')}
								</span>
								<span class="font-mono text-base font-bold text-amber-600 dark:text-amber-400">
									{settings.formatTemperature(targetSpargeTempC)}
								</span>
								<p class="mt-1 text-[11px] text-zinc-500 dark:text-zinc-400">
									{t('batches.wizard.sparge_temp_help', {
										targetTemp: settings.formatTemperature(targetSpargeTempC)
									})}
								</p>
							</div>
						{:else}
							<div
								class="rounded-xl border border-amber-500/20 bg-white/60 p-3 dark:border-white/5 dark:bg-zinc-900/60"
							>
								<span
									class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400"
								>
									{t('batches.wizard.sparge_temp')}
								</span>
								<span class="font-mono text-xs font-semibold text-zinc-500 dark:text-zinc-400">
									{t('batches.wizard.sparge_disabled_label')}
								</span>
								<p class="mt-1 text-[11px] text-zinc-500 dark:text-zinc-400">
									{t('batches.wizard.no_sparge_biab_help')}
								</p>
							</div>
						{/if}
					</div>
				</div>

				<!-- Initial Brew Day Optional Readings -->
				<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
					<div class="space-y-1.5">
						<label
							for="measured-og"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.wizard.measured_og_optional')}
						</label>
						<input
							id="measured-og"
							type="number"
							step="0.001"
							placeholder={formatNumber(targetOg, 3)}
							bind:value={measuredOg}
							class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div class="space-y-1.5">
						<label
							for="pitch-temp"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.wizard.pitch_temp_optional')}
						</label>
						<input
							id="pitch-temp"
							type="number"
							step="0.5"
							placeholder="19.0"
							bind:value={pitchTemperatureC}
							class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>
				</div>

				<!-- Inherited Fermentation Schedule Card -->
				{#if recipeFermentationSteps.length > 0}
					<div class="space-y-3 rounded-2xl border border-cyan-500/20 bg-cyan-500/5 p-4">
						<div class="flex items-center gap-2 text-xs font-bold text-cyan-600 dark:text-cyan-400">
							<Activity class="h-4 w-4" />
							<span>{t('batches.wizard.fermentation_profile_title')}</span>
						</div>
						<div class="grid grid-cols-1 gap-2 text-xs sm:grid-cols-2 md:grid-cols-3">
							{#each recipeFermentationSteps as step}
								<div
									class="rounded-xl border border-cyan-500/20 bg-white/60 p-2.5 dark:border-zinc-800 dark:bg-zinc-900/60"
								>
									<div class="flex items-center justify-between">
										<span class="font-bold text-zinc-900 dark:text-white">{step.name}</span>
										<span class="text-[10px] font-semibold text-cyan-600 dark:text-cyan-400">
											{t(`fermentation_profile.type_${step.type.toLowerCase()}`)}
										</span>
									</div>
									<div
										class="mt-1 flex items-center justify-between font-mono text-zinc-500 dark:text-zinc-400"
									>
										<span>{settings.formatTemperature(step.targetTemperatureC)}</span>
										<span>{step.durationDays} {t('fermentation_profile.days')}</span>
									</div>
								</div>
							{/each}
						</div>
					</div>
				{/if}

				<!-- Notes -->
				<div class="space-y-1.5">
					<label
						for="batch-notes"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('batches.wizard.notes_label')}
					</label>
					<textarea
						id="batch-notes"
						rows="3"
						bind:value={notes}
						placeholder={t('batches.wizard.notes_placeholder')}
						class="w-full rounded-xl border border-zinc-200/80 bg-white p-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
					></textarea>
				</div>

				<!-- Step 3 Actions -->
				<div
					class="flex items-center justify-between border-t border-zinc-200/60 pt-4 dark:border-white/5"
				>
					<button
						type="button"
						onclick={() => (currentStep = 2)}
						class="flex min-h-[44px] items-center gap-2 rounded-xl border border-zinc-200 px-4 py-2.5 text-sm font-semibold text-zinc-700 transition-colors hover:bg-zinc-100 dark:border-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-900"
					>
						<ArrowLeft class="h-4 w-4" />
						<span>{t('batches.wizard.prev')}</span>
					</button>

					<button
						type="button"
						onclick={() => handleSubmit()}
						disabled={submitting}
						class="flex min-h-[44px] items-center gap-2 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-6 py-2.5 text-sm font-bold text-zinc-950 shadow-md transition-all hover:from-amber-400 hover:to-amber-500 active:scale-[0.98] disabled:opacity-50"
					>
						{#if submitting}
							<Loader2 class="h-4 w-4 animate-spin" />
							<span>{t('batches.wizard.submitting')}</span>
						{:else}
							<CheckCircle2 class="h-4 w-4 stroke-[2.5]" />
							<span>{t('batches.wizard.submit')}</span>
						{/if}
					</button>
				</div>
			</div>
		{/if}
	{/if}
</div>

<InsufficientStockModal
	open={stockModalOpen}
	{shortages}
	onCancel={() => (stockModalOpen = false)}
	onContinue={handleContinueAnyway}
/>
