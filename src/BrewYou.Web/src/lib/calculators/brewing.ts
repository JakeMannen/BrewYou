import type { CalculateRecipeResponse, IngredientDto, IngredientUsage } from '$lib/types/api';
import type { BitternessFormula, ColorFormula, AbvFormula } from '$lib/stores/settings.svelte';

const KG_TO_LBS = 2.20462262;
const LITERS_TO_GALLONS = 0.264172052;
const LB_TO_KG = 0.45359237;
const OZ_TO_KG = 0.028349523125;
const OZ_TO_GRAMS = 28.349523125;
const LB_TO_GRAMS = 453.59237;
const REFERENCE_MASH_TEMP_C = 67.0;
const MASH_TEMP_SLOPE_PER_DEGREE_C = 2.5;
const MAX_MASH_ATTENUATION_DELTA = 8.0;

const SOLUBLE_SUGAR_KEYWORDS = [
	'dme',
	'dry malt extract',
	'dried malt extract',
	'lme',
	'liquid malt extract',
	'malt extract',
	'extract',
	'sugar',
	'sucrose',
	'dextrose',
	'glucose',
	'fructose',
	'candi',
	'honey',
	'lactose',
	'maltodextrin',
	'molasses',
	'treacle',
	'maple syrup',
	'agave'
];

const UNFERMENTABLE_SUGAR_KEYWORDS = ['lactose', 'milk sugar', 'maltodextrin'];

export interface CalculatorItem {
	ingredient: IngredientDto;
	amount: number; // canonical metric: kg for grains, grams for hops/yeast (or converted via unit)
	unit?: string;
	durationMinutes?: number | null;
	usage: IngredientUsage;
	form?: string | null;
}

export interface MashStepTempInput {
	temperatureC: number;
	durationMinutes: number;
	stepOrder?: number;
}

export interface BrewingCalculationOptions {
	bitternessFormula?: BitternessFormula;
	colorFormula?: ColorFormula;
	abvFormula?: AbvFormula;
	mashSteps?: MashStepTempInput[] | null;
}

export function normalizeFermentableToKg(amount: number, unit?: string | null): number {
	if (amount <= 0) return 0;
	if (!unit) return amount;
	const u = unit.trim().toLowerCase();
	if (u === 'kg' || u === 'kilogram' || u === 'kilograms') return amount;
	if (u === 'g' || u === 'gram' || u === 'grams') return amount / 1000;
	if (u === 'lb' || u === 'lbs' || u === 'pound' || u === 'pounds') return amount * LB_TO_KG;
	if (u === 'oz' || u === 'ounce' || u === 'ounces') return amount * OZ_TO_KG;
	return amount;
}

export function normalizeHopToGrams(amount: number, unit?: string | null): number {
	if (amount <= 0) return 0;
	if (!unit) return amount;
	const u = unit.trim().toLowerCase();
	if (u === 'g' || u === 'gram' || u === 'grams') return amount;
	if (u === 'kg' || u === 'kilogram' || u === 'kilograms') return amount * 1000;
	if (u === 'oz' || u === 'ounce' || u === 'ounces') return amount * OZ_TO_GRAMS;
	if (u === 'lb' || u === 'lbs' || u === 'pound' || u === 'pounds') return amount * LB_TO_GRAMS;
	return amount;
}

export function isSolubleSugarOrExtract(
	name: string | null | undefined,
	usage?: IngredientUsage
): boolean {
	if (
		usage === 'Whirlpool' ||
		usage === 'Primary' ||
		usage === 'Secondary' ||
		usage === 'Bottling'
	) {
		return true;
	}
	if (!name) return false;
	const lower = name.trim().toLowerCase();
	return SOLUBLE_SUGAR_KEYWORDS.some((kw) => lower.includes(kw));
}

export function isUnfermentableSugar(name: string | null | undefined): boolean {
	if (!name) return false;
	const lower = name.trim().toLowerCase();
	return UNFERMENTABLE_SUGAR_KEYWORDS.some((kw) => lower.includes(kw));
}

/**
 * Adjusts the yeast's laboratory base attenuation (rated in a standard 67.0 °C / 152.6 °F congress/single-infusion wort)
 * for the recipe's active enzymatic saccharification rest temperature (60.0 °C – 72.5 °C).
 * Beta-amylase rests below 67.0 °C (such as a 64.5 °C Hochkurz maltose rest) increase fermentability by +2.5% per °C,
 * while alpha-amylase rests above 67.0 °C increase unfermentable limit dextrins by -2.5% per °C (clamped to ±8.0%).
 */
export function calculateMashAdjustedAttenuationPercent(
	baseAttenuationPercent: number,
	mashSteps?: MashStepTempInput[] | null
): number {
	const clampAttenuation = (val: number) => Math.min(98.0, Math.max(30.0, val));
	if (!mashSteps || mashSteps.length === 0) {
		return clampAttenuation(baseAttenuationPercent);
	}

	const sacchSteps = mashSteps
		.filter((s) => s.durationMinutes > 0 && s.temperatureC >= 60.0 && s.temperatureC <= 72.5)
		.slice()
		.sort((a, b) => (a.stepOrder ?? 1) - (b.stepOrder ?? 1));

	if (sacchSteps.length === 0) {
		return clampAttenuation(baseAttenuationPercent);
	}

	const primaryRest = sacchSteps.find((s) => s.durationMinutes >= 15);
	let primaryTempC: number;
	if (primaryRest) {
		primaryTempC = primaryRest.temperatureC;
	} else {
		const totalDuration = sacchSteps.reduce((acc, s) => acc + s.durationMinutes, 0);
		primaryTempC =
			totalDuration > 0
				? sacchSteps.reduce((acc, s) => acc + s.temperatureC * s.durationMinutes, 0) / totalDuration
				: sacchSteps[0].temperatureC;
	}

	const rawDelta = (REFERENCE_MASH_TEMP_C - primaryTempC) * MASH_TEMP_SLOPE_PER_DEGREE_C;
	const delta = Math.min(
		MAX_MASH_ATTENUATION_DELTA,
		Math.max(-MAX_MASH_ATTENUATION_DELTA, rawDelta)
	);
	return clampAttenuation(baseAttenuationPercent + delta);
}

export function getHopFormMultiplier(form?: string | null): number {
	if (!form) return 1.0;
	const lower = form.trim().toLowerCase();
	if (lower.includes('leaf') || lower.includes('whole')) return 0.9;
	if (lower.includes('plug')) return 0.92;
	return 1.0;
}

export function calculateBrewMetrics(
	batchSizeLiters: number,
	efficiencyPercent: number,
	boilTimeMinutes: number,
	items: CalculatorItem[],
	options: BrewingCalculationOptions = {}
): CalculateRecipeResponse {
	const safeBatchLiters = batchSizeLiters > 0 ? Math.max(0.1, batchSizeLiters) : 18.9270589;
	const volumeGallons =
		batchSizeLiters > 0 ? Math.max(0.1, batchSizeLiters * LITERS_TO_GALLONS) : 5.0;
	const eff = efficiencyPercent > 0 ? Math.max(0.01, efficiencyPercent / 100) : 0.72;

	let totalGravityPoints = 0;
	let unfermentablePoints = 0;
	let totalMcu = 0;
	let maxYeastAttenuation: number | null = null;

	for (const item of items) {
		if (!item.ingredient) continue;

		if (item.ingredient.type === 'Fermentable') {
			const weightKg = normalizeFermentableToKg(item.amount, item.unit);
			const weightLbs = weightKg * KG_TO_LBS;
			const ppg = item.ingredient.potentialGravity
				? (item.ingredient.potentialGravity - 1.0) * 1000
				: 37;

			const effectiveEff = isSolubleSugarOrExtract(item.ingredient.name, item.usage) ? 1.0 : eff;
			const points = (weightLbs * ppg * effectiveEff) / volumeGallons;
			totalGravityPoints += points;

			if (isUnfermentableSugar(item.ingredient.name)) {
				unfermentablePoints += points;
			}

			const color = item.ingredient.colorSrm ?? 2.0;
			totalMcu += (weightLbs * color) / volumeGallons;
		} else if (item.ingredient.type === 'Yeast') {
			if (
				item.ingredient.attenuationPercent !== undefined &&
				item.ingredient.attenuationPercent !== null &&
				item.ingredient.attenuationPercent > 0
			) {
				maxYeastAttenuation =
					maxYeastAttenuation !== null
						? Math.max(maxYeastAttenuation, item.ingredient.attenuationPercent)
						: item.ingredient.attenuationPercent;
			}
		}
	}

	const ogPoints = Math.max(0, totalGravityPoints);
	const og = 1.0 + ogPoints / 1000.0;

	const baseAttenuationPercent = maxYeastAttenuation ?? 75.0;
	const effectiveAttenuationPercent = calculateMashAdjustedAttenuationPercent(
		baseAttenuationPercent,
		options.mashSteps
	);
	const attenuation = effectiveAttenuationPercent / 100.0;
	const fermentablePoints = Math.max(0, ogPoints - unfermentablePoints);
	const fgPoints = fermentablePoints * (1.0 - attenuation) + unfermentablePoints;
	const fg = 1.0 + fgPoints / 1000.0;

	// ABV calculation
	const abvFormula = options.abvFormula ?? 'Linear';
	const abv = calculateActualAbv(og, fg, abvFormula);

	// Bitterness
	const bitternessFormula = options.bitternessFormula ?? 'Tinseth';
	let totalIbu = 0;
	for (const item of items) {
		if (item.ingredient?.type !== 'Hop') continue;

		const weightGrams = normalizeHopToGrams(item.amount, item.unit);
		const alpha = item.ingredient.alphaAcidPercent ?? 5.0;
		const hopForm = item.form ?? item.ingredient.form;

		let hopMinutes: number;
		if (item.usage === 'FirstWort') {
			hopMinutes =
				item.durationMinutes !== undefined &&
				item.durationMinutes !== null &&
				item.durationMinutes > 0
					? item.durationMinutes
					: boilTimeMinutes;
		} else if (item.usage === 'Whirlpool') {
			hopMinutes = item.durationMinutes ?? 0;
		} else {
			hopMinutes = item.durationMinutes ?? boilTimeMinutes;
		}

		totalIbu += calculateSingleHopIbu(
			weightGrams,
			alpha,
			hopMinutes,
			og,
			safeBatchLiters,
			bitternessFormula,
			item.usage,
			hopForm
		);
	}

	// Color (SRM)
	const colorFormula = options.colorFormula ?? 'Morey';
	const srm = calculateSrm(totalMcu, colorFormula);

	return {
		originalGravity: Number(og.toFixed(3)),
		finalGravity: Number(fg.toFixed(3)),
		alcoholByVolume: Number(abv.toFixed(2)),
		bitternessIbu: Number(totalIbu.toFixed(1)),
		colorSrm: Number(srm.toFixed(1))
	};
}

/**
 * Calculates Bitterness-to-Gravity Ratio (BU:GU = IBU / ((OG - 1.0) * 1000)).
 */
export function calculateBuGuRatio(og: number, ibu: number): number {
	if (!og || og <= 1.001 || !ibu || ibu <= 0) return 0;
	const gravityUnits = (og - 1.0) * 1000;
	return Math.round((ibu / gravityUnits) * 100) / 100;
}

/**
 * Calculates Apparent Attenuation percentage (AA% = ((OG - FG) / (OG - 1.0)) * 100).
 */
export function calculateApparentAttenuation(og: number, fg: number): number {
	if (!og || og <= 1.0 || !fg || og <= fg) return 0;
	return Math.round(((og - fg) / (og - 1.0)) * 1000) / 10;
}

/**
 * Calculates ABV using Linear or ASBC Advanced non-linear formula
 */
export function calculateActualAbv(og: number, fg: number, formula: AbvFormula = 'Linear'): number {
	if (og <= fg || og <= 1.0) return 0;
	if (formula === 'Advanced') {
		const abw = (76.08 * (og - fg)) / (1.775 - og);
		const abv = abw * (fg / 0.794);
		return Math.max(0, abv);
	}
	return Math.max(0, (og - fg) * 131.25);
}

/**
 * Calculates single hop IBU contribution based on the chosen formula, usage stage, and physical form
 */
export function calculateSingleHopIbu(
	weightGrams: number,
	alphaAcidPercent: number,
	boilTimeMinutes: number,
	og: number,
	batchSizeLiters: number,
	formula: BitternessFormula = 'Tinseth',
	usage: IngredientUsage = 'Boil',
	form?: string | null
): number {
	if (weightGrams <= 0 || alphaAcidPercent <= 0 || batchSizeLiters <= 0) return 0;

	let usageMultiplier: number;
	const effectiveMinutes = Math.max(0, boilTimeMinutes);

	switch (usage) {
		case 'Boil':
			usageMultiplier = 1.0;
			break;
		case 'FirstWort':
			usageMultiplier = 1.1;
			break;
		case 'Mash':
			usageMultiplier = 0.3;
			break;
		case 'Whirlpool':
			usageMultiplier = effectiveMinutes > 0 ? 0.2 : 0;
			break;
		default:
			return 0;
	}

	if (usageMultiplier <= 0) return 0;

	const formMultiplier = getHopFormMultiplier(form);

	let baseIbu: number;
	if (formula === 'Rager') {
		baseIbu = ragerIbu(weightGrams, alphaAcidPercent, effectiveMinutes, og, batchSizeLiters);
	} else if (formula === 'Daniels') {
		baseIbu = danielsIbu(weightGrams, alphaAcidPercent, effectiveMinutes, og, batchSizeLiters);
	} else {
		baseIbu = tinsethIbu(weightGrams, alphaAcidPercent, effectiveMinutes, og, batchSizeLiters);
	}

	return baseIbu * usageMultiplier * formMultiplier;
}

/**
 * Calculates color in SRM from total MCU based on the chosen model
 */
export function calculateSrm(totalMcu: number, formula: ColorFormula = 'Morey'): number {
	if (totalMcu <= 0) return 0;
	if (formula === 'Mosher') {
		return Math.max(0, 0.3 * totalMcu + 4.7);
	}
	if (formula === 'Daniels') {
		return totalMcu > 11 ? 0.2 * totalMcu + 8.4 : totalMcu * 0.95;
	}
	return 1.4922 * Math.pow(totalMcu, 0.6859);
}

/**
 * Returns a CSS hex color code representing the beer color for a given SRM value
 */
export function srmToHexColor(srm: number): string {
	if (srm <= 1) return '#f8f5b8';
	if (srm <= 2) return '#f6f1a8';
	if (srm <= 3) return '#ece67a';
	if (srm <= 4) return '#e5d34e';
	if (srm <= 6) return '#d4bc2b';
	if (srm <= 8) return '#bf9224';
	if (srm <= 10) return '#b0761a';
	if (srm <= 13) return '#975412';
	if (srm <= 17) return '#7a370b';
	if (srm <= 20) return '#5e2307';
	if (srm <= 24) return '#481905';
	if (srm <= 29) return '#351204';
	if (srm <= 35) return '#240c03';
	return '#120501';
}

/**
 * Calculates Tinseth IBU contribution for a single hop addition
 */
export function tinsethIbu(
	weightGrams: number,
	alphaAcidPercent: number,
	boilTimeMinutes: number,
	og: number,
	batchSizeLiters: number
): number {
	if (weightGrams <= 0 || alphaAcidPercent <= 0 || batchSizeLiters <= 0) return 0;
	const bivalence = 1.65 * Math.pow(0.000125, Math.max(1.0, og) - 1.0);
	const timeFactor = (1.0 - Math.exp(-0.04 * Math.max(0, boilTimeMinutes))) / 4.15;
	const utilization = bivalence * timeFactor;
	const alpha = alphaAcidPercent / 100.0;
	const alphaMgL = (weightGrams * alpha * 1000.0) / Math.max(0.1, batchSizeLiters);
	return utilization * alphaMgL;
}

/**
 * Calculates Rager (1990) hyperbolic-tangent IBU contribution for a single hop addition
 */
export function ragerIbu(
	weightGrams: number,
	alphaAcidPercent: number,
	boilTimeMinutes: number,
	og: number,
	batchSizeLiters: number
): number {
	if (weightGrams <= 0 || alphaAcidPercent <= 0 || batchSizeLiters <= 0) return 0;
	const time = Math.max(0, boilTimeMinutes);
	if (time <= 0) return 0;
	const gravityAdjustment = og > 1.05 ? (og - 1.05) / 0.2 : 0;
	const baseUtilization = (18.11 + 13.86 * Math.tanh((time - 31.32) / 18.27)) / 100.0;
	const utilization = baseUtilization / (1.0 + gravityAdjustment);
	const alpha = alphaAcidPercent / 100.0;
	const alphaMgL = (weightGrams * alpha * 1000.0) / Math.max(0.1, batchSizeLiters);
	return utilization * alphaMgL;
}

/**
 * Calculates Daniels IBU contribution for a single hop addition
 */
export function danielsIbu(
	weightGrams: number,
	alphaAcidPercent: number,
	boilTimeMinutes: number,
	og: number,
	batchSizeLiters: number
): number {
	if (weightGrams <= 0 || alphaAcidPercent <= 0 || batchSizeLiters <= 0) return 0;
	const time = Math.max(0, boilTimeMinutes);
	let baseUtil = 0.05;
	if (time >= 60) baseUtil = 0.25;
	else if (time >= 45) baseUtil = 0.22;
	else if (time >= 30) baseUtil = 0.17;
	else if (time >= 15) baseUtil = 0.12;
	else if (time >= 5) baseUtil = 0.05;

	const gravityAdjustment = og > 1.05 ? (og - 1.05) / 0.2 : 0;
	const utilization = baseUtil / (1.0 + gravityAdjustment);
	const alpha = alphaAcidPercent / 100.0;
	const alphaMgL = (weightGrams * alpha * 1000.0) / Math.max(0.1, batchSizeLiters);
	return utilization * alphaMgL;
}

/**
 * Converts Specific Gravity (SG, e.g. 1.038) to Points per Pound per Gallon (PPG, e.g. 38).
 */
export function sgToPpg(sg: number | null | undefined): number {
	if (sg === null || sg === undefined || isNaN(sg)) return 37;
	return Math.round((sg - 1.0) * 1000);
}

/**
 * Converts Points per Pound per Gallon (PPG, e.g. 38) to Specific Gravity (SG, e.g. 1.038).
 */
export function ppgToSg(ppg: number | null | undefined): number {
	if (ppg === null || ppg === undefined || isNaN(ppg)) return 1.037;
	return Math.round((1.0 + ppg / 1000.0) * 1000) / 1000;
}

/**
 * Smartly interprets and normalizes a fermentable potential value to Specific Gravity (SG).
 * If the value is in the PPG range (10 - 150), it is converted to SG (e.g. 38 -> 1.038).
 * If it is already in the SG range (1.000 - 1.200), it is rounded to 3 decimal places.
 */
export function smartPotentialToSg(value: number | null | undefined): number | undefined {
	if (value === null || value === undefined || isNaN(value)) return undefined;
	if (value >= 10 && value <= 150) {
		return ppgToSg(value);
	}
	if (value >= 1.0 && value <= 1.2) {
		return Math.round(value * 1000) / 1000;
	}
	return Math.round(value * 1000) / 1000;
}

/**
 * Formats fermentable potential displaying both SG and PPG.
 * E.g., "1.038 (38 PPG)"
 */
export function formatPotentialGravity(sg: number | null | undefined): string {
	const val = sg ?? 1.037;
	const ppg = sgToPpg(val);
	return `${val.toFixed(3)} (${ppg} PPG)`;
}

/**
 * Reconciles imported recipe efficiency against an explicit target Original Gravity (OG).
 * Bridges BeerXML/BeerJSON exports where <EFFICIENCY> represents brewhouse efficiency
 * (post-trub loss) while <OG>/<EST_OG> was calculated from mash/kettle efficiency.
 */
export function reconcileImportedEfficiency(
	batchSizeLiters: number,
	statedEfficiencyPercent: number,
	targetOg: number | null | undefined,
	fermentables: Array<{ amount: number; potentialGravity?: number | null }>
): number {
	if (
		!targetOg ||
		isNaN(targetOg) ||
		targetOg <= 1.001 ||
		targetOg > 1.25 ||
		batchSizeLiters <= 0 ||
		fermentables.length === 0
	) {
		return statedEfficiencyPercent;
	}

	const volumeGallons = batchSizeLiters * LITERS_TO_GALLONS;
	let maxGravityPoints = 0;

	for (const f of fermentables) {
		if (!f || f.amount <= 0) continue;
		const weightLbs = f.amount * KG_TO_LBS;
		const ppg = f.potentialGravity ? (f.potentialGravity - 1.0) * 1000 : 37;
		maxGravityPoints += (weightLbs * ppg) / volumeGallons;
	}

	if (maxGravityPoints <= 0) {
		return statedEfficiencyPercent;
	}

	const calculatedOg = 1.0 + (maxGravityPoints * (statedEfficiencyPercent / 100.0)) / 1000.0;
	if (Math.round(calculatedOg * 1000) === Math.round(targetOg * 1000)) {
		return statedEfficiencyPercent;
	}

	const targetPoints = (targetOg - 1.0) * 1000.0;
	const derivedEfficiency = (targetPoints / maxGravityPoints) * 100.0;

	if (derivedEfficiency >= 25.0 && derivedEfficiency <= 100.0) {
		return Math.round(derivedEfficiency * 10) / 10;
	}

	return statedEfficiencyPercent;
}
