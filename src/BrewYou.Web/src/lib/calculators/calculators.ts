/**
 * Common Craft Brewery Calculators
 * Pure functions covering strike water, ABV & attenuation, hydrometer calibration,
 * refractometer Brix conversion, dilution/boil-off, and carbonation.
 */

// ---------------------------------------------------------------------------
// 1. Strike Water Temperature
// ---------------------------------------------------------------------------

export interface StrikeWaterInput {
	grainWeightKg: number;
	grainTempC: number;
	targetMashTempC: number;
	ratioLitersPerKg: number; // typically 2.5 - 3.5 L/kg
}

export interface StrikeWaterResult {
	strikeTempC: number;
	strikeTempF: number;
	totalWaterLiters: number;
	totalWaterGallons: number;
}

/**
 * Calculates required strike water temperature based on thermodynamic mash equilibrium
 * Palmer: Tw = (0.418 / r) * (Tm - Tg) + Tm
 */
export function calculateStrikeWater(input: StrikeWaterInput): StrikeWaterResult {
	const r = Math.max(0.5, input.ratioLitersPerKg);
	const tg = input.grainTempC;
	const tm = input.targetMashTempC;
	const grainKg = Math.max(0.1, input.grainWeightKg);

	const strikeC = (0.418 / r) * (tm - tg) + tm;
	const strikeF = (strikeC * 9) / 5 + 32;

	const totalL = grainKg * r;
	const totalGal = totalL * 0.264172;

	return {
		strikeTempC: Number(strikeC.toFixed(1)),
		strikeTempF: Number(strikeF.toFixed(1)),
		totalWaterLiters: Number(totalL.toFixed(1)),
		totalWaterGallons: Number(totalGal.toFixed(2))
	};
}

// ---------------------------------------------------------------------------
// 2. Alcohol & Attenuation & Calories
// ---------------------------------------------------------------------------

export interface AbvInput {
	originalGravity: number;
	finalGravity: number;
}

export interface AbvResult {
	abvStandard: number;
	abvAlternate: number; // Hall / Ritchie high gravity formula
	abw: number;
	apparentAttenuation: number;
	realAttenuation: number;
	caloriesPer330ml: number;
	caloriesPer12oz: number;
	originalPlato: number;
	finalPlato: number;
}

export function sgToPlatoASBC(sg: number): number {
	if (sg <= 1.0) return 0;
	return -616.868 + 1111.14 * sg - 630.272 * Math.pow(sg, 2) + 135.997 * Math.pow(sg, 3);
}

export function calculateAbv(input: AbvInput): AbvResult {
	const og = Math.max(1.0, input.originalGravity);
	const fg = Math.max(0.98, input.finalGravity);

	// Standard formula: (OG - FG) * 131.25
	const abvStd = Math.max(0, (og - fg) * 131.25);

	// Alternate high-gravity formula (Hall / Ritchie)
	const abvAlt = og > fg ? ((76.08 * (og - fg)) / (1.775 - og)) * (fg / 0.794) : 0;

	// Alcohol by Weight
	const abw = (abvStd * 0.79336) / fg;

	// Attenuation
	const apparentAtt = og > 1.0 ? ((og - fg) / (og - 1.0)) * 100 : 0;
	const realAtt = apparentAtt * 0.8114;

	// Calories estimation
	// Cal = [(6.9 * ABW) + 4.0 * (RealExtract - 0.1)] * FG * Volume(ml) / 100
	const oe = sgToPlatoASBC(og);
	const ae = sgToPlatoASBC(fg);
	const re = 0.1808 * oe + 0.8192 * ae; // Real extract Plato

	const calPer100ml = (6.9 * abw + 4.0 * Math.max(0, re - 0.1)) * fg;
	const cal330 = Math.max(0, calPer100ml * 3.3);
	const cal12oz = Math.max(0, calPer100ml * 3.55);

	return {
		abvStandard: Number(abvStd.toFixed(2)),
		abvAlternate: Number(abvAlt.toFixed(2)),
		abw: Number(abw.toFixed(2)),
		apparentAttenuation: Number(apparentAtt.toFixed(1)),
		realAttenuation: Number(realAtt.toFixed(1)),
		caloriesPer330ml: Math.round(cal330),
		caloriesPer12oz: Math.round(cal12oz),
		originalPlato: Number(oe.toFixed(1)),
		finalPlato: Number(ae.toFixed(1))
	};
}

// ---------------------------------------------------------------------------
// 3. Hydrometer Temperature Correction
// ---------------------------------------------------------------------------

export interface HydrometerCorrectionInput {
	measuredSg: number;
	sampleTempC: number;
	calibrationTempC: number; // usually 15C, 20C, or 60F (15.55C)
}

export interface HydrometerCorrectionResult {
	correctedSg: number;
	deltaPoints: number;
}

/**
 * ASBC standard hydrometer temperature compensation polynomial
 * Truncated polynomial using Fahrenheit
 */
export function calculateHydrometerCorrection(
	input: HydrometerCorrectionInput
): HydrometerCorrectionResult {
	const sg = input.measuredSg;
	const tempF = (input.sampleTempC * 9) / 5 + 32;
	const calF = (input.calibrationTempC * 9) / 5 + 32;

	const poly = (t: number) =>
		1.00130346 -
		0.000134722124 * t +
		0.00000204052596 * Math.pow(t, 2) -
		0.00000000232820948 * Math.pow(t, 3);

	const corrected = sg * (poly(tempF) / poly(calF));
	const delta = (corrected - sg) * 1000;

	return {
		correctedSg: Number(corrected.toFixed(4)),
		deltaPoints: Number(delta.toFixed(1))
	};
}

// ---------------------------------------------------------------------------
// 4. Refractometer Brix to SG & Alcohol Correction
// ---------------------------------------------------------------------------

export interface RefractometerInput {
	originalBrix: number;
	currentBrix: number;
	wortCorrectionFactor: number; // default 1.00 (typical range 1.00 - 1.04)
}

export interface RefractometerResult {
	correctedFg: number;
	correctedFgPlato: number;
	abv: number;
}

/**
 * Sean Terrill's cubic polynomial fit for post-fermentation refractometer readings
 */
export function calculateRefractometerFg(input: RefractometerInput): RefractometerResult {
	const wcf = Math.max(0.8, input.wortCorrectionFactor);
	const ob = input.originalBrix / wcf;
	const cb = input.currentBrix / wcf;

	if (ob <= 0 || cb <= 0) {
		return { correctedFg: 1.0, correctedFgPlato: 0, abv: 0 };
	}

	// Sean Terrill cubic equation:
	// FG = 1.0000 - 0.00085683*OB + 0.0034941*CB
	// or standard polynomial:
	const fg =
		1.0 -
		0.0044993 * ob +
		0.011774 * cb +
		0.00027581 * Math.pow(ob, 2) -
		0.0012717 * Math.pow(cb, 2) -
		0.00000728 * Math.pow(ob, 3) +
		0.000063293 * Math.pow(cb, 3);

	const boundedFg = Math.max(0.98, fg);
	const ogEstimated = 1.0 + ob / (258.6 - (ob / 258.2) * 227.1);
	const abv = Math.max(0, (ogEstimated - boundedFg) * 131.25);
	const plato = sgToPlatoASBC(boundedFg);

	return {
		correctedFg: Number(boundedFg.toFixed(3)),
		correctedFgPlato: Number(plato.toFixed(1)),
		abv: Number(abv.toFixed(2))
	};
}

// ---------------------------------------------------------------------------
// 5. Dilution & Boil-Off / Gravity Adjustment
// ---------------------------------------------------------------------------

export interface DilutionInput {
	currentVolumeLiters: number;
	currentSg: number;
	targetSg: number;
}

export interface DilutionResult {
	type: 'dilution' | 'boiloff' | 'equal';
	waterToAddLiters: number;
	waterToAddGallons: number;
	volumeToBoilOffLiters: number;
	volumeToBoilOffGallons: number;
	finalVolumeLiters: number;
	finalVolumeGallons: number;
}

export function calculateDilutionBoiloff(input: DilutionInput): DilutionResult {
	const v1 = Math.max(0.1, input.currentVolumeLiters);
	const p1 = Math.max(0, (input.currentSg - 1.0) * 1000);
	const p2 = Math.max(0.1, (input.targetSg - 1.0) * 1000);

	// V1 * P1 = V2 * P2 => V2 = V1 * P1 / P2
	const v2 = (v1 * p1) / p2;

	if (Math.abs(p1 - p2) < 0.1) {
		return {
			type: 'equal',
			waterToAddLiters: 0,
			waterToAddGallons: 0,
			volumeToBoilOffLiters: 0,
			volumeToBoilOffGallons: 0,
			finalVolumeLiters: Number(v1.toFixed(1)),
			finalVolumeGallons: Number((v1 * 0.264172).toFixed(2))
		};
	}

	if (p1 > p2) {
		// Needs dilution with water
		const addL = v2 - v1;
		return {
			type: 'dilution',
			waterToAddLiters: Number(addL.toFixed(2)),
			waterToAddGallons: Number((addL * 0.264172).toFixed(2)),
			volumeToBoilOffLiters: 0,
			volumeToBoilOffGallons: 0,
			finalVolumeLiters: Number(v2.toFixed(1)),
			finalVolumeGallons: Number((v2 * 0.264172).toFixed(2))
		};
	} else {
		// Needs boil-off concentration
		const boilOffL = v1 - v2;
		return {
			type: 'boiloff',
			waterToAddLiters: 0,
			waterToAddGallons: 0,
			volumeToBoilOffLiters: Number(boilOffL.toFixed(2)),
			volumeToBoilOffGallons: Number((boilOffL * 0.264172).toFixed(2)),
			finalVolumeLiters: Number(v2.toFixed(1)),
			finalVolumeGallons: Number((v2 * 0.264172).toFixed(2))
		};
	}
}

// ---------------------------------------------------------------------------
// 6. Priming Sugar & Carbonation
// ---------------------------------------------------------------------------

export interface CarbonationInput {
	beerVolumeLiters: number;
	targetCo2Volumes: number; // e.g. 2.4 - 2.6
	beerTempC: number; // temperature at end of fermentation for dissolved CO2
}

export interface CarbonationResult {
	tableSugarGrams: number; // Sucrose
	cornSugarGrams: number; // Dextrose / Glucose monohydrate
	dmeGrams: number; // Dry Malt Extract
	honeyGrams: number;
	residualCo2Volumes: number;
	kegPsiAt4C: number; // draft dispensing PSI at 4C (39F)
}

/**
 * Calculates bottle priming sugar additions and draft keg regulator PSI
 */
export function calculateCarbonation(input: CarbonationInput): CarbonationResult {
	const volL = Math.max(0.1, input.beerVolumeLiters);
	const volGal = volL * 0.264172;
	const tempF = (input.beerTempC * 9) / 5 + 32;

	// Dissolved residual CO2 based on Henry's law approximation
	// CO2_res = 3.0378 - 0.050062*T + 0.00026555*T^2
	const residualCo2 = Math.max(0.5, 3.0378 - 0.050062 * tempF + 0.00026555 * Math.pow(tempF, 2));

	const netCo2Volumes = Math.max(0, input.targetCo2Volumes - residualCo2);

	// 1 volume of CO2 requires ~4 grams of sucrose per gallon, or ~15.195 grams/gal
	// Formula: grams sucrose = netCo2 * volGal * 15.195
	const sucroseGrams = netCo2Volumes * volGal * 15.195;
	const dextroseGrams = sucroseGrams / 0.91; // Corn sugar is ~91% fermentable sugar by weight
	const dmeGrams = sucroseGrams / 0.68; // DME is ~68% fermentable
	const honeyGrams = sucroseGrams / 0.74; // Honey is ~74% fermentable sugars

	// Keg PSI at 4°C (39.2°F):
	// PSI = -16.6999 - (0.0101059 * T) + (0.00116512 * T^2) + (0.173354 * T * V) + (4.24267 * V) - (0.0684226 * V^2)
	const tkF = 39.2;
	const targetV = input.targetCo2Volumes;
	const psi =
		-16.6999 -
		0.0101059 * tkF +
		0.00116512 * Math.pow(tkF, 2) +
		0.173354 * tkF * targetV +
		4.24267 * targetV -
		0.0684226 * Math.pow(targetV, 2);

	return {
		tableSugarGrams: Math.round(sucroseGrams),
		cornSugarGrams: Math.round(dextroseGrams),
		dmeGrams: Math.round(dmeGrams),
		honeyGrams: Math.round(honeyGrams),
		residualCo2Volumes: Number(residualCo2.toFixed(2)),
		kegPsiAt4C: Number(Math.max(0, psi).toFixed(1))
	};
}
