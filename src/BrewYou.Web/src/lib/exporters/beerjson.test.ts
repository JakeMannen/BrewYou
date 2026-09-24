import { describe, expect, it } from 'vitest';
import { serializeBeerJson, analyzeBeerJsonCompatibility } from './beerjson';
import type { ExportableRecipe } from './types';

describe('BeerJSON 2.x Exporter', () => {
	const sampleRecipe: ExportableRecipe = {
		name: 'Pacific Pale Ale',
		beerStyle: 'Pale Ale',
		description: 'A vibrant Pacific Pale Ale with Galaxy and Mosaic hops.',
		batchSizeLiters: 20.0,
		boilTimeMinutes: 60,
		efficiencyPercent: 72.0,
		originalGravity: 1.048,
		finalGravity: 1.01,
		alcoholByVolume: 5.0,
		bitternessIbu: 35.0,
		colorSrm: 5.5,
		brewer: 'Craft Brewer',
		ingredients: [
			{
				name: 'Pale Ale Malt',
				type: 'Fermentable',
				amount: 4.2,
				unit: 'kg',
				usage: 'Mash',
				potentialGravity: 1.037,
				colorSrm: 2.5
			},
			{
				name: 'Galaxy',
				type: 'Hop',
				amount: 30,
				unit: 'g',
				durationMinutes: 15,
				usage: 'Whirlpool',
				alphaAcidPercent: 14.5
			},
			{
				name: 'Mosaic',
				type: 'Hop',
				amount: 50,
				unit: 'g',
				durationMinutes: 4320,
				usage: 'DryHop',
				alphaAcidPercent: 12.0
			},
			{
				name: 'US-05',
				type: 'Yeast',
				amount: 11.5,
				unit: 'g',
				usage: 'Primary',
				attenuationPercent: 78.0
			}
		],
		mashSteps: [
			{
				stepOrder: 1,
				name: 'Single Infusion',
				type: 'Infusion',
				temperatureC: 66.0,
				durationMinutes: 60,
				infuseAmountLiters: 14.0
			}
		]
	};

	it('produces valid BeerJSON structure with explicit composite unit objects', () => {
		const result = serializeBeerJson(sampleRecipe);

		expect(result.format).toBe('beerjson');
		expect(result.fileExtension).toBe('json');
		expect(result.filename).toBe('Pacific_Pale_Ale.json');
		expect(result.mimeType).toBe('application/json;charset=utf-8');

		const parsed = JSON.parse(result.content);
		expect(parsed.beerjson).toBeDefined();
		expect(parsed.beerjson.version).toBe(2.06);
		expect(parsed.beerjson.recipes).toHaveLength(1);

		const recipe = parsed.beerjson.recipes[0];
		expect(recipe.name).toBe('Pacific Pale Ale');
		expect(recipe.type).toBe('all grain');
		expect(recipe.author).toBe('Craft Brewer');

		// Composite unit objects
		expect(recipe.batch_size).toEqual({ value: 20.0, unit: 'l' });
		expect(recipe.boil.boil_time).toEqual({ value: 60, unit: 'min' });
		expect(recipe.efficiency.brewhouse).toEqual({ value: 72.0, unit: '%' });

		// Fermentable addition
		const fermentables = recipe.ingredients.fermentable_additions;
		expect(fermentables).toHaveLength(1);
		expect(fermentables[0].name).toBe('Pale Ale Malt');
		expect(fermentables[0].amount).toEqual({ value: 4.2, unit: 'kg' });
		expect(fermentables[0].color).toEqual({ value: 2.5, unit: 'SRM' });
		expect(fermentables[0].yield.potential).toEqual({ value: 1.037, unit: 'sg' });

		// Hop additions with native whirlpool support
		const hops = recipe.ingredients.hop_additions;
		expect(hops).toHaveLength(2);

		const galaxy = hops.find((h: { name: string }) => h.name === 'Galaxy');
		expect(galaxy.timing.use).toBe('add_to_whirlpool');
		expect(galaxy.timing.time).toEqual({ value: 15, unit: 'min' });
		expect(galaxy.amount).toEqual({ value: 30, unit: 'g' });
		expect(galaxy.alpha_acid).toEqual({ value: 14.5, unit: '%' });

		const mosaic = hops.find((h: { name: string }) => h.name === 'Mosaic');
		expect(mosaic.timing.use).toBe('add_to_fermentation');
		expect(mosaic.timing.time).toEqual({ value: 3, unit: 'day' });

		// Culture additions
		const cultures = recipe.ingredients.culture_additions;
		expect(cultures).toHaveLength(1);
		expect(cultures[0].name).toBe('US-05');
		expect(cultures[0].attenuation).toEqual({ value: 78.0, unit: '%' });

		// Mash steps
		const steps = recipe.mash.mash_steps;
		expect(steps).toHaveLength(1);
		expect(steps[0].name).toBe('Single Infusion');
		expect(steps[0].step_temperature).toEqual({ value: 66.0, unit: 'C' });
		expect(steps[0].step_time).toEqual({ value: 60, unit: 'min' });
		expect(steps[0].infuse_amount).toEqual({ value: 14.0, unit: 'l' });
	});

	it('reports 100% compatibility for complete recipes without data loss', () => {
		const report = analyzeBeerJsonCompatibility(sampleRecipe);
		expect(report.isCompatible).toBe(true);
		expect(report.warnings).toHaveLength(0);
	});

	it('detects missing hop alpha and fermentable potential in BeerJSON analysis', () => {
		const incompleteRecipe: ExportableRecipe = {
			...sampleRecipe,
			ingredients: [
				{
					name: 'Mystery Grain',
					type: 'Fermentable',
					amount: 4.0,
					unit: 'kg',
					usage: 'Mash'
				},
				{
					name: 'Mystery Hop',
					type: 'Hop',
					amount: 25,
					unit: 'g',
					usage: 'Boil'
				}
			]
		};

		const report = analyzeBeerJsonCompatibility(incompleteRecipe);
		expect(report.isCompatible).toBe(false);
		expect(report.warnings.some((w) => w.code === 'MISSING_HOP_ALPHA')).toBe(true);
		expect(report.warnings.some((w) => w.code === 'MISSING_FERMENTABLE_POTENTIAL')).toBe(true);
	});

	it('serializes specified hop and culture forms and amounts in BeerJSON', () => {
		const formRecipe: ExportableRecipe = {
			...sampleRecipe,
			ingredients: [
				{
					name: 'Saaz Leaf',
					type: 'Hop',
					amount: 50,
					unit: 'g',
					usage: 'Boil',
					alphaAcidPercent: 3.5,
					form: 'Leaf'
				},
				{
					name: 'WLP001 Liquid',
					type: 'Yeast',
					amount: 1,
					unit: 'pkg',
					usage: 'Primary',
					attenuationPercent: 78.0,
					form: 'Liquid'
				}
			]
		};

		const result = serializeBeerJson(formRecipe);
		const parsed = JSON.parse(result.content);
		const recipe = parsed.beerjson.recipes[0];

		const hop = recipe.ingredients.hop_additions[0];
		expect(hop.form).toBe('leaf');

		const culture = recipe.ingredients.culture_additions[0];
		expect(culture.form).toBe('liquid');
		expect(culture.amount).toEqual({ value: 1, unit: 'pkg' });
	});
});
