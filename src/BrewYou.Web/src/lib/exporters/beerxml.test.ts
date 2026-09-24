import { describe, expect, it } from 'vitest';
import { serializeBeerXml, analyzeBeerXmlCompatibility, potentialToYieldPercent } from './beerxml';
import type { ExportableRecipe } from './types';

describe('BeerXML 1.0 Exporter', () => {
	const sampleRecipe: ExportableRecipe = {
		name: 'Centennial Blonde',
		beerStyle: 'Blonde Ale',
		description: 'A clean, crisp & refreshing blonde ale with "Centennial" hops.',
		batchSizeLiters: 20.0,
		boilTimeMinutes: 60,
		efficiencyPercent: 75.0,
		originalGravity: 1.045,
		finalGravity: 1.009,
		alcoholByVolume: 4.7,
		bitternessIbu: 22.5,
		colorSrm: 4.2,
		brewer: 'Master Brewer',
		ingredients: [
			{
				name: 'Pale Malt (2-Row)',
				type: 'Fermentable',
				amount: 3.8,
				unit: 'kg',
				usage: 'Mash',
				potentialGravity: 1.037,
				colorSrm: 3.0
			},
			{
				name: 'Carahell',
				type: 'Fermentable',
				amount: 0.4,
				unit: 'kg',
				usage: 'Mash',
				potentialGravity: 1.035,
				colorSrm: 10.0
			},
			{
				name: 'Centennial',
				type: 'Hop',
				amount: 20,
				unit: 'g',
				durationMinutes: 60,
				usage: 'Boil',
				alphaAcidPercent: 10.0
			},
			{
				name: 'Cascade',
				type: 'Hop',
				amount: 30,
				unit: 'g',
				durationMinutes: 4320, // 3 days in minutes
				usage: 'DryHop',
				alphaAcidPercent: 6.0
			},
			{
				name: 'SafAle US-05',
				type: 'Yeast',
				amount: 11,
				unit: 'g',
				usage: 'Primary',
				attenuationPercent: 80.0
			}
		],
		mashSteps: [
			{
				stepOrder: 1,
				name: 'Saccharification Rest',
				type: 'Infusion',
				temperatureC: 65.5,
				durationMinutes: 60,
				infuseAmountLiters: 12.5,
				notes: 'Main starch conversion rest'
			}
		]
	};

	it('calculates dry basis yield percentage from potential gravity correctly', () => {
		// (1.037 - 1.0) * 1000 / 0.46 = 37 / 0.46 = 80.43% -> 80.4%
		expect(potentialToYieldPercent(1.037)).toBe(80.4);
		expect(potentialToYieldPercent(1.035)).toBe(76.1);
		expect(potentialToYieldPercent(null)).toBe(78.0);
	});

	it('serializes a complete recipe to valid BeerXML 1.0 document', () => {
		const result = serializeBeerXml(sampleRecipe);

		expect(result.format).toBe('beerxml');
		expect(result.fileExtension).toBe('xml');
		expect(result.filename).toBe('Centennial_Blonde.xml');
		expect(result.mimeType).toBe('application/xml;charset=utf-8');

		const xml = result.content;
		expect(xml).toContain('<?xml version="1.0" encoding="UTF-8"?>');
		expect(xml).toContain('<RECIPES>');
		expect(xml).toContain('<RECIPE>');
		expect(xml).toContain('<NAME>Centennial Blonde</NAME>');
		expect(xml).toContain('<VERSION>1</VERSION>');
		expect(xml).toContain('<TYPE>All Grain</TYPE>');
		expect(xml).toContain('<BREWER>Master Brewer</BREWER>');
		expect(xml).toContain('<BATCH_SIZE>20</BATCH_SIZE>');
		expect(xml).toContain('<BOIL_TIME>60</BOIL_TIME>');
		expect(xml).toContain('<EFFICIENCY>75</EFFICIENCY>');
		expect(xml).toContain('<OG>1.045</OG>');
		expect(xml).toContain('<FG>1.009</FG>');

		// Escaping check
		expect(xml).toContain('&amp;');
		expect(xml).toContain('&quot;Centennial&quot;');

		// Fermentable checks
		expect(xml).toContain('<FERMENTABLES>');
		expect(xml).toContain('<NAME>Pale Malt (2-Row)</NAME>');
		expect(xml).toContain('<AMOUNT>3.8</AMOUNT>');
		expect(xml).toContain('<YIELD>80.4</YIELD>');
		expect(xml).toContain('<COLOR>3</COLOR>');

		// Hop conversion checks: grams to kilograms (20g -> 0.02)
		expect(xml).toContain('<HOPS>');
		expect(xml).toContain('<NAME>Centennial</NAME>');
		expect(xml).toContain('<AMOUNT>0.02</AMOUNT>');
		expect(xml).toContain('<ALPHA>10</ALPHA>');
		expect(xml).toContain('<USE>Boil</USE>');
		expect(xml).toContain('<TIME>60</TIME>');

		// Dry hop time in days: 4320 mins / 1440 = 3 days
		expect(xml).toContain('<NAME>Cascade</NAME>');
		expect(xml).toContain('<AMOUNT>0.03</AMOUNT>');
		expect(xml).toContain('<USE>Dry Hop</USE>');
		expect(xml).toContain('<TIME>3</TIME>');

		// Mash step checks
		expect(xml).toContain('<MASH>');
		expect(xml).toContain('<MASH_STEPS>');
		expect(xml).toContain('<NAME>Saccharification Rest</NAME>');
		expect(xml).toContain('<TYPE>Infusion</TYPE>');
		expect(xml).toContain('<STEP_TEMP>65.5</STEP_TEMP>');
		expect(xml).toContain('<STEP_TIME>60</STEP_TIME>');
		expect(xml).toContain('<INFUSE_AMOUNT>12.5</INFUSE_AMOUNT>');
	});

	it('detects whirlpool hop incompatibility and warns the user', () => {
		const recipeWithWhirlpool: ExportableRecipe = {
			...sampleRecipe,
			ingredients: [
				...sampleRecipe.ingredients,
				{
					name: 'Citra',
					type: 'Hop',
					amount: 40,
					unit: 'g',
					durationMinutes: 20,
					usage: 'Whirlpool',
					alphaAcidPercent: 12.5
				}
			]
		};

		const report = analyzeBeerXmlCompatibility(recipeWithWhirlpool);
		expect(report.isCompatible).toBe(false);
		expect(report.warnings.some((w) => w.code === 'BEERXML_WHIRLPOOL_MAPPED')).toBe(true);

		const result = serializeBeerXml(recipeWithWhirlpool);
		expect(result.content).toContain('<NAME>Citra</NAME>');
		// Whirlpool mapped to Aroma in BeerXML
		expect(result.content).toContain('<USE>Aroma</USE>');
	});

	it('detects missing hop alpha and missing fermentable potential warnings', () => {
		const incompleteRecipe: ExportableRecipe = {
			...sampleRecipe,
			ingredients: [
				{
					name: 'Mystery Grain',
					type: 'Fermentable',
					amount: 4.0,
					unit: 'kg',
					usage: 'Mash'
					// potentialGravity missing
				},
				{
					name: 'Mystery Hop',
					type: 'Hop',
					amount: 25,
					unit: 'g',
					usage: 'Boil'
					// alphaAcidPercent missing
				}
			]
		};

		const report = analyzeBeerXmlCompatibility(incompleteRecipe);
		expect(report.warnings.some((w) => w.code === 'MISSING_HOP_ALPHA')).toBe(true);
		expect(report.warnings.some((w) => w.code === 'MISSING_FERMENTABLE_POTENTIAL')).toBe(true);

		const result = serializeBeerXml(incompleteRecipe);
		// Verified fallbacks are used
		expect(result.content).toContain('<ALPHA>5</ALPHA>');
		expect(result.content).toContain('<YIELD>78</YIELD>');
	});

	it('sanitizes filenames and escapes special characters in output', () => {
		const maliciousRecipe: ExportableRecipe = {
			...sampleRecipe,
			name: 'IPA <script>alert(1)</script> / Special',
			description: 'Test <tag> & "quotes"'
		};

		const result = serializeBeerXml(maliciousRecipe);
		expect(result.filename).not.toContain('<');
		expect(result.filename).not.toContain('>');
		expect(result.filename).not.toContain('/');

		expect(result.content).toContain('&lt;script&gt;alert(1)&lt;/script&gt;');
		expect(result.content).toContain('&lt;tag&gt; &amp; &quot;quotes&quot;');
	});

	it('exports specified hop forms and yeast forms with dynamic amounts', () => {
		const formRecipe: ExportableRecipe = {
			...sampleRecipe,
			ingredients: [
				{
					name: 'Cascade Whole Cone',
					type: 'Hop',
					amount: 40,
					unit: 'g',
					durationMinutes: 60,
					usage: 'Boil',
					alphaAcidPercent: 6.0,
					form: 'Leaf'
				},
				{
					name: 'Wyeast 3068 Weihenstephan',
					type: 'Yeast',
					amount: 1,
					unit: 'pkg',
					usage: 'Primary',
					attenuationPercent: 74.0,
					form: 'Liquid'
				}
			]
		};

		const result = serializeBeerXml(formRecipe);
		expect(result.content).toContain('<FORM>Leaf</FORM>');
		expect(result.content).toContain('<FORM>Liquid</FORM>');
		expect(result.content).toContain('<AMOUNT>0.125</AMOUNT>');
		expect(result.content).toContain('<AMOUNT_IS_WEIGHT>FALSE</AMOUNT_IS_WEIGHT>');
	});
});
