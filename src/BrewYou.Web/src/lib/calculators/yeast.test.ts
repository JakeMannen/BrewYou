import { describe, expect, it } from 'vitest';
import { calculateYeastPitch, calculateYeastViability } from './yeast';

describe('yeast pitch calculator', () => {
	it('computes 0.75M/mL/P for ales and 1.50M/mL/P for lagers', () => {
		const ale = calculateYeastPitch({
			batchVolumeLiters: 20,
			originalGravity: 1.05,
			styleCategory: 'Ale',
			yeastForm: 'Liquid',
			packageAgeDays: 0,
			baseViabilityPercent: 100,
			packsAvailable: 1,
			starterMethod: 'StirPlate'
		});
		const lager = calculateYeastPitch({
			batchVolumeLiters: 20,
			originalGravity: 1.05,
			styleCategory: 'Lager',
			yeastForm: 'Dry',
			packageAgeDays: 0,
			baseViabilityPercent: 100,
			packsAvailable: 1,
			starterMethod: 'None'
		});

		expect(ale.targetPitchRateMillionPerMlPlato).toBe(0.75);
		expect(lager.targetPitchRateMillionPerMlPlato).toBe(1.5);
		expect(lager.requiredCellsBillions).toBeCloseTo(ale.requiredCellsBillions * 2, 0);
		expect(lager.dryPacksRecommended).toBeGreaterThanOrEqual(3);
	});

	it('decays liquid viability by 20% per 30 days', () => {
		expect(calculateYeastViability('Liquid', 0, 100)).toBe(100);
		expect(calculateYeastViability('Liquid', 30, 100)).toBe(80);
		expect(calculateYeastViability('Liquid', 60, 100)).toBe(60);
	});
});
