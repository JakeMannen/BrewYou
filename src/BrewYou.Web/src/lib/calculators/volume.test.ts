import { describe, expect, it } from 'vitest';
import {
	calculateWaterRequirements,
	calculateExpectedPostBoilVolume,
	calculateExpectedOg,
	calculateBoilTimeAdjustment,
	calculateActualBoilOffRate,
	calculateActualGrainAbsorption,
	calculateMashEfficiency,
	recalculatePostStepMilestones
} from './volume';
import type { BatchVolumeProfileDto } from '$lib/types/api';

describe('calculateWaterRequirements', () => {
	it('calculates expected water schedule for standard 20L batch with 4.5kg grain', () => {
		const result = calculateWaterRequirements({
			targetBatchSizeLiters: 20.0,
			boilTimeMinutes: 60,
			totalGrainWeightKg: 4.5,
			boilOffRatePerHour: 3.0,
			grainAbsorptionRate: 0.96,
			kettleTrubLossLiters: 1.5,
			fermenterLossLiters: 1.5,
			mashTunDeadSpaceLiters: 0.0,
			coolingShrinkagePercent: 4.0,
			packagingLossLiters: 0.5,
			mashThicknessLitersPerKg: 3.0
		});

		expect(result.totalWaterLiters).toBeGreaterThan(25);
		expect(result.strikeWaterLiters).toBe(13.5); // 4.5 * 3.0
		expect(result.spargeWaterLiters).toBeCloseTo(result.totalWaterLiters - 13.5, 2);
		expect(result.targetFermenterVolumeLiters).toBe(20.0);
		expect(result.targetPackagedVolumeLiters).toBe(18.5); // 20 - 1.5
		expect(result.grainAbsorptionLossLiters).toBe(4.32); // 4.5 * 0.96
		expect(result.boilOffLossLiters).toBe(3.0);
	});

	it('handles BIAB scenario where sparge is 0 if strike water exceeds total water', () => {
		const result = calculateWaterRequirements({
			targetBatchSizeLiters: 20.0,
			boilTimeMinutes: 60,
			totalGrainWeightKg: 4.5,
			mashThicknessLitersPerKg: 8.0 // High thickness (no-sparge BIAB)
		});

		expect(result.spargeWaterLiters).toBe(0);
		expect(result.strikeWaterLiters).toBe(36.0); // 4.5 * 8.0
	});

	it('uses fermenter basis by default when targetBasis is omitted', () => {
		const result = calculateWaterRequirements({
			targetBatchSizeLiters: 20.0,
			boilTimeMinutes: 60,
			totalGrainWeightKg: 4.5,
			fermenterLossLiters: 1.5,
			packagingLossLiters: 0.5
		});

		// Default: target is fermenter volume
		expect(result.targetFermenterVolumeLiters).toBe(20.0);
		expect(result.targetPackagedVolumeLiters).toBe(18.5); // 20 - 1.5
	});

	it('calculates backwards from packaged volume when targetBasis is packaged', () => {
		const result = calculateWaterRequirements({
			targetBatchSizeLiters: 15.0,
			targetBasis: 'packaged',
			boilTimeMinutes: 60,
			totalGrainWeightKg: 5.0,
			boilOffRatePerHour: 3.0,
			grainAbsorptionRate: 0.96,
			kettleTrubLossLiters: 1.5,
			fermenterLossLiters: 1.5,
			mashTunDeadSpaceLiters: 0.0,
			coolingShrinkagePercent: 4.0,
			packagingLossLiters: 0.5,
			mashThicknessLitersPerKg: 3.0
		});

		// Packaged target must equal input
		expect(result.targetPackagedVolumeLiters).toBe(15.0);

		// Fermenter = packaged + fermenterLoss + packagingLoss = 15 + 1.5 + 0.5 = 17.0
		expect(result.targetFermenterVolumeLiters).toBe(17.0);

		// Post-boil cold = fermenter + kettleTrub = 17.0 + 1.5 = 18.5
		expect(result.targetPostBoilVolumeLiters).toBe(18.5);

		// Total water must be larger than pre-boil (grain absorption added)
		expect(result.totalWaterLiters).toBeGreaterThan(result.targetPreBoilVolumeLiters);

		// Strike water = 5.0 * 3.0 = 15.0
		expect(result.strikeWaterLiters).toBe(15.0);

		// Grain absorption = 5.0 * 0.96 = 4.8
		expect(result.grainAbsorptionLossLiters).toBe(4.8);

		// Boil-off = 3.0 * 1 = 3.0
		expect(result.boilOffLossLiters).toBe(3.0);
	});

	it('produces higher total water for packaged basis than fermenter basis with same target', () => {
		const sharedInput = {
			targetBatchSizeLiters: 20.0,
			boilTimeMinutes: 60,
			totalGrainWeightKg: 4.5,
			fermenterLossLiters: 1.5,
			packagingLossLiters: 0.5
		} as const;

		const fermenterResult = calculateWaterRequirements({
			...sharedInput,
			targetBasis: 'fermenter'
		});
		const packagedResult = calculateWaterRequirements({
			...sharedInput,
			targetBasis: 'packaged'
		});

		// With packaged basis, 20L means 20L in keg, requiring more water
		expect(packagedResult.totalWaterLiters).toBeGreaterThan(fermenterResult.totalWaterLiters);
		expect(packagedResult.targetFermenterVolumeLiters).toBeGreaterThan(
			fermenterResult.targetFermenterVolumeLiters
		);
		expect(packagedResult.targetPackagedVolumeLiters).toBe(20.0);
		expect(fermenterResult.targetPackagedVolumeLiters).toBe(18.5);
	});

	it('sets sparge to 0 and all water as strike when spargeEnabled is false', () => {
		const result = calculateWaterRequirements({
			targetBatchSizeLiters: 20.0,
			boilTimeMinutes: 60,
			totalGrainWeightKg: 4.5,
			spargeEnabled: false,
			boilOffRatePerHour: 3.0,
			kettleTrubLossLiters: 1.5,
			fermenterLossLiters: 1.5,
			mashThicknessLitersPerKg: 3.0
		});

		expect(result.spargeWaterLiters).toBe(0);
		expect(result.strikeWaterLiters).toBe(result.totalWaterLiters);
	});

	it('produces same total water regardless of spargeEnabled flag', () => {
		const sharedInput = {
			targetBatchSizeLiters: 20.0,
			boilTimeMinutes: 60,
			totalGrainWeightKg: 4.5,
			boilOffRatePerHour: 3.0,
			kettleTrubLossLiters: 1.5,
			fermenterLossLiters: 1.5
		} as const;

		const withSparge = calculateWaterRequirements({ ...sharedInput, spargeEnabled: true });
		const withoutSparge = calculateWaterRequirements({ ...sharedInput, spargeEnabled: false });

		// Total water needed is the same — only the split changes
		expect(withoutSparge.totalWaterLiters).toBe(withSparge.totalWaterLiters);
		expect(withoutSparge.spargeWaterLiters).toBe(0);
		expect(withoutSparge.strikeWaterLiters).toBe(withoutSparge.totalWaterLiters);
		expect(withSparge.spargeWaterLiters).toBeGreaterThan(0);
	});
});

describe('calculateExpectedPostBoilVolume', () => {
	it('computes correct post-boil cold volume', () => {
		const result = calculateExpectedPostBoilVolume(28.0, 3.5, 60, 4.0);
		expect(result).toBeGreaterThan(20);
		expect(result).toBeLessThan(28);
	});

	it('returns 0 when cooling shrinkage is 100%', () => {
		const result = calculateExpectedPostBoilVolume(28.0, 3.5, 60, 100.0);
		expect(result).toBe(0);
	});
});

describe('calculateExpectedOg', () => {
	it('concentrates gravity points proportionally', () => {
		const og = calculateExpectedOg(28.0, 1.04, 21.5);
		expect(og).toBeGreaterThan(1.04);
		expect(og).toBe(1.052);
	});

	it('returns 0 for invalid inputs', () => {
		expect(calculateExpectedOg(0, 1.04, 20)).toBe(0);
		expect(calculateExpectedOg(20, 0.999, 20)).toBe(0);
		expect(calculateExpectedOg(20, 1.04, 0)).toBe(0);
	});
});

describe('calculateBoilTimeAdjustment', () => {
	it('calculates extra boiling minutes for excess volume', () => {
		const mins = calculateBoilTimeAdjustment(31.5, 28.0, 3.5);
		expect(mins).toBe(60); // 3.5L excess at 3.5 L/hr = 60 min
	});

	it('returns 0 when actual volume is at or below target', () => {
		expect(calculateBoilTimeAdjustment(27.0, 28.0, 3.5)).toBe(0);
		expect(calculateBoilTimeAdjustment(28.0, 28.0, 3.5)).toBe(0);
	});

	it('returns 0 if boil-off rate is zero or negative', () => {
		expect(calculateBoilTimeAdjustment(30.0, 28.0, 0)).toBe(0);
	});
});

describe('calculateActualBoilOffRate', () => {
	it('calculates evaporation rate in L/hr', () => {
		const rate = calculateActualBoilOffRate(28.0, 24.5, 60);
		expect(rate).toBe(3.5);
	});

	it('guards against division by zero and invalid transitions', () => {
		expect(calculateActualBoilOffRate(28.0, 24.5, 0)).toBe(0);
		expect(calculateActualBoilOffRate(20.0, 25.0, 60)).toBe(0);
	});
});

describe('calculateActualGrainAbsorption', () => {
	it('calculates absorption in L/kg', () => {
		const rate = calculateActualGrainAbsorption(33.85, 28.57, 1.0, 4.5);
		expect(rate).toBeCloseTo(0.95, 2);
	});

	it('returns 0 when grain weight is 0', () => {
		expect(calculateActualGrainAbsorption(30, 25, 1, 0)).toBe(0);
	});
});

describe('calculateMashEfficiency', () => {
	it('calculates mash efficiency with known values', () => {
		// 28L pre-boil at 1.045 with 400 potential points
		const eff = calculateMashEfficiency(28.0, 1.045, 400.0);
		expect(eff).toBeGreaterThan(0);
		expect(eff).toBeLessThan(100);
	});

	it('returns 0 for invalid inputs', () => {
		expect(calculateMashEfficiency(0, 1.045, 400)).toBe(0);
		expect(calculateMashEfficiency(28, 1.0, 400)).toBe(0);
		expect(calculateMashEfficiency(28, 1.045, 0)).toBe(0);
	});
});

describe('recalculatePostStepMilestones', () => {
	const initialProfile: BatchVolumeProfileDto = {
		totalWaterLiters: 33,
		strikeWaterLiters: 18,
		spargeWaterLiters: 15,
		targetPreBoilVolumeLiters: 27.0,
		measuredPreBoilVolumeLiters: null,
		measuredPreBoilGravity: null,
		targetPostBoilVolumeLiters: 23.0,
		measuredPostBoilVolumeLiters: null,
		targetFermenterVolumeLiters: 21.0,
		measuredFermenterVolumeLiters: null,
		targetPackagedVolumeLiters: 19.0,
		measuredPackagedVolumeLiters: null,
		boilOffRatePerHour: 4.0,
		grainAbsorptionRateLPerKg: 1.0,
		kettleTrubLossLiters: 2.0,
		fermenterTrubLossLiters: 1.5,
		mashTunDeadSpaceLiters: 0.5,
		coolingShrinkagePercent: 4.0,
		packagingLossLiters: 0.5
	};

	it('updates post-boil, fermenter, and packaged targets when pre-boil is measured', () => {
		const updated = recalculatePostStepMilestones(
			{
				...initialProfile,
				measuredPreBoilVolumeLiters: 25.0
			},
			60
		);

		// preBoilHot = 25.0 / 0.96 = 26.04166...
		// postBoilHot = 26.04166... - 4.0 = 22.04166...
		// postBoilCold = 22.04166... * 0.96 = 21.16L
		expect(updated.targetPostBoilVolumeLiters).toBe(21.16);

		// Fermenter: 21.16 - 2.0 = 19.16
		expect(updated.targetFermenterVolumeLiters).toBe(19.16);

		// Packaged: 19.16 - (1.5 + 0.5) = 17.16
		expect(updated.targetPackagedVolumeLiters).toBe(17.16);
	});

	it('updates fermenter and packaged targets from measured post-boil volume', () => {
		const updated = recalculatePostStepMilestones(
			{
				...initialProfile,
				measuredPreBoilVolumeLiters: 25.0,
				measuredPostBoilVolumeLiters: 22.0
			},
			60
		);

		// Fermenter derived from measured post-boil: 22.0 - 2.0 = 20.0
		expect(updated.targetFermenterVolumeLiters).toBe(20.0);

		// Packaged derived from updated fermenter target: 20.0 - 2.0 = 18.0
		expect(updated.targetPackagedVolumeLiters).toBe(18.0);
	});

	it('updates packaged target from measured fermenter volume', () => {
		const updated = recalculatePostStepMilestones(
			{
				...initialProfile,
				measuredPreBoilVolumeLiters: 25.0,
				measuredPostBoilVolumeLiters: 22.0,
				measuredFermenterVolumeLiters: 19.5
			},
			60
		);

		// Packaged derived from measured fermenter volume: 19.5 - 2.0 = 17.5
		expect(updated.targetPackagedVolumeLiters).toBe(17.5);
	});
});
