<script lang="ts">
	import { goto, beforeNavigate } from '$app/navigation';
	import { api, ApiClientError } from '$lib/api/client';
	import { auth } from '$lib/stores/auth.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import { t } from '$lib/i18n/index.svelte';
	import { formatNumber } from '$lib/utils/formatNumber';
	import { calculateBrewMetrics, type CalculatorItem } from '$lib/calculators/brewing';
	import type {
		IngredientDto,
		IngredientType,
		IngredientUsage,
		RecipeDetailDto
	} from '$lib/types/api';
	import type { ParsedRecipe } from '$lib/parsers/types';
	import IngredientsCard, {
		type FormIngredientItem
	} from '$lib/components/recipes/IngredientsCard.svelte';
	import MashStepsCard, {
		type FormMashStepItem
	} from '$lib/components/recipes/MashStepsCard.svelte';
	import FermentationStepsCard, {
		type FormFermentationStepItem
	} from '$lib/components/recipes/FermentationStepsCard.svelte';
	import RecipeImportModal from '$lib/components/recipes/RecipeImportModal.svelte';
	import RecipeExportModal from '$lib/components/recipes/RecipeExportModal.svelte';
	import ConfirmModal from '$lib/components/ui/ConfirmModal.svelte';
	import BeerGlass from '$lib/components/brewery/BeerGlass.svelte';
	import type { ExportableRecipe } from '$lib/exporters/types';
	import {
		Sparkles,
		Save,
		UploadCloud,
		Download,
		Info,
		AlertCircle,
		ChevronDown,
		Sliders
	} from '@lucide/svelte';

	interface Props {
		mode: 'new' | 'edit';
		initialRecipe?: RecipeDetailDto | null;
		catalogIngredients: IngredientDto[];
	}

	let {
		mode,
		initialRecipe = null,
		catalogIngredients: initialCatalogIngredients
	}: Props = $props();

	let extraCreatedIngredients = $state<IngredientDto[]>([]);

	let currentCatalogIngredients = $derived([
		...initialCatalogIngredients,
		...extraCreatedIngredients
	]);

	function handleIngredientsCreated(newIngredients: IngredientDto[]) {
		extraCreatedIngredients = [...extraCreatedIngredients, ...newIngredients];
	}

	// Modal states
	let isImportModalOpen = $state(false);
	let isExportModalOpen = $state(false);
	let hudExpanded = $state(false);

	// Form state initialized with defaults
	let name = $state('');
	let description = $state('');
	let beerStyle = $state('');
	let batchSizeLiters = $state(settings.defaultBatchSizeLiters || 20.0);
	let boilTimeMinutes = $state(settings.defaultBoilTimeMinutes || 60);
	let efficiencyPercent = $state(settings.defaultEfficiencyPercent || 72.0);
	let isPublic = $state(true);
	let items = $state<FormIngredientItem[]>([]);
	let mashSteps = $state<FormMashStepItem[]>([
		{
			id: Math.random().toString(36).substring(2, 9),
			stepOrder: 1,
			name: 'Saccharification Rest',
			type: 'Infusion',
			temperatureC: 65.0,
			durationMinutes: 60
		}
	]);
	let fermentationSteps = $state<FormFermentationStepItem[]>([
		{
			id: Math.random().toString(36).substring(2, 9),
			stepOrder: 1,
			name: 'Primary Fermentation',
			type: 'Primary',
			targetTemperatureC: 19.0,
			durationDays: 14
		}
	]);

	let isHydrated = false;

	$effect(() => {
		if (isHydrated) return;
		if (mode === 'edit' && initialRecipe) {
			name = initialRecipe.name;
			description = initialRecipe.description ?? '';
			beerStyle = initialRecipe.beerStyle;
			batchSizeLiters = initialRecipe.batchSizeLiters;
			boilTimeMinutes = initialRecipe.boilTimeMinutes;
			efficiencyPercent = initialRecipe.efficiencyPercent;
			isPublic = initialRecipe.isPublic;
			items = initialRecipe.ingredients.map((ing) => ({
				id: ing.id || Math.random().toString(36).substring(2, 9),
				ingredientId: ing.ingredientId,
				type: ing.ingredientType,
				amount: ing.amount,
				unit: ing.unit,
				durationMinutes: ing.durationMinutes ?? 0,
				usage: ing.usage,
				notes: ing.notes ?? undefined,
				form: ing.form ?? undefined
			}));
			if (initialRecipe.mashSteps && initialRecipe.mashSteps.length > 0) {
				mashSteps = initialRecipe.mashSteps.map((s) => ({
					id: s.id || Math.random().toString(36).substring(2, 9),
					stepOrder: s.stepOrder,
					name: s.name,
					type: s.type,
					temperatureC: s.temperatureC,
					durationMinutes: s.durationMinutes,
					rampTimeMinutes: s.rampTimeMinutes,
					infuseAmountLiters: s.infuseAmountLiters,
					notes: s.notes
				}));
			}
			if (initialRecipe.fermentationSteps && initialRecipe.fermentationSteps.length > 0) {
				fermentationSteps = initialRecipe.fermentationSteps.map((s) => ({
					id: s.id || Math.random().toString(36).substring(2, 9),
					stepOrder: s.stepOrder,
					name: s.name,
					type: s.type,
					targetTemperatureC: s.targetTemperatureC,
					durationDays: s.durationDays,
					rampTimeHours: s.rampTimeHours,
					triggerGravity: s.triggerGravity,
					notes: s.notes
				}));
			}
			isHydrated = true;
		} else if (mode === 'new') {
			isHydrated = true;
		}
	});

	// --- Unsaved Changes Guard ---

	/**
	 * Serializes the current form state into a comparable string.
	 * Excludes auto-generated `id` fields from items/mashSteps since those
	 * are ephemeral client-side keys and don't represent user changes.
	 */
	function serializeFormState(): string {
		return JSON.stringify({
			name,
			description,
			beerStyle,
			batchSizeLiters,
			boilTimeMinutes,
			efficiencyPercent,
			isPublic,
			items: items.map((i) => ({
				ingredientId: i.ingredientId,
				type: i.type,
				amount: i.amount,
				unit: i.unit,
				durationMinutes: i.durationMinutes,
				usage: i.usage,
				notes: i.notes,
				form: i.form
			})),
			mashSteps: mashSteps.map((s) => ({
				stepOrder: s.stepOrder,
				name: s.name,
				type: s.type,
				temperatureC: s.temperatureC,
				durationMinutes: s.durationMinutes,
				rampTimeMinutes: s.rampTimeMinutes,
				infuseAmountLiters: s.infuseAmountLiters,
				notes: s.notes
			})),
			fermentationSteps: fermentationSteps.map((s) => ({
				stepOrder: s.stepOrder,
				name: s.name,
				type: s.type,
				targetTemperatureC: s.targetTemperatureC,
				durationDays: s.durationDays,
				rampTimeHours: s.rampTimeHours,
				triggerGravity: s.triggerGravity,
				notes: s.notes
			}))
		});
	}

	let initialFormSnapshot = $state('');
	let savedSuccessfully = $state(false);

	// Capture the initial snapshot once hydration completes
	$effect(() => {
		if (isHydrated && !initialFormSnapshot) {
			initialFormSnapshot = serializeFormState();
		}
	});

	let hasUnsavedChanges = $derived(
		isHydrated && initialFormSnapshot !== '' && serializeFormState() !== initialFormSnapshot
	);

	// Unsaved changes modal state
	let showUnsavedModal = $state(false);
	let pendingNavigationUrl = $state<string | null>(null);

	// SvelteKit in-app navigation guard
	beforeNavigate((navigation) => {
		if (hasUnsavedChanges && !savedSuccessfully) {
			navigation.cancel();
			pendingNavigationUrl = navigation.to?.url?.pathname ?? '/recipes';
			showUnsavedModal = true;
		}
	});

	function handleDiscardChanges() {
		showUnsavedModal = false;
		const target = pendingNavigationUrl ?? '/recipes';
		pendingNavigationUrl = null;
		// Mark as saved to bypass the guard on the subsequent goto
		savedSuccessfully = true;
		goto(target);
	}

	function handleKeepEditing() {
		showUnsavedModal = false;
		pendingNavigationUrl = null;
	}

	// Browser close/refresh guard
	function handleBeforeUnload(e: BeforeUnloadEvent) {
		if (hasUnsavedChanges && !savedSuccessfully) {
			e.preventDefault();
		}
	}

	function handleAddMashStep() {
		const order = mashSteps.length + 1;
		const lastStep = mashSteps[mashSteps.length - 1];
		mashSteps.push({
			id: Math.random().toString(36).substring(2, 9),
			stepOrder: order,
			name: `Mash Step ${order}`,
			type: 'Temperature',
			temperatureC: lastStep ? Math.min(78, lastStep.temperatureC + 5) : 65.0,
			durationMinutes: 15
		});
	}

	function handleRemoveMashStep(id: string) {
		if (mashSteps.length <= 1) return;
		mashSteps = mashSteps.filter((s) => s.id !== id);
		mashSteps.forEach((s, idx) => {
			s.stepOrder = idx + 1;
		});
	}

	function handleMoveMashStep(index: number, direction: 'up' | 'down') {
		const targetIndex = direction === 'up' ? index - 1 : index + 1;
		if (targetIndex < 0 || targetIndex >= mashSteps.length) return;

		const current = mashSteps[index];
		mashSteps[index] = mashSteps[targetIndex];
		mashSteps[targetIndex] = current;

		mashSteps.forEach((s, idx) => {
			s.stepOrder = idx + 1;
		});
	}

	function handleApplyMashPreset(preset: 'single' | 'multi' | 'hochkurz') {
		if (preset === 'single') {
			mashSteps = [
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 1,
					name: 'Saccharification Rest',
					type: 'Infusion',
					temperatureC: 65.0,
					durationMinutes: 60
				}
			];
		} else if (preset === 'hochkurz') {
			mashSteps = [
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 1,
					name: 'Maltose Rest (Beta)',
					type: 'Infusion',
					temperatureC: 63.0,
					durationMinutes: 35
				},
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 2,
					name: 'Dextrinization Rest (Alpha)',
					type: 'Temperature',
					temperatureC: 72.0,
					durationMinutes: 30
				},
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 3,
					name: 'Mash Out',
					type: 'Temperature',
					temperatureC: 76.0,
					durationMinutes: 10
				}
			];
		} else {
			// Multi-step
			mashSteps = [
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 1,
					name: 'Protein Rest',
					type: 'Infusion',
					temperatureC: 52.0,
					durationMinutes: 20
				},
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 2,
					name: 'Saccharification Rest',
					type: 'Temperature',
					temperatureC: 65.0,
					durationMinutes: 45
				},
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 3,
					name: 'Mash Out',
					type: 'Temperature',
					temperatureC: 76.0,
					durationMinutes: 10
				}
			];
		}
	}

	function handleAddFermentationStep() {
		const order = fermentationSteps.length + 1;
		const lastStep = fermentationSteps[fermentationSteps.length - 1];
		fermentationSteps.push({
			id: Math.random().toString(36).substring(2, 9),
			stepOrder: order,
			name: `Fermentation Step ${order}`,
			type: order === 2 ? 'Secondary' : 'Conditioning',
			targetTemperatureC: lastStep ? lastStep.targetTemperatureC : 19.0,
			durationDays: 7
		});
	}

	function handleRemoveFermentationStep(id: string) {
		if (fermentationSteps.length <= 1) return;
		fermentationSteps = fermentationSteps.filter((s) => s.id !== id);
		fermentationSteps.forEach((s, idx) => {
			s.stepOrder = idx + 1;
		});
	}

	function handleMoveFermentationStep(index: number, direction: 'up' | 'down') {
		const targetIndex = direction === 'up' ? index - 1 : index + 1;
		if (targetIndex < 0 || targetIndex >= fermentationSteps.length) return;

		const current = fermentationSteps[index];
		fermentationSteps[index] = fermentationSteps[targetIndex];
		fermentationSteps[targetIndex] = current;

		fermentationSteps.forEach((s, idx) => {
			s.stepOrder = idx + 1;
		});
	}

	function handleApplyFermentationPreset(preset: 'ale' | 'lager' | 'saison' | 'neipa') {
		if (preset === 'ale') {
			fermentationSteps = [
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 1,
					name: 'Primary Fermentation',
					type: 'Primary',
					targetTemperatureC: 19.0,
					durationDays: 14
				}
			];
		} else if (preset === 'lager') {
			fermentationSteps = [
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 1,
					name: 'Primary Fermentation',
					type: 'Primary',
					targetTemperatureC: 11.0,
					durationDays: 10
				},
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 2,
					name: 'Diacetyl Rest',
					type: 'Ramp',
					targetTemperatureC: 18.0,
					durationDays: 3
				},
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 3,
					name: 'Cold Lagering',
					type: 'ColdCrash',
					targetTemperatureC: 2.0,
					durationDays: 14
				}
			];
		} else if (preset === 'saison') {
			fermentationSteps = [
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 1,
					name: 'Primary Pitch',
					type: 'Primary',
					targetTemperatureC: 20.0,
					durationDays: 3
				},
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 2,
					name: 'Free Rise Fermentation',
					type: 'FreeRise',
					targetTemperatureC: 27.0,
					durationDays: 10
				},
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 3,
					name: 'Cold Conditioning',
					type: 'ColdCrash',
					targetTemperatureC: 2.0,
					durationDays: 3
				}
			];
		} else if (preset === 'neipa') {
			fermentationSteps = [
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 1,
					name: 'Active Primary',
					type: 'Primary',
					targetTemperatureC: 19.0,
					durationDays: 7
				},
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 2,
					name: 'Dry Hop Rest',
					type: 'Secondary',
					targetTemperatureC: 16.0,
					durationDays: 4
				},
				{
					id: Math.random().toString(36).substring(2, 9),
					stepOrder: 3,
					name: 'Cold Crash',
					type: 'ColdCrash',
					targetTemperatureC: 2.0,
					durationDays: 3
				}
			];
		}
	}

	let saving = $state(false);
	let saveError = $state<string | null>(null);

	// Derived live calculation metrics
	let calculatedMetrics = $derived.by(() => {
		const calcItems: CalculatorItem[] = items
			.map((item) => {
				const ing = currentCatalogIngredients.find((i) => i.id === item.ingredientId);
				return {
					ingredient: ing!,
					amount: item.amount,
					durationMinutes: item.durationMinutes,
					usage: item.usage
				};
			})
			.filter((ci) => Boolean(ci.ingredient));

		return calculateBrewMetrics(batchSizeLiters, efficiencyPercent, boilTimeMinutes, calcItems, {
			bitternessFormula: settings.bitternessFormula,
			colorFormula: settings.colorFormula,
			abvFormula: settings.abvFormula
		});
	});

	let currentExportableRecipe = $derived.by<ExportableRecipe>(() => {
		const exportIngredients = items.map((item) => {
			const catalogMatch = currentCatalogIngredients.find((c) => c.id === item.ingredientId);
			return {
				name: catalogMatch?.name || 'Custom Ingredient',
				type: item.type,
				amount: item.amount,
				unit: item.unit,
				durationMinutes: item.durationMinutes,
				usage: item.usage,
				notes: item.notes,
				potentialGravity: catalogMatch?.potentialGravity,
				colorSrm: catalogMatch?.colorSrm,
				alphaAcidPercent: catalogMatch?.alphaAcidPercent,
				attenuationPercent: catalogMatch?.attenuationPercent,
				form: item.form ?? catalogMatch?.form ?? undefined
			};
		});

		const exportMashSteps = mashSteps.map((s) => ({
			stepOrder: s.stepOrder,
			name: s.name,
			type: s.type,
			temperatureC: s.temperatureC,
			durationMinutes: s.durationMinutes,
			rampTimeMinutes: s.rampTimeMinutes,
			infuseAmountLiters: s.infuseAmountLiters,
			notes: s.notes
		}));

		const exportFermentationSteps = fermentationSteps.map((s) => ({
			stepOrder: s.stepOrder,
			name: s.name,
			type: s.type,
			targetTemperatureC: s.targetTemperatureC,
			durationDays: s.durationDays,
			rampTimeHours: s.rampTimeHours,
			triggerGravity: s.triggerGravity,
			notes: s.notes
		}));

		return {
			name: name || 'Untitled Recipe',
			beerStyle,
			description,
			batchSizeLiters,
			boilTimeMinutes,
			efficiencyPercent,
			originalGravity: calculatedMetrics.originalGravity,
			finalGravity: calculatedMetrics.finalGravity,
			alcoholByVolume: calculatedMetrics.alcoholByVolume,
			bitternessIbu: calculatedMetrics.bitternessIbu,
			colorSrm: calculatedMetrics.colorSrm,
			brewer: auth.user?.displayName || 'BrewYou User',
			ingredients: exportIngredients,
			mashSteps: exportMashSteps,
			fermentationSteps: exportFermentationSteps
		};
	});

	function handleAddItem(type: IngredientType) {
		let defaultIng = currentCatalogIngredients.find((i) => i.type === type);
		if (!defaultIng) {
			defaultIng = currentCatalogIngredients[0];
		}
		if (!defaultIng) return;

		const isHop = type === 'Hop';
		const isYeast = type === 'Yeast';
		const defaultForm = defaultIng.form ?? (isHop ? 'Pellet' : isYeast ? 'Dry' : undefined);
		const defaultUnit =
			type === 'Fermentable' ? 'kg' : isYeast && defaultForm === 'Liquid' ? 'pkg' : 'g';
		const defaultAmount =
			type === 'Fermentable'
				? 1.0
				: isHop
					? 20
					: isYeast
						? defaultForm === 'Liquid'
							? 1
							: 11.5
						: 5;

		items.push({
			id: Math.random().toString(36).substring(2, 9),
			ingredientId: defaultIng.id,
			type,
			amount: defaultAmount,
			unit: defaultUnit,
			durationMinutes: isHop ? 15 : 60,
			usage: (type === 'Fermentable'
				? 'Mash'
				: isHop
					? 'Boil'
					: isYeast
						? 'Primary'
						: 'Boil') as IngredientUsage,
			form: defaultForm
		});
	}

	function handleRemoveItem(id: string) {
		items = items.filter((i) => i.id !== id);
	}

	async function checkImportNameConflict(importName: string): Promise<boolean> {
		if (
			mode === 'edit' &&
			initialRecipe?.name &&
			initialRecipe.name.trim().toLowerCase() === importName.trim().toLowerCase()
		) {
			return false;
		}
		return await api.recipes.checkName(importName.trim());
	}

	function handleImportRecipe(imported: ParsedRecipe, updatedCatalog?: IngredientDto[]) {
		if (updatedCatalog && updatedCatalog.length > 0) {
			const existingIds = new Set(currentCatalogIngredients.map((i) => i.id));
			const toAdd = updatedCatalog.filter((i) => !existingIds.has(i.id));
			if (toAdd.length > 0) {
				extraCreatedIngredients = [...extraCreatedIngredients, ...toAdd];
			}
		}

		name = imported.name || name;
		if (imported.description) description = imported.description;
		if (imported.beerStyle) beerStyle = imported.beerStyle;
		if (imported.batchSizeLiters > 0) batchSizeLiters = imported.batchSizeLiters;
		if (imported.boilTimeMinutes > 0) boilTimeMinutes = imported.boilTimeMinutes;
		if (imported.efficiencyPercent > 0) efficiencyPercent = imported.efficiencyPercent;

		if (imported.ingredients && imported.ingredients.length > 0) {
			const activeCatalog =
				updatedCatalog && updatedCatalog.length > 0
					? [...currentCatalogIngredients, ...updatedCatalog]
					: currentCatalogIngredients;

			const newItems: FormIngredientItem[] = [];

			for (const ing of imported.ingredients) {
				const lowerName = ing.name.trim().toLowerCase();
				// Find matching ingredient in catalog (exact match, contains, or contained by)
				let catalogMatch = activeCatalog.find(
					(c) =>
						c.name.toLowerCase() === lowerName ||
						c.name.toLowerCase().includes(lowerName) ||
						lowerName.includes(c.name.toLowerCase())
				);

				// Fallback to first ingredient of corresponding type
				if (!catalogMatch) {
					catalogMatch = activeCatalog.find((c) => c.type === ing.type);
				}

				if (catalogMatch) {
					newItems.push({
						id: Math.random().toString(36).substring(2, 9),
						ingredientId: catalogMatch.id,
						type: ing.type,
						amount: ing.amount,
						unit: ing.unit,
						durationMinutes: ing.durationMinutes,
						usage: ing.usage,
						notes: ing.notes,
						form: ing.form ?? catalogMatch.form ?? undefined
					});
				}
			}

			if (newItems.length > 0) {
				items = newItems;
			}
		}

		if (imported.mashSteps && imported.mashSteps.length > 0) {
			mashSteps = imported.mashSteps.map((s) => ({
				id: Math.random().toString(36).substring(2, 9),
				stepOrder: s.stepOrder,
				name: s.name,
				type: s.type,
				temperatureC: s.temperatureC,
				durationMinutes: s.durationMinutes,
				rampTimeMinutes: s.rampTimeMinutes,
				infuseAmountLiters: s.infuseAmountLiters,
				notes: s.notes
			}));
		}

		if (imported.fermentationSteps && imported.fermentationSteps.length > 0) {
			fermentationSteps = imported.fermentationSteps.map((s) => ({
				id: Math.random().toString(36).substring(2, 9),
				stepOrder: s.stepOrder,
				name: s.name,
				type: s.type,
				targetTemperatureC: s.targetTemperatureC,
				durationDays: s.durationDays,
				rampTimeHours: s.rampTimeHours,
				triggerGravity: s.triggerGravity,
				notes: s.notes
			}));
		}
	}

	async function handleSave() {
		if (!name.trim()) {
			saveError = t('formulator.name_required');
			return;
		}

		if (!auth.isAuthenticated) {
			goto('/login');
			return;
		}

		saving = true;
		saveError = null;

		try {
			const payload = {
				name: name.trim(),
				description: description?.trim() || null,
				beerStyle: beerStyle?.trim() || null,
				batchSizeLiters,
				boilTimeMinutes,
				efficiencyPercent,
				isPublic,
				ingredients: items.map((item) => ({
					ingredientId: item.ingredientId,
					amount: item.amount,
					unit: item.unit,
					durationMinutes: item.durationMinutes,
					usage: item.usage,
					form: item.form ?? null
				})),
				mashSteps: mashSteps.map((s, idx) => ({
					stepOrder: idx + 1,
					name: s.name.trim() || `Step ${idx + 1}`,
					type: s.type,
					temperatureC: s.temperatureC,
					durationMinutes: s.durationMinutes,
					rampTimeMinutes: s.rampTimeMinutes ?? null,
					infuseAmountLiters: s.infuseAmountLiters ?? null,
					notes: s.notes ?? null
				})),
				fermentationSteps: fermentationSteps.map((s, idx) => ({
					stepOrder: idx + 1,
					name: s.name.trim() || `Step ${idx + 1}`,
					type: s.type,
					targetTemperatureC: s.targetTemperatureC,
					durationDays: s.durationDays,
					rampTimeHours: s.rampTimeHours ?? null,
					triggerGravity: s.triggerGravity ?? null,
					notes: s.notes ?? null
				}))
			};

			if (mode === 'edit' && initialRecipe) {
				await api.recipes.update(initialRecipe.id, payload);
			} else {
				await api.recipes.create(payload);
			}

			savedSuccessfully = true;
			goto('/recipes');
		} catch (err: unknown) {
			if (err instanceof ApiClientError && (err.code === 'DUPLICATE_NAME' || err.status === 409)) {
				saveError = t('recipes.duplicate_error');
			} else {
				saveError = (err as Error).message || t('formulator.save_error');
			}
		} finally {
			saving = false;
		}
	}
</script>

<svelte:window onbeforeunload={handleBeforeUnload} />

<div class="space-y-8 pb-24 sm:pb-0">
	<!-- Title & Actions Header -->
	<div
		class="flex flex-col justify-between gap-4 border-b border-zinc-200/80 pb-6 sm:flex-row sm:items-center dark:border-white/5"
	>
		<div>
			<div class="flex items-center gap-2.5">
				<div
					class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-500"
				>
					<Sparkles class="h-5 w-5" />
				</div>
				<h1 class="text-2xl font-bold tracking-tight text-zinc-900 sm:text-3xl dark:text-white">
					{mode === 'edit' ? t('formulator.title_edit') : t('formulator.title_new')}
				</h1>
			</div>
			<p class="mt-1 text-sm text-zinc-600 dark:text-zinc-400">
				{t('formulator.subtitle')}
			</p>
		</div>

		<div class="flex flex-wrap items-center gap-3">
			<!-- Import Recipe Button -->
			<button
				type="button"
				onclick={() => (isImportModalOpen = true)}
				class="flex min-h-[44px] cursor-pointer items-center gap-2 rounded-xl border border-zinc-200/80 bg-white px-4 py-2.5 text-sm font-semibold text-zinc-700 shadow-sm transition-all hover:bg-zinc-50 active:scale-[0.98] dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-200 dark:hover:bg-zinc-800"
			>
				<UploadCloud class="h-4 w-4 text-amber-500" />
				<span>{t('formulator.import_recipe')}</span>
			</button>

			<!-- Export Recipe Button -->
			<button
				type="button"
				onclick={() => (isExportModalOpen = true)}
				class="flex min-h-[44px] cursor-pointer items-center gap-2 rounded-xl border border-zinc-200/80 bg-white px-4 py-2.5 text-sm font-semibold text-zinc-700 shadow-sm transition-all hover:bg-zinc-50 active:scale-[0.98] dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-200 dark:hover:bg-zinc-800"
			>
				<Download class="h-4 w-4 text-amber-500" />
				<span>{t('formulator.export_recipe')}</span>
			</button>

			<!-- Save/Update Button -->
			<button
				type="button"
				onclick={handleSave}
				disabled={saving}
				class="flex min-h-[44px] cursor-pointer items-center gap-2 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-5 py-2.5 text-sm font-bold text-zinc-950 shadow-md transition-all hover:from-amber-400 hover:to-amber-500 active:scale-[0.98] disabled:opacity-50"
			>
				<Save class="h-4 w-4" />
				<span>
					{saving
						? t('formulator.saving')
						: !auth.isAuthenticated
							? t('formulator.sign_in_to_save')
							: mode === 'edit'
								? t('formulator.update_recipe')
								: t('formulator.save_recipe')}
				</span>
			</button>
		</div>
	</div>

	<!-- Error Alert Banner -->
	{#if saveError}
		<div
			class="flex items-center gap-2.5 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
		>
			<AlertCircle class="h-5 w-5 flex-shrink-0" />
			<span>{saveError}</span>
		</div>
	{/if}

	<!-- Mobile Compact Metrics Strip (< md) -->
	<div
		class="sticky top-16 z-30 -mx-4 block border-b border-zinc-200/80 bg-white/95 px-4 py-2.5 shadow-md backdrop-blur md:hidden dark:border-white/10 dark:bg-zinc-950/95"
	>
		<div class="flex items-center justify-between gap-2">
			<div class="flex items-center gap-2 overflow-hidden font-mono text-xs">
				<span class="truncate"
					>OG <strong class="text-amber-600 dark:text-amber-400"
						>{settings.formatGravity(calculatedMetrics.originalGravity)}</strong
					></span
				>
				<span class="text-zinc-300 dark:text-zinc-700">•</span>
				<span class="truncate"
					>ABV <strong class="text-emerald-600 dark:text-emerald-400"
						>{formatNumber(calculatedMetrics.alcoholByVolume, 1)}%</strong
					></span
				>
				<span class="text-zinc-300 dark:text-zinc-700">•</span>
				<span class="truncate"
					>IBU <strong class="text-sky-600 dark:text-sky-400"
						>{formatNumber(calculatedMetrics.bitternessIbu, 0)}</strong
					></span
				>
			</div>
			<button
				type="button"
				onclick={() => (hudExpanded = !hudExpanded)}
				class="flex h-9 flex-shrink-0 cursor-pointer items-center gap-1 rounded-lg border border-zinc-200/80 bg-zinc-100 px-2.5 text-xs font-semibold text-zinc-700 transition-colors hover:bg-zinc-200 dark:border-zinc-800 dark:bg-zinc-800 dark:text-zinc-200 dark:hover:bg-zinc-700"
				aria-expanded={hudExpanded}
				aria-label={hudExpanded ? t('formulator.hide_details') : t('formulator.show_details')}
			>
				<span>{hudExpanded ? t('formulator.hide_details') : t('formulator.show_details')}</span>
				<ChevronDown
					class="h-3.5 w-3.5 transition-transform duration-200 {hudExpanded ? 'rotate-180' : ''}"
				/>
			</button>
		</div>

		{#if hudExpanded}
			<div
				class="mt-3 grid grid-cols-2 gap-2.5 border-t border-zinc-200/80 pt-3 dark:border-white/5"
			>
				<div
					class="rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-2.5 dark:border-zinc-800/80 dark:bg-zinc-900/60"
				>
					<span class="text-[10px] font-medium text-zinc-500 dark:text-zinc-400"
						>{t('metrics.fg')}</span
					>
					<span class="mt-0.5 block font-mono text-base font-bold text-zinc-800 dark:text-zinc-200">
						{settings.formatGravity(calculatedMetrics.finalGravity)}
					</span>
				</div>
				<div
					class="flex items-stretch justify-between rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-2.5 dark:border-zinc-800/80 dark:bg-zinc-900/60"
				>
					<div class="flex flex-col justify-between">
						<span class="text-[10px] font-medium text-zinc-500 dark:text-zinc-400"
							>{t('metrics.color', { unit: settings.colorShort })}</span
						>
						<span class="mt-0.5 block font-mono text-base font-bold text-zinc-900 dark:text-white">
							{formatNumber(settings.toDisplayColor(calculatedMetrics.colorSrm), 1)}
						</span>
					</div>
					<div class="flex items-center justify-end">
						<BeerGlass srm={calculatedMetrics.colorSrm} size="full" class="h-full shrink-0" />
					</div>
				</div>
			</div>
		{/if}
	</div>

	<!-- Desktop / Tablet Full HUD (>= md) -->
	<div
		class="glass-panel sticky top-20 z-40 hidden grid-cols-5 gap-3 rounded-2xl border border-zinc-200/80 p-4 shadow-xl backdrop-blur md:grid dark:border-white/[0.08]"
	>
		<!-- OG -->
		<div
			class="flex flex-col rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-3 dark:border-zinc-800/80 dark:bg-zinc-900/60"
		>
			<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400"
				>{settings.gravityUnit === 'Plato' ? t('metrics.plato') : t('metrics.og')}</span
			>
			<span class="mt-1 font-mono text-xl font-bold text-amber-600 lg:text-2xl dark:text-amber-400">
				{settings.formatGravity(calculatedMetrics.originalGravity)}
			</span>
			<span class="mt-0.5 text-[10px] text-zinc-400 dark:text-zinc-500">{t('metrics.og_desc')}</span
			>
		</div>

		<!-- FG -->
		<div
			class="flex flex-col rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-3 dark:border-zinc-800/80 dark:bg-zinc-900/60"
		>
			<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400"
				>{settings.gravityUnit === 'Plato' ? t('metrics.fg_plato') : t('metrics.fg')}</span
			>
			<span class="mt-1 font-mono text-xl font-bold text-zinc-800 lg:text-2xl dark:text-zinc-200">
				{settings.formatGravity(calculatedMetrics.finalGravity)}
			</span>
			<span class="mt-0.5 text-[10px] text-zinc-400 dark:text-zinc-500">{t('metrics.fg_desc')}</span
			>
		</div>

		<!-- ABV -->
		<div
			class="flex flex-col rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-3 dark:border-zinc-800/80 dark:bg-zinc-900/60"
		>
			<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400">{t('metrics.abv')}</span>
			<span
				class="mt-1 font-mono text-xl font-bold text-emerald-600 lg:text-2xl dark:text-emerald-400"
			>
				{formatNumber(calculatedMetrics.alcoholByVolume, 2)}%
			</span>
			<span class="mt-0.5 text-[10px] text-zinc-400 dark:text-zinc-500"
				>{t('metrics.abv_desc')}</span
			>
		</div>

		<!-- IBU -->
		<div
			class="flex flex-col rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-3 dark:border-zinc-800/80 dark:bg-zinc-900/60"
		>
			<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400">{t('metrics.ibu')}</span>
			<span class="mt-1 font-mono text-xl font-bold text-sky-600 lg:text-2xl dark:text-sky-400">
				{formatNumber(calculatedMetrics.bitternessIbu, 1)}
			</span>
			<span class="mt-0.5 text-[10px] text-zinc-400 dark:text-zinc-500"
				>{t('metrics.ibu_desc')}</span
			>
		</div>

		<!-- SRM Color -->
		<div
			class="flex items-stretch justify-between rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-3 dark:border-zinc-800/80 dark:bg-zinc-900/60"
		>
			<div class="flex flex-col justify-between">
				<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400"
					>{t('metrics.color', { unit: settings.colorShort })}</span
				>
				<span class="mt-1 font-mono text-xl font-bold text-zinc-900 lg:text-2xl dark:text-white">
					{formatNumber(settings.toDisplayColor(calculatedMetrics.colorSrm), 1)}
				</span>
				<span class="mt-0.5 text-[10px] text-zinc-400 dark:text-zinc-500"
					>{t('metrics.color_desc')}</span
				>
			</div>
			<div class="flex items-center justify-end">
				<BeerGlass srm={calculatedMetrics.colorSrm} size="full" class="h-full shrink-0" />
			</div>
		</div>
	</div>

	<!-- Recipe Overview & Brewhouse Calibration Grid -->
	<div class="grid grid-cols-1 gap-6 lg:grid-cols-3">
		<!-- Left: Recipe Overview (2 cols) -->
		<div
			class="glass-panel space-y-4 rounded-2xl border border-zinc-200/80 p-6 lg:col-span-2 dark:border-white/[0.08]"
		>
			<h2 class="flex items-center gap-2 text-base font-bold text-zinc-900 dark:text-white">
				<Info class="h-4 w-4 text-amber-500" />
				{t('formulator.recipe_overview')}
			</h2>

			<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
				<div>
					<label
						for="recipe-name"
						class="mb-1.5 block text-xs font-semibold text-zinc-600 dark:text-zinc-400"
						>{t('formulator.recipe_name')} <span class="font-bold text-amber-500">*</span></label
					>
					<input
						id="recipe-name"
						type="text"
						bind:value={name}
						placeholder={t('formulator.name_placeholder')}
						required
						class="h-11 w-full rounded-xl border border-zinc-200/80 bg-white px-3.5 py-2.5 text-base text-zinc-900 transition-colors focus:border-amber-500/60 focus:ring-2 focus:ring-amber-500/20 focus:outline-none sm:text-sm dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
					/>
				</div>

				<div>
					<label
						for="beer-style"
						class="mb-1.5 block text-xs font-semibold text-zinc-600 dark:text-zinc-400"
						>{t('formulator.beer_style')}</label
					>
					<input
						id="beer-style"
						type="text"
						bind:value={beerStyle}
						placeholder={t('formulator.beer_style_placeholder')}
						class="h-11 w-full rounded-xl border border-zinc-200/80 bg-white px-3.5 py-2.5 text-base text-zinc-900 transition-colors focus:border-amber-500/60 focus:ring-2 focus:ring-amber-500/20 focus:outline-none sm:text-sm dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
					/>
				</div>

				<div class="sm:col-span-2">
					<label
						for="recipe-description"
						class="mb-1.5 block text-xs font-semibold text-zinc-600 dark:text-zinc-400"
						>{t('formulator.description')}</label
					>
					<textarea
						id="recipe-description"
						bind:value={description}
						rows="2"
						class="w-full resize-none rounded-xl border border-zinc-200/80 bg-white px-3.5 py-2.5 text-base text-zinc-900 transition-colors focus:border-amber-500/60 focus:ring-2 focus:ring-amber-500/20 focus:outline-none sm:text-sm dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
					></textarea>
				</div>

				<!-- Public Visibility Checkbox -->
				<div class="flex items-center gap-3 pt-1 sm:col-span-2">
					<input
						id="recipe-is-public"
						type="checkbox"
						bind:checked={isPublic}
						class="h-4 w-4 rounded border-zinc-300 text-amber-600 focus:ring-amber-500 dark:border-zinc-700 dark:bg-zinc-900"
					/>
					<label
						for="recipe-is-public"
						class="cursor-pointer text-xs font-medium text-zinc-700 dark:text-zinc-300"
					>
						<span class="font-semibold text-zinc-900 dark:text-white"
							>{t('formulator.is_public')}</span
						>
						—
						<span class="text-zinc-500 dark:text-zinc-400">{t('formulator.is_public_desc')}</span>
					</label>
				</div>
			</div>
		</div>

		<!-- Right: Brewhouse Calibration (1 col) -->
		<div
			class="glass-panel space-y-4 rounded-2xl border border-zinc-200/80 p-6 dark:border-white/[0.08]"
		>
			<h2 class="flex items-center gap-2 text-base font-bold text-zinc-900 dark:text-white">
				<Sliders class="h-4 w-4 text-amber-500" />
				{t('formulator.brewhouse_specs')}
			</h2>

			<div class="space-y-4">
				<!-- Batch Volume -->
				<div>
					<div
						class="mb-1.5 flex items-center justify-between text-xs font-semibold text-zinc-600 dark:text-zinc-400"
					>
						<div class="flex items-center gap-1.5">
							<span>{t('formulator.batch_size')}</span>
							<span class="text-[10px] font-normal text-zinc-400 dark:text-zinc-500"
								>({t('formulator.batch_size_standard_hint')})</span
							>
						</div>
						<span class="font-mono text-zinc-900 dark:text-white"
							>{settings.formatVolume(batchSizeLiters)}</span
						>
					</div>
					<input
						type="range"
						min="5"
						max="100"
						step="1"
						bind:value={batchSizeLiters}
						class="w-full cursor-pointer accent-amber-500"
					/>
				</div>

				<!-- Boil Duration -->
				<div>
					<div
						class="mb-1.5 flex items-center justify-between text-xs font-semibold text-zinc-600 dark:text-zinc-400"
					>
						<span>{t('formulator.boil_duration')}</span>
						<span class="font-mono text-zinc-900 dark:text-white"
							>{boilTimeMinutes} {t('common.minutes')}</span
						>
					</div>
					<input
						type="range"
						min="30"
						max="120"
						step="5"
						bind:value={boilTimeMinutes}
						class="w-full cursor-pointer accent-amber-500"
					/>
				</div>

				<!-- Efficiency -->
				<div>
					<div
						class="mb-1.5 flex items-center justify-between text-xs font-semibold text-zinc-600 dark:text-zinc-400"
					>
						<span>{t('formulator.efficiency')}</span>
						<span class="font-mono text-zinc-900 dark:text-white">{efficiencyPercent}%</span>
					</div>
					<input
						type="range"
						min="50"
						max="90"
						step="1"
						bind:value={efficiencyPercent}
						class="w-full cursor-pointer accent-amber-500"
					/>
				</div>
			</div>
		</div>
	</div>

	<!-- Mash Profile & Steps Card -->
	<MashStepsCard
		bind:steps={mashSteps}
		onAddStep={handleAddMashStep}
		onRemoveStep={handleRemoveMashStep}
		onMoveStep={handleMoveMashStep}
		onApplyPreset={handleApplyMashPreset}
	/>

	<!-- Fermentation Profile & Steps Card -->
	<FermentationStepsCard
		bind:steps={fermentationSteps}
		onAddStep={handleAddFermentationStep}
		onRemoveStep={handleRemoveFermentationStep}
		onMoveStep={handleMoveFermentationStep}
		onApplyPreset={handleApplyFermentationPreset}
	/>

	<!-- Full-Width Ingredients Bill Card -->
	<IngredientsCard
		bind:items
		catalogIngredients={currentCatalogIngredients}
		{batchSizeLiters}
		{efficiencyPercent}
		onAddItem={handleAddItem}
		onRemoveItem={handleRemoveItem}
	/>

	<!-- Mobile Sticky Save Bar (< sm) -->
	<div
		class="fixed right-0 bottom-0 left-0 z-40 border-t border-zinc-200/80 bg-white/95 p-3.5 pb-[calc(0.875rem+env(safe-area-inset-bottom,0px))] shadow-2xl backdrop-blur-md sm:hidden dark:border-white/10 dark:bg-zinc-950/95"
	>
		<button
			type="button"
			onclick={handleSave}
			disabled={saving}
			class="flex min-h-[48px] w-full cursor-pointer items-center justify-center gap-2 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 py-3 text-sm font-bold text-zinc-950 shadow-md transition-all hover:from-amber-400 hover:to-amber-500 active:scale-[0.98] disabled:opacity-50"
		>
			<Save class="h-4 w-4" />
			<span>
				{saving
					? t('formulator.saving')
					: !auth.isAuthenticated
						? t('formulator.sign_in_to_save')
						: mode === 'edit'
							? t('formulator.update_recipe')
							: t('formulator.save_recipe')}
			</span>
		</button>
	</div>
</div>

<!-- Recipe Import Modal -->
<RecipeImportModal
	open={isImportModalOpen}
	catalogIngredients={currentCatalogIngredients}
	checkNameConflictFn={checkImportNameConflict}
	onClose={() => (isImportModalOpen = false)}
	onImportRecipe={handleImportRecipe}
	onIngredientsCreated={handleIngredientsCreated}
/>

<!-- Recipe Export Modal -->
<RecipeExportModal
	open={isExportModalOpen}
	recipe={currentExportableRecipe}
	onClose={() => (isExportModalOpen = false)}
/>

<!-- Unsaved Changes Warning Modal -->
<ConfirmModal
	open={showUnsavedModal}
	title={t('formulator.unsaved_changes.title')}
	message={t('formulator.unsaved_changes.message')}
	confirmText={t('formulator.unsaved_changes.discard')}
	cancelText={t('formulator.unsaved_changes.keep_editing')}
	variant="warning"
	confirmTestId="unsaved-changes-discard-btn"
	cancelTestId="unsaved-changes-keep-editing-btn"
	onConfirm={handleDiscardChanges}
	onClose={handleKeepEditing}
/>
