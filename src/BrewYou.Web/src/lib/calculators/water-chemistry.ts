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

// ==========================================
// Full Brewing Water Chemistry & Recipe Water Plan Helpers
// ==========================================

export interface WaterIons {
	calciumPpm: number;
	magnesiumPpm: number;
	sodiumPpm: number;
	sulfatePpm: number;
	chloridePpm: number;
	bicarbonatePpm: number;
}

export interface WaterSaltAdditions {
	gypsumGrams: number;
	calciumChlorideGrams: number;
	epsomSaltGrams: number;
	tableSaltGrams: number;
	bakingSodaGrams: number;
	chalkGrams: number;
	acidType: string;
	acidAmountMlOrG: number;
}

export interface GristGrainSample {
	weightKg: number;
	colorSrm: number;
	name?: string | null;
}

export type WaterBalanceProfile =
	'SoftNeutral' | 'VeryBitter' | 'Bitter' | 'Balanced' | 'Malty' | 'VeryMalty';

export interface EvaluatedWaterPlan {
	mashIons: WaterIons;
	totalIons: WaterIons;
	sulfateToChlorideRatio: number | null;
	balanceProfile: WaterBalanceProfile;
	alkalinityPpm: number;
	residualAlkalinityPpm: number;
	predictedMashPh: number;
	hasChalkWarning: boolean;
}

export const EMPTY_SALT_ADDITIONS = (acidType = 'Lactic80'): WaterSaltAdditions => ({
	gypsumGrams: 0,
	calciumChlorideGrams: 0,
	epsomSaltGrams: 0,
	tableSaltGrams: 0,
	bakingSodaGrams: 0,
	chalkGrams: 0,
	acidType,
	acidAmountMlOrG: 0
});

const WATER_ACID_MEQ_PER_UNIT: Record<string, number> = {
	lactic80: 10.57,
	lactic88: 11.82,
	phosphoric10: 1.07,
	phosphoric75: 12.1,
	phosphoric85: 14.7,
	citric: 15.61,
	none: 0
};

export function getWaterAcidMeqPerUnit(acidType?: string | null): number {
	return WATER_ACID_MEQ_PER_UNIT[(acidType ?? 'none').trim().toLowerCase()] ?? 0;
}

export function calculateWaterIonsAfterAdditions(
	source: WaterIons,
	additions: WaterSaltAdditions,
	volumeLiters: number
): WaterIons {
	if (volumeLiters <= 0) return { ...source };
	const inv = 1 / volumeLiters;

	const ca =
		source.calciumPpm +
		Math.max(0, additions.gypsumGrams) * 232.8 * inv +
		Math.max(0, additions.calciumChlorideGrams) * 272.6 * inv +
		Math.max(0, additions.chalkGrams) * 400.4 * inv;

	const mg = source.magnesiumPpm + Math.max(0, additions.epsomSaltGrams) * 98.6 * inv;

	const na =
		source.sodiumPpm +
		Math.max(0, additions.tableSaltGrams) * 393.4 * inv +
		Math.max(0, additions.bakingSodaGrams) * 273.7 * inv;

	const so4 =
		source.sulfatePpm +
		Math.max(0, additions.gypsumGrams) * 557.7 * inv +
		Math.max(0, additions.epsomSaltGrams) * 389.7 * inv;

	const cl =
		source.chloridePpm +
		Math.max(0, additions.calciumChlorideGrams) * 482.3 * inv +
		Math.max(0, additions.tableSaltGrams) * 606.6 * inv;

	const rawHco3 =
		source.bicarbonatePpm +
		Math.max(0, additions.bakingSodaGrams) * 726.3 * inv +
		Math.max(0, additions.chalkGrams) * 599.6 * inv;

	const acidMeq =
		Math.max(0, additions.acidAmountMlOrG) * getWaterAcidMeqPerUnit(additions.acidType);
	const hco3 = Math.max(0, rawHco3 - acidMeq * 61.0168 * inv);

	return {
		calciumPpm: Number(ca.toFixed(1)),
		magnesiumPpm: Number(mg.toFixed(1)),
		sodiumPpm: Number(na.toFixed(1)),
		sulfatePpm: Number(so4.toFixed(1)),
		chloridePpm: Number(cl.toFixed(1)),
		bicarbonatePpm: Number(hco3.toFixed(1))
	};
}

export function calculateAlkalinityAsCaCo3(bicarbonatePpm: number): number {
	return Number((bicarbonatePpm * (50.0435 / 61.0168)).toFixed(1));
}

export function calculateResidualAlkalinity(ions: WaterIons): number {
	const alk = ions.bicarbonatePpm * (50.0435 / 61.0168);
	const offset = ions.calciumPpm / 1.403 + ions.magnesiumPpm / 1.701;
	return Number((alk - offset).toFixed(1));
}

export function calculateSulfateToChlorideRatio(
	sulfatePpm: number,
	chloridePpm: number
): number | null {
	if (chloridePpm <= 0.1) {
		return sulfatePpm > 0 ? null : 1.0;
	}
	return Number((sulfatePpm / chloridePpm).toFixed(2));
}

export function classifySulfateChlorideBalance(
	ratio: number | null,
	sulfatePpm: number,
	chloridePpm: number
): WaterBalanceProfile {
	if (sulfatePpm < 25 && chloridePpm < 25) return 'SoftNeutral';
	if (ratio === null || ratio >= 3.0) return 'VeryBitter';
	if (ratio >= 1.5) return 'Bitter';
	if (ratio >= 0.75) return 'Balanced';
	if (ratio >= 0.4) return 'Malty';
	return 'VeryMalty';
}

export function estimateGrainDistilledWaterPh(grain: GristGrainSample): number {
	const nameLower = (grain.name ?? '').toLowerCase();
	if (nameLower.includes('acidulated') || nameLower.includes('sauermalz')) return 3.45;
	if (
		grain.colorSrm >= 150 ||
		nameLower.includes('roast') ||
		nameLower.includes('black') ||
		nameLower.includes('chocolate')
	) {
		return Math.max(4.35, 4.72 - 0.0005 * Math.min(grain.colorSrm, 600));
	}
	if (
		grain.colorSrm >= 25 ||
		nameLower.includes('crystal') ||
		nameLower.includes('caramel') ||
		nameLower.includes('cara')
	) {
		return Math.max(4.7, 5.36 - 0.005 * grain.colorSrm);
	}
	if (
		grain.colorSrm >= 6 ||
		nameLower.includes('munich') ||
		nameLower.includes('vienna') ||
		nameLower.includes('biscuit') ||
		nameLower.includes('amber')
	) {
		return Math.max(5.35, 5.65 - 0.01 * grain.colorSrm);
	}
	return Math.max(5.55, 5.74 - 0.012 * grain.colorSrm);
}

export function predictMashPh(
	source: WaterIons,
	mashAdditions: WaterSaltAdditions,
	mashVolumeLiters: number,
	grist: GristGrainSample[]
): number {
	const positiveGrains = grist.filter((g) => g.weightKg > 0);
	const totalGrainKg = positiveGrains.reduce((s, g) => s + g.weightKg, 0);
	const weightedDiPh =
		totalGrainKg > 0
			? positiveGrains.reduce((s, g) => s + estimateGrainDistilledWaterPh(g) * g.weightKg, 0) /
				totalGrainKg
			: 5.7;

	if (mashVolumeLiters <= 0) return Number(weightedDiPh.toFixed(2));
	const inv = 1 / mashVolumeLiters;

	const ca =
		source.calciumPpm +
		Math.max(0, mashAdditions.gypsumGrams) * 232.8 * inv +
		Math.max(0, mashAdditions.calciumChlorideGrams) * 272.6 * inv +
		Math.max(0, mashAdditions.chalkGrams) * 400.4 * inv;
	const mg = source.magnesiumPpm + Math.max(0, mashAdditions.epsomSaltGrams) * 98.6 * inv;
	const rawHco3 =
		source.bicarbonatePpm +
		Math.max(0, mashAdditions.bakingSodaGrams) * 726.3 * inv +
		Math.max(0, mashAdditions.chalkGrams) * 599.6 * inv;

	const acidMeq =
		Math.max(0, mashAdditions.acidAmountMlOrG) * getWaterAcidMeqPerUnit(mashAdditions.acidType);
	const effectiveHco3MeqPerL = rawHco3 / 61.0168 - acidMeq * inv;
	const hardnessOffsetMeqPerL = (ca / 1.403 + mg / 1.701) / 50.0435;
	const netRaMeqTotal = (effectiveHco3MeqPerL - hardnessOffsetMeqPerL) * mashVolumeLiters;
	const buffering = totalGrainKg > 0 ? 45 * totalGrainKg : 180;
	const predicted = Math.min(7.2, Math.max(3.8, weightedDiPh + netRaMeqTotal / buffering));
	return Number(predicted.toFixed(2));
}

export function evaluateWaterPlan(
	source: WaterIons,
	mashAdditions: WaterSaltAdditions,
	mashVolumeLiters: number,
	spargeAdditions: WaterSaltAdditions,
	spargeVolumeLiters: number,
	grist: GristGrainSample[]
): EvaluatedWaterPlan {
	const mashL = Math.max(1, mashVolumeLiters);
	const spargeL = Math.max(0, spargeVolumeLiters);
	const totalL = mashL + spargeL;

	const combined: WaterSaltAdditions = {
		gypsumGrams: mashAdditions.gypsumGrams + spargeAdditions.gypsumGrams,
		calciumChlorideGrams: mashAdditions.calciumChlorideGrams + spargeAdditions.calciumChlorideGrams,
		epsomSaltGrams: mashAdditions.epsomSaltGrams + spargeAdditions.epsomSaltGrams,
		tableSaltGrams: mashAdditions.tableSaltGrams + spargeAdditions.tableSaltGrams,
		bakingSodaGrams: mashAdditions.bakingSodaGrams + spargeAdditions.bakingSodaGrams,
		chalkGrams: mashAdditions.chalkGrams + spargeAdditions.chalkGrams,
		acidType: mashAdditions.acidType,
		acidAmountMlOrG: mashAdditions.acidAmountMlOrG + spargeAdditions.acidAmountMlOrG
	};

	const mashIons = calculateWaterIonsAfterAdditions(source, mashAdditions, mashL);
	const totalIons = calculateWaterIonsAfterAdditions(source, combined, totalL);
	const ratio = calculateSulfateToChlorideRatio(totalIons.sulfatePpm, totalIons.chloridePpm);
	const balance = classifySulfateChlorideBalance(
		ratio,
		totalIons.sulfatePpm,
		totalIons.chloridePpm
	);
	const alkalinity = calculateAlkalinityAsCaCo3(totalIons.bicarbonatePpm);
	const ra = calculateResidualAlkalinity(mashIons);
	const predictedMashPh = predictMashPh(source, mashAdditions, mashL, grist);

	return {
		mashIons,
		totalIons,
		sulfateToChlorideRatio: ratio,
		balanceProfile: balance,
		alkalinityPpm: alkalinity,
		residualAlkalinityPpm: ra,
		predictedMashPh,
		hasChalkWarning: mashAdditions.chalkGrams > 0 || spargeAdditions.chalkGrams > 0
	};
}

export function suggestWaterSaltAdditions(
	source: WaterIons,
	target: WaterIons,
	mashVolumeLiters: number,
	spargeVolumeLiters: number,
	grist: GristGrainSample[],
	targetMashPh = 5.4,
	acidType = 'Lactic80'
): {
	mashAdditions: WaterSaltAdditions;
	spargeAdditions: WaterSaltAdditions;
	summary: EvaluatedWaterPlan;
} {
	const mashL = Math.max(1, mashVolumeLiters);
	const spargeL = Math.max(0, spargeVolumeLiters);
	const totalL = mashL + spargeL;

	const deltaSo4 = Math.max(0, target.sulfatePpm - source.sulfatePpm);
	const deltaCl = Math.max(0, target.chloridePpm - source.chloridePpm);
	const deltaMg = Math.max(0, target.magnesiumPpm - source.magnesiumPpm);

	const totalEpsom = deltaMg > 0 ? (deltaMg * totalL) / 98.6 : 0;
	const so4FromEpsom = (totalEpsom * 389.7) / totalL;
	const totalGypsum = (Math.max(0, deltaSo4 - so4FromEpsom) * totalL) / 557.7;
	const totalCaCl2 = deltaCl > 0 ? (deltaCl * totalL) / 482.3 : 0;

	const mashShare = mashL / totalL;
	const spargeShare = spargeL / totalL;

	const mashAdditions: WaterSaltAdditions = {
		gypsumGrams: Number((totalGypsum * mashShare).toFixed(1)),
		calciumChlorideGrams: Number((totalCaCl2 * mashShare).toFixed(1)),
		epsomSaltGrams: Number((totalEpsom * mashShare).toFixed(1)),
		tableSaltGrams: 0,
		bakingSodaGrams: 0,
		chalkGrams: 0,
		acidType,
		acidAmountMlOrG: 0
	};

	const spargeAdditions: WaterSaltAdditions = {
		gypsumGrams: Number((totalGypsum * spargeShare).toFixed(1)),
		calciumChlorideGrams: Number((totalCaCl2 * spargeShare).toFixed(1)),
		epsomSaltGrams: Number((totalEpsom * spargeShare).toFixed(1)),
		tableSaltGrams: 0,
		bakingSodaGrams: 0,
		chalkGrams: 0,
		acidType,
		acidAmountMlOrG: 0
	};

	const basePh = predictMashPh(source, mashAdditions, mashL, grist);
	const totalGrainKg = grist.filter((g) => g.weightKg > 0).reduce((s, g) => s + g.weightKg, 0);
	const buffering = totalGrainKg > 0 ? 45 * totalGrainKg : 180;
	const meqPerUnit = getWaterAcidMeqPerUnit(acidType);

	if (basePh > targetMashPh + 0.03 && meqPerUnit > 0) {
		mashAdditions.acidAmountMlOrG = Number(
			(((basePh - targetMashPh) * buffering) / meqPerUnit).toFixed(1)
		);
	} else if (basePh < targetMashPh - 0.05) {
		mashAdditions.bakingSodaGrams = Number(
			(((targetMashPh - basePh) * buffering) / 11.9).toFixed(1)
		);
	}

	if (spargeL > 0 && source.bicarbonatePpm > 50 && meqPerUnit > 0) {
		const spargeMeq = ((source.bicarbonatePpm - 25) / 61.0168) * spargeL;
		spargeAdditions.acidAmountMlOrG = Number((spargeMeq / meqPerUnit).toFixed(1));
	}

	return {
		mashAdditions,
		spargeAdditions,
		summary: evaluateWaterPlan(source, mashAdditions, mashL, spargeAdditions, spargeL, grist)
	};
}
