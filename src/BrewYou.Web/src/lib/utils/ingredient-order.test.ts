import { describe, it, expect } from 'vitest';
import { sortIngredientsChronologically } from './ingredient-order';
import type { BatchIngredientDto } from '$lib/types/api';

describe('sortIngredientsChronologically', () => {
	it('places the first hop added during boil (highest minutes remaining) at the top of the list', () => {
		const additions: BatchIngredientDto[] = [
			{
				id: '1',
				batchId: 'b1',
				name: 'Amarillo Flameout',
				type: 'Hop',
				amount: 50,
				unit: 'g',
				additionStage: 'Boil',
				additionTimeMinutes: 0,
				isChecked: false
			},
			{
				id: '2',
				batchId: 'b1',
				name: 'Cascade 15m',
				type: 'Hop',
				amount: 30,
				unit: 'g',
				additionStage: 'Boil',
				additionTimeMinutes: 15,
				isChecked: false
			},
			{
				id: '3',
				batchId: 'b1',
				name: 'Magnum 60m',
				type: 'Hop',
				amount: 25,
				unit: 'g',
				additionStage: 'Boil',
				additionTimeMinutes: 60,
				isChecked: false
			},
			{
				id: '4',
				batchId: 'b1',
				name: 'Centennial 30m',
				type: 'Hop',
				amount: 20,
				unit: 'g',
				additionStage: 'Boil',
				additionTimeMinutes: 30,
				isChecked: false
			}
		];

		const sorted = sortIngredientsChronologically(additions);

		expect(sorted.map((i) => i.name)).toEqual([
			'Magnum 60m',
			'Centennial 30m',
			'Cascade 15m',
			'Amarillo Flameout'
		]);
	});

	it('orders across all brewing stages in process sequence', () => {
		const additions: BatchIngredientDto[] = [
			{
				id: 'pkg1',
				batchId: 'b1',
				name: 'Priming Sugar',
				type: 'Other',
				amount: 120,
				unit: 'g',
				additionStage: 'Bottling',
				isChecked: false
			},
			{
				id: 'hop1',
				batchId: 'b1',
				name: 'Simcoe 60m',
				type: 'Hop',
				amount: 30,
				unit: 'g',
				additionStage: 'Boil',
				additionTimeMinutes: 60,
				isChecked: false
			},
			{
				id: 'mash1',
				batchId: 'b1',
				name: 'Pilsner Malt',
				type: 'Fermentable',
				amount: 5.0,
				unit: 'kg',
				additionStage: 'Mash',
				isChecked: false
			},
			{
				id: 'yeast1',
				batchId: 'b1',
				name: 'US-05 Yeast',
				type: 'Yeast',
				amount: 11.5,
				unit: 'g',
				additionStage: 'Primary',
				isChecked: false
			},
			{
				id: 'wp1',
				batchId: 'b1',
				name: 'Mosaic Whirlpool',
				type: 'Hop',
				amount: 50,
				unit: 'g',
				additionStage: 'Whirlpool',
				additionTimeMinutes: 20,
				isChecked: false
			},
			{
				id: 'dry1',
				batchId: 'b1',
				name: 'Citra Dry Hop',
				type: 'Hop',
				amount: 50,
				unit: 'g',
				additionStage: 'DryHop',
				additionTimeMinutes: 3,
				isChecked: false
			}
		];

		const sorted = sortIngredientsChronologically(additions);

		expect(sorted.map((i) => i.name)).toEqual([
			'Pilsner Malt', // Mash
			'Simcoe 60m', // Boil (60m)
			'Mosaic Whirlpool', // Whirlpool
			'US-05 Yeast', // Primary
			'Citra Dry Hop', // DryHop
			'Priming Sugar' // Bottling
		]);
	});

	it('places yeast first within primary fermentation', () => {
		const additions: BatchIngredientDto[] = [
			{
				id: 'ferm1',
				batchId: 'b1',
				name: 'Bio-transformation Dry Hop',
				type: 'Hop',
				amount: 30,
				unit: 'g',
				additionStage: 'Primary',
				additionTimeMinutes: 2,
				isChecked: false
			},
			{
				id: 'yeast1',
				batchId: 'b1',
				name: 'California Ale Yeast',
				type: 'Yeast',
				amount: 1,
				unit: 'pkg',
				additionStage: 'Primary',
				isChecked: false
			}
		];

		const sorted = sortIngredientsChronologically(additions);

		expect(sorted[0].name).toBe('California Ale Yeast');
		expect(sorted[1].name).toBe('Bio-transformation Dry Hop');
	});

	it('orders mash ingredients with grains first descending by amount, then non-grains', () => {
		const mashAdditions: BatchIngredientDto[] = [
			{
				id: 'm1',
				batchId: 'b1',
				name: 'Gypsum',
				type: 'Other',
				amount: 5,
				unit: 'g',
				additionStage: 'Mash',
				isChecked: false
			},
			{
				id: 'm2',
				batchId: 'b1',
				name: 'Munich Malt',
				type: 'Fermentable',
				amount: 1.5,
				unit: 'kg',
				additionStage: 'Mash',
				isChecked: false
			},
			{
				id: 'm3',
				batchId: 'b1',
				name: 'Pale Ale Malt',
				type: 'Fermentable',
				amount: 4.5,
				unit: 'kg',
				additionStage: 'Mash',
				isChecked: false
			}
		];

		const sorted = sortIngredientsChronologically(mashAdditions);
		expect(sorted.map((i) => i.name)).toEqual(['Pale Ale Malt', 'Munich Malt', 'Gypsum']);
	});

	it('orders dry hop and secondary additions ascending by time, then name', () => {
		const additions: BatchIngredientDto[] = [
			{
				id: 'd1',
				batchId: 'b1',
				name: 'Mosaic Day 5',
				type: 'Hop',
				amount: 50,
				unit: 'g',
				additionStage: 'DryHop',
				additionTimeMinutes: 5,
				isChecked: false
			},
			{
				id: 'd2',
				batchId: 'b1',
				name: 'Citra Day 2',
				type: 'Hop',
				amount: 50,
				unit: 'g',
				additionStage: 'DryHop',
				additionTimeMinutes: 2,
				isChecked: false
			},
			{
				id: 'd3',
				batchId: 'b1',
				name: 'Amarillo Day 2',
				type: 'Hop',
				amount: 50,
				unit: 'g',
				additionStage: 'DryHop',
				additionTimeMinutes: 2,
				isChecked: false
			}
		];

		const sorted = sortIngredientsChronologically(additions);
		expect(sorted.map((i) => i.name)).toEqual(['Amarillo Day 2', 'Citra Day 2', 'Mosaic Day 5']);
	});
});
