import { describe, expect, it } from 'vitest';
import {
	calculateBrewMetrics,
	calculateBuGuRatio,
	calculateApparentAttenuation,
	calculateRealAttenuation,
	calculateActualAbv,
	correctHydrometerReading,
	danielsIbu,
	srmToHexColor,
	sgToPpg,
	ppgToSg,
	smartPotentialToSg,
	formatPotentialGravity,
	isMashGrain,
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

		it('calculates BU:GU ratio, Apparent Attenuation, and Real Attenuation accurately', () => {
			expect(calculateBuGuRatio(1.05, 40)).toBe(0.8);
			expect(calculateBuGuRatio(1.0, 40)).toBe(0);
			expect(calculateApparentAttenuation(1.05, 1.01)).toBe(80.0);
			expect(calculateApparentAttenuation(1.01, 1.01)).toBe(0);
			expect(calculateApparentAttenuation(1.05, 0)).toBe(0);
			expect(calculateApparentAttenuation(1.05, -0.5)).toBe(0);
			expect(calculateApparentAttenuation(NaN, 1.01)).toBe(0);

			// Real attenuation overloads (ASBC 0.8192 factor, 1 decimal place)
			expect(calculateRealAttenuation(80.0)).toBe(65.5);
			expect(calculateRealAttenuation(1.05, 1.01)).toBe(65.5);
			expect(calculateRealAttenuation(0)).toBe(0);
			expect(calculateRealAttenuation(-10)).toBe(0);
			expect(calculateRealAttenuation(NaN)).toBe(0);
			expect(calculateRealAttenuation(1.01, 1.05)).toBe(0);
			expect(calculateRealAttenuation(1.0, 0.99)).toBe(0);
			expect(calculateRealAttenuation(1.05, 0)).toBe(0);
		});

		it('adjusts yeast attenuation for mash saccharification temperature (e.g. 64.5C Hochkurz maltose rest with 70% WLP860)', () => {
			const hellesItems: CalculatorItem[] = [
				{
					ingredient: {
						id: 'g1',
						name: 'Barke Pilsner Malt',
						type: 'Fermentable',
						potentialGravity: 1.037,
						colorSrm: 1.8,
						isCatalogItem: false
					},
					amount: 4.4,
					unit: 'kg',
					usage: 'Mash'
				},
				{
					ingredient: {
						id: 'g2',
						name: 'Munich Malt (Light)',
						type: 'Fermentable',
						potentialGravity: 1.035,
						colorSrm: 9.0,
						isCatalogItem: true
					},
					amount: 0.2,
					unit: 'kg',
					usage: 'Mash'
				},
				{
					ingredient: {
						id: 'y1',
						name: 'Munich Helles Yeast WLP860',
						type: 'Yeast',
						attenuationPercent: 70.0,
						isCatalogItem: false
					},
					amount: 70,
					unit: 'ml',
					usage: 'Primary'
				}
			];

			const standard67C = calculateBrewMetrics(20, 72, 75, hellesItems, {
				mashSteps: [
					{ temperatureC: 67.0, durationMinutes: 60, stepOrder: 1 },
					{ temperatureC: 76.0, durationMinutes: 10, stepOrder: 2 }
				]
			});
			const hochkurz645C = calculateBrewMetrics(20, 72, 75, hellesItems, {
				mashSteps: [
					{ temperatureC: 64.5, durationMinutes: 45, stepOrder: 1 },
					{ temperatureC: 71.0, durationMinutes: 25, stepOrder: 2 },
					{ temperatureC: 76.0, durationMinutes: 10, stepOrder: 3 }
				]
			});
			const dextrinous70C = calculateBrewMetrics(20, 72, 75, hellesItems, {
				mashSteps: [{ temperatureC: 70.0, durationMinutes: 60, stepOrder: 1 }]
			});

			expect(hochkurz645C.finalGravity).toBe(1.012);
			expect(hochkurz645C.alcoholByVolume).toBeGreaterThanOrEqual(5.05);
			expect(hochkurz645C.finalGravity).toBeLessThan(standard67C.finalGravity);
			expect(dextrinous70C.finalGravity).toBeGreaterThan(standard67C.finalGravity);
		});

		it('handles Sugar 100% fermentability, Extract yeast attenuation, explicit fermentabilityPercent, and Bottling exclusion', () => {
			const baseGrain: CalculatorItem = {
				ingredient: {
					id: 'g1',
					name: 'Pilsner Malt',
					type: 'Fermentable',
					fermentableCategory: 'Grain',
					potentialGravity: 1.037,
					colorSrm: 1.8,
					isCatalogItem: true
				},
				amount: 4.0,
				unit: 'kg',
				usage: 'Mash'
			};
			const yeast: CalculatorItem = {
				ingredient: {
					id: 'y1',
					name: 'Abbey Ale',
					type: 'Yeast',
					attenuationPercent: 78,
					isCatalogItem: true
				},
				amount: 11.5,
				unit: 'g',
				usage: 'Primary'
			};
			const sugar: CalculatorItem = {
				ingredient: {
					id: 's1',
					name: 'Belgian Candi Sugar',
					type: 'Fermentable',
					fermentableCategory: 'Sugar',
					potentialGravity: 1.038,
					colorSrm: 0.5,
					isCatalogItem: true
				},
				amount: 1.0,
				unit: 'kg',
				usage: 'Boil'
			};
			const extract: CalculatorItem = {
				ingredient: {
					id: 'e1',
					name: 'Custom Powder',
					type: 'Fermentable',
					fermentableCategory: 'DryExtract',
					potentialGravity: 1.044,
					colorSrm: 3.0,
					isCatalogItem: true
				},
				amount: 1.0,
				unit: 'kg',
				usage: 'Boil'
			};
			const priming: CalculatorItem = {
				ingredient: {
					id: 'p1',
					name: 'Priming Dextrose',
					type: 'Fermentable',
					fermentableCategory: 'Sugar',
					potentialGravity: 1.046,
					colorSrm: 5.0,
					isCatalogItem: true
				},
				amount: 0.14,
				unit: 'kg',
				usage: 'Bottling'
			};
			const partialHoney: CalculatorItem = {
				ingredient: {
					id: 'h1',
					name: 'Acacia Honey',
					type: 'Fermentable',
					fermentableCategory: 'Sugar',
					fermentabilityPercent: 50,
					potentialGravity: 1.035,
					colorSrm: 1.5,
					isCatalogItem: false
				},
				amount: 1.0,
				unit: 'kg',
				usage: 'Boil'
			};

			const maltOnly = calculateBrewMetrics(20, 75, 60, [baseGrain, yeast]);
			const withSugar = calculateBrewMetrics(20, 75, 60, [baseGrain, sugar, yeast]);
			const withExtract = calculateBrewMetrics(20, 75, 60, [baseGrain, extract, yeast]);
			const withPriming = calculateBrewMetrics(20, 75, 60, [baseGrain, priming, yeast]);
			const withPartialHoney = calculateBrewMetrics(20, 75, 60, [baseGrain, partialHoney, yeast]);

			expect(withSugar.originalGravity).toBeGreaterThan(maltOnly.originalGravity);
			expect(withSugar.finalGravity).toBe(maltOnly.finalGravity);
			expect(withExtract.finalGravity).toBeGreaterThan(maltOnly.finalGravity);
			expect(withPriming).toEqual(maltOnly);

			const addedOg = withPartialHoney.originalGravity - maltOnly.originalGravity;
			const addedFg = withPartialHoney.finalGravity - maltOnly.finalGravity;
			expect(addedFg).toBeCloseTo(addedOg * 0.5, 2);

			expect(isMashGrain('Fermentable', 'Mash', 'Grain', 'Pilsner')).toBe(true);
			expect(isMashGrain('Fermentable', 'Mash', 'Adjunct', 'Flaked Oats')).toBe(true);
			expect(isMashGrain('Fermentable', 'Boil', 'Grain', 'Caramel 60L')).toBe(false);
			expect(isMashGrain('Fermentable', 'Mash', 'Sugar', 'Dextrose')).toBe(false);
			expect(isMashGrain('Fermentable', 'Boil', 'DryExtract', 'Light DME')).toBe(false);
		});
	});

	describe('calculateActualAbv & High-Gravity ASBC Parity (TC-T1-R4-03, TC-T2-R4-02)', () => {
		it('calculates ABV with Linear formula ((OG - FG) * 131.25) when OG <= 1.060', () => {
			const abv = calculateActualAbv(1.05, 1.01, 'Linear');
			expect(abv).toBe(5.25);
		});

		it('automatically switches to ASBC non-linear formula for OG > 1.060 regardless of formula selection', () => {
			const linearOverride = calculateActualAbv(1.09, 1.02, 'Linear');
			const advanced = calculateActualAbv(1.09, 1.02, 'Advanced');
			const defaultFormula = calculateActualAbv(1.09, 1.02);
			expect(linearOverride).toBe(9.99);
			expect(advanced).toBe(9.99);
			expect(defaultFormula).toBe(9.99);

			// Formula-override parity at 1.070 and 1.080
			expect(calculateActualAbv(1.07, 1.015, 'Linear')).toBe(7.59);
			expect(calculateActualAbv(1.07, 1.015, 'Advanced')).toBe(7.59);
			expect(calculateActualAbv(1.07, 1.015)).toBe(7.59);
			expect(calculateActualAbv(1.08, 1.018, 'Linear')).toBe(8.7);
			expect(calculateActualAbv(1.08, 1.018, 'Advanced')).toBe(8.7);
		});

		it('switches cleanly at exact OG 1.0600 vs 1.0601 / 1.061 boundary', () => {
			// At 1.0600 (<= 1.060): linear -> (1.060 - 1.010) * 131.25 = 6.5625 -> 6.56
			expect(calculateActualAbv(1.06, 1.01, 'Linear')).toBe(6.56);
			expect(calculateActualAbv(1.06, 1.01)).toBe(6.56);

			// Above 1.060 (1.0601 & 1.061): ASBC non-linear -> 6.78 and 6.91
			expect(calculateActualAbv(1.0601, 1.01, 'Linear')).toBe(6.78);
			expect(calculateActualAbv(1.0601, 1.01)).toBe(6.78);
			expect(calculateActualAbv(1.061, 1.01, 'Linear')).toBe(6.91);
			expect(calculateActualAbv(1.061, 1.01, 'Advanced')).toBe(6.91);
		});

		it('TC-T1-R4-03: matches backend ASBC formula calculation for OG 1.070, FG 1.015', () => {
			// Backend BrewingCalculator.CalculateActualAbv(1.070, 1.015) computes 7.59%
			// abw = (76.08 * 0.055) / (1.775 - 1.070) = 5.935319
			// abv = 5.935319 * (1.015 / 0.794) = 7.587% -> 7.59%
			const abv = calculateActualAbv(1.07, 1.015, 'Advanced');
			expect(abv).toBe(7.59);
		});

		it('TC-T1-R4-03: matches backend ASBC formula calculation for Imperial Stout (OG 1.080, FG 1.018)', () => {
			// Backend BrewingCalculator.CalculateActualAbv(1.080, 1.018) computes 8.70%
			// abw = (76.08 * 0.062) / (1.775 - 1.080) = 4.71696 / 0.695 = 6.78699
			// abv = 6.78699 * (1.018 / 0.794) = 8.7017% -> 8.70%
			const abv = calculateActualAbv(1.08, 1.018, 'Advanced');
			expect(abv).toBe(8.7);
		});

		it('TC-T2-R4-02: handles threshold and corner cases safely', () => {
			// Identical gravities (no fermentation)
			expect(calculateActualAbv(1.05, 1.05, 'Linear')).toBe(0);
			expect(calculateActualAbv(1.05, 1.05, 'Advanced')).toBe(0);

			// Inverted gravities (FG > OG)
			expect(calculateActualAbv(1.01, 1.05, 'Linear')).toBe(0);
			expect(calculateActualAbv(1.01, 1.05, 'Advanced')).toBe(0);

			// Sub-water or water gravities (OG <= 1.0)
			expect(calculateActualAbv(1.0, 1.0, 'Linear')).toBe(0);
			expect(calculateActualAbv(0.998, 0.995, 'Linear')).toBe(0);
			expect(calculateActualAbv(0.998, 0.995, 'Advanced')).toBe(0);

			// Zero / negative FG and non-finite inputs
			expect(calculateActualAbv(1.05, 0, 'Linear')).toBe(0);
			expect(calculateActualAbv(1.05, -1, 'Advanced')).toBe(0);
			expect(calculateActualAbv(NaN, 1.01)).toBe(0);
			expect(calculateActualAbv(1.05, NaN)).toBe(0);

			// ASBC singularity guard (og >= 1.775)
			expect(calculateActualAbv(1.775, 1.01, 'Advanced')).toBe(0);
			expect(calculateActualAbv(1.8, 1.05, 'Linear')).toBe(0);

			// Extreme OG values within valid range
			const extremeAbv = calculateActualAbv(1.15, 1.03, 'Advanced');
			expect(extremeAbv).toBeGreaterThan(15.0);
			expect(extremeAbv).toBeLessThan(20.0);
		});
	});

	describe('Hydrometer Temperature Correction ASBC Parity (TC-T1-R4-01, TC-T1-R4-02, TC-T2-R4-01)', () => {
		it('TC-T1-R4-01: at calibration temperature (20°C), hydrometer reading is unchanged', () => {
			const corrected = correctHydrometerReading(1.05, 20.0, 20.0);
			expect(corrected).toBe(1.05);
		});

		it('TC-T1-R4-02: warm hydrometer sample (50°C) corrects to higher gravity due to wort expansion', () => {
			// Backend test vector: 1.050 at 50°C with 20°C calTemp -> approx 1.0607
			const corrected = correctHydrometerReading(1.05, 50.0, 20.0);
			expect(corrected).toBeGreaterThan(1.058);
			expect(corrected).toBeCloseTo(1.0607, 3);
		});

		it('TC-T1-R4-02: cold hydrometer sample (10°C) corrects to lower gravity due to wort contraction', () => {
			// Backend test vector: 1.050 at 10°C with 20°C calTemp -> approx 1.0483
			const corrected = correctHydrometerReading(1.05, 10.0, 20.0);
			expect(corrected).toBeLessThan(1.05);
			expect(corrected).toBeCloseTo(1.0483, 3);
		});

		it('TC-T1-R4-02: supports custom calibration temperature (e.g. 15.55°C / 60°F)', () => {
			// Backend test vector: 1.050 at 25°C with 15.55°C calTemp -> approx 1.0522
			const corrected = correctHydrometerReading(1.05, 25.0, 15.55);
			expect(corrected).toBeGreaterThan(1.05);
			expect(corrected).toBeCloseTo(1.0522, 3);
		});

		it('TC-T2-R4-01: handles extreme and invalid temperatures safely without divergence or NaN', () => {
			// tempC <= 0: safely returns raw SG
			expect(correctHydrometerReading(1.05, 0.0, 20.0)).toBe(1.05);
			expect(correctHydrometerReading(1.05, -5.0, 20.0)).toBe(1.05);

			// tempC > 100: safely returns raw SG
			expect(correctHydrometerReading(1.05, 105.0, 20.0)).toBe(1.05);

			// rawSg <= 0: safely returns raw SG
			expect(correctHydrometerReading(0.0, 25.0, 20.0)).toBe(0.0);
			expect(correctHydrometerReading(-1.0, 25.0, 20.0)).toBe(-1.0);

			// calTempC <= 0 or > 100: safely returns raw SG
			expect(correctHydrometerReading(1.05, 25.0, 0.0)).toBe(1.05);
			expect(correctHydrometerReading(1.05, 25.0, 105.0)).toBe(1.05);

			// Non-finite inputs safely return raw SG
			expect(correctHydrometerReading(1.05, NaN, 20.0)).toBe(1.05);
			expect(correctHydrometerReading(1.05, 25.0, Infinity)).toBe(1.05);
		});
	});
});
