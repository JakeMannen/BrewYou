import type { CalculateRecipeResponse, IngredientDto, IngredientUsage } from '$lib/types/api';
import type { BitternessFormula, ColorFormula, AbvFormula } from '$lib/stores/settings.svelte';

const KG_TO_LBS = 2.20462262;
const LITERS_TO_GALLONS = 0.264172052;

export interface CalculatorItem {
	ingredient: IngredientDto;
	amount: number; // kg for grains, grams for hops/yeast
	durationMinutes?: number | null;
	usage: IngredientUsage;
}

export interface BrewingCalculationOptions {
	bitternessFormula?: BitternessFormula;
	colorFormula?: ColorFormula;
	abvFormula?: AbvFormula;
}

export function calculateBrewMetrics(
	batchSizeLiters: number,
	efficiencyPercent: number,
	boilTimeMinutes: number,
	items: CalculatorItem[],
	options: BrewingCalculationOptions = {}
): CalculateRecipeResponse {
	const volumeGallons = Math.max(0.1, batchSizeLiters * LITERS_TO_GALLONS);
	const eff = Math.max(0.1, efficiencyPercent / 100);

	let totalGravityPoints = 0;
	let totalMcu = 0;
	let yeastAttenuation: number | null = null;

	for (const item of items) {
		if (!item.ingredient) continue;

		if (item.ingredient.type === 'Fermentable') {
			const weightLbs = item.amount * KG_TO_LBS;
			const ppg = item.ingredient.potentialGravity
				? (item.ingredient.potentialGravity - 1.0) * 1000
				: 37;

			totalGravityPoints += (weightLbs * ppg * eff) / volumeGallons;

			const color = item.ingredient.colorSrm ?? 2.0;
			totalMcu += (weightLbs * color) / volumeGallons;
		} else if (item.ingredient.type === 'Yeast') {
			if (item.ingredient.attenuationPercent) {
				yeastAttenuation = item.ingredient.attenuationPercent;
			}
		}
	}

	const ogPoints = Math.max(0, totalGravityPoints);
	const og = 1.0 + ogPoints / 1000.0;

	const attenuation = (yeastAttenuation ?? 75.0) / 100.0;
	const fg = 1.0 + (og - 1.0) * (1.0 - attenuation);

	// ABV calculation
	const abvFormula = options.abvFormula ?? 'Linear';
	const abv = calculateActualAbv(og, fg, abvFormula);

	// Bitterness
	const bitternessFormula = options.bitternessFormula ?? 'Tinseth';
	let totalIbu = 0;
	for (const item of items) {
		if (item.ingredient?.type === 'Hop' && (item.usage === 'Boil' || item.usage === 'Mash')) {
			const boilMins = item.durationMinutes ?? boilTimeMinutes;
			const alpha = item.ingredient.alphaAcidPercent ?? 5.0;
			totalIbu += calculateSingleHopIbu(
				item.amount,
				alpha,
				boilMins,
				og,
				batchSizeLiters,
				bitternessFormula
			);
		}
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
 * Calculates single hop IBU contribution based on the chosen formula
 */
export function calculateSingleHopIbu(
	weightGrams: number,
	alphaAcidPercent: number,
	boilTimeMinutes: number,
	og: number,
	batchSizeLiters: number,
	formula: BitternessFormula = 'Tinseth'
): number {
	if (formula === 'Rager') {
		return ragerIbu(weightGrams, alphaAcidPercent, boilTimeMinutes, og, batchSizeLiters);
	}
	if (formula === 'Daniels') {
		return danielsIbu(weightGrams, alphaAcidPercent, boilTimeMinutes, og, batchSizeLiters);
	}
	return tinsethIbu(weightGrams, alphaAcidPercent, boilTimeMinutes, og, batchSizeLiters);
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
 * Calculates Rager IBU contribution for a single hop addition
 */
export function ragerIbu(
	weightGrams: number,
	alphaAcidPercent: number,
	boilTimeMinutes: number,
	og: number,
	batchSizeLiters: number
): number {
	if (weightGrams <= 0 || alphaAcidPercent <= 0 || batchSizeLiters <= 0) return 0;
	const gravityAdjustment = og > 1.05 ? (og - 1.05) / 0.2 : 0;
	const time = Math.max(0, boilTimeMinutes);
	const utilization = (0.044 + 0.0006 * time) / (1.0 + gravityAdjustment);
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
