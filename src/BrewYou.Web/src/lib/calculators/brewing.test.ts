import { describe, expect, it } from 'vitest';
import {
	calculateBrewMetrics,
	danielsIbu,
	srmToHexColor,
	sgToPpg,
	ppgToSg,
	smartPotentialToSg,
	formatPotentialGravity,
	type CalculatorItem
} from './brewing';

describe('calculateBrewMetrics', () => {
	it('calculates expected OG and color for 5kg base malt in 20L', () => {
		const items: CalculatorItem[] = [
			{
				ingredient: {
					id: '1',
					name: 'Pale Malt',
					type: 'Fermentable',
					potentialGravity: 1.037,
					colorSrm: 2.0,
					isCatalogItem: true
				},
				amount: 5.0,
				usage: 'Mash'
			}
		];

		const result = calculateBrewMetrics(20, 75, 60, items);

		expect(result.originalGravity).toBeGreaterThan(1.05);
		expect(result.originalGravity).toBeLessThan(1.065);
		expect(result.colorSrm).toBeGreaterThan(0);
	});

	it('calculates FG and ABV with yeast attenuation', () => {
		const items: CalculatorItem[] = [
			{
				ingredient: {
					id: '1',
					name: 'Pale Malt',
					type: 'Fermentable',
					potentialGravity: 1.037,
					colorSrm: 2.0,
					isCatalogItem: true
				},
				amount: 5.0,
				usage: 'Mash'
			},
			{
				ingredient: {
					id: '2',
					name: 'SafAle US-05',
					type: 'Yeast',
					attenuationPercent: 80,
					isCatalogItem: true
				},
				amount: 11.5,
				usage: 'Primary'
			}
		];

		const result = calculateBrewMetrics(20, 75, 60, items);

		expect(result.finalGravity).toBeLessThan(result.originalGravity);
		expect(result.finalGravity).toBeGreaterThanOrEqual(1.008);
		expect(result.alcoholByVolume).toBeGreaterThan(5.0);
		expect(result.alcoholByVolume).toBeLessThan(6.5);
	});

	it('calculates Tinseth IBU with boil hops', () => {
		const items: CalculatorItem[] = [
			{
				ingredient: {
					id: '1',
					name: 'Pale Malt',
					type: 'Fermentable',
					potentialGravity: 1.037,
					isCatalogItem: true
				},
				amount: 5.0,
				usage: 'Mash'
			},
			{
				ingredient: {
					id: '2',
					name: 'Citra',
					type: 'Hop',
					alphaAcidPercent: 12.0,
					isCatalogItem: true
				},
				amount: 30.0,
				durationMinutes: 60,
				usage: 'Boil'
			}
		];

		const result = calculateBrewMetrics(20, 75, 60, items);

		expect(result.bitternessIbu).toBeGreaterThan(30);
		expect(result.bitternessIbu).toBeLessThan(65);
	});

	it('maps SRM values to valid hex color strings', () => {
		expect(srmToHexColor(2)).toMatch(/^#[0-9a-f]{6}$/i);
		expect(srmToHexColor(30)).toMatch(/^#[0-9a-f]{6}$/i);
	});

	it('supports alternative bitterness formulas (Rager and Daniels)', () => {
		const items: CalculatorItem[] = [
			{
				ingredient: {
					id: '1',
					name: 'Pale Malt',
					type: 'Fermentable',
					potentialGravity: 1.037,
					isCatalogItem: true
				},
				amount: 5.0,
				usage: 'Mash'
			},
			{
				ingredient: {
					id: '2',
					name: 'Citra',
					type: 'Hop',
					alphaAcidPercent: 12.0,
					isCatalogItem: true
				},
				amount: 30.0,
				durationMinutes: 60,
				usage: 'Boil'
			}
		];

		const tinsethResult = calculateBrewMetrics(20, 75, 60, items, { bitternessFormula: 'Tinseth' });
		const ragerResult = calculateBrewMetrics(20, 75, 60, items, { bitternessFormula: 'Rager' });
		const danielsResult = calculateBrewMetrics(20, 75, 60, items, { bitternessFormula: 'Daniels' });

		expect(tinsethResult.bitternessIbu).toBeGreaterThan(0);
		expect(ragerResult.bitternessIbu).toBeGreaterThan(0);
		expect(danielsResult.bitternessIbu).toBeGreaterThan(0);
	});

	it('supports alternative color formulas (Mosher and Daniels)', () => {
		const items: CalculatorItem[] = [
			{
				ingredient: {
					id: '1',
					name: 'Munich Malt',
					type: 'Fermentable',
					potentialGravity: 1.035,
					colorSrm: 9.0,
					isCatalogItem: true
				},
				amount: 4.0,
				usage: 'Mash'
			}
		];

		const moreyResult = calculateBrewMetrics(20, 75, 60, items, { colorFormula: 'Morey' });
		const mosherResult = calculateBrewMetrics(20, 75, 60, items, { colorFormula: 'Mosher' });
		const danielsResult = calculateBrewMetrics(20, 75, 60, items, { colorFormula: 'Daniels' });

		expect(moreyResult.colorSrm).toBeGreaterThan(0);
		expect(mosherResult.colorSrm).toBeGreaterThan(0);
		expect(danielsResult.colorSrm).toBeGreaterThan(0);
	});

	it('supports advanced ASBC non-linear ABV formula', () => {
		const items: CalculatorItem[] = [
			{
				ingredient: {
					id: '1',
					name: 'Pale Malt',
					type: 'Fermentable',
					potentialGravity: 1.037,
					isCatalogItem: true
				},
				amount: 8.0,
				usage: 'Mash'
			},
			{
				ingredient: {
					id: '2',
					name: 'SafAle US-05',
					type: 'Yeast',
					attenuationPercent: 80,
					isCatalogItem: true
				},
				amount: 11.5,
				usage: 'Primary'
			}
		];

		const linearResult = calculateBrewMetrics(20, 75, 60, items, { abvFormula: 'Linear' });
		const advancedResult = calculateBrewMetrics(20, 75, 60, items, { abvFormula: 'Advanced' });

		expect(linearResult.alcoholByVolume).toBeGreaterThan(7.0);
		expect(advancedResult.alcoholByVolume).toBeGreaterThan(7.0);
	});

	it('calculates danielsIbu across all boil durations and zero edge cases', () => {
		expect(danielsIbu(0, 10, 60, 1.05, 20)).toBe(0);
		expect(danielsIbu(30, 0, 60, 1.05, 20)).toBe(0);
		expect(danielsIbu(30, 10, 60, 1.05, 0)).toBe(0);

		const ibu60 = danielsIbu(30, 10, 60, 1.05, 20);
		const ibu45 = danielsIbu(30, 10, 45, 1.05, 20);
		const ibu30 = danielsIbu(30, 10, 30, 1.05, 20);
		const ibu15 = danielsIbu(30, 10, 15, 1.05, 20);
		const ibu5 = danielsIbu(30, 10, 5, 1.05, 20);
		const ibu0 = danielsIbu(30, 10, 0, 1.05, 20);

		expect(ibu60).toBeGreaterThan(ibu45);
		expect(ibu45).toBeGreaterThan(ibu30);
		expect(ibu30).toBeGreaterThan(ibu15);
		expect(ibu15).toBeGreaterThan(ibu5);
		expect(ibu5).toBeGreaterThanOrEqual(ibu0);
	});

	describe('PPG and Specific Gravity (SG) unit conversions', () => {
		it('converts SG to PPG accurately for common brewing fermentables', () => {
			expect(sgToPpg(1.037)).toBe(37);
			expect(sgToPpg(1.038)).toBe(38);
			expect(sgToPpg(1.046)).toBe(46); // Sucrose
			expect(sgToPpg(1.03)).toBe(30); // Roasted malt
			expect(sgToPpg(1.0)).toBe(0);
			expect(sgToPpg(null)).toBe(37); // Default fallback
			expect(sgToPpg(undefined)).toBe(37);
		});

		it('converts PPG to SG accurately', () => {
			expect(ppgToSg(38)).toBe(1.038);
			expect(ppgToSg(37)).toBe(1.037);
			expect(ppgToSg(46)).toBe(1.046);
			expect(ppgToSg(0)).toBe(1.0);
			expect(ppgToSg(null)).toBe(1.037);
			expect(ppgToSg(undefined)).toBe(1.037);
		});

		it('smartly normalizes input potentials from either PPG or SG values', () => {
			// PPG inputs (e.g. 38, 46, 30)
			expect(smartPotentialToSg(38)).toBe(1.038);
			expect(smartPotentialToSg(36)).toBe(1.036);
			expect(smartPotentialToSg(36.5)).toBe(1.037);
			expect(smartPotentialToSg(46)).toBe(1.046);

			// Standard SG inputs (1.000 - 1.200)
			expect(smartPotentialToSg(1.038)).toBe(1.038);
			expect(smartPotentialToSg(1.0354)).toBe(1.035);

			// Edge cases
			expect(smartPotentialToSg(null)).toBeUndefined();
			expect(smartPotentialToSg(undefined)).toBeUndefined();
			expect(smartPotentialToSg(NaN)).toBeUndefined();
		});

		it('formats potential gravity with both SG and PPG units', () => {
			expect(formatPotentialGravity(1.038)).toBe('1.038 (38 PPG)');
			expect(formatPotentialGravity(1.037)).toBe('1.037 (37 PPG)');
			expect(formatPotentialGravity(null)).toBe('1.037 (37 PPG)');
		});
	});
});
