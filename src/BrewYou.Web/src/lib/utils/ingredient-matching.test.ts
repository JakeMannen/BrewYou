import { describe, expect, it } from 'vitest';
import type { IngredientDto } from '$lib/types/api';
import {
	findCatalogIngredientMatch,
	isIngredientMatch,
	normalizeIngredientName,
	stripManufacturerPrefix,
	stripParentheticalSuffix,
	extractYeastCode
} from './ingredient-matching';

describe('Ingredient Matching Utility', () => {
	const catalog: IngredientDto[] = [
		{
			id: 'ing-pilsner',
			name: 'Pilsner Malt',
			type: 'Fermentable',
			potentialGravity: 1.037,
			colorSrm: 1.6,
			isCatalogItem: true
		},
		{
			id: 'ing-pale-2row',
			name: 'Pale Malt (2-Row)',
			type: 'Fermentable',
			potentialGravity: 1.037,
			colorSrm: 1.8,
			isCatalogItem: true
		},
		{
			id: 'ing-munich-1',
			name: 'Munich Malt (Light)',
			type: 'Fermentable',
			potentialGravity: 1.035,
			colorSrm: 9.0,
			isCatalogItem: true
		},
		{
			id: 'ing-hallertau',
			name: 'Hallertau Mittelfrüh',
			type: 'Hop',
			alphaAcidPercent: 4.0,
			isCatalogItem: true
		},
		{
			id: 'ing-centennial',
			name: 'Centennial',
			type: 'Hop',
			alphaAcidPercent: 10.0,
			isCatalogItem: true
		},
		{
			id: 'ing-wlp860',
			name: 'WLP860 Munich Helles',
			type: 'Yeast',
			attenuationPercent: 75.0,
			isCatalogItem: true
		},
		{
			id: 'ing-us05',
			name: 'SafAle US-05',
			type: 'Yeast',
			attenuationPercent: 81.0,
			isCatalogItem: true
		},
		{
			id: 'ing-irish-moss',
			name: 'Irish Moss',
			type: 'Other',
			isCatalogItem: true
		}
	];

	describe('Exact & Normalized Matches', () => {
		it('matches exact case-insensitive names', () => {
			const match = findCatalogIngredientMatch(
				{ name: '  pilsner malt  ', type: 'Fermentable' },
				catalog
			);
			expect(match?.id).toBe('ing-pilsner');
		});

		it('normalizes diacritics / accents', () => {
			const match = findCatalogIngredientMatch(
				{ name: 'Hallertau Mittelfruh', type: 'Hop' },
				catalog
			);
			expect(match?.id).toBe('ing-hallertau');
		});

		it('normalizes punctuation and parenthesis formatting', () => {
			const match = findCatalogIngredientMatch(
				{ name: 'Pale Malt 2-Row', type: 'Fermentable' },
				catalog
			);
			expect(match?.id).toBe('ing-pale-2row');
		});

		it('normalizes German regional hop suffixes (Hallertauer -> Hallertau)', () => {
			const match = findCatalogIngredientMatch(
				{ name: 'Hallertauer Mittelfruh', type: 'Hop' },
				catalog
			);
			expect(match?.id).toBe('ing-hallertau');
		});

		it('splits Swedish/German compound terms (Pilsnermalt -> Pilsner malt)', () => {
			const match = findCatalogIngredientMatch(
				{ name: 'Pilsnermalt', type: 'Fermentable' },
				catalog
			);
			expect(match?.id).toBe('ing-pilsner');
		});
	});

	describe('Manufacturer Prefix and Suffix Stripping', () => {
		it('strips manufacturer prefix delimiter ("Weyermann - Pilsner Malt")', () => {
			const match = findCatalogIngredientMatch(
				{ name: 'Weyermann - Pilsner Malt', type: 'Fermentable' },
				catalog
			);
			expect(match?.id).toBe('ing-pilsner');
		});

		it('strips manufacturer suffix in parentheses ("Pilsner Malt (Weyermann)")', () => {
			const match = findCatalogIngredientMatch(
				{ name: 'Pilsner Malt (Weyermann)', type: 'Fermentable' },
				catalog
			);
			expect(match?.id).toBe('ing-pilsner');
		});

		it('strips hop stopwords ("Centennial Hops" -> "Centennial")', () => {
			const match = findCatalogIngredientMatch({ name: 'Centennial Hops', type: 'Hop' }, catalog);
			expect(match?.id).toBe('ing-centennial');
		});
	});

	describe('Yeast Product Code Matching', () => {
		it('matches yeast by White Labs code WLP860', () => {
			const match = findCatalogIngredientMatch(
				{ name: 'Munich Helles Yeast WLP860', type: 'Yeast' },
				catalog
			);
			expect(match?.id).toBe('ing-wlp860');
		});

		it('matches yeast by Fermentis code US-05', () => {
			const match = findCatalogIngredientMatch(
				{ name: 'Fermentis Dry Yeast US-05', type: 'Yeast' },
				catalog
			);
			expect(match?.id).toBe('ing-us05');
		});
	});

	describe('Critical Bug Prevention: Barke Pilsner vs Pilsner Malt', () => {
		it('does NOT match Weyermann - Barke Pilsner Malt to generic Pilsner Malt', () => {
			const match = findCatalogIngredientMatch(
				{ name: 'Weyermann - Barke Pilsner Malt', type: 'Fermentable' },
				catalog
			);
			// Must return null so it can be flagged as missing and created properly
			expect(match).toBeNull();
		});

		it('does NOT match Barke Pilsner to Pilsnermalt', () => {
			const catalogWithPilsnermalt: IngredientDto[] = [
				{
					id: 'ing-sv-pilsner',
					name: 'Pilsnermalt',
					type: 'Fermentable',
					potentialGravity: 1.037,
					colorSrm: 1.6,
					isCatalogItem: true
				}
			];

			const match = findCatalogIngredientMatch(
				{ name: 'Weyermann - Barke Pilsner Malt', type: 'Fermentable' },
				catalogWithPilsnermalt
			);
			expect(match).toBeNull();
		});

		it('matches Weyermann - Barke Pilsner Malt to itself even when Pilsner Malt is first in catalog', () => {
			const extendedCatalog: IngredientDto[] = [
				...catalog, // Pilsner Malt is at index 0
				{
					id: 'ing-barke-pilsner',
					name: 'Weyermann - Barke Pilsner Malt',
					type: 'Fermentable',
					potentialGravity: 1.038,
					colorSrm: 1.8,
					isCatalogItem: false
				}
			];

			const match = findCatalogIngredientMatch(
				{ name: 'Weyermann - Barke Pilsner Malt', type: 'Fermentable' },
				extendedCatalog
			);
			expect(match).not.toBeNull();
			expect(match?.id).toBe('ing-barke-pilsner');
			expect(match?.name).toBe('Weyermann - Barke Pilsner Malt');
		});
	});

	describe('Helper Functions Unit Tests', () => {
		it('tests normalizeIngredientName directly', () => {
			expect(normalizeIngredientName('  Pilsner-Malt (2-Row)  ')).toBe('pilsner malt 2 row');
			expect(normalizeIngredientName('Hallertauer Mittelfrüh')).toBe('hallertau mittelfruh');
		});

		it('tests stripManufacturerPrefix directly', () => {
			expect(stripManufacturerPrefix('Weyermann - Barke Pilsner')).toBe('Barke Pilsner');
			expect(stripManufacturerPrefix('Crisp: Maris Otter')).toBe('Maris Otter');
			expect(stripManufacturerPrefix('Just An Ingredient')).toBe('Just An Ingredient');
		});

		it('tests stripParentheticalSuffix directly', () => {
			expect(stripParentheticalSuffix('Pilsner Malt (Weyermann)')).toBe('Pilsner Malt');
			expect(stripParentheticalSuffix('Cascade (US)')).toBe('Cascade');
			expect(stripParentheticalSuffix('No Parens')).toBe('No Parens');
		});

		it('tests extractYeastCode directly', () => {
			expect(extractYeastCode('Munich Helles Yeast WLP860')).toBe('wlp860');
			expect(extractYeastCode('SafAle US-05')).toBe('us05');
			expect(extractYeastCode('LalBrew Voss')).toBeNull();
		});
	});

	describe('Type Safety & Negative Cases', () => {
		it('rejects match across mismatched types (Fermentable vs Hop)', () => {
			const match = findCatalogIngredientMatch(
				{ name: 'Centennial', type: 'Fermentable' },
				catalog
			);
			expect(match).toBeNull();
		});

		it('returns null for empty or whitespace name', () => {
			expect(findCatalogIngredientMatch({ name: '', type: 'Fermentable' }, catalog)).toBeNull();
			expect(findCatalogIngredientMatch({ name: '   ', type: 'Fermentable' }, catalog)).toBeNull();
		});

		it('isIngredientMatch helper returns boolean accordingly', () => {
			expect(
				isIngredientMatch(
					{ name: 'Pale Malt (2-Row)', type: 'Fermentable' },
					catalog.find((c) => c.id === 'ing-pale-2row')!
				)
			).toBe(true);

			expect(
				isIngredientMatch(
					{ name: 'Weyermann - Barke Pilsner Malt', type: 'Fermentable' },
					catalog.find((c) => c.id === 'ing-pilsner')!
				)
			).toBe(false);
		});
	});
});
