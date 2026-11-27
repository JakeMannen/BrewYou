import { describe, it, expect } from 'vitest';
import type { BatchSummaryDto, EquipmentTelemetryUpdateDto } from '$lib/types/api';
import {
	applyBrewLinkSyncTempToBatches,
	applyEquipmentTelemetryToBatches,
	getActiveStageVesselIds,
	isActiveStageVessel,
	isBatchInProgress,
	shouldApplyTemperatureToBatch
} from './batchTelemetry';

function makeBatch(overrides: Partial<BatchSummaryDto> = {}): BatchSummaryDto {
	return {
		id: 'batch-1',
		batchCode: 'B-2026-11-27-01',
		name: 'Test Ale',
		beerStyle: 'Pale Ale',
		status: 'Brewing',
		currentStage: 'Mash',
		brewDate: '2026-11-27',
		daysActive: 0,
		targetOg: 1.052,
		targetFg: 1.011,
		colorSrm: 6,
		targetBatchSizeLiters: 20,
		createdAt: '2026-11-27T10:00:00Z',
		updatedAt: '2026-11-27T10:00:00Z',
		boilerId: 'boiler-1',
		fermenterId: 'ferm-1',
		vesselTempC: null,
		...overrides
	};
}

describe('batchTelemetry helpers', () => {
	it('identifies in-progress statuses accurately', () => {
		expect(isBatchInProgress({ status: 'Brewing' })).toBe(true);
		expect(isBatchInProgress({ status: 'Fermenting' })).toBe(true);
		expect(isBatchInProgress({ status: 'Conditioning' })).toBe(true);
		expect(isBatchInProgress({ status: 'Planned' })).toBe(false);
		expect(isBatchInProgress({ status: 'Completed' })).toBe(false);
		expect(isBatchInProgress({ status: 'Archived' })).toBe(false);
	});

	it('matches only the active stage vessel and prioritizes fermentation sensor in Ferment stage', () => {
		const mashBatch = makeBatch({
			status: 'Brewing',
			currentStage: 'Mash',
			mashTunId: 'mt-1',
			boilerId: 'boiler-1',
			fermenterId: 'ferm-1'
		});
		expect(isActiveStageVessel(mashBatch, 'mt-1')).toBe(true);
		expect(isActiveStageVessel(mashBatch, 'boiler-1')).toBe(true);
		expect(isActiveStageVessel(mashBatch, 'ferm-1')).toBe(false);

		const fermentWithoutSensor = makeBatch({
			status: 'Fermenting',
			currentStage: 'Ferment',
			boilerId: 'boiler-1',
			fermenterId: 'ferm-1',
			fermentationSensorId: null
		});
		expect(isActiveStageVessel(fermentWithoutSensor, 'ferm-1')).toBe(true);
		expect(isActiveStageVessel(fermentWithoutSensor, 'boiler-1')).toBe(false);

		const fermentWithSensor = makeBatch({
			status: 'Fermenting',
			currentStage: 'Ferment',
			fermenterId: 'ferm-1',
			fermentationSensorId: 'ispindel-1'
		});
		expect(isActiveStageVessel(fermentWithSensor, 'ispindel-1')).toBe(true);
		expect(isActiveStageVessel(fermentWithSensor, 'ferm-1')).toBe(false);
		expect(shouldApplyTemperatureToBatch(fermentWithSensor, 'ispindel-1')).toBe(true);
		expect(shouldApplyTemperatureToBatch(fermentWithSensor, 'ferm-1')).toBe(false);

		const completedBatch = makeBatch({
			status: 'Completed',
			currentStage: 'Package',
			boilerId: 'boiler-1',
			fermenterId: 'ferm-1'
		});
		expect(isActiveStageVessel(completedBatch, 'boiler-1')).toBe(false);
		expect(isActiveStageVessel(completedBatch, 'ferm-1')).toBe(false);
	});

	it('updates in-progress batch on SSE reading without overwriting completed or other-stage batches', () => {
		const completedSameBoiler = makeBatch({
			id: 'batch-done',
			status: 'Completed',
			currentStage: 'Package',
			boilerId: 'boiler-1',
			vesselTempC: 18.0
		});
		const fermentingSameBoiler = makeBatch({
			id: 'batch-ferm',
			status: 'Fermenting',
			currentStage: 'Ferment',
			boilerId: 'boiler-1',
			fermenterId: 'ferm-1',
			vesselTempC: 19.5
		});
		const activeMash = makeBatch({
			id: 'batch-mash',
			status: 'Brewing',
			currentStage: 'Mash',
			boilerId: 'boiler-1',
			fermenterId: 'ferm-2',
			vesselTempC: null
		});

		const reading: EquipmentTelemetryUpdateDto = {
			equipmentId: 'boiler-1',
			equipmentName: 'G30',
			temperatureC: 66.5,
			timestamp: '2026-11-27T12:00:00Z'
		};

		const updated = applyEquipmentTelemetryToBatches(
			[completedSameBoiler, fermentingSameBoiler, activeMash],
			reading,
			'Linear'
		);

		expect(updated[0].vesselTempC).toBe(18.0);
		expect(updated[1].vesselTempC).toBe(19.5);
		expect(updated[2].vesselTempC).toBe(66.5);
	});

	it('uses fermentation sensor temp and gravity over fermenter telemetry during Ferment stage even when batchId matches', () => {
		const fermentBatch = makeBatch({
			id: 'batch-ferm',
			status: 'Fermenting',
			currentStage: 'Ferment',
			measuredOg: 1.055,
			currentGravity: 1.022,
			alcoholByVolume: 4.33,
			fermenterId: 'ferm-1',
			fermentationSensorId: 'ispindel-1',
			vesselTempC: 20.4
		});

		// Fermenter reading with matching batchId must NOT overwrite sensor temperature, gravity, or ABV
		const fermenterReading: EquipmentTelemetryUpdateDto = {
			equipmentId: 'ferm-1',
			equipmentName: 'Glycol Conical',
			batchId: 'batch-ferm',
			temperatureC: 18.0,
			specificGravity: 1.04,
			timestamp: '2026-11-27T12:00:00Z'
		};
		const afterFermenter = applyEquipmentTelemetryToBatches(
			[fermentBatch],
			fermenterReading,
			'Linear'
		);
		expect(afterFermenter[0].vesselTempC).toBe(20.4);
		expect(afterFermenter[0].currentGravity).toBe(1.022);
		expect(afterFermenter[0].alcoholByVolume).toBe(4.33);

		// Fermentation sensor reading updates vesselTempC, gravity, and ABV
		const sensorReading: EquipmentTelemetryUpdateDto = {
			equipmentId: 'ispindel-1',
			equipmentName: 'iSpindel',
			batchId: 'batch-ferm',
			temperatureC: 21.1,
			specificGravity: 1.015,
			timestamp: '2026-11-27T12:05:00Z'
		};
		const afterSensor = applyEquipmentTelemetryToBatches(afterFermenter, sensorReading, 'Linear');
		expect(afterSensor[0].vesselTempC).toBe(21.1);
		expect(afterSensor[0].currentGravity).toBe(1.015);
		expect(afterSensor[0].alcoholByVolume).toBeGreaterThan(5);
	});

	it('collects only active stage vessel IDs and skips fermenter sync when fermentation sensor is selected', () => {
		const completed = makeBatch({
			id: 'b-done',
			status: 'Completed',
			currentStage: 'Package',
			boilerId: 'boiler-1',
			vesselTempC: 12.0
		});
		const fermentingWithSensor = makeBatch({
			id: 'b-ferm-sensor',
			status: 'Fermenting',
			currentStage: 'Ferment',
			fermenterId: 'ferm-sensor-backed',
			fermentationSensorId: 'ispindel-1',
			vesselTempC: 20.5
		});
		const fermentingNoSensor = makeBatch({
			id: 'b-ferm',
			status: 'Fermenting',
			currentStage: 'Ferment',
			boilerId: 'boiler-1',
			fermenterId: 'ferm-1',
			vesselTempC: 19.0
		});
		const boiling = makeBatch({
			id: 'b-boil',
			status: 'Brewing',
			currentStage: 'Boil',
			boilerId: 'boiler-1',
			fermenterId: 'ferm-2',
			vesselTempC: 98.0
		});

		const ids = getActiveStageVesselIds([
			completed,
			fermentingWithSensor,
			fermentingNoSensor,
			boiling
		]);
		expect(ids).toEqual(['ferm-1', 'boiler-1']);

		const syncedBoiler = applyBrewLinkSyncTempToBatches(
			[completed, fermentingWithSensor, fermentingNoSensor, boiling],
			'boiler-1',
			100.0
		);
		expect(syncedBoiler[0].vesselTempC).toBe(12.0);
		expect(syncedBoiler[1].vesselTempC).toBe(20.5);
		expect(syncedBoiler[2].vesselTempC).toBe(19.0);
		expect(syncedBoiler[3].vesselTempC).toBe(100.0);

		const syncedFerm = applyBrewLinkSyncTempToBatches(syncedBoiler, 'ferm-sensor-backed', 17.5);
		expect(syncedFerm[1].vesselTempC).toBe(20.5);
	});
});
