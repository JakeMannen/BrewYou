import { describe, expect, it } from 'vitest';
import {
	calculateBrewMetrics,
	calculateBuGuRatio,
	calculateApparentAttenuation,
	danielsIbu,
	srmToHexColor,
	sgToPpg,
	ppgToSg,
	smartPotentialToSg,
	formatPotentialGravity,
	reconcileImportedEfficiency,
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

		expect(tinsethResult.bitternessIbu).toBeGreaterThan(30);
		expect(ragerResult.bitternessIbu).toBeGreaterThan(tinsethResult.bitternessIbu);
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

	describe('reconcileImportedEfficiency', () => {
		it('derives effective mash efficiency when stated brewhouse efficiency under-predicts target OG', () => {
			// Erdinger Clone grain bill: 3kg Wheat (1.038), 2kg Pilsner (1.037), 0.2kg Carahell (1.035) in 23L
			const fermentables = [
				{ amount: 3.0, potentialGravity: 1.038 },
				{ amount: 2.0, potentialGravity: 1.037 },
				{ amount: 0.2, potentialGravity: 1.035 }
			];
			const eff = reconcileImportedEfficiency(23.0, 65.0, 1.05, fermentables);
			expect(eff).toBe(70.7);
		});

		it('retains stated efficiency when calculated OG already matches target OG', () => {
			const fermentables = [{ amount: 4.5, potentialGravity: 1.037 }];
			const eff = reconcileImportedEfficiency(20.0, 75.0, 1.052, fermentables);
			expect(eff).toBe(75.0);
		});

		it('retains stated efficiency when target OG is missing or invalid', () => {
			const fermentables = [{ amount: 4.5, potentialGravity: 1.037 }];
			expect(reconcileImportedEfficiency(20.0, 65.0, null, fermentables)).toBe(65.0);
			expect(reconcileImportedEfficiency(20.0, 65.0, 1.0, fermentables)).toBe(65.0);
			expect(reconcileImportedEfficiency(20.0, 65.0, 1.5, fermentables)).toBe(65.0);
		});
	});

	describe('Brewing Physics Parity (FirstWort, Hop Form, Soluble Sugars, Lactose, Multi-Yeast, Units)', () => {
		it('applies FirstWort (+10%), Whirlpool (20%), Mash (30%), DryHop (0%), and Leaf (-10%) hop multipliers', () => {
			const makeHopRecipe = (
				usage: 'Boil' | 'FirstWort' | 'Whirlpool' | 'Mash' | 'DryHop',
				form: string,
				mins = 60
			): CalculatorItem[] => [
				{
					ingredient: {
						id: 'g1',
						name: 'Pale Malt',
						type: 'Fermentable',
						potentialGravity: 1.037,
						isCatalogItem: true
					},
					amount: 4.5,
					unit: 'kg',
					usage: 'Mash'
				},
				{
					ingredient: {
						id: 'h1',
						name: 'Cascade',
						type: 'Hop',
						alphaAcidPercent: 6.5,
						isCatalogItem: true
					},
					amount: 30,
					unit: 'g',
					durationMinutes: mins,
					usage,
					form
				}
			];

			const boilPellet = calculateBrewMetrics(20, 75, 60, makeHopRecipe('Boil', 'Pellet', 60));
			const fwhPellet = calculateBrewMetrics(20, 75, 60, makeHopRecipe('FirstWort', 'Pellet', 60));
			const boilLeaf = calculateBrewMetrics(20, 75, 60, makeHopRecipe('Boil', 'Leaf', 60));
			const whirlpool = calculateBrewMetrics(20, 75, 60, makeHopRecipe('Whirlpool', 'Pellet', 20));
			const mashHop = calculateBrewMetrics(20, 75, 60, makeHopRecipe('Mash', 'Pellet', 60));
			const dryHop = calculateBrewMetrics(20, 75, 60, makeHopRecipe('DryHop', 'Pellet', 4320));

			expect(fwhPellet.bitternessIbu).toBeCloseTo(boilPellet.bitternessIbu * 1.1, 0);
			expect(boilLeaf.bitternessIbu).toBeCloseTo(boilPellet.bitternessIbu * 0.9, 0);
			expect(whirlpool.bitternessIbu).toBeGreaterThan(0);
			expect(whirlpool.bitternessIbu).toBeLessThan(boilPellet.bitternessIbu);
			expect(mashHop.bitternessIbu).toBeCloseTo(boilPellet.bitternessIbu * 0.3, 0);
			expect(dryHop.bitternessIbu).toBe(0);
		});

		it('bypasses mash efficiency for soluble sugars and retains 100% of unfermentable lactose in FG', () => {
			const grainBase: CalculatorItem[] = [
				{
					ingredient: {
						id: 'g1',
						name: 'Pale Malt',
						type: 'Fermentable',
						potentialGravity: 1.037,
						isCatalogItem: true
					},
					amount: 4.0,
					unit: 'kg',
					usage: 'Mash'
				},
				{
					ingredient: {
						id: 'y1',
						name: 'SafAle US-05',
						type: 'Yeast',
						attenuationPercent: 80,
						isCatalogItem: true
					},
					amount: 11.5,
					unit: 'g',
					usage: 'Primary'
				}
			];

			const withDextrose: CalculatorItem[] = [
				...grainBase,
				{
					ingredient: {
						id: 's1',
						name: 'Corn Sugar (Dextrose)',
						type: 'Fermentable',
						potentialGravity: 1.046,
						isCatalogItem: true
					},
					amount: 0.5,
					unit: 'kg',
					usage: 'Boil'
				}
			];

			const withLactose: CalculatorItem[] = [
				...grainBase,
				{
					ingredient: {
						id: 'l1',
						name: 'Milk Sugar (Lactose)',
						type: 'Fermentable',
						potentialGravity: 1.043,
						isCatalogItem: true
					},
					amount: 0.5,
					unit: 'kg',
					usage: 'Boil'
				}
			];

			const base60 = calculateBrewMetrics(20, 60, 60, grainBase);
			const dex60 = calculateBrewMetrics(20, 60, 60, withDextrose);
			const base85 = calculateBrewMetrics(20, 85, 60, grainBase);
			const dex85 = calculateBrewMetrics(20, 85, 60, withDextrose);

			expect(dex60.originalGravity - base60.originalGravity).toBeCloseTo(
				dex85.originalGravity - base85.originalGravity,
				3
			);

			const base75 = calculateBrewMetrics(20, 75, 60, grainBase);
			const lac75 = calculateBrewMetrics(20, 75, 60, withLactose);
			const lactoseOgPoints = lac75.originalGravity - base75.originalGravity;
			expect(lac75.finalGravity - base75.finalGravity).toBeCloseTo(lactoseOgPoints, 3);
		});

		it('uses maximum attenuation across co-pitched yeasts and normalizes imperial units (lb / oz)', () => {
			const imperialCoPitch: CalculatorItem[] = [
				{
					ingredient: {
						id: 'g1',
						name: 'Pale Malt',
						type: 'Fermentable',
						potentialGravity: 1.037,
						isCatalogItem: true
					},
					amount: 11.0231, // ~5.0 kg
					unit: 'lb',
					usage: 'Mash'
				},
				{
					ingredient: {
						id: 'h1',
						name: 'Citra',
						type: 'Hop',
						alphaAcidPercent: 12.0,
						isCatalogItem: true
					},
					amount: 1.0582, // ~30.0 g
					unit: 'oz',
					durationMinutes: 60,
					usage: 'Boil'
				},
				{
					ingredient: {
						id: 'y1',
						name: 'English Ale',
						type: 'Yeast',
						attenuationPercent: 70,
						isCatalogItem: true
					},
					amount: 1,
					unit: 'pkg',
					usage: 'Primary'
				},
				{
					ingredient: {
						id: 'y2',
						name: 'Belle Saison',
						type: 'Yeast',
						attenuationPercent: 88,
						isCatalogItem: true
					},
					amount: 1,
					unit: 'pkg',
					usage: 'Secondary'
				}
			];

			const metricSingle: CalculatorItem[] = [
				{
					ingredient: {
						id: 'g1',
						name: 'Pale Malt',
						type: 'Fermentable',
						potentialGravity: 1.037,
						isCatalogItem: true
					},
					amount: 5.0,
					unit: 'kg',
					usage: 'Mash'
				},
				{
					ingredient: {
						id: 'h1',
						name: 'Citra',
						type: 'Hop',
						alphaAcidPercent: 12.0,
						isCatalogItem: true
					},
					amount: 30.0,
					unit: 'g',
					durationMinutes: 60,
					usage: 'Boil'
				},
				{
					ingredient: {
						id: 'y2',
						name: 'Belle Saison',
						type: 'Yeast',
						attenuationPercent: 88,
						isCatalogItem: true
					},
					amount: 1,
					unit: 'pkg',
					usage: 'Primary'
				}
			];

			const imp = calculateBrewMetrics(20, 75, 60, imperialCoPitch);
			const met = calculateBrewMetrics(20, 75, 60, metricSingle);

			expect(imp.originalGravity).toBe(met.originalGravity);
			expect(imp.finalGravity).toBe(met.finalGravity);
			expect(imp.bitternessIbu).toBeCloseTo(met.bitternessIbu, 1);
		});

		it('calculates BU:GU ratio and Apparent Attenuation accurately', () => {
			expect(calculateBuGuRatio(1.05, 40)).toBe(0.8);
			expect(calculateBuGuRatio(1.0, 40)).toBe(0);
			expect(calculateApparentAttenuation(1.05, 1.01)).toBe(80.0);
			expect(calculateApparentAttenuation(1.01, 1.01)).toBe(0);
		});
	});
});
