import { describe, it, expect } from 'vitest';
import fs from 'fs';
import path from 'path';
import type { RecipeIngredientOutputDto } from '$lib/types/api';
import {
	buildPrintableBoilSchedule,
	formatPrintableStageTiming,
	splitPrintableStageIngredients,
	type PrintableTimingLabels
} from './recipePrint';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';
import de from '$lib/i18n/locales/de.json';
import fr from '$lib/i18n/locales/fr.json';
import es from '$lib/i18n/locales/es.json';

const testLabels: PrintableTimingLabels = {
	rampLabel: 'Heat to Boil',
	whirlpoolLabel: 'Whirlpool',
	flameoutLabel: 'Flameout (0m)',
	minutesShortLabel: 'min',
	firstWortDurationText: 'First Wort',
	rampDurationText: 'Ramp',
	usages: {
		Mash: 'Mash',
		FirstWort: 'First Wort',
		Boil: 'Boil',
		Whirlpool: 'Whirlpool',
		Primary: 'Primary',
		Secondary: 'Secondary',
		DryHop: 'Dry Hop',
		Bottling: 'Bottling'
	}
};

function makeIngredient(overrides: Partial<RecipeIngredientOutputDto>): RecipeIngredientOutputDto {
	return {
		id: 'ing-1',
		ingredientId: 'cat-1',
		ingredientName: 'Pale Malt',
		ingredientType: 'Fermentable',
		amount: 1,
		unit: 'kg',
		durationMinutes: 60,
		usage: 'Mash',
		fermentableCategory: 'Grain',
		...overrides
	};
}

describe('recipePrint helpers', () => {
	it('includes boil, whirlpool, first-wort, and bottling fermentables in fermentableIngredients', () => {
		const ingredients: RecipeIngredientOutputDto[] = [
			makeIngredient({ id: 'malt-1', ingredientName: 'Pilsner Malt', usage: 'Mash' }),
			makeIngredient({
				id: 'sugar-boil',
				ingredientName: 'Cane Sugar',
				fermentableCategory: 'Sugar',
				usage: 'Boil',
				amount: 500,
				unit: 'g',
				durationMinutes: 15
			}),
			makeIngredient({
				id: 'honey-wp',
				ingredientName: 'Wildflower Honey',
				fermentableCategory: 'Sugar',
				usage: 'Whirlpool',
				durationMinutes: 15
			}),
			makeIngredient({
				id: 'sugar-bottle',
				ingredientName: 'Dextrose',
				fermentableCategory: 'Sugar',
				usage: 'Bottling',
				durationMinutes: 0
			}),
			makeIngredient({
				id: 'gypsum',
				ingredientName: 'Gypsum',
				ingredientType: 'Other',
				usage: 'Mash',
				amount: 5,
				unit: 'g'
			}),
			makeIngredient({
				id: 'citra-dry',
				ingredientName: 'Citra',
				ingredientType: 'Hop',
				usage: 'DryHop',
				amount: 50,
				unit: 'g',
				durationMinutes: 4320
			})
		];

		const split = splitPrintableStageIngredients(ingredients);

		expect(split.fermentableIngredients.map((i) => i.id)).toEqual([
			'malt-1',
			'sugar-boil',
			'honey-wp',
			'sugar-bottle'
		]);
		expect(split.mashNonFermentableIngredients.map((i) => i.id)).toEqual(['gypsum']);
		expect(split.fermentationIngredients.map((i) => i.id)).toEqual(['citra-dry']);
		expect(split.packagingIngredients.map((i) => i.id)).toEqual(['sugar-bottle']);
	});

	it('places boil sugars into their configured boil time bucket in chronological descending order', () => {
		const ingredients: RecipeIngredientOutputDto[] = [
			makeIngredient({ id: 'malt-1', ingredientName: 'Pale Malt', usage: 'Mash' }),
			makeIngredient({
				id: 'hop-60',
				ingredientName: 'Magnum',
				ingredientType: 'Hop',
				usage: 'Boil',
				amount: 30,
				unit: 'g',
				durationMinutes: 60
			}),
			makeIngredient({
				id: 'sugar-15',
				ingredientName: 'Belgian Candi Sugar',
				fermentableCategory: 'Sugar',
				usage: 'Boil',
				amount: 0.5,
				unit: 'kg',
				durationMinutes: 15
			}),
			makeIngredient({
				id: 'dme-0',
				ingredientName: 'Light Dry Malt Extract',
				fermentableCategory: 'DryExtract',
				usage: 'Boil',
				amount: 0.25,
				unit: 'kg',
				durationMinutes: 0
			}),
			makeIngredient({
				id: 'dextrose-bottle',
				ingredientName: 'Dextrose',
				fermentableCategory: 'Sugar',
				usage: 'Bottling',
				amount: 120,
				unit: 'g',
				durationMinutes: 0
			})
		];

		const rows = buildPrintableBoilSchedule(ingredients, 60, testLabels);

		expect(rows.map((r) => r.key)).toEqual(['ramp', 'boil-60', 'boil-15', 'boil-0']);
		expect(rows[1].label).toBe('60 min');
		expect(rows[1].items.map((i) => i.id)).toEqual(['hop-60']);
		expect(rows[2].label).toBe('15 min');
		expect(rows[2].items.map((i) => i.id)).toEqual(['sugar-15']);
		expect(rows[3].label).toBe('Flameout (0m)');
		expect(rows[3].items.map((i) => i.id)).toEqual(['dme-0']);
	});

	it('defaults boil additions without durationMinutes to boilTimeMinutes', () => {
		const ingredients: RecipeIngredientOutputDto[] = [
			makeIngredient({
				id: 'sugar-default',
				ingredientName: 'Table Sugar',
				fermentableCategory: 'Sugar',
				usage: 'Boil',
				durationMinutes: null
			})
		];

		const rows = buildPrintableBoilSchedule(ingredients, 75, testLabels);

		expect(rows.map((r) => r.key)).toEqual(['ramp', 'boil-75']);
		expect(rows[1].label).toBe('75 min');
		expect(rows[1].items[0].id).toBe('sugar-default');
	});

	it('formats stage and boil timing labels cleanly', () => {
		expect(formatPrintableStageTiming({ usage: 'Mash', durationMinutes: 60 }, testLabels)).toBe(
			'Mash'
		);
		expect(formatPrintableStageTiming({ usage: 'Boil', durationMinutes: 15 }, testLabels)).toBe(
			'Boil • 15 min'
		);
		expect(formatPrintableStageTiming({ usage: 'Boil', durationMinutes: 0 }, testLabels)).toBe(
			'Boil • Flameout (0m)'
		);
		expect(
			formatPrintableStageTiming({ usage: 'Whirlpool', durationMinutes: 20 }, testLabels)
		).toBe('Whirlpool • 20 min');
		expect(formatPrintableStageTiming({ usage: 'Bottling', durationMinutes: 0 }, testLabels)).toBe(
			'Bottling'
		);
	});
});

describe('Fermentable boil timing & printable recipe UI integration', () => {
	it('exposes fermentable boil duration input and usage change handler in IngredientsCard', () => {
		const cardPath = path.resolve(__dirname, '../components/recipes/IngredientsCard.svelte');
		const content = fs.readFileSync(cardPath, 'utf8');

		expect(content).toContain('data-testid="fermentable-duration-input"');
		expect(content).toContain('handleFermentableUsageChange');
		expect(content).toContain('supportsFermentableBoilTiming');
	});

	it('wires printable fermentables bill and boil schedule in printable recipe route', () => {
		const printPath = path.resolve(__dirname, '../../routes/recipes/[id]/print/+page.svelte');
		const content = fs.readFileSync(printPath, 'utf8');

		expect(content).toContain('data-testid="print-fermentables-table"');
		expect(content).toContain('buildPrintableBoilSchedule');
		expect(content).toContain('splitPrintableStageIngredients');
		expect(content).toContain('print:grid-cols-2');
		expect(content).toContain('size: A4 portrait');
		expect(content).not.toContain('data-testid="print-packaging-section"');
	});

	it('prevents flex/overflow clipping and hides app shell chrome during print', () => {
		const layoutPath = path.resolve(__dirname, '../../routes/+layout.svelte');
		const layoutContent = fs.readFileSync(layoutPath, 'utf8');
		expect(layoutContent).toContain('print:block');
		expect(layoutContent).toContain('print:overflow-visible');
		expect(layoutContent).toContain('print:hidden');

		const cssPath = path.resolve(__dirname, '../../routes/layout.css');
		const cssContent = fs.readFileSync(cssPath, 'utf8');
		expect(cssContent).toContain('@media print');
		expect(cssContent).toContain('overflow: visible !important');
	});

	it('normalizes grain weights and shows fermentable boil timing on recipe detail route', () => {
		const detailPath = path.resolve(__dirname, '../../routes/recipes/[id]/+page.svelte');
		const content = fs.readFileSync(detailPath, 'utf8');

		expect(content).toContain('settings.formatGrain(toGrainKg(item.amount, item.unit))');
		expect(content).toContain('formatPrintableStageTiming');
	});

	it('maintains 100% i18n parity for new print_sheet keys across all 5 locales', () => {
		const locales = [en, sv, de, fr, es];
		const requiredKeys = ['fermentables_bill_title', 'mash_additions_title'];

		for (const loc of locales) {
			for (const key of requiredKeys) {
				const value = (loc.recipes.print_sheet as Record<string, string>)[key];
				expect(value).toBeDefined();
				expect(typeof value).toBe('string');
				expect(value.trim().length).toBeGreaterThan(0);
			}
		}
	});
});
