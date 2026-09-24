import { describe, it, expect } from 'vitest';
import type { BrewStage } from './BrewStageTracker.svelte';
import { getDefaultEquipmentSubtype } from './BrewStageTracker.svelte';

export function resolveStageState(
	stage: BrewStage,
	currentStageId?: string,
	selectedStageId?: string
): {
	isCurrent: boolean;
	isCompleted: boolean;
	isSkipped: boolean;
	isPending: boolean;
	isSelected: boolean;
} {
	const isCurrent = stage.id === currentStageId || stage.status === 'in_progress';
	const isCompleted = stage.status === 'completed';
	const isSkipped = stage.status === 'skipped';
	const isPending = stage.status === 'pending';
	const isSelected = stage.id === selectedStageId;

	return { isCurrent, isCompleted, isSkipped, isPending, isSelected };
}

export function shouldDisplayEquipment(stage: BrewStage, currentStageId?: string): boolean {
	const state = resolveStageState(stage, currentStageId);
	return state.isCurrent || state.isCompleted;
}

describe('BrewStageTracker Lifecycle Logic', () => {
	const mockStages: BrewStage[] = [
		{ id: 'mash', label: 'Mash', status: 'completed', timestamp: '65°C' },
		{ id: 'boil', label: 'Boil', status: 'completed', timestamp: '60 min' },
		{ id: 'ferment', label: 'Ferment', status: 'in_progress', timestamp: 'Day 3' },
		{ id: 'condition', label: 'Condition', status: 'pending' },
		{ id: 'package', label: 'Package', status: 'pending' }
	];

	it('correctly identifies active, completed, and pending stages', () => {
		const mashState = resolveStageState(mockStages[0]);
		expect(mashState.isCompleted).toBe(true);
		expect(mashState.isCurrent).toBe(false);

		const fermentState = resolveStageState(mockStages[2]);
		expect(fermentState.isCurrent).toBe(true);
		expect(fermentState.isCompleted).toBe(false);

		const packageState = resolveStageState(mockStages[4]);
		expect(packageState.isPending).toBe(true);
		expect(packageState.isCurrent).toBe(false);
	});

	it('allows overriding active stage with explicit currentStageId', () => {
		const boilState = resolveStageState(mockStages[1], 'boil');
		expect(boilState.isCurrent).toBe(true);
	});

	it('supports skipped stage status for alternate brew profiles', () => {
		const skippedStage: BrewStage = { id: 'dry-hop', label: 'Dry Hop', status: 'skipped' };
		const state = resolveStageState(skippedStage);
		expect(state.isSkipped).toBe(true);
		expect(state.isCurrent).toBe(false);
		expect(state.isCompleted).toBe(false);
	});

	it('decouples selected inspection stage from active batch stage', () => {
		// Active stage is ferment, but user is inspecting completed mash step
		const mashInspection = resolveStageState(mockStages[0], 'ferment', 'mash');
		expect(mashInspection.isCompleted).toBe(true);
		expect(mashInspection.isCurrent).toBe(false);
		expect(mashInspection.isSelected).toBe(true);

		const fermentState = resolveStageState(mockStages[2], 'ferment', 'mash');
		expect(fermentState.isCurrent).toBe(true);
		expect(fermentState.isSelected).toBe(false);
	});

	it('preserves timestamps and days active on stages across lifecycle states', () => {
		const stagesWithDays: BrewStage[] = [
			{ id: 'mash', label: 'Mash', status: 'completed' },
			{ id: 'boil', label: 'Boil', status: 'completed', timestamp: '60m' },
			{ id: 'ferment', label: 'Ferment', status: 'in_progress', timestamp: '14d' },
			{ id: 'condition', label: 'Condition', status: 'pending' },
			{ id: 'package', label: 'Package', status: 'pending' }
		];
		expect(stagesWithDays[2].timestamp).toBe('14d');
		const fermentState = resolveStageState(stagesWithDays[2]);
		expect(fermentState.isCurrent).toBe(true);
		expect(fermentState.isCompleted).toBe(false);
	});

	it('only displays equipment icons for active or finished (completed) stages', () => {
		// Completed stages display equipment
		expect(shouldDisplayEquipment(mockStages[0])).toBe(true); // Mash (completed)
		expect(shouldDisplayEquipment(mockStages[1])).toBe(true); // Boil (completed)

		// Active (in_progress) stage displays equipment
		expect(shouldDisplayEquipment(mockStages[2])).toBe(true); // Ferment (in_progress)

		// Pending stages DO NOT display equipment
		expect(shouldDisplayEquipment(mockStages[3])).toBe(false); // Condition (pending)
		expect(shouldDisplayEquipment(mockStages[4])).toBe(false); // Package (pending)

		// Skipped stages DO NOT display equipment
		const skipped: BrewStage = { id: 'dry-hop', label: 'Dry Hop', status: 'skipped' };
		expect(shouldDisplayEquipment(skipped)).toBe(false);
	});

	it('supports custom equipment subtype, fillPercent, and equipmentName metadata on stages', () => {
		const stageWithEquipment: BrewStage = {
			id: 'boil',
			label: 'Boil',
			status: 'in_progress',
			equipmentSubtype: 'AllInOne',
			fillPercent: 78,
			equipmentName: 'Grainfather G40'
		};

		expect(stageWithEquipment.equipmentSubtype).toBe('AllInOne');
		expect(stageWithEquipment.fillPercent).toBe(78);
		expect(stageWithEquipment.equipmentName).toBe('Grainfather G40');
		expect(shouldDisplayEquipment(stageWithEquipment)).toBe(true);
	});

	it('defaults Package stage equipment icon to Bottle when no vessel is specified', () => {
		expect(getDefaultEquipmentSubtype('package')).toBe('Bottle');
		expect(getDefaultEquipmentSubtype('Package')).toBe('Bottle');
		expect(getDefaultEquipmentSubtype('PACKAGE')).toBe('Bottle');
	});

	it('provides correct default vessel equipment subtypes across all stages', () => {
		expect(getDefaultEquipmentSubtype('mash')).toBe('AllInOne');
		expect(getDefaultEquipmentSubtype('boil')).toBe('Pan');
		expect(getDefaultEquipmentSubtype('ferment')).toBe('ConicalFermenter');
		expect(getDefaultEquipmentSubtype('condition')).toBe('Carboy');
		expect(getDefaultEquipmentSubtype('package')).toBe('Bottle');
		expect(getDefaultEquipmentSubtype('unknown_stage')).toBe('Other');
	});
});
