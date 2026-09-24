import { describe, it, expect } from 'vitest';
import type { BatchEquipmentReadingDto, BatchMashStepDto } from '$lib/types/api';

export function calculateReadingStats(readings: BatchEquipmentReadingDto[]) {
	if (readings.length === 0) {
		return { min: null, max: null, avg: null, latest: null };
	}
	const temps = readings.map((r) => r.temperatureC);
	const min = Math.min(...temps);
	const max = Math.max(...temps);
	const sum = temps.reduce((acc, v) => acc + v, 0);
	const avg = Number((sum / temps.length).toFixed(1));
	const latest = readings[readings.length - 1].temperatureC;
	return { min, max, avg, latest };
}

export function filterReadingsByStep(
	readings: BatchEquipmentReadingDto[],
	selectedStepId: string | null,
	stage?: string
): BatchEquipmentReadingDto[] {
	const stageFiltered = stage ? readings.filter((r) => !r.stage || r.stage === stage) : readings;
	if (!selectedStepId) return stageFiltered;
	return stageFiltered.filter((r) => r.batchMashStepId === selectedStepId);
}

export function resolveStepTargetTemp(
	stage: string,
	selectedStepId: string | null,
	mashSteps: BatchMashStepDto[] = [],
	targetTemperatureC?: number | null
): number | null {
	if (selectedStepId && mashSteps.length > 0) {
		const step = mashSteps.find((s) => s.id === selectedStepId);
		if (step) return step.targetTemperatureC;
	}
	if (targetTemperatureC !== null && targetTemperatureC !== undefined) return targetTemperatureC;
	if (stage === 'Mash' && mashSteps.length > 0) {
		const active = mashSteps.find((s) => !s.isCompleted) ?? mashSteps[0];
		return active.targetTemperatureC;
	}
	if (stage === 'Mash') return 65.0;
	if (stage === 'Boil') return 100.0;
	if (stage === 'Ferment') return 20.0;
	if (stage === 'Condition') return 2.0;
	return null;
}

export function calculateTempY(
	temp: number,
	minTemp: number,
	maxTemp: number,
	height: number,
	paddingTop: number
): number {
	const range = maxTemp - minTemp || 1;
	const norm = (temp - minTemp) / range;
	return paddingTop + height - norm * height;
}

describe('StepTemperatureChart Telemetry & Geometry Logic', () => {
	const mockReadings: BatchEquipmentReadingDto[] = [
		{
			id: 'r-1',
			equipmentId: 'eq-1',
			equipmentName: 'Grainfather G40',
			batchId: 'b-1',
			stage: 'Mash',
			batchMashStepId: 'step-1',
			stepName: 'Dough-In',
			temperatureC: 44.8,
			timestamp: '2026-09-12T10:00:00Z'
		},
		{
			id: 'r-2',
			equipmentId: 'eq-1',
			equipmentName: 'Grainfather G40',
			batchId: 'b-1',
			stage: 'Mash',
			batchMashStepId: 'step-1',
			stepName: 'Dough-In',
			temperatureC: 45.2,
			timestamp: '2026-09-12T10:05:00Z'
		},
		{
			id: 'r-3',
			equipmentId: 'eq-1',
			equipmentName: 'Grainfather G40',
			batchId: 'b-1',
			stage: 'Mash',
			batchMashStepId: 'step-2',
			stepName: 'Saccharification',
			temperatureC: 65.1,
			timestamp: '2026-09-12T10:20:00Z'
		}
	];

	const mockMashSteps: BatchMashStepDto[] = [
		{
			id: 'step-1',
			batchId: 'b-1',
			stepOrder: 1,
			name: 'Dough-In',
			type: 'Infusion',
			targetTemperatureC: 45.0,
			durationMinutes: 15,
			isCompleted: true
		},
		{
			id: 'step-2',
			batchId: 'b-1',
			stepOrder: 2,
			name: 'Saccharification',
			type: 'Temperature',
			targetTemperatureC: 65.0,
			durationMinutes: 60,
			isCompleted: false
		}
	];

	it('computes accurate min, max, avg, and latest summary metrics', () => {
		const stats = calculateReadingStats(mockReadings);
		expect(stats.min).toBe(44.8);
		expect(stats.max).toBe(65.1);
		expect(stats.avg).toBe(51.7);
		expect(stats.latest).toBe(65.1);
	});

	it('handles empty readings gracefully without throwing', () => {
		const stats = calculateReadingStats([]);
		expect(stats.min).toBeNull();
		expect(stats.max).toBeNull();
		expect(stats.avg).toBeNull();
		expect(stats.latest).toBeNull();
	});

	it('filters readings accurately by active mash step', () => {
		const step1Readings = filterReadingsByStep(mockReadings, 'step-1');
		expect(step1Readings).toHaveLength(2);
		expect(step1Readings.every((r) => r.batchMashStepId === 'step-1')).toBe(true);

		const step2Readings = filterReadingsByStep(mockReadings, 'step-2');
		expect(step2Readings).toHaveLength(1);
		expect(step2Readings[0].stepName).toBe('Saccharification');

		const allReadings = filterReadingsByStep(mockReadings, null);
		expect(allReadings).toHaveLength(3);
	});

	it('resolves correct target temperature for each brewing stage and step', () => {
		expect(resolveStepTargetTemp('Mash', 'step-1', mockMashSteps)).toBe(45.0);
		expect(resolveStepTargetTemp('Mash', 'step-2', mockMashSteps)).toBe(65.0);
		expect(resolveStepTargetTemp('Boil', null, mockMashSteps)).toBe(100.0);
		expect(resolveStepTargetTemp('Ferment', null, mockMashSteps)).toBe(20.0);
		expect(resolveStepTargetTemp('Condition', null, mockMashSteps)).toBe(2.0);

		// Mash fallback when no step is selected: resolves first incomplete step (step-2: 65°C)
		expect(resolveStepTargetTemp('Mash', null, mockMashSteps)).toBe(65.0);

		// Mash fallback when all steps are completed: falls back to first step (step-1: 45°C)
		const completedSteps = mockMashSteps.map((s) => ({ ...s, isCompleted: true }));
		expect(resolveStepTargetTemp('Mash', null, completedSteps)).toBe(45.0);

		// Mash fallback when no mash steps exist: defaults to 65°C
		expect(resolveStepTargetTemp('Mash', null, [])).toBe(65.0);

		// Explicit targetTemperatureC takes precedence when no specific step ID is selected
		expect(resolveStepTargetTemp('Mash', null, mockMashSteps, 67.5)).toBe(67.5);
	});

	it('filters readings accurately by brewing stage when viewing all steps', () => {
		const mixedReadings: BatchEquipmentReadingDto[] = [
			...mockReadings,
			{
				id: 'r-boil-1',
				equipmentId: 'eq-1',
				equipmentName: 'Grainfather G40',
				batchId: 'b-1',
				stage: 'Boil',
				temperatureC: 100.0,
				timestamp: '2026-09-12T11:00:00Z'
			}
		];

		const mashAll = filterReadingsByStep(mixedReadings, null, 'Mash');
		expect(mashAll).toHaveLength(3);
		expect(mashAll.every((r) => r.stage === 'Mash')).toBe(true);

		const boilAll = filterReadingsByStep(mixedReadings, null, 'Boil');
		expect(boilAll).toHaveLength(1);
		expect(boilAll[0].temperatureC).toBe(100.0);
	});

	it('calculates valid SVG Y coordinates within bounds', () => {
		const height = 170;
		const paddingTop = 30;
		const minTemp = 40;
		const maxTemp = 70;

		// 40°C should be at bottom of plot (paddingTop + height)
		const bottomY = calculateTempY(40, minTemp, maxTemp, height, paddingTop);
		expect(bottomY).toBe(200);

		// 70°C should be at top of plot (paddingTop)
		const topY = calculateTempY(70, minTemp, maxTemp, height, paddingTop);
		expect(topY).toBe(30);

		// 55°C (midpoint) should be in the middle (115)
		const midY = calculateTempY(55, minTemp, maxTemp, height, paddingTop);
		expect(midY).toBe(115);
	});

	it('updates stats dynamically when live readings are appended', () => {
		const liveList = [...mockReadings];
		const initialStats = calculateReadingStats(liveList);
		expect(initialStats.latest).toBe(65.1);

		// Simulate live reading arriving over SSE
		const newLiveReading: BatchEquipmentReadingDto = {
			id: 'r-live-1',
			equipmentId: 'eq-1',
			equipmentName: 'Grainfather G40',
			batchId: 'b-1',
			stage: 'Mash',
			batchMashStepId: 'step-2',
			stepName: 'Saccharification',
			temperatureC: 66.5,
			timestamp: '2026-09-12T10:25:00Z'
		};
		liveList.push(newLiveReading);

		const updatedStats = calculateReadingStats(liveList);
		expect(updatedStats.latest).toBe(66.5);
		expect(updatedStats.max).toBe(66.5);
		expect(liveList).toHaveLength(4);
	});
});
