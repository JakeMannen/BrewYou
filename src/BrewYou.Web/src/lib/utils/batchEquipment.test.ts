import { describe, expect, it } from 'vitest';
import type { BatchDetailDto, BatchSummaryDto, EquipmentDto } from '$lib/types/api';
import {
	getBatchActiveStageEquipmentIds,
	getOccupiedEquipmentCount,
	isEquipmentOccupied,
	resolveLogTempEquipmentMode,
	resolveStageBrewLinkEquipment,
	resolveStageEquipment
} from './batchEquipment';

function makeEq(overrides: Partial<EquipmentDto> & { id: string; name: string }): EquipmentDto {
	return {
		brewerySetupId: 'setup-1',
		type: 'Fermenter',
		subtype: 'ConicalFermenter',
		capacity: 30,
		unit: 'Liters',
		capacityLiters: 30,
		currentVolume: 0,
		currentVolumeLiters: 0,
		fillPercentage: 0,
		createdAt: '2026-11-28T00:00:00Z',
		updatedAt: '2026-11-28T00:00:00Z',
		...overrides
	};
}

function makeBatchSummary(overrides: Partial<BatchSummaryDto> = {}): BatchSummaryDto {
	return {
		id: 'batch-1',
		batchCode: 'B-2026-11-28-01',
		name: 'Test Ale',
		beerStyle: 'Pale Ale',
		status: 'Brewing',
		currentStage: 'Mash',
		brewDate: '2026-11-28',
		daysActive: 0,
		targetOg: 1.052,
		targetFg: 1.01,
		colorSrm: 6,
		targetBatchSizeLiters: 20,
		createdAt: '2026-11-28T08:00:00Z',
		updatedAt: '2026-11-28T08:00:00Z',
		...overrides
	};
}

function makeBatchDetail(overrides: Partial<BatchDetailDto> = {}): BatchDetailDto {
	return {
		id: 'batch-1',
		userId: 'user-1',
		batchCode: 'B-2026-11-28-01',
		name: 'Test Ale',
		beerStyle: 'Pale Ale',
		status: 'Brewing',
		currentStage: 'Mash',
		brewDate: '2026-11-28',
		daysActive: 0,
		targetOg: 1.052,
		targetFg: 1.01,
		targetAbv: 5.5,
		targetIbu: 35,
		targetColorSrm: 6,
		targetBatchSizeLiters: 20,
		boilTimeMinutes: 60,
		efficiencyPercent: 72,
		createdAt: '2026-11-28T08:00:00Z',
		updatedAt: '2026-11-28T08:00:00Z',
		readings: [],
		ingredients: [],
		mashSteps: [],
		fermentationSteps: [],
		stageHistory: [],
		sensorAssignments: [],
		...overrides
	};
}

describe('batchEquipment shared helpers', () => {
	const hlt = makeEq({ id: 'hlt-1', name: 'HLT 1', type: 'Boiler', subtype: 'Pan' });
	const mashTun = makeEq({
		id: 'mt-1',
		name: 'Mash Tun',
		type: 'Boiler',
		subtype: 'MashTun'
	});
	const boiler = makeEq({
		id: 'boiler-1',
		name: 'G30 Connect',
		type: 'Boiler',
		subtype: 'AllInOne',
		connectionType: 'BrewLink'
	});
	const fermenter = makeEq({
		id: 'ferm-1',
		name: 'Conical 30L',
		type: 'Fermenter',
		subtype: 'ConicalFermenter'
	});
	const keg = makeEq({
		id: 'keg-1',
		name: 'Corny Keg 19L',
		type: 'Keg',
		subtype: 'Cornelius',
		capacity: 19,
		capacityLiters: 19,
		assignedBatchId: 'batch-done'
	});
	const sensor = makeEq({
		id: 'sensor-1',
		name: 'iSpindel Red',
		type: 'Sensor',
		subtype: 'ISpindel'
	});
	const stepSensor = makeEq({
		id: 'sensor-mash-step',
		name: 'Mash Probe',
		type: 'Sensor',
		subtype: 'GenericSensor'
	});
	const tap = makeEq({ id: 'tap-1', name: 'Tap 1', type: 'Tap', subtype: 'Other' });
	const gateway = makeEq({
		id: 'gw-1',
		name: 'BrewLink GW',
		type: 'Sensor',
		subtype: 'BrewLink',
		connectionType: 'BrewLink'
	});
	const equipmentList = [hlt, mashTun, boiler, fermenter, keg, sensor, stepSensor, tap, gateway];

	describe('resolveStageEquipment', () => {
		it('prioritizes mash step sensor, stage sensor, and global cellar sensor over vessels', () => {
			const batch = makeBatchDetail({
				mashTunId: mashTun.id,
				boilerId: boiler.id,
				fermenterId: fermenter.id,
				packagingVesselId: keg.id,
				sensorAssignments: [
					{
						id: 'sa-step',
						equipmentId: stepSensor.id,
						equipmentName: stepSensor.name,
						stage: 'Mash',
						batchMashStepId: 'step-1'
					},
					{
						id: 'sa-global',
						equipmentId: sensor.id,
						equipmentName: sensor.name,
						stage: null,
						batchMashStepId: null
					}
				]
			});

			expect(resolveStageEquipment(batch, equipmentList, 'Mash', 'step-1')?.id).toBe(stepSensor.id);
			expect(resolveStageEquipment(batch, equipmentList, 'Boil')?.id).toBe(boiler.id);
			// Global sensor beats fermenter in both Ferment and Condition
			expect(resolveStageEquipment(batch, equipmentList, 'Ferment')?.id).toBe(sensor.id);
			expect(resolveStageEquipment(batch, equipmentList, 'Condition')?.id).toBe(sensor.id);
			expect(resolveStageEquipment(batch, equipmentList, 'Package')?.id).toBe(keg.id);
		});

		it('falls back to stage vessel when no sensor applies', () => {
			const batch = makeBatchDetail({
				boilerId: boiler.id,
				boilerName: 'G30 Connect',
				fermenterId: fermenter.id,
				fermenterName: 'Conical 30L'
			});

			expect(resolveStageEquipment(batch, equipmentList, 'Mash')?.id).toBe(boiler.id);
			expect(resolveStageEquipment(batch, equipmentList, 'Boil')?.id).toBe(boiler.id);
			expect(resolveStageEquipment(batch, equipmentList, 'Ferment')?.id).toBe(fermenter.id);
			expect(resolveStageEquipment(batch, equipmentList, 'Condition')?.id).toBe(fermenter.id);
		});
	});

	describe('resolveStageBrewLinkEquipment', () => {
		it('returns the BrewLink vessel only for stages controlled by BrewLink equipment', () => {
			const batch = makeBatchDetail({
				boilerId: boiler.id,
				fermenterId: fermenter.id
			});

			expect(resolveStageBrewLinkEquipment(batch, equipmentList, 'Mash')?.id).toBe(boiler.id);
			expect(resolveStageBrewLinkEquipment(batch, equipmentList, 'Boil')?.id).toBe(boiler.id);
			expect(resolveStageBrewLinkEquipment(batch, equipmentList, 'Ferment')).toBeNull();
		});
	});

	describe('isEquipmentOccupied and getOccupiedEquipmentCount', () => {
		it('tracks stage-aware occupancy for HLT, MashTun, Boiler, Fermenter, Sensor, Package, and Keg assignedBatchId', () => {
			const mashBatch = makeBatchSummary({
				id: 'b-mash',
				status: 'Brewing',
				currentStage: 'Mash',
				hltId: hlt.id,
				mashTunId: mashTun.id,
				boilerId: boiler.id,
				fermenterId: fermenter.id
			});
			expect(getBatchActiveStageEquipmentIds(mashBatch)).toEqual([hlt.id, mashTun.id, boiler.id]);
			expect(isEquipmentOccupied(hlt.id, [mashBatch])).toBe(true);
			expect(isEquipmentOccupied(mashTun.id, [mashBatch])).toBe(true);
			expect(isEquipmentOccupied(boiler.id, [mashBatch])).toBe(true);
			expect(isEquipmentOccupied(fermenter.id, [mashBatch])).toBe(false);

			// Once fermenting, boiler is free and fermenter + sensor are occupied
			const fermentBatch = makeBatchSummary({
				id: 'b-ferm',
				status: 'Fermenting',
				currentStage: 'Ferment',
				boilerId: boiler.id,
				fermenterId: fermenter.id,
				fermentationSensorId: sensor.id
			});
			expect(isEquipmentOccupied(boiler.id, [fermentBatch])).toBe(false);
			expect(isEquipmentOccupied(fermenter.id, [fermentBatch])).toBe(true);
			expect(isEquipmentOccupied(sensor.id, [fermentBatch])).toBe(true);

			// excludeBatchId allows the owning batch to see its own vessel as not occupied by others
			expect(isEquipmentOccupied(fermenter.id, [fermentBatch], fermenter, 'b-ferm')).toBe(false);

			// Keg assignedBatchId counts as occupied even with no active batches
			expect(isEquipmentOccupied(keg.id, [], keg)).toBe(true);
			expect(isEquipmentOccupied(keg.id, [], keg, 'batch-done')).toBe(false);

			// Planned and historical batches never occupy equipment
			const planned = makeBatchSummary({
				id: 'b-planned',
				status: 'Planned',
				currentStage: 'Mash',
				boilerId: boiler.id
			});
			const historical = makeBatchSummary({
				id: 'b-hist',
				status: 'Fermenting',
				currentStage: 'Ferment',
				fermenterId: fermenter.id,
				isHistorical: true
			});
			expect(isEquipmentOccupied(boiler.id, [planned])).toBe(false);
			expect(isEquipmentOccupied(fermenter.id, [historical])).toBe(false);

			expect(getOccupiedEquipmentCount([boiler, fermenter, sensor, keg], [fermentBatch])).toBe(3);
		});
	});

	describe('resolveLogTempEquipmentMode', () => {
		it('locks read-only only when defaultEquipmentId exists among eligible equipment', () => {
			expect(resolveLogTempEquipmentMode(boiler.id, equipmentList)).toEqual({
				locked: true,
				equipmentId: boiler.id,
				equipmentName: boiler.name
			});

			// Deleted vessel falls back to unlocked select mode
			expect(resolveLogTempEquipmentMode('deleted-vessel-id', equipmentList)).toEqual({
				locked: false,
				equipmentId: '',
				equipmentName: null
			});

			// Ineligible equipment (Tap, BrewLink gateway) falls back to unlocked select mode
			expect(resolveLogTempEquipmentMode(tap.id, equipmentList).locked).toBe(false);
			expect(resolveLogTempEquipmentMode(gateway.id, equipmentList).locked).toBe(false);
			expect(resolveLogTempEquipmentMode(null, equipmentList).locked).toBe(false);
		});
	});
});
