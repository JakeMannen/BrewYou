import { describe, it, expect } from 'vitest';
import {
	calculateStrikeWater,
	calculateAbv,
	calculateHydrometerCorrection,
	calculateRefractometerFg,
	calculateDilutionBoiloff,
	calculateCarbonation,
	sgToPlatoASBC
} from './calculators';

describe('Common Brewery Calculators', () => {
	describe('Strike Water Calculator', () => {
		it('calculates correct strike water temperature for typical infusion mash', () => {
			// 5 kg malt at 20°C, target 65°C, ratio 3.0 L/kg
			const result = calculateStrikeWater({
				grainWeightKg: 5,
				grainTempC: 20,
				targetMashTempC: 65,
				ratioLitersPerKg: 3.0
			});

			// Tw = (0.418 / 3.0) * (65 - 20) + 65 = 0.13933 * 45 + 65 = 6.27 + 65 = 71.3°C
			expect(result.strikeTempC).toBeCloseTo(71.3, 1);
			expect(result.totalWaterLiters).toBe(15.0);
			expect(result.strikeTempF).toBeCloseTo((71.3 * 9) / 5 + 32, 1);
		});

		it('handles cold grain requiring higher strike temp', () => {
			const result = calculateStrikeWater({
				grainWeightKg: 5,
				grainTempC: 10,
				targetMashTempC: 66,
				ratioLitersPerKg: 2.7
			});

			expect(result.strikeTempC).toBeGreaterThan(74.0);
		});

		it('calculates strike water for higher mash temperatures (e.g. 67°C)', () => {
			const result = calculateStrikeWater({
				grainWeightKg: 5,
				grainTempC: 20,
				targetMashTempC: 67,
				ratioLitersPerKg: 3.0
			});

			// Tw = (0.418 / 3.0) * (67 - 20) + 67 = 0.13933 * 47 + 67 = 6.55 + 67 = 73.5°C
			expect(result.strikeTempC).toBeCloseTo(73.5, 1);
		});

		it('calculates lower strike temperature for lower mash rest (e.g. 63°C)', () => {
			const result = calculateStrikeWater({
				grainWeightKg: 5,
				grainTempC: 20,
				targetMashTempC: 63,
				ratioLitersPerKg: 3.0
			});

			// Tw = (0.418 / 3.0) * (63 - 20) + 63 = 0.13933 * 43 + 63 = 5.99 + 63 = 69.0°C
			expect(result.strikeTempC).toBeCloseTo(69.0, 1);
		});

		it('adjusts strike temp for high-ratio no-sparge / BIAB mashing (e.g. 6.0 L/kg)', () => {
			const traditional = calculateStrikeWater({
				grainWeightKg: 5,
				grainTempC: 20,
				targetMashTempC: 65,
				ratioLitersPerKg: 3.0
			});

			const biab = calculateStrikeWater({
				grainWeightKg: 5,
				grainTempC: 20,
				targetMashTempC: 65,
				ratioLitersPerKg: 6.0
			});

			// High volume BIAB strike water has higher thermal mass, needing less elevation above target mash
			expect(biab.strikeTempC).toBeLessThan(traditional.strikeTempC);
			expect(biab.strikeTempC).toBeGreaterThan(65);
			expect(biab.totalWaterLiters).toBe(30.0);
		});
	});

	describe('ABV & Attenuation Calculator', () => {
		it('calculates ABV, attenuation and calories accurately', () => {
			const res = calculateAbv({
				originalGravity: 1.05,
				finalGravity: 1.01
			});

			// (1.050 - 1.010) * 131.25 = 5.25%
			expect(res.abvStandard).toBe(5.25);
			expect(res.apparentAttenuation).toBe(80.0);
			expect(res.realAttenuation).toBeCloseTo(64.9, 1);
			expect(res.caloriesPer330ml).toBeGreaterThan(120);
			expect(res.caloriesPer330ml).toBeLessThan(180);
			expect(res.originalPlato).toBeCloseTo(12.4, 1);
		});

		it('calculates high gravity ABV correctly', () => {
			const res = calculateAbv({
				originalGravity: 1.095,
				finalGravity: 1.02
			});

			expect(res.abvStandard).toBeCloseTo(9.84, 1);
			expect(res.abvAlternate).toBeGreaterThan(res.abvStandard); // Higher in high gravity
		});
	});

	describe('Hydrometer Temperature Correction', () => {
		it('corrects warm sample to higher gravity', () => {
			// Measured 1.050 at 50°C with hydrometer calibrated at 20°C
			const res = calculateHydrometerCorrection({
				measuredSg: 1.05,
				sampleTempC: 50,
				calibrationTempC: 20
			});

			expect(res.correctedSg).toBeGreaterThan(1.058);
			expect(res.deltaPoints).toBeGreaterThan(8);
		});

		it('leaves SG unchanged when measured at calibration temp', () => {
			const res = calculateHydrometerCorrection({
				measuredSg: 1.05,
				sampleTempC: 20,
				calibrationTempC: 20
			});

			expect(res.correctedSg).toBe(1.05);
			expect(res.deltaPoints).toBeCloseTo(0, 1);
		});
	});

	describe('Refractometer Brix to SG Calculator', () => {
		it('corrects final gravity accounting for alcohol interference', () => {
			// OG 12 Brix (~1.048), FG 6 Brix
			const res = calculateRefractometerFg({
				originalBrix: 12,
				currentBrix: 6,
				wortCorrectionFactor: 1.0
			});

			expect(res.correctedFg).toBeLessThan(1.015);
			expect(res.correctedFg).toBeGreaterThan(1.006);
			expect(res.abv).toBeGreaterThan(4.5);
		});

		it('returns default fallback when brix is zero or negative', () => {
			const res = calculateRefractometerFg({
				originalBrix: 0,
				currentBrix: 5,
				wortCorrectionFactor: 1.0
			});

			expect(res).toEqual({
				correctedFg: 1.0,
				correctedFgPlato: 0,
				abv: 0
			});
		});
	});

	describe('Dilution & Boil-Off Calculator', () => {
		it('calculates water addition when gravity is too high', () => {
			// 20L at 1.060, target 1.050
			const res = calculateDilutionBoiloff({
				currentVolumeLiters: 20,
				currentSg: 1.06,
				targetSg: 1.05
			});

			expect(res.type).toBe('dilution');
			// 20 * 60 / 50 = 24L -> add 4L
			expect(res.waterToAddLiters).toBe(4.0);
			expect(res.finalVolumeLiters).toBe(24.0);
		});

		it('calculates boil-off volume when gravity is too low', () => {
			// 25L at 1.040, target 1.050
			const res = calculateDilutionBoiloff({
				currentVolumeLiters: 25,
				currentSg: 1.04,
				targetSg: 1.05
			});

			expect(res.type).toBe('boiloff');
			// 25 * 40 / 50 = 20L -> boil off 5L
			expect(res.volumeToBoilOffLiters).toBe(5.0);
			expect(res.finalVolumeLiters).toBe(20.0);
		});

		it('handles equal current and target gravity', () => {
			const res = calculateDilutionBoiloff({
				currentVolumeLiters: 20,
				currentSg: 1.05,
				targetSg: 1.05
			});

			expect(res.type).toBe('equal');
			expect(res.waterToAddLiters).toBe(0);
			expect(res.volumeToBoilOffLiters).toBe(0);
			expect(res.finalVolumeLiters).toBe(20.0);
		});
	});

	describe('Priming Sugar & Carbonation Calculator', () => {
		it('calculates priming sugar amounts for 20L batch', () => {
			const res = calculateCarbonation({
				beerVolumeLiters: 20,
				targetCo2Volumes: 2.4,
				beerTempC: 20
			});

			expect(res.tableSugarGrams).toBeGreaterThan(80);
			expect(res.tableSugarGrams).toBeLessThan(150);
			// Dextrose requires more grams than sucrose
			expect(res.cornSugarGrams).toBeGreaterThan(res.tableSugarGrams);
			// DME requires even more
			expect(res.dmeGrams).toBeGreaterThan(res.cornSugarGrams);
			expect(res.kegPsiAt4C).toBeGreaterThan(8);
		});
	});

	describe('sgToPlatoASBC', () => {
		it('converts 1.000 to 0 Plato and 1.040 to ~10 Plato', () => {
			expect(sgToPlatoASBC(1.0)).toBe(0);
			expect(sgToPlatoASBC(1.04)).toBeCloseTo(10.0, 1);
		});
	});
});
