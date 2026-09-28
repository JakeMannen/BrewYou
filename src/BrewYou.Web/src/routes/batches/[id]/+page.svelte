<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { api, ApiClientError } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import { formatNumber } from '$lib/utils/formatNumber';
	import type {
		BatchDetailDto,
		BrewStage,
		BatchStatus,
		AdvanceBatchStageRequest,
		AddBatchReadingRequest,
		UpdateBatchRequest,
		EquipmentDto,
		EquipmentSubtype,
		BatchIngredientDto,
		BatchMashStepDto,
		BatchFermentationStepDto,
		BatchEquipmentReadingDto,
		LogBatchTemperatureRequest
	} from '$lib/types/api';
	import SrmSwatch from '$lib/components/brewery/SrmSwatch.svelte';
	import BrewStageTracker, {
		type BrewStage as UiBrewStage
	} from '$lib/components/brewery/BrewStageTracker.svelte';
	import AttenuationChart from '$lib/components/batches/AttenuationChart.svelte';
	import StageMeasurementsCard from '$lib/components/batches/StageMeasurementsCard.svelte';
	import StepTemperatureChart from '$lib/components/batches/StepTemperatureChart.svelte';
	import LogStepTemperatureModal from '$lib/components/batches/LogStepTemperatureModal.svelte';
	import DeleteBatchModal from '$lib/components/batches/DeleteBatchModal.svelte';
	import BrewStationHudModal from '$lib/components/batches/BrewStationHudModal.svelte';
	import { sortIngredientsChronologically } from '$lib/utils/ingredient-order';
	import { calculateActualAbv } from '$lib/calculators/brewing';
	import {
		ArrowRight,
		CheckCircle2,
		Flame,
		Play,
		Pause,
		RotateCcw,
		Trash2,
		AlertTriangle,
		PackageCheck,
		Loader2,
		X,
		Filter,
		Timer,
		Thermometer,
		StepForward,
		Clock,
		Activity,
		Maximize2
	} from '@lucide/svelte';

	let batch = $state<BatchDetailDto | null>(null);

	function recalculateBatchAbv() {
		if (!batch || batch.currentGravity === null || batch.currentGravity === undefined) return;
		const og = batch.measuredOg ?? batch.targetOg;
		if (og && og > batch.currentGravity) {
			const calculated = calculateActualAbv(og, batch.currentGravity, settings.abvFormula);
			batch.alcoholByVolume = Number(calculated.toFixed(2));
		}
	}

	let currentAbv = $derived.by<number | null>(() => {
		if (!batch) return null;
		if (batch.currentGravity) {
			const og = batch.measuredOg ?? batch.targetOg;
			if (og && og > batch.currentGravity) {
				return Number(calculateActualAbv(og, batch.currentGravity, settings.abvFormula).toFixed(2));
			}
		}
		return batch.alcoholByVolume ?? null;
	});
	let equipmentList = $state<EquipmentDto[]>([]);
	let batchEquipmentReadings = $state<BatchEquipmentReadingDto[]>([]);
	let showLogTempModal = $state(false);
	let showDeleteBatchModal = $state(false);
	let showBrewStationHud = $state(false);
	let logTempStage = $state<BrewStage>('Mash');
	let logTempMashStepId = $state<string | null>(null);
	let selectedMashStepGraphId = $state<string | null>(null);
	let loading = $state(true);
	let error = $state<string | null>(null);

	function openLogTempModal(targetStage: BrewStage, mashStepId?: string | null) {
		logTempStage = targetStage;
		const resolvedStepId =
			mashStepId ??
			(targetStage === 'Mash'
				? (selectedMashStepGraphId ?? currentSelectedMashStep?.id ?? null)
				: null);
		logTempMashStepId = resolvedStepId;
		if (resolvedStepId && batch?.mashSteps) {
			selectedMashStepGraphId = resolvedStepId;
			const stepIdx = batch.mashSteps.findIndex((s) => s.id === resolvedStepId);
			if (stepIdx !== -1) {
				activeMashStepIndex = stepIdx;
			}
		}
		showLogTempModal = true;
	}

	async function handleLogStepTemperature(req: LogBatchTemperatureRequest) {
		if (!batch) return;
		const saved = await api.batches.logEquipmentReading(batch.id, req);
		if (!batchEquipmentReadings.some((r) => r.id === saved.id)) {
			batchEquipmentReadings = [...batchEquipmentReadings, saved];
		}

		// If a mash step was updated, reflect actual temperature in batch.mashSteps
		if (saved.batchMashStepId && batch.mashSteps) {
			const idx = batch.mashSteps.findIndex((s) => s.id === saved.batchMashStepId);
			if (idx !== -1) {
				batch.mashSteps[idx].actualTemperatureC = saved.temperatureC;
			}
		}
	}

	let filterCurrentStageOnly = $state(true);

	// Inspected Stage State (Decoupled from batch.currentStage)
	let inspectedStage = $state<BrewStage>('Mash');
	let isInspectingActiveStage = $derived(batch ? inspectedStage === batch.currentStage : true);
	let canLogInspectedStage = $derived(
		isInspectingActiveStage && batch?.status !== 'Completed' && batch?.status !== 'Archived'
	);
	let inspectedStageStatus = $derived.by<'completed' | 'in_progress' | 'pending'>(() => {
		if (!batch) return 'pending';
		if (batch.status === 'Completed') return 'completed';
		const stageOrder: BrewStage[] = ['Mash', 'Boil', 'Ferment', 'Condition', 'Package'];
		const currentIndex = stageOrder.indexOf(batch.currentStage);
		const targetIndex = stageOrder.indexOf(inspectedStage);
		if (targetIndex < currentIndex) return 'completed';
		if (targetIndex === currentIndex) return 'in_progress';
		return 'pending';
	});

	async function handleSaveStageMeasurements(req: UpdateBatchRequest) {
		if (!batch) return;
		const updated = await api.batches.update(batch.id, req);
		batch = updated;
		recalculateBatchAbv();
	}

	// Advance Stage Modal
	let showAdvanceModal = $state(false);
	let advancing = $state(false);
	let advanceError = $state<string | null>(null);
	let nextStage = $state<BrewStage>('Boil');
	let advanceMeasuredOg = $state<number | null>(null);
	let advanceMeasuredFg = $state<number | null>(null);
	let advanceVolume = $state<number | null>(null);
	let advancePitchTemp = $state<number | null>(null);
	let advancePackagingVesselId = $state<string>('');
	let advanceNotes = $state('');
	let advancePreBoilVolume = $state<number | null>(null);
	let advancePreBoilGravity = $state<number | null>(null);
	let advancePostBoilVolume = $state<number | null>(null);
	let advancePackagedVolume = $state<number | null>(null);

	// Add Reading Modal
	let showReadingModal = $state(false);
	let savingReading = $state(false);
	let readingError = $state<string | null>(null);
	let newReadingSg = $state(1.02);
	let newReadingNotes = $state('');

	let activeFermentorSensor = $derived.by(() => {
		if (!batch?.sensorAssignments || batch.sensorAssignments.length === 0) return null;
		const assignment = batch.sensorAssignments.find(
			(sa) => sa.stage === inspectedStage || sa.stage === null
		);
		if (!assignment) return null;
		return equipmentList.find((e) => e.id === assignment.equipmentId) ?? null;
	});

	let fermentorSensorName = $derived(activeFermentorSensor?.name ?? batch?.fermenterName ?? null);
	let fermentorSensorSubtype = $derived(activeFermentorSensor?.subtype ?? null);

	let currentStageEquipment = $derived.by(() => {
		const currentBatch = batch;
		if (!currentBatch) return null;
		// 1. Step-level sensor assignment (Mash step)
		if (logTempStage === 'Mash' && logTempMashStepId && currentBatch.sensorAssignments?.length) {
			const stepAssignment = currentBatch.sensorAssignments.find(
				(sa) => sa.batchMashStepId === logTempMashStepId
			);
			if (stepAssignment) {
				const eq = equipmentList.find((e) => e.id === stepAssignment.equipmentId);
				return {
					id: stepAssignment.equipmentId,
					name: eq?.name ?? stepAssignment.equipmentName
				};
			}
		}

		// 2. Stage-level sensor assignment
		if (currentBatch.sensorAssignments?.length) {
			const stageAssignment = currentBatch.sensorAssignments.find(
				(sa) => sa.stage === logTempStage
			);
			if (stageAssignment) {
				const eq = equipmentList.find((e) => e.id === stageAssignment.equipmentId);
				return {
					id: stageAssignment.equipmentId,
					name: eq?.name ?? stageAssignment.equipmentName
				};
			}
		}

		// 3. Stage vessel from batch profile
		if (logTempStage === 'Mash' || logTempStage === 'Boil') {
			if (currentBatch.boilerId) {
				const eq = equipmentList.find((e) => e.id === currentBatch.boilerId);
				return {
					id: currentBatch.boilerId,
					name: eq?.name ?? currentBatch.boilerName ?? 'Boiler'
				};
			}
		} else if (logTempStage === 'Ferment' || logTempStage === 'Condition') {
			if (currentBatch.fermenterId) {
				const eq = equipmentList.find((e) => e.id === currentBatch.fermenterId);
				return {
					id: currentBatch.fermenterId,
					name: eq?.name ?? currentBatch.fermenterName ?? 'Fermenter'
				};
			}
		} else if (logTempStage === 'Package') {
			if (currentBatch.packagingVesselId) {
				const eq = equipmentList.find((e) => e.id === currentBatch.packagingVesselId);
				return {
					id: currentBatch.packagingVesselId,
					name: eq?.name ?? currentBatch.packagingVesselName ?? 'Packaging'
				};
			}
		}

		// 4. Global sensor assignment (stage === null)
		if (currentBatch.sensorAssignments?.length) {
			const globalAssignment = currentBatch.sensorAssignments.find((sa) => sa.stage === null);
			if (globalAssignment) {
				const eq = equipmentList.find((e) => e.id === globalAssignment.equipmentId);
				return {
					id: globalAssignment.equipmentId,
					name: eq?.name ?? globalAssignment.equipmentName
				};
			}
		}

		// 5. Fallback: first equipment in list if any
		if (equipmentList.length > 0) {
			return {
				id: equipmentList[0].id,
				name: equipmentList[0].name
			};
		}

		return null;
	});

	let currentStageEquipmentId = $derived(currentStageEquipment?.id ?? null);
	let currentStageEquipmentName = $derived(currentStageEquipment?.name ?? null);

	// Boil Timer
	let timerRunning = $state(false);
	let timerRemainingSeconds = $state(3600); // 60 mins default
	let timerInterval = $state<ReturnType<typeof setInterval> | null>(null);

	const batchId = $derived(page.params.id);

	let streamAbortController: AbortController | null = null;
	let isTelemetryLive = $state(false);
	let fallbackPollTimer: ReturnType<typeof setInterval> | null = null;

	function handleNewTelemetryReading(reading: BatchEquipmentReadingDto) {
		// Deduplicate: only append if reading ID is not already present
		if (!batchEquipmentReadings.some((r) => r.id === reading.id)) {
			batchEquipmentReadings.push(reading);
		}

		// If reading carries specificGravity, update batch metrics and batch.readings
		if (reading.specificGravity !== null && reading.specificGravity !== undefined && batch) {
			batch.currentGravity = reading.specificGravity;
			recalculateBatchAbv();
			if (!batch.readings.some((r) => r.id === reading.id)) {
				batch.readings = [
					...batch.readings,
					{
						id: reading.id,
						batchId: batch.id,
						timestamp: reading.timestamp,
						specificGravity: reading.specificGravity,
						temperatureC: reading.temperatureC,
						notes: reading.notes ?? reading.equipmentName,
						createdAt: reading.timestamp,
						alcoholByVolume: batch.alcoholByVolume
					}
				];
			}
		}

		// If this reading belongs to a mash step, update actual temperature in batch.mashSteps
		if (reading.batchMashStepId && batch?.mashSteps) {
			const step = batch.mashSteps.find((s) => s.id === reading.batchMashStepId);
			if (step) {
				step.actualTemperatureC = reading.temperatureC;
			}
		}
	}

	async function startLiveTelemetryStream() {
		if (!batch || batch.status === 'Completed' || batch.status === 'Archived') {
			isTelemetryLive = false;
			return;
		}

		stopLiveTelemetryStream();
		streamAbortController = new AbortController();
		isTelemetryLive = true;

		try {
			await api.batches.streamEquipmentReadings(batch.id, {
				onReading: handleNewTelemetryReading,
				onError: () => {
					startFallbackPolling();
				},
				signal: streamAbortController.signal
			});
		} catch {
			startFallbackPolling();
		}
	}

	function stopLiveTelemetryStream() {
		if (streamAbortController) {
			streamAbortController.abort();
			streamAbortController = null;
		}
		stopFallbackPolling();
		isTelemetryLive = false;
	}

	function startFallbackPolling() {
		if (fallbackPollTimer || !batch || batch.status === 'Completed' || batch.status === 'Archived')
			return;
		isTelemetryLive = true;
		fallbackPollTimer = setInterval(async () => {
			if (document.visibilityState === 'hidden' || !batch) return;
			try {
				const latestReadings = await api.batches.getEquipmentReadings(batch.id);
				for (const r of latestReadings) {
					handleNewTelemetryReading(r);
				}
			} catch (pollErr) {
				console.error('Fallback telemetry poll error', pollErr);
			}
		}, 5000);
	}

	function stopFallbackPolling() {
		if (fallbackPollTimer) {
			clearInterval(fallbackPollTimer);
			fallbackPollTimer = null;
		}
	}

	async function handleVisibilityChange() {
		if (document.visibilityState === 'visible') {
			if (batch && batch.status !== 'Completed' && batch.status !== 'Archived') {
				try {
					const latest = await api.batches.getEquipmentReadings(batch.id);
					for (const r of latest) {
						handleNewTelemetryReading(r);
					}
				} catch {
					// Transient gap recovery error ignored
				}
				startLiveTelemetryStream();
			}
		} else {
			stopLiveTelemetryStream();
		}
	}

	onMount(async () => {
		await loadBatch();
		if (batch && batch.status !== 'Completed' && batch.status !== 'Archived') {
			startLiveTelemetryStream();
		}
		if (typeof document !== 'undefined') {
			document.addEventListener('visibilitychange', handleVisibilityChange);
		}
	});

	onDestroy(() => {
		stopLiveTelemetryStream();
		if (typeof document !== 'undefined') {
			document.removeEventListener('visibilitychange', handleVisibilityChange);
		}
	});

	async function loadBatch() {
		if (!batchId) {
			error = t('batches.errors.invalid_id');
			loading = false;
			return;
		}
		loading = true;
		error = null;
		try {
			const [batchData, eq, readings] = await Promise.all([
				api.batches.getById(batchId),
				api.equipment.list().catch(() => []),
				api.batches.getEquipmentReadings(batchId).catch(() => [])
			]);
			batch = batchData;
			equipmentList = eq;
			batchEquipmentReadings = readings;
			inspectedStage = batch.currentStage;

			if (batch.mashSteps && batch.mashSteps.length > 0) {
				const activeIdx = batch.mashSteps.findIndex((s) => !s.isCompleted);
				activeMashStepIndex = activeIdx !== -1 ? activeIdx : 0;
				selectedMashStepGraphId = batch.mashSteps[activeMashStepIndex]?.id ?? null;
			}

			if (!timerRunning) {
				if (batch.currentStage === 'Boil' && batch.boilTimeMinutes) {
					timerRemainingSeconds = batch.boilTimeMinutes * 60;
				} else if (batch.currentStage === 'Mash') {
					const activeStep = batch.mashSteps?.[activeMashStepIndex] ?? batch.mashSteps?.[0];
					timerRemainingSeconds = (activeStep?.durationMinutes ?? 60) * 60;
				} else {
					timerRemainingSeconds = 3600;
				}
			}
			determineNextStage(batch.currentStage);
		} catch (err: unknown) {
			error = (err as Error).message || t('batches.errors.load_failed');
		} finally {
			loading = false;
		}
	}

	function determineNextStage(current: BrewStage) {
		switch (current) {
			case 'Mash':
				nextStage = 'Boil';
				break;
			case 'Boil':
				nextStage = 'Ferment';
				break;
			case 'Ferment':
				nextStage = 'Condition';
				break;
			case 'Condition':
				nextStage = 'Package';
				break;
			case 'Package':
				nextStage = 'Package';
				break;
		}
	}

	// Dynamic UI Stages for BrewStageTracker
	let uiStages = $derived.by<UiBrewStage[]>(() => {
		if (!batch) return [];
		const stageOrder: BrewStage[] = ['Mash', 'Boil', 'Ferment', 'Condition', 'Package'];
		const currentIndex = stageOrder.indexOf(batch.currentStage);

		const boiler = equipmentList.find((e) => e.id === batch!.boilerId);
		const fermenter = equipmentList.find((e) => e.id === batch!.fermenterId);
		const pkgVessel = equipmentList.find((e) => e.id === batch!.packagingVesselId);

		return stageOrder.map((s, idx) => {
			let status: 'completed' | 'in_progress' | 'pending' = 'pending';
			if (batch!.status === 'Completed') {
				status = 'completed';
			} else if (idx < currentIndex) {
				status = 'completed';
			} else if (idx === currentIndex) {
				status = 'in_progress';
			}

			const hist = batch!.stageHistory.find((h) => h.stage === s);
			let timestamp: string | undefined = undefined;
			if (s === 'Ferment' && batch!.daysActive !== undefined) {
				timestamp = `${batch!.daysActive}d`;
			} else if (hist?.durationMinutes) {
				timestamp = `${hist.durationMinutes}m`;
			}

			let equipmentSubtype: EquipmentSubtype | string | undefined = undefined;
			let fillPercent: number | undefined = undefined;
			let equipmentName: string | undefined = undefined;

			if (s === 'Mash') {
				equipmentSubtype = boiler?.subtype ?? 'AllInOne';
				equipmentName = batch!.boilerName ?? boiler?.name;
				const mashVol =
					batch!.volumeProfile?.strikeWaterLiters ??
					batch!.volumeProfile?.targetPreBoilVolumeLiters ??
					batch!.targetBatchSizeLiters;
				if (boiler && boiler.capacityLiters > 0) {
					fillPercent = Math.min(100, Math.round((mashVol / boiler.capacityLiters) * 100));
				} else {
					fillPercent = 75;
				}
			} else if (s === 'Boil') {
				equipmentSubtype = boiler?.subtype ?? 'Pan';
				equipmentName = batch!.boilerName ?? boiler?.name;
				const boilVol =
					batch!.volumeProfile?.measuredPreBoilVolumeLiters ??
					batch!.volumeProfile?.targetPreBoilVolumeLiters ??
					batch!.targetBatchSizeLiters * 1.15;
				if (boiler && boiler.capacityLiters > 0) {
					fillPercent = Math.min(100, Math.round((boilVol / boiler.capacityLiters) * 100));
				} else {
					fillPercent = 85;
				}
			} else if (s === 'Ferment') {
				equipmentSubtype = fermenter?.subtype ?? 'ConicalFermenter';
				equipmentName = batch!.fermenterName ?? fermenter?.name;
				const fermVol =
					batch!.volumeProfile?.measuredFermenterVolumeLiters ??
					batch!.volumeProfile?.targetFermenterVolumeLiters ??
					batch!.targetBatchSizeLiters;
				if (fermenter && fermenter.capacityLiters > 0) {
					fillPercent = Math.min(100, Math.round((fermVol / fermenter.capacityLiters) * 100));
				} else {
					fillPercent = 80;
				}
			} else if (s === 'Condition') {
				equipmentSubtype = fermenter?.subtype ?? 'Carboy';
				equipmentName = batch!.fermenterName ?? fermenter?.name;
				const condVol =
					batch!.volumeProfile?.measuredFermenterVolumeLiters ??
					batch!.volumeProfile?.targetFermenterVolumeLiters ??
					batch!.targetBatchSizeLiters;
				if (fermenter && fermenter.capacityLiters > 0) {
					fillPercent = Math.min(100, Math.round((condVol / fermenter.capacityLiters) * 100));
				} else {
					fillPercent = 80;
				}
			} else if (s === 'Package') {
				equipmentSubtype = pkgVessel?.subtype ?? 'Bottle';
				equipmentName = batch!.packagingVesselName ?? pkgVessel?.name;
				const pkgVol =
					batch!.volumeProfile?.measuredPackagedVolumeLiters ??
					batch!.volumeProfile?.targetPackagedVolumeLiters ??
					batch!.measuredBatchSizeLiters ??
					batch!.targetBatchSizeLiters;
				if (pkgVessel && pkgVessel.capacityLiters > 0) {
					fillPercent = Math.min(100, Math.round((pkgVol / pkgVessel.capacityLiters) * 100));
				} else {
					fillPercent = 90;
				}
			}

			return {
				id: s.toLowerCase(),
				label: t(`batches.stages.${s.toLowerCase()}`),
				status,
				timestamp,
				equipmentSubtype,
				fillPercent,
				equipmentName
			};
		});
	});

	function getStatusBadge(status: BatchStatus) {
		switch (status) {
			case 'Brewing':
				return {
					class: 'border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400',
					dot: 'bg-amber-500 animate-pulse',
					label: t('batches.status_brewing')
				};
			case 'Fermenting':
				return {
					class: 'border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400',
					dot: 'bg-emerald-500 animate-ping',
					label: t('batches.status_fermenting')
				};
			case 'Conditioning':
				return {
					class: 'border-sky-500/30 bg-sky-500/10 text-sky-600 dark:text-sky-400',
					dot: 'bg-sky-500',
					label: t('batches.status_conditioning')
				};
			case 'Completed':
				return {
					class: 'border-zinc-500/30 bg-zinc-500/10 text-zinc-600 dark:text-zinc-400',
					dot: 'bg-zinc-400',
					label: t('batches.status_completed')
				};
			default:
				return {
					class: 'border-zinc-500/30 bg-zinc-500/10 text-zinc-600 dark:text-zinc-400',
					dot: 'bg-zinc-400',
					label: status
				};
		}
	}

	async function handleToggleIngredient(ingredientId: string, current: boolean) {
		if (!batch) return;
		try {
			await api.batches.toggleIngredient(batch.id, ingredientId, !current);
			const idx = batch.ingredients.findIndex((i) => i.id === ingredientId);
			if (idx !== -1) {
				batch.ingredients[idx].isChecked = !current;
			}
		} catch (err: unknown) {
			console.error('Failed to toggle ingredient', err);
		}
	}

	function getIngredientsForStage(
		stage: BrewStage,
		ingredients: BatchIngredientDto[]
	): BatchIngredientDto[] {
		let filtered: BatchIngredientDto[];
		switch (stage) {
			case 'Mash':
				filtered = ingredients.filter(
					(i) =>
						i.additionStage === 'Mash' ||
						(i.type === 'Fermentable' &&
							i.additionStage !== 'Boil' &&
							i.additionStage !== 'Bottling')
				);
				break;
			case 'Boil':
				filtered = ingredients.filter(
					(i) =>
						i.additionStage === 'Boil' ||
						i.additionStage === 'Whirlpool' ||
						(i.type === 'Hop' && i.additionStage !== 'DryHop' && i.additionStage !== 'Mash')
				);
				break;
			case 'Ferment':
				filtered = ingredients.filter(
					(i) => i.additionStage === 'Primary' || i.additionStage === 'DryHop' || i.type === 'Yeast'
				);
				break;
			case 'Condition':
				filtered = ingredients.filter(
					(i) =>
						i.additionStage === 'Secondary' ||
						(i.additionStage === 'DryHop' && (i.additionTimeMinutes ?? 0) <= 3)
				);
				break;
			case 'Package':
				filtered = ingredients.filter((i) => i.additionStage === 'Bottling');
				break;
			default:
				filtered = ingredients;
				break;
		}
		return sortIngredientsChronologically(filtered);
	}

	function getStageChecklistTitle(stage: BrewStage): string {
		switch (stage) {
			case 'Mash':
				return t('batches.workspace.mash_checklist_title');
			case 'Boil':
				return t('batches.workspace.boil_checklist_title');
			case 'Ferment':
				return t('batches.workspace.ferment_checklist_title');
			case 'Condition':
				return t('batches.workspace.condition_checklist_title');
			case 'Package':
				return t('batches.workspace.package_checklist_title');
			default:
				return t('batches.workspace.checklist_title');
		}
	}

	let activeStageIngredients = $derived.by<BatchIngredientDto[]>(() => {
		if (!batch) return [];
		return filterCurrentStageOnly
			? getIngredientsForStage(inspectedStage, batch.ingredients)
			: sortIngredientsChronologically(batch.ingredients);
	});

	let advanceUpcomingIngredients = $derived.by<BatchIngredientDto[]>(() => {
		if (!batch) return [];
		return getIngredientsForStage(nextStage, batch.ingredients);
	});

	let totalMashGrainKg = $derived.by(() => {
		if (!batch) return 0;
		return batch.ingredients
			.filter(
				(i) =>
					i.type === 'Fermentable' &&
					(i.additionStage === 'Mash' ||
						(i.additionStage !== 'Boil' && i.additionStage !== 'Bottling'))
			)
			.reduce((sum, i) => sum + (i.unit === 'g' ? i.amount / 1000 : i.amount), 0);
	});

	let activeMashStepIndex = $state(0);

	let firstMashStepTargetTempC = $derived.by(() => {
		if (batch?.mashSteps && batch.mashSteps.length > 0) {
			return batch.mashSteps[0].targetTemperatureC;
		}
		return 65.0;
	});

	let currentSelectedMashStep = $derived.by<BatchMashStepDto | null>(() => {
		if (!batch?.mashSteps || batch.mashSteps.length === 0) return null;
		return batch.mashSteps[activeMashStepIndex] ?? batch.mashSteps[0];
	});

	let strikeRatio = $derived.by(() => {
		const strikeVol = batch?.volumeProfile?.strikeWaterLiters;
		if (strikeVol && totalMashGrainKg > 0) {
			return strikeVol / totalMashGrainKg;
		}
		return 3.0;
	});

	let estimatedStrikeTempC = $derived.by(() => {
		const targetMash = firstMashStepTargetTempC;
		const grainTemp = 20.0;
		const ratio = Math.max(0.5, strikeRatio);
		const tunLoss = 1.0;
		return formatNumber(targetMash + (0.41 / ratio) * (targetMash - grainTemp) + tunLoss, 1);
	});

	let estimatedSpargeTempC = $derived.by(() => {
		if (batch?.mashSteps && batch.mashSteps.length > 0) {
			const mashOutStep = [...batch.mashSteps]
				.reverse()
				.find(
					(s) =>
						/mash\s*out|utmäsk|sparge|lak/i.test(s.name) ||
						(s.targetTemperatureC >= 74 && s.targetTemperatureC <= 80)
				);
			if (mashOutStep) {
				return mashOutStep.targetTemperatureC;
			}
		}
		return 76.0;
	});

	let estimatedSpargeVolumeL = $derived.by(() => {
		return batch?.volumeProfile?.spargeWaterLiters ?? 0;
	});

	let estimatedStrikeVolumeL = $derived.by(() => {
		if (batch?.volumeProfile?.strikeWaterLiters) {
			return formatNumber(batch.volumeProfile.strikeWaterLiters, 1);
		}
		const vol = totalMashGrainKg * 3.0;
		return vol > 0
			? formatNumber(vol, 1)
			: batch
				? formatNumber(batch.targetBatchSizeLiters * 0.75, 1)
				: formatNumber(15.0, 1);
	});

	function handleSelectMashStep(index: number) {
		if (!batch?.mashSteps || index < 0 || index >= batch.mashSteps.length) return;
		activeMashStepIndex = index;
		selectedMashStepGraphId = batch.mashSteps[index].id;
		pauseTimer();
		timerRemainingSeconds = batch.mashSteps[index].durationMinutes * 60;
	}

	async function handleToggleMashStep(stepId: string, currentCompleted: boolean) {
		if (!batch) return;
		try {
			const updatedStep = await api.batches.toggleMashStep(batch.id, stepId, {
				isCompleted: !currentCompleted
			});
			const idx = batch.mashSteps.findIndex((s) => s.id === stepId);
			if (idx !== -1) {
				batch.mashSteps[idx] = updatedStep;
			}
		} catch (err: unknown) {
			console.error('Failed to toggle mash step', err);
		}
	}

	async function handleToggleFermentationStep(stepId: string, currentCompleted: boolean) {
		if (!batch) return;
		try {
			const updatedStep = await api.batches.toggleFermentationStep(batch.id, stepId, {
				isCompleted: !currentCompleted
			});
			if (batch.fermentationSteps) {
				const idx = batch.fermentationSteps.findIndex((s) => s.id === stepId);
				if (idx !== -1) {
					batch.fermentationSteps[idx] = updatedStep;
				}
			}
		} catch (err: unknown) {
			console.error('Failed to toggle fermentation step', err);
		}
	}

	let activeFermentationStep = $derived.by<BatchFermentationStepDto | null>(() => {
		if (!batch?.fermentationSteps || batch.fermentationSteps.length === 0) return null;
		const sorted = [...batch.fermentationSteps].sort((a, b) => a.stepOrder - b.stepOrder);
		const pending = sorted.find((s) => !s.isCompleted);
		return pending ?? sorted[sorted.length - 1];
	});

	let activeFermentationStepTargetTemp = $derived.by<number>(() => {
		if (activeFermentationStep) {
			return activeFermentationStep.targetTemperatureC;
		}
		if (inspectedStage === 'Ferment') {
			return batch?.pitchTemperatureC ?? 20.0;
		}
		return 2.0;
	});

	async function handleAdvanceMashStep() {
		if (!batch?.mashSteps || activeMashStepIndex >= batch.mashSteps.length) return;
		const currentStep = batch.mashSteps[activeMashStepIndex];
		if (currentStep && !currentStep.isCompleted) {
			await handleToggleMashStep(currentStep.id, false);
		}
		if (activeMashStepIndex + 1 < batch.mashSteps.length) {
			handleSelectMashStep(activeMashStepIndex + 1);
		}
	}

	function openAdvanceModal() {
		if (!batch) return;
		determineNextStage(batch.currentStage);
		advanceMeasuredOg = batch.measuredOg ?? batch.targetOg;
		advanceMeasuredFg = batch.measuredFg ?? batch.targetFg;
		advanceVolume =
			batch.volumeProfile?.targetFermenterVolumeLiters ??
			batch.measuredBatchSizeLiters ??
			batch.targetBatchSizeLiters;
		advancePitchTemp = batch.pitchTemperatureC ?? 20.0;
		advancePackagingVesselId = batch.packagingVesselId ?? '';
		advancePreBoilVolume =
			batch.volumeProfile?.measuredPreBoilVolumeLiters ??
			batch.volumeProfile?.targetPreBoilVolumeLiters ??
			null;
		advancePreBoilGravity = batch.volumeProfile?.measuredPreBoilGravity ?? null;
		advancePostBoilVolume =
			batch.volumeProfile?.measuredPostBoilVolumeLiters ??
			batch.volumeProfile?.targetPostBoilVolumeLiters ??
			null;
		advancePackagedVolume =
			batch.volumeProfile?.measuredPackagedVolumeLiters ??
			batch.volumeProfile?.targetPackagedVolumeLiters ??
			batch.targetBatchSizeLiters;
		advanceNotes = '';
		advanceError = null;
		showAdvanceModal = true;
	}

	async function handleAdvanceSubmit() {
		if (!batch) return;
		advancing = true;
		advanceError = null;
		try {
			const req: AdvanceBatchStageRequest = {
				targetStage: nextStage,
				measuredBatchSizeLiters: nextStage === 'Ferment' ? advanceVolume : null,
				measuredOg: nextStage === 'Ferment' ? advanceMeasuredOg : null,
				pitchTemperatureC: nextStage === 'Ferment' ? advancePitchTemp : null,
				measuredFg: nextStage === 'Package' ? advanceMeasuredFg : null,
				packagingVesselId: nextStage === 'Package' ? advancePackagingVesselId || null : null,
				notes: advanceNotes.trim() || null,
				measuredPreBoilVolumeLiters: nextStage === 'Boil' ? advancePreBoilVolume : null,
				measuredPreBoilGravity: nextStage === 'Boil' ? advancePreBoilGravity : null,
				measuredPostBoilVolumeLiters: nextStage === 'Ferment' ? advancePostBoilVolume : null,
				measuredPackagedVolumeLiters: nextStage === 'Package' ? advancePackagedVolume : null
			};
			const updated = await api.batches.advanceStage(batch.id, req);
			batch = updated;
			inspectedStage = updated.currentStage;
			determineNextStage(updated.currentStage);
			showAdvanceModal = false;
		} catch (err: unknown) {
			if (err instanceof ApiClientError && err.details && err.details.length > 0) {
				advanceError = err.details.map((d) => `${d.field}: ${d.issue}`).join(', ');
			} else {
				advanceError = (err as Error).message || t('batches.errors.advance_failed');
			}
		} finally {
			advancing = false;
		}
	}

	function openReadingModal() {
		newReadingSg = batch?.currentGravity ?? batch?.targetFg ?? 1.015;
		newReadingNotes = '';
		readingError = null;
		showReadingModal = true;
	}

	async function handleReadingSubmit() {
		if (!batch) return;
		savingReading = true;
		readingError = null;
		try {
			const req: AddBatchReadingRequest = {
				specificGravity: newReadingSg,
				temperatureC: null,
				notes: newReadingNotes.trim() || null
			};
			const newReading = await api.batches.addReading(batch.id, req);
			batch.readings = [...batch.readings, newReading];
			batch.currentGravity = newReading.specificGravity;
			if (newReading.alcoholByVolume !== undefined && newReading.alcoholByVolume !== null) {
				batch.alcoholByVolume = newReading.alcoholByVolume;
			} else {
				recalculateBatchAbv();
			}
			showReadingModal = false;
		} catch (err: unknown) {
			readingError = (err as Error).message || t('batches.errors.save_reading_failed');
		} finally {
			savingReading = false;
		}
	}

	function startTimer() {
		if (timerRunning) return;
		timerRunning = true;
		timerInterval = setInterval(() => {
			if (timerRemainingSeconds > 0) {
				timerRemainingSeconds--;
			} else {
				pauseTimer();
			}
		}, 1000);
	}

	function pauseTimer() {
		timerRunning = false;
		if (timerInterval) {
			clearInterval(timerInterval);
			timerInterval = null;
		}
	}

	function resetTimer() {
		pauseTimer();
		if (inspectedStage === 'Boil' && batch?.boilTimeMinutes) {
			timerRemainingSeconds = batch.boilTimeMinutes * 60;
		} else if (inspectedStage === 'Mash') {
			timerRemainingSeconds = (currentSelectedMashStep?.durationMinutes ?? 60) * 60;
		} else {
			timerRemainingSeconds = 3600;
		}
	}

	function formatTime(seconds: number) {
		const m = Math.floor(seconds / 60);
		const s = seconds % 60;
		return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
	}
</script>

<svelte:head>
	<title>BrewYou — {batch ? `${batch.batchCode} ${batch.name}` : t('batches.title')}</title>
</svelte:head>

<div class="space-y-6">
	{#if loading}
		<div class="flex flex-col items-center justify-center gap-3 py-20 text-zinc-400">
			<Loader2 class="h-8 w-8 animate-spin text-amber-500" />
			<span class="text-sm font-medium">{t('common.loading')}</span>
		</div>
	{:else if error || !batch}
		<div
			class="glass-panel flex flex-col items-center justify-center rounded-3xl border border-red-500/20 py-16 text-center"
		>
			<AlertTriangle class="h-10 w-10 text-red-500" />
			<h2 class="mt-4 text-lg font-bold text-zinc-900 dark:text-white">
				{error || t('batches.not_found')}
			</h2>
			<a
				href="/batches"
				class="mt-4 rounded-xl bg-zinc-100 px-4 py-2 text-xs font-semibold text-zinc-800 transition-colors hover:bg-zinc-200 dark:bg-zinc-800 dark:text-zinc-200 dark:hover:bg-zinc-700"
			>
				{t('batches.back_to_batches')}
			</a>
		</div>
	{:else}
		{@const badge = getStatusBadge(batch.status)}

		<!-- Header & Actions -->
		<div class="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
			<div class="space-y-1">
				<div class="flex flex-wrap items-center gap-2.5">
					<span class="font-mono text-sm font-bold text-amber-600 dark:text-amber-400">
						{batch.batchCode}
					</span>
					<span class="text-zinc-300 dark:text-zinc-700">•</span>
					<span class="text-sm text-zinc-500">{batch.beerStyle}</span>
					<div
						class="flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-xs font-bold tracking-wide uppercase {badge.class}"
					>
						<span class="relative flex h-2 w-2">
							<span class="relative inline-flex h-2 w-2 rounded-full {badge.dot.split(' ')[0]}"
							></span>
						</span>
						<span>{badge.label}</span>
					</div>
				</div>
				<h1 class="text-2xl font-bold tracking-tight text-zinc-900 sm:text-3xl dark:text-white">
					{batch.name}
				</h1>
			</div>

			<!-- Advance Stage & Primary CTA -->
			<div class="flex items-center gap-2.5 self-start sm:self-auto">
				<!-- Brew Station HUD Button -->
				<button
					type="button"
					data-testid="open-brew-station-hud-btn"
					onclick={() => (showBrewStationHud = true)}
					class="tactile-pill flex min-h-[44px] items-center gap-2 rounded-xl border border-amber-500/40 bg-amber-500/10 px-4 py-2.5 text-sm font-bold text-amber-700 transition-all hover:bg-amber-500/20 active:scale-[0.98] dark:text-amber-300"
					title={t('batches.workspace.brew_station_hud')}
				>
					<Flame class="h-4 w-4 animate-pulse text-amber-500" />
					<span class="hidden sm:inline">{t('batches.workspace.brew_station_hud')}</span>
					<Maximize2 class="h-3.5 w-3.5 opacity-70" />
				</button>

				{#if batch.status !== 'Completed'}
					<button
						type="button"
						data-testid="advance-stage-btn"
						onclick={openAdvanceModal}
						class="flex min-h-[44px] items-center gap-2 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-4 py-2.5 text-sm font-bold text-zinc-950 shadow-md transition-all hover:from-amber-400 hover:to-amber-500 active:scale-[0.98]"
					>
						<ArrowRight class="h-4 w-4 stroke-[2.5]" />
						<span>{t('batches.workspace.advance_stage')}</span>
					</button>
				{/if}

				<button
					type="button"
					onclick={() => (showDeleteBatchModal = true)}
					title={t('batches.workspace.delete_batch')}
					aria-label={t('batches.workspace.delete_batch')}
					class="flex min-h-[44px] min-w-[44px] items-center justify-center rounded-xl border border-zinc-200 p-2.5 text-zinc-500 transition-colors hover:border-red-500/30 hover:bg-red-500/10 hover:text-red-500 dark:border-zinc-800 dark:text-zinc-400"
				>
					<Trash2 class="h-4 w-4" />
				</button>
			</div>
		</div>

		<!-- Stage Tracker Navigation Banner -->
		<div class="glass-panel rounded-2xl border border-zinc-200/80 p-2 sm:p-3 dark:border-white/10">
			<BrewStageTracker
				stages={uiStages}
				activeStageId={batch.currentStage.toLowerCase()}
				selectedStageId={inspectedStage.toLowerCase()}
				interactive={true}
				onStageSelect={(s) => {
					const stageOrder: BrewStage[] = ['Mash', 'Boil', 'Ferment', 'Condition', 'Package'];
					const found = stageOrder.find((st) => st.toLowerCase() === s.id.toLowerCase());
					if (found) {
						inspectedStage = found;
					}
				}}
			/>
		</div>

		<!-- Contextual Inspection Banner when viewing non-active stage -->
		{#if !isInspectingActiveStage && batch}
			<aside
				role="status"
				aria-live="polite"
				data-testid="inspecting-stage-banner"
				class="glass-panel flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-amber-500/30 bg-amber-500/10 px-4 py-3 dark:border-amber-400/20 dark:bg-amber-500/5"
			>
				<div class="flex items-center gap-3">
					<div
						class="flex h-8 w-8 shrink-0 items-center justify-center rounded-xl bg-amber-500/20 text-amber-600 dark:text-amber-400"
					>
						<Timer class="h-4 w-4" />
					</div>
					<p class="text-xs font-medium text-zinc-700 dark:text-zinc-200">
						{t('batches.workspace.inspecting_banner', {
							stage: t(`batches.stages.${inspectedStage.toLowerCase()}`),
							status: t(`batches.workspace.stage_status_${inspectedStageStatus}`),
							activeStage: t(`batches.stages.${batch.currentStage.toLowerCase()}`)
						})}
					</p>
				</div>

				<button
					type="button"
					data-testid="return-to-active-stage-btn"
					onclick={() => (inspectedStage = batch!.currentStage)}
					class="flex min-h-[36px] items-center gap-1.5 rounded-xl bg-amber-500/20 px-3 py-1.5 text-xs font-bold text-amber-800 transition-colors hover:bg-amber-500/30 active:scale-95 dark:text-amber-300"
				>
					<RotateCcw class="h-3.5 w-3.5" />
					<span>
						{t('batches.workspace.return_to_active', {
							stage: t(`batches.stages.${batch.currentStage.toLowerCase()}`)
						})}
					</span>
				</button>
			</aside>
		{/if}

		<!-- Vital Metrics HUD Grid -->
		<div class="grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-5">
			<!-- ABV -->
			<div
				class="glass-panel rounded-2xl border border-zinc-200/80 p-3.5 text-center dark:border-white/10"
			>
				<span
					class="block text-[11px] font-semibold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
				>
					{t('batches.workspace.current_abv')}
				</span>
				<div class="mt-1 flex items-baseline justify-center gap-1">
					<span
						data-testid="hud-current-abv"
						class="font-mono text-xl font-bold text-amber-600 dark:text-amber-400"
					>
						{currentAbv ? `${formatNumber(currentAbv, 2)}%` : '—'}
					</span>
					<span class="text-xs text-zinc-400">/ {formatNumber(batch.targetAbv, 1)}%</span>
				</div>
			</div>

			<!-- Gravity (Current vs Target FG) -->
			<div
				class="glass-panel rounded-2xl border border-zinc-200/80 p-3.5 text-center dark:border-white/10"
			>
				<span
					class="block text-[11px] font-semibold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
				>
					{t('batches.current_gravity')}
				</span>
				<div class="mt-1 flex items-baseline justify-center gap-1">
					{#if batch.currentGravity}
						<span class="font-mono text-xl font-bold text-emerald-600 dark:text-emerald-400">
							{formatNumber(batch.currentGravity, 3)}
						</span>
						<span class="text-xs text-zinc-400">/ {formatNumber(batch.targetFg, 3)}</span>
					{:else if batch.currentStage === 'Mash' || batch.currentStage === 'Boil'}
						<span class="font-mono text-xl font-bold text-zinc-400 dark:text-zinc-500"> — </span>
						<span class="text-xs text-zinc-400">({t('batches.workspace.not_started')})</span>
					{:else}
						<span class="font-mono text-xl font-bold text-zinc-500">
							{formatNumber(batch.measuredOg ?? batch.targetOg, 3)}
						</span>
						<span class="text-xs text-zinc-400">/ {formatNumber(batch.targetFg, 3)}</span>
					{/if}
				</div>
			</div>

			<!-- Measured OG / Target OG -->
			<div
				class="glass-panel rounded-2xl border border-zinc-200/80 p-3.5 text-center dark:border-white/10"
			>
				<span
					class="block text-[11px] font-semibold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
				>
					{batch.measuredOg ? t('batches.measured_og') : t('batches.telemetry.target_og')}
				</span>
				<div class="mt-1 flex items-baseline justify-center gap-1">
					<span class="font-mono text-xl font-bold text-zinc-900 dark:text-white">
						{formatNumber(batch.measuredOg ?? batch.targetOg, 3)}
					</span>
				</div>
			</div>

			<!-- Volume -->
			<div
				class="glass-panel rounded-2xl border border-zinc-200/80 p-3.5 text-center dark:border-white/10"
			>
				<span
					class="block text-[11px] font-semibold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
				>
					{t('batches.volume')}
				</span>
				<div class="mt-1 flex items-baseline justify-center gap-1">
					<span class="font-mono text-xl font-bold text-zinc-900 dark:text-white">
						{batch.measuredBatchSizeLiters ?? batch.targetBatchSizeLiters}L
					</span>
				</div>
			</div>

			<!-- SRM / Color -->
			<div
				class="glass-panel col-span-2 flex items-center justify-between rounded-2xl border border-zinc-200/80 p-3.5 sm:col-span-4 lg:col-span-1 dark:border-white/10"
			>
				<div>
					<span
						class="block text-[11px] font-semibold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
					>
						{t('batches.color_srm')}
					</span>
					<span class="font-mono text-lg font-bold text-zinc-900 dark:text-white">
						{formatNumber(batch.targetColorSrm, 1)}
					</span>
				</div>
				<SrmSwatch srm={batch.targetColorSrm} size="md" shape="glass" />
			</div>
		</div>

		<!-- Active Stage Workspace -->
		<div class="space-y-6">
			<!-- Non-active stage status notice -->
			{#if !isInspectingActiveStage}
				{#if inspectedStageStatus === 'pending'}
					<div
						class="flex items-center gap-2 rounded-2xl border border-zinc-200/80 bg-zinc-50 p-4 text-xs font-medium text-zinc-600 dark:border-white/10 dark:bg-zinc-900/60 dark:text-zinc-400"
					>
						<AlertTriangle class="h-4 w-4 shrink-0 text-zinc-400" />
						<span>
							{t('batches.workspace.pending_stage_notice', {
								activeStage: t(`batches.stages.${batch.currentStage.toLowerCase()}`)
							})}
						</span>
					</div>
				{/if}
			{/if}

			{#if inspectedStage === 'Mash'}
				<!-- Brew Day Mash Panel -->
				<div class="space-y-6">
					<!-- Mash Guidance & Strike Water Card -->
					<div
						class="glass-panel rounded-3xl border border-amber-500/20 bg-amber-500/5 p-5 dark:border-amber-500/10 dark:bg-amber-500/5"
					>
						<div class="flex items-center gap-2 font-bold text-amber-900 dark:text-amber-300">
							<Thermometer class="h-4 w-4 text-amber-500" />
							<span class="text-sm">{t('batches.workspace.mash_guidance_title')}</span>
						</div>
						<div
							class="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-3 {estimatedSpargeVolumeL > 0
								? 'lg:grid-cols-5'
								: 'lg:grid-cols-4'}"
						>
							<div
								class="rounded-xl border border-zinc-200/60 bg-white/70 p-2.5 text-center dark:border-white/5 dark:bg-zinc-900/60"
							>
								<span
									class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400"
								>
									{t('batches.workspace.target_mash_temp')}
								</span>
								<span class="font-mono text-sm font-bold text-zinc-900 dark:text-white">
									{settings.formatTemperature(firstMashStepTargetTempC)}
								</span>
							</div>
							<div
								class="rounded-xl border border-zinc-200/60 bg-white/70 p-2.5 text-center dark:border-white/5 dark:bg-zinc-900/60"
							>
								<span
									class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400"
								>
									{t('batches.workspace.recommended_strike_temp')}
								</span>
								<span class="font-mono text-sm font-bold text-amber-600 dark:text-amber-400">
									{settings.formatTemperature(parseFloat(estimatedStrikeTempC))}
								</span>
							</div>
							{#if estimatedSpargeVolumeL > 0}
								<div
									class="rounded-xl border border-zinc-200/60 bg-white/70 p-2.5 text-center dark:border-white/5 dark:bg-zinc-900/60"
								>
									<span
										class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400"
									>
										{t('batches.workspace.recommended_sparge_temp')}
									</span>
									<span class="font-mono text-sm font-bold text-amber-600 dark:text-amber-400">
										{settings.formatTemperature(estimatedSpargeTempC)}
									</span>
								</div>
							{/if}
							<div
								class="rounded-xl border border-zinc-200/60 bg-white/70 p-2.5 text-center dark:border-white/5 dark:bg-zinc-900/60"
							>
								<span
									class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400"
								>
									{t('batches.workspace.total_grain_weight')}
								</span>
								<span class="font-mono text-sm font-bold text-zinc-900 dark:text-white">
									{totalMashGrainKg > 0 ? settings.formatGrainWeight(totalMashGrainKg) : '—'}
								</span>
							</div>
							<div
								class="rounded-xl border border-zinc-200/60 bg-white/70 p-2.5 text-center dark:border-white/5 dark:bg-zinc-900/60"
							>
								<span
									class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400"
								>
									{t('batches.workspace.est_strike_volume')}
								</span>
								<span class="font-mono text-sm font-bold text-zinc-900 dark:text-white">
									{settings.formatVolume(parseFloat(estimatedStrikeVolumeL))}
								</span>
							</div>
						</div>
					</div>

					<!-- Mash Rest Timer & Step Progression Grid -->
					<div class="grid grid-cols-1 gap-6 lg:grid-cols-3">
						<!-- Active Mash Step Timer & Controls -->
						<div
							data-testid="mash-timer-panel"
							class="glass-panel flex flex-col justify-between rounded-3xl border border-zinc-200/80 p-6 dark:border-white/10"
						>
							<div class="space-y-2">
								<div class="flex items-center justify-between">
									<div class="flex items-center gap-2 text-amber-500">
										<Timer class="h-5 w-5" />
										<h2 class="text-base font-bold text-zinc-900 dark:text-white">
											{t('batches.workspace.mash_timer')}
										</h2>
									</div>
									<div class="flex items-center gap-2">
										{#if currentSelectedMashStep}
											<span
												data-testid="active-step-order-badge"
												class="rounded-md bg-amber-500/10 px-2 py-0.5 font-mono text-xs font-semibold text-amber-600 dark:text-amber-400"
											>
												{t('mash_profile.step_order_badge', {
													order: currentSelectedMashStep.stepOrder
												})}
											</span>
										{/if}
										<button
											type="button"
											onclick={() => (showBrewStationHud = true)}
											class="rounded-lg p-1 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
											title={t('batches.workspace.brew_station_hud')}
											aria-label={t('batches.workspace.brew_station_hud')}
										>
											<Maximize2 class="h-4 w-4" />
										</button>
									</div>
								</div>

								{#if currentSelectedMashStep}
									<div
										class="rounded-xl border border-zinc-200/60 bg-white/50 p-2.5 dark:border-zinc-800/60 dark:bg-zinc-900/40"
									>
										<p
											data-testid="active-step-name"
											class="font-semibold text-zinc-900 dark:text-white"
										>
											{currentSelectedMashStep.name}
										</p>
										<p class="text-xs text-zinc-500 dark:text-zinc-400">
											{settings.formatTemperature(currentSelectedMashStep.targetTemperatureC)} • {currentSelectedMashStep.durationMinutes}
											{t('common.minutes')}
										</p>
									</div>
								{/if}

								<div class="py-4 text-center">
									<span class="font-mono text-5xl font-extrabold text-zinc-900 dark:text-white">
										{formatTime(timerRemainingSeconds)}
									</span>
								</div>
							</div>

							<div class="flex flex-col gap-3">
								<div class="flex items-center justify-center gap-3">
									{#if !timerRunning}
										<button
											type="button"
											onclick={startTimer}
											class="flex min-h-[44px] items-center gap-2 rounded-xl bg-amber-500 px-5 py-2.5 text-xs font-bold text-zinc-950 shadow-md transition-all hover:bg-amber-400 active:scale-95"
										>
											<Play class="h-4 w-4 fill-current" />
											<span>{t('batches.workspace.start_timer')}</span>
										</button>
									{:else}
										<button
											type="button"
											onclick={pauseTimer}
											class="flex min-h-[44px] items-center gap-2 rounded-xl bg-zinc-200 px-5 py-2.5 text-xs font-bold text-zinc-800 transition-all hover:bg-zinc-300 active:scale-95 dark:bg-zinc-800 dark:text-zinc-200"
										>
											<Pause class="h-4 w-4 fill-current" />
											<span>{t('batches.workspace.pause_timer')}</span>
										</button>
									{/if}

									<button
										type="button"
										onclick={resetTimer}
										class="flex min-h-[44px] items-center gap-2 rounded-xl border border-zinc-200 px-4 py-2.5 text-xs font-semibold text-zinc-600 transition-colors hover:bg-zinc-100 active:scale-95 dark:border-zinc-800 dark:text-zinc-400 dark:hover:bg-zinc-800"
									>
										<RotateCcw class="h-3.5 w-3.5" />
										<span>{t('batches.workspace.reset_timer')}</span>
									</button>
								</div>

								{#if batch.mashSteps && batch.mashSteps.length > 1}
									<button
										type="button"
										data-testid="advance-mash-step-btn"
										onclick={handleAdvanceMashStep}
										class="flex min-h-[38px] w-full items-center justify-center gap-1.5 rounded-xl border border-amber-500/30 bg-amber-500/10 py-2 text-xs font-bold text-amber-700 transition-colors hover:bg-amber-500/20 active:scale-95 dark:text-amber-300"
									>
										<StepForward class="h-4 w-4" />
										<span>{t('batches.workspace.complete_and_next_step')}</span>
									</button>
								{/if}
							</div>
						</div>

						<!-- Mash Step Timeline & Execution Stepper -->
						<div
							data-testid="mash-schedule-card"
							class="glass-panel flex flex-col rounded-3xl border border-zinc-200/80 p-6 lg:col-span-2 dark:border-white/10"
						>
							<div
								class="flex items-center justify-between border-b border-zinc-200/60 pb-3 dark:border-white/5"
							>
								<div class="flex items-center gap-2 text-zinc-900 dark:text-white">
									<Clock class="h-5 w-5 text-amber-500" />
									<h2 class="text-base font-bold">
										{t('batches.workspace.mash_schedule_title')}
									</h2>
								</div>
								<span class="font-mono text-xs text-zinc-500">
									{batch.mashSteps?.filter((s) => s.isCompleted).length ?? 0} / {batch.mashSteps
										?.length ?? 1}
								</span>
							</div>

							<div class="mt-4 space-y-3">
								{#if !batch.mashSteps || batch.mashSteps.length === 0}
									<div
										class="rounded-xl border border-zinc-200/60 bg-white/50 p-4 text-center text-xs text-zinc-500 dark:border-zinc-800/60 dark:bg-zinc-900/40"
									>
										{t('mash_profile.no_steps')}
									</div>
								{:else}
									{#each batch.mashSteps as step, idx (step.id)}
										<div
											data-testid="mash-step-row-{step.id}"
											class="flex items-center justify-between rounded-xl border p-3.5 transition-all {activeMashStepIndex ===
											idx
												? 'border-amber-500 bg-amber-50/50 shadow-xs dark:border-amber-500/50 dark:bg-amber-500/10'
												: 'border-zinc-200/60 bg-white/60 hover:bg-zinc-50 dark:border-zinc-800/60 dark:bg-zinc-900/30 dark:hover:bg-zinc-900/60'}"
										>
											<div class="flex items-center gap-3">
												<input
													type="checkbox"
													data-testid="mash-step-checkbox-{step.id}"
													checked={step.isCompleted}
													onchange={() => handleToggleMashStep(step.id, step.isCompleted)}
													class="h-4 w-4 rounded-md border-zinc-300 text-amber-500 focus:ring-amber-500/30 dark:border-zinc-700 dark:bg-zinc-900"
												/>
												<div>
													<button
														type="button"
														data-testid="mash-step-name-btn-{step.id}"
														onclick={() => handleSelectMashStep(idx)}
														class="cursor-pointer text-left text-sm font-semibold {step.isCompleted
															? 'text-zinc-400 line-through dark:text-zinc-500'
															: 'text-zinc-900 dark:text-white'} hover:text-amber-600 dark:hover:text-amber-400"
													>
														<span
															class="mr-1.5 font-mono text-xs text-amber-600 dark:text-amber-400"
															>#{step.stepOrder}</span
														>
														{step.name}
													</button>
													<p class="text-xs text-zinc-500 dark:text-zinc-400">
														{settings.formatTemperature(step.targetTemperatureC)} • {step.durationMinutes}
														{t('common.minutes')} • {t(
															`mash_profile.type_${step.type.toLowerCase()}`
														)}
														{#if step.actualTemperatureC !== null && step.actualTemperatureC !== undefined}
															• <span class="font-semibold text-amber-600 dark:text-amber-400">
																{t('batches.telemetry.actual_temp')}: {settings.formatTemperature(
																	step.actualTemperatureC
																)}
															</span>
														{/if}
													</p>
												</div>
											</div>

											<div class="flex items-center gap-2">
												{#if canLogInspectedStage}
													<button
														type="button"
														onclick={() => openLogTempModal('Mash', step.id)}
														class="inline-flex cursor-pointer items-center gap-1 rounded-lg border border-amber-500/30 bg-amber-500/10 px-2 py-1 text-xs font-medium text-amber-700 transition-colors hover:bg-amber-500/20 dark:bg-amber-500/20 dark:text-amber-300 dark:hover:bg-amber-500/30"
														title={t('batches.telemetry.log_temperature')}
													>
														<Thermometer class="h-3 w-3" />
														<span>{t('batches.telemetry.log_temperature')}</span>
													</button>
												{/if}

												{#if activeMashStepIndex === idx}
													<span
														class="rounded-full bg-amber-500/20 px-2 py-0.5 text-[10px] font-bold tracking-wider text-amber-700 uppercase dark:text-amber-300"
													>
														{t('batches.workspace.active_step')}
													</span>
												{:else}
													<button
														type="button"
														data-testid="mash-step-select-{step.id}"
														onclick={() => handleSelectMashStep(idx)}
														class="cursor-pointer rounded-lg border border-zinc-200/80 px-2.5 py-1 text-xs font-medium text-zinc-600 transition-colors hover:bg-zinc-100 dark:border-zinc-800 dark:text-zinc-400 dark:hover:bg-zinc-800"
													>
														{t('batches.workspace.select_step')}
													</button>
												{/if}
											</div>
										</div>
									{/each}
								{/if}
							</div>
						</div>
					</div>

					<!-- Step Temperature Progression Graph -->
					<StepTemperatureChart
						stage="Mash"
						readings={batchEquipmentReadings}
						mashSteps={batch.mashSteps}
						selectedStepId={selectedMashStepGraphId}
						targetTemperatureC={currentSelectedMashStep?.targetTemperatureC}
						onSelectStep={(id) => {
							selectedMashStepGraphId = id;
							if (id && batch?.mashSteps) {
								const idx = batch.mashSteps.findIndex((s) => s.id === id);
								if (idx !== -1) activeMashStepIndex = idx;
							}
						}}
						isLive={isTelemetryLive && isInspectingActiveStage}
						onOpenLogModal={canLogInspectedStage
							? () =>
									openLogTempModal('Mash', selectedMashStepGraphId ?? currentSelectedMashStep?.id)
							: undefined}
						equipmentName={currentStageEquipmentName ?? batch.boilerName}
					/>

					<!-- Mash Additions Checklist -->
					<div class="glass-panel rounded-3xl border border-zinc-200/80 p-6 dark:border-white/10">
						<div
							class="flex flex-wrap items-center justify-between gap-3 border-b border-zinc-200/60 pb-4 dark:border-white/5"
						>
							<div class="flex items-center gap-2 text-zinc-900 dark:text-white">
								<CheckCircle2 class="h-5 w-5 text-emerald-500" />
								<h2 class="text-base font-bold">
									{filterCurrentStageOnly
										? t('batches.workspace.mash_checklist_title')
										: t('batches.workspace.checklist_title')}
								</h2>
							</div>
							<div class="flex items-center gap-3">
								<button
									type="button"
									onclick={() => (filterCurrentStageOnly = !filterCurrentStageOnly)}
									class="flex min-h-[32px] items-center gap-1.5 rounded-lg border border-zinc-200/80 px-2.5 py-1 text-xs font-medium text-zinc-600 transition-colors hover:bg-zinc-100 dark:border-white/10 dark:text-zinc-400 dark:hover:bg-zinc-800"
								>
									<Filter class="h-3.5 w-3.5" />
									<span>
										{filterCurrentStageOnly
											? t('batches.workspace.stage_filter_current')
											: t('batches.workspace.stage_filter_all')}
									</span>
								</button>
								<span class="font-mono text-xs text-zinc-500">
									{activeStageIngredients.filter((i) => i.isChecked).length} / {activeStageIngredients.length}
								</span>
							</div>
						</div>

						{#if activeStageIngredients.length === 0}
							<p class="py-8 text-center text-xs text-zinc-400">
								{filterCurrentStageOnly
									? t('batches.workspace.stage_ingredients_empty')
									: t('batches.workspace.checklist_empty')}
							</p>
						{:else}
							<ul class="mt-4 divide-y divide-zinc-200/40 dark:divide-white/5">
								{#each activeStageIngredients as ing (ing.id)}
									<li class="flex items-center justify-between py-3">
										<label class="flex cursor-pointer items-center gap-3">
											<input
												type="checkbox"
												checked={ing.isChecked}
												onchange={() => handleToggleIngredient(ing.id, ing.isChecked)}
												class="h-4 w-4 rounded-md border-zinc-300 text-amber-500 focus:ring-amber-500/30 dark:border-zinc-700 dark:bg-zinc-900"
											/>
											<div class="space-y-0.5">
												<span
													class="text-sm font-semibold {ing.isChecked
														? 'text-zinc-400 line-through dark:text-zinc-500'
														: 'text-zinc-900 dark:text-zinc-100'}"
												>
													{ing.name}
												</span>
												<span class="block text-xs text-zinc-500">
													{ing.type} • {ing.additionStage}
													{#if ing.additionTimeMinutes}
														({ing.additionTimeMinutes}m)
													{/if}
												</span>
											</div>
										</label>

										<span class="font-mono text-xs font-bold text-zinc-700 dark:text-zinc-300">
											{ing.amount}
											{ing.unit}
										</span>
									</li>
								{/each}
							</ul>
						{/if}
					</div>

					<!-- Mash Stage Measurements & Manual Inputs Card -->
					<StageMeasurementsCard
						stage="Mash"
						{batch}
						{equipmentList}
						onSave={handleSaveStageMeasurements}
					/>
				</div>
			{:else if inspectedStage === 'Boil'}
				<!-- Brew Day Boil Panel -->
				<div class="grid grid-cols-1 gap-6 lg:grid-cols-3">
					<!-- Boil Timer & Controls -->
					<div
						class="glass-panel flex flex-col justify-between rounded-3xl border border-zinc-200/80 p-6 dark:border-white/10"
					>
						<div class="space-y-2">
							<div class="flex items-center justify-between">
								<div class="flex items-center gap-2 text-amber-500">
									<Flame class="h-5 w-5" />
									<h2 class="text-base font-bold text-zinc-900 dark:text-white">
										{t('batches.workspace.boil_timer')}
									</h2>
								</div>
								<button
									type="button"
									onclick={() => (showBrewStationHud = true)}
									class="rounded-lg p-1 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
									title={t('batches.workspace.brew_station_hud')}
									aria-label={t('batches.workspace.brew_station_hud')}
								>
									<Maximize2 class="h-4 w-4" />
								</button>
							</div>
							<p class="text-xs text-zinc-500 dark:text-zinc-400">
								{t('batches.workspace.boil_timer_desc')}
							</p>
							<div class="py-6 text-center">
								<span class="font-mono text-5xl font-extrabold text-zinc-900 dark:text-white">
									{formatTime(timerRemainingSeconds)}
								</span>
							</div>
						</div>

						<div class="flex items-center justify-center gap-3">
							{#if !timerRunning}
								<button
									type="button"
									onclick={startTimer}
									class="flex min-h-[44px] items-center gap-2 rounded-xl bg-amber-500 px-5 py-2.5 text-xs font-bold text-zinc-950 shadow-md transition-all hover:bg-amber-400"
								>
									<Play class="h-4 w-4 fill-current" />
									<span>{t('batches.workspace.start_timer')}</span>
								</button>
							{:else}
								<button
									type="button"
									onclick={pauseTimer}
									class="flex min-h-[44px] items-center gap-2 rounded-xl bg-zinc-200 px-5 py-2.5 text-xs font-bold text-zinc-800 transition-all hover:bg-zinc-300 dark:bg-zinc-800 dark:text-zinc-200"
								>
									<Pause class="h-4 w-4 fill-current" />
									<span>{t('batches.workspace.pause_timer')}</span>
								</button>
							{/if}

							<button
								type="button"
								onclick={resetTimer}
								class="flex min-h-[44px] items-center gap-2 rounded-xl border border-zinc-200 px-4 py-2.5 text-xs font-semibold text-zinc-600 transition-colors hover:bg-zinc-100 dark:border-zinc-800 dark:text-zinc-400 dark:hover:bg-zinc-800"
							>
								<RotateCcw class="h-3.5 w-3.5" />
								<span>{t('batches.workspace.reset_timer')}</span>
							</button>
						</div>
					</div>

					<!-- Boil Checklist -->
					<div
						class="glass-panel rounded-3xl border border-zinc-200/80 p-6 lg:col-span-2 dark:border-white/10"
					>
						<div
							class="flex flex-wrap items-center justify-between gap-3 border-b border-zinc-200/60 pb-4 dark:border-white/5"
						>
							<div class="flex items-center gap-2 text-zinc-900 dark:text-white">
								<CheckCircle2 class="h-5 w-5 text-emerald-500" />
								<h2 class="text-base font-bold">
									{filterCurrentStageOnly
										? t('batches.workspace.boil_checklist_title')
										: t('batches.workspace.checklist_title')}
								</h2>
							</div>
							<div class="flex items-center gap-3">
								<button
									type="button"
									onclick={() => (filterCurrentStageOnly = !filterCurrentStageOnly)}
									class="flex min-h-[32px] items-center gap-1.5 rounded-lg border border-zinc-200/80 px-2.5 py-1 text-xs font-medium text-zinc-600 transition-colors hover:bg-zinc-100 dark:border-white/10 dark:text-zinc-400 dark:hover:bg-zinc-800"
								>
									<Filter class="h-3.5 w-3.5" />
									<span>
										{filterCurrentStageOnly
											? t('batches.workspace.stage_filter_current')
											: t('batches.workspace.stage_filter_all')}
									</span>
								</button>
								<span class="font-mono text-xs text-zinc-500">
									{activeStageIngredients.filter((i) => i.isChecked).length} / {activeStageIngredients.length}
								</span>
							</div>
						</div>

						{#if activeStageIngredients.length === 0}
							<p class="py-8 text-center text-xs text-zinc-400">
								{filterCurrentStageOnly
									? t('batches.workspace.stage_ingredients_empty')
									: t('batches.workspace.checklist_empty')}
							</p>
						{:else}
							<ul class="mt-4 divide-y divide-zinc-200/40 dark:divide-white/5">
								{#each activeStageIngredients as ing (ing.id)}
									<li class="flex items-center justify-between py-3">
										<label class="flex cursor-pointer items-center gap-3">
											<input
												type="checkbox"
												checked={ing.isChecked}
												onchange={() => handleToggleIngredient(ing.id, ing.isChecked)}
												class="h-4 w-4 rounded-md border-zinc-300 text-amber-500 focus:ring-amber-500/30 dark:border-zinc-700 dark:bg-zinc-900"
											/>
											<div class="space-y-0.5">
												<span
													class="text-sm font-semibold {ing.isChecked
														? 'text-zinc-400 line-through dark:text-zinc-500'
														: 'text-zinc-900 dark:text-zinc-100'}"
												>
													{ing.name}
												</span>
												<span class="block text-xs text-zinc-500">
													{ing.type} • {ing.additionStage}
													{#if ing.additionTimeMinutes}
														({ing.additionTimeMinutes}m)
													{/if}
												</span>
											</div>
										</label>

										<span class="font-mono text-xs font-bold text-zinc-700 dark:text-zinc-300">
											{ing.amount}
											{ing.unit}
										</span>
									</li>
								{/each}
							</ul>
						{/if}
					</div>

					<!-- Boil Stage Temperature Progression Graph -->
					<div class="lg:col-span-3">
						<StepTemperatureChart
							stage="Boil"
							readings={batchEquipmentReadings}
							targetTemperatureC={100.0}
							isLive={isTelemetryLive && isInspectingActiveStage}
							onOpenLogModal={canLogInspectedStage ? () => openLogTempModal('Boil') : undefined}
							equipmentName={currentStageEquipmentName ?? batch.boilerName}
						/>
					</div>

					<!-- Boil Stage Measurements & Manual Inputs Card -->
					<div class="lg:col-span-3">
						<StageMeasurementsCard
							stage="Boil"
							{batch}
							{equipmentList}
							onSave={handleSaveStageMeasurements}
						/>
					</div>
				</div>
			{:else if inspectedStage === 'Ferment' || inspectedStage === 'Condition'}
				<!-- Fermentation & Attenuation Panel -->
				<div class="space-y-6">
					<!-- Unified Fermentation & Attenuation Graph (Single graph incorporating SG, fermentor sensors, temp, and FG) -->
					<AttenuationChart
						readings={batch.readings}
						equipmentReadings={batchEquipmentReadings.filter(
							(r) =>
								r.stage === inspectedStage ||
								(!r.stage && (inspectedStage === 'Ferment' || inspectedStage === 'Condition'))
						)}
						targetOg={batch.measuredOg ?? batch.targetOg}
						targetFg={batch.targetFg}
						targetTemperatureC={activeFermentationStepTargetTemp}
						stage={inspectedStage}
						equipmentName={fermentorSensorName}
						sensorSubtype={fermentorSensorSubtype}
						isLive={isTelemetryLive && isInspectingActiveStage}
						daysActive={batch.daysActive}
						canLog={canLogInspectedStage}
						onOpenLogReading={openReadingModal}
						onOpenLogTemperature={canLogInspectedStage
							? () => openLogTempModal(inspectedStage)
							: undefined}
					/>

					<!-- Fermentation Steps Schedule & Progress Card -->
					{#if batch.fermentationSteps && batch.fermentationSteps.length > 0}
						<div
							data-testid="fermentation-steps-card"
							class="glass-panel rounded-3xl border border-zinc-200/80 p-6 dark:border-white/10"
						>
							<div
								class="flex flex-wrap items-center justify-between gap-3 border-b border-zinc-200/60 pb-4 dark:border-white/5"
							>
								<div class="flex items-center gap-2 text-zinc-900 dark:text-white">
									<Activity class="h-5 w-5 text-cyan-500" />
									<h2 class="text-base font-bold">
										{t('batches.workspace.fermentation_steps_title')}
									</h2>
								</div>
								{#if activeFermentationStep}
									<span
										data-testid="active-fermentation-step-badge"
										class="rounded-lg bg-cyan-500/10 px-2.5 py-1 text-xs font-semibold text-cyan-600 dark:text-cyan-400"
									>
										{t('batches.workspace.active_step')}: {activeFermentationStep.name} ({settings.formatTemperature(
											activeFermentationStep.targetTemperatureC
										)})
									</span>
								{/if}
							</div>

							<div class="mt-4 divide-y divide-zinc-200/40 dark:divide-white/5">
								{#each batch.fermentationSteps as step (step.id)}
									<div
										data-testid="fermentation-step-row-{step.id}"
										class="flex flex-col justify-between gap-2 py-3 sm:flex-row sm:items-center"
									>
										<label class="flex cursor-pointer items-center gap-3">
											<input
												type="checkbox"
												data-testid="fermentation-step-checkbox-{step.id}"
												checked={step.isCompleted}
												onchange={() => handleToggleFermentationStep(step.id, step.isCompleted)}
												class="h-4 w-4 rounded-md border-zinc-300 text-cyan-600 focus:ring-cyan-500/30 dark:border-zinc-700 dark:bg-zinc-900"
											/>
											<div class="space-y-0.5">
												<div class="flex items-center gap-2">
													<span
														class="text-sm font-semibold {step.isCompleted
															? 'text-zinc-400 line-through dark:text-zinc-500'
															: 'text-zinc-900 dark:text-zinc-100'}"
													>
														{step.stepOrder}. {step.name}
													</span>
													<span
														class="rounded-md bg-zinc-100 px-1.5 py-0.5 text-[10px] font-bold tracking-wider text-zinc-600 uppercase dark:bg-zinc-800 dark:text-zinc-300"
													>
														{t(`fermentation_profile.type_${step.type.toLowerCase()}`)}
													</span>
												</div>
												<div
													class="flex flex-wrap items-center gap-2 text-xs text-zinc-500 dark:text-zinc-400"
												>
													<span>{step.durationDays} {t('fermentation_profile.days')}</span>
													{#if step.rampTimeHours}
														<span>• {step.rampTimeHours}h ramp</span>
													{/if}
													{#if step.triggerGravity}
														<span>• SG &le; {step.triggerGravity}</span>
													{/if}
													{#if step.notes}
														<span>• {step.notes}</span>
													{/if}
												</div>
											</div>
										</label>

										<div class="flex items-center gap-3 self-end sm:self-center">
											<span class="font-mono text-sm font-bold text-cyan-600 dark:text-cyan-400">
												{settings.formatTemperature(step.targetTemperatureC)}
											</span>
											{#if step.isCompleted}
												<span class="text-xs font-medium text-emerald-600 dark:text-emerald-400">
													{t('batches.workspace.step_completed')}
												</span>
											{/if}
										</div>
									</div>
								{/each}
							</div>
						</div>
					{/if}

					<!-- Stage Ingredients Checklist (e.g. yeast pitch, dry hops) -->
					<div class="glass-panel rounded-3xl border border-zinc-200/80 p-6 dark:border-white/10">
						<div
							class="flex flex-wrap items-center justify-between gap-3 border-b border-zinc-200/60 pb-4 dark:border-white/5"
						>
							<div class="flex items-center gap-2 text-zinc-900 dark:text-white">
								<CheckCircle2 class="h-5 w-5 text-emerald-500" />
								<h2 class="text-base font-bold">
									{filterCurrentStageOnly
										? getStageChecklistTitle(batch.currentStage)
										: t('batches.workspace.checklist_title')}
								</h2>
							</div>
							<div class="flex items-center gap-3">
								<button
									type="button"
									onclick={() => (filterCurrentStageOnly = !filterCurrentStageOnly)}
									class="flex min-h-[32px] items-center gap-1.5 rounded-lg border border-zinc-200/80 px-2.5 py-1 text-xs font-medium text-zinc-600 transition-colors hover:bg-zinc-100 dark:border-white/10 dark:text-zinc-400 dark:hover:bg-zinc-800"
								>
									<Filter class="h-3.5 w-3.5" />
									<span>
										{filterCurrentStageOnly
											? t('batches.workspace.stage_filter_current')
											: t('batches.workspace.stage_filter_all')}
									</span>
								</button>
								<span class="font-mono text-xs text-zinc-500">
									{activeStageIngredients.filter((i) => i.isChecked).length} / {activeStageIngredients.length}
								</span>
							</div>
						</div>

						{#if activeStageIngredients.length === 0}
							<p class="py-6 text-center text-xs text-zinc-400">
								{filterCurrentStageOnly
									? t('batches.workspace.stage_ingredients_empty')
									: t('batches.workspace.checklist_empty')}
							</p>
						{:else}
							<ul class="mt-4 divide-y divide-zinc-200/40 dark:divide-white/5">
								{#each activeStageIngredients as ing (ing.id)}
									<li class="flex items-center justify-between py-3">
										<label class="flex cursor-pointer items-center gap-3">
											<input
												type="checkbox"
												checked={ing.isChecked}
												onchange={() => handleToggleIngredient(ing.id, ing.isChecked)}
												class="h-4 w-4 rounded-md border-zinc-300 text-amber-500 focus:ring-amber-500/30 dark:border-zinc-700 dark:bg-zinc-900"
											/>
											<div class="space-y-0.5">
												<span
													class="text-sm font-semibold {ing.isChecked
														? 'text-zinc-400 line-through dark:text-zinc-500'
														: 'text-zinc-900 dark:text-zinc-100'}"
												>
													{ing.name}
												</span>
												<span class="block text-xs text-zinc-500">
													{ing.type} • {ing.additionStage}
													{#if ing.additionTimeMinutes}
														({ing.additionTimeMinutes}m)
													{/if}
												</span>
											</div>
										</label>

										<span class="font-mono text-xs font-bold text-zinc-700 dark:text-zinc-300">
											{ing.amount}
											{ing.unit}
										</span>
									</li>
								{/each}
							</ul>
						{/if}
					</div>

					<!-- Ferment / Condition Stage Measurements & Manual Inputs Card -->
					<StageMeasurementsCard
						stage={inspectedStage}
						{batch}
						{equipmentList}
						onSave={handleSaveStageMeasurements}
					/>
				</div>
			{:else if inspectedStage === 'Package'}
				<!-- Packaging Panel -->
				<div class="space-y-6">
					<div
						class="glass-panel rounded-3xl border border-zinc-200/80 p-6 text-center dark:border-white/10"
					>
						<PackageCheck class="mx-auto h-12 w-12 text-emerald-500" />
						<h2 class="mt-4 text-lg font-bold text-zinc-900 dark:text-white">
							{t('batches.workspace.packaging_title')}
						</h2>
						<p class="mx-auto mt-1 max-w-md text-xs text-zinc-500">
							{t('batches.workspace.packaging_desc')}
						</p>

						{#if batch.status !== 'Completed' && inspectedStage === batch.currentStage}
							<button
								type="button"
								onclick={openAdvanceModal}
								class="mt-6 inline-flex min-h-[44px] items-center gap-2 rounded-xl bg-gradient-to-r from-emerald-500 to-emerald-600 px-6 py-2.5 text-sm font-bold text-white shadow-md transition-all hover:from-emerald-400 hover:to-emerald-500"
							>
								<CheckCircle2 class="h-4 w-4 stroke-[2.5]" />
								<span>{t('batches.workspace.complete_package_button')}</span>
							</button>
						{:else}
							<div
								class="mt-6 inline-flex items-center gap-2 rounded-xl border border-emerald-500/30 bg-emerald-500/10 px-4 py-2 text-xs font-bold text-emerald-600 dark:text-emerald-400"
							>
								<CheckCircle2 class="h-4 w-4" />
								<span>{t('batches.status_completed')}</span>
							</div>
						{/if}
					</div>

					<!-- Stage Ingredients Checklist (e.g. priming sugar, bottling yeast) -->
					<div class="glass-panel rounded-3xl border border-zinc-200/80 p-6 dark:border-white/10">
						<div
							class="flex flex-wrap items-center justify-between gap-3 border-b border-zinc-200/60 pb-4 dark:border-white/5"
						>
							<div class="flex items-center gap-2 text-zinc-900 dark:text-white">
								<CheckCircle2 class="h-5 w-5 text-emerald-500" />
								<h2 class="text-base font-bold">
									{filterCurrentStageOnly
										? getStageChecklistTitle('Package')
										: t('batches.workspace.checklist_title')}
								</h2>
							</div>
							<div class="flex items-center gap-3">
								<button
									type="button"
									onclick={() => (filterCurrentStageOnly = !filterCurrentStageOnly)}
									class="flex min-h-[32px] items-center gap-1.5 rounded-lg border border-zinc-200/80 px-2.5 py-1 text-xs font-medium text-zinc-600 transition-colors hover:bg-zinc-100 dark:border-white/10 dark:text-zinc-400 dark:hover:bg-zinc-800"
								>
									<Filter class="h-3.5 w-3.5" />
									<span>
										{filterCurrentStageOnly
											? t('batches.workspace.stage_filter_current')
											: t('batches.workspace.stage_filter_all')}
									</span>
								</button>
								<span class="font-mono text-xs text-zinc-500">
									{activeStageIngredients.filter((i) => i.isChecked).length} / {activeStageIngredients.length}
								</span>
							</div>
						</div>

						{#if activeStageIngredients.length === 0}
							<p class="py-6 text-center text-xs text-zinc-400">
								{filterCurrentStageOnly
									? t('batches.workspace.stage_ingredients_empty')
									: t('batches.workspace.checklist_empty')}
							</p>
						{:else}
							<ul class="mt-4 divide-y divide-zinc-200/40 dark:divide-white/5">
								{#each activeStageIngredients as ing (ing.id)}
									<li class="flex items-center justify-between py-3">
										<label class="flex cursor-pointer items-center gap-3">
											<input
												type="checkbox"
												checked={ing.isChecked}
												onchange={() => handleToggleIngredient(ing.id, ing.isChecked)}
												class="h-4 w-4 rounded-md border-zinc-300 text-amber-500 focus:ring-amber-500/30 dark:border-zinc-700 dark:bg-zinc-900"
											/>
											<div class="space-y-0.5">
												<span
													class="text-sm font-semibold {ing.isChecked
														? 'text-zinc-400 line-through dark:text-zinc-500'
														: 'text-zinc-900 dark:text-zinc-100'}"
												>
													{ing.name}
												</span>
												<span class="block text-xs text-zinc-500">
													{ing.type} • {ing.additionStage}
													{#if ing.additionTimeMinutes}
														({ing.additionTimeMinutes}m)
													{/if}
												</span>
											</div>
										</label>

										<span class="font-mono text-xs font-bold text-zinc-700 dark:text-zinc-300">
											{ing.amount}
											{ing.unit}
										</span>
									</li>
								{/each}
							</ul>
						{/if}
					</div>

					<!-- Package Stage Measurements & Manual Inputs Card -->
					<StageMeasurementsCard
						stage="Package"
						{batch}
						{equipmentList}
						onSave={handleSaveStageMeasurements}
					/>
				</div>
			{/if}
		</div>
	{/if}
</div>

<!-- Advance Stage Modal -->
{#if showAdvanceModal && batch}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-xs"
		role="dialog"
		aria-modal="true"
	>
		<div
			class="glass-panel relative w-full max-w-lg rounded-3xl border border-zinc-200/80 bg-white p-6 shadow-2xl dark:border-white/10 dark:bg-zinc-950"
		>
			<div
				class="flex items-center justify-between border-b border-zinc-200/60 pb-4 dark:border-white/5"
			>
				<div>
					<h2 class="text-lg font-bold text-zinc-900 dark:text-white">
						{t('batches.workspace.advance_modal_title', { stage: nextStage })}
					</h2>
					<p class="text-xs text-zinc-500">
						{t('batches.workspace.advance_modal_desc')}
					</p>
				</div>
				<button
					type="button"
					onclick={() => (showAdvanceModal = false)}
					aria-label={t('common.close_dialog')}
					class="rounded-lg p-1.5 text-zinc-400 hover:bg-zinc-100 hover:text-zinc-600 dark:hover:bg-zinc-800"
				>
					<X class="h-4 w-4" />
				</button>
			</div>

			{#if advanceError}
				<div
					class="mt-4 rounded-xl border border-red-200 bg-red-50 p-3 text-xs text-red-700 dark:border-red-900/40 dark:bg-red-950/30 dark:text-red-300"
				>
					{advanceError}
				</div>
			{/if}

			<div class="mt-4 space-y-4 text-xs">
				<!-- Stage Target Selector -->
				<div>
					<label
						for="target-stage-select"
						class="block font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('batches.workspace.stage_destination_label')}
					</label>
					<select
						id="target-stage-select"
						bind:value={nextStage}
						class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
					>
						<option value="Boil">{t('batches.stages.boil')}</option>
						<option value="Ferment">{t('batches.stages.ferment')}</option>
						<option value="Condition">{t('batches.stages.condition')}</option>
						<option value="Package">{t('batches.stages.package')}</option>
					</select>
				</div>

				<!-- Upcoming Additions Preview for Target Stage -->
				<div
					class="rounded-2xl border border-amber-500/20 bg-amber-500/5 p-3.5 dark:border-amber-500/10 dark:bg-amber-500/5"
				>
					<div class="flex items-center justify-between">
						<span class="font-semibold text-amber-900 dark:text-amber-300">
							{t('batches.workspace.advance_upcoming_ingredients', {
								stage: t(`batches.stages.${nextStage.toLowerCase()}`)
							})}
						</span>
						<span class="font-mono text-xs text-amber-700 dark:text-amber-400">
							{advanceUpcomingIngredients.length}
						</span>
					</div>
					{#if advanceUpcomingIngredients.length === 0}
						<p class="mt-1.5 text-xs text-zinc-500 dark:text-zinc-400">
							{t('batches.workspace.advance_no_ingredients', {
								stage: t(`batches.stages.${nextStage.toLowerCase()}`)
							})}
						</p>
					{:else}
						<ul class="mt-2 divide-y divide-amber-500/10 dark:divide-amber-500/10">
							{#each advanceUpcomingIngredients as ing (ing.id)}
								<li class="flex items-center justify-between py-1.5 text-xs">
									<span class="font-medium text-zinc-800 dark:text-zinc-200">
										{ing.name}
										<span class="text-[11px] text-zinc-500 dark:text-zinc-400">
											({ing.type}{#if ing.additionTimeMinutes}
												• {ing.additionTimeMinutes}m{/if})
										</span>
									</span>
									<span class="font-mono font-semibold text-zinc-900 dark:text-zinc-100">
										{ing.amount}
										{ing.unit}
									</span>
								</li>
							{/each}
						</ul>
					{/if}
				</div>

				{#if nextStage === 'Boil'}
					<!-- Pre-Boil Volume & Gravity -->
					<div class="grid grid-cols-2 gap-3">
						<div class="flex flex-col">
							<div class="flex min-h-[1.75rem] items-end">
								<label
									for="advance-preboil-vol-input"
									class="block font-semibold text-zinc-700 dark:text-zinc-300"
								>
									{t('batches.workspace.pre_boil_vol_label')}
								</label>
							</div>
							<input
								id="advance-preboil-vol-input"
								type="number"
								step="0.1"
								bind:value={advancePreBoilVolume}
								class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
							/>
							{#if batch?.volumeProfile?.targetPreBoilVolumeLiters}
								<div class="mt-1.5 flex items-center justify-between text-[11px]">
									<span class="text-zinc-500 dark:text-zinc-400">
										{t('batches.workspace.target_from_equipment', {
											target: `${formatNumber(batch.volumeProfile.targetPreBoilVolumeLiters, 1)}L`
										})}
									</span>
									<button
										type="button"
										onclick={() =>
											(advancePreBoilVolume =
												batch?.volumeProfile?.targetPreBoilVolumeLiters ?? null)}
										class="font-medium text-amber-600 hover:underline dark:text-amber-400"
									>
										{t('batches.workspace.reset_to_target')}
									</button>
								</div>
							{/if}
						</div>
						<div class="flex flex-col">
							<div class="flex min-h-[1.75rem] items-end">
								<label
									for="advance-preboil-grav-input"
									class="block font-semibold text-zinc-700 dark:text-zinc-300"
								>
									{t('batches.workspace.pre_boil_grav_label')}
								</label>
							</div>
							<input
								id="advance-preboil-grav-input"
								type="number"
								step="0.001"
								bind:value={advancePreBoilGravity}
								placeholder="e.g. 1.045"
								class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
							/>
						</div>
					</div>
				{/if}

				{#if nextStage === 'Ferment'}
					<!-- Post-Boil Volume & Measured Volume into Fermenter & Measured OG & Pitch Temp -->
					<div class="space-y-3">
						<div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
							<div class="flex flex-col">
								<div class="flex min-h-[1.75rem] items-end">
									<label
										for="advance-postboil-vol-input"
										class="block font-semibold text-zinc-700 dark:text-zinc-300"
									>
										{t('batches.workspace.post_boil_vol_label')}
									</label>
								</div>
								<input
									id="advance-postboil-vol-input"
									type="number"
									step="0.1"
									bind:value={advancePostBoilVolume}
									class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
								/>
								{#if batch?.volumeProfile?.targetPostBoilVolumeLiters}
									<div class="mt-1.5 flex items-center justify-between text-[11px]">
										<span class="text-zinc-500 dark:text-zinc-400">
											{t('batches.workspace.target_from_equipment', {
												target: `${formatNumber(batch.volumeProfile.targetPostBoilVolumeLiters, 1)}L`
											})}
										</span>
										<button
											type="button"
											onclick={() =>
												(advancePostBoilVolume =
													batch?.volumeProfile?.targetPostBoilVolumeLiters ?? null)}
											class="font-medium text-amber-600 hover:underline dark:text-amber-400"
										>
											{t('batches.workspace.reset_to_target')}
										</button>
									</div>
								{/if}
							</div>

							<div class="flex flex-col">
								<div class="flex min-h-[1.75rem] items-end">
									<label
										for="advance-volume-input"
										class="block font-semibold text-zinc-700 dark:text-zinc-300"
									>
										{t('batches.workspace.measured_volume_label')}
									</label>
								</div>
								<input
									id="advance-volume-input"
									type="number"
									step="0.1"
									bind:value={advanceVolume}
									class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
								/>
								{#if batch?.volumeProfile?.targetFermenterVolumeLiters || batch?.targetBatchSizeLiters}
									{@const targetVol =
										batch.volumeProfile?.targetFermenterVolumeLiters ?? batch.targetBatchSizeLiters}
									<div class="mt-1.5 flex items-center justify-between text-[11px]">
										<span class="text-zinc-500 dark:text-zinc-400">
											{t('batches.workspace.target_from_equipment', {
												target: `${formatNumber(targetVol, 1)}L`
											})}
										</span>
										<button
											type="button"
											onclick={() => (advanceVolume = targetVol)}
											class="font-medium text-amber-600 hover:underline dark:text-amber-400"
										>
											{t('batches.workspace.reset_to_target')}
										</button>
									</div>
								{/if}
							</div>
						</div>

						<div class="grid grid-cols-2 gap-3">
							<div class="flex flex-col">
								<div class="flex min-h-[1.75rem] items-end">
									<label
										for="advance-measured-og-input"
										class="block font-semibold text-zinc-700 dark:text-zinc-300"
									>
										{t('batches.workspace.measured_og_label')}
									</label>
								</div>
								<input
									id="advance-measured-og-input"
									type="number"
									step="0.001"
									bind:value={advanceMeasuredOg}
									class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
								/>
								{#if batch?.targetOg}
									<div class="mt-1.5 flex items-center justify-between text-[11px]">
										<span class="text-zinc-500 dark:text-zinc-400">
											{t('batches.workspace.target_from_equipment', {
												target: `${formatNumber(batch.targetOg, 3)} SG`
											})}
										</span>
										<button
											type="button"
											onclick={() => (advanceMeasuredOg = batch?.targetOg ?? null)}
											class="font-medium text-amber-600 hover:underline dark:text-amber-400"
										>
											{t('batches.workspace.reset_to_target')}
										</button>
									</div>
								{/if}
							</div>
							<div class="flex flex-col">
								<div class="flex min-h-[1.75rem] items-end">
									<label
										for="advance-pitch-temp-input"
										class="block font-semibold text-zinc-700 dark:text-zinc-300"
									>
										{t('batches.workspace.pitch_temp_label')}
									</label>
								</div>
								<input
									id="advance-pitch-temp-input"
									type="number"
									step="0.5"
									bind:value={advancePitchTemp}
									class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
								/>
								{#if batch?.pitchTemperatureC}
									<div class="mt-1.5 flex items-center justify-between text-[11px]">
										<span class="text-zinc-500 dark:text-zinc-400">
											{t('batches.workspace.target_from_equipment', {
												target: `${formatNumber(batch.pitchTemperatureC, 1)}°C`
											})}
										</span>
										<button
											type="button"
											onclick={() => (advancePitchTemp = batch?.pitchTemperatureC ?? null)}
											class="font-medium text-amber-600 hover:underline dark:text-amber-400"
										>
											{t('batches.workspace.reset_to_target')}
										</button>
									</div>
								{/if}
							</div>
						</div>
					</div>
				{/if}

				{#if nextStage === 'Package'}
					<!-- Measured FG & Packaging Vessel -->
					<div class="space-y-3">
						<div class="grid grid-cols-2 gap-3">
							<div class="flex flex-col">
								<div class="flex min-h-[1.75rem] items-end">
									<label
										for="advance-measured-fg-input"
										class="block font-semibold text-zinc-700 dark:text-zinc-300"
									>
										{t('batches.workspace.measured_fg_label')}
									</label>
								</div>
								<input
									id="advance-measured-fg-input"
									type="number"
									step="0.001"
									bind:value={advanceMeasuredFg}
									class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
								/>
								{#if batch?.targetFg}
									<div class="mt-1.5 flex items-center justify-between text-[11px]">
										<span class="text-zinc-500 dark:text-zinc-400">
											{t('batches.workspace.target_from_equipment', {
												target: `${formatNumber(batch.targetFg, 3)} SG`
											})}
										</span>
										<button
											type="button"
											onclick={() => (advanceMeasuredFg = batch?.targetFg ?? null)}
											class="font-medium text-amber-600 hover:underline dark:text-amber-400"
										>
											{t('batches.workspace.reset_to_target')}
										</button>
									</div>
								{/if}
							</div>
							<div class="flex flex-col">
								<div class="flex min-h-[1.75rem] items-end">
									<label
										for="advance-packaged-vol-input"
										class="block font-semibold text-zinc-700 dark:text-zinc-300"
									>
										{t('batches.workspace.packaged_vol_label')}
									</label>
								</div>
								<input
									id="advance-packaged-vol-input"
									type="number"
									step="0.1"
									bind:value={advancePackagedVolume}
									class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
								/>
								{#if batch?.volumeProfile?.targetPackagedVolumeLiters}
									<div class="mt-1.5 flex items-center justify-between text-[11px]">
										<span class="text-zinc-500 dark:text-zinc-400">
											{t('batches.workspace.target_from_equipment', {
												target: `${formatNumber(batch.volumeProfile.targetPackagedVolumeLiters, 1)}L`
											})}
										</span>
										<button
											type="button"
											onclick={() =>
												(advancePackagedVolume =
													batch?.volumeProfile?.targetPackagedVolumeLiters ?? null)}
											class="font-medium text-amber-600 hover:underline dark:text-amber-400"
										>
											{t('batches.workspace.reset_to_target')}
										</button>
									</div>
								{/if}
							</div>
						</div>

						<div>
							<label
								for="advance-packaging-vessel-select"
								class="block font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('batches.equipment.select_packaging_vessel')}
							</label>
							<select
								id="advance-packaging-vessel-select"
								bind:value={advancePackagingVesselId}
								class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
							>
								<option value="">{t('batches.workspace.bottled_cellared_option')}</option>
								{#each equipmentList.filter((e) => e.type === 'Keg') as keg}
									<option value={keg.id}>{keg.name} ({keg.capacity}L)</option>
								{/each}
							</select>
						</div>
					</div>
				{/if}

				<!-- Notes -->
				<div>
					<label
						for="advance-stage-notes"
						class="block font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('batches.workspace.stage_transition_notes_label')}
					</label>
					<textarea
						id="advance-stage-notes"
						rows="2"
						bind:value={advanceNotes}
						placeholder={t('batches.workspace.advance_stage_notes_placeholder')}
						class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white p-2.5 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
					></textarea>
				</div>
			</div>

			<div
				class="mt-6 flex items-center justify-end gap-3 border-t border-zinc-200/60 pt-4 dark:border-white/5"
			>
				<button
					type="button"
					onclick={() => (showAdvanceModal = false)}
					class="min-h-[40px] rounded-xl border border-zinc-200 px-4 py-2 text-xs font-semibold text-zinc-600 transition-colors hover:bg-zinc-100 dark:border-zinc-800 dark:text-zinc-400"
				>
					{t('brewery.cancel')}
				</button>

				<button
					type="button"
					data-testid="confirm-advance-stage-btn"
					onclick={handleAdvanceSubmit}
					disabled={advancing}
					class="flex min-h-[40px] items-center gap-2 rounded-xl bg-amber-500 px-5 py-2 text-xs font-bold text-zinc-950 shadow-md transition-all hover:bg-amber-400 disabled:opacity-50"
				>
					{#if advancing}
						<Loader2 class="h-3.5 w-3.5 animate-spin" />
						<span>{t('batches.workspace.advancing')}</span>
					{:else}
						<CheckCircle2 class="h-3.5 w-3.5 stroke-[2.5]" />
						<span>{t('batches.workspace.confirm_advance')}</span>
					{/if}
				</button>
			</div>
		</div>
	</div>
{/if}

<!-- Add Reading Modal -->
{#if showReadingModal && batch}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-xs"
		role="dialog"
		aria-modal="true"
	>
		<div
			class="glass-panel relative w-full max-w-md rounded-3xl border border-zinc-200/80 bg-white p-6 shadow-2xl dark:border-white/10 dark:bg-zinc-950"
		>
			<div
				class="flex items-center justify-between border-b border-zinc-200/60 pb-4 dark:border-white/5"
			>
				<div>
					<h2 class="text-lg font-bold text-zinc-900 dark:text-white">
						{t('batches.workspace.log_reading_modal_title')}
					</h2>
				</div>
				<button
					type="button"
					onclick={() => (showReadingModal = false)}
					aria-label={t('common.close_dialog')}
					class="rounded-lg p-1.5 text-zinc-400 hover:bg-zinc-100 hover:text-zinc-600 dark:hover:bg-zinc-800"
				>
					<X class="h-4 w-4" />
				</button>
			</div>

			{#if readingError}
				<div
					class="mt-4 rounded-xl border border-red-200 bg-red-50 p-3 text-xs text-red-700 dark:border-red-900/40 dark:bg-red-950/30 dark:text-red-300"
				>
					{readingError}
				</div>
			{/if}

			<div class="mt-4 space-y-4 text-xs">
				<div>
					<label
						for="reading-sg-input"
						class="block font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('batches.workspace.reading_sg_label')}
					</label>
					<input
						id="reading-sg-input"
						type="number"
						step="0.001"
						bind:value={newReadingSg}
						class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
					/>
				</div>

				<div>
					<label
						for="reading-notes-textarea"
						class="block font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('batches.workspace.reading_notes_label')}
					</label>
					<textarea
						id="reading-notes-textarea"
						rows="3"
						bind:value={newReadingNotes}
						placeholder={t('batches.workspace.reading_notes_placeholder')}
						class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white p-2.5 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
					></textarea>
				</div>
			</div>

			<div
				class="mt-6 flex items-center justify-end gap-3 border-t border-zinc-200/60 pt-4 dark:border-white/5"
			>
				<button
					type="button"
					onclick={() => (showReadingModal = false)}
					class="min-h-[40px] rounded-xl border border-zinc-200 px-4 py-2 text-xs font-semibold text-zinc-600 transition-colors hover:bg-zinc-100 dark:border-zinc-800 dark:text-zinc-400"
				>
					{t('brewery.cancel')}
				</button>

				<button
					type="button"
					data-testid="submit-reading-btn"
					onclick={handleReadingSubmit}
					disabled={savingReading}
					class="flex min-h-[40px] items-center gap-2 rounded-xl bg-amber-500 px-5 py-2 text-xs font-bold text-zinc-950 shadow-md transition-all hover:bg-amber-400 disabled:opacity-50"
				>
					{#if savingReading}
						<Loader2 class="h-3.5 w-3.5 animate-spin" />
						<span>{t('batches.workspace.saving_reading')}</span>
					{:else}
						<CheckCircle2 class="h-3.5 w-3.5 stroke-[2.5]" />
						<span>{t('batches.workspace.save_reading')}</span>
					{/if}
				</button>
			</div>
		</div>
	</div>
{/if}

<LogStepTemperatureModal
	isOpen={showLogTempModal}
	stage={logTempStage}
	{equipmentList}
	defaultEquipmentId={currentStageEquipmentId}
	equipmentName={currentStageEquipmentName}
	mashSteps={batch?.mashSteps ?? []}
	defaultMashStepId={logTempMashStepId}
	targetTemperatureC={logTempStage === 'Mash'
		? (batch?.mashSteps?.find((s) => s.id === logTempMashStepId)?.targetTemperatureC ?? 65)
		: logTempStage === 'Boil'
			? 100
			: logTempStage === 'Ferment'
				? (batch?.pitchTemperatureC ?? 20)
				: 2}
	onClose={() => (showLogTempModal = false)}
	onSubmit={handleLogStepTemperature}
/>

<DeleteBatchModal
	open={showDeleteBatchModal}
	{batch}
	onClose={() => (showDeleteBatchModal = false)}
	onDeleted={() => goto('/batches')}
/>

{#if batch}
	<BrewStationHudModal
		open={showBrewStationHud}
		batchName={batch.name}
		beerStyle={batch.beerStyle}
		currentStage={batch.currentStage}
		{timerRemainingSeconds}
		{timerRunning}
		targetTemperatureC={batch.currentStage === 'Mash'
			? (currentSelectedMashStep?.targetTemperatureC ?? 65)
			: batch.currentStage === 'Boil'
				? 100
				: batch.currentStage === 'Ferment'
					? activeFermentationStepTargetTemp
					: null}
		currentTemperatureC={batchEquipmentReadings.length > 0
			? batchEquipmentReadings[batchEquipmentReadings.length - 1].temperatureC
			: (currentSelectedMashStep?.actualTemperatureC ?? null)}
		activeStepName={batch.currentStage === 'Mash'
			? currentSelectedMashStep?.name
			: batch.currentStage === 'Boil'
				? `Boil (${batch.boilTimeMinutes ?? 60}m)`
				: batch.currentStage === 'Ferment'
					? activeFermentationStep?.name
					: null}
		{activeMashStepIndex}
		totalMashSteps={batch.mashSteps?.length}
		onToggleTimer={() => {
			if (timerRunning) {
				pauseTimer();
			} else {
				startTimer();
			}
		}}
		onResetTimer={resetTimer}
		onAdjustTimer={(seconds) => {
			timerRemainingSeconds = Math.max(0, timerRemainingSeconds + seconds);
		}}
		onAdvanceStep={batch.currentStage === 'Mash' ? handleAdvanceMashStep : undefined}
		onClose={() => (showBrewStationHud = false)}
	/>
{/if}
