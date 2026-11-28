import type { BatchDetailDto, BatchSummaryDto, BrewStage, EquipmentDto } from '$lib/types/api';
import { isBatchInProgress } from './batchTelemetry';

export interface ResolvedStageEquipment {
	id: string;
	name: string;
	equipment: EquipmentDto | null;
}

export type BatchOccupancySummary = Pick<
	BatchSummaryDto,
	| 'id'
	| 'status'
	| 'currentStage'
	| 'hltId'
	| 'mashTunId'
	| 'boilerId'
	| 'fermenterId'
	| 'fermentationSensorId'
	| 'isHistorical'
> & {
	packagingVesselId?: string | null;
	sensorAssignments?: Array<{
		equipmentId: string;
		stage?: BrewStage | null;
	}>;
};

/**
 * Resolves the primary equipment (sensor or vessel) for a given stage on a batch detail record.
 * Mirrors backend precedence:
 * 1. Mash step-scoped sensor assignment (when stage === 'Mash' and mashStepId is set)
 * 2. Exact stage-scoped sensor assignment
 * 3. Global (stage === null) sensor assignment during cellar stages (Ferment / Condition)
 * 4. Stage vessel (MashTun/Boiler, Boiler, Fermenter, PackagingVessel)
 * 5. Global (stage === null) sensor assignment for non-cellar stages
 */
export function resolveStageEquipment(
	batch: BatchDetailDto | null | undefined,
	equipmentList: EquipmentDto[],
	stage: BrewStage,
	mashStepId?: string | null
): ResolvedStageEquipment | null {
	if (!batch) return null;

	const findEq = (id: string) => equipmentList.find((e) => e.id === id) ?? null;

	// 1. Step-level sensor assignment (Mash step)
	if (stage === 'Mash' && mashStepId && batch.sensorAssignments?.length) {
		const stepAssignment = batch.sensorAssignments.find((sa) => sa.batchMashStepId === mashStepId);
		if (stepAssignment) {
			const eq = findEq(stepAssignment.equipmentId);
			return {
				id: stepAssignment.equipmentId,
				name: eq?.name ?? stepAssignment.equipmentName ?? '',
				equipment: eq
			};
		}
	}

	// 2. Exact stage-level sensor assignment
	if (batch.sensorAssignments?.length) {
		const stageAssignment = batch.sensorAssignments.find((sa) => sa.stage === stage);
		if (stageAssignment) {
			const eq = findEq(stageAssignment.equipmentId);
			return {
				id: stageAssignment.equipmentId,
				name: eq?.name ?? stageAssignment.equipmentName ?? '',
				equipment: eq
			};
		}
	}

	// 3. Global sensor assignment beats fermenter during Ferment and Condition
	if ((stage === 'Ferment' || stage === 'Condition') && batch.sensorAssignments?.length) {
		const globalAssignment = batch.sensorAssignments.find((sa) => sa.stage === null);
		if (globalAssignment) {
			const eq = findEq(globalAssignment.equipmentId);
			return {
				id: globalAssignment.equipmentId,
				name: eq?.name ?? globalAssignment.equipmentName ?? '',
				equipment: eq
			};
		}
	}

	// 4. Stage vessel from batch profile
	if (stage === 'Mash') {
		const mashVesselId = batch.mashTunId ?? batch.boilerId;
		if (mashVesselId) {
			const eq = findEq(mashVesselId);
			return {
				id: mashVesselId,
				name: eq?.name ?? batch.mashTunName ?? batch.boilerName ?? '',
				equipment: eq
			};
		}
	} else if (stage === 'Boil') {
		if (batch.boilerId) {
			const eq = findEq(batch.boilerId);
			return {
				id: batch.boilerId,
				name: eq?.name ?? batch.boilerName ?? '',
				equipment: eq
			};
		}
	} else if (stage === 'Ferment' || stage === 'Condition') {
		if (batch.fermenterId) {
			const eq = findEq(batch.fermenterId);
			return {
				id: batch.fermenterId,
				name: eq?.name ?? batch.fermenterName ?? '',
				equipment: eq
			};
		}
	} else if (stage === 'Package') {
		if (batch.packagingVesselId) {
			const eq = findEq(batch.packagingVesselId);
			return {
				id: batch.packagingVesselId,
				name: eq?.name ?? batch.packagingVesselName ?? '',
				equipment: eq
			};
		}
	}

	// 5. Global sensor assignment fallback for non-cellar stages
	if (batch.sensorAssignments?.length) {
		const globalAssignment = batch.sensorAssignments.find((sa) => sa.stage === null);
		if (globalAssignment) {
			const eq = findEq(globalAssignment.equipmentId);
			return {
				id: globalAssignment.equipmentId,
				name: eq?.name ?? globalAssignment.equipmentName ?? '',
				equipment: eq
			};
		}
	}

	return null;
}

/**
 * Resolves the BrewLink-connected vessel controlling the given stage on a batch, if any.
 */
export function resolveStageBrewLinkEquipment(
	batch: BatchDetailDto | null | undefined,
	equipmentList: EquipmentDto[],
	stage: BrewStage
): EquipmentDto | null {
	if (!batch || equipmentList.length === 0) return null;

	let targetId: string | null = null;
	if (stage === 'Mash') {
		targetId = batch.mashTunId ?? batch.boilerId ?? null;
	} else if (stage === 'Boil') {
		targetId = batch.boilerId ?? null;
	} else if (stage === 'Ferment' || stage === 'Condition') {
		targetId = batch.fermenterId ?? null;
	} else if (stage === 'Package') {
		targetId = batch.packagingVesselId ?? null;
	}

	if (!targetId) return null;
	const eq = equipmentList.find((e) => e.id === targetId);
	return eq?.connectionType === 'BrewLink' ? eq : null;
}

/**
 * Returns all equipment IDs actively occupied by a batch in its current stage,
 * mirroring backend BatchLifecycle.ActiveStageVesselIds.
 * Planned, Completed, Archived, and historical batches occupy no live stage vessels.
 */
export function getBatchActiveStageEquipmentIds(batch: BatchOccupancySummary): string[] {
	if (!isBatchInProgress(batch) || batch.isHistorical) {
		return [];
	}

	const ids: string[] = [];
	const addUnique = (id?: string | null) => {
		if (id && !ids.includes(id)) {
			ids.push(id);
		}
	};

	switch (batch.currentStage) {
		case 'Mash':
			addUnique(batch.hltId);
			addUnique(batch.mashTunId);
			addUnique(batch.boilerId);
			break;
		case 'Boil':
			addUnique(batch.boilerId);
			break;
		case 'Ferment':
		case 'Condition':
			addUnique(batch.fermenterId);
			addUnique(batch.fermentationSensorId);
			break;
		case 'Package':
			addUnique(batch.packagingVesselId);
			break;
	}

	if (batch.sensorAssignments?.length) {
		for (const sa of batch.sensorAssignments) {
			if (sa.stage == null || sa.stage === batch.currentStage) {
				addUnique(sa.equipmentId);
			}
		}
	}

	return ids;
}

/**
 * Determines whether an equipment item is currently occupied by a live batch (stage-aware)
 * or assigned as a keg/tap via assignedBatchId.
 */
export function isEquipmentOccupied(
	equipId: string,
	batches: BatchOccupancySummary[],
	equipment?: Pick<EquipmentDto, 'id' | 'assignedBatchId'> | null,
	excludeBatchId?: string | null
): boolean {
	if (equipment?.assignedBatchId && equipment.assignedBatchId !== excludeBatchId) {
		return true;
	}

	return batches.some((b) => {
		if (excludeBatchId && b.id === excludeBatchId) return false;
		return getBatchActiveStageEquipmentIds(b).includes(equipId);
	});
}

export function getOccupiedEquipmentCount(
	equipment: EquipmentDto[],
	batches: BatchOccupancySummary[],
	excludeBatchId?: string | null
): number {
	return equipment.filter((eq) => isEquipmentOccupied(eq.id, batches, eq, excludeBatchId)).length;
}

/**
 * Resolves whether LogStepTemperatureModal should show read-only equipment text or a <select>.
 * Only locks to read-only text when defaultEquipmentId actually exists among eligible equipment.
 */
export function resolveLogTempEquipmentMode(
	defaultEquipmentId: string | null | undefined,
	equipmentList: EquipmentDto[]
): { locked: boolean; equipmentId: string; equipmentName: string | null } {
	if (defaultEquipmentId) {
		const found = equipmentList.find(
			(e) => e.id === defaultEquipmentId && e.subtype !== 'BrewLink' && e.type !== 'Tap'
		);
		if (found) {
			return {
				locked: true,
				equipmentId: found.id,
				equipmentName: found.name
			};
		}
	}

	return {
		locked: false,
		equipmentId: '',
		equipmentName: null
	};
}
