import { describe, it, expect } from 'vitest';
import {
	calculateWaterAcidification,
	calculateWaterAlkalinization,
	calculateAcidMeqPerLiter,
	ACID_CATALOG,
	BASE_CATALOG
} from './water-chemistry';

describe('Water Chemistry & pH Adjustment Calculator', () => {
	it('calculates acid mEq needed for typical brewing water titration', () => {
		// Water with 100 ppm CaCO3 starting at pH 7.8, target 5.5
		const meq = calculateAcidMeqPerLiter(7.8, 5.5, 100);
		expect(meq).toBeGreaterThan(1.5);
		expect(meq).toBeLessThan(2.0);
	});

	it('returns 0 when starting pH <= target pH', () => {
		expect(calculateAcidMeqPerLiter(5.4, 5.5, 100)).toBe(0);
		expect(calculateAcidMeqPerLiter(5.4, 5.4, 100)).toBe(0);
	});

	it('calculates 80% lactic acid addition accurately for 20 liters', () => {
		const result = calculateWaterAcidification(20, 7.5, 5.5, 80, 'lactic_80');
		expect(result.mode).toBe('acid');
		expect(result.unit).toBe('mL');
		expect(result.requiredAmount).toBeGreaterThan(1.0);
		expect(result.requiredAmount).toBeLessThan(5.0);
		expect(result.amountPerLiter).toBeCloseTo(result.requiredAmount / 20, 2);
		expect(result.exceedsFlavorThreshold).toBe(false);
	});

	it('flags flavor threshold when lactic acid dosage is excessive', () => {
		// Extremely hard, high alkalinity water (300 ppm) dropped to 5.0
		const result = calculateWaterAcidification(20, 8.2, 5.0, 350, 'lactic_80');
		expect(result.amountPerLiter).toBeGreaterThan(0.35);
		expect(result.exceedsFlavorThreshold).toBe(true);
	});

	it('supports phosphoric acid 10% and 85%', () => {
		const res10 = calculateWaterAcidification(20, 7.5, 5.5, 80, 'phosphoric_10');
		const res85 = calculateWaterAcidification(20, 7.5, 5.5, 80, 'phosphoric_85');

		expect(res10.requiredAmount).toBeGreaterThan(res85.requiredAmount);
		// 85% is ~14.6 mEq/mL vs 1.08 mEq/mL for 10% (~13.5x stronger)
		expect(res10.requiredAmount / res85.requiredAmount).toBeCloseTo(13.5, 0);
	});

	it('supports citric acid powder and acidulated malt', () => {
		const citric = calculateWaterAcidification(25, 7.6, 5.4, 100, 'citric_powder');
		expect(citric.unit).toBe('g');
		expect(citric.requiredAmount).toBeGreaterThan(1.5);

		const acidMalt = calculateWaterAcidification(25, 7.6, 5.4, 100, 'acid_malt');
		expect(acidMalt.unit).toBe('g');
		expect(acidMalt.requiredAmount).toBeGreaterThan(100);
	});

	it('calculates baking soda base addition to raise pH', () => {
		const result = calculateWaterAlkalinization(20, 5.0, 5.4, 'baking_soda');
		expect(result.mode).toBe('base');
		expect(result.unit).toBe('g');
		expect(result.requiredAmount).toBeGreaterThan(0.5);
		expect(result.addedSodiumMgL).toBeDefined();
		expect(result.addedSodiumMgL!).toBeGreaterThan(0);
	});

	it('calculates slaked lime base addition to raise pH with calcium addition', () => {
		const result = calculateWaterAlkalinization(20, 5.0, 5.4, 'slaked_lime');
		expect(result.unit).toBe('g');
		expect(result.addedCalciumMgL).toBeDefined();
		expect(result.addedCalciumMgL!).toBeGreaterThan(0);
	});

	it('verifies all acid and base catalogs have valid metadata', () => {
		for (const acid of Object.values(ACID_CATALOG)) {
			expect(acid.mEqPerUnit).toBeGreaterThan(0);
			expect(acid.nameKey).toContain('calculations.water_ph.acids.');
		}
		for (const base of Object.values(BASE_CATALOG)) {
			expect(base.mEqPerGram).toBeGreaterThan(0);
			expect(base.nameKey).toContain('calculations.water_ph.bases.');
		}
	});
});
