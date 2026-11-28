import { beforeEach, describe, expect, it, vi } from 'vitest';
import { dashboard } from './dashboard.svelte';
import { api } from '$lib/api/client';
import { auth } from '$lib/stores/auth.svelte';
import type {
	BatchDetailDto,
	BatchReadingDto,
	BatchSummaryDto,
	EquipmentDto,
	UserDto
} from '$lib/types/api';

vi.mock('$app/environment', () => ({
	browser: true,
	dev: true,
	building: false,
	version: '1'
}));

vi.mock(import('$lib/api/client'), async (importOriginal) => {
	const actual = await importOriginal();
	return {
		...actual,
		api: {
			...actual.api,
			batches: {
				...actual.api.batches,
				list: vi.fn(),
				addReading: vi.fn(),
				advanceStage: vi.fn(),
				streamEquipmentReadings: vi.fn().mockImplementation((_id, opts) => {
					opts?.onReading?.({
						id: 'r-stream',
						temperatureC: 20.5,
						specificGravity: 1.025,
						timestamp: new Date().toISOString()
					});
					return Promise.resolve();
				})
			},
			equipment: {
				...actual.api.equipment,
				list: vi.fn(),
				streamTelemetry: vi.fn().mockImplementation((opts) => {
					opts?.onReading?.({
						equipmentId: 'eq1',
						batchId: 'b1',
						currentTemperatureC: 20.5,
						temperatureC: 20.5,
						specificGravity: 1.025,
						timestamp: new Date().toISOString()
					});
					return Promise.resolve();
				})
			}
		}
	};
});

describe('Dashboard Store (dashboard.svelte.ts)', () => {
	const mockBatches: BatchSummaryDto[] = [
		{
			id: 'b1',
			batchCode: '#B001',
			name: 'Hazy IPA',
			beerStyle: 'IPA',
			status: 'Fermenting',
			currentStage: 'Ferment',
			brewDate: '2026-09-10',
			daysActive: 4,
			targetOg: 1.065,
			targetFg: 1.012,
			colorSrm: 5.5,
			targetBatchSizeLiters: 20,
			measuredBatchSizeLiters: 21,
			fermenterId: 'eq1',
			createdAt: '',
			updatedAt: ''
		},
		{
			id: 'b2',
			batchCode: '#B002',
			name: 'Pilsner',
			beerStyle: 'Czech Pilsner',
			status: 'Conditioning',
			currentStage: 'Condition',
			brewDate: '2026-08-20',
			daysActive: 10,
			targetOg: 1.048,
			targetFg: 1.01,
			colorSrm: 3.2,
			targetBatchSizeLiters: 50,
			createdAt: '',
			updatedAt: ''
		},
		{
			id: 'b3',
			batchCode: '#B003',
			name: 'Old Ale',
			beerStyle: 'Ale',
			status: 'Completed',
			currentStage: 'Package',
			brewDate: '2026-09-01',
			daysActive: 20,
			targetOg: 1.05,
			targetFg: 1.012,
			colorSrm: 12.0,
			targetBatchSizeLiters: 25,
			createdAt: '',
			updatedAt: ''
		}
	];

	const mockEquipment: EquipmentDto[] = [
		{
			id: 'eq1',
			brewerySetupId: 's1',
			name: 'FV 1',
			type: 'Fermenter',
			subtype: 'ConicalFermenter',
			capacity: 30,
			unit: 'Liters',
			capacityLiters: 30,
			currentVolume: 21,
			currentVolumeLiters: 21,
			fillPercentage: 70,
			createdAt: '',
			updatedAt: ''
		},
		{
			id: 'eq2',
			brewerySetupId: 's1',
			name: 'BK 1',
			type: 'Boiler',
			subtype: 'Pan',
			capacity: 40,
			unit: 'Liters',
			capacityLiters: 40,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '',
			updatedAt: ''
		}
	];

	beforeEach(() => {
		vi.clearAllMocks();
		dashboard.destroy();
		dashboard.batches = [];
		dashboard.equipment = [];
		dashboard.adhocTasks = [];
		dashboard.completedMilestoneIds = [];
		dashboard.recentActivity = [];
		dashboard.error = null;
		dashboard.loading = true;
	});

	it('computes active batches and activeBatchCount correctly', () => {
		dashboard.batches = [...mockBatches];
		expect(dashboard.activeBatches.length).toBe(2);
		expect(dashboard.activeBatchCount).toBe(2);
		expect(dashboard.activeBatches.some((b) => b.id === 'b3')).toBe(false);
	});

	it('computes fermenters and cellarUtilizationPercent from distinct active-setup fermenters', () => {
		const secondFermenter: EquipmentDto = {
			...mockEquipment[0],
			id: 'eq3',
			name: 'FV 2'
		};
		const otherSetupBatch: BatchSummaryDto = {
			...mockBatches[1],
			id: 'b-other-setup',
			fermenterId: 'ferm-in-another-setup'
		};
		const historicalBatch: BatchSummaryDto = {
			...mockBatches[0],
			id: 'b-hist',
			fermenterId: 'eq3',
			isHistorical: true
		};

		dashboard.batches = [mockBatches[0], otherSetupBatch, historicalBatch];
		dashboard.equipment = [mockEquipment[0], secondFermenter, mockEquipment[1]];

		expect(dashboard.fermenters.length).toBe(2);
		// Only eq1 is occupied by a live cellar batch in the active setup -> 1/2 = 50%
		expect(dashboard.cellarUtilizationPercent).toBe(50);

		// When only 1 fermenter exists and it is occupied -> 100%
		dashboard.equipment = [...mockEquipment];
		expect(dashboard.cellarUtilizationPercent).toBe(100);
	});

	it('does not count boil or mash batches towards cellar utilization or volume under yeast', () => {
		const boilBatch: BatchSummaryDto = {
			id: 'b-boil',
			batchCode: '#B004',
			name: 'Boiling Stout',
			beerStyle: 'Stout',
			status: 'Brewing',
			currentStage: 'Boil',
			brewDate: '2026-10-27',
			daysActive: 0,
			targetOg: 1.06,
			targetFg: 1.015,
			colorSrm: 30,
			targetBatchSizeLiters: 25,
			createdAt: '',
			updatedAt: ''
		};

		// Only the boil batch
		dashboard.batches = [boilBatch];
		dashboard.equipment = [...mockEquipment];

		expect(dashboard.activeBatches.length).toBe(1);
		expect(dashboard.activeBatchCount).toBe(1);
		expect(dashboard.activeCellarBatches.length).toBe(0);
		expect(dashboard.cellarUtilizationPercent).toBe(0);
		expect(dashboard.totalVolumeUnderYeast).toBe(0);
	});

	it('computes total volume under yeast correctly', () => {
		dashboard.batches = [...mockBatches];
		// b1 has measuredBatchSizeLiters 21, b2 has targetBatchSizeLiters 50 => 71
		expect(dashboard.totalVolumeUnderYeast).toBe(71);
	});

	it('generates automated milestones for active fermentation and condition batches', () => {
		dashboard.batches = [...mockBatches];
		const milestones = dashboard.milestoneTasks;
		expect(milestones.length).toBeGreaterThanOrEqual(2);

		const dryHopTask = milestones.find((m) => m.id.includes('dryhop'));
		expect(dryHopTask).toBeDefined();
		expect(dryHopTask?.batchId).toBe('b1');

		const packageTask = milestones.find((m) => m.id.includes('package'));
		expect(packageTask).toBeDefined();
		expect(packageTask?.batchId).toBe('b2');
	});

	it('manages adhoc task lifecycle: add, toggle, and delete', () => {
		expect(dashboard.adhocTasks.length).toBe(0);

		dashboard.addAdhocTask('Clean keg lines');
		expect(dashboard.adhocTasks.length).toBe(1);
		expect(dashboard.adhocTasks[0].text).toBe('Clean keg lines');
		expect(dashboard.adhocTasks[0].completed).toBe(false);

		const taskId = dashboard.adhocTasks[0].id;
		dashboard.toggleTask(taskId);
		expect(dashboard.adhocTasks[0].completed).toBe(true);

		dashboard.toggleTask(taskId);
		expect(dashboard.adhocTasks[0].completed).toBe(false);

		dashboard.deleteTask(taskId);
		expect(dashboard.adhocTasks.length).toBe(0);
	});

	it('handles milestone task completion toggling', () => {
		const milestoneId = 'milestone-b1-dryhop';
		dashboard.toggleTask(milestoneId);
		expect(dashboard.completedMilestoneIds.includes(milestoneId)).toBe(true);

		dashboard.toggleTask(milestoneId);
		expect(dashboard.completedMilestoneIds.includes(milestoneId)).toBe(false);
	});

	it('computes allTasks, tasksDueCount, and probeStats', () => {
		dashboard.batches = [...mockBatches];
		dashboard.equipment = [...mockEquipment];
		dashboard.addAdhocTask('Sanitize kegs');

		expect(dashboard.allTasks.length).toBeGreaterThanOrEqual(3);
		expect(dashboard.tasksDueCount).toBeGreaterThanOrEqual(3);

		expect(dashboard.probeStats.total).toBe(2);
		expect(dashboard.probeStats.manualCount).toBe(0);
	});

	it('successfully loads dashboard data via loadDashboard', async () => {
		const mockUser: UserDto = {
			id: 'u1',
			email: 'test@brewyou.local',
			displayName: 'Brewer',
			preferredLanguage: 'en',
			preferredVolumeUnit: 'Liters'
		};
		auth.user = mockUser;

		vi.mocked(api.batches.list).mockResolvedValueOnce(mockBatches);
		vi.mocked(api.equipment.list).mockResolvedValueOnce(mockEquipment);

		await dashboard.loadDashboard();

		expect(dashboard.loading).toBe(false);
		expect(dashboard.error).toBeNull();
		expect(dashboard.batches.length).toBe(3);
		expect(dashboard.equipment.length).toBe(2);
		expect(dashboard.lastUpdated).toBeInstanceOf(Date);
	});

	it('handles error in loadDashboard gracefully', async () => {
		const mockUser: UserDto = {
			id: 'u1',
			email: 'test@brewyou.local',
			displayName: 'Brewer',
			preferredLanguage: 'en',
			preferredVolumeUnit: 'Liters'
		};
		auth.user = mockUser;

		vi.mocked(api.batches.list).mockRejectedValueOnce(new Error('Network error'));
		vi.mocked(api.equipment.list).mockResolvedValueOnce([]);

		await dashboard.loadDashboard();

		expect(dashboard.loading).toBe(false);
		expect(dashboard.error).toBe('Network error');
	});

	it('records a reading and prepends to recentActivity', async () => {
		dashboard.batches = [...mockBatches];
		const mockReading = {
			id: 'r1',
			batchId: 'b1',
			temperatureC: 19.5,
			specificGravity: 1.035,
			readingDate: new Date().toISOString()
		};

		vi.mocked(api.batches.addReading).mockResolvedValueOnce(
			mockReading as unknown as BatchReadingDto
		);

		const result = await dashboard.logReading('b1', {
			temperatureC: 19.5,
			specificGravity: 1.035
		});

		expect(result).toEqual(mockReading);
		expect(dashboard.recentActivity.length).toBe(1);
		expect(dashboard.recentActivity[0].type).toBe('reading');
		expect(dashboard.recentActivity[0].tempC).toBe(19.5);
		expect(dashboard.recentActivity[0].gravity).toBe(1.035);
	});

	it('advances batch stage and adds to activity feed', async () => {
		dashboard.batches = [...mockBatches];
		const updatedBatch = {
			...mockBatches[0],
			currentStage: 'Condition',
			status: 'Conditioning'
		};

		vi.mocked(api.batches.advanceStage).mockResolvedValueOnce(
			updatedBatch as unknown as BatchDetailDto
		);

		const result = await dashboard.advanceStage('b1', 'Condition');

		expect(result.currentStage).toBe('Condition');
		expect(dashboard.batches.find((b) => b.id === 'b1')?.currentStage).toBe('Condition');
		expect(dashboard.recentActivity.some((a) => a.type === 'stage')).toBe(true);
	});

	it('initializes polling and teardown via destroy', async () => {
		const mockUser: UserDto = {
			id: 'u1',
			email: 'test@brewyou.local',
			displayName: 'Brewer',
			preferredLanguage: 'en',
			preferredVolumeUnit: 'Liters'
		};
		auth.user = mockUser;
		vi.mocked(api.batches.list).mockResolvedValue(mockBatches);
		vi.mocked(api.equipment.list).mockResolvedValue(mockEquipment);

		const addEventListenerMock = vi.fn();
		const removeEventListenerMock = vi.fn();
		vi.stubGlobal('document', {
			visibilityState: 'visible',
			addEventListener: addEventListenerMock,
			removeEventListener: removeEventListenerMock
		});

		dashboard.init();
		await dashboard.refreshData();
		dashboard.destroy();

		expect(removeEventListenerMock).toHaveBeenCalledWith('visibilitychange', expect.any(Function));
	});

	it('does not overwrite temperature or inject 0C on offline BrewLink SSE updates without temperatureC', async () => {
		auth.user = {
			id: 'u1',
			email: 'test@brewyou.local',
			displayName: 'Brewer',
			preferredLanguage: 'en',
			preferredVolumeUnit: 'Liters'
		};

		const testEquipment: EquipmentDto[] = [
			{
				...mockEquipment[1],
				id: 'eq-offline',
				currentTemperatureC: undefined,
				temperatureUpdatedAt: undefined,
				connectionType: 'BrewLink'
			},
			{
				...mockEquipment[1],
				id: 'eq-prev-online',
				currentTemperatureC: 65.4,
				temperatureUpdatedAt: '2026-11-14T18:00:00Z',
				connectionType: 'BrewLink'
			}
		];

		vi.mocked(api.batches.list).mockResolvedValue([]);
		vi.mocked(api.equipment.list).mockResolvedValue(testEquipment);
		vi.mocked(api.equipment.streamTelemetry).mockImplementationOnce(async (opts) => {
			opts?.onReading?.({
				equipmentId: 'eq-offline',
				equipmentName: 'G30-OFF',
				temperatureC: null,
				timestamp: '2026-11-14T18:05:00Z',
				source: 'BrewLinkPoll',
				connected: false
			});
			opts?.onReading?.({
				equipmentId: 'eq-prev-online',
				equipmentName: 'G30-ON',
				temperatureC: null,
				timestamp: '2026-11-14T18:05:00Z',
				source: 'BrewLinkWs',
				connected: false
			});
		});

		vi.stubGlobal('document', {
			visibilityState: 'visible',
			addEventListener: vi.fn(),
			removeEventListener: vi.fn()
		});

		dashboard.init();
		await dashboard.refreshData();
		await Promise.resolve();
		dashboard.destroy();

		const offItem = dashboard.equipment.find((e) => e.id === 'eq-offline');
		const prevItem = dashboard.equipment.find((e) => e.id === 'eq-prev-online');
		expect(offItem?.currentTemperatureC).toBeUndefined();
		expect(offItem?.temperatureUpdatedAt).toBeUndefined();
		expect(prevItem?.currentTemperatureC).toBe(65.4);
		expect(prevItem?.temperatureUpdatedAt).toBe('2026-11-14T18:00:00Z');
	});

	it('respects fermentationSensorId precedence in logReading', async () => {
		const sensorBackedBatch: BatchSummaryDto = {
			...mockBatches[0],
			id: 'b-sensor',
			fermenterId: 'eq1',
			fermentationSensorId: 'ispindel-1',
			vesselTempC: 20.4,
			currentGravity: 1.022
		};
		dashboard.batches = [sensorBackedBatch];

		vi.mocked(api.batches.addReading).mockResolvedValue({
			id: 'r-ferm',
			batchId: 'b-sensor',
			timestamp: '2026-11-28T12:00:00Z',
			specificGravity: 1.03,
			temperatureC: 17.5,
			createdAt: '2026-11-28T12:00:00Z'
		} as unknown as BatchReadingDto);

		// Logging against the fermenter must not overwrite live sensor temp/gravity
		await dashboard.logReading('b-sensor', {
			equipmentId: 'eq1',
			specificGravity: 1.03,
			temperatureC: 17.5
		});
		expect(dashboard.batches[0].vesselTempC).toBe(20.4);
		expect(dashboard.batches[0].currentGravity).toBe(1.022);

		// Logging against the assigned sensor updates temp and gravity
		await dashboard.logReading('b-sensor', {
			equipmentId: 'ispindel-1',
			specificGravity: 1.015,
			temperatureC: 21.0
		});
		expect(dashboard.batches[0].vesselTempC).toBe(21.0);
		expect(dashboard.batches[0].currentGravity).toBe(1.015);
	});

	it('re-fetches batches and restarts equipment streams on advanceStage', async () => {
		auth.user = {
			id: 'u1',
			email: 'test@brewyou.local',
			displayName: 'Brewer',
			preferredLanguage: 'en',
			preferredVolumeUnit: 'Liters'
		};
		dashboard.batches = [...mockBatches];
		const updatedDetail = {
			...mockBatches[0],
			currentStage: 'Condition',
			status: 'Conditioning'
		};
		const refreshedSummary: BatchSummaryDto[] = [
			{
				...mockBatches[0],
				currentStage: 'Condition',
				status: 'Conditioning',
				fermenterName: 'FV 1'
			}
		];

		vi.mocked(api.batches.advanceStage).mockResolvedValueOnce(
			updatedDetail as unknown as BatchDetailDto
		);
		vi.mocked(api.batches.list).mockResolvedValueOnce(refreshedSummary);
		vi.mocked(api.equipment.list).mockResolvedValueOnce(mockEquipment);

		await dashboard.advanceStage('b1', 'Condition', { fermenterId: 'eq1' });

		expect(api.batches.advanceStage).toHaveBeenCalledWith('b1', {
			fermenterId: 'eq1',
			targetStage: 'Condition'
		});
		expect(api.batches.list).toHaveBeenCalledTimes(1);
		expect(api.equipment.list).toHaveBeenCalledTimes(1);
		expect(api.equipment.streamTelemetry).toHaveBeenCalled();
		expect(dashboard.batches[0].currentStage).toBe('Condition');
	});
});
