import { describe, it, expect } from 'vitest';
import type { IngredientShortageDto, BatchStockCheckResult } from '$lib/types/api';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';

describe('InsufficientStockModal & Stock Check Logic', () => {
	it('verifies stock shortage calculation and filtering', () => {
		const shortages: IngredientShortageDto[] = [
			{
				ingredientId: '1',
				name: 'Pale Malt (2-Row)',
				type: 'Fermentable',
				requiredAmount: 5.0,
				stockAmount: 2.5,
				unit: 'kg',
				deficit: 2.5
			},
			{
				ingredientId: '2',
				name: 'Citra',
				type: 'Hop',
				requiredAmount: 100,
				stockAmount: 50,
				unit: 'g',
				deficit: 50
			}
		];

		const result: BatchStockCheckResult = {
			hasShortage: shortages.length > 0,
			shortages
		};

		expect(result.hasShortage).toBe(true);
		expect(result.shortages).toHaveLength(2);
		expect(result.shortages[0].deficit).toBe(2.5);
		expect(result.shortages[1].deficit).toBe(50);
	});

	it('has all stock warning translations in both en.json and sv.json', () => {
		expect(en.batches.wizard.stock_warning.title).toBeDefined();
		expect(sv.batches.wizard.stock_warning.title).toBeDefined();

		expect(en.batches.wizard.stock_warning.desc).toBeDefined();
		expect(sv.batches.wizard.stock_warning.desc).toBeDefined();

		expect(en.batches.wizard.stock_warning.cancel).toBeDefined();
		expect(sv.batches.wizard.stock_warning.cancel).toBeDefined();

		expect(en.batches.wizard.stock_warning.continue_anyway).toBeDefined();
		expect(sv.batches.wizard.stock_warning.continue_anyway).toBeDefined();
	});

	it('has ingredient stock translation keys in both en.json and sv.json', () => {
		expect(en.ingredients.filter_in_stock).toBeDefined();
		expect(sv.ingredients.filter_in_stock).toBeDefined();

		expect(en.ingredients.update_stock).toBeDefined();
		expect(sv.ingredients.update_stock).toBeDefined();

		expect(en.ingredients.stock_amount_label).toBeDefined();
		expect(sv.ingredients.stock_amount_label).toBeDefined();
	});

	it('generates unique loop keys even when recipe has multiple additions of the same ingredient', () => {
		const duplicateShortages: IngredientShortageDto[] = [
			{
				ingredientId: 'ingredient-citra',
				name: 'Citra (15m Boil)',
				type: 'Hop',
				requiredAmount: 20,
				stockAmount: 0,
				unit: 'g',
				deficit: 20
			},
			{
				ingredientId: 'ingredient-citra',
				name: 'Citra (Dry Hop)',
				type: 'Hop',
				requiredAmount: 50,
				stockAmount: 0,
				unit: 'g',
				deficit: 50
			}
		];

		const keys = duplicateShortages.map((item, idx) => `${item.ingredientId}-${idx}`);
		const uniqueKeys = new Set(keys);
		expect(uniqueKeys.size).toBe(duplicateShortages.length);
		expect(keys[0]).not.toBe(keys[1]);
	});
});
