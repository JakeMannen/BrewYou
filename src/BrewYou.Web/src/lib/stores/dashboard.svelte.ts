import { browser } from '$app/environment';
import { api, ApiClientError } from '$lib/api/client';
import { brewery, DEFAULT_BREWERY_ID } from '$lib/stores/brewery.svelte';
import { auth } from '$lib/stores/auth.svelte';
import { settings } from '$lib/stores/settings.svelte';
import { calculateActualAbv } from '$lib/calculators/brewing';
import type {
	BatchSummaryDto,
	EquipmentDto,
	BrewStage,
	AddBatchReadingRequest,
	BatchReadingDto,
	BatchEquipmentReadingDto,
	EquipmentTelemetryUpdateDto
} from '$lib/types/api';

export interface AdhocTask {
	id: string;
	text: string;
	completed: boolean;
	createdAt: string;
}

export interface BatchMilestoneTask {
	id: string;
	batchId: string;
	batchCode: string;
	text: string;
	completed: boolean;
	isMilestone: true;
}

export type DashboardTask = AdhocTask | BatchMilestoneTask;

export interface ActivityFeedItem {
	id: string;
	type: 'reading' | 'stage' | 'status';
	batchId?: string;
	batchCode?: string;
	batchName: string;
	message: string;
	timestamp: string;
	tempC?: number | null;
	gravity?: number | null;
}

class DashboardStore {
	batches = $state<BatchSummaryDto[]>([]);
	equipment = $state<EquipmentDto[]>([]);
	loading = $state(true);
	refreshing = $state(false);
	error = $state<string | null>(null);
	lastUpdated = $state<Date | null>(null);
	adhocTasks = $state<AdhocTask[]>([]);
	completedMilestoneIds = $state<string[]>([]);
	recentActivity = $state<ActivityFeedItem[]>([]);

	private pollInterval: ReturnType<typeof setInterval> | null = null;
	private streamControllers: Map<string, AbortController> = new Map();
	private reconnectTimers: Map<string, ReturnType<typeof setTimeout>> = new Map();
	private retryCounts: Map<string, number> = new Map();
	private equipmentStreamController: AbortController | null = null;
	private equipmentReconnectTimer: ReturnType<typeof setTimeout> | null = null;
	private equipmentRetries = 0;
	private handleVisibilityChange: (() => void) | null = null;
	private isInitialized = false;

	// Active batches: in cellar or active brewhouse
	activeBatches = $derived.by(() => {
		return this.batches.filter(
			(b) => b.status === 'Brewing' || b.status === 'Fermenting' || b.status === 'Conditioning'
		);
	});

	activeBatchCount = $derived(this.activeBatches.length);

	// Fermentation vessels
	fermenters = $derived.by(() => {
		return this.equipment.filter(
			(e) =>
				e.type === 'Fermenter' ||
				e.subtype === 'ConicalFermenter' ||
				e.subtype === 'Carboy' ||
				e.subtype === 'PressureFermenter' ||
				e.subtype === 'StainlessBucket' ||
				e.subtype === 'Bucket'
		);
	});

	// Cellar utilization %
	cellarUtilizationPercent = $derived.by(() => {
		const totalFermenters = this.fermenters.length;
		if (totalFermenters === 0) {
			return this.activeBatches.length > 0 ? 100 : 0;
		}
		const occupiedCount = Math.min(this.activeBatches.length, totalFermenters);
		return Math.min(100, Math.round((occupiedCount / totalFermenters) * 100));
	});

	// Total volume currently under fermentation/conditioning
	totalVolumeUnderYeast = $derived.by(() => {
		return this.activeBatches.reduce((sum, b) => {
			const vol = b.measuredBatchSizeLiters ?? b.targetBatchSizeLiters ?? 0;
			return sum + vol;
		}, 0);
	});

	// Automated batch milestones derived from active batches
	milestoneTasks = $derived.by(() => {
		const list: BatchMilestoneTask[] = [];

		for (const batch of this.activeBatches) {
			const batchLabel = batch.batchCode || batch.name;

			if (batch.currentStage === 'Ferment') {
				if (batch.daysActive >= 3 && batch.daysActive <= 6) {
					const id = `milestone-${batch.id}-dryhop`;
					list.push({
						id,
						batchId: batch.id,
						batchCode: batch.batchCode,
						text: `${batchLabel}: Add dry hop additions (Day ${batch.daysActive})`,
						completed: this.completedMilestoneIds.includes(id),
						isMilestone: true
					});
				}

				if (batch.daysActive >= 7) {
					const id = `milestone-${batch.id}-gravity`;
					list.push({
						id,
						batchId: batch.id,
						batchCode: batch.batchCode,
						text: `${batchLabel}: Measure terminal gravity & test diacetyl`,
						completed: this.completedMilestoneIds.includes(id),
						isMilestone: true
					});
				}
			} else if (batch.currentStage === 'Condition') {
				const id = `milestone-${batch.id}-package`;
				list.push({
					id,
					batchId: batch.id,
					batchCode: batch.batchCode,
					text: `${batchLabel}: Prepare kegs or bottles for packaging`,
					completed: this.completedMilestoneIds.includes(id),
					isMilestone: true
				});
			}
		}

		return list;
	});

	// Unified task list
	allTasks = $derived.by(() => {
		return [...this.milestoneTasks, ...this.adhocTasks];
	});

	// Tasks due count (incomplete)
	tasksDueCount = $derived.by(() => {
		return this.allTasks.filter((t) => !t.completed).length;
	});

	// Equipment probe online stats
	probeStats = $derived.by(() => {
		const totalProbes = this.equipment.filter(
			(e) =>
				e.subtype === 'ISpindel' ||
				e.subtype === 'Tilt' ||
				e.subtype === 'GenericSensor' ||
				e.connectionType !== 'None'
		);
		const onlineProbes = totalProbes.filter(
			(e) => e.currentTemperatureC !== null && e.currentTemperatureC !== undefined
		);

		return {
			total: totalProbes.length,
			online: onlineProbes.length,
			manualCount: Math.max(0, this.equipment.length - totalProbes.length)
		};
	});

	init() {
		if (this.isInitialized || !browser) return;
		this.isInitialized = true;

		this.loadStoredTasks();
		this.loadDashboard();

		// Auto-poll every 30s when tab is active
		this.pollInterval = setInterval(() => {
			if (document.visibilityState === 'visible' && auth.isAuthenticated) {
				this.refreshData(true);
			}
		}, 30000);

		// Visibility change listener with stored reference for clean teardown
		this.handleVisibilityChange = () => {
			if (document.visibilityState === 'visible' && auth.isAuthenticated) {
				this.refreshData(true);
			}
		};
		document.addEventListener('visibilitychange', this.handleVisibilityChange);
	}

	destroy() {
		if (this.pollInterval) {
			clearInterval(this.pollInterval);
			this.pollInterval = null;
		}
		if (this.handleVisibilityChange) {
			document.removeEventListener('visibilitychange', this.handleVisibilityChange);
			this.handleVisibilityChange = null;
		}
		this.stopAllStreams();
		this.isInitialized = false;
	}

	private loadStoredTasks() {
		if (!browser) return;
		try {
			const setupKey = brewery.activeSetupId || 'default';
			const stored = localStorage.getItem(`brewyou_adhoc_tasks_${setupKey}`);
			if (stored) {
				this.adhocTasks = JSON.parse(stored);
			}

			const storedMilestones = localStorage.getItem(`brewyou_completed_milestones_${setupKey}`);
			if (storedMilestones) {
				this.completedMilestoneIds = JSON.parse(storedMilestones);
			}
		} catch {
			// Ignore localStorage parse errors
		}
	}

	private saveStoredTasks() {
		if (!browser) return;
		try {
			const setupKey = brewery.activeSetupId || 'default';
			localStorage.setItem(`brewyou_adhoc_tasks_${setupKey}`, JSON.stringify(this.adhocTasks));
			localStorage.setItem(
				`brewyou_completed_milestones_${setupKey}`,
				JSON.stringify(this.completedMilestoneIds)
			);
		} catch {
			// Ignore localStorage write errors
		}
	}

	async loadDashboard() {
		if (!auth.isAuthenticated) {
			this.loading = false;
			return;
		}

		this.loading = true;
		this.error = null;

		try {
			await this.fetchData();
		} catch (err: unknown) {
			this.error = (err as Error).message || 'Failed to load dashboard data';
		} finally {
			this.loading = false;
		}
	}

	async refreshData(silent = false) {
		if (!auth.isAuthenticated) return;
		if (!silent) this.refreshing = true;

		try {
			await this.fetchData();
		} catch (err: unknown) {
			if (!silent) {
				this.error = (err as Error).message || 'Failed to refresh dashboard data';
			}
		} finally {
			if (!silent) this.refreshing = false;
		}
	}

	private async fetchData() {
		const setupIdFilter =
			brewery.activeSetupId && brewery.activeSetupId !== DEFAULT_BREWERY_ID
				? brewery.activeSetupId
				: undefined;

		const [batchesRes, equipmentRes] = await Promise.all([
			api.batches.list({ limit: 50 }),
			api.equipment.list(undefined, undefined, undefined, undefined, setupIdFilter)
		]);

		this.batches = batchesRes ?? [];
		this.equipment = equipmentRes ?? [];
		this.lastUpdated = new Date();

		this.syncEventStreams();
	}

	private syncEventStreams() {
		if (!browser) return;

		// Active batches that require live telemetry
		const activeIds = new Set(
			this.activeBatches
				.filter((b) => b.status === 'Fermenting' || b.status === 'Brewing')
				.map((b) => b.id)
		);

		// Clean up streams for batches no longer active
		for (const id of this.streamControllers.keys()) {
			if (!activeIds.has(id)) {
				this.stopBatchTelemetry(id);
			}
		}

		// Clean up any pending reconnect timers for batches no longer active
		for (const id of this.reconnectTimers.keys()) {
			if (!activeIds.has(id)) {
				this.stopBatchTelemetry(id);
			}
		}

		// Connect new active batches
		for (const id of activeIds) {
			if (!this.streamControllers.has(id) && !this.reconnectTimers.has(id)) {
				this.connectBatchTelemetry(id);
			}
		}

		// Connect equipment telemetry stream if not already active
		if (!this.equipmentStreamController && !this.equipmentReconnectTimer) {
			this.connectEquipmentTelemetry();
		}
	}

	private async connectBatchTelemetry(batchId: string) {
		if (!browser || !auth.isAuthenticated) return;

		// Ensure any existing connection or timer is aborted first
		this.stopBatchTelemetry(batchId);

		const controller = new AbortController();
		this.streamControllers.set(batchId, controller);

		try {
			await api.batches.streamEquipmentReadings(batchId, {
				signal: controller.signal,
				onReading: (reading: BatchEquipmentReadingDto) => {
					// Reset retries on receipt of data
					this.retryCounts.set(batchId, 0);
					this.updateBatchTelemetry(batchId, reading);
				},
				onError: (err: Error) => {
					this.handleStreamError(batchId, err, controller);
				}
			});

			// If stream ended cleanly without explicit abort (e.g. 15-minute server connection lifetime limit)
			if (!controller.signal.aborted) {
				this.scheduleReconnect(batchId, 1000);
			}
		} catch (err: unknown) {
			this.handleStreamError(batchId, err as Error, controller);
		} finally {
			if (this.streamControllers.get(batchId) === controller) {
				this.streamControllers.delete(batchId);
			}
		}
	}

	private handleStreamError(batchId: string, err: Error, controller: AbortController) {
		if (controller.signal.aborted || !browser) return;

		// Halt retry on unauthorized or forbidden
		if (err instanceof ApiClientError && (err.status === 401 || err.status === 403)) {
			console.warn(`[Dashboard] Telemetry unauthorized for batch ${batchId}. Halting stream.`);
			this.stopBatchTelemetry(batchId);
			return;
		}

		// Halt retry if batch is gone
		if (err instanceof ApiClientError && err.status === 404) {
			console.warn(`[Dashboard] Batch ${batchId} not found. Halting stream.`);
			this.stopBatchTelemetry(batchId);
			return;
		}

		const retries = this.retryCounts.get(batchId) ?? 0;
		const maxRetries = 5;

		if (retries < maxRetries) {
			const delay = Math.min(2000 * Math.pow(2, retries), 30000);
			this.retryCounts.set(batchId, retries + 1);
			this.scheduleReconnect(batchId, delay);
		} else {
			console.warn(
				`[Dashboard] Telemetry stream for batch ${batchId} failed ${maxRetries} times. Falling back to periodic 30s polling.`
			);
			this.retryCounts.delete(batchId);
		}
	}

	private scheduleReconnect(batchId: string, delayMs: number) {
		if (!browser || !auth.isAuthenticated) return;

		const isStillActive = this.activeBatches.some(
			(b) => b.id === batchId && (b.status === 'Fermenting' || b.status === 'Brewing')
		);
		if (!isStillActive) return;

		const existing = this.reconnectTimers.get(batchId);
		if (existing) clearTimeout(existing);

		const timer = setTimeout(() => {
			this.reconnectTimers.delete(batchId);
			if (document.visibilityState === 'visible' && auth.isAuthenticated) {
				this.connectBatchTelemetry(batchId);
			}
		}, delayMs);

		this.reconnectTimers.set(batchId, timer);
	}

	private updateBatchTelemetry(batchId: string, reading: BatchEquipmentReadingDto) {
		const idx = this.batches.findIndex((b) => b.id === batchId);
		if (idx >= 0) {
			const batch = this.batches[idx];
			const nextGravity = reading.specificGravity ?? batch.currentGravity;
			let nextAbv = batch.alcoholByVolume;
			const og = batch.measuredOg ?? batch.targetOg;

			if (nextGravity && og && og > nextGravity) {
				nextAbv = Number(calculateActualAbv(og, nextGravity, settings.abvFormula).toFixed(2));
			}

			// Reactive array update triggers Svelte 5 derived runes
			this.batches[idx] = {
				...batch,
				vesselTempC: reading.temperatureC,
				currentGravity: nextGravity,
				alcoholByVolume: nextAbv
			};
		}

		// Also update equipment probe status if equipmentId is present
		if (reading.equipmentId) {
			const eqIdx = this.equipment.findIndex((e) => e.id === reading.equipmentId);
			if (eqIdx >= 0) {
				this.equipment[eqIdx] = {
					...this.equipment[eqIdx],
					currentTemperatureC: reading.temperatureC,
					temperatureUpdatedAt: reading.timestamp
				};
			}
		}
	}

	private stopBatchTelemetry(batchId: string) {
		const timer = this.reconnectTimers.get(batchId);
		if (timer) {
			clearTimeout(timer);
			this.reconnectTimers.delete(batchId);
		}

		const controller = this.streamControllers.get(batchId);
		if (controller) {
			controller.abort();
			this.streamControllers.delete(batchId);
		}

		this.retryCounts.delete(batchId);
	}

	private async connectEquipmentTelemetry() {
		if (!browser || !auth.isAuthenticated) return;

		this.stopEquipmentTelemetry();

		const controller = new AbortController();
		this.equipmentStreamController = controller;

		try {
			await api.equipment.streamTelemetry({
				signal: controller.signal,
				onReading: (reading: EquipmentTelemetryUpdateDto) => {
					this.equipmentRetries = 0;
					this.updateEquipmentTelemetry(reading);
				},
				onError: (err: Error) => {
					this.handleEquipmentStreamError(err, controller);
				}
			});

			if (!controller.signal.aborted) {
				this.scheduleEquipmentReconnect(1000);
			}
		} catch (err: unknown) {
			this.handleEquipmentStreamError(err as Error, controller);
		} finally {
			if (this.equipmentStreamController === controller) {
				this.equipmentStreamController = null;
			}
		}
	}

	private handleEquipmentStreamError(err: Error, controller: AbortController) {
		if (controller.signal.aborted || !browser) return;

		if (err instanceof ApiClientError && (err.status === 401 || err.status === 403)) {
			console.warn('[Dashboard] Equipment telemetry unauthorized. Halting stream.');
			this.stopEquipmentTelemetry();
			return;
		}

		const maxRetries = 5;
		if (this.equipmentRetries < maxRetries) {
			const delay = Math.min(2000 * Math.pow(2, this.equipmentRetries), 30000);
			this.equipmentRetries++;
			this.scheduleEquipmentReconnect(delay);
		} else {
			console.warn(`[Dashboard] Equipment telemetry stream failed ${maxRetries} times.`);
			this.equipmentRetries = 0;
		}
	}

	private scheduleEquipmentReconnect(delayMs: number) {
		if (!browser || !auth.isAuthenticated) return;

		if (this.equipmentReconnectTimer) {
			clearTimeout(this.equipmentReconnectTimer);
		}

		this.equipmentReconnectTimer = setTimeout(() => {
			this.equipmentReconnectTimer = null;
			if (document.visibilityState === 'visible' && auth.isAuthenticated) {
				this.connectEquipmentTelemetry();
			}
		}, delayMs);
	}

	private stopEquipmentTelemetry() {
		if (this.equipmentReconnectTimer) {
			clearTimeout(this.equipmentReconnectTimer);
			this.equipmentReconnectTimer = null;
		}
		if (this.equipmentStreamController) {
			this.equipmentStreamController.abort();
			this.equipmentStreamController = null;
		}
		this.equipmentRetries = 0;
	}

	private updateEquipmentTelemetry(reading: EquipmentTelemetryUpdateDto) {
		const eqIdx = this.equipment.findIndex((e) => e.id === reading.equipmentId);
		let eqName = 'Equipment';
		if (eqIdx >= 0) {
			eqName = this.equipment[eqIdx].name;
			this.equipment[eqIdx] = {
				...this.equipment[eqIdx],
				currentTemperatureC:
					reading.temperatureC !== undefined
						? reading.temperatureC
						: this.equipment[eqIdx].currentTemperatureC,
				temperatureUpdatedAt: reading.timestamp,
				currentVolumeLiters:
					reading.currentVolumeLiters !== undefined && reading.currentVolumeLiters !== null
						? reading.currentVolumeLiters
						: this.equipment[eqIdx].currentVolumeLiters,
				fillPercentage:
					reading.fillPercentage !== undefined && reading.fillPercentage !== null
						? reading.fillPercentage
						: this.equipment[eqIdx].fillPercentage
			};
		}

		// Also update batch telemetry if reading is linked to a batch
		if (reading.batchId) {
			const bIdx = this.batches.findIndex((b) => b.id === reading.batchId);
			if (bIdx >= 0) {
				const batch = this.batches[bIdx];
				const nextGravity = reading.specificGravity ?? batch.currentGravity;
				let nextAbv = batch.alcoholByVolume;
				if (nextGravity && batch.measuredOg && batch.measuredOg > nextGravity) {
					nextAbv = Math.round((batch.measuredOg - nextGravity) * 131.25 * 10) / 10;
				}
				this.batches[bIdx] = {
					...batch,
					vesselTempC: reading.temperatureC ?? batch.vesselTempC,
					currentGravity: nextGravity,
					alcoholByVolume: nextAbv
				};
			}
		}

		// Optionally prepend to activity feed if temperature is present
		if (reading.temperatureC !== undefined && reading.temperatureC !== null) {
			const matchedBatch = reading.batchId
				? this.batches.find((b) => b.id === reading.batchId)
				: undefined;
			const feedItem: ActivityFeedItem = {
				id: `iot-${Date.now()}-${reading.equipmentId}`,
				type: 'reading',
				batchId: reading.batchId ?? undefined,
				batchCode: matchedBatch?.batchCode,
				batchName: eqName,
				message: `${eqName}: ${reading.temperatureC.toFixed(1)}°C${reading.specificGravity ? ` | ${reading.specificGravity.toFixed(3)} SG` : ''}`,
				timestamp: reading.timestamp || new Date().toISOString(),
				tempC: reading.temperatureC,
				gravity: reading.specificGravity ?? undefined
			};
			this.recentActivity = [feedItem, ...this.recentActivity.slice(0, 19)];
		}
	}

	private stopAllStreams() {
		for (const timer of this.reconnectTimers.values()) {
			clearTimeout(timer);
		}
		this.reconnectTimers.clear();

		for (const controller of this.streamControllers.values()) {
			controller.abort();
		}
		this.streamControllers.clear();
		this.retryCounts.clear();

		this.stopEquipmentTelemetry();
	}

	// Ad-hoc task operations
	addAdhocTask(text: string) {
		const trimmed = text.trim();
		if (!trimmed) return;

		const newTask: AdhocTask = {
			id: `adhoc-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`,
			text: trimmed,
			completed: false,
			createdAt: new Date().toISOString()
		};

		this.adhocTasks = [newTask, ...this.adhocTasks];
		this.saveStoredTasks();
	}

	toggleTask(taskId: string) {
		if (taskId.startsWith('milestone-')) {
			if (this.completedMilestoneIds.includes(taskId)) {
				this.completedMilestoneIds = this.completedMilestoneIds.filter((id) => id !== taskId);
			} else {
				this.completedMilestoneIds = [...this.completedMilestoneIds, taskId];
			}
			this.saveStoredTasks();
			return;
		}

		const idx = this.adhocTasks.findIndex((t) => t.id === taskId);
		if (idx >= 0) {
			const updated = [...this.adhocTasks];
			updated[idx] = { ...updated[idx], completed: !updated[idx].completed };
			this.adhocTasks = updated;
			this.saveStoredTasks();
		}
	}

	deleteTask(taskId: string) {
		this.adhocTasks = this.adhocTasks.filter((t) => t.id !== taskId);
		this.saveStoredTasks();
	}

	// In-dashboard reading logger
	async logReading(batchId: string, req: AddBatchReadingRequest): Promise<BatchReadingDto> {
		const reading = await api.batches.addReading(batchId, req);

		// Update batch in local state immediately
		const idx = this.batches.findIndex((b) => b.id === batchId);
		if (idx >= 0) {
			const targetBatch = this.batches[idx];
			const nextGravity = req.specificGravity ?? targetBatch.currentGravity;
			let nextAbv = reading.alcoholByVolume ?? targetBatch.alcoholByVolume;
			const og = targetBatch.measuredOg ?? targetBatch.targetOg;
			if (nextAbv == null && nextGravity && og && og > nextGravity) {
				nextAbv = Number(calculateActualAbv(og, nextGravity, settings.abvFormula).toFixed(2));
			}
			this.batches[idx] = {
				...targetBatch,
				currentGravity: nextGravity,
				alcoholByVolume: nextAbv,
				vesselTempC: req.temperatureC ?? targetBatch.vesselTempC
			};

			// Prepend to activity feed
			const feedItem: ActivityFeedItem = {
				id: reading.id || `feed-${Date.now()}`,
				type: 'reading',
				batchId: targetBatch.id,
				batchCode: targetBatch.batchCode,
				batchName: targetBatch.name,
				message: `Logged reading: ${req.specificGravity ? `${req.specificGravity.toFixed(3)} SG` : ''} ${req.temperatureC !== undefined && req.temperatureC !== null ? `${req.temperatureC.toFixed(1)}°C` : ''}`,
				timestamp: new Date().toISOString(),
				tempC: req.temperatureC,
				gravity: req.specificGravity
			};

			this.recentActivity = [feedItem, ...this.recentActivity.slice(0, 19)];
		}

		return reading;
	}

	// In-dashboard stage advancement
	async advanceStage(batchId: string, targetStage: BrewStage) {
		const updated = await api.batches.advanceStage(batchId, { targetStage });

		const idx = this.batches.findIndex((b) => b.id === batchId);
		if (idx >= 0) {
			this.batches[idx] = {
				...this.batches[idx],
				currentStage: updated.currentStage,
				status: updated.status
			};

			const feedItem: ActivityFeedItem = {
				id: `stage-${Date.now()}`,
				type: 'stage',
				batchId: updated.id,
				batchCode: updated.batchCode,
				batchName: updated.name,
				message: `Advanced to ${updated.currentStage} stage`,
				timestamp: new Date().toISOString()
			};

			this.recentActivity = [feedItem, ...this.recentActivity.slice(0, 19)];
		}

		return updated;
	}
}

export const dashboard = new DashboardStore();
