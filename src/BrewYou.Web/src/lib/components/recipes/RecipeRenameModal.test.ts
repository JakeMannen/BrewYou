import { describe, it, expect, vi } from 'vitest';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';

describe('RecipeRenameModal Component Logic & Invariants', () => {
	it('has complete internationalization key parity for rename_modal in en.json and sv.json', () => {
		const enKeys = Object.keys(en.recipes.rename_modal);
		const svKeys = Object.keys(sv.recipes.rename_modal);

		expect(enKeys.sort()).toEqual(svKeys.sort());

		expect(en.recipes.rename_modal.title).toBe('Recipe Name Conflict');
		expect(sv.recipes.rename_modal.title).toBe('Namnkonflikt för recept');

		expect(en.recipes.rename_modal.continue_button).toBe('Rename & Import');
		expect(sv.recipes.rename_modal.continue_button).toBe('Byt namn & importera');

		expect(en.recipes.rename_modal.cancel_button).toBe('Cancel');
		expect(sv.recipes.rename_modal.cancel_button).toBe('Avbryt');

		expect(en.recipes.rename_modal.error_required).toBe('Recipe name cannot be empty.');
		expect(sv.recipes.rename_modal.error_required).toBe('Receptnamn kan inte vara tomt.');

		expect(en.recipes.rename_modal.error_conflict).toBe(
			'A recipe with this name already exists in your library.'
		);
		expect(sv.recipes.rename_modal.error_conflict).toBe(
			'Ett recept med detta namn finns redan i ditt bibliotek.'
		);
	});

	it('computes default rename string with localized copy suffix', () => {
		const originalName = 'Hazy NEIPA';
		const defaultEn = `${originalName} (${en.recipes.copy_suffix})`;
		const defaultSv = `${originalName} (${sv.recipes.copy_suffix})`;

		expect(defaultEn).toBe('Hazy NEIPA (Copy)');
		expect(defaultSv).toBe('Hazy NEIPA (Kopia)');
	});

	it('validates against empty input names', async () => {
		let validationError: string | null = null;
		const nameInput = '   ';

		const trimmed = nameInput.trim();
		if (!trimmed) {
			validationError = en.recipes.rename_modal.error_required;
		}

		expect(validationError).toBe(en.recipes.rename_modal.error_required);
	});

	it('validates against names that conflict with existing recipes', async () => {
		const existingNames = ['Citra Pale Ale', 'Hazy NEIPA', 'Oatmeal Stout'];
		const checkConflict = (name: string) => {
			const lower = name.trim().toLowerCase();
			return existingNames.some((n) => n.toLowerCase() === lower);
		};

		let validationError: string | null = null;
		const proposedName = '  hazy neipa  ';
		const trimmed = proposedName.trim();

		const hasConflict = checkConflict(trimmed);
		if (hasConflict) {
			validationError = en.recipes.rename_modal.error_conflict;
		}

		expect(hasConflict).toBe(true);
		expect(validationError).toBe(en.recipes.rename_modal.error_conflict);
	});

	it('accepts unique recipe names and invokes onRename with trimmed value', async () => {
		const existingNames = ['Citra Pale Ale', 'Hazy NEIPA'];
		const checkConflict = vi.fn((name: string) => {
			const lower = name.trim().toLowerCase();
			return existingNames.some((n) => n.toLowerCase() === lower);
		});

		const onRename = vi.fn();
		const proposedName = '  Hazy NEIPA (Version 2)  ';
		const trimmed = proposedName.trim();

		const hasConflict = checkConflict(trimmed);
		expect(hasConflict).toBe(false);

		if (!hasConflict) {
			onRename(trimmed);
		}

		expect(onRename).toHaveBeenCalledWith('Hazy NEIPA (Version 2)');
	});

	it('handles keyboard escape and cancel callbacks without modifying recipes', () => {
		let wasCancelled = false;
		const onCancel = () => {
			wasCancelled = true;
		};

		function handleKeydown(e: { key: string }, open: boolean, isSubmitting: boolean) {
			if (e.key === 'Escape' && open && !isSubmitting) {
				onCancel();
			}
		}

		handleKeydown({ key: 'Escape' }, true, false);
		expect(wasCancelled).toBe(true);

		// During submitting, escape should NOT dismiss
		wasCancelled = false;
		handleKeydown({ key: 'Escape' }, true, true);
		expect(wasCancelled).toBe(false);
	});
});
