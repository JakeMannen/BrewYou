import { describe, expect, it } from 'vitest';
import type { ExportableRecipe } from '$lib/exporters/types';
import { analyzeBeerXmlCompatibility, serializeBeerXml } from '$lib/exporters/beerxml';
import { analyzeBeerJsonCompatibility, serializeBeerJson } from '$lib/exporters/beerjson';
import { sanitizeFilename } from '$lib/utils/downloadFile';

describe('RecipeExportModal Logic & Fidelity Tests', () => {
	const cleanRecipe: ExportableRecipe = {
		name: 'Standard Bitter',
		beerStyle: 'Extra Special Bitter',
		description: 'A traditional British ESB.',
		batchSizeLiters: 20.0,
		boilTimeMinutes: 60,
		efficiencyPercent: 75.0,
		originalGravity: 1.05,
		finalGravity: 1.012,
		alcoholByVolume: 5.0,
		bitternessIbu: 35.0,
		colorSrm: 12.0,
		ingredients: [
			{
				name: 'Maris Otter',
				type: 'Fermentable',
				amount: 4.5,
				unit: 'kg',
				usage: 'Mash',
				potentialGravity: 1.038,
				colorSrm: 3.5
			},
			{
				name: 'East Kent Goldings',
				type: 'Hop',
				amount: 35,
				unit: 'g',
				durationMinutes: 60,
				usage: 'Boil',
				alphaAcidPercent: 5.5
			},
			{
				name: 'Wyeast 1968',
				type: 'Yeast',
				amount: 11,
				unit: 'g',
				usage: 'Primary',
				attenuationPercent: 72.0
			}
		],
		mashSteps: [
			{
				stepOrder: 1,
				name: 'Saccharification',
				type: 'Infusion',
				temperatureC: 67.0,
				durationMinutes: 60
			}
		]
	};

	it('reports 100% compatibility for clean recipe under both BeerXML and BeerJSON', () => {
		const xmlReport = analyzeBeerXmlCompatibility(cleanRecipe);
		expect(xmlReport.isCompatible).toBe(true);
		expect(xmlReport.warnings).toHaveLength(0);

		const jsonReport = analyzeBeerJsonCompatibility(cleanRecipe);
		expect(jsonReport.isCompatible).toBe(true);
		expect(jsonReport.warnings).toHaveLength(0);
	});

	it('shows incompatibility warning for whirlpool hop in BeerXML while BeerJSON remains 100% compatible', () => {
		const recipeWithWhirlpool: ExportableRecipe = {
			...cleanRecipe,
			ingredients: [
				...cleanRecipe.ingredients,
				{
					name: 'Fuggles',
					type: 'Hop',
					amount: 30,
					unit: 'g',
					durationMinutes: 20,
					usage: 'Whirlpool',
					alphaAcidPercent: 4.5
				}
			]
		};

		const xmlReport = analyzeBeerXmlCompatibility(recipeWithWhirlpool);
		expect(xmlReport.isCompatible).toBe(false);
		expect(xmlReport.warnings.some((w) => w.code === 'BEERXML_WHIRLPOOL_MAPPED')).toBe(true);
		expect(xmlReport.warnings[0].fallbackText).toContain('whirlpool');

		// BeerJSON natively models whirlpool additions with temperature and duration
		const jsonReport = analyzeBeerJsonCompatibility(recipeWithWhirlpool);
		expect(jsonReport.isCompatible).toBe(true);
		expect(jsonReport.warnings).toHaveLength(0);
	});

	it('flags dry hop day conversion in BeerXML', () => {
		const recipeWithDryHop: ExportableRecipe = {
			...cleanRecipe,
			ingredients: [
				...cleanRecipe.ingredients,
				{
					name: 'Goldings',
					type: 'Hop',
					amount: 25,
					unit: 'g',
					durationMinutes: 3000, // Not an exact multiple of 1440
					usage: 'DryHop',
					alphaAcidPercent: 5.0
				}
			]
		};

		const xmlReport = analyzeBeerXmlCompatibility(recipeWithDryHop);
		expect(xmlReport.warnings.some((w) => w.code === 'BEERXML_DRY_HOP_DAYS')).toBe(true);
	});

	it('generates proper download payloads and filenames for both formats', () => {
		const xmlResult = serializeBeerXml(cleanRecipe);
		expect(xmlResult.format).toBe('beerxml');
		expect(xmlResult.filename).toBe('Standard_Bitter.xml');
		expect(xmlResult.mimeType).toBe('application/xml;charset=utf-8');
		expect(xmlResult.content).toContain('<NAME>Standard Bitter</NAME>');

		const jsonResult = serializeBeerJson(cleanRecipe);
		expect(jsonResult.format).toBe('beerjson');
		expect(jsonResult.filename).toBe('Standard_Bitter.json');
		expect(jsonResult.mimeType).toBe('application/json;charset=utf-8');
		expect(jsonResult.content).toContain('"name": "Standard Bitter"');
	});

	it('sanitizes filenames across edge cases and illegal characters', () => {
		expect(sanitizeFilename('My/Cool\\Beer:Recipe?', 'xml')).toBe('My_Cool_Beer_Recipe.xml');
		expect(sanitizeFilename('CON', 'json')).toBe('recipe_CON.json');
		expect(sanitizeFilename('  Trimmed Name  ', 'xml')).toBe('Trimmed_Name.xml');
		expect(sanitizeFilename('', 'xml')).toBe('recipe.xml');
		expect(sanitizeFilename(null, 'json')).toBe('recipe.json');
	});
});
