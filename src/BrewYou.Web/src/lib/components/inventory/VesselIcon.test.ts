import { describe, it, expect } from 'vitest';
import type { EquipmentSubtype } from '$lib/types/api';

import {
	CHAMBER_BOUNDS,
	calculateLiquidGeometry,
	resolveVesselAnimationType,
	isVesselFillable,
	SENSOR_SUBTYPES
} from './vessel-geometry';

describe('VesselIcon Liquid Fill Geometry & Boundary Calculations', () => {
	const allSubtypes: EquipmentSubtype[] = [
		'AllInOne',
		'Pan',
		'Hlt',
		'HermsRims',
		'Bucket',
		'ConicalFermenter',
		'Carboy',
		'PressureFermenter',
		'StainlessBucket',
		'Cornelius',
		'Minikeg',
		'MiniBarrel',
		'PetKeg',
		'ISpindel',
		'Tilt',
		'GenericSensor',
		'Other'
	];

	it('calculates 0% liquid height at bottom coordinate when vessel is empty', () => {
		for (const subtype of allSubtypes) {
			const bounds = CHAMBER_BOUNDS[subtype];
			const { liquidHeight, liquidY, clampedFill } = calculateLiquidGeometry(subtype, 0);

			expect(clampedFill).toBe(0);
			expect(liquidHeight).toBe(0);
			expect(liquidY).toBe(bounds.bottomY);
		}
	});

	it('calculates 50% liquid height at midpoint coordinate when vessel is half full', () => {
		for (const subtype of allSubtypes) {
			const bounds = CHAMBER_BOUNDS[subtype];
			const usableHeight = bounds.bottomY - bounds.topY;
			const { liquidHeight, liquidY, clampedFill } = calculateLiquidGeometry(subtype, 50);

			expect(clampedFill).toBe(50);
			expect(liquidHeight).toBeCloseTo(usableHeight * 0.5);
			expect(liquidY).toBeCloseTo(bounds.bottomY - usableHeight * 0.5);
		}
	});

	it('calculates 100% liquid height spanning from bottom to top rim when full', () => {
		for (const subtype of allSubtypes) {
			const bounds = CHAMBER_BOUNDS[subtype];
			const usableHeight = bounds.bottomY - bounds.topY;
			const { liquidHeight, liquidY, clampedFill } = calculateLiquidGeometry(subtype, 100);

			expect(clampedFill).toBe(100);
			expect(liquidHeight).toBeCloseTo(usableHeight);
			expect(liquidY).toBeCloseTo(bounds.topY);
		}
	});

	it('clamps negative or NaN fill levels to 0% defensively', () => {
		const neg = calculateLiquidGeometry('Cornelius', -25);
		expect(neg.clampedFill).toBe(0);
		expect(neg.liquidHeight).toBe(0);

		const nanVal = calculateLiquidGeometry('Pan', NaN);
		expect(nanVal.clampedFill).toBe(0);
		expect(nanVal.liquidHeight).toBe(0);
	});

	it('clamps overflowing fill percentages (>100%) to exactly 100% without overflowing bounds', () => {
		const overfill = calculateLiquidGeometry('ConicalFermenter', 150);
		const bounds = CHAMBER_BOUNDS.ConicalFermenter;
		const usableHeight = bounds.bottomY - bounds.topY;

		expect(overfill.clampedFill).toBe(100);
		expect(overfill.liquidHeight).toBeCloseTo(usableHeight);
		expect(overfill.liquidY).toBeCloseTo(bounds.topY);
	});

	it('verifies all 7 requested subtypes have positive usable chamber height', () => {
		for (const subtype of allSubtypes) {
			const bounds = CHAMBER_BOUNDS[subtype];
			const usableHeight = bounds.bottomY - bounds.topY;
			expect(usableHeight).toBeGreaterThan(15);
			expect(bounds.bottomY).toBeLessThanOrEqual(44);
			expect(bounds.topY).toBeGreaterThanOrEqual(6);
		}
	});
});

describe('Bucket Subtype Geometry & Thematic Bounds', () => {
	it('defines valid Bucket chamber boundaries within 48x48 viewport', () => {
		const bounds = CHAMBER_BOUNDS.Bucket;
		expect(bounds.topY).toBeGreaterThanOrEqual(6);
		expect(bounds.bottomY).toBeLessThanOrEqual(44);
		expect(bounds.bottomY - bounds.topY).toBeGreaterThan(15);
	});

	it('calculates exact coordinates across discrete fill increments for Bucket', () => {
		const bounds = CHAMBER_BOUNDS.Bucket;
		const totalHeight = bounds.bottomY - bounds.topY;

		// Empty (0%)
		const empty = calculateLiquidGeometry('Bucket', 0);
		expect(empty.liquidHeight).toBe(0);
		expect(empty.liquidY).toBe(bounds.bottomY);
		expect(empty.clampedFill).toBe(0);

		// Quarter full (25%)
		const quarter = calculateLiquidGeometry('Bucket', 25);
		expect(quarter.liquidHeight).toBeCloseTo(totalHeight * 0.25);
		expect(quarter.liquidY).toBeCloseTo(bounds.bottomY - totalHeight * 0.25);
		expect(quarter.clampedFill).toBe(25);

		// Half full (50%)
		const half = calculateLiquidGeometry('Bucket', 50);
		expect(half.liquidHeight).toBeCloseTo(totalHeight * 0.5);
		expect(half.liquidY).toBeCloseTo(bounds.bottomY - totalHeight * 0.5);
		expect(half.clampedFill).toBe(50);

		// Full (100%)
		const full = calculateLiquidGeometry('Bucket', 100);
		expect(full.liquidHeight).toBeCloseTo(totalHeight);
		expect(full.liquidY).toBeCloseTo(bounds.topY);
		expect(full.clampedFill).toBe(100);
	});

	it('correctly clamps Bucket fill extremes without geometry inversion', () => {
		const bounds = CHAMBER_BOUNDS.Bucket;
		const underflow = calculateLiquidGeometry('Bucket', -50);
		expect(underflow.clampedFill).toBe(0);
		expect(underflow.liquidHeight).toBe(0);
		expect(underflow.liquidY).toBe(bounds.bottomY);

		const overflow = calculateLiquidGeometry('Bucket', 200);
		expect(overflow.clampedFill).toBe(100);
		expect(overflow.liquidHeight).toBeCloseTo(bounds.bottomY - bounds.topY);
		expect(overflow.liquidY).toBeCloseTo(bounds.topY);
	});
});

describe('Vessel Brewing Animations & State Resolution', () => {
	it('returns null when vessel is neither active nor animated', () => {
		expect(resolveVesselAnimationType('Pan')).toBeNull();
		expect(resolveVesselAnimationType('Bucket', { active: false })).toBeNull();
		expect(resolveVesselAnimationType('Cornelius', { animated: false })).toBeNull();
	});

	it('resolves explicit animationStage override regardless of subtype', () => {
		expect(resolveVesselAnimationType('Bucket', { active: true, animationStage: 'boil' })).toBe(
			'boil'
		);
		expect(resolveVesselAnimationType('Pan', { active: true, animationStage: 'ferment' })).toBe(
			'ferment'
		);
		expect(resolveVesselAnimationType('AllInOne', { active: true, animationStage: 'mash' })).toBe(
			'mash'
		);
	});

	it('infers boil animation for boiler/kettle vessels when active', () => {
		expect(resolveVesselAnimationType('Pan', { active: true })).toBe('boil');
		expect(resolveVesselAnimationType('AllInOne', { active: true })).toBe('boil');
		expect(resolveVesselAnimationType('Hlt', { active: true })).toBe('boil');
		expect(resolveVesselAnimationType('HermsRims', { active: true })).toBe('boil');
	});

	it('infers ferment animation for fermenter vessels when active', () => {
		expect(resolveVesselAnimationType('Bucket', { active: true })).toBe('ferment');
		expect(resolveVesselAnimationType('ConicalFermenter', { active: true })).toBe('ferment');
		expect(resolveVesselAnimationType('Carboy', { active: true })).toBe('ferment');
		expect(resolveVesselAnimationType('PressureFermenter', { active: true })).toBe('ferment');
		expect(resolveVesselAnimationType('StainlessBucket', { active: true })).toBe('ferment');
	});

	it('infers package animation for keg and dispensing vessels when active', () => {
		expect(resolveVesselAnimationType('Cornelius', { active: true })).toBe('package');
		expect(resolveVesselAnimationType('Minikeg', { active: true })).toBe('package');
		expect(resolveVesselAnimationType('MiniBarrel', { active: true })).toBe('package');
		expect(resolveVesselAnimationType('PetKeg', { active: true })).toBe('package');
		expect(resolveVesselAnimationType('Bottle', { active: true })).toBe('package');
	});

	it('infers sensor digital telemetry animation for ISpindel, Tilt, and GenericSensor', () => {
		expect(resolveVesselAnimationType('ISpindel', { active: true })).toBe('sensor');
		expect(resolveVesselAnimationType('Tilt', { active: true })).toBe('sensor');
		expect(resolveVesselAnimationType('GenericSensor', { active: true })).toBe('sensor');
		expect(resolveVesselAnimationType('Tilt', { animated: true, animationStage: 'ferment' })).toBe(
			'sensor'
		);
	});
});

describe('Non-Fillable Auxiliary Sensors & Fill Level Rules', () => {
	it('identifies ISpindel, Tilt, and GenericSensor as non-fillable sensors', () => {
		expect(isVesselFillable('ISpindel')).toBe(false);
		expect(isVesselFillable('Tilt')).toBe(false);
		expect(isVesselFillable('GenericSensor')).toBe(false);
	});

	it('identifies all standard liquid vessels as fillable', () => {
		const fillableVessels: EquipmentSubtype[] = [
			'AllInOne',
			'Pan',
			'Hlt',
			'HermsRims',
			'Bucket',
			'ConicalFermenter',
			'Carboy',
			'PressureFermenter',
			'StainlessBucket',
			'Cornelius',
			'Minikeg',
			'MiniBarrel',
			'PetKeg',
			'Other'
		];

		for (const vessel of fillableVessels) {
			expect(isVesselFillable(vessel)).toBe(true);
		}
		expect(isVesselFillable('Bottle')).toBe(true);
	});

	it('exports SENSOR_SUBTYPES containing only the 3 dedicated sensor types', () => {
		expect(SENSOR_SUBTYPES).toEqual(['GenericSensor', 'Tilt', 'ISpindel']);
	});
});

describe('Bottle Subtype Geometry & Thematic Bounds', () => {
	it('defines valid Bottle chamber boundaries within 48x48 viewport', () => {
		const bounds = CHAMBER_BOUNDS.Bottle;
		expect(bounds.topY).toBeGreaterThanOrEqual(6);
		expect(bounds.bottomY).toBeLessThanOrEqual(44);
		expect(bounds.bottomY - bounds.topY).toBeGreaterThan(15);
	});

	it('calculates exact coordinates across discrete fill increments for Bottle', () => {
		const bounds = CHAMBER_BOUNDS.Bottle;
		const totalHeight = bounds.bottomY - bounds.topY;

		// Empty (0%)
		const empty = calculateLiquidGeometry('Bottle', 0);
		expect(empty.liquidHeight).toBe(0);
		expect(empty.liquidY).toBe(bounds.bottomY);
		expect(empty.clampedFill).toBe(0);

		// Quarter full (25%)
		const quarter = calculateLiquidGeometry('Bottle', 25);
		expect(quarter.liquidHeight).toBeCloseTo(totalHeight * 0.25);
		expect(quarter.liquidY).toBeCloseTo(bounds.bottomY - totalHeight * 0.25);
		expect(quarter.clampedFill).toBe(25);

		// Half full (50%)
		const half = calculateLiquidGeometry('Bottle', 50);
		expect(half.liquidHeight).toBeCloseTo(totalHeight * 0.5);
		expect(half.liquidY).toBeCloseTo(bounds.bottomY - totalHeight * 0.5);
		expect(half.clampedFill).toBe(50);

		// Full (100%)
		const full = calculateLiquidGeometry('Bottle', 100);
		expect(full.liquidHeight).toBeCloseTo(totalHeight);
		expect(full.liquidY).toBeCloseTo(bounds.topY);
		expect(full.clampedFill).toBe(100);
	});

	it('correctly clamps Bottle fill extremes without geometry inversion', () => {
		const bounds = CHAMBER_BOUNDS.Bottle;
		const underflow = calculateLiquidGeometry('Bottle', -50);
		expect(underflow.clampedFill).toBe(0);
		expect(underflow.liquidHeight).toBe(0);
		expect(underflow.liquidY).toBe(bounds.bottomY);

		const overflow = calculateLiquidGeometry('Bottle', 200);
		expect(overflow.clampedFill).toBe(100);
		expect(overflow.liquidHeight).toBeCloseTo(bounds.bottomY - bounds.topY);
		expect(overflow.liquidY).toBeCloseTo(bounds.topY);
	});
});
