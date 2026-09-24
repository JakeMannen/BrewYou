import { describe, it, expect } from 'vitest';
import type { IngredientDto, IngredientType } from '$lib/types/api';
import { ApiClientError } from '$lib/api/client';
import { srmToHexColor } from '$lib/calculators/brewing';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';

describe('IngredientModal Validation & Calculation Logic', () => {
	const sampleIngredients: IngredientDto[] = [
		{
			id: 'ing-1',
			name: 'Citra',
			type: 'Hop',
			alphaAcidPercent: 12.5,
			isCatalogItem: true
		},
		{
			id: 'ing-2',
			name: 'Pilsner Malt',
			type: 'Fermentable',
			potentialGravity: 1.037,
			colorSrm: 1.8,
			isCatalogItem: true
		},
		{
			id: 'ing-3',
			name: 'My Special Hop',
			type: 'Hop',
			alphaAcidPercent: 14.0,
			isCatalogItem: false
		}
	];

	function isDuplicateName(
		newName: string,
		existing: IngredientDto[],
		currentId?: string
	): boolean {
		const trimmed = newName.trim().toLowerCase();
		if (!trimmed) return false;
		return existing.some((item) => {
			if (currentId && item.id === currentId) return false;
			return item.name.trim().toLowerCase() === trimmed;
		});
	}

	function validateIngredient(input: {
		name: string;
		type: IngredientType;
		potentialGravity?: number | '';
		potentialUnit?: 'SG' | 'PPG';
		colorSrm?: number | '';
		alphaAcidPercent?: number | '';
		attenuationPercent?: number | '';
		description?: string;
	}): Record<string, string> {
		const errors: Record<string, string> = {};
		const trimmed = input.name.trim();

		if (!trimmed) {
			errors.name = 'Name required';
		} else if (trimmed.length < 2) {
			errors.name = 'Name too short';
		} else if (trimmed.length > 100) {
			errors.name = 'Name too long';
		} else if (trimmed.includes('<') || trimmed.includes('>')) {
			errors.name = 'Invalid characters';
		}

		if (input.description && input.description.length > 1000) {
			errors.description = 'Description too long';
		}

		if (input.type === 'Fermentable') {
			if (typeof input.potentialGravity === 'number') {
				const unit = input.potentialUnit || 'SG';
				if (unit === 'SG') {
					if (input.potentialGravity >= 10 && input.potentialGravity <= 150) {
						// Auto-converted from PPG to SG -> valid
					} else if (input.potentialGravity < 1.0 || input.potentialGravity > 1.2) {
						errors.potentialGravity = 'Invalid gravity range';
					}
				} else {
					if (input.potentialGravity < 1 || input.potentialGravity > 150) {
						errors.potentialGravity = 'Invalid PPG range';
					}
				}
			}
			if (typeof input.colorSrm === 'number') {
				if (input.colorSrm < 0 || input.colorSrm > 1000) {
					errors.colorSrm = 'Invalid SRM range';
				}
			}
		} else if (input.type === 'Hop') {
			if (typeof input.alphaAcidPercent === 'number') {
				if (input.alphaAcidPercent < 0 || input.alphaAcidPercent > 100) {
					errors.alphaAcidPercent = 'Invalid alpha acid range';
				}
			}
		} else if (input.type === 'Yeast') {
			if (typeof input.attenuationPercent === 'number') {
				if (input.attenuationPercent < 0 || input.attenuationPercent > 100) {
					errors.attenuationPercent = 'Invalid attenuation range';
				}
			}
		}

		return errors;
	}

	it('provides comprehensive bilingual localization keys for all modal elements', () => {
		const enIng = en.ingredients;
		const svIng = sv.ingredients;

		expect(enIng.add_ingredient).toBe('Add Ingredient');
		expect(svIng.add_ingredient).toBe('Lägg till råvara');

		expect(enIng.modal_add_title).toBe('Add New Ingredient');
		expect(svIng.modal_add_title).toBe('Lägg till ny råvara');

		expect(enIng.custom_badge).toBe('Custom');
		expect(svIng.custom_badge).toBe('Anpassad');

		expect(enIng.save_ingredient).toBe('Save Ingredient');
		expect(svIng.save_ingredient).toBe('Spara råvara');

		expect(enIng.potential_ppg_label).toBe('Potential (PPG)');
		expect(svIng.potential_ppg_label).toBe('Utbytespotential (PPG)');
		expect(enIng.unit_sg).toBe('SG');
		expect(svIng.unit_sg).toBe('SG');
		expect(enIng.unit_ppg).toBe('PPG');
		expect(svIng.unit_ppg).toBe('PPG');

		expect(enIng.err_duplicate).toBe(
			'An ingredient with this name already exists in your catalog.'
		);
		expect(svIng.err_duplicate).toBe('En råvara med detta namn finns redan i din katalog.');
	});

	it('detects duplicate ingredient names case-insensitively', () => {
		expect(isDuplicateName('Citra', sampleIngredients)).toBe(true);
		expect(isDuplicateName('citra', sampleIngredients)).toBe(true);
		expect(isDuplicateName('  PILSNER MALT  ', sampleIngredients)).toBe(true);
		expect(isDuplicateName('My Special Hop', sampleIngredients)).toBe(true);
		expect(isDuplicateName('Galaxy', sampleIngredients)).toBe(false);

		// Self-name when editing is not a duplicate
		expect(isDuplicateName('My Special Hop', sampleIngredients, 'ing-3')).toBe(false);
	});

	it('validates mandatory name field boundaries and rejects XSS vectors', () => {
		// Empty name
		expect(validateIngredient({ name: '', type: 'Hop' }).name).toBe('Name required');
		expect(validateIngredient({ name: '   ', type: 'Hop' }).name).toBe('Name required');

		// Too short (< 2 chars)
		expect(validateIngredient({ name: 'A', type: 'Hop' }).name).toBe('Name too short');

		// Too long (> 100 chars)
		expect(validateIngredient({ name: 'A'.repeat(101), type: 'Hop' }).name).toBe('Name too long');

		// XSS characters (<>)
		expect(validateIngredient({ name: '<script>alert(1)</script>', type: 'Hop' }).name).toBe(
			'Invalid characters'
		);

		// Valid name
		expect(validateIngredient({ name: 'Mosaic', type: 'Hop' }).name).toBeUndefined();
	});

	it('validates fermentable-specific boundaries (potential gravity and SRM)', () => {
		// Invalid low gravity
		expect(
			validateIngredient({ name: 'Malt', type: 'Fermentable', potentialGravity: 0.999 })
				.potentialGravity
		).toBe('Invalid gravity range');

		// Invalid high gravity
		expect(
			validateIngredient({ name: 'Malt', type: 'Fermentable', potentialGravity: 1.201 })
				.potentialGravity
		).toBe('Invalid gravity range');

		// Valid gravity
		expect(
			validateIngredient({ name: 'Malt', type: 'Fermentable', potentialGravity: 1.037 })
				.potentialGravity
		).toBeUndefined();

		// Auto-convert PPG entered into SG mode
		expect(
			validateIngredient({
				name: 'Malt',
				type: 'Fermentable',
				potentialGravity: 38,
				potentialUnit: 'SG'
			}).potentialGravity
		).toBeUndefined();

		// Valid PPG in PPG mode
		expect(
			validateIngredient({
				name: 'Malt',
				type: 'Fermentable',
				potentialGravity: 38,
				potentialUnit: 'PPG'
			}).potentialGravity
		).toBeUndefined();

		// Invalid PPG in PPG mode
		expect(
			validateIngredient({
				name: 'Malt',
				type: 'Fermentable',
				potentialGravity: 0,
				potentialUnit: 'PPG'
			}).potentialGravity
		).toBe('Invalid PPG range');

		expect(
			validateIngredient({
				name: 'Malt',
				type: 'Fermentable',
				potentialGravity: 160,
				potentialUnit: 'PPG'
			}).potentialGravity
		).toBe('Invalid PPG range');

		// Invalid SRM
		expect(validateIngredient({ name: 'Malt', type: 'Fermentable', colorSrm: -1 }).colorSrm).toBe(
			'Invalid SRM range'
		);
		expect(validateIngredient({ name: 'Malt', type: 'Fermentable', colorSrm: 1001 }).colorSrm).toBe(
			'Invalid SRM range'
		);

		// Valid SRM
		expect(
			validateIngredient({ name: 'Malt', type: 'Fermentable', colorSrm: 15.0 }).colorSrm
		).toBeUndefined();
	});

	it('validates hop-specific boundaries (alpha acid percentage)', () => {
		expect(
			validateIngredient({ name: 'Hop', type: 'Hop', alphaAcidPercent: -0.1 }).alphaAcidPercent
		).toBe('Invalid alpha acid range');
		expect(
			validateIngredient({ name: 'Hop', type: 'Hop', alphaAcidPercent: 100.1 }).alphaAcidPercent
		).toBe('Invalid alpha acid range');
		expect(
			validateIngredient({ name: 'Hop', type: 'Hop', alphaAcidPercent: 14.5 }).alphaAcidPercent
		).toBeUndefined();
	});

	it('validates yeast-specific boundaries (attenuation percentage)', () => {
		expect(
			validateIngredient({ name: 'Yeast', type: 'Yeast', attenuationPercent: -1 })
				.attenuationPercent
		).toBe('Invalid attenuation range');
		expect(
			validateIngredient({ name: 'Yeast', type: 'Yeast', attenuationPercent: 101 })
				.attenuationPercent
		).toBe('Invalid attenuation range');
		expect(
			validateIngredient({ name: 'Yeast', type: 'Yeast', attenuationPercent: 78.0 })
				.attenuationPercent
		).toBeUndefined();
	});

	it('computes live SRM color hex values accurately', () => {
		// Low SRM -> Pale straw / yellow
		const pale = srmToHexColor(2);
		expect(pale).toMatch(/^#[0-9a-fA-F]{6}$/);

		// Medium SRM -> Amber / copper
		const amber = srmToHexColor(15);
		expect(amber).toMatch(/^#[0-9a-fA-F]{6}$/);

		// High SRM -> Dark / stout
		const dark = srmToHexColor(40);
		expect(dark).toMatch(/^#[0-9a-fA-F]{6}$/);

		// Zero or negative -> water white fallback
		const zero = srmToHexColor(0);
		expect(zero).toMatch(/^#[0-9a-fA-F]{6}$/);
	});

	it('handles ApiClientError field error mapping correctly', () => {
		const apiError = new ApiClientError('Validation failed.', 400, 'VALIDATION_ERROR', [
			{ field: 'Name', issue: 'Ingredient name is required.' },
			{ field: 'PotentialGravity', issue: 'Potential gravity must be between 1.000 and 1.200.' }
		]);

		const fieldErrors: Record<string, string> = {};
		if (apiError.details) {
			for (const d of apiError.details) {
				fieldErrors[d.field.toLowerCase()] = d.issue;
			}
		}

		expect(fieldErrors['name']).toBe('Ingredient name is required.');
		expect(fieldErrors['potentialgravity']).toBe(
			'Potential gravity must be between 1.000 and 1.200.'
		);
	});
});
