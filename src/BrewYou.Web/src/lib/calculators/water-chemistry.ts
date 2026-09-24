/**
 * Water chemistry and pH adjustment calculations
 * Based on ASBC, Palmer, and deLange thermodynamic equations for the bicarbonate buffer system
 */

export type AcidType =
	'lactic_80' | 'phosphoric_10' | 'phosphoric_75' | 'phosphoric_85' | 'citric_powder' | 'acid_malt';

export type BaseType = 'baking_soda' | 'slaked_lime' | 'chalk';

export interface AcidInfo {
	id: AcidType;
	nameKey: string;
	unit: 'mL' | 'g';
	mEqPerUnit: number;
	density?: number;
	flavorThresholdPerLiter?: number;
}

export interface BaseInfo {
	id: BaseType;
	nameKey: string;
	unit: 'g';
	mEqPerGram: number;
	calciumContributionPerGram?: number; // mg Ca / g in 1L
	sodiumContributionPerGram?: number; // mg Na / g in 1L
	bicarbonateContributionPerGram?: number; // mg HCO3 / g in 1L
}

export const ACID_CATALOG: Record<AcidType, AcidInfo> = {
	lactic_80: {
		id: 'lactic_80',
		nameKey: 'calculations.water_ph.acids.lactic_80',
		unit: 'mL',
		mEqPerUnit: 10.6,
		density: 1.206,
		flavorThresholdPerLiter: 0.35 // ~400 mg/L
	},
	phosphoric_10: {
		id: 'phosphoric_10',
		nameKey: 'calculations.water_ph.acids.phosphoric_10',
		unit: 'mL',
		mEqPerUnit: 1.08,
		density: 1.05
	},
	phosphoric_75: {
		id: 'phosphoric_75',
		nameKey: 'calculations.water_ph.acids.phosphoric_75',
		unit: 'mL',
		mEqPerUnit: 12.1,
		density: 1.58
	},
	phosphoric_85: {
		id: 'phosphoric_85',
		nameKey: 'calculations.water_ph.acids.phosphoric_85',
		unit: 'mL',
		mEqPerUnit: 14.6,
		density: 1.68
	},
	citric_powder: {
		id: 'citric_powder',
		nameKey: 'calculations.water_ph.acids.citric_powder',
		unit: 'g',
		mEqPerUnit: 15.6
	},
	acid_malt: {
		id: 'acid_malt',
		nameKey: 'calculations.water_ph.acids.acid_malt',
		unit: 'g',
		mEqPerUnit: 0.33 // ~3% lactic acid by weight
	}
};

export const BASE_CATALOG: Record<BaseType, BaseInfo> = {
	baking_soda: {
		id: 'baking_soda',
		nameKey: 'calculations.water_ph.bases.baking_soda',
		unit: 'g',
		mEqPerGram: 11.9,
		sodiumContributionPerGram: 273.7,
		bicarbonateContributionPerGram: 726.3
	},
	slaked_lime: {
		id: 'slaked_lime',
		nameKey: 'calculations.water_ph.bases.slaked_lime',
		unit: 'g',
		mEqPerGram: 27.0,
		calciumContributionPerGram: 540.9
	},
	chalk: {
		id: 'chalk',
		nameKey: 'calculations.water_ph.bases.chalk',
		unit: 'g',
		mEqPerGram: 20.0,
		calciumContributionPerGram: 400.4,
		bicarbonateContributionPerGram: 599.6
	}
};

export interface WaterPhAcidResult {
	mode: 'acid';
	requiredAmount: number;
	unit: 'mL' | 'g';
	amountPerLiter: number;
	amountPerGallon: number;
	mEqTotal: number;
	exceedsFlavorThreshold: boolean;
	flavorThresholdPerLiter?: number;
}

export interface WaterPhBaseResult {
	mode: 'base';
	requiredAmount: number;
	unit: 'g';
	amountPerLiter: number;
	amountPerGallon: number;
	mEqTotal: number;
	addedAlkalinityPpm: number;
	addedCalciumMgL?: number;
	addedSodiumMgL?: number;
}

export type WaterPhResult = WaterPhAcidResult | WaterPhBaseResult;

/**
 * Calculates mEq of acid per liter required to titrate water alkalinity to target pH
 * using the deLange carbonate/bicarbonate equilibrium equation (pK1 = 6.38).
 */
export function calculateAcidMeqPerLiter(
	startingPh: number,
	targetPh: number,
	alkalinityPpmCaCo3: number
): number {
	if (startingPh <= targetPh) return 0;
	if (alkalinityPpmCaCo3 <= 0) {
		// Pure unbuffered water: delta [H+]
		const hStart = Math.pow(10, -startingPh);
		const hTarget = Math.pow(10, -targetPh);
		return Math.max(0, (hTarget - hStart) * 1000);
	}

	const pk1 = 6.38;
	const ratioStart = Math.pow(10, startingPh - pk1);
	const fractionStart = ratioStart / (1.0 + ratioStart);

	const ratioTarget = Math.pow(10, targetPh - pk1);
	const fractionTarget = ratioTarget / (1.0 + ratioTarget);

	const alkMeqPerL = alkalinityPpmCaCo3 / 50.0;
	const neutralizedFraction = Math.max(0, fractionStart - fractionTarget);
	const alkalinityMeqNeeded = alkMeqPerL * (neutralizedFraction / Math.max(0.001, fractionStart));

	// Free hydrogen ion adjustment
	const freeH = Math.pow(10, -targetPh) - Math.pow(10, -startingPh);
	const freeHMeq = freeH * 1000;

	return Math.max(0, alkalinityMeqNeeded + freeHMeq);
}

/**
 * Calculates acid addition required to lower water pH
 */
export function calculateWaterAcidification(
	waterVolumeLiters: number,
	startingPh: number,
	targetPh: number,
	alkalinityPpmCaCo3: number,
	acidType: AcidType = 'lactic_80'
): WaterPhAcidResult {
	const vol = Math.max(0.01, waterVolumeLiters);
	const acid = ACID_CATALOG[acidType] ?? ACID_CATALOG.lactic_80;

	const meqPerL = calculateAcidMeqPerLiter(startingPh, targetPh, alkalinityPpmCaCo3);
	const totalMeq = meqPerL * vol;
	const amount = totalMeq / Math.max(0.01, acid.mEqPerUnit);

	const amountPerL = amount / vol;
	const amountPerGal = amountPerL * 3.785411784;

	const exceedsFlavorThreshold =
		acid.flavorThresholdPerLiter !== undefined && amountPerL > acid.flavorThresholdPerLiter;

	return {
		mode: 'acid',
		requiredAmount: Number(amount.toFixed(2)),
		unit: acid.unit,
		amountPerLiter: Number(amountPerL.toFixed(3)),
		amountPerGallon: Number(amountPerGal.toFixed(2)),
		mEqTotal: Number(totalMeq.toFixed(2)),
		exceedsFlavorThreshold,
		flavorThresholdPerLiter: acid.flavorThresholdPerLiter
	};
}

/**
 * Calculates base addition required to raise alkalinity / pH
 */
export function calculateWaterAlkalinization(
	waterVolumeLiters: number,
	startingPh: number,
	targetPh: number,
	baseType: BaseType = 'baking_soda'
): WaterPhBaseResult {
	const vol = Math.max(0.01, waterVolumeLiters);
	const base = BASE_CATALOG[baseType] ?? BASE_CATALOG.baking_soda;

	// In brewing water, raising pH usually means adding 20 - 100 ppm alkalinity
	// Empirical approximation: ~25 ppm CaCO3 alkalinity per 0.2 pH shift
	const deltaPh = Math.max(0, targetPh - startingPh);
	const targetAlkalinityIncreasePpm = deltaPh * 125.0; // 1.0 pH shift ≈ 125 ppm CaCO3
	const meqPerLNeeded = targetAlkalinityIncreasePpm / 50.0;
	const totalMeq = meqPerLNeeded * vol;

	const gramsNeeded = totalMeq / Math.max(0.01, base.mEqPerGram);
	const gramsPerL = gramsNeeded / vol;
	const gramsPerGal = gramsPerL * 3.785411784;

	const addedCalciumMgL = base.calciumContributionPerGram
		? (gramsNeeded * base.calciumContributionPerGram) / vol
		: undefined;
	const addedSodiumMgL = base.sodiumContributionPerGram
		? (gramsNeeded * base.sodiumContributionPerGram) / vol
		: undefined;

	return {
		mode: 'base',
		requiredAmount: Number(gramsNeeded.toFixed(2)),
		unit: 'g',
		amountPerLiter: Number(gramsPerL.toFixed(3)),
		amountPerGallon: Number(gramsPerGal.toFixed(2)),
		mEqTotal: Number(totalMeq.toFixed(2)),
		addedAlkalinityPpm: Number(targetAlkalinityIncreasePpm.toFixed(1)),
		addedCalciumMgL: addedCalciumMgL ? Number(addedCalciumMgL.toFixed(1)) : undefined,
		addedSodiumMgL: addedSodiumMgL ? Number(addedSodiumMgL.toFixed(1)) : undefined
	};
}
