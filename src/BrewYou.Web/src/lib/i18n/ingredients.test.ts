import { describe, expect, it } from 'vitest';
import { i18n } from './index.svelte';
import {
	normalizeIngredientKey,
	getLocalizedIngredientName,
	getLocalizedIngredientDescription,
	getLocalizedIngredientType,
	getLocalizedIngredientForm
} from './ingredients';

describe('Ingredient Localization Engine', () => {
	it('normalizes complex ingredient names into standard keys', () => {
		expect(normalizeIngredientKey('Pale Malt (2-Row)')).toBe('pale_malt_2_row');
		expect(normalizeIngredientKey('Caramel / Crystal 60L')).toBe('caramel_crystal_60l');
		expect(normalizeIngredientKey('Hallertau Mittelfrüh')).toBe('hallertau_mittelfruh');
		expect(normalizeIngredientKey('Fermentis SafAle BE-256 (Abbaye)')).toBe(
			'fermentis_safale_be_256_abbaye'
		);
		expect(normalizeIngredientKey('Columbus / CTZ')).toBe('columbus_ctz');
	});

	it('localizes ingredient types in both English and Swedish', () => {
		i18n.setLocale('en');
		expect(getLocalizedIngredientType('Fermentable')).toBe('Fermentable');
		expect(getLocalizedIngredientType('Hop')).toBe('Hop');
		expect(getLocalizedIngredientType('Yeast')).toBe('Yeast');
		expect(getLocalizedIngredientType('Other')).toBe('Other');

		i18n.setLocale('sv');
		expect(getLocalizedIngredientType('Fermentable')).toBe('Malt & Fermenterbart');
		expect(getLocalizedIngredientType('Hop')).toBe('Humle');
		expect(getLocalizedIngredientType('Yeast')).toBe('Jäst');
		expect(getLocalizedIngredientType('Other')).toBe('Övrigt');
	});

	it('localizes ingredient names with fallback support', () => {
		const pilsner = { name: 'Pilsner Malt' };
		const customMalt = { name: 'My Backyard Grain' };

		i18n.setLocale('en');
		expect(getLocalizedIngredientName(pilsner)).toBe('Pilsner Malt');
		expect(getLocalizedIngredientName(customMalt)).toBe('My Backyard Grain');

		i18n.setLocale('sv');
		expect(getLocalizedIngredientName(pilsner)).toBe('Pilsnermalt');
		expect(getLocalizedIngredientName(customMalt)).toBe('My Backyard Grain');
	});

	it('localizes ingredient descriptions with fallback support', () => {
		const citra = {
			name: 'Citra',
			description:
				'Intense citrus and tropical fruit punch (mango, passionfruit, lime, grapefruit) aromas.'
		};
		const customHop = {
			name: 'Unknown Wild Hop',
			description: 'Found in the forest.'
		};

		i18n.setLocale('en');
		expect(getLocalizedIngredientDescription(citra)).toContain('tropical fruit punch');
		expect(getLocalizedIngredientDescription(customHop)).toBe('Found in the forest.');

		i18n.setLocale('sv');
		expect(getLocalizedIngredientDescription(citra)).toContain('passionsfrukt');
		expect(getLocalizedIngredientDescription(customHop)).toBe('Found in the forest.');
	});

	it('localizes Other category ingredients and tab labels', () => {
		const irishMoss = { name: 'Irish Moss' };
		const gypsum = { name: 'Gypsum (Calcium Sulfate)' };

		i18n.setLocale('en');
		expect(getLocalizedIngredientName(irishMoss)).toBe('Irish Moss');
		expect(getLocalizedIngredientName(gypsum)).toBe('Gypsum (Calcium Sulfate)');
		expect(getLocalizedIngredientType('Other')).toBe('Other');

		i18n.setLocale('sv');
		expect(getLocalizedIngredientName(irishMoss)).toBe('Irish Moss (Irländsk mossa)');
		expect(getLocalizedIngredientName(gypsum)).toBe('Brygggips (Kalciumsulfat)');
		expect(getLocalizedIngredientType('Other')).toBe('Övrigt');
	});

	it('sorts ingredients alphabetically by localized name according to the active locale', () => {
		const items = [
			{ name: 'Pilsner Malt' },
			{ name: 'Amarillo' },
			{ name: 'Gypsum (Calcium Sulfate)' },
			{ name: 'Citra' }
		];

		// In English:
		// Amarillo, Citra, Gypsum (Calcium Sulfate), Pilsner Malt
		i18n.setLocale('en');
		const sortedEn = items
			.slice()
			.sort((a, b) =>
				getLocalizedIngredientName(a).localeCompare(getLocalizedIngredientName(b), i18n.locale, {
					sensitivity: 'base'
				})
			)
			.map((item) => getLocalizedIngredientName(item));

		expect(sortedEn).toEqual(['Amarillo', 'Citra', 'Gypsum (Calcium Sulfate)', 'Pilsner Malt']);

		// In Swedish:
		// Gypsum -> 'Brygggips (Kalciumsulfat)'
		// Pilsner Malt -> 'Pilsnermalt'
		// Amarillo, Brygggips (Kalciumsulfat), Citra, Pilsnermalt
		i18n.setLocale('sv');
		const sortedSv = items
			.slice()
			.sort((a, b) =>
				getLocalizedIngredientName(a).localeCompare(getLocalizedIngredientName(b), i18n.locale, {
					sensitivity: 'base'
				})
			)
			.map((item) => getLocalizedIngredientName(item));

		expect(sortedSv).toEqual(['Amarillo', 'Brygggips (Kalciumsulfat)', 'Citra', 'Pilsnermalt']);
	});

	it('localizes new German and craft catalogue ingredients in both English and Swedish', () => {
		const tradition = { name: 'Hallertauer Tradition' };
		const darkWheat = { name: 'Dark Wheat Malt' };
		const sauermalz = { name: 'Acidulated Malt' };
		const rauchmalz = { name: 'Smoked Malt (Rauchmalz)' };
		const weizenYeast = { name: 'Wyeast 3068 / WLP300 Weihenstephan Weizen' };

		// Key normalizations
		expect(normalizeIngredientKey(tradition.name)).toBe('hallertauer_tradition');
		expect(normalizeIngredientKey(darkWheat.name)).toBe('dark_wheat_malt');
		expect(normalizeIngredientKey(sauermalz.name)).toBe('acidulated_malt');
		expect(normalizeIngredientKey(rauchmalz.name)).toBe('smoked_malt_rauchmalz');
		expect(normalizeIngredientKey(weizenYeast.name)).toBe(
			'wyeast_3068_wlp300_weihenstephan_weizen'
		);

		// English
		i18n.setLocale('en');
		expect(getLocalizedIngredientName(tradition)).toBe('Hallertauer Tradition');
		expect(getLocalizedIngredientName(darkWheat)).toBe('Dark Wheat Malt');
		expect(getLocalizedIngredientName(sauermalz)).toBe('Acidulated Malt');
		expect(getLocalizedIngredientName(rauchmalz)).toBe('Smoked Malt (Rauchmalz)');
		expect(getLocalizedIngredientName(weizenYeast)).toBe(
			'Wyeast 3068 / WLP300 Weihenstephan Weizen'
		);
		expect(getLocalizedIngredientDescription(darkWheat)).toContain('Dunkelweizen');
		expect(getLocalizedIngredientDescription(weizenYeast)).toContain('isoamyl acetate');

		// Swedish
		i18n.setLocale('sv');
		expect(getLocalizedIngredientName(tradition)).toBe('Hallertauer Tradition');
		expect(getLocalizedIngredientName(darkWheat)).toBe('Mörkt vetemalt (Dunkles Weizenmalz)');
		expect(getLocalizedIngredientName(sauermalz)).toBe('Surmalt (Sauermalz)');
		expect(getLocalizedIngredientName(rauchmalz)).toBe('Rökmalt (Rauchmalz)');
		expect(getLocalizedIngredientName(weizenYeast)).toBe(
			'Wyeast 3068 / WLP300 Weihenstephan Weizen'
		);
		expect(getLocalizedIngredientDescription(darkWheat)).toContain('dunkelweizen');
		expect(getLocalizedIngredientDescription(weizenYeast)).toContain('isoamylacetat');
	});

	it('localizes ingredient forms in both English and Swedish', () => {
		i18n.setLocale('en');
		expect(getLocalizedIngredientForm('Pellet')).toBe('Pellet');
		expect(getLocalizedIngredientForm('Leaf')).toBe('Leaf');
		expect(getLocalizedIngredientForm('Plug')).toBe('Plug');
		expect(getLocalizedIngredientForm('Dry')).toBe('Dry');
		expect(getLocalizedIngredientForm('Liquid')).toBe('Liquid');
		expect(getLocalizedIngredientForm('Slant')).toBe('Slant');
		expect(getLocalizedIngredientForm('Culture')).toBe('Culture');
		expect(getLocalizedIngredientForm(null)).toBe('');
		expect(getLocalizedIngredientForm('UnknownForm')).toBe('UnknownForm');

		i18n.setLocale('sv');
		expect(getLocalizedIngredientForm('Pellet')).toBe('Pellets');
		expect(getLocalizedIngredientForm('Leaf')).toBe('Kottar');
		expect(getLocalizedIngredientForm('Plug')).toBe('Puckar');
		expect(getLocalizedIngredientForm('Dry')).toBe('Torr');
		expect(getLocalizedIngredientForm('Liquid')).toBe('Flytande');
		expect(getLocalizedIngredientForm('Slant')).toBe('Snedagarrör');
		expect(getLocalizedIngredientForm('Culture')).toBe('Kultur');
	});

	it('localizes stock labels, edit buttons, and out of stock states in English and Swedish', async () => {
		const en = (await import('$lib/i18n/locales/en.json')).default;
		expect(en.ingredients.stock).toBe('Stock');
		expect(en.ingredients.out_of_stock).toBe('Out of Stock');
		expect(en.ingredients.edit_stock).toBe('Edit Stock');
		expect(en.ingredients.update_stock).toBe('Update Stock');

		const sv = (await import('$lib/i18n/locales/sv.json')).default;
		expect(sv.ingredients.stock).toBe('Lager');
		expect(sv.ingredients.out_of_stock).toBe('Ej i lager');
		expect(sv.ingredients.edit_stock).toBe('Redigera lager');
		expect(sv.ingredients.update_stock).toBe('Uppdatera lager');
	});
});
