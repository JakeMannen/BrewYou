import { describe, expect, it } from 'vitest';
import { parseBeerJson } from './beerjson';

describe('BeerJSON Parser', () => {
	it('parses standard BeerJSON v1 format', () => {
		const json = JSON.stringify({
			beerjson: {
				version: 1,
				recipes: [
					{
						name: 'Juicy Hazy IPA',
						style: { name: 'Hazy IPA' },
						description: 'New England style IPA.',
						batch_size: { value: 20, unit: 'l' },
						boil: { boil_time: { value: 60, unit: 'min' } },
						efficiency: { brewhouse: 72 },
						ingredients: {
							fermentable_additions: [
								{ name: 'Pilsner Malt', amount: { value: 5.0, unit: 'kg' }, color: 3.5 }
							],
							hop_additions: [
								{
									name: 'Citra',
									amount: { value: 50, unit: 'g' },
									timing: { time: { value: 15, unit: 'min' } },
									use: 'whirlpool',
									alpha_acid: 13.5
								}
							],
							culture_additions: [{ name: 'London Ale III', attenuation: 76 }]
						}
					}
				]
			}
		});

		const recipes = parseBeerJson(json);
		expect(recipes).toHaveLength(1);

		const recipe = recipes[0];
		expect(recipe.name).toBe('Juicy Hazy IPA');
		expect(recipe.beerStyle).toBe('Hazy IPA');
		expect(recipe.batchSizeLiters).toBe(20.0);
		expect(recipe.boilTimeMinutes).toBe(60);
		expect(recipe.efficiencyPercent).toBe(72.0);

		expect(recipe.ingredients).toHaveLength(3);
		const hop = recipe.ingredients.find((i) => i.type === 'Hop');
		expect(hop).toBeDefined();
		expect(hop!.name).toBe('Citra');
		expect(hop!.amount).toBe(50);
		expect(hop!.usage).toBe('Whirlpool');
		expect(hop!.alphaAcidPercent).toBe(13.5);
	});

	it('converts US Imperial units (gal, lb, oz) to canonical metric units', () => {
		const json = JSON.stringify({
			recipes: [
				{
					name: 'Imperial Converted Pale Ale',
					batch_size: { value: 5, unit: 'gal' }, // ~18.9 L
					ingredients: {
						fermentable_additions: [{ name: '2-Row', amount: { value: 10, unit: 'lb' } }], // ~4.54 kg
						hop_additions: [{ name: 'Cascade', amount: { value: 2, unit: 'oz' }, use: 'boil' }] // ~56.7 g
					}
				}
			]
		});

		const recipes = parseBeerJson(json);
		expect(recipes).toHaveLength(1);
		const recipe = recipes[0];

		expect(recipe.batchSizeLiters).toBeCloseTo(18.9, 0.1);
		const grain = recipe.ingredients.find((i) => i.type === 'Fermentable');
		expect(grain!.amount).toBeCloseTo(4.54, 0.1);
		expect(grain!.unit).toBe('kg');

		const hop = recipe.ingredients.find((i) => i.type === 'Hop');
		expect(hop!.amount).toBeCloseTo(56.7, 0.1);
		expect(hop!.unit).toBe('g');
	});

	it('parses multi-step mash profile with Fahrenheit conversion from BeerJSON', () => {
		const json = JSON.stringify({
			beerjson: {
				version: 1,
				recipes: [
					{
						name: 'Munich Dunkel',
						mash: {
							mash_steps: [
								{
									name: 'Protein Rest',
									type: 'infusion',
									step_temperature: { value: 122, unit: 'F' }, // 50°C
									step_time: { value: 25, unit: 'min' }
								},
								{
									name: 'Saccharification',
									type: 'temperature',
									step_temperature: { value: 65, unit: 'C' },
									step_time: { value: 45, unit: 'min' }
								},
								{
									name: 'Mash Out',
									type: 'temperature',
									step_temperature: { value: 168, unit: 'F' }, // ~75.6°C
									step_time: { value: 10, unit: 'min' }
								}
							]
						}
					}
				]
			}
		});

		const recipes = parseBeerJson(json);
		expect(recipes).toHaveLength(1);
		const steps = recipes[0].mashSteps;
		expect(steps).toHaveLength(3);

		expect(steps[0].stepOrder).toBe(1);
		expect(steps[0].name).toBe('Protein Rest');
		expect(steps[0].type).toBe('Infusion');
		expect(steps[0].temperatureC).toBe(50.0);
		expect(steps[0].durationMinutes).toBe(25);

		expect(steps[1].stepOrder).toBe(2);
		expect(steps[1].name).toBe('Saccharification');
		expect(steps[1].type).toBe('Temperature');
		expect(steps[1].temperatureC).toBe(65.0);
		expect(steps[1].durationMinutes).toBe(45);

		expect(steps[2].stepOrder).toBe(3);
		expect(steps[2].name).toBe('Mash Out');
		expect(steps[2].type).toBe('Temperature');
		expect(steps[2].temperatureC).toBeCloseTo(75.6, 0.1);
		expect(steps[2].durationMinutes).toBe(10);
	});

	it('strips prototype pollution keys safely', () => {
		const maliciousJson =
			'{"beerjson": {"version": 1, "recipes": [{"name": "Safe Recipe"}]}, "__proto__": {"polluted": true}}';
		const recipes = parseBeerJson(maliciousJson);
		expect(recipes).toHaveLength(1);
		expect((Object.prototype as unknown as Record<string, unknown>).polluted).toBeUndefined();
	});

	it('normalizes EBC color in fermentables to canonical SRM', () => {
		const json = JSON.stringify({
			recipes: [
				{
					name: 'German Helles',
					ingredients: {
						fermentable_additions: [
							{
								name: 'Pilsner',
								amount: { value: 5, unit: 'kg' },
								color: { value: 7.4, unit: 'ebc' }
							}
						]
					}
				}
			]
		});

		const recipes = parseBeerJson(json);
		const grain = recipes[0].ingredients[0];
		expect(grain.colorSrm).toBeCloseTo(3.8, 1); // 7.4 / 1.97 ≈ 3.76 -> 3.8
	});

	it('throws an error for empty or invalid JSON', () => {
		expect(() => parseBeerJson('')).toThrow(/empty/);
		expect(() => parseBeerJson('{ invalid json ')).toThrow(/Malformed JSON/);
	});

	it('parses fermentation profile and steps from BeerJSON', () => {
		const json = JSON.stringify({
			beerjson: {
				version: 2.06,
				recipes: [
					{
						name: 'Saison Dupont Clone',
						fermentation: {
							name: 'Saison Profile',
							fermentation_steps: [
								{
									name: 'Pitch',
									type: 'primary',
									step_temperature: { value: 20.0, unit: 'C' },
									step_time: { value: 3, unit: 'day' }
								},
								{
									name: 'Free Rise',
									type: 'free rise',
									step_temperature: { value: 28.0, unit: 'C' },
									step_time: { value: 10, unit: 'day' },
									ramp_time: { value: 120, unit: 'min' },
									free_rise_gravity: '1.020'
								},
								{
									name: 'Cold Conditioning',
									type: 'cold crash',
									step_temperature: { value: 2.0, unit: 'C' },
									step_time: { value: 5, unit: 'day' }
								}
							]
						}
					}
				]
			}
		});

		const recipes = parseBeerJson(json);
		expect(recipes).toHaveLength(1);
		const fSteps = recipes[0].fermentationSteps;
		expect(fSteps).toBeDefined();
		expect(fSteps).toHaveLength(3);

		expect(fSteps[0].name).toBe('Pitch');
		expect(fSteps[0].type).toBe('Primary');
		expect(fSteps[0].targetTemperatureC).toBe(20.0);
		expect(fSteps[0].durationDays).toBe(3);

		expect(fSteps[1].name).toBe('Free Rise');
		expect(fSteps[1].type).toBe('FreeRise');
		expect(fSteps[1].targetTemperatureC).toBe(28.0);
		expect(fSteps[1].durationDays).toBe(10);
		expect(fSteps[1].rampTimeHours).toBe(2);
		expect(fSteps[1].triggerGravity).toBe(1.02);

		expect(fSteps[2].name).toBe('Cold Conditioning');
		expect(fSteps[2].type).toBe('ColdCrash');
		expect(fSteps[2].targetTemperatureC).toBe(2.0);
		expect(fSteps[2].durationDays).toBe(5);
	});

	it('parses timing.use, alpha_acid objects, attenuation objects, and fermentable yield potential from BeerJSON 2.x', () => {
		const json = JSON.stringify({
			beerjson: {
				version: 2.06,
				recipes: [
					{
						name: 'Modern West Coast IPA',
						ingredients: {
							fermentable_additions: [
								{
									name: 'Pilsner Malt',
									amount: { value: 4.5, unit: 'kg' },
									yield: {
										potential: { value: 1.038, unit: 'sg' },
										fine_grind: { value: 82.5, unit: '%' }
									}
								},
								{
									name: 'Vienna Malt',
									amount: { value: 0.5, unit: 'kg' },
									yield: {
										fine_grind: { value: 79.0, unit: '%' }
									}
								}
							],
							hop_additions: [
								{
									name: 'Mosaic',
									amount: { value: 60, unit: 'g' },
									alpha_acid: { value: 12.5, unit: '%' },
									timing: {
										use: 'add_to_whirlpool',
										duration: { value: 20, unit: 'min' }
									}
								},
								{
									name: 'Simcoe',
									amount: { value: 50, unit: 'g' },
									alpha_acid: { value: 13.0, unit: '%' },
									timing: {
										use: 'add_to_fermentation',
										time: { value: 4, unit: 'day' }
									}
								}
							],
							culture_additions: [
								{
									name: 'SafAle US-05',
									attenuation: { value: 81.0, unit: '%' }
								}
							]
						}
					}
				]
			}
		});

		const recipes = parseBeerJson(json);
		expect(recipes).toHaveLength(1);
		const recipe = recipes[0];

		// Fermentable potential check
		const pilsner = recipe.ingredients.find((i) => i.name === 'Pilsner Malt');
		expect(pilsner?.potentialGravity).toBe(1.038);

		const vienna = recipe.ingredients.find((i) => i.name === 'Vienna Malt');
		// 1.0 + (79.0 * 0.46) / 1000 = 1.03634 -> 1.036
		expect(vienna?.potentialGravity).toBe(1.036);

		// Hop timing and alpha acid check
		const mosaic = recipe.ingredients.find((i) => i.name === 'Mosaic');
		expect(mosaic?.usage).toBe('Whirlpool');
		expect(mosaic?.alphaAcidPercent).toBe(12.5);
		expect(mosaic?.durationMinutes).toBe(20);

		const simcoe = recipe.ingredients.find((i) => i.name === 'Simcoe');
		expect(simcoe?.usage).toBe('DryHop');
		expect(simcoe?.alphaAcidPercent).toBe(13.0);
		expect(simcoe?.durationMinutes).toBe(5760); // 4 days * 1440 min/day

		// Yeast attenuation check
		const yeast = recipe.ingredients.find((i) => i.name === 'SafAle US-05');
		expect(yeast?.attenuationPercent).toBe(81.0);
	});

	it('detects lager style in fallback fermentation step and sets 12°C', () => {
		const json = JSON.stringify({
			beerjson: {
				version: 2.06,
				recipes: [
					{
						name: 'Franconian Rotbier',
						style: { name: 'Kellerbier: Amber Kellerbier' },
						ingredients: {
							fermentable_additions: [{ name: 'Munich Malt', amount: { value: 4, unit: 'kg' } }]
						}
					}
				]
			}
		});

		const recipes = parseBeerJson(json);
		expect(recipes[0].fermentationSteps).toHaveLength(1);
		expect(recipes[0].fermentationSteps[0].targetTemperatureC).toBe(12.0);
		expect(recipes[0].fermentationSteps[0].durationDays).toBe(21);
	});

	it('normalizes fermentable potentials expressed in PPG to specific gravity (SG)', () => {
		const json = JSON.stringify({
			beerjson: {
				version: 2.06,
				recipes: [
					{
						name: 'PPG Test Beer',
						ingredients: {
							fermentable_additions: [
								{
									name: 'Maris Otter',
									amount: { value: 5, unit: 'kg' },
									yield: { potential: { value: 38, unit: 'ppg' } }
								},
								{
									name: 'Crystal 60',
									amount: { value: 0.5, unit: 'kg' },
									potential: 34
								}
							]
						}
					}
				]
			}
		});

		const recipes = parseBeerJson(json);
		const maris = recipes[0].ingredients.find((i) => i.name === 'Maris Otter');
		const crystal = recipes[0].ingredients.find((i) => i.name === 'Crystal 60');

		expect(maris?.potentialGravity).toBe(1.038);
		expect(crystal?.potentialGravity).toBe(1.034);
	});

	it('parses hop forms and culture forms with dynamic amounts from BeerJSON', () => {
		const json = JSON.stringify({
			beerjson: {
				version: 2.06,
				recipes: [
					{
						name: 'Form & Culture Test',
						ingredients: {
							hop_additions: [
								{ name: 'Mosaic Pellet', form: 'pellet', amount: { value: 30, unit: 'g' } },
								{ name: 'Saaz Whole Leaf', form: 'leaf', amount: { value: 40, unit: 'g' } }
							],
							culture_additions: [
								{ name: 'US-05 Dry', form: 'dry', amount: { value: 23, unit: 'g' } },
								{ name: 'WLP001 Liquid', form: 'liquid', amount: { value: 1, unit: 'pkg' } }
							]
						}
					}
				]
			}
		});

		const recipes = parseBeerJson(json);
		const hops = recipes[0].ingredients.filter((i) => i.type === 'Hop');
		expect(hops[0].form).toBe('Pellet');
		expect(hops[1].form).toBe('Leaf');

		const cultures = recipes[0].ingredients.filter((i) => i.type === 'Yeast');
		expect(cultures[0].form).toBe('Dry');
		expect(cultures[0].amount).toBe(23);
		expect(cultures[0].unit).toBe('g');

		expect(cultures[1].form).toBe('Liquid');
		expect(cultures[1].amount).toBe(1);
		expect(cultures[1].unit).toBe('pkg');
	});
});
