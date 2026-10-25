import { t } from '$lib/i18n/index.svelte';
import type { IngredientType } from '$lib/types/api';

export function normalizeIngredientKey(name: string): string {
	return name
		.toLowerCase()
		.replace(/ü/g, 'u')
		.replace(/ä/g, 'a')
		.replace(/ö/g, 'o')
		.replace(/[^a-z0-9]+/g, '_')
		.replace(/^_+|_+$/g, '');
}

/**
 * Ingredient names are not localized to preserve standard brewing ingredient names.
 */
export function getLocalizedIngredientName(item: { name: string }): string {
	return item.name;
}

/**
 * Ingredient descriptions are not localized to preserve original brewing notes.
 */
export function getLocalizedIngredientDescription(item: {
	name?: string;
	description?: string | null;
}): string {
	return item.description ?? '';
}

export function getLocalizedIngredientType(type: IngredientType | string): string {
	switch (type) {
		case 'Fermentable':
			return t('ingredients.type_fermentable');
		case 'Hop':
			return t('ingredients.type_hop');
		case 'Yeast':
			return t('ingredients.type_yeast');
		default:
			return t('ingredients.type_other');
	}
}

export function getLocalizedIngredientForm(form?: string | null): string {
	if (!form) return '';
	const key = form.toLowerCase().trim();
	const translationKey = `ingredients.forms.${key}`;
	const translated = t(translationKey);
	return translated !== translationKey ? translated : form;
}
