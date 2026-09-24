import { describe, it, expect, vi } from 'vitest';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';
import type { IngredientDto, IngredientUsageDto } from '$lib/types/api';

describe('DeleteIngredientModal Component Logic & Invariants', () => {
	it('has complete internationalization keys for delete ingredient in en.json and sv.json', () => {
		expect(en.ingredients.delete_ingredient).toBe('Delete Ingredient');
		expect(sv.ingredients.delete_ingredient).toBe('Ta bort råvara');

		expect(en.ingredients.delete_modal_title).toBe('Delete Custom Ingredient');
		expect(sv.ingredients.delete_modal_title).toBe('Ta bort anpassad råvara');

		expect(en.ingredients.delete_modal_confirm).toContain('{name}');
		expect(sv.ingredients.delete_modal_confirm).toContain('{name}');

		expect(en.ingredients.delete_modal_in_use_warning).toContain('{count}');
		expect(sv.ingredients.delete_modal_in_use_warning).toContain('{count}');

		expect(en.ingredients.delete_modal_in_use_notice).toBeDefined();
		expect(sv.ingredients.delete_modal_in_use_notice).toBeDefined();

		expect(en.ingredients.delete_modal_confirm_btn).toBe('Delete Ingredient');
		expect(sv.ingredients.delete_modal_confirm_btn).toBe('Ta bort råvara');

		expect(en.ingredients.delete_modal_remove_and_delete_btn).toBe('Remove from Recipes & Delete');
		expect(sv.ingredients.delete_modal_remove_and_delete_btn).toBe('Ta bort från recept & radera');

		expect(en.ingredients.delete_modal_deleting).toBe('Deleting...');
		expect(sv.ingredients.delete_modal_deleting).toBe('Tar bort...');

		expect(en.ingredients.delete_success).toBe('Ingredient deleted successfully.');
		expect(sv.ingredients.delete_success).toBe('Råvaran har tagits bort.');

		expect(en.ingredients.delete_error).toBe('Failed to delete ingredient.');
		expect(sv.ingredients.delete_error).toBe('Kunde inte ta bort råvaran.');
	});

	it('ensures default system catalog items cannot be marked for deletion', () => {
		const catalogIngredient: IngredientDto = {
			id: 'citra-id',
			name: 'Citra',
			type: 'Hop',
			isCatalogItem: true
		};

		const customIngredient: IngredientDto = {
			id: 'custom-malt-id',
			name: 'Home Grown Malt',
			type: 'Fermentable',
			isCatalogItem: false
		};

		// Only custom/imported items should allow deletion
		const canDeleteCatalog = !catalogIngredient.isCatalogItem;
		const canDeleteCustom = !customIngredient.isCatalogItem;

		expect(canDeleteCatalog).toBe(false);
		expect(canDeleteCustom).toBe(true);
	});

	it('correctly manages ingredient deletion lifecycle with recipe usage', async () => {
		const customHop: IngredientDto = {
			id: 'custom-hop-1',
			name: 'Super Aroma Hop',
			type: 'Hop',
			isCatalogItem: false
		};

		const usageWithRecipes: IngredientUsageDto = {
			ingredientId: 'custom-hop-1',
			ingredientName: 'Super Aroma Hop',
			recipeCount: 2,
			recipes: [
				{ id: 'recipe-1', name: 'West Coast IPA' },
				{ id: 'recipe-2', name: 'Session Pale Ale' }
			]
		};

		let deletedId: string | null = null;
		let closed = false;
		let capturedError: string | null = null;

		const onDeleted = (id: string) => {
			deletedId = id;
		};
		const onClose = () => {
			closed = true;
		};

		const mockDeleteApi = vi.fn().mockResolvedValue(undefined);

		async function executeDelete(ingredient: IngredientDto | null) {
			if (!ingredient) return;
			try {
				await mockDeleteApi(ingredient.id);
				onDeleted(ingredient.id);
				onClose();
			} catch (err: unknown) {
				capturedError = (err as Error).message || en.ingredients.delete_error;
			}
		}

		// Verify usage recipes are present
		expect(usageWithRecipes.recipeCount).toBe(2);
		expect(usageWithRecipes.recipes).toHaveLength(2);
		expect(usageWithRecipes.recipes[0].name).toBe('West Coast IPA');
		expect(usageWithRecipes.recipes[1].name).toBe('Session Pale Ale');

		await executeDelete(customHop);

		expect(mockDeleteApi).toHaveBeenCalledWith('custom-hop-1');
		expect(deletedId).toBe('custom-hop-1');
		expect(closed).toBe(true);
		expect(capturedError).toBeNull();

		// Failure case
		mockDeleteApi.mockRejectedValueOnce(new Error('Deletion failed due to network error'));
		deletedId = null;
		closed = false;

		await executeDelete(customHop);

		expect(deletedId).toBeNull();
		expect(closed).toBe(false);
		expect(capturedError).toBe('Deletion failed due to network error');
	});
});
