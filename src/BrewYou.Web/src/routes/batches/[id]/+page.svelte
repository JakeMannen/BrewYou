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
		BatchBoilStep,
		LogBatchTemperatureRequest
	} from '$lib/types/api';
	import ConfirmModal from '$lib/components/ui/ConfirmModal.svelte';
	import SrmSwatch from '$lib/components/brewery/SrmSwatch.svelte';
	import BrewStageTracker, {
		type BrewStage as UiBrewStage
	} from '$lib/components/brewery/BrewStageTracker.svelte';
	import AttenuationChart from '$lib/components/batches/AttenuationChart.svelte';
	import StageMeasurementsCard from '$lib/components/batches/StageMeasurementsCard.svelte';
	import StepTemperatureChart from '$lib/components/batches/StepTemperatureChart.svelte';
	import LogStepTemperatureModal from '$lib/components/batches/LogStepTemperatureModal.svelte';
	import DeleteBatchModal from '$lib/components/batches/DeleteBatchModal.svelte';
	import BatchQrModal from '$lib/components/batches/BatchQrModal.svelte';
	import BrewStationHudModal from '$lib/components/batches/BrewStationHudModal.svelte';
	import BrewLinkInteractionModal from '$lib/components/brewery/BrewLinkInteractionModal.svelte';
	import BrewLinkControlPanel from '$lib/components/brewery/BrewLinkControlPanel.svelte';
	import StageProgressBar from '$lib/components/batches/StageProgressBar.svelte';
	import { extractInteraction } from '$lib/utils/brewlink';
	import { batchTimer } from '$lib/stores/batch-timer.svelte';
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
		Droplets,
		StepForward,
		Clock,
		Activity,
		Maximize2,
		QrCode,
		Printer,
		Radio,
		ChevronDown,
		ChevronUp
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
	let showQrModal = $state(false);
	let showBrewStationHud = $state(false);
	let logTempStage = $state<BrewStage>('Mash');
	let logTempMashStepId = $state<string | null>(null);
	let logTempBoilStepName = $state<string | null>(null);
	let selectedMashStepGraphId = $state<string | null>(null);
	let selectedBoilStepGraphName = $state<string | null>(null);
	let showConfirmPauseTimerModal = $state(false);
	let showConfirmResetTimerModal = $state(false);
	let showConfirmAdvanceBoilStepModal = $state(false);
	let showConfirmAdvanceMashStepModal = $state(false);
	let showConfirmAdvanceActiveBoilModal = $state(false);
	let hasConfirmedActiveBoil = $state(false);
	let showBrewLinkPromptModal = $state(false);
	let brewLinkPromptSubmitting = $state(false);
	let dismissedPromptKey = $state<string | null>(null);
	let isAutoAdvancingToBoil = $state(false);
	let mashTimerHadRun = $state(false);
	let brewLinkSyncTimer: ReturnType<typeof setInterval> | null = null;
	let loading = $state(true);
	let error = $state<string | null>(null);

	function openLogTempModal(targetStage: BrewStage, stepIdentifier?: string | null) {
		logTempStage = targetStage;
		if (targetStage === 'Mash') {
			const resolvedStepId =
				stepIdentifier ?? selectedMashStepGraphId ?? currentSelectedMashStep?.id ?? null;
			logTempMashStepId = resolvedStepId;
			if (resolvedStepId && batch?.mashSteps) {
				selectedMashStepGraphId = resolvedStepId;
				const stepIdx = batch.mashSteps.findIndex((s) => s.id === resolvedStepId);
				if (stepIdx !== -1) {
					activeMashStepIndex = stepIdx;
				}
			}
		} else if (targetStage === 'Boil') {
			const resolvedName =
				stepIdentifier ?? selectedBoilStepGraphName ?? currentSelectedBoilStep?.name ?? null;
			logTempBoilStepName = resolvedName;
			if (resolvedName && boilSteps.length > 0) {
				selectedBoilStepGraphName = resolvedName;
				const stepIdx = boilSteps.findIndex((s) => s.name === resolvedName);
				if (stepIdx !== -1) {
					activeBoilStepIndex = stepIdx;
				}
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
	let isReadOnlyBatch = $derived(
		batch ? batch.status === 'Completed' || batch.status === 'Archived' : false
	);
	let isInspectingActiveStage = $derived(batch ? inspectedStage === batch.currentStage : true);
	let canLogInspectedStage = $derived(isInspectingActiveStage && !isReadOnlyBatch);
	let inspectedStageStatus = $derived.by<'completed' | 'in_progress' | 'pending' | 'skipped'>(
		() => {
			if (!batch) return 'pending';
			const hist = batch.stageHistory.find((h) => h.stage === inspectedStage);
			if (hist?.status === 'Skipped') return 'skipped';
			if (batch.status === 'Completed' || hist?.status === 'Completed') return 'completed';
			const stageOrder: BrewStage[] = ['Mash', 'Boil', 'Ferment', 'Condition', 'Package'];
			const currentIndex = stageOrder.indexOf(batch.currentStage);
			const targetIndex = stageOrder.indexOf(inspectedStage);
			if (targetIndex < currentIndex) return 'completed';
			if (targetIndex === currentIndex) return 'in_progress';
			return 'pending';
		}
	);

	let forwardStageOptions = $derived.by<BrewStage[]>(() => {
		if (!batch) return ['Boil', 'Ferment', 'Condition', 'Package'];
		if (batch.currentStage === 'Package') return ['Package'];
		const stageOrder: BrewStage[] = ['Mash', 'Boil', 'Ferment', 'Condition', 'Package'];
		const currentIndex = stageOrder.indexOf(batch.currentStage);
		return currentIndex >= 0 ? stageOrder.slice(currentIndex + 1) : stageOrder.slice(1);
	});

	async function handleSaveStageMeasurements(req: UpdateBatchRequest) {
		if (!batch) return;
		const [updated, eq] = await Promise.all([
			api.batches.update(batch.id, req),
			api.equipment.list().catch(() => equipmentList)
		]);
		batch = updated;
		equipmentList = eq;
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

	let assignedHlt = $derived.by(() => {
		const b = batch;
		if (!b?.hltId) return null;
		return equipmentList.find((e) => e.id === b.hltId) ?? null;
	});

	let activeStageBrewLinkEquipment = $derived.by<EquipmentDto | null>(() => {
		if (!batch || equipmentList.length === 0) return null;
		let targetId: string | null = null;
		if (inspectedStage === 'Mash') {
			targetId = batch.mashTunId ?? batch.boilerId ?? null;
		} else if (inspectedStage === 'Boil') {
			targetId = batch.boilerId ?? null;
		} else if (inspectedStage === 'Ferment' || inspectedStage === 'Condition') {
			targetId = batch.fermenterId ?? null;
		}
		if (!targetId) return null;
		const eq = equipmentList.find((e) => e.id === targetId);
		return eq?.connectionType === 'BrewLink' ? eq : null;
	});

	let activeBrewLinkPrompt = $derived.by<{
		equipment: EquipmentDto;
		message: string;
		code?: number;
	} | null>(() => {
		if (!batch || equipmentList.length === 0) return null;

		// Prioritize inspected stage equipment if available
		const priorityEq = activeStageBrewLinkEquipment;
		if (priorityEq?.latestMetricsJson) {
			const parsed = extractInteraction(priorityEq.latestMetricsJson);
			if (parsed.required) {
				return {
					equipment: priorityEq,
					message: parsed.message || t('equipment.brewlink_interaction_alert'),
					code: parsed.code
				};
			}
		}

		// Also check any other equipment assigned to this batch
		const candidateIds = [batch.boilerId, batch.mashTunId, batch.hltId, batch.fermenterId].filter(
			(id): id is string => Boolean(id) && id !== priorityEq?.id
		);

		for (const id of candidateIds) {
			const eq = equipmentList.find((e) => e.id === id && e.connectionType === 'BrewLink');
			if (eq?.latestMetricsJson) {
				const parsed = extractInteraction(eq.latestMetricsJson);
				if (parsed.required) {
					return {
						equipment: eq,
						message: parsed.message || t('equipment.brewlink_interaction_alert'),
						code: parsed.code
					};
				}
			}
		}
		return null;
	});

	$effect(() => {
		if (activeBrewLinkPrompt) {
			const promptKey = `${activeBrewLinkPrompt.equipment.id}:${activeBrewLinkPrompt.code ?? activeBrewLinkPrompt.message}`;
			if (dismissedPromptKey !== promptKey) {
				showBrewLinkPromptModal = true;
			}
		} else {
			// If confirmed or dismissed from the BrewLink hardware controller,
			// activeBrewLinkPrompt becomes null; automatically handle and close the dialog!
			if (showBrewLinkPromptModal) {
				showBrewLinkPromptModal = false;
				dismissedPromptKey = null;
			}
		}
	});

	let showBatchBrewLinkControls = $state(false);

	function handleEquipmentUpdate(updated: EquipmentDto) {
		const idx = equipmentList.findIndex((e) => e.id === updated.id);
		if (idx !== -1) {
			const next = [...equipmentList];
			next[idx] = { ...next[idx], ...updated };
			equipmentList = next;
		}
	}

	async function handleConfirmBrewLinkInteraction() {
		if (!activeBrewLinkPrompt) {
			showBrewLinkPromptModal = false;
			return;
		}
		brewLinkPromptSubmitting = true;
		try {
			const eq = activeBrewLinkPrompt.equipment;
			const msg = activeBrewLinkPrompt.message.toLowerCase();
			await api.equipment.sendBrewLinkControl(eq.id, {
				action: 'ack_interaction'
			});
			const res = await api.equipment.syncBrewLinkState(eq.id);
			if (res && (res.temperatureC != null || res.metricsJson)) {
				handleEquipmentUpdate({
					...eq,
					currentTemperatureC: res.temperatureC ?? eq.currentTemperatureC,
					latestMetricsJson: res.metricsJson ?? eq.latestMetricsJson,
					temperatureUpdatedAt: new Date().toISOString()
				});
			}

			// If the prompt was "add grain" or mash related and current mash step is pending, mark it
			if (
				(msg.includes('grain') || msg.includes('malt') || msg.includes('mash')) &&
				inspectedStage === 'Mash'
			) {
				if (currentSelectedMashStep && !currentSelectedMashStep.isCompleted) {
					void handleToggleMashStep(currentSelectedMashStep.id, false);
				}
			}

			showBrewLinkPromptModal = false;
		} catch (err: unknown) {
			console.error('Failed to confirm BrewLink interaction', err);
			showBrewLinkPromptModal = false;
		} finally {
			brewLinkPromptSubmitting = false;
		}
	}

	async function handleDismissBrewLinkInteraction() {
		if (!activeBrewLinkPrompt) {
			showBrewLinkPromptModal = false;
			return;
		}
		brewLinkPromptSubmitting = true;
		try {
			const eq = activeBrewLinkPrompt.equipment;
			const promptKey = `${eq.id}:${activeBrewLinkPrompt.code ?? activeBrewLinkPrompt.message}`;
			dismissedPromptKey = promptKey;
			await api.equipment.sendBrewLinkControl(eq.id, {
				action: 'dismiss_alert'
			});
			const res = await api.equipment.syncBrewLinkState(eq.id);
			if (res && (res.temperatureC != null || res.metricsJson)) {
				handleEquipmentUpdate({
					...eq,
					currentTemperatureC: res.temperatureC ?? eq.currentTemperatureC,
					latestMetricsJson: res.metricsJson ?? eq.latestMetricsJson,
					temperatureUpdatedAt: new Date().toISOString()
				});
			}
			showBrewLinkPromptModal = false;
		} catch (err: unknown) {
			console.error('Failed to dismiss BrewLink interaction', err);
			showBrewLinkPromptModal = false;
		} finally {
			brewLinkPromptSubmitting = false;
		}
	}

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
		if (logTempStage === 'Mash') {
			const mashVesselId = currentBatch.mashTunId ?? currentBatch.boilerId;
			if (mashVesselId) {
				const eq = equipmentList.find((e) => e.id === mashVesselId);
				return {
					id: mashVesselId,
					name: eq?.name ?? currentBatch.mashTunName ?? currentBatch.boilerName ?? 'Mash Tun'
				};
			}
		} else if (logTempStage === 'Boil') {
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

		return null;
	});

	let currentStageEquipmentId = $derived(currentStageEquipment?.id ?? null);
	let currentStageEquipmentName = $derived(currentStageEquipment?.name ?? null);

	const batchId = $derived(page.params.id);

	let streamAbortController: AbortController | null = null;
	let isTelemetryLive = $state(false);
	let fallbackPollTimer: ReturnType<typeof setInterval> | null = null;

	function handleNewTelemetryReading(reading: BatchEquipmentReadingDto) {
		// Update equipment list entry with live temperature and metrics
		if (reading.equipmentId) {
			const eqIdx = equipmentList.findIndex((e) => e.id === reading.equipmentId);
			if (eqIdx !== -1) {
				const next = [...equipmentList];
				next[eqIdx] = {
					...next[eqIdx],
					currentTemperatureC: reading.temperatureC,
					latestMetricsJson: reading.metricsJson ?? next[eqIdx].latestMetricsJson
				};
				equipmentList = next;
			}
		}

		// Deduplicate: only append if reading ID is not already present
		if (!batchEquipmentReadings.some((r) => r.id === reading.id)) {
			batchEquipmentReadings.push(reading);
		}

		// If reading carries specificGravity, update batch metrics and batch.readings
		if (reading.specificGravity !== null && reading.specificGravity !== undefined && batch) {
			batch.currentGravity = reading.specificGravity;
			recalculateBatchAbv();
			const readingTimeMs = new Date(reading.timestamp).getTime();
			const alreadyInReadings = batch.readings.some(
				(r) =>
					r.id === reading.id ||
					(r.specificGravity === reading.specificGravity &&
						Math.abs(new Date(r.timestamp).getTime() - readingTimeMs) < 2000)
			);
			if (!alreadyInReadings) {
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
		} else if (
			(reading.stage === 'Ferment' || reading.stage === 'Condition') &&
			batch?.fermentationSteps?.length
		) {
			const activeFerm =
				[...batch.fermentationSteps]
					.sort((a, b) => a.stepOrder - b.stepOrder)
					.find((s) => !s.isCompleted) ?? null;
			if (activeFerm) {
				activeFerm.actualTemperatureC = reading.temperatureC;
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

	function startBrewLinkAutoSync() {
		if (brewLinkSyncTimer || !batch || batch.status === 'Completed' || batch.status === 'Archived')
			return;
		brewLinkSyncTimer = setInterval(async () => {
			if (document.visibilityState === 'hidden' || !batch) return;
			const targetEq = activeStageBrewLinkEquipment;
			if (targetEq) {
				try {
					const res = await api.equipment.syncBrewLinkState(targetEq.id);
					if (res && (res.temperatureC != null || res.metricsJson)) {
						handleEquipmentUpdate({
							...targetEq,
							currentTemperatureC: res.temperatureC ?? targetEq.currentTemperatureC,
							latestMetricsJson: res.metricsJson ?? targetEq.latestMetricsJson,
							temperatureUpdatedAt: new Date().toISOString()
						});
					}
				} catch {
					// Gateway offline or transient error
				}
			}
		}, 3000);
	}

	function stopBrewLinkAutoSync() {
		if (brewLinkSyncTimer) {
			clearInterval(brewLinkSyncTimer);
			brewLinkSyncTimer = null;
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
					if (activeStageBrewLinkEquipment) {
						const res = await api.equipment.syncBrewLinkState(activeStageBrewLinkEquipment.id);
						if (res && (res.temperatureC != null || res.metricsJson)) {
							handleEquipmentUpdate({
								...activeStageBrewLinkEquipment,
								currentTemperatureC:
									res.temperatureC ?? activeStageBrewLinkEquipment.currentTemperatureC,
								latestMetricsJson:
									res.metricsJson ?? activeStageBrewLinkEquipment.latestMetricsJson,
								temperatureUpdatedAt: new Date().toISOString()
							});
						}
					}
				} catch {
					// Transient gap recovery error ignored
				}
				startLiveTelemetryStream();
				startBrewLinkAutoSync();
			}
		} else {
			stopLiveTelemetryStream();
			stopBrewLinkAutoSync();
		}
	}

	onMount(async () => {
		await loadBatch();
		if (batch && batch.status !== 'Completed' && batch.status !== 'Archived') {
			startLiveTelemetryStream();
			startBrewLinkAutoSync();
		}
		if (typeof document !== 'undefined') {
			document.addEventListener('visibilitychange', handleVisibilityChange);
		}
	});

	onDestroy(() => {
		stopLiveTelemetryStream();
		stopBrewLinkAutoSync();
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

		const mashVessel = equipmentList.find((e) => e.id === (batch!.mashTunId ?? batch!.boilerId));
		const boiler = equipmentList.find((e) => e.id === batch!.boilerId);
		const fermenter = equipmentList.find((e) => e.id === batch!.fermenterId);
		const pkgVessel = equipmentList.find((e) => e.id === batch!.packagingVesselId);

		return stageOrder.map((s, idx) => {
			const hist = batch!.stageHistory.find((h) => h.stage === s);
			let status: 'completed' | 'in_progress' | 'pending' | 'skipped' = 'pending';
			if (hist?.status === 'Skipped') {
				status = 'skipped';
			} else if (batch!.status === 'Completed' || hist?.status === 'Completed') {
				status = 'completed';
			} else if (idx < currentIndex) {
				status = 'completed';
			} else if (idx === currentIndex) {
				status = 'in_progress';
			}

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
				equipmentSubtype = mashVessel?.subtype ?? 'AllInOne';
				equipmentName = batch!.mashTunName ?? batch!.boilerName ?? mashVessel?.name;
				const mashVol =
					batch!.volumeProfile?.strikeWaterLiters ??
					batch!.volumeProfile?.targetPreBoilVolumeLiters ??
					batch!.targetBatchSizeLiters;
				if (mashVessel && mashVessel.capacityLiters > 0) {
					fillPercent = Math.min(100, Math.round((mashVol / mashVessel.capacityLiters) * 100));
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
		if (!batch || isReadOnlyBatch) return;
		try {
			const updated = await api.batches.toggleIngredient(batch.id, ingredientId, !current);
			const idx = batch.ingredients.findIndex((i) => i.id === ingredientId);
			if (idx !== -1) {
				batch.ingredients[idx] = updated;
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
				filtered = ingredients.filter((i) => i.additionStage === 'Mash');
				break;
			case 'Boil':
				filtered = ingredients.filter(
					(i) =>
						i.additionStage === 'FirstWort' ||
						i.additionStage === 'Boil' ||
						i.additionStage === 'Whirlpool' ||
						(i.type === 'Hop' &&
							i.additionStage !== 'DryHop' &&
							i.additionStage !== 'Mash' &&
							i.additionStage !== 'Primary' &&
							i.additionStage !== 'Secondary' &&
							i.additionStage !== 'Bottling')
				);
				break;
			case 'Ferment':
				filtered = ingredients.filter(
					(i) =>
						i.additionStage === 'Primary' ||
						i.additionStage === 'DryHop' ||
						(i.type === 'Yeast' &&
							i.additionStage !== 'Secondary' &&
							i.additionStage !== 'Bottling')
				);
				break;
			case 'Condition':
				filtered = ingredients.filter((i) => i.additionStage === 'Secondary');
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

	let boilStageIngredients = $derived.by<BatchIngredientDto[]>(() => {
		if (!batch) return [];
		return getIngredientsForStage('Boil', batch.ingredients);
	});

	let advanceUpcomingIngredients = $derived.by<BatchIngredientDto[]>(() => {
		if (!batch) return [];
		return getIngredientsForStage(nextStage, batch.ingredients);
	});

	function convertMassToKg(amount: number, unit: string): number {
		const u = unit.trim().toLowerCase();
		if (u === 'kg' || u === 'kilogram' || u === 'kilograms') return amount;
		if (u === 'g' || u === 'gram' || u === 'grams') return amount / 1000;
		if (u === 'lb' || u === 'lbs' || u === 'pound' || u === 'pounds') return amount * 0.45359237;
		if (u === 'oz' || u === 'ounce' || u === 'ounces') return amount * 0.0283495231;
		return 0;
	}

	let totalMashGrainKg = $derived.by(() => {
		if (!batch) return 0;
		return batch.ingredients
			.filter((i) => i.type === 'Fermentable' && i.additionStage === 'Mash')
			.reduce((sum, i) => sum + convertMassToKg(i.amount, i.unit), 0);
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

	let currentMashStepDurationSeconds = $derived.by<number>(() => {
		const activeStep = currentSelectedMashStep;
		return (activeStep?.durationMinutes ?? 60) * 60;
	});

	let boilDurationSeconds = $derived.by<number>(() => {
		return (batch?.boilTimeMinutes ?? 60) * 60;
	});

	let inspectedStagePlannedDurationSeconds = $derived.by<number>(() => {
		if (inspectedStage === 'Boil') return boilDurationSeconds;
		if (inspectedStage === 'Mash') return currentMashStepDurationSeconds;
		return 3600;
	});

	let inspectedTimerStepId = $derived<string | null>(
		inspectedStage === 'Mash' ? (currentSelectedMashStep?.id ?? null) : null
	);

	let activeStageBrewLinkMetrics = $derived.by(() => {
		if (!activeStageBrewLinkEquipment?.latestMetricsJson) return null;
		try {
			return JSON.parse(activeStageBrewLinkEquipment.latestMetricsJson);
		} catch {
			return null;
		}
	});

	let brewLinkTimer = $derived.by(() => {
		const m = activeStageBrewLinkMetrics;
		if (!m) return null;
		const timerObj = m.timer ?? {};
		const remaining = timerObj.remainingSeconds ?? m.timerRemainingSec ?? null;
		const total = timerObj.totalSeconds ?? m.timerTotalSec ?? null;
		const active = timerObj.active ?? m.timerActive ?? (remaining != null && remaining > 0);
		const paused = timerObj.paused ?? m.timerPaused ?? false;
		if (remaining == null && !active) return null;
		return {
			active: Boolean(active),
			paused: Boolean(paused),
			remainingSeconds: remaining ?? 0,
			totalSeconds: total ?? inspectedStagePlannedDurationSeconds
		};
	});

	// BrewLink hardware timers are synchronised for both the Mash and Boil stages
	let isBrewLinkTimerStage = $derived(inspectedStage === 'Boil' || inspectedStage === 'Mash');

	let isBrewLinkTimerActive = $derived(
		isBrewLinkTimerStage && Boolean(activeStageBrewLinkEquipment && brewLinkTimer)
	);

	let activeTimerView = $derived.by(() => {
		if (isBrewLinkTimerActive && brewLinkTimer) {
			return {
				remainingSeconds: brewLinkTimer.remainingSeconds,
				running: brewLinkTimer.active && !brewLinkTimer.paused,
				totalSeconds: brewLinkTimer.totalSeconds || inspectedStagePlannedDurationSeconds
			};
		}
		return batch
			? batchTimer.getTimer(
					batch.id,
					inspectedStage,
					inspectedStagePlannedDurationSeconds,
					inspectedTimerStepId
				)
			: { remainingSeconds: 3600, running: false, totalSeconds: 3600 };
	});

	let timerRemainingSeconds = $derived(activeTimerView.remainingSeconds);
	let timerRunning = $derived(activeTimerView.running);
	let currentStageTotalSeconds = $derived(activeTimerView.totalSeconds);

	let activeBoilStepIndex = $state(0);

	let boilSteps = $derived.by<BatchBoilStep[]>(() => {
		if (!batch) return [];
		const steps: BatchBoilStep[] = [];

		// 1. Pre-Boil Ramp
		const firstWortIngredients = batch.ingredients.filter((i) => i.additionStage === 'FirstWort');
		const rampReading = [...batchEquipmentReadings]
			.reverse()
			.find(
				(r) => r.stage === 'Boil' && r.stepName && /ramp|pre[- ]?boil|uppkok/i.test(r.stepName)
			);

		steps.push({
			id: 'boil-step-ramp',
			stepOrder: steps.length + 1,
			name: t('batches.workspace.boil_step_ramp'),
			stepType: 'ramp',
			additionTimeMinutes: null,
			targetTemperatureC:
				(activeStageBrewLinkMetrics?.heating?.target_temp as number | undefined) ?? 100.0,
			ingredients: firstWortIngredients,
			isCompleted:
				firstWortIngredients.length > 0
					? firstWortIngredients.every((i) => i.isChecked)
					: timerRunning ||
						(timerRemainingSeconds < boilDurationSeconds && timerRemainingSeconds > 0),
			actualTemperatureC: rampReading?.temperatureC ?? null
		});

		// 2. Timed boil additions
		const boilIngs = batch.ingredients.filter(
			(i) =>
				(i.additionStage === 'Boil' ||
					(i.type === 'Hop' &&
						i.additionStage !== 'Whirlpool' &&
						i.additionStage !== 'DryHop' &&
						i.additionStage !== 'Mash' &&
						i.additionStage !== 'Primary' &&
						i.additionStage !== 'Secondary' &&
						i.additionStage !== 'Bottling')) &&
				i.additionTimeMinutes !== null &&
				i.additionTimeMinutes !== undefined
		);

		const distinctMinutes = Array.from(new Set(boilIngs.map((i) => i.additionTimeMinutes!))).sort(
			(a, b) => b - a
		);

		if (distinctMinutes.length === 0) {
			const boilTime = batch.boilTimeMinutes ?? 60;
			const stepName = t('batches.workspace.boil_step_general', { time: boilTime });
			const matchingReading = [...batchEquipmentReadings]
				.reverse()
				.find((r) => r.stage === 'Boil' && r.stepName === stepName);
			steps.push({
				id: `boil-step-${boilTime}`,
				stepOrder: steps.length + 1,
				name: stepName,
				stepType: 'addition',
				additionTimeMinutes: boilTime,
				targetTemperatureC: 100.0,
				ingredients: boilStageIngredients.filter((i) => i.additionStage === 'Boil'),
				isCompleted: false,
				actualTemperatureC: matchingReading?.temperatureC ?? null
			});
		} else {
			for (const m of distinctMinutes) {
				const ingsAtMinute = boilIngs.filter((i) => i.additionTimeMinutes === m);
				const isFlameout = m === 0;
				const stepName = isFlameout
					? t('batches.workspace.boil_step_flameout')
					: t('batches.workspace.boil_step_addition', { time: m });
				const matchingReading = [...batchEquipmentReadings]
					.reverse()
					.find(
						(r) =>
							r.stage === 'Boil' &&
							r.stepName &&
							(r.stepName === stepName ||
								(m > 0 && r.stepName.includes(`${m}m`)) ||
								(isFlameout && /flameout|kokslut/i.test(r.stepName)))
					);
				steps.push({
					id: `boil-step-${m}`,
					stepOrder: steps.length + 1,
					name: stepName,
					stepType: isFlameout ? 'flameout' : 'addition',
					additionTimeMinutes: m,
					targetTemperatureC: 100.0,
					ingredients: ingsAtMinute,
					isCompleted:
						ingsAtMinute.length > 0
							? ingsAtMinute.every((i) => i.isChecked)
							: timerRemainingSeconds <= m * 60,
					actualTemperatureC: matchingReading?.temperatureC ?? null
				});
			}
		}

		// 3. Whirlpool / Hop Stand
		const whirlpoolIngs = batch.ingredients.filter((i) => i.additionStage === 'Whirlpool');
		const hopStandMin = whirlpoolIngs
			.map((i) => i.additionTimeMinutes ?? 0)
			.reduce((max, val) => Math.max(max, val), 0);

		if (whirlpoolIngs.length > 0 || hopStandMin > 0) {
			const wpName = t('batches.workspace.boil_step_whirlpool');
			const wpReading = [...batchEquipmentReadings]
				.reverse()
				.find((r) => r.stage === 'Boil' && r.stepName && /whirlpool/i.test(r.stepName));
			steps.push({
				id: 'boil-step-whirlpool',
				stepOrder: steps.length + 1,
				name: wpName,
				stepType: 'whirlpool',
				durationMinutes: hopStandMin > 0 ? hopStandMin : 15,
				targetTemperatureC: 85.0,
				ingredients: whirlpoolIngs,
				isCompleted: whirlpoolIngs.length > 0 ? whirlpoolIngs.every((i) => i.isChecked) : false,
				actualTemperatureC: wpReading?.temperatureC ?? null
			});
		}

		return steps;
	});

	let currentSelectedBoilStep = $derived.by<BatchBoilStep | null>(() => {
		if (boilSteps.length === 0) return null;
		return boilSteps[activeBoilStepIndex] ?? boilSteps[0];
	});

	// Synchronize boil step index with BrewLink stage telemetry if available
	$effect(() => {
		if (inspectedStage === 'Boil' && activeStageBrewLinkMetrics && boilSteps.length > 0) {
			const stg = activeStageBrewLinkMetrics.stage;
			if (
				stg?.stepIndex !== undefined &&
				stg?.stepIndex !== null &&
				stg.stepIndex >= 0 &&
				stg.stepIndex < boilSteps.length
			) {
				activeBoilStepIndex = stg.stepIndex;
			} else if (stg?.stepName) {
				const matchIdx = boilSteps.findIndex((s) =>
					s.name.toLowerCase().includes(stg.stepName!.toLowerCase())
				);
				if (matchIdx !== -1) {
					activeBoilStepIndex = matchIdx;
				}
			} else if (activeStageBrewLinkMetrics.rampActive) {
				activeBoilStepIndex = 0;
			}
		}
	});

	let isMashControlledByBrewLink = $derived.by<boolean>(() => {
		const b = batch;
		if (!b || equipmentList.length === 0) return false;
		const mashTun = equipmentList.find((e) => e.id === b.mashTunId);
		const boiler = equipmentList.find((e) => e.id === b.boilerId);
		return mashTun?.connectionType === 'BrewLink' || boiler?.connectionType === 'BrewLink';
	});

	let isBoilControlledByBrewLink = $derived.by<boolean>(() => {
		const b = batch;
		if (!b || equipmentList.length === 0) return false;
		const boiler = equipmentList.find((e) => e.id === b.boilerId);
		return boiler?.connectionType === 'BrewLink';
	});

	let isMashComplete = $derived.by<boolean>(() => {
		if (!batch?.mashSteps || batch.mashSteps.length === 0) return true;
		return batch.mashSteps.every((s) => s.isCompleted);
	});

	let isBoilComplete = $derived.by<boolean>(() => {
		const stepsDone = boilSteps.length === 0 || boilSteps.every((s) => s.isCompleted);
		const timerDone =
			!brewLinkTimer || (!brewLinkTimer.active && brewLinkTimer.remainingSeconds <= 0);
		return stepsDone && timerDone;
	});

	let isAdvanceStageBlocked = $derived.by<boolean>(() => {
		if (!batch || isReadOnlyBatch) return false;
		if (batch.currentStage === 'Mash' && isMashControlledByBrewLink) {
			return !isMashComplete;
		}
		return false;
	});

	let advanceStageBlockedReason = $derived.by<string>(() => {
		if (batch?.currentStage === 'Mash' && isMashControlledByBrewLink && !isMashComplete) {
			return t('batches.workspace.brewlink_mash_in_progress');
		}
		return '';
	});

	// Auto-advance to Boil when last mash step is completed and ready for boil
	$effect(() => {
		if (
			batch &&
			!isReadOnlyBatch &&
			!advancing &&
			!isAutoAdvancingToBoil &&
			batch.currentStage === 'Mash' &&
			isMashControlledByBrewLink &&
			isMashComplete &&
			!activeStageBrewLinkMetrics?.interaction?.required
		) {
			advanceToBoilAutomatically();
		}
	});

	// Auto-complete current mash step when BrewLink hardware timer finishes
	$effect(() => {
		if (inspectedStage === 'Mash' && isMashControlledByBrewLink && brewLinkTimer) {
			if (brewLinkTimer.active && brewLinkTimer.remainingSeconds > 0) {
				mashTimerHadRun = true;
			} else if (mashTimerHadRun && !brewLinkTimer.active && brewLinkTimer.remainingSeconds <= 0) {
				mashTimerHadRun = false;
				if (currentSelectedMashStep && !currentSelectedMashStep.isCompleted) {
					handleToggleMashStep(currentSelectedMashStep.id, false);
				}
			}
		}
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
	}

	async function handleToggleMashStep(stepId: string, currentCompleted: boolean) {
		if (!batch || isReadOnlyBatch) return;
		try {
			const updatedStep = await api.batches.toggleMashStep(batch.id, stepId, {
				isCompleted: !currentCompleted
			});
			const idx = batch.mashSteps.findIndex((s) => s.id === stepId);
			if (idx !== -1) {
				batch.mashSteps[idx] = updatedStep;
			}
			if (updatedStep.isCompleted) {
				for (const ing of batch.ingredients) {
					if (ing.additionStage === 'Mash' && !ing.isDeducted) {
						ing.isChecked = true;
						ing.isDeducted = true;
					}
				}
				if (
					isMashControlledByBrewLink &&
					batch.currentStage === 'Mash' &&
					batch.mashSteps.every((s) => s.isCompleted)
				) {
					await advanceToBoilAutomatically();
				}
			}
		} catch (err: unknown) {
			console.error('Failed to toggle mash step', err);
		}
	}

	async function handleToggleFermentationStep(stepId: string, currentCompleted: boolean) {
		if (!batch || isReadOnlyBatch) return;
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

	async function advanceToBoilAutomatically() {
		if (!batch || isReadOnlyBatch || advancing || isAutoAdvancingToBoil) return;
		if (batch.currentStage !== 'Mash' || !isMashControlledByBrewLink) return;
		if (!isMashComplete) return;
		if (activeStageBrewLinkMetrics?.interaction?.required) return;

		isAutoAdvancingToBoil = true;
		advancing = true;
		try {
			const req: AdvanceBatchStageRequest = {
				targetStage: 'Boil'
			};
			const previousStage = batch.currentStage;
			const [updated, eq] = await Promise.all([
				api.batches.advanceStage(batch.id, req),
				api.equipment.list().catch(() => equipmentList)
			]);
			batchTimer.pauseStage(batch.id, previousStage);
			batch = updated;
			equipmentList = eq;
			inspectedStage = updated.currentStage;
			determineNextStage(updated.currentStage);
			showAdvanceModal = false;
		} catch (err: unknown) {
			console.error('Failed to auto-advance batch to Boil', err);
		} finally {
			advancing = false;
			isAutoAdvancingToBoil = false;
		}
	}

	async function handleAdvanceMashStep() {
		if (!batch?.mashSteps || activeMashStepIndex >= batch.mashSteps.length) return;
		if (activeStageBrewLinkEquipment && inspectedStage === 'Mash') {
			showConfirmAdvanceMashStepModal = true;
			return;
		}
		await executeAdvanceMashStep();
	}

	async function executeAdvanceMashStep() {
		if (!batch?.mashSteps || activeMashStepIndex >= batch.mashSteps.length) return;
		const currentStep = batch.mashSteps[activeMashStepIndex];
		if (currentStep) {
			batchTimer.pause(batch.id, 'Mash', currentStep.id);
		}
		if (currentStep && !currentStep.isCompleted) {
			await handleToggleMashStep(currentStep.id, false);
		}
		if (activeMashStepIndex + 1 < batch.mashSteps.length) {
			handleSelectMashStep(activeMashStepIndex + 1);
		} else if (isMashControlledByBrewLink && batch.currentStage === 'Mash') {
			await advanceToBoilAutomatically();
		}
	}

	async function confirmAdvanceMashStep() {
		showConfirmAdvanceMashStepModal = false;
		const equipment = activeStageBrewLinkEquipment;
		if (!batch?.mashSteps || !equipment) {
			await executeAdvanceMashStep();
			return;
		}
		const nextStep = batch.mashSteps[activeMashStepIndex + 1] ?? null;
		const timerWasRunning = timerRunning;
		try {
			await api.equipment.sendBrewLinkControl(equipment.id, { action: 'press_set' });
			if (nextStep) {
				await api.equipment.sendBrewLinkControl(equipment.id, {
					action: 'set_target_temp',
					tempC: nextStep.targetTemperatureC
				});
				if (timerWasRunning) {
					await api.equipment.sendBrewLinkControl(equipment.id, {
						action: 'set_timer',
						durationMin: Math.max(1, Math.round(nextStep.durationMinutes))
					});
				}
			}
			const updated = await api.equipment.getById(equipment.id);
			handleEquipmentUpdate(updated);
		} catch (err: unknown) {
			console.error('Failed to notify BrewLink of mash step advance', err);
		}
		await executeAdvanceMashStep();

		// Capture a baseline hardware reading for the newly selected step
		if (nextStep && !batchEquipmentReadings.some((r) => r.batchMashStepId === nextStep.id)) {
			await handleLogLiveBrewLinkTemp(
				'Mash',
				nextStep.id,
				nextStep.name,
				nextStep.targetTemperatureC
			);
		}
	}

	async function handleSyncMashStepTargetTemp(step: BatchMashStepDto | null) {
		if (!step || !activeStageBrewLinkEquipment) return;
		try {
			await api.equipment.sendBrewLinkControl(activeStageBrewLinkEquipment.id, {
				action: 'set_target_temp',
				tempC: step.targetTemperatureC
			});
			const updated = await api.equipment.getById(activeStageBrewLinkEquipment.id);
			handleEquipmentUpdate(updated);
		} catch (err: unknown) {
			console.error('Failed to sync mash step setpoint to BrewLink', err);
		}
	}

	function openAdvanceModal() {
		if (!batch || isAdvanceStageBlocked) return;
		determineNextStage(batch.currentStage);

		hasConfirmedActiveBoil = false;
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

	async function confirmAdvanceActiveBoil() {
		showConfirmAdvanceActiveBoilModal = false;
		hasConfirmedActiveBoil = true;
		await executeAdvanceSubmit(true);
	}

	function cancelAdvanceActiveBoil() {
		showConfirmAdvanceActiveBoilModal = false;
		hasConfirmedActiveBoil = false;
		showAdvanceModal = true;
	}

	async function handleAdvanceSubmit() {
		if (!batch || isAdvanceStageBlocked) return;
		const isBoilActive =
			batch.currentStage === 'Boil' &&
			nextStage === 'Ferment' &&
			isBoilControlledByBrewLink &&
			!isBoilComplete;

		if (isBoilActive && !hasConfirmedActiveBoil) {
			showAdvanceModal = false;
			showConfirmAdvanceActiveBoilModal = true;
			return;
		}

		await executeAdvanceSubmit(hasConfirmedActiveBoil || isBoilActive);
	}

	async function executeAdvanceSubmit(force: boolean = false) {
		if (!batch || isAdvanceStageBlocked) return;
		advancing = true;
		advanceError = null;

		if (force && activeStageBrewLinkEquipment) {
			try {
				await api.equipment.sendBrewLinkControl(activeStageBrewLinkEquipment.id, {
					action: 'cancel_timer'
				});
			} catch (err: unknown) {
				console.error('Failed to cancel BrewLink boil timer', err);
			}
		}

		try {
			const req: AdvanceBatchStageRequest = {
				targetStage: nextStage,
				measuredBatchSizeLiters: nextStage === 'Ferment' ? advanceVolume : null,
				measuredOg: nextStage === 'Ferment' ? advanceMeasuredOg : null,
				pitchTemperatureC: nextStage === 'Ferment' ? advancePitchTemp : null,
				measuredFg: nextStage === 'Package' ? advanceMeasuredFg : null,
				packagingVesselId:
					nextStage === 'Package'
						? advancePackagingVesselId
							? advancePackagingVesselId
							: batch.packagingVesselId
								? '00000000-0000-0000-0000-000000000000'
								: null
						: null,
				notes: advanceNotes.trim() || null,
				measuredPreBoilVolumeLiters: nextStage === 'Boil' ? advancePreBoilVolume : null,
				measuredPreBoilGravity: nextStage === 'Boil' ? advancePreBoilGravity : null,
				measuredPostBoilVolumeLiters: nextStage === 'Ferment' ? advancePostBoilVolume : null,
				measuredPackagedVolumeLiters: nextStage === 'Package' ? advancePackagedVolume : null,
				force
			};
			const previousStage = batch.currentStage;
			const [updated, eq] = await Promise.all([
				api.batches.advanceStage(batch.id, req),
				api.equipment.list().catch(() => equipmentList)
			]);
			batchTimer.pauseStage(batch.id, previousStage);
			batch = updated;
			equipmentList = eq;
			inspectedStage = updated.currentStage;
			determineNextStage(updated.currentStage);
			showAdvanceModal = false;
			showConfirmAdvanceActiveBoilModal = false;
			hasConfirmedActiveBoil = false;
		} catch (err: unknown) {
			if (err instanceof ApiClientError && err.details && err.details.length > 0) {
				advanceError = err.details.map((d) => `${d.field}: ${d.issue}`).join(', ');
			} else {
				advanceError = (err as Error).message || t('batches.errors.advance_failed');
			}
			showAdvanceModal = true;
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

	async function startTimer() {
		if (!batch) return;
		if (activeStageBrewLinkEquipment && isBrewLinkTimerStage) {
			try {
				if (brewLinkTimer?.paused) {
					await api.equipment.sendBrewLinkControl(activeStageBrewLinkEquipment.id, {
						action: 'resume_timer'
					});
				} else if (inspectedStage === 'Mash') {
					const step = currentSelectedMashStep;
					await api.equipment.sendBrewLinkControl(activeStageBrewLinkEquipment.id, {
						action: 'set_timer',
						durationMin: Math.max(1, Math.round(currentMashStepDurationSeconds / 60))
					});
					await api.equipment.sendBrewLinkControl(activeStageBrewLinkEquipment.id, {
						action: 'set_target_temp',
						tempC: step?.targetTemperatureC ?? 65.0
					});
					await api.equipment.sendBrewLinkControl(activeStageBrewLinkEquipment.id, {
						action: 'set_heat',
						state: true
					});
				} else {
					await api.equipment.sendBrewLinkControl(activeStageBrewLinkEquipment.id, {
						action: 'set_timer',
						durationMin: Math.max(1, Math.round(boilDurationSeconds / 60))
					});
				}
				const updated = await api.equipment.getById(activeStageBrewLinkEquipment.id);
				handleEquipmentUpdate(updated);

				// Capture a baseline hardware reading for this mash step if none exists yet
				const mashStep = currentSelectedMashStep;
				if (
					inspectedStage === 'Mash' &&
					mashStep &&
					!batchEquipmentReadings.some((r) => r.batchMashStepId === mashStep.id)
				) {
					await handleLogLiveBrewLinkTemp(
						'Mash',
						mashStep.id,
						mashStep.name,
						mashStep.targetTemperatureC
					);
				}
			} catch (err: unknown) {
				console.error('Failed to start BrewLink timer', err);
			}
			return;
		}
		batchTimer.start(
			batch.id,
			inspectedStage,
			inspectedStagePlannedDurationSeconds,
			inspectedTimerStepId
		);
	}

	function handlePauseTimerClick() {
		if (!batch) return;
		if (activeStageBrewLinkEquipment && isBrewLinkTimerStage) {
			showConfirmPauseTimerModal = true;
			return;
		}
		pauseTimer();
	}

	async function confirmPauseTimer() {
		showConfirmPauseTimerModal = false;
		if (activeStageBrewLinkEquipment && isBrewLinkTimerStage) {
			try {
				await api.equipment.sendBrewLinkControl(activeStageBrewLinkEquipment.id, {
					action: 'pause_timer'
				});
				const updated = await api.equipment.getById(activeStageBrewLinkEquipment.id);
				handleEquipmentUpdate(updated);
			} catch (err: unknown) {
				console.error('Failed to pause BrewLink timer', err);
			}
			return;
		}
		pauseTimer();
	}

	function pauseTimer() {
		if (!batch) return;
		batchTimer.pause(batch.id, inspectedStage, inspectedTimerStepId);
	}

	function handleResetTimerClick() {
		if (!batch) return;
		if (activeStageBrewLinkEquipment && isBrewLinkTimerStage) {
			showConfirmResetTimerModal = true;
			return;
		}
		resetTimer();
	}

	async function confirmResetTimer() {
		showConfirmResetTimerModal = false;
		if (activeStageBrewLinkEquipment && isBrewLinkTimerStage) {
			try {
				await api.equipment.sendBrewLinkControl(activeStageBrewLinkEquipment.id, {
					action: 'cancel_timer'
				});
				const updated = await api.equipment.getById(activeStageBrewLinkEquipment.id);
				handleEquipmentUpdate(updated);
			} catch (err: unknown) {
				console.error('Failed to reset BrewLink timer', err);
			}
			return;
		}
		resetTimer();
	}

	function resetTimer() {
		if (!batch) return;
		batchTimer.reset(
			batch.id,
			inspectedStage,
			inspectedStagePlannedDurationSeconds,
			inspectedTimerStepId
		);
	}

	async function handleAdvanceBoilStep() {
		if (activeStageBrewLinkEquipment) {
			showConfirmAdvanceBoilStepModal = true;
			return;
		}
		await executeAdvanceBoilStep();
	}

	async function executeAdvanceBoilStep() {
		if (boilSteps.length === 0 || activeBoilStepIndex >= boilSteps.length) return;
		const currentStep = boilSteps[activeBoilStepIndex];
		if (currentStep) {
			for (const ing of currentStep.ingredients) {
				if (!ing.isChecked) {
					await handleToggleIngredient(ing.id, false);
				}
			}
		}
		if (activeBoilStepIndex + 1 < boilSteps.length) {
			activeBoilStepIndex = activeBoilStepIndex + 1;
			selectedBoilStepGraphName = boilSteps[activeBoilStepIndex].name;
		}
	}

	async function confirmAdvanceBoilStep() {
		showConfirmAdvanceBoilStepModal = false;
		if (activeStageBrewLinkEquipment) {
			try {
				await api.equipment.sendBrewLinkControl(activeStageBrewLinkEquipment.id, {
					action: 'press_set'
				});
				const updated = await api.equipment.getById(activeStageBrewLinkEquipment.id);
				handleEquipmentUpdate(updated);
			} catch (err: unknown) {
				console.error('Failed to notify BrewLink of step advance', err);
			}
		}
		await executeAdvanceBoilStep();
	}

	async function handleToggleBoilStep(index: number) {
		if (!batch || isReadOnlyBatch || index < 0 || index >= boilSteps.length) return;
		const step = boilSteps[index];
		const nextCompleted = !step.isCompleted;
		step.isCompleted = nextCompleted;
		for (const ing of step.ingredients) {
			if (ing.isChecked !== nextCompleted) {
				await handleToggleIngredient(ing.id, ing.isChecked);
			}
		}
	}

	function handleSelectBoilStep(index: number) {
		if (index < 0 || index >= boilSteps.length) return;
		activeBoilStepIndex = index;
		selectedBoilStepGraphName = boilSteps[index].name;
	}

	async function handleLogLiveBrewLinkTemp(
		stage: BrewStage,
		stepId?: string | null,
		stepName?: string,
		targetTemp?: number
	) {
		if (
			!batch ||
			!activeStageBrewLinkEquipment ||
			activeStageBrewLinkEquipment.currentTemperatureC == null
		)
			return;
		const currentTemp = activeStageBrewLinkEquipment.currentTemperatureC;
		const isMash = stage === 'Mash';
		const sName =
			stepName ??
			(isMash
				? (currentSelectedMashStep?.name ?? t('batches.stages.mash'))
				: (currentSelectedBoilStep?.name ?? t('batches.stages.boil')));
		const tTemp =
			targetTemp ??
			(isMash
				? (currentSelectedMashStep?.targetTemperatureC ?? 65.0)
				: (currentSelectedBoilStep?.targetTemperatureC ?? 100.0));
		await handleLogStepTemperature({
			temperatureC: currentTemp,
			equipmentId: activeStageBrewLinkEquipment.id,
			stage,
			batchMashStepId: isMash ? (stepId ?? currentSelectedMashStep?.id ?? null) : null,
			stepName: sName,
			targetTemperatureC: tTemp
		});
	}

	async function handleAcknowledgeInteraction() {
		if (!activeStageBrewLinkEquipment) return;
		try {
			await api.equipment.sendBrewLinkControl(activeStageBrewLinkEquipment.id, {
				action: 'ack_interaction'
			});
			const updated = await api.equipment.getById(activeStageBrewLinkEquipment.id);
			handleEquipmentUpdate(updated);
		} catch (err: unknown) {
			console.error('Failed to acknowledge BrewLink interaction', err);
		}
	}

	async function handleDismissAlert() {
		if (!activeStageBrewLinkEquipment) return;
		try {
			await api.equipment.sendBrewLinkControl(activeStageBrewLinkEquipment.id, {
				action: 'dismiss_alert'
			});
			const updated = await api.equipment.getById(activeStageBrewLinkEquipment.id);
			handleEquipmentUpdate(updated);
		} catch (err: unknown) {
			console.error('Failed to dismiss BrewLink alert', err);
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
				<!-- Brew Station HUD Button (Active only during hands-on Mash & Boil stages) -->
				{#if inspectedStage === 'Mash' || inspectedStage === 'Boil'}
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
				{/if}

				{#if !isReadOnlyBatch}
					<button
						type="button"
						data-testid="advance-stage-btn"
						disabled={isAdvanceStageBlocked}
						onclick={openAdvanceModal}
						class="flex min-h-[44px] items-center gap-2 rounded-xl px-4 py-2.5 text-sm font-bold shadow-md transition-all active:scale-[0.98] {isAdvanceStageBlocked
							? 'cursor-not-allowed bg-zinc-300 text-zinc-500 opacity-60 dark:bg-zinc-800 dark:text-zinc-500'
							: 'bg-gradient-to-r from-amber-500 to-amber-600 text-zinc-950 hover:from-amber-400 hover:to-amber-500'}"
						title={isAdvanceStageBlocked
							? advanceStageBlockedReason
							: t('batches.workspace.advance_stage')}
					>
						<ArrowRight class="h-4 w-4 stroke-[2.5]" />
						<span>{t('batches.workspace.advance_stage')}</span>
					</button>
				{/if}

				<a
					href="/batches/{batch.id}/labels/print"
					title={t('labels.print_labels_btn')}
					aria-label={t('labels.print_labels_btn')}
					class="flex min-h-[44px] min-w-[44px] items-center justify-center rounded-xl border border-zinc-200 p-2.5 text-zinc-600 transition-colors hover:border-amber-500/40 hover:bg-amber-500/10 hover:text-amber-600 dark:border-zinc-800 dark:text-zinc-300 dark:hover:text-amber-400"
				>
					<Printer class="h-4 w-4" />
				</a>

				<button
					type="button"
					data-testid="batch-detail-qr-btn"
					onclick={() => (showQrModal = true)}
					title={t('batches.qr.open_title')}
					aria-label={t('batches.qr.open_title')}
					class="flex min-h-[44px] min-w-[44px] items-center justify-center rounded-xl border border-zinc-200 p-2.5 text-zinc-600 transition-colors hover:border-amber-500/40 hover:bg-amber-500/10 hover:text-amber-600 dark:border-zinc-800 dark:text-zinc-300 dark:hover:text-amber-400"
				>
					<QrCode class="h-4 w-4" />
				</button>

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

		{#if isAdvanceStageBlocked}
			<div
				data-testid="advance-stage-blocked-banner"
				class="flex items-center gap-2 rounded-2xl border border-amber-500/30 bg-amber-500/10 px-4 py-2.5 text-xs font-semibold text-amber-800 dark:text-amber-200"
			>
				<AlertTriangle class="h-4 w-4 shrink-0 text-amber-500" />
				<span>{advanceStageBlockedReason}</span>
			</div>
		{/if}

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
					onclick={() => {
						inspectedStage = batch!.currentStage;
					}}
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

			<!-- BrewLink Equipment Hardware Controls in Batch Workspace -->
			{#if activeStageBrewLinkEquipment}
				<div
					class="rounded-3xl border border-sky-500/25 bg-sky-500/5 p-4 dark:border-sky-500/20 dark:bg-sky-500/10"
				>
					<div class="flex flex-wrap items-center justify-between gap-3">
						<div class="flex items-center gap-2">
							<Radio class="h-5 w-5 text-sky-500" />
							<div>
								<h2 class="text-sm font-bold text-zinc-900 dark:text-zinc-100">
									{activeStageBrewLinkEquipment.name} ({t('equipment.brewlink_tag')})
								</h2>
								<span class="text-xs text-zinc-500 dark:text-zinc-400">
									{t('equipment.brewlink_controls_title')}
								</span>
							</div>
						</div>

						<div class="flex items-center gap-2">
							<button
								type="button"
								onclick={() => (showBatchBrewLinkControls = !showBatchBrewLinkControls)}
								class="inline-flex items-center gap-1.5 rounded-xl border border-sky-500/30 bg-white px-3 py-1.5 text-xs font-bold text-sky-700 shadow-sm transition hover:bg-sky-50 dark:border-zinc-700 dark:bg-zinc-900 dark:text-sky-300 dark:hover:bg-zinc-800"
								data-testid="batch-workspace-toggle-brewlink"
							>
								<span>{t('equipment.brewlink_controls')}</span>
								{#if showBatchBrewLinkControls}
									<ChevronUp class="h-3.5 w-3.5" />
								{:else}
									<ChevronDown class="h-3.5 w-3.5" />
								{/if}
							</button>
						</div>
					</div>

					{#if showBatchBrewLinkControls}
						<div class="mt-4 border-t border-sky-500/20 pt-4">
							<BrewLinkControlPanel
								equipment={activeStageBrewLinkEquipment}
								onUpdate={handleEquipmentUpdate}
							/>
						</div>
					{/if}
				</div>
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

					<!-- Hot Liquor Tank / Sparge Water Status Card -->
					{#if batch.hltId || (batch.volumeProfile && batch.volumeProfile.spargeWaterLiters > 0)}
						<div
							class="glass-panel flex flex-wrap items-center justify-between gap-4 rounded-3xl border border-cyan-500/20 bg-cyan-500/5 p-4 sm:p-5 dark:border-cyan-500/10 dark:bg-cyan-500/5"
						>
							<div class="flex items-center gap-3">
								<div
									class="flex h-10 w-10 items-center justify-center rounded-2xl bg-cyan-500/10 text-cyan-500"
								>
									<Droplets class="h-5 w-5" />
								</div>
								<div>
									<div class="flex items-center gap-2">
										<h3 class="text-sm font-bold text-zinc-900 dark:text-white">
											{t('batches.workspace.hlt_status_title')}
										</h3>
										<span
											class="rounded-md border border-cyan-500/30 bg-cyan-500/10 px-2 py-0.5 text-[10px] font-bold text-cyan-600 uppercase dark:text-cyan-400"
										>
											{batch.hltName ?? assignedHlt?.name ?? 'HLT'}
										</span>
									</div>
									<p class="text-xs text-zinc-500 dark:text-zinc-400">
										{batch.volumeProfile
											? `${formatNumber(batch.volumeProfile.spargeWaterLiters, 1)} L`
											: ''} • {t('batches.workspace.hlt_target_temp')}: {settings.formatTemperature(
											estimatedSpargeTempC
										)}
									</p>
								</div>
							</div>

							<div class="flex items-center gap-3">
								{#if assignedHlt?.currentTemperatureC !== null && assignedHlt?.currentTemperatureC !== undefined}
									<div class="text-right">
										<span class="block text-[10px] font-semibold text-zinc-400 uppercase">
											{t('batches.telemetry.current_temp')}
										</span>
										<span class="font-mono text-base font-bold text-cyan-600 dark:text-cyan-400">
											{settings.formatTemperature(assignedHlt.currentTemperatureC)}
										</span>
									</div>
								{/if}
								<span
									class="rounded-xl border border-cyan-500/30 bg-cyan-500/10 px-3 py-1.5 text-xs font-semibold text-cyan-700 dark:text-cyan-300"
								>
									{assignedHlt?.currentTemperatureC &&
									assignedHlt.currentTemperatureC >= estimatedSpargeTempC - 1
										? t('batches.workspace.hlt_ready')
										: t('batches.workspace.hlt_heating')}
								</span>
							</div>
						</div>
					{/if}

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
										{#if isBrewLinkTimerActive}
											<span
												data-testid="brewlink-timer-badge"
												class="inline-flex items-center gap-1 rounded-full border border-sky-500/40 bg-sky-500/10 px-2.5 py-0.5 text-[10px] font-bold text-sky-700 dark:text-sky-300"
											>
												<Radio class="h-3 w-3 animate-pulse text-sky-500" />
												<span>{t('batches.workspace.brewlink_controlled_timer')}</span>
											</span>
										{/if}
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
										{#if activeStageBrewLinkEquipment && activeStageBrewLinkEquipment.currentTemperatureC != null}
											<div
												data-testid="mash-hardware-temp-banner"
												class="mt-2 flex flex-wrap items-center justify-between gap-2"
											>
												<span
													class="inline-flex items-center gap-1 text-xs font-semibold text-sky-700 dark:text-sky-300"
												>
													<Radio class="h-3 w-3 animate-pulse text-sky-500" />
													{t('batches.workspace.live_hardware_temp')}: {settings.formatTemperature(
														activeStageBrewLinkEquipment.currentTemperatureC
													)}
												</span>
												{#if canLogInspectedStage}
													<button
														type="button"
														data-testid="sync-mash-setpoint-btn"
														onclick={() => handleSyncMashStepTargetTemp(currentSelectedMashStep)}
														class="inline-flex cursor-pointer items-center gap-1 rounded-lg border border-sky-500/30 bg-sky-500/10 px-2 py-1 text-xs font-semibold text-sky-700 transition-colors hover:bg-sky-500/20 dark:bg-sky-500/20 dark:text-sky-300"
													>
														<Thermometer class="h-3 w-3" />
														<span>{t('batches.workspace.sync_hardware_temp')}</span>
													</button>
												{/if}
											</div>
										{/if}
									</div>
								{/if}

								<div class="py-4 text-center">
									<span
										data-testid="mash-timer-display"
										class="font-mono text-5xl font-extrabold text-zinc-900 dark:text-white"
									>
										{formatTime(timerRemainingSeconds)}
									</span>
								</div>

								<StageProgressBar
									totalSeconds={currentMashStepDurationSeconds}
									remainingSeconds={timerRemainingSeconds}
									running={timerRunning}
									stage="Mash"
									variant="default"
								/>
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
											onclick={handlePauseTimerClick}
											class="flex min-h-[44px] items-center gap-2 rounded-xl bg-zinc-200 px-5 py-2.5 text-xs font-bold text-zinc-800 transition-all hover:bg-zinc-300 active:scale-95 dark:bg-zinc-800 dark:text-zinc-200"
										>
											<Pause class="h-4 w-4 fill-current" />
											<span>{t('batches.workspace.pause_timer')}</span>
										</button>
									{/if}

									<button
										type="button"
										onclick={handleResetTimerClick}
										class="flex min-h-[44px] items-center gap-2 rounded-xl border border-zinc-200 px-4 py-2.5 text-xs font-semibold text-zinc-600 transition-colors hover:bg-zinc-100 active:scale-95 dark:border-zinc-800 dark:text-zinc-400 dark:hover:bg-zinc-800"
									>
										<RotateCcw class="h-3.5 w-3.5" />
										<span>{t('batches.workspace.reset_timer')}</span>
									</button>
								</div>

								{#if !isReadOnlyBatch && batch.mashSteps && batch.mashSteps.length > 0}
									<button
										type="button"
										data-testid="advance-mash-step-btn"
										onclick={handleAdvanceMashStep}
										class="flex min-h-[38px] w-full items-center justify-center gap-1.5 rounded-xl border border-amber-500/30 bg-amber-500/10 py-2 text-xs font-bold text-amber-700 transition-colors hover:bg-amber-500/20 active:scale-95 dark:text-amber-300"
									>
										<StepForward class="h-4 w-4" />
										<span>
											{activeMashStepIndex === batch.mashSteps.length - 1 &&
											isMashControlledByBrewLink
												? t('batches.workspace.complete_mash_and_boil')
												: t('batches.workspace.complete_and_next_step')}
										</span>
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
													disabled={isReadOnlyBatch}
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
													{#if activeStageBrewLinkEquipment && activeStageBrewLinkEquipment.currentTemperatureC != null && activeMashStepIndex === idx}
														<button
															type="button"
															data-testid="mash-step-sync-setpoint-{step.id}"
															onclick={() => handleSyncMashStepTargetTemp(step)}
															class="inline-flex cursor-pointer items-center gap-1 rounded-lg border border-sky-500/30 bg-sky-500/10 px-2 py-1 text-xs font-semibold text-sky-700 transition-colors hover:bg-sky-500/20 dark:bg-sky-500/20 dark:text-sky-300"
															title={t('batches.workspace.sync_hardware_temp')}
														>
															<Thermometer class="h-3 w-3" />
															<span class="hidden sm:inline">
																{t('batches.workspace.sync_hardware_temp')}
															</span>
														</button>
													{/if}
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
						equipmentName={currentStageEquipmentName}
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
												disabled={isReadOnlyBatch}
												onchange={() => handleToggleIngredient(ing.id, ing.isChecked)}
												class="h-4 w-4 rounded-md border-zinc-300 text-amber-500 focus:ring-amber-500/30 disabled:cursor-not-allowed disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-900"
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
				<div class="space-y-6">
					<!-- BrewLink Equipment Interaction / Alert Prompt Banner -->
					{#if activeStageBrewLinkMetrics?.interaction?.required}
						<div
							class="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-amber-500/40 bg-amber-500/15 p-4 text-amber-900 shadow-md dark:bg-amber-500/20 dark:text-amber-100"
							data-testid="boil-brewlink-interaction-banner"
						>
							<div class="flex items-center gap-3">
								<AlertTriangle
									class="h-6 w-6 shrink-0 animate-bounce text-amber-600 dark:text-amber-400"
								/>
								<div>
									<h3 class="text-sm font-bold">{t('batches.workspace.brewlink_alert_title')}</h3>
									<p class="text-xs">
										{activeStageBrewLinkMetrics.interaction.message ||
											t('equipment.brewlink_interaction_alert')}
									</p>
								</div>
							</div>
							<div class="flex items-center gap-2">
								<button
									type="button"
									onclick={handleAcknowledgeInteraction}
									class="rounded-xl bg-amber-600 px-4 py-2 text-xs font-bold text-white shadow hover:bg-amber-700 active:scale-95 dark:bg-amber-500 dark:hover:bg-amber-600"
									data-testid="boil-ack-interaction-btn"
								>
									{t('common.yes')} — {t('equipment.brewlink_ack_prompt')}
								</button>
								<button
									type="button"
									onclick={handleDismissAlert}
									class="rounded-xl border border-amber-600/30 bg-white/60 px-3 py-2 text-xs font-semibold text-amber-900 hover:bg-white/90 active:scale-95 dark:bg-zinc-900/60 dark:text-amber-200"
									data-testid="boil-dismiss-alert-btn"
								>
									{t('common.no')} — {t('equipment.brewlink_dismiss_alert')}
								</button>
							</div>
						</div>
					{/if}

					<!-- Boil Timer & Boil Execution Stepper Grid -->
					<div class="grid grid-cols-1 gap-6 lg:grid-cols-3">
						<!-- Boil Timer & Controls -->
						<div
							data-testid="boil-timer-panel"
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
									<div class="flex items-center gap-2">
										{#if isBrewLinkTimerActive}
											<span
												data-testid="brewlink-timer-badge"
												class="inline-flex items-center gap-1 rounded-full border border-sky-500/40 bg-sky-500/10 px-2.5 py-0.5 text-[10px] font-bold text-sky-700 dark:text-sky-300"
											>
												<Radio class="h-3 w-3 animate-pulse text-sky-500" />
												<span>{t('batches.workspace.brewlink_controlled_timer')}</span>
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

								{#if currentSelectedBoilStep}
									<div
										class="rounded-xl border border-zinc-200/60 bg-white/50 p-2.5 dark:border-zinc-800/60 dark:bg-zinc-900/40"
									>
										<p
											data-testid="active-boil-step-name"
											class="font-semibold text-zinc-900 dark:text-white"
										>
											{currentSelectedBoilStep.name}
										</p>
										<p class="text-xs text-zinc-500 dark:text-zinc-400">
											{settings.formatTemperature(currentSelectedBoilStep.targetTemperatureC)}
											{#if currentSelectedBoilStep.additionTimeMinutes !== null && currentSelectedBoilStep.additionTimeMinutes !== undefined}
												• {currentSelectedBoilStep.additionTimeMinutes} {t('common.minutes')}
											{:else if currentSelectedBoilStep.durationMinutes}
												• {currentSelectedBoilStep.durationMinutes} {t('common.minutes')}
											{/if}
										</p>
									</div>
								{:else}
									<p class="text-xs text-zinc-500 dark:text-zinc-400">
										{t('batches.workspace.boil_timer_desc')}
									</p>
								{/if}

								<div class="py-4 text-center">
									<span
										data-testid="boil-timer-display"
										class="font-mono text-5xl font-extrabold text-zinc-900 dark:text-white"
									>
										{formatTime(timerRemainingSeconds)}
									</span>
								</div>

								<StageProgressBar
									totalSeconds={boilDurationSeconds}
									remainingSeconds={timerRemainingSeconds}
									running={timerRunning}
									stage="Boil"
									milestones={boilStageIngredients}
									onToggleMilestone={handleToggleIngredient}
									variant="default"
								/>
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
											onclick={handlePauseTimerClick}
											class="flex min-h-[44px] items-center gap-2 rounded-xl bg-zinc-200 px-5 py-2.5 text-xs font-bold text-zinc-800 transition-all hover:bg-zinc-300 active:scale-95 dark:bg-zinc-800 dark:text-zinc-200"
										>
											<Pause class="h-4 w-4 fill-current" />
											<span>{t('batches.workspace.pause_timer')}</span>
										</button>
									{/if}

									<button
										type="button"
										onclick={handleResetTimerClick}
										class="flex min-h-[44px] items-center gap-2 rounded-xl border border-zinc-200 px-4 py-2.5 text-xs font-semibold text-zinc-600 transition-colors hover:bg-zinc-100 active:scale-95 dark:border-zinc-800 dark:text-zinc-400 dark:hover:bg-zinc-800"
									>
										<RotateCcw class="h-3.5 w-3.5" />
										<span>{t('batches.workspace.reset_timer')}</span>
									</button>
								</div>

								{#if !isReadOnlyBatch && boilSteps.length > 1}
									<button
										type="button"
										data-testid="advance-boil-step-btn"
										onclick={handleAdvanceBoilStep}
										class="flex min-h-[38px] w-full items-center justify-center gap-1.5 rounded-xl border border-amber-500/30 bg-amber-500/10 py-2 text-xs font-bold text-amber-700 transition-colors hover:bg-amber-500/20 active:scale-95 dark:text-amber-300"
									>
										<StepForward class="h-4 w-4" />
										<span>{t('batches.workspace.complete_and_next_step')}</span>
									</button>
								{/if}
							</div>
						</div>

						<!-- Boil Schedule Timeline & Stepper (matching BrewLink equipment) -->
						<div
							data-testid="boil-schedule-card"
							class="glass-panel flex flex-col rounded-3xl border border-zinc-200/80 p-6 lg:col-span-2 dark:border-white/10"
						>
							<div
								class="flex items-center justify-between border-b border-zinc-200/60 pb-3 dark:border-white/5"
							>
								<div class="flex items-center gap-2 text-zinc-900 dark:text-white">
									<Clock class="h-5 w-5 text-amber-500" />
									<h2 class="text-base font-bold">
										{t('batches.workspace.boil_schedule_title')}
									</h2>
								</div>
								<span class="font-mono text-xs text-zinc-500">
									{boilSteps.filter((s) => s.isCompleted).length} / {boilSteps.length}
								</span>
							</div>

							<div class="mt-4 space-y-3">
								{#each boilSteps as step, idx (step.id)}
									<div
										data-testid="boil-step-row-{step.id}"
										class="flex items-center justify-between rounded-xl border p-3.5 transition-all {activeBoilStepIndex ===
										idx
											? 'border-amber-500 bg-amber-50/50 shadow-xs dark:border-amber-500/50 dark:bg-amber-500/10'
											: 'border-zinc-200/60 bg-white/60 hover:bg-zinc-50 dark:border-zinc-800/60 dark:bg-zinc-900/30 dark:hover:bg-zinc-900/60'}"
									>
										<div class="flex items-center gap-3">
											<input
												type="checkbox"
												data-testid="boil-step-checkbox-{step.id}"
												checked={step.isCompleted}
												disabled={isReadOnlyBatch}
												onchange={() => handleToggleBoilStep(idx)}
												class="h-4 w-4 rounded-md border-zinc-300 text-amber-500 focus:ring-amber-500/30 dark:border-zinc-700 dark:bg-zinc-900"
											/>
											<div>
												<button
													type="button"
													data-testid="boil-step-name-btn-{step.id}"
													onclick={() => handleSelectBoilStep(idx)}
													class="cursor-pointer text-left text-sm font-semibold {step.isCompleted
														? 'text-zinc-400 line-through dark:text-zinc-500'
														: 'text-zinc-900 dark:text-white'} hover:text-amber-600 dark:hover:text-amber-400"
												>
													<span class="mr-1.5 font-mono text-xs text-amber-600 dark:text-amber-400">
														#{step.stepOrder}
													</span>
													{step.name}
												</button>
												<p class="text-xs text-zinc-500 dark:text-zinc-400">
													{settings.formatTemperature(step.targetTemperatureC)}
													{#if step.additionTimeMinutes !== null && step.additionTimeMinutes !== undefined}
														• {step.additionTimeMinutes} {t('common.minutes')}
													{:else if step.durationMinutes}
														• {step.durationMinutes} {t('common.minutes')}
													{/if}
													{#if step.ingredients.length > 0}
														• {step.ingredients
															.map((i) => `${i.name} (${i.amount}${i.unit})`)
															.join(', ')}
													{/if}
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
													onclick={() => openLogTempModal('Boil', step.name)}
													class="inline-flex cursor-pointer items-center gap-1 rounded-lg border border-amber-500/30 bg-amber-500/10 px-2 py-1 text-xs font-medium text-amber-700 transition-colors hover:bg-amber-500/20 dark:bg-amber-500/20 dark:text-amber-300"
													title={t('batches.telemetry.log_temperature')}
												>
													<Thermometer class="h-3 w-3" />
													<span>{t('batches.telemetry.log_temperature')}</span>
												</button>
											{/if}

											{#if activeBoilStepIndex === idx}
												<span
													class="rounded-full bg-amber-500/20 px-2 py-0.5 text-[10px] font-bold tracking-wider text-amber-700 uppercase dark:text-amber-300"
												>
													{t('batches.workspace.active_step')}
												</span>
											{:else}
												<button
													type="button"
													data-testid="boil-step-select-{step.id}"
													onclick={() => handleSelectBoilStep(idx)}
													class="cursor-pointer rounded-lg border border-zinc-200/80 px-2.5 py-1 text-xs font-medium text-zinc-600 transition-colors hover:bg-zinc-100 dark:border-zinc-800 dark:text-zinc-400 dark:hover:bg-zinc-800"
												>
													{t('batches.workspace.select_step')}
												</button>
											{/if}
										</div>
									</div>
								{/each}
							</div>
						</div>
					</div>

					<!-- Boil Stage Temperature Progression Graph -->
					<StepTemperatureChart
						stage="Boil"
						readings={batchEquipmentReadings}
						{boilSteps}
						selectedBoilStepName={selectedBoilStepGraphName}
						targetTemperatureC={currentSelectedBoilStep?.targetTemperatureC ?? 100.0}
						onSelectBoilStep={(name) => {
							selectedBoilStepGraphName = name;
							if (name) {
								const idx = boilSteps.findIndex((s) => s.name === name);
								if (idx !== -1) activeBoilStepIndex = idx;
							}
						}}
						isLive={isTelemetryLive && isInspectingActiveStage}
						onOpenLogModal={canLogInspectedStage
							? () =>
									openLogTempModal(
										'Boil',
										selectedBoilStepGraphName ?? currentSelectedBoilStep?.name
									)
							: undefined}
						equipmentName={currentStageEquipmentName}
					/>

					<!-- Boil Checklist -->
					<div class="glass-panel rounded-3xl border border-zinc-200/80 p-6 dark:border-white/10">
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
												disabled={isReadOnlyBatch}
												onchange={() => handleToggleIngredient(ing.id, ing.isChecked)}
												class="h-4 w-4 rounded-md border-zinc-300 text-amber-500 focus:ring-amber-500/30 disabled:cursor-not-allowed disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-900"
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

					<!-- Boil Stage Measurements & Manual Inputs Card -->
					<div>
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
												disabled={isReadOnlyBatch}
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
											{#if !step.isCompleted && step.triggerGravity !== null && step.triggerGravity !== undefined && batch.currentGravity !== null && batch.currentGravity !== undefined && batch.currentGravity <= step.triggerGravity}
												<span
													data-testid="fermentation-step-trigger-ready-{step.id}"
													class="rounded-full border border-amber-500/40 bg-amber-500/15 px-2 py-0.5 text-[10px] font-bold text-amber-600 dark:text-amber-400"
												>
													{t('batches.workspace.trigger_gravity_reached')}
												</span>
											{/if}
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
												disabled={isReadOnlyBatch}
												onchange={() => handleToggleIngredient(ing.id, ing.isChecked)}
												class="h-4 w-4 rounded-md border-zinc-300 text-amber-500 focus:ring-amber-500/30 disabled:cursor-not-allowed disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-900"
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
												disabled={isReadOnlyBatch}
												onchange={() => handleToggleIngredient(ing.id, ing.isChecked)}
												class="h-4 w-4 rounded-md border-zinc-300 text-amber-500 focus:ring-amber-500/30 disabled:cursor-not-allowed disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-900"
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
					onclick={() => {
						showAdvanceModal = false;
						hasConfirmedActiveBoil = false;
					}}
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
						{#each forwardStageOptions as stageOpt (stageOpt)}
							<option value={stageOpt}>{t(`batches.stages.${stageOpt.toLowerCase()}`)}</option>
						{/each}
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
					onclick={() => {
						showAdvanceModal = false;
						hasConfirmedActiveBoil = false;
					}}
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
	{boilSteps}
	defaultBoilStepName={logTempBoilStepName}
	targetTemperatureC={logTempStage === 'Mash'
		? (batch?.mashSteps?.find((s) => s.id === logTempMashStepId)?.targetTemperatureC ?? 65)
		: logTempStage === 'Boil'
			? (boilSteps.find((s) => s.name === logTempBoilStepName)?.targetTemperatureC ??
				currentSelectedBoilStep?.targetTemperatureC ??
				100)
			: activeFermentationStepTargetTemp}
	onClose={() => (showLogTempModal = false)}
	onSubmit={handleLogStepTemperature}
/>

<DeleteBatchModal
	open={showDeleteBatchModal}
	{batch}
	onClose={() => (showDeleteBatchModal = false)}
	onDeleted={() => goto('/batches')}
/>

<BatchQrModal open={showQrModal} {batch} onClose={() => (showQrModal = false)} />

<ConfirmModal
	open={showConfirmPauseTimerModal}
	title={t('batches.workspace.confirm_pause_timer_title')}
	message={t('batches.workspace.confirm_pause_timer_message', {
		equipment: activeStageBrewLinkEquipment?.name ?? ''
	})}
	confirmText={t('common.yes')}
	cancelText={t('common.no')}
	variant="warning"
	confirmTestId="confirm-pause-timer-btn"
	cancelTestId="cancel-pause-timer-btn"
	onConfirm={confirmPauseTimer}
	onClose={() => (showConfirmPauseTimerModal = false)}
/>

<ConfirmModal
	open={showConfirmResetTimerModal}
	title={t('batches.workspace.confirm_reset_timer_title')}
	message={t('batches.workspace.confirm_reset_timer_message', {
		equipment: activeStageBrewLinkEquipment?.name ?? ''
	})}
	confirmText={t('common.yes')}
	cancelText={t('common.no')}
	variant="danger"
	confirmTestId="confirm-reset-timer-btn"
	cancelTestId="cancel-reset-timer-btn"
	onConfirm={confirmResetTimer}
	onClose={() => (showConfirmResetTimerModal = false)}
/>

<ConfirmModal
	open={showConfirmAdvanceBoilStepModal}
	title={t('batches.workspace.confirm_advance_step_title')}
	message={t('batches.workspace.confirm_advance_step_message', {
		equipment: activeStageBrewLinkEquipment?.name ?? ''
	})}
	confirmText={t('common.yes')}
	cancelText={t('common.no')}
	variant="info"
	confirmTestId="confirm-advance-step-btn"
	cancelTestId="cancel-advance-step-btn"
	onConfirm={confirmAdvanceBoilStep}
	onClose={() => (showConfirmAdvanceBoilStepModal = false)}
/>

<ConfirmModal
	open={showConfirmAdvanceMashStepModal}
	title={t('batches.workspace.confirm_advance_mash_step_title')}
	message={t('batches.workspace.confirm_advance_mash_step_message', {
		equipment: activeStageBrewLinkEquipment?.name ?? ''
	})}
	confirmText={t('common.yes')}
	cancelText={t('common.no')}
	variant="info"
	confirmTestId="confirm-advance-mash-step-btn"
	cancelTestId="cancel-advance-mash-step-btn"
	onConfirm={confirmAdvanceMashStep}
	onClose={() => (showConfirmAdvanceMashStepModal = false)}
/>

<ConfirmModal
	open={showConfirmAdvanceActiveBoilModal}
	title={t('batches.workspace.confirm_advance_active_boil_title')}
	message={t('batches.workspace.confirm_advance_active_boil_message')}
	confirmText={t('batches.workspace.proceed_to_ferment')}
	cancelText={t('common.cancel')}
	variant="warning"
	confirmTestId="confirm-advance-active-boil-btn"
	cancelTestId="cancel-advance-active-boil-btn"
	onConfirm={confirmAdvanceActiveBoil}
	onClose={cancelAdvanceActiveBoil}
/>

{#if batch && (inspectedStage === 'Mash' || inspectedStage === 'Boil')}
	<BrewStationHudModal
		open={showBrewStationHud}
		batchName={batch.name}
		beerStyle={batch.beerStyle}
		currentStage={inspectedStage}
		{timerRemainingSeconds}
		{timerRunning}
		stageTotalSeconds={currentStageTotalSeconds}
		stageIngredients={inspectedStage === 'Boil' ? boilStageIngredients : []}
		onToggleIngredient={handleToggleIngredient}
		targetTemperatureC={inspectedStage === 'Mash'
			? (currentSelectedMashStep?.targetTemperatureC ?? 65)
			: (currentSelectedBoilStep?.targetTemperatureC ?? 100)}
		currentTemperatureC={batchEquipmentReadings.length > 0
			? batchEquipmentReadings[batchEquipmentReadings.length - 1].temperatureC
			: (currentSelectedMashStep?.actualTemperatureC ?? null)}
		activeStepName={inspectedStage === 'Mash'
			? currentSelectedMashStep?.name
			: (currentSelectedBoilStep?.name ??
				`${t('batches.stages.boil')} (${batch.boilTimeMinutes ?? 60}m)`)}
		{activeMashStepIndex}
		totalMashSteps={batch.mashSteps?.length}
		spargeEnabled={Boolean(
			(batch.volumeProfile && batch.volumeProfile.spargeWaterLiters > 0) || batch.hltId
		)}
		spargeWaterTempC={estimatedSpargeTempC}
		spargeLiveTempC={assignedHlt?.currentTemperatureC ?? null}
		spargeEquipmentName={batch.hltName ?? assignedHlt?.name ?? null}
		brewLinkEquipment={activeStageBrewLinkEquipment}
		onBrewLinkUpdate={handleEquipmentUpdate}
		targetPostBoilVolumeLiters={batch.volumeProfile?.targetPostBoilVolumeLiters ??
			batch.targetBatchSizeLiters}
		boilOffRatePerHour={batch.volumeProfile?.boilOffRatePerHour ?? null}
		onToggleTimer={() => {
			if (timerRunning) {
				handlePauseTimerClick();
			} else {
				startTimer();
			}
		}}
		onResetTimer={handleResetTimerClick}
		onAdjustTimer={(seconds) => {
			if (!batch) return;
			batchTimer.adjust(
				batch.id,
				inspectedStage,
				seconds,
				inspectedStagePlannedDurationSeconds,
				inspectedTimerStepId
			);
		}}
		onAdvanceStep={inspectedStage === 'Mash' ? handleAdvanceMashStep : handleAdvanceBoilStep}
		onClose={() => (showBrewStationHud = false)}
	/>
{/if}

<!-- BrewLink Interactive Action Confirmation Modal -->
<BrewLinkInteractionModal
	open={showBrewLinkPromptModal}
	message={activeBrewLinkPrompt?.message ?? ''}
	equipmentName={activeBrewLinkPrompt?.equipment.name}
	loading={brewLinkPromptSubmitting}
	onConfirm={handleConfirmBrewLinkInteraction}
	onDismiss={handleDismissBrewLinkInteraction}
/>
