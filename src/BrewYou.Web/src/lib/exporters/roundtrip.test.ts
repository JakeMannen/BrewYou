import { describe, expect, it } from 'vitest';
import { serializeBeerXml } from './beerxml';
import { serializeBeerJson } from './beerjson';
import { parseBeerXml } from '$lib/parsers/beerxml';
import { parseBeerJson } from '$lib/parsers/beerjson';
import type { ExportableRecipe } from './types';

describe('Recipe Interoperability & Round-Trip Tests', () => {
	const recipe: ExportableRecipe = {
		name: 'Roundtrip Pale Ale',
		beerStyle: 'American Pale Ale',
		description: 'A crisp pale ale designed to test bidirectional serialization.',
		batchSizeLiters: 20.0,
		boilTimeMinutes: 60,
		efficiencyPercent: 75.0,
		originalGravity: 1.052,
		finalGravity: 1.011,
		alcoholByVolume: 5.4,
		bitternessIbu: 38.0,
		colorSrm: 6.5,
		brewer: 'Interoperability Tester',
		ingredients: [
			{
				name: 'Pale Malt',
				type: 'Fermentable',
				amount: 4.5,
				unit: 'kg',
				usage: 'Mash',
				potentialGravity: 1.037,
				colorSrm: 3.0
			},
			{
				name: 'Cascade',
				type: 'Hop',
				amount: 35,
				unit: 'g',
				durationMinutes: 60,
				usage: 'Boil',
				alphaAcidPercent: 6.5
			},
			{
				name: 'SafAle US-05',
				type: 'Yeast',
				amount: 11,
				unit: 'g',
				usage: 'Primary',
				attenuationPercent: 78.0
			}
		],
		mashSteps: [
			{
				stepOrder: 1,
				name: 'Conversion Rest',
				type: 'Infusion',
				temperatureC: 66.0,
				durationMinutes: 60,
				infuseAmountLiters: 13.5
			}
		],
		fermentationSteps: [
			{
				stepOrder: 1,
				name: 'Primary',
				type: 'Primary',
				targetTemperatureC: 19.0,
				durationDays: 14
			}
		]
	};

	it('preserves recipe data in BeerXML serialize -> parse round-trip', () => {
		const exported = serializeBeerXml(recipe);
		const parsed = parseBeerXml(exported.content);

		expect(parsed).toHaveLength(1);
		const r = parsed[0];

		expect(r.name).toBe('Roundtrip Pale Ale');
		expect(r.beerStyle).toBe('American Pale Ale');
		expect(r.batchSizeLiters).toBe(20.0);
		expect(r.boilTimeMinutes).toBe(60);
		expect(r.efficiencyPercent).toBe(75.0);
		expect(r.description).toBe('A crisp pale ale designed to test bidirectional serialization.');

		// Ingredients
		const grain = r.ingredients.find((i) => i.name === 'Pale Malt');
		expect(grain).toBeDefined();
		expect(grain?.amount).toBe(4.5);
		expect(grain?.unit).toBe('kg');

		const hop = r.ingredients.find((i) => i.name === 'Cascade');
		expect(hop).toBeDefined();
		expect(hop?.amount).toBe(35); // 0.035 kg -> 35 g
		expect(hop?.unit).toBe('g');

		const yeast = r.ingredients.find((i) => i.name === 'SafAle US-05');
		expect(yeast).toBeDefined();

		// Mash steps
		expect(r.mashSteps).toHaveLength(1);
		expect(r.mashSteps[0].name).toBe('Conversion Rest');
		expect(r.mashSteps[0].temperatureC).toBe(66.0);
		expect(r.mashSteps[0].durationMinutes).toBe(60);

		// Fermentation steps
		expect(r.fermentationSteps).toHaveLength(1);
		expect(r.fermentationSteps[0].targetTemperatureC).toBe(19.0);
		expect(r.fermentationSteps[0].durationDays).toBe(14);
	});

	it('preserves recipe data in BeerJSON serialize -> parse round-trip', () => {
		const exported = serializeBeerJson(recipe);
		const parsed = parseBeerJson(exported.content);

		expect(parsed).toHaveLength(1);
		const r = parsed[0];

		expect(r.name).toBe('Roundtrip Pale Ale');
		expect(r.beerStyle).toBe('American Pale Ale');
		expect(r.batchSizeLiters).toBe(20.0);
		expect(r.boilTimeMinutes).toBe(60);
		expect(r.efficiencyPercent).toBe(75.0);

		// Ingredients
		const grain = r.ingredients.find((i) => i.name === 'Pale Malt');
		expect(grain).toBeDefined();
		expect(grain?.amount).toBe(4.5);

		const hop = r.ingredients.find((i) => i.name === 'Cascade');
		expect(hop).toBeDefined();
		expect(hop?.amount).toBe(35);

		const yeast = r.ingredients.find((i) => i.name === 'SafAle US-05');
		expect(yeast).toBeDefined();

		// Mash steps
		expect(r.mashSteps).toHaveLength(1);
		expect(r.mashSteps[0].name).toBe('Conversion Rest');
		expect(r.mashSteps[0].temperatureC).toBe(66.0);
		expect(r.mashSteps[0].durationMinutes).toBe(60);

		// Fermentation steps
		expect(r.fermentationSteps).toHaveLength(1);
		expect(r.fermentationSteps[0].name).toBe('Primary');
		expect(r.fermentationSteps[0].targetTemperatureC).toBe(19.0);
		expect(r.fermentationSteps[0].durationDays).toBe(14);
	});

	it('preserves multi-step lager recipe in BeerXML and BeerJSON round-trips', () => {
		const lagerRecipe: ExportableRecipe = {
			name: 'Bavarian Dunkel',
			beerStyle: 'Munich Dunkel',
			batchSizeLiters: 20.0,
			boilTimeMinutes: 60,
			efficiencyPercent: 75.0,
			ingredients: [
				{
					name: 'Munich Malt',
					type: 'Fermentable',
					amount: 4.5,
					unit: 'kg',
					usage: 'Mash'
				},
				{
					name: 'Hallertauer Mittelfrüh',
					type: 'Hop',
					amount: 30,
					unit: 'g',
					durationMinutes: 60,
					usage: 'Boil',
					alphaAcidPercent: 4.2
				},
				{
					name: 'Saflager W-34/70',
					type: 'Yeast',
					amount: 23,
					unit: 'g',
					usage: 'Primary',
					attenuationPercent: 82.0
				}
			],
			mashSteps: [
				{
					stepOrder: 1,
					name: 'Mäskning',
					type: 'Infusion',
					temperatureC: 67.0,
					durationMinutes: 60,
					infuseAmountLiters: 15.0
				},
				{
					stepOrder: 2,
					name: 'Utmäskning',
					type: 'Temperature',
					temperatureC: 78.0,
					durationMinutes: 10
				}
			],
			fermentationSteps: [
				{
					stepOrder: 1,
					name: 'Primary Fermentation',
					type: 'Primary',
					targetTemperatureC: 12.0,
					durationDays: 21
				}
			]
		};

		// 1. BeerXML roundtrip
		const xmlExport = serializeBeerXml(lagerRecipe);
		expect(xmlExport.content).toContain('<TYPE>Lager</TYPE>');
		const xmlParsed = parseBeerXml(xmlExport.content)[0];
		expect(xmlParsed.mashSteps).toHaveLength(2);
		expect(xmlParsed.mashSteps[0].temperatureC).toBe(67.0);
		expect(xmlParsed.mashSteps[1].temperatureC).toBe(78.0);
		expect(xmlParsed.fermentationSteps[0].targetTemperatureC).toBe(12.0);
		expect(xmlParsed.fermentationSteps[0].durationDays).toBe(21);

		// 2. BeerJSON roundtrip
		const jsonExport = serializeBeerJson(lagerRecipe);
		expect(jsonExport.content).toContain('"type": "lager"');
		const jsonParsed = parseBeerJson(jsonExport.content)[0];
		expect(jsonParsed.mashSteps).toHaveLength(2);
		expect(jsonParsed.mashSteps[0].temperatureC).toBe(67.0);
		expect(jsonParsed.mashSteps[1].temperatureC).toBe(78.0);
		expect(jsonParsed.fermentationSteps[0].targetTemperatureC).toBe(12.0);
		expect(jsonParsed.fermentationSteps[0].durationDays).toBe(21);
	});

	it('preserves FirstWort hops, DryHop days (canonical minutes), and MISC weight/volume in BeerXML & BeerJSON round-trips', () => {
		const complexRecipe: ExportableRecipe = {
			name: 'First Wort & Dry Hop Precision IPA',
			beerStyle: 'American IPA',
			batchSizeLiters: 20.0,
			boilTimeMinutes: 60,
			efficiencyPercent: 75.0,
			ingredients: [
				{
					name: 'Pilsner Malt',
					type: 'Fermentable',
					amount: 5.0,
					unit: 'kg',
					usage: 'Mash',
					potentialGravity: 1.037,
					colorSrm: 2.0
				},
				{
					name: 'Magnum',
					type: 'Hop',
					amount: 20,
					unit: 'g',
					durationMinutes: 60,
					usage: 'FirstWort',
					alphaAcidPercent: 12.0,
					form: 'Leaf'
				},
				{
					name: 'Citra',
					type: 'Hop',
					amount: 60,
					unit: 'g',
					durationMinutes: 4320, // 3 days in canonical minutes
					usage: 'DryHop',
					alphaAcidPercent: 12.5,
					form: 'Pellet'
				},
				{
					name: 'Irish Moss',
					type: 'Other',
					amount: 5,
					unit: 'g',
					durationMinutes: 15,
					usage: 'Boil',
					notes: 'Kettle fining'
				},
				{
					name: 'Lactic Acid 80%',
					type: 'Other',
					amount: 12,
					unit: 'ml',
					durationMinutes: 60,
					usage: 'Mash',
					notes: 'Mash pH adjustment'
				}
			],
			mashSteps: [],
			fermentationSteps: []
		};

		// BeerXML roundtrip
		const xmlOut = serializeBeerXml(complexRecipe);
		expect(xmlOut.content).toContain('<USE>First Wort</USE>');
		expect(xmlOut.content).toContain('<AMOUNT_IS_WEIGHT>TRUE</AMOUNT_IS_WEIGHT>');
		expect(xmlOut.content).toContain('<AMOUNT_IS_WEIGHT>FALSE</AMOUNT_IS_WEIGHT>');

		const xmlParsed = parseBeerXml(xmlOut.content)[0];
		const xmlFwh = xmlParsed.ingredients.find((i) => i.name === 'Magnum');
		expect(xmlFwh?.usage).toBe('FirstWort');
		expect(xmlFwh?.durationMinutes).toBe(60);

		const xmlDry = xmlParsed.ingredients.find((i) => i.name === 'Citra');
		expect(xmlDry?.usage).toBe('DryHop');
		expect(xmlDry?.durationMinutes).toBe(4320);

		const xmlMoss = xmlParsed.ingredients.find((i) => i.name === 'Irish Moss');
		expect(xmlMoss?.amount).toBeCloseTo(5, 2);
		expect(xmlMoss?.unit).toBe('g');
		expect(xmlMoss?.usage).toBe('Boil');

		const xmlAcid = xmlParsed.ingredients.find((i) => i.name === 'Lactic Acid 80%');
		expect(xmlAcid?.amount).toBeCloseTo(12, 2);
		expect(xmlAcid?.unit).toBe('ml');
		expect(xmlAcid?.usage).toBe('Mash');

		// BeerJSON roundtrip
		const jsonOut = serializeBeerJson(complexRecipe);
		expect(jsonOut.content).toContain('"use": "add_to_first_wort"');

		const jsonParsed = parseBeerJson(jsonOut.content)[0];
		const jsonFwh = jsonParsed.ingredients.find((i) => i.name === 'Magnum');
		expect(jsonFwh?.usage).toBe('FirstWort');
		expect(jsonFwh?.durationMinutes).toBe(60);

		const jsonDry = jsonParsed.ingredients.find((i) => i.name === 'Citra');
		expect(jsonDry?.usage).toBe('DryHop');
		expect(jsonDry?.durationMinutes).toBe(4320);

		const jsonMoss = jsonParsed.ingredients.find((i) => i.name === 'Irish Moss');
		expect(jsonMoss?.amount).toBe(5);
		expect(jsonMoss?.unit).toBe('g');
		expect(jsonMoss?.usage).toBe('Boil');

		const jsonAcid = jsonParsed.ingredients.find((i) => i.name === 'Lactic Acid 80%');
		expect(jsonAcid?.amount).toBe(12);
		expect(jsonAcid?.unit).toBe('ml');
		expect(jsonAcid?.usage).toBe('Mash');
	});
});
