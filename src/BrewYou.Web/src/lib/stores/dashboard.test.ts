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

	it('computes fermenters and cellarUtilizationPercent correctly', () => {
		dashboard.batches = [...mockBatches];
		dashboard.equipment = [...mockEquipment];

		expect(dashboard.fermenters.length).toBe(1);
		expect(dashboard.fermenters[0].id).toBe('eq1');
		// 2 active batches with 1 fermenter => 100% cap
		expect(dashboard.cellarUtilizationPercent).toBe(100);
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
});
