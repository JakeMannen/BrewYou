import type { IngredientUsage, RecipeIngredientOutputDto } from '$lib/types/api';

export interface PrintableBoilRow {
	key: string;
	label: string;
	durationText: string;
	targetTempC: number;
	items: RecipeIngredientOutputDto[];
}

export interface PrintableStageIngredients {
	fermentableIngredients: RecipeIngredientOutputDto[];
	mashNonFermentableIngredients: RecipeIngredientOutputDto[];
	fermentationIngredients: RecipeIngredientOutputDto[];
	packagingIngredients: RecipeIngredientOutputDto[];
}

export interface PrintableTimingLabels {
	rampLabel: string;
	whirlpoolLabel: string;
	flameoutLabel: string;
	minutesShortLabel: string;
	firstWortDurationText: string;
	rampDurationText: string;
	usages: Record<IngredientUsage, string>;
}

function normalizeBoilDuration(
	durationMinutes: number | null | undefined,
	boilTimeMinutes: number
): number {
	if (durationMinutes === null || durationMinutes === undefined) {
		return boilTimeMinutes;
	}
	const parsed = Number(durationMinutes);
	if (!Number.isFinite(parsed)) {
		return boilTimeMinutes;
	}
	return Math.max(0, parsed);
}

/**
 * Partitions recipe ingredients into the sections printed on the brew day record sheet
 * so non-mash fermentables (e.g., boil sugars and extracts) appear in Stage 1's bill
 * as well as their chronological boil/whirlpool/packaging stages.
 */
export function splitPrintableStageIngredients(
	ingredients: readonly RecipeIngredientOutputDto[]
): PrintableStageIngredients {
	return {
		fermentableIngredients: ingredients.filter((i) => i.ingredientType === 'Fermentable'),
		mashNonFermentableIngredients: ingredients.filter(
			(i) => i.usage === 'Mash' && i.ingredientType !== 'Fermentable'
		),
		fermentationIngredients: ingredients.filter(
			(i) =>
				i.usage === 'Primary' ||
				i.usage === 'Secondary' ||
				i.usage === 'DryHop' ||
				i.ingredientType === 'Yeast'
		),
		packagingIngredients: ingredients.filter((i) => i.usage === 'Bottling')
	};
}

/**
 * Builds chronological Stage 2 boil rows (Ramp / First Wort -> Timed Boil buckets -> Whirlpool)
 * for all kettle ingredients including hops, boil fermentables/sugars, and miscs.
 */
export function buildPrintableBoilSchedule(
	ingredients: readonly RecipeIngredientOutputDto[],
	boilTimeMinutes: number,
	labels: Pick<
		PrintableTimingLabels,
		| 'rampLabel'
		| 'whirlpoolLabel'
		| 'flameoutLabel'
		| 'minutesShortLabel'
		| 'firstWortDurationText'
		| 'rampDurationText'
	>
): PrintableBoilRow[] {
	const rows: PrintableBoilRow[] = [];
	const boilTime = boilTimeMinutes > 0 ? boilTimeMinutes : 60;

	const firstWort = ingredients.filter((i) => i.usage === 'FirstWort');
	rows.push({
		key: 'ramp',
		label: labels.rampLabel,
		durationText: firstWort.length > 0 ? labels.firstWortDurationText : labels.rampDurationText,
		targetTempC: 100,
		items: firstWort
	});

	const boilItems = ingredients.filter((i) => i.usage === 'Boil');
	const timeBuckets: Record<number, RecipeIngredientOutputDto[]> = {};
	for (const item of boilItems) {
		const mins = normalizeBoilDuration(item.durationMinutes, boilTime);
		if (!timeBuckets[mins]) {
			timeBuckets[mins] = [];
		}
		timeBuckets[mins].push(item);
	}

	const sortedTimes = Object.keys(timeBuckets)
		.map((k) => Number(k))
		.sort((a, b) => b - a);
	for (const mins of sortedTimes) {
		const items = timeBuckets[mins] ?? [];
		const formattedMins = `${mins} ${labels.minutesShortLabel}`;
		rows.push({
			key: `boil-${mins}`,
			label: mins === 0 ? labels.flameoutLabel : formattedMins,
			durationText: formattedMins,
			targetTempC: 100,
			items
		});
	}

	const whirlpoolItems = ingredients.filter((i) => i.usage === 'Whirlpool');
	if (whirlpoolItems.length > 0) {
		const maxWhirlpool = Math.max(
			15,
			...whirlpoolItems.map((w) => normalizeBoilDuration(w.durationMinutes, 15))
		);
		rows.push({
			key: 'whirlpool',
			label: labels.whirlpoolLabel,
			durationText: `${maxWhirlpool} ${labels.minutesShortLabel}`,
			targetTempC: 85,
			items: whirlpoolItems
		});
	}

	return rows;
}

/**
 * Formats an ingredient's stage and optional boil/whirlpool timing into a concise localized badge string.
 */
export function formatPrintableStageTiming(
	item: Pick<RecipeIngredientOutputDto, 'usage' | 'durationMinutes'>,
	labels: Pick<PrintableTimingLabels, 'flameoutLabel' | 'minutesShortLabel' | 'usages'>
): string {
	const baseUsage = labels.usages[item.usage] ?? item.usage;
	const hasDuration =
		item.durationMinutes !== null &&
		item.durationMinutes !== undefined &&
		Number.isFinite(Number(item.durationMinutes));

	if (!hasDuration) {
		return baseUsage;
	}

	const mins = Math.max(0, Number(item.durationMinutes));
	if (item.usage === 'Boil') {
		return mins === 0
			? `${baseUsage} • ${labels.flameoutLabel}`
			: `${baseUsage} • ${mins} ${labels.minutesShortLabel}`;
	}
	if ((item.usage === 'FirstWort' || item.usage === 'Whirlpool') && mins > 0) {
		return `${baseUsage} • ${mins} ${labels.minutesShortLabel}`;
	}

	return baseUsage;
}
