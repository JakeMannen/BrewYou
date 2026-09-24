import type { BatchIngredientDto, IngredientUsage } from '$lib/types/api';

export function getStageOrderPriority(usage: IngredientUsage): number {
	switch (usage) {
		case 'Mash':
			return 1;
		case 'Boil':
			return 2;
		case 'Whirlpool':
			return 3;
		case 'Primary':
			return 4;
		case 'Secondary':
			return 5;
		case 'DryHop':
			return 6;
		case 'Bottling':
			return 7;
		default:
			return 8;
	}
}

/**
 * Sorts batch ingredients in strict chronological addition order:
 * 1. Stages ordered by process: Mash -> Boil -> Whirlpool -> Primary -> Secondary -> DryHop -> Bottling.
 * 2. Boil & Whirlpool additions: AdditionTimeMinutes represents minutes remaining in the boil/whirlpool.
 *    Therefore, the first addition added to the kettle (e.g. 60m) is FIRST in the list (descending time).
 * 3. Mash additions: Grains first (by amount descending), followed by salts/enzymes.
 * 4. Primary: Yeast pitched first, then mid-fermentation additions.
 * 5. DryHop / Secondary: By addition time/day.
 */
export function sortIngredientsChronologically(
	ingredients: BatchIngredientDto[]
): BatchIngredientDto[] {
	return [...ingredients].sort((a, b) => {
		const pA = getStageOrderPriority(a.additionStage);
		const pB = getStageOrderPriority(b.additionStage);
		if (pA !== pB) return pA - pB;

		// Boil & Whirlpool: higher minutes remaining first (e.g. 60m before 15m, 15m before 0m)
		if (a.additionStage === 'Boil' || a.additionStage === 'Whirlpool') {
			const tA = a.additionTimeMinutes ?? -1;
			const tB = b.additionTimeMinutes ?? -1;
			if (tA !== tB) return tB - tA;
		}

		// Mash: Fermentables first, then larger amount first
		if (a.additionStage === 'Mash') {
			if (a.type === 'Fermentable' && b.type !== 'Fermentable') return -1;
			if (b.type === 'Fermentable' && a.type !== 'Fermentable') return 1;
			if (b.amount !== a.amount) return Number(b.amount) - Number(a.amount);
		}

		// Primary: Yeast first
		if (a.additionStage === 'Primary') {
			if (a.type === 'Yeast' && b.type !== 'Yeast') return -1;
			if (b.type === 'Yeast' && a.type !== 'Yeast') return 1;
		}

		// DryHop / Secondary
		if (a.additionStage === 'DryHop' || a.additionStage === 'Secondary') {
			const tA = a.additionTimeMinutes ?? 0;
			const tB = b.additionTimeMinutes ?? 0;
			if (tA !== tB) return tA - tB;
		}

		if (b.amount !== a.amount) return Number(b.amount) - Number(a.amount);
		return a.name.localeCompare(b.name);
	});
}
