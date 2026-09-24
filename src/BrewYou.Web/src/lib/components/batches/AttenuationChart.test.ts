import { describe, it, expect } from 'vitest';
import { i18n, t } from '$lib/i18n/index.svelte';
import type { BatchReadingDto, BatchEquipmentReadingDto } from '$lib/types/api';

interface GravityPoint {
	id: string;
	time: number;
	sg: number;
	tempC?: number | null;
	source: string;
	isSensor: boolean;
}

interface TemperaturePoint {
	id: string;
	time: number;
	tempC: number;
	sg?: number | null;
	source: string;
	isSensor: boolean;
}

export function extractSgPoints(
	readings: BatchReadingDto[],
	equipmentReadings: BatchEquipmentReadingDto[]
): GravityPoint[] {
	const points: GravityPoint[] = [];

	for (const r of readings) {
		const time = new Date(r.timestamp).getTime();
		if (!isNaN(time) && r.specificGravity) {
			points.push({
				id: r.id,
				time,
				sg: r.specificGravity,
				tempC: r.temperatureC,
				source: r.notes || 'Manual',
				isSensor: false
			});
		}
	}

	for (const er of equipmentReadings) {
		if (er.specificGravity !== null && er.specificGravity !== undefined) {
			const time = new Date(er.timestamp).getTime();
			if (!isNaN(time)) {
				if (!points.some((p) => p.id === er.id || Math.abs(p.time - time) < 2000)) {
					points.push({
						id: er.id,
						time,
						sg: er.specificGravity,
						tempC: er.temperatureC,
						source: er.equipmentName || er.source || 'Sensor',
						isSensor: true
					});
				}
			}
		}
	}

	return points.sort((a, b) => a.time - b.time);
}

export function extractTempPoints(
	readings: BatchReadingDto[],
	equipmentReadings: BatchEquipmentReadingDto[]
): TemperaturePoint[] {
	const points: TemperaturePoint[] = [];

	for (const er of equipmentReadings) {
		const time = new Date(er.timestamp).getTime();
		if (!isNaN(time) && er.temperatureC !== null && er.temperatureC !== undefined) {
			points.push({
				id: er.id,
				time,
				tempC: er.temperatureC,
				sg: er.specificGravity,
				source: er.equipmentName || er.source || 'Sensor',
				isSensor: true
			});
		}
	}

	for (const r of readings) {
		if (r.temperatureC !== null && r.temperatureC !== undefined) {
			const time = new Date(r.timestamp).getTime();
			if (!isNaN(time)) {
				if (!points.some((p) => p.id === r.id || Math.abs(p.time - time) < 2000)) {
					points.push({
						id: r.id,
						time,
						tempC: r.temperatureC,
						sg: r.specificGravity,
						source: r.notes || 'Manual',
						isSensor: false
					});
				}
			}
		}
	}

	return points.sort((a, b) => a.time - b.time);
}

export function computeAttenuationMetrics(
	currentSg: number | null,
	targetOg: number,
	targetFg: number
) {
	let apparentAttenuation: number | null = null;
	if (currentSg && targetOg > 1.0) {
		const totalDrop = targetOg - 1.0;
		const currentDrop = targetOg - currentSg;
		apparentAttenuation = Math.max(0, Math.min(100, (currentDrop / totalDrop) * 100));
	}

	let progressToFg: number | null = null;
	if (currentSg && targetOg > targetFg) {
		const targetDrop = targetOg - targetFg;
		const currentDrop = targetOg - currentSg;
		progressToFg = Math.max(0, Math.min(100, Math.round((currentDrop / targetDrop) * 100)));
	}

	let currentAbv: number | null = null;
	if (currentSg && targetOg > currentSg) {
		currentAbv = (targetOg - currentSg) * 131.25;
	}

	let pointsToFg: number | null = null;
	if (currentSg && targetFg) {
		pointsToFg = Math.max(0, Math.round((currentSg - targetFg) * 1000));
	}

	let totalDropPoints: number | null = null;
	if (currentSg && targetOg) {
		totalDropPoints = Math.max(0, Math.round((targetOg - currentSg) * 1000));
	}

	return { apparentAttenuation, progressToFg, currentAbv, pointsToFg, totalDropPoints };
}

export function calculateSgY(
	val: number,
	minSg: number,
	maxSg: number,
	innerHeight: number,
	paddingTop: number
): number {
	const range = maxSg - minSg || 0.01;
	const norm = (val - minSg) / range;
	return paddingTop + innerHeight - norm * innerHeight;
}

describe('AttenuationChart Unified Fermentation Data & Math', () => {
	const manualReadings: BatchReadingDto[] = [
		{
			id: 'mr-1',
			batchId: 'b-1',
			timestamp: '2026-09-12T10:00:00Z',
			createdAt: '2026-09-12T10:00:00Z',
			specificGravity: 1.052,
			temperatureC: 20.1,
			notes: 'Manual hydrometer sample'
		}
	];

	const sensorReadings: BatchEquipmentReadingDto[] = [
		{
			id: 'er-1',
			equipmentId: 'eq-tilt',
			equipmentName: 'Tilt Red',
			batchId: 'b-1',
			stage: 'Ferment',
			timestamp: '2026-09-12T12:00:00Z',
			specificGravity: 1.048,
			temperatureC: 20.4
		},
		{
			id: 'er-2',
			equipmentId: 'eq-tilt',
			equipmentName: 'Tilt Red',
			batchId: 'b-1',
			stage: 'Ferment',
			timestamp: '2026-09-12T18:00:00Z',
			specificGravity: 1.036,
			temperatureC: 21.0
		},
		{
			id: 'er-3',
			equipmentId: 'eq-chamber',
			equipmentName: 'Fermentation Chamber',
			batchId: 'b-1',
			stage: 'Ferment',
			timestamp: '2026-09-12T19:00:00Z',
			temperatureC: 19.8
		}
	];

	it('merges manual and sensor SG points in strict chronological sequence', () => {
		const sgPoints = extractSgPoints(manualReadings, sensorReadings);
		expect(sgPoints).toHaveLength(3);
		expect(sgPoints[0].sg).toBe(1.052);
		expect(sgPoints[0].isSensor).toBe(false);
		expect(sgPoints[1].sg).toBe(1.048);
		expect(sgPoints[1].isSensor).toBe(true);
		expect(sgPoints[2].sg).toBe(1.036);
		expect(sgPoints[2].isSensor).toBe(true);
	});

	it('merges sensor and manual temperature points accurately', () => {
		const tempPoints = extractTempPoints(manualReadings, sensorReadings);
		expect(tempPoints).toHaveLength(4);
		expect(tempPoints[0].tempC).toBe(20.1);
		expect(tempPoints[1].tempC).toBe(20.4);
		expect(tempPoints[2].tempC).toBe(21.0);
		expect(tempPoints[3].tempC).toBe(19.8);
	});

	it('filters out duplicated readings within 2-second collision window', () => {
		const duplicateReading: BatchEquipmentReadingDto = {
			id: 'er-dup',
			equipmentId: 'eq-tilt',
			equipmentName: 'Tilt Red',
			batchId: 'b-1',
			stage: 'Ferment',
			timestamp: '2026-09-12T10:00:01Z',
			specificGravity: 1.052,
			temperatureC: 20.1
		};
		const sgPoints = extractSgPoints(manualReadings, [duplicateReading]);
		expect(sgPoints).toHaveLength(1);
	});

	it('computes correct apparent attenuation, progress to FG, estimated ABV, points to FG, and drop points', () => {
		const targetOg = 1.055;
		const targetFg = 1.011;
		const currentSg = 1.022;

		const metrics = computeAttenuationMetrics(currentSg, targetOg, targetFg);

		expect(metrics.apparentAttenuation).toBeCloseTo(60.0, 1);
		expect(metrics.progressToFg).toBe(75);
		expect(metrics.currentAbv).toBeCloseTo(4.33, 2);
		expect(metrics.pointsToFg).toBe(11);
		expect(metrics.totalDropPoints).toBe(33);
	});

	it('handles empty readings and non-fermenting values gracefully', () => {
		const emptySg = extractSgPoints([], []);
		expect(emptySg).toHaveLength(0);

		const metrics = computeAttenuationMetrics(null, 1.05, 1.01);
		expect(metrics.apparentAttenuation).toBeNull();
		expect(metrics.progressToFg).toBeNull();
		expect(metrics.currentAbv).toBeNull();
		expect(metrics.pointsToFg).toBeNull();
		expect(metrics.totalDropPoints).toBeNull();
	});

	it('calculates valid SVG Y coordinates within bounded range', () => {
		const innerHeight = 195;
		const paddingTop = 25;
		const minSg = 1.005;
		const maxSg = 1.055;

		const yMax = calculateSgY(maxSg, minSg, maxSg, innerHeight, paddingTop);
		expect(yMax).toBeCloseTo(paddingTop, 3);

		const yMin = calculateSgY(minSg, minSg, maxSg, innerHeight, paddingTop);
		expect(yMin).toBeCloseTo(paddingTop + innerHeight, 3);

		const yMid = calculateSgY(1.03, minSg, maxSg, innerHeight, paddingTop);
		expect(yMid).toBeCloseTo(paddingTop + innerHeight / 2, 3);
	});

	it('translates fermentation telemetry and monitor KPI labels in English and Swedish', () => {
		i18n.setLocale('en');
		expect(t('batches.telemetry.current_temp')).toBe('Current Temp');
		expect(t('batches.telemetry.current_sg')).toBe('Current Gravity');
		expect(t('batches.telemetry.attenuation_label')).toBe('Apparent Attenuation');
		expect(t('batches.telemetry.progress_to_fg_title')).toBe('Progress to FG');
		expect(t('batches.telemetry.points_remaining', { points: 11 })).toBe('11 pts to FG');
		expect(t('batches.telemetry.points_dropped', { points: 33 })).toBe('-33 pts drop');
		expect(t('batches.telemetry.fermenter_time_label')).toBe('Time in Fermenter');
		expect(t('batches.telemetry.fermentation_chart_title')).toBe(
			'Fermentation & Attenuation Monitor'
		);
		expect(t('batches.telemetry.waiting_for_telemetry')).toBe(
			'Waiting for hydrometer or temperature telemetry readings.'
		);

		i18n.setLocale('sv');
		expect(t('batches.telemetry.current_temp')).toBe('Nuvarande temp');
		expect(t('batches.telemetry.current_sg')).toBe('Nuvarande densitet');
		expect(t('batches.telemetry.attenuation_label')).toBe('Skenbar utjäsning');
		expect(t('batches.telemetry.progress_to_fg_title')).toBe('Förlopp mot FG');
		expect(t('batches.telemetry.points_remaining', { points: 11 })).toBe('11 punkter till FG');
		expect(t('batches.telemetry.points_dropped', { points: 33 })).toBe('-33 punkter minskat');
		expect(t('batches.telemetry.fermenter_time_label')).toBe('Tid i jäskärl');
		expect(t('batches.telemetry.fermentation_chart_title')).toBe('Jäsnings- & utjäsningsmonitor');
		expect(t('batches.telemetry.waiting_for_telemetry')).toBe(
			'Väntar på hydrometer- eller temperaturmätningar.'
		);
	});
});
