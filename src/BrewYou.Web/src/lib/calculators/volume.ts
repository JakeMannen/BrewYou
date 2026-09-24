import type { BatchVolumeProfileDto } from '$lib/types/api';

export type TargetVolumeBasis = 'fermenter' | 'packaged';

export interface VolumeCalculationInput {
	targetBatchSizeLiters: number;
	boilTimeMinutes: number;
	totalGrainWeightKg: number;
	targetBasis?: TargetVolumeBasis;
	spargeEnabled?: boolean;
	boilOffRatePerHour?: number;
	grainAbsorptionRate?: number;
	kettleTrubLossLiters?: number;
	fermenterLossLiters?: number;
	mashTunDeadSpaceLiters?: number;
	coolingShrinkagePercent?: number;
	packagingLossLiters?: number;
	mashThicknessLitersPerKg?: number;
}

export interface VolumeCalculationResult {
	totalWaterLiters: number;
	strikeWaterLiters: number;
	spargeWaterLiters: number;

	targetPreBoilVolumeLiters: number;
	targetPostBoilVolumeLiters: number;
	targetFermenterVolumeLiters: number;
	targetPackagedVolumeLiters: number;

	grainAbsorptionLossLiters: number;
	boilOffLossLiters: number;
	kettleTrubLossLiters: number;
	shrinkageLossLiters: number;
	fermenterLossLiters: number;
	packagingLossLiters: number;
	mashTunDeadSpaceLossLiters: number;
}

const LITERS_TO_GALLONS = 0.264172052;

function roundTo(val: number, decimals: number): number {
	const factor = Math.pow(10, decimals);
	return Math.round(val * factor) / factor;
}

/**
 * Calculates full water requirements and volume milestones working backwards from target batch size.
 * When targetBasis is 'fermenter' (default), targetBatchSizeLiters is the volume into the fermenter.
 * When targetBasis is 'packaged', targetBatchSizeLiters is the desired final packaged beer volume
 * and all losses are added backwards to determine how much wort is needed at each stage.
 */
export function calculateWaterRequirements(input: VolumeCalculationInput): VolumeCalculationResult {
	const basis = input.targetBasis ?? 'fermenter';
	const boilOffRate = input.boilOffRatePerHour ?? 3.0;
	const grainAbsorptionRate = input.grainAbsorptionRate ?? 0.96;
	const kettleTrub = input.kettleTrubLossLiters ?? 1.5;
	const fermenterLoss = input.fermenterLossLiters ?? 1.5;
	const mashTunDeadSpace = input.mashTunDeadSpaceLiters ?? 0.0;
	const shrinkagePercent = input.coolingShrinkagePercent ?? 4.0;
	const packagingLoss = input.packagingLossLiters ?? 0.5;
	const mashThickness = input.mashThicknessLitersPerKg ?? 3.0;

	let targetFermenter: number;
	let targetPackaged: number;

	if (basis === 'packaged') {
		// Brewer specifies desired packaged beer volume; work backwards
		targetPackaged = input.targetBatchSizeLiters;
		targetFermenter = targetPackaged + fermenterLoss + packagingLoss;
	} else {
		// Brewer specifies volume into fermenter (legacy default)
		targetFermenter = input.targetBatchSizeLiters;
		targetPackaged = targetFermenter - fermenterLoss;
	}

	// Post-boil cold volume needed in kettle (to yield targetFermenter after trub separation)
	const postBoilCold = targetFermenter + kettleTrub;

	// Hot post-boil (before cooling shrinkage)
	const shrinkageFactor = 1.0 - shrinkagePercent / 100.0;
	const postBoilHot = shrinkageFactor > 0 ? postBoilCold / shrinkageFactor : postBoilCold;

	// Boil off
	const boilOff = boilOffRate * (input.boilTimeMinutes / 60.0);

	// Hot pre-boil
	const preBoilHot = postBoilHot + boilOff;

	// Pre-boil cold equivalent
	const preBoilCold = preBoilHot * shrinkageFactor;

	// Grain absorption
	const grainAbsorption = input.totalGrainWeightKg * grainAbsorptionRate;

	// Total water
	const totalWater = preBoilCold + grainAbsorption + mashTunDeadSpace;

	// Strike and sparge water split
	const sparge = input.spargeEnabled ?? true;
	let strikeWater: number;
	let spargeWater: number;

	if (sparge) {
		// Traditional sparge: split water based on mash thickness ratio
		strikeWater = input.totalGrainWeightKg * mashThickness + mashTunDeadSpace;
		spargeWater = Math.max(0, totalWater - strikeWater);
	} else {
		// No-sparge / BIAB: all water is strike water
		strikeWater = totalWater;
		spargeWater = 0;
	}

	return {
		totalWaterLiters: roundTo(totalWater, 2),
		strikeWaterLiters: roundTo(strikeWater, 2),
		spargeWaterLiters: roundTo(spargeWater, 2),

		targetPreBoilVolumeLiters: roundTo(preBoilCold, 2),
		targetPostBoilVolumeLiters: roundTo(postBoilCold, 2),
		targetFermenterVolumeLiters: roundTo(targetFermenter, 2),
		targetPackagedVolumeLiters: roundTo(targetPackaged, 2),

		grainAbsorptionLossLiters: roundTo(grainAbsorption, 2),
		boilOffLossLiters: roundTo(boilOff, 2),
		kettleTrubLossLiters: roundTo(kettleTrub, 2),
		shrinkageLossLiters: roundTo(postBoilHot - postBoilCold, 2),
		fermenterLossLiters: roundTo(fermenterLoss, 2),
		packagingLossLiters: roundTo(packagingLoss, 2),
		mashTunDeadSpaceLossLiters: roundTo(mashTunDeadSpace, 2)
	};
}

/**
 * Expected post-boil cold volume based on measured pre-boil volume.
 */
export function calculateExpectedPostBoilVolume(
	measuredPreBoilVolumeLiters: number,
	boilOffRatePerHour: number,
	boilTimeMinutes: number,
	coolingShrinkagePercent: number = 4.0
): number {
	const shrinkageFactor = 1.0 - coolingShrinkagePercent / 100.0;
	if (shrinkageFactor <= 0) return 0;

	const preBoilHot = measuredPreBoilVolumeLiters / shrinkageFactor;
	const boilOff = boilOffRatePerHour * (boilTimeMinutes / 60.0);
	const postBoilHot = preBoilHot - boilOff;
	const postBoilCold = postBoilHot * shrinkageFactor;

	return Math.max(0, roundTo(postBoilCold, 2));
}

/**
 * Recalculates downstream milestone targets from measured checkpoints based on equipment settings.
 * When a brewer manually records or edits a volume between steps (pre-boil, post-boil, or fermenter),
 * subsequent target volumes are dynamically updated according to boil-off, cooling shrinkage, and vessel losses.
 */
export function recalculatePostStepMilestones(
	profile: BatchVolumeProfileDto,
	boilTimeMinutes: number
): BatchVolumeProfileDto {
	const updated = { ...profile };

	// 1. Post-Boil Milestone (updated if pre-boil is measured)
	if (
		updated.measuredPreBoilVolumeLiters !== null &&
		updated.measuredPreBoilVolumeLiters !== undefined &&
		!isNaN(updated.measuredPreBoilVolumeLiters)
	) {
		updated.targetPostBoilVolumeLiters = calculateExpectedPostBoilVolume(
			updated.measuredPreBoilVolumeLiters,
			updated.boilOffRatePerHour,
			boilTimeMinutes,
			updated.coolingShrinkagePercent
		);
	}

	// 2. Fermenter Milestone (derived from measured or expected post-boil minus kettle trub loss)
	const effectivePostBoil =
		updated.measuredPostBoilVolumeLiters !== null &&
		updated.measuredPostBoilVolumeLiters !== undefined &&
		!isNaN(updated.measuredPostBoilVolumeLiters)
			? updated.measuredPostBoilVolumeLiters
			: updated.targetPostBoilVolumeLiters;
	updated.targetFermenterVolumeLiters = Math.max(
		0,
		roundTo(effectivePostBoil - (updated.kettleTrubLossLiters ?? 0), 2)
	);

	// 3. Packaging Milestone (derived from measured or expected fermenter volume minus equipment losses)
	const effectiveFermenter =
		updated.measuredFermenterVolumeLiters !== null &&
		updated.measuredFermenterVolumeLiters !== undefined &&
		!isNaN(updated.measuredFermenterVolumeLiters)
			? updated.measuredFermenterVolumeLiters
			: updated.targetFermenterVolumeLiters;
	const fermenterToPackagedLoss =
		(updated.fermenterTrubLossLiters ?? 0) + (updated.packagingLossLiters ?? 0);
	updated.targetPackagedVolumeLiters = Math.max(
		0,
		roundTo(effectiveFermenter - fermenterToPackagedLoss, 2)
	);

	return updated;
}

/**
 * Expected OG based on pre-boil volume and gravity concentration.
 */
export function calculateExpectedOg(
	measuredPreBoilVolumeLiters: number,
	measuredPreBoilGravity: number,
	expectedPostBoilVolumeLiters: number
): number {
	if (
		expectedPostBoilVolumeLiters <= 0 ||
		measuredPreBoilVolumeLiters <= 0 ||
		measuredPreBoilGravity < 1.0
	) {
		return 0;
	}

	const og =
		1.0 +
		(measuredPreBoilVolumeLiters * (measuredPreBoilGravity - 1.0)) / expectedPostBoilVolumeLiters;
	return roundTo(og, 3);
}

/**
 * Extra boil minutes needed if pre-boil volume is higher than expected.
 */
export function calculateBoilTimeAdjustment(
	actualPreBoilVolumeLiters: number,
	targetPreBoilVolumeLiters: number,
	boilOffRatePerHour: number
): number {
	if (boilOffRatePerHour <= 0 || actualPreBoilVolumeLiters <= targetPreBoilVolumeLiters) {
		return 0;
	}

	const excessVolume = actualPreBoilVolumeLiters - targetPreBoilVolumeLiters;
	const extraHours = excessVolume / boilOffRatePerHour;
	return Math.round(extraHours * 60.0);
}

/**
 * Actual boil-off rate from measured volumes.
 */
export function calculateActualBoilOffRate(
	preBoilVolumeLiters: number,
	postBoilVolumeLiters: number,
	boilTimeMinutes: number
): number {
	if (boilTimeMinutes <= 0 || preBoilVolumeLiters <= postBoilVolumeLiters) {
		return 0;
	}

	const volumeLost = preBoilVolumeLiters - postBoilVolumeLiters;
	const rate = volumeLost / (boilTimeMinutes / 60.0);
	return roundTo(rate, 2);
}

/**
 * Actual grain absorption rate from brew day measurements.
 */
export function calculateActualGrainAbsorption(
	totalWaterUsedLiters: number,
	preBoilVolumeLiters: number,
	mashTunDeadSpaceLiters: number,
	totalGrainWeightKg: number
): number {
	if (totalGrainWeightKg <= 0) return 0;

	const grainAbsorption = totalWaterUsedLiters - preBoilVolumeLiters - mashTunDeadSpaceLiters;
	const rate = grainAbsorption / totalGrainWeightKg;
	return Math.max(0, roundTo(rate, 2));
}

/**
 * Mash efficiency percentage.
 */
export function calculateMashEfficiency(
	preBoilVolumeLiters: number,
	preBoilGravity: number,
	totalPotentialPoints: number
): number {
	if (preBoilVolumeLiters <= 0 || preBoilGravity <= 1.0 || totalPotentialPoints <= 0) {
		return 0;
	}

	const volumeGallons = preBoilVolumeLiters * LITERS_TO_GALLONS;
	const yieldedPoints = (preBoilGravity - 1.0) * 1000.0 * volumeGallons;
	const efficiency = (yieldedPoints / totalPotentialPoints) * 100.0;
	return Math.max(0, roundTo(efficiency, 1));
}
