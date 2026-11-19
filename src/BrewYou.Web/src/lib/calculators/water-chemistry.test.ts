import { describe, expect, it } from 'vitest';
import {
	EMPTY_SALT_ADDITIONS,
	calculateResidualAlkalinity,
	calculateWaterIonsAfterAdditions,
	evaluateWaterPlan,
	suggestWaterSaltAdditions
} from './water-chemistry';

describe('water chemistry calculator', () => {
	it('computes gypsum and CaCl2 ion boosts and Kolbach RA', () => {
		const ro = {
			calciumPpm: 0,
			magnesiumPpm: 0,
			sodiumPpm: 0,
			sulfatePpm: 0,
			chloridePpm: 0,
			bicarbonatePpm: 0
		};
		const after = calculateWaterIonsAfterAdditions(
			ro,
			{ ...EMPTY_SALT_ADDITIONS(), gypsumGrams: 1, calciumChlorideGrams: 1 },
			10
		);
		expect(after.calciumPpm).toBeCloseTo(50.5, 0);
		expect(after.sulfatePpm).toBeCloseTo(55.8, 0);
		expect(after.chloridePpm).toBeCloseTo(48.2, 0);

		const pilsenRa = calculateResidualAlkalinity({
			calciumPpm: 7,
			magnesiumPpm: 2,
			sodiumPpm: 2,
			sulfatePpm: 5,
			chloridePpm: 5,
			bicarbonatePpm: 15
		});
		expect(pilsenRa).toBeCloseTo(6.1, 0);
	});

	it('suggests salts & lactic acid to hit target IPA water and 5.4 mash pH', () => {
		const source = {
			calciumPpm: 35,
			magnesiumPpm: 8,
			sodiumPpm: 15,
			sulfatePpm: 18,
			chloridePpm: 22,
			bicarbonatePpm: 140
		};
		const target = {
			calciumPpm: 110,
			magnesiumPpm: 10,
			sodiumPpm: 15,
			sulfatePpm: 250,
			chloridePpm: 55,
			bicarbonatePpm: 35
		};
		const grist = [{ weightKg: 5, colorSrm: 2.5, name: 'Pale Malt' }];
		const suggested = suggestWaterSaltAdditions(source, target, 16, 12, grist, 5.4, 'Lactic80');

		expect(suggested.mashAdditions.gypsumGrams).toBeGreaterThan(0);
		expect(suggested.mashAdditions.acidAmountMlOrG).toBeGreaterThan(0);
		expect(suggested.summary.predictedMashPh).toBeCloseTo(5.4, 1);

		const check = evaluateWaterPlan(
			source,
			suggested.mashAdditions,
			16,
			suggested.spargeAdditions,
			12,
			grist
		);
		expect(check.sulfateToChlorideRatio).toBeGreaterThan(2.5);
	});
});
