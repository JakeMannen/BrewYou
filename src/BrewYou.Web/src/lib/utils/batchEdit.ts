import { isVesselFillable } from '$lib/components/inventory/vessel-geometry';
import type {
	BatchDetailDto,
	BatchSensorAssignmentDto,
	BatchSensorAssignmentInput,
	BrewStage,
	EquipmentDto
} from '$lib/types/api';

export const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

export function isPackagingVesselEquipment(
	equipment: Pick<EquipmentDto, 'type' | 'subtype'>
): boolean {
	return (
		equipment.type === 'Keg' || (equipment.type === 'Other' && isVesselFillable(equipment.subtype))
	);
}

export function getPrimaryFermentationSensorAssignment(
	batch: Pick<BatchDetailDto, 'sensorAssignments'>
): BatchSensorAssignmentDto | null {
	const assignments = batch.sensorAssignments ?? [];
	return (
		assignments.find((sa) => sa.stage === 'Ferment') ??
		assignments.find((sa) => sa.stage === null) ??
		null
	);
}

export function resolveActiveStageSensorAssignment(
	batch: Pick<BatchDetailDto, 'sensorAssignments'> | null | undefined,
	stage: BrewStage
): BatchSensorAssignmentDto | null {
	const assignments = batch?.sensorAssignments ?? [];
	if (assignments.length === 0) return null;

	return (
		assignments.find((sa) => sa.stage === stage) ??
		(stage === 'Condition' ? assignments.find((sa) => sa.stage === 'Ferment') : undefined) ??
		assignments.find((sa) => sa.stage === null) ??
		null
	);
}

export function buildEquipmentIdUpdate(
	draftEquipmentId: string,
	currentEquipmentId: string | null | undefined
): string | undefined {
	const current = currentEquipmentId ?? '';
	if (draftEquipmentId === current) {
		return undefined;
	}
	return draftEquipmentId ? draftEquipmentId : EMPTY_GUID;
}

export function buildFermentationSensorAssignmentsUpdate(
	batch: Pick<BatchDetailDto, 'sensorAssignments'>,
	draftFermentationSensorId: string
): BatchSensorAssignmentInput[] | undefined {
	const initialAssignment = getPrimaryFermentationSensorAssignment(batch);
	const initialId = initialAssignment?.equipmentId ?? '';
	if (draftFermentationSensorId === initialId) {
		return undefined;
	}

	const preservedAssignments: BatchSensorAssignmentInput[] = (batch.sensorAssignments ?? [])
		.filter((sa) => {
			if (!initialAssignment) return true;
			if (initialAssignment.id && sa.id) {
				return sa.id !== initialAssignment.id;
			}
			return !(
				sa.equipmentId === initialAssignment.equipmentId &&
				sa.stage === initialAssignment.stage &&
				(sa.batchMashStepId ?? null) === (initialAssignment.batchMashStepId ?? null)
			);
		})
		.map((sa) => ({
			equipmentId: sa.equipmentId,
			stage: sa.stage,
			batchMashStepId: sa.batchMashStepId ?? null
		}));

	if (draftFermentationSensorId) {
		preservedAssignments.push({
			equipmentId: draftFermentationSensorId,
			stage: initialAssignment ? initialAssignment.stage : 'Ferment',
			batchMashStepId: null
		});
	}

	return preservedAssignments;
}
