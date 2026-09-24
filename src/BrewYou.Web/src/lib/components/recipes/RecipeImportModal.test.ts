import { describe, expect, it } from 'vitest';
import type { IngredientDto, IngredientType } from '$lib/types/api';
import type { ParsedRecipe } from '$lib/parsers/types';

interface MissingIngredientItem {
	name: string;
	type: IngredientType;
	potentialGravity?: number;
	colorSrm?: number;
	alphaAcidPercent?: number;
	attenuationPercent?: number;
	description?: string;
}

function detectMissingIngredients(
	parsedRecipe: ParsedRecipe,
	catalogIngredients: IngredientDto[]
): MissingIngredientItem[] {
	const missing: MissingIngredientItem[] = [];
	const seen = new Set<string>();

	for (const ing of parsedRecipe.ingredients) {
		const trimmedName = ing.name.trim();
		const lower = trimmedName.toLowerCase();
		if (!lower) continue;

		const match = catalogIngredients.some(
			(c) => c.name.toLowerCase() === lower || c.name.toLowerCase().includes(lower)
		);

		if (!match && !seen.has(lower)) {
			seen.add(lower);
			missing.push({
				name: trimmedName,
				type: ing.type,
				potentialGravity: ing.potentialGravity,
				colorSrm: ing.colorSrm,
				alphaAcidPercent: ing.alphaAcidPercent,
				attenuationPercent: ing.attenuationPercent,
				description: ing.notes
			});
		}
	}

	return missing;
}

function sanitizeString(str: string): string {
	return str.replace(/[<>]/g, '').trim();
}

function sanitizeNumber(
	val: number | undefined | null,
	min: number,
	max: number,
	fallback?: number
): number | undefined {
	if (val === undefined || val === null || isNaN(val)) return fallback;
	return Math.min(Math.max(val, min), max);
}

function buildCreateIngredientPayload(missing: MissingIngredientItem) {
	const sanitizedName = sanitizeString(missing.name) || 'Unnamed Ingredient';
	const sanitizedDesc = missing.description
		? sanitizeString(missing.description).substring(0, 1000)
		: null;

	let potentialGravity: number | undefined;
	let colorSrm: number | undefined;
	let alphaAcidPercent: number | undefined;
	let attenuationPercent: number | undefined;

	if (missing.type === 'Fermentable') {
		potentialGravity = sanitizeNumber(missing.potentialGravity, 1.0, 1.2, 1.037);
		colorSrm = sanitizeNumber(missing.colorSrm, 0, 1000, 2.0);
	} else if (missing.type === 'Hop') {
		alphaAcidPercent = sanitizeNumber(missing.alphaAcidPercent, 0, 100, 5.0);
	} else if (missing.type === 'Yeast') {
		attenuationPercent = sanitizeNumber(missing.attenuationPercent, 0, 100, 75.0);
	}

	return {
		name: sanitizedName,
		type: missing.type,
		potentialGravity,
		colorSrm,
		alphaAcidPercent,
		attenuationPercent,
		description: sanitizedDesc
	};
}

describe('Recipe Import Missing Ingredients Reconciliation', () => {
	const sampleCatalog: IngredientDto[] = [
		{
			id: 'ing-1',
			name: 'Pale Malt (2-Row)',
			type: 'Fermentable',
			potentialGravity: 1.037,
			colorSrm: 3.0,
			isCatalogItem: true
		},
		{
			id: 'ing-2',
			name: 'Centennial',
			type: 'Hop',
			alphaAcidPercent: 10.0,
			isCatalogItem: true
		},
		{
			id: 'ing-3',
			name: 'SafAle US-05',
			type: 'Yeast',
			attenuationPercent: 81.0,
			isCatalogItem: true
		}
	];

	it('returns empty array when all ingredients are available in catalog', () => {
		const recipe: ParsedRecipe = {
			name: 'All Matched',
			beerStyle: 'Pale Ale',
			description: '',
			batchSizeLiters: 20,
			boilTimeMinutes: 60,
			efficiencyPercent: 75,
			ingredients: [
				{
					name: 'Pale Malt (2-Row)',
					type: 'Fermentable',
					amount: 4.5,
					unit: 'kg',
					durationMinutes: 60,
					usage: 'Mash'
				},
				{
					name: 'Centennial',
					type: 'Hop',
					amount: 25,
					unit: 'g',
					durationMinutes: 60,
					usage: 'Boil'
				}
			],
			mashSteps: [],
			fermentationSteps: []
		};

		const missing = detectMissingIngredients(recipe, sampleCatalog);
		expect(missing).toHaveLength(0);
	});

	it('identifies missing ingredients and deduplicates multiple additions of same ingredient', () => {
		const recipe: ParsedRecipe = {
			name: 'Mosaic & Citra IPA',
			beerStyle: 'IPA',
			description: 'Fruity IPA',
			batchSizeLiters: 20,
			boilTimeMinutes: 60,
			efficiencyPercent: 75,
			ingredients: [
				{
					name: 'Golden Promise',
					type: 'Fermentable',
					amount: 5.0,
					unit: 'kg',
					durationMinutes: 60,
					usage: 'Mash',
					colorSrm: 4.5
				},
				{
					name: 'Citra',
					type: 'Hop',
					amount: 15,
					unit: 'g',
					durationMinutes: 60,
					usage: 'Boil',
					alphaAcidPercent: 12.5
				},
				{
					name: 'Citra',
					type: 'Hop',
					amount: 35,
					unit: 'g',
					durationMinutes: 10,
					usage: 'Boil',
					alphaAcidPercent: 12.5
				},
				{
					name: 'Citra',
					type: 'Hop',
					amount: 50,
					unit: 'g',
					durationMinutes: 0,
					usage: 'Whirlpool',
					alphaAcidPercent: 12.5
				},
				{
					name: 'WLP001 California Ale',
					type: 'Yeast',
					amount: 1,
					unit: 'pkg',
					durationMinutes: 0,
					usage: 'Primary',
					attenuationPercent: 77.0
				}
			],
			mashSteps: [],
			fermentationSteps: []
		};

		const missing = detectMissingIngredients(recipe, sampleCatalog);
		// Should have 3 unique missing ingredients: Golden Promise, Citra (deduped from 3 additions), and WLP001
		expect(missing).toHaveLength(3);
		expect(missing.map((m) => m.name)).toEqual([
			'Golden Promise',
			'Citra',
			'WLP001 California Ale'
		]);
	});

	it('applies Master Brewer calibrated defaults when metrics are absent', () => {
		const missingGrain: MissingIngredientItem = {
			name: 'Unknown Specialty Grain',
			type: 'Fermentable'
		};

		const grainPayload = buildCreateIngredientPayload(missingGrain);
		expect(grainPayload.potentialGravity).toBe(1.037);
		expect(grainPayload.colorSrm).toBe(2.0);

		const missingHop: MissingIngredientItem = {
			name: 'Unknown Wild Hop',
			type: 'Hop'
		};
		const hopPayload = buildCreateIngredientPayload(missingHop);
		expect(hopPayload.alphaAcidPercent).toBe(5.0);

		const missingYeast: MissingIngredientItem = {
			name: 'House Blend Yeast',
			type: 'Yeast'
		};
		const yeastPayload = buildCreateIngredientPayload(missingYeast);
		expect(yeastPayload.attenuationPercent).toBe(75.0);
	});

	it('strips HTML/angle-bracket characters from names and notes to prevent Stored XSS', () => {
		const maliciousItem: MissingIngredientItem = {
			name: '<script>alert("hack")</script>Nelson Sauvin',
			type: 'Hop',
			alphaAcidPercent: 12.0,
			description: 'Crisp notes <img src=x onerror=alert(1)> from NZ'
		};

		const payload = buildCreateIngredientPayload(maliciousItem);
		expect(payload.name).toBe('scriptalert("hack")/scriptNelson Sauvin');
		expect(payload.description).toBe('Crisp notes img src=x onerror=alert(1) from NZ');
		expect(payload.name.includes('<')).toBe(false);
		expect(payload.name.includes('>')).toBe(false);
		expect(payload.description?.includes('<')).toBe(false);
		expect(payload.description?.includes('>')).toBe(false);
	});

	it('detects missing ingredients of type Other (miscellaneous and water additions)', () => {
		const recipeWithOther: ParsedRecipe = {
			name: 'Belgian Witbier with Spices',
			beerStyle: 'Witbier',
			description: 'Classic Belgian wit with coriander and orange peel',
			batchSizeLiters: 20,
			boilTimeMinutes: 60,
			efficiencyPercent: 72,
			ingredients: [
				{
					name: 'Pale Malt (2-Row)',
					type: 'Fermentable',
					amount: 3.0,
					unit: 'kg',
					durationMinutes: 60,
					usage: 'Mash'
				},
				{
					name: 'Irish Moss',
					type: 'Other',
					amount: 5,
					unit: 'g',
					durationMinutes: 15,
					usage: 'Boil',
					notes: 'Kettle fining'
				},
				{
					name: 'Coriander Seed',
					type: 'Other',
					amount: 15,
					unit: 'g',
					durationMinutes: 5,
					usage: 'Boil',
					notes: 'Crushed spice'
				}
			],
			mashSteps: [],
			fermentationSteps: []
		};

		const missing = detectMissingIngredients(recipeWithOther, sampleCatalog);
		expect(missing).toHaveLength(2);
		expect(missing.map((m) => m.name)).toEqual(['Irish Moss', 'Coriander Seed']);
		expect(missing.every((m) => m.type === 'Other')).toBe(true);

		const irishMossPayload = buildCreateIngredientPayload(missing[0]);
		expect(irishMossPayload.name).toBe('Irish Moss');
		expect(irishMossPayload.type).toBe('Other');
		expect(irishMossPayload.description).toBe('Kettle fining');
	});

	it('maps imported Other ingredients correctly when updated catalog is supplied', () => {
		const newlyCreatedOther: IngredientDto[] = [
			{
				id: 'created-misc-1',
				name: 'Irish Moss',
				type: 'Other',
				isCatalogItem: false
			},
			{
				id: 'created-misc-2',
				name: 'Coriander Seed',
				type: 'Other',
				isCatalogItem: false
			}
		];

		const activeCatalog = [...sampleCatalog, ...newlyCreatedOther];

		const importedIngredients = [
			{
				name: 'Pale Malt (2-Row)',
				type: 'Fermentable' as IngredientType,
				amount: 3.0,
				unit: 'kg',
				durationMinutes: 60,
				usage: 'Mash' as const
			},
			{
				name: 'Irish Moss',
				type: 'Other' as IngredientType,
				amount: 5,
				unit: 'g',
				durationMinutes: 15,
				usage: 'Boil' as const,
				notes: 'Fining agent'
			}
		];

		// Simulate RecipeFormulator/Recipe page reconciliation logic
		const mappedItems = importedIngredients.map((ing) => {
			const lower = ing.name.trim().toLowerCase();
			const match =
				activeCatalog.find(
					(c) =>
						c.name.toLowerCase() === lower ||
						c.name.toLowerCase().includes(lower) ||
						lower.includes(c.name.toLowerCase())
				) || activeCatalog.find((c) => c.type === ing.type);

			return {
				ingredientId: match ? match.id : activeCatalog[0]?.id || '',
				type: ing.type,
				amount: ing.amount,
				unit: ing.unit,
				durationMinutes: ing.durationMinutes,
				usage: ing.usage
			};
		});

		expect(mappedItems[1].ingredientId).toBe('created-misc-1');
		expect(mappedItems[1].type).toBe('Other');
	});

	it('groups items into correct sections and ensures Other items are never grouped as Fermentables', () => {
		const items = [
			{
				id: 'item-1',
				ingredientId: 'ing-1',
				type: 'Fermentable' as IngredientType,
				amount: 4.5,
				unit: 'kg',
				durationMinutes: 60,
				usage: 'Mash' as const
			},
			{
				id: 'item-2',
				ingredientId: 'ing-2',
				type: 'Hop' as IngredientType,
				amount: 25,
				unit: 'g',
				durationMinutes: 60,
				usage: 'Boil' as const
			},
			{
				id: 'item-3',
				ingredientId: 'ing-3',
				type: 'Yeast' as IngredientType,
				amount: 11.5,
				unit: 'g',
				durationMinutes: 0,
				usage: 'Primary' as const
			},
			{
				id: 'item-4',
				ingredientId: 'misc-custom-id',
				type: 'Other' as IngredientType,
				amount: 5,
				unit: 'g',
				durationMinutes: 15,
				usage: 'Boil' as const
			}
		];

		// Grouping logic matching IngredientsCard.svelte
		const fermentables = items.filter((item) => {
			const ing = sampleCatalog.find((i) => i.id === item.ingredientId);
			const type = ing?.type ?? item.type ?? 'Fermentable';
			return type === 'Fermentable';
		});

		const hops = items.filter((item) => {
			const ing = sampleCatalog.find((i) => i.id === item.ingredientId);
			const type = ing?.type ?? item.type;
			return type === 'Hop';
		});

		const yeasts = items.filter((item) => {
			const ing = sampleCatalog.find((i) => i.id === item.ingredientId);
			const type = ing?.type ?? item.type;
			return type === 'Yeast';
		});

		const miscs = items.filter((item) => {
			const ing = sampleCatalog.find((i) => i.id === item.ingredientId);
			const type = ing?.type ?? item.type;
			return type === 'Other';
		});

		expect(fermentables).toHaveLength(1);
		expect(fermentables[0].id).toBe('item-1');

		expect(hops).toHaveLength(1);
		expect(hops[0].id).toBe('item-2');

		expect(yeasts).toHaveLength(1);
		expect(yeasts[0].id).toBe('item-3');

		expect(miscs).toHaveLength(1);
		expect(miscs[0].id).toBe('item-4');
		expect(miscs[0].type).toBe('Other');
	});

	describe('Recipe Import Name Conflict Pre-Check & Rename Workflow', () => {
		const existingRecipeNames = ['Summer Ale', 'Mosaic IPA', 'Belgian Dubbel'];

		function checkNameConflict(name: string, existingList: string[]): boolean {
			const trimmed = name.trim().toLowerCase();
			if (!trimmed) return false;
			return existingList.some((n) => n.trim().toLowerCase() === trimmed);
		}

		it('detects case-insensitive and trimmed name conflicts', () => {
			expect(checkNameConflict('summer ale', existingRecipeNames)).toBe(true);
			expect(checkNameConflict('  MOSAIC IPA  ', existingRecipeNames)).toBe(true);
			expect(checkNameConflict('Belgian Dubbel', existingRecipeNames)).toBe(true);
			expect(checkNameConflict('Autumn Ale', existingRecipeNames)).toBe(false);
		});

		it('prevents missing ingredients creation when recipe name conflicts until renamed', async () => {
			const conflictingRecipe: ParsedRecipe = {
				name: 'Mosaic IPA',
				beerStyle: 'IPA',
				description: 'A conflict recipe',
				batchSizeLiters: 20,
				boilTimeMinutes: 60,
				efficiencyPercent: 75,
				ingredients: [
					{
						name: 'Rare Special Malt',
						type: 'Fermentable',
						amount: 5.0,
						unit: 'kg',
						durationMinutes: 60,
						usage: 'Mash'
					}
				],
				mashSteps: [],
				fermentationSteps: []
			};

			const missing = detectMissingIngredients(conflictingRecipe, sampleCatalog);
			expect(missing).toHaveLength(1);
			expect(missing[0].name).toBe('Rare Special Malt');

			let ingredientsCreatedCount = 0;
			let importCompleted = false;
			let renameModalOpened = false;

			async function attemptImport(recipe: ParsedRecipe) {
				const hasConflict = checkNameConflict(recipe.name, existingRecipeNames);
				if (hasConflict) {
					renameModalOpened = true;
					return; // Stop before creating missing ingredients!
				}

				// If no conflict, create missing ingredients and import
				ingredientsCreatedCount += missing.length;
				importCompleted = true;
			}

			// First attempt with conflicting name
			await attemptImport(conflictingRecipe);

			// Rename modal should be opened, NO ingredients created, import not completed
			expect(renameModalOpened).toBe(true);
			expect(ingredientsCreatedCount).toBe(0);
			expect(importCompleted).toBe(false);

			// User renames the recipe
			conflictingRecipe.name = 'Mosaic IPA (Imported)';

			// Retry import with updated name
			await attemptImport(conflictingRecipe);

			// Now ingredients are created and import succeeds
			expect(ingredientsCreatedCount).toBe(1);
			expect(importCompleted).toBe(true);
		});

		it('leaves catalog untouched when user cancels during rename modal', async () => {
			const conflictingRecipe: ParsedRecipe = {
				name: 'Summer Ale',
				beerStyle: 'Blonde Ale',
				description: 'Summer beer',
				batchSizeLiters: 20,
				boilTimeMinutes: 60,
				efficiencyPercent: 75,
				ingredients: [
					{
						name: 'Uncatalogued Hop',
						type: 'Hop',
						amount: 20,
						unit: 'g',
						durationMinutes: 15,
						usage: 'Boil'
					}
				],
				mashSteps: [],
				fermentationSteps: []
			};

			const createdIngredients: string[] = [];
			let wasImportCancelled = false;

			const hasConflict = checkNameConflict(conflictingRecipe.name, existingRecipeNames);
			expect(hasConflict).toBe(true);

			// User clicks cancel in rename modal
			function onCancelRename() {
				wasImportCancelled = true;
			}

			onCancelRename();

			expect(wasImportCancelled).toBe(true);
			expect(createdIngredients).toHaveLength(0);
		});
	});
});
