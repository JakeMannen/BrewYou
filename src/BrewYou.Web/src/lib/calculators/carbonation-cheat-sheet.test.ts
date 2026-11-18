import { describe, it, expect } from 'vitest';
import {
	CARBONATION_STYLE_GUIDE,
	DRAFT_TROUBLESHOOTING_GUIDE,
	DRAFT_TUBING_SPECS,
	FORCE_CARB_METHODS,
	FORCE_CARB_TEMPS_C,
	FORCE_CARB_VOLUMES,
	PRIMING_SUGAR_GUIDE,
	barToPsi,
	calculateCo2VolumesFromPsi,
	calculateDraftLineLength,
	calculateKegEquilibriumPsi,
	calculateResidualCo2,
	getBottleSafetyLevel,
	getCarbonationZone,
	psiToBar
} from './carbonation-cheat-sheet';

describe('Carbonation Cheat Sheet Science & Catalog', () => {
	describe('ASBC / Zahm & Nagel Equilibrium Pressure', () => {
		it('matches verified ASBC equilibrium PSI and bar values across temperature and CO2 targets', () => {
			expect(calculateKegEquilibriumPsi(0, 1.8)).toBeCloseTo(1.6, 1);
			expect(calculateKegEquilibriumPsi(4, 2.4)).toBeCloseTo(10.8, 1);
			expect(psiToBar(10.8)).toBeCloseTo(0.74, 2);
			expect(calculateKegEquilibriumPsi(12, 3.5)).toBeCloseTo(32.6, 1);
			expect(psiToBar(32.6)).toBeCloseTo(2.25, 2);
		});

		it('clamps sub-atmospheric vacuum pressures to 0 PSI and converts bar <-> PSI consistently', () => {
			expect(calculateKegEquilibriumPsi(0, 1.0)).toBe(0);
			expect(calculateKegEquilibriumPsi(4, 0)).toBe(0);
			expect(calculateKegEquilibriumPsi(Number.NaN, 2.4)).toBe(0);
			expect(barToPsi(1.0)).toBeCloseTo(14.5, 1);
			expect(psiToBar(-5)).toBe(0);
		});

		it('round-trips equilibrium PSI back to target CO2 volumes via inverse quadratic equation', () => {
			for (const tempC of FORCE_CARB_TEMPS_C) {
				for (const vol of FORCE_CARB_VOLUMES) {
					const psi = calculateKegEquilibriumPsi(tempC, vol);
					const resolvedVol = calculateCo2VolumesFromPsi(tempC, psi);
					expect(resolvedVol).toBeCloseTo(vol, 1);
				}
			}
		});
	});

	describe('Residual CO2 Solubility (Henrys Law)', () => {
		it('matches expected dissolved CO2 at 0C, 10C, and 20C', () => {
			expect(calculateResidualCo2(0)).toBeCloseTo(1.71, 2);
			expect(calculateResidualCo2(10)).toBeCloseTo(1.2, 2);
			expect(calculateResidualCo2(20)).toBeCloseTo(0.86, 2);
		});

		it('prevents non-physical upward parabolic curl above 34.6C (e.g. Kveik fermentations)', () => {
			const atPeakMin = calculateResidualCo2(34.6);
			const atHotKveik = calculateResidualCo2(40);
			expect(atHotKveik).toBeLessThanOrEqual(atPeakMin);
			expect(atHotKveik).toBeGreaterThanOrEqual(0.5);
		});
	});

	describe('Zones & Bottle Safety Classification', () => {
		it('classifies carbonation zones accurately', () => {
			expect(getCarbonationZone(1.5)).toBe('low');
			expect(getCarbonationZone(2.4)).toBe('standard');
			expect(getCarbonationZone(2.8)).toBe('high');
			expect(getCarbonationZone(3.4)).toBe('effervescent');
		});

		it('flags caution and heavy-glass requirements at bottle safety thresholds', () => {
			expect(getBottleSafetyLevel(2.5)).toBe('standard');
			expect(getBottleSafetyLevel(3.0)).toBe('standard');
			expect(getBottleSafetyLevel(3.1)).toBe('caution');
			expect(getBottleSafetyLevel(3.2)).toBe('caution');
			expect(getBottleSafetyLevel(3.5)).toBe('heavy_glass_required');
		});
	});

	describe('Draft Line Balancing Calculator', () => {
		it('computes balanced line length accounting for static lift and faucet residual pressure', () => {
			// 12 PSI keg, 2 ft rise (-1 PSI), 1.5 PSI faucet residual => 9.5 PSI / 2.2 PSI/ft = 4.3 ft
			const res4mm = calculateDraftLineLength({
				kegPsi: 12,
				verticalRiseFeet: 2,
				residualFaucetPsi: 1.5,
				resistancePsiPerFoot: 2.2
			});
			expect(res4mm.effectivePressurePsi).toBe(9.5);
			expect(res4mm.lengthFeet).toBeCloseTo(4.3, 1);
			expect(res4mm.lengthMeters).toBeCloseTo(1.32, 2);

			// 5 mm EVAbarrier requires significantly longer line for same PSI
			const res5mm = calculateDraftLineLength({
				kegPsi: 12,
				verticalRiseFeet: 2,
				residualFaucetPsi: 1.5,
				resistancePsiPerFoot: 1.0
			});
			expect(res5mm.lengthFeet).toBeGreaterThan(res4mm.lengthFeet * 2);
		});

		it('handles zero or sub-head pressures without negative line lengths', () => {
			const res = calculateDraftLineLength({
				kegPsi: 1,
				verticalRiseFeet: 4,
				residualFaucetPsi: 1.5,
				resistancePsiPerFoot: 2.2
			});
			expect(res.effectivePressurePsi).toBe(0);
			expect(res.lengthFeet).toBe(0);
			expect(res.lengthMeters).toBe(0);
		});
	});

	describe('Reference Catalog Integrity', () => {
		it('ensures all 18 BJCP style carbonation entries have valid bounds and unique IDs', () => {
			expect(CARBONATION_STYLE_GUIDE.length).toBe(18);
			const ids = new Set<string>();
			for (const style of CARBONATION_STYLE_GUIDE) {
				expect(ids.has(style.id)).toBe(false);
				ids.add(style.id);
				expect(style.minVol).toBeGreaterThan(0.5);
				expect(style.maxVol).toBeGreaterThanOrEqual(style.minVol);
				expect(style.defaultVol).toBeGreaterThanOrEqual(style.minVol);
				expect(style.defaultVol).toBeLessThanOrEqual(style.maxVol);
			}
		});

		it('ensures priming sugar multipliers, force carb methods, tubing specs, and troubleshooting entries are valid', () => {
			expect(PRIMING_SUGAR_GUIDE.length).toBeGreaterThanOrEqual(5);
			for (const agent of PRIMING_SUGAR_GUIDE) {
				expect(agent.yieldFactor).toBeGreaterThan(0);
				expect(agent.yieldFactor).toBeLessThanOrEqual(1);
				expect(agent.multiplierVsSucrose).toBeGreaterThanOrEqual(1);
				expect(agent.gramsPerLiterPerVol).toBeGreaterThan(3.5);
			}

			expect(FORCE_CARB_METHODS.length).toBe(5);
			expect(DRAFT_TUBING_SPECS.length).toBe(4);
			expect(DRAFT_TROUBLESHOOTING_GUIDE.length).toBe(6);
		});
	});
});
