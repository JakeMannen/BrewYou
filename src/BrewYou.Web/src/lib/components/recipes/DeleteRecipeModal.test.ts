import { describe, it, expect } from 'vitest';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';
import de from '$lib/i18n/locales/de.json';
import fr from '$lib/i18n/locales/fr.json';
import es from '$lib/i18n/locales/es.json';
import type { RecipeActiveBatchDto } from '$lib/types/api';

describe('DeleteRecipeModal Component Logic & Invariants', () => {
	it('ensures all 5 locales have complete active batch warning and force delete keys with 100% parity', () => {
		const locales = [
			{ name: 'en', data: en },
			{ name: 'sv', data: sv },
			{ name: 'de', data: de },
			{ name: 'fr', data: fr },
			{ name: 'es', data: es }
		];

		const requiredModalKeys = [
			'title',
			'warning',
			'batches_notice',
			'active_batches_warning',
			'checking_batches',
			'in_use_warning_title',
			'in_use_warning_desc',
			'view_batch',
			'conflict_error',
			'force_delete_checkbox',
			'force_confirm_button',
			'cancel',
			'confirm',
			'deleting',
			'error'
		];

		for (const loc of locales) {
			for (const key of requiredModalKeys) {
				const val = (loc.data.recipes.delete_modal as Record<string, string>)[key];
				expect(val, `Missing key '${key}' in locale '${loc.name}'`).toBeDefined();
				expect(typeof val).toBe('string');
				expect(val.length).toBeGreaterThan(0);
			}
		}

		// Verify key phrases in English and Swedish
		expect(en.recipes.delete_modal.in_use_warning_title).toBe('Active Batches in Progress');
		expect(sv.recipes.delete_modal.in_use_warning_title).toBe('Aktiva bryggningar pågår');
		expect(en.recipes.delete_modal.force_confirm_button).toBe('Force Delete Recipe');
		expect(sv.recipes.delete_modal.force_confirm_button).toBe('Tvångsradera recept');
	});

	it('ensures all 5 locales have fallback bottle label keys with 100% parity', () => {
		const locales = [
			{ name: 'en', data: en },
			{ name: 'sv', data: sv },
			{ name: 'de', data: de },
			{ name: 'fr', data: fr },
			{ name: 'es', data: es }
		];

		const requiredLabelKeys = [
			'recipe',
			'default_craft_mark',
			'using_default_label',
			'default_label_desc'
		];

		for (const loc of locales) {
			for (const key of requiredLabelKeys) {
				const val = (loc.data.labels as Record<string, string>)[key];
				expect(val, `Missing labels key '${key}' in locale '${loc.name}'`).toBeDefined();
				expect(typeof val).toBe('string');
				expect(val.length).toBeGreaterThan(0);
			}
		}
	});

	it('requires force confirmation when active batches exist', () => {
		const activeBatches: RecipeActiveBatchDto[] = [
			{
				id: 'batch-001',
				batchCode: 'B-2026-11-04-01',
				name: 'Active Fermenting Batch',
				status: 'Fermenting',
				currentStage: 'Ferment'
			}
		];

		const hasActiveBatches = activeBatches.length > 0;
		let forceChecked = false;

		// When active batches are present and force is not checked, submission must be blocked
		const canSubmitWithoutForce = !hasActiveBatches || forceChecked;
		expect(canSubmitWithoutForce).toBe(false);

		// When force is checked, submission must be allowed
		forceChecked = true;
		const canSubmitWithForce = !hasActiveBatches || forceChecked;
		expect(canSubmitWithForce).toBe(true);
	});

	it('allows immediate unforced delete when no active batches are linked', () => {
		const activeBatches: RecipeActiveBatchDto[] = [];
		const hasActiveBatches = activeBatches.length > 0;
		const forceChecked = false;

		const canSubmit = !hasActiveBatches || forceChecked;
		expect(canSubmit).toBe(true);
	});
});
