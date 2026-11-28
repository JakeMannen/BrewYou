import { calculateActualAbv } from '$lib/calculators/brewing';
import type { AbvFormula } from '$lib/stores/settings.svelte';
import type { BatchSummaryDto, EquipmentTelemetryUpdateDto } from '$lib/types/api';

export function isBatchInProgress(batch: Pick<BatchSummaryDto, 'status'>): boolean {
	return (
		batch.status === 'Brewing' || batch.status === 'Fermenting' || batch.status === 'Conditioning'
	);
}

export function isActiveStageVessel(
	batch: Pick<
		BatchSummaryDto,
		'status' | 'currentStage' | 'mashTunId' | 'boilerId' | 'fermenterId' | 'fermentationSensorId'
	>,
	equipmentId: string | null | undefined
): boolean {
	if (!equipmentId || !isBatchInProgress(batch)) return false;

	switch (batch.currentStage) {
		case 'Mash':
			return batch.mashTunId === equipmentId || batch.boilerId === equipmentId;
		case 'Boil':
			return batch.boilerId === equipmentId;
		case 'Ferment':
		case 'Condition':
			if (batch.fermentationSensorId) {
				return batch.fermentationSensorId === equipmentId;
			}
			return batch.fermenterId === equipmentId;
		default:
			return false;
	}
}

export function shouldApplyTelemetryToBatch(
	batch: Pick<BatchSummaryDto, 'currentStage' | 'fermentationSensorId'>,
	equipmentId: string | null | undefined
): boolean {
	if (
		(batch.currentStage === 'Ferment' || batch.currentStage === 'Condition') &&
		batch.fermentationSensorId
	) {
		return Boolean(equipmentId) && equipmentId === batch.fermentationSensorId;
	}
	return true;
}

export const shouldApplyTemperatureToBatch = shouldApplyTelemetryToBatch;

export function getActiveStageVesselIds(batches: BatchSummaryDto[]): string[] {
	const eqIds: string[] = [];
	const addUnique = (id?: string | null) => {
		if (id && !eqIds.includes(id)) {
			eqIds.push(id);
		}
	};

	for (const batch of batches) {
		if (!isBatchInProgress(batch)) continue;

		switch (batch.currentStage) {
			case 'Mash':
				addUnique(batch.mashTunId);
				addUnique(batch.boilerId);
				break;
			case 'Boil':
				addUnique(batch.boilerId);
				break;
			case 'Ferment':
			case 'Condition':
				if (!batch.fermentationSensorId) {
					addUnique(batch.fermenterId);
				}
				break;
			default:
				break;
		}
	}

	return eqIds;
}

export function applyEquipmentTelemetryToBatches(
	batches: BatchSummaryDto[],
	reading: EquipmentTelemetryUpdateDto,
	abvFormula: AbvFormula
): BatchSummaryDto[] {
	let updatedAny = false;
	const nextBatches = batches.map((batch) => {
		const isDirectBatchMatch =
			Boolean(reading.batchId) && batch.id === reading.batchId && isBatchInProgress(batch);
		const isVesselMatch = !reading.batchId && isActiveStageVessel(batch, reading.equipmentId);

		if (!isDirectBatchMatch && !isVesselMatch) {
			return batch;
		}

		const allowTelemetry = shouldApplyTelemetryToBatch(batch, reading.equipmentId);
		const nextGravity = allowTelemetry
			? (reading.specificGravity ?? batch.currentGravity)
			: batch.currentGravity;
		let nextAbv = batch.alcoholByVolume;
		const og = batch.measuredOg ?? batch.targetOg;
		if (allowTelemetry && nextGravity && og && og > nextGravity) {
			nextAbv = Number(calculateActualAbv(og, nextGravity, abvFormula).toFixed(2));
		}

		const nextTemp = allowTelemetry
			? (reading.temperatureC ?? batch.vesselTempC)
			: batch.vesselTempC;
		if (
			nextTemp === batch.vesselTempC &&
			nextGravity === batch.currentGravity &&
			nextAbv === batch.alcoholByVolume
		) {
			return batch;
		}

		updatedAny = true;
		return {
			...batch,
			vesselTempC: nextTemp,
			currentGravity: nextGravity,
			alcoholByVolume: nextAbv
		};
	});

	return updatedAny ? nextBatches : batches;
}

export function applyBrewLinkSyncTempToBatches(
	batches: BatchSummaryDto[],
	equipmentId: string,
	temperatureC: number
): BatchSummaryDto[] {
	let updatedAny = false;
	const nextBatches = batches.map((batch) => {
		if (!isActiveStageVessel(batch, equipmentId) || batch.vesselTempC === temperatureC) {
			return batch;
		}

		updatedAny = true;
		return {
			...batch,
			vesselTempC: temperatureC
		};
	});

	return updatedAny ? nextBatches : batches;
}
