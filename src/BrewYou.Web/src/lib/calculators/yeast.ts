import { sgToPlatoASBC } from './calculators';

export type YeastStyleCategory = 'Ale' | 'Hybrid' | 'Lager' | 'Custom';
export type YeastFormFactor = 'Liquid' | 'Dry' | 'Slurry';
export type YeastStarterMethod = 'None' | 'Simple' | 'Shaken' | 'StirPlate';

export interface YeastPitchInput {
	batchVolumeLiters: number;
	originalGravity: number;
	styleCategory: YeastStyleCategory;
	customPitchRateMillionPerMlPlato?: number | null;
	yeastForm: YeastFormFactor;
	packageAgeDays: number;
	baseViabilityPercent?: number;
	cellsPerUnitBillions?: number;
	packsAvailable: number;
	slurryDensityBillionPerMl?: number;
	slurryVolumeAvailableMl?: number;
	starterMethod: YeastStarterMethod;
	starterVolumeLiters?: number | null;
}

export interface YeastPitchResult {
	plato: number;
	targetPitchRateMillionPerMlPlato: number;
	requiredCellsBillions: number;
	estimatedViabilityPercent: number;
	viableCellsPerUnitBillions: number;
	initialViableCellsBillions: number;
	deficitCellsBillions: number;
	dryPacksExact: number;
	dryPacksRecommended: number;
	requiredSlurryMl: number;
	recommendedStarterVolumeLiters: number;
	starterDmeGrams: number;
	projectedStarterCellsBillions: number;
	needsMultiStepStarter: boolean;
	recommendationSummaryKey:
		'PitchSufficient' | 'PitchMorePacks' | 'StarterRecommended' | 'StepUpStarterRecommended';
}

export const DEFAULT_LIQUID_PACK_CELLS_BILLIONS = 100;
export const DEFAULT_DRY_PACK_CELLS_BILLIONS = 115;

export function resolveTargetPitchRate(
	styleCategory: YeastStyleCategory,
	customRate: number | null | undefined,
	originalGravity: number
): number {
	if (customRate && customRate > 0) return Number(customRate.toFixed(2));
	if (styleCategory === 'Lager') return originalGravity >= 1.065 ? 2.0 : 1.5;
	if (styleCategory === 'Hybrid') return originalGravity >= 1.07 ? 1.25 : 1.0;
	return originalGravity >= 1.065 ? 1.0 : 0.75;
}

export function calculateYeastViability(
	form: YeastFormFactor,
	ageDays: number,
	baseViabilityPercent = 98
): number {
	const months = Math.max(0, ageDays) / 30;
	const start = Math.min(100, Math.max(1, baseViabilityPercent));
	const decayPerMonth = form === 'Dry' ? 2 : form === 'Slurry' ? 25 : 20;
	const floor = form === 'Dry' ? 10 : 5;
	return Number(Math.min(100, Math.max(floor, start - decayPerMonth * months)).toFixed(1));
}

export function calculateYeastPitch(input: YeastPitchInput): YeastPitchResult {
	const volL = Math.max(0.1, input.batchVolumeLiters);
	const og = Math.min(1.2, Math.max(1.0, input.originalGravity));
	const plato = Number(Math.max(0, sgToPlatoASBC(og)).toFixed(2));
	const pitchRate = resolveTargetPitchRate(
		input.styleCategory,
		input.customPitchRateMillionPerMlPlato,
		og
	);

	const requiredCellsBillions = Number((pitchRate * volL * plato).toFixed(1));
	const estimatedViabilityPercent = calculateYeastViability(
		input.yeastForm,
		input.packageAgeDays,
		input.baseViabilityPercent ?? 98
	);

	const nominalPerUnit =
		input.cellsPerUnitBillions ??
		(input.yeastForm === 'Dry'
			? DEFAULT_DRY_PACK_CELLS_BILLIONS
			: input.yeastForm === 'Slurry'
				? Math.max(0.1, input.slurryDensityBillionPerMl ?? 1.0)
				: DEFAULT_LIQUID_PACK_CELLS_BILLIONS);

	const viableCellsPerUnitBillions = Number(
		(nominalPerUnit * (estimatedViabilityPercent / 100)).toFixed(2)
	);
	const unitsCount =
		input.yeastForm === 'Slurry'
			? Math.max(0, input.slurryVolumeAvailableMl ?? 0)
			: Math.max(1, input.packsAvailable);

	const initialViableCellsBillions = Number((viableCellsPerUnitBillions * unitsCount).toFixed(1));
	const deficitCellsBillions = Number(
		Math.max(0, requiredCellsBillions - initialViableCellsBillions).toFixed(1)
	);

	const dryViablePerPack = Math.max(
		1,
		DEFAULT_DRY_PACK_CELLS_BILLIONS *
			(calculateYeastViability('Dry', input.packageAgeDays, input.baseViabilityPercent ?? 98) / 100)
	);
	const dryPacksExact =
		requiredCellsBillions > 0 ? Number((requiredCellsBillions / dryViablePerPack).toFixed(2)) : 0;
	const dryPacksRecommended = Math.max(1, Math.ceil(dryPacksExact));

	const slurryDensity = Math.max(0.1, input.slurryDensityBillionPerMl ?? 1.0);
	const slurryViablePerMl =
		slurryDensity *
		(calculateYeastViability('Slurry', input.packageAgeDays, input.baseViabilityPercent ?? 95) /
			100);
	const requiredSlurryMl =
		slurryViablePerMl > 0 ? Math.round(requiredCellsBillions / slurryViablePerMl) : 0;

	const yieldMap: Record<YeastStarterMethod, number> = {
		StirPlate: 1.4,
		Shaken: 0.75,
		Simple: 0.45,
		None: 0
	};
	const yieldPerG = yieldMap[input.starterMethod] ?? 0;

	let recommendedStarterVolumeLiters = 0;
	let starterDmeGrams = 0;
	let projectedStarterCellsBillions = initialViableCellsBillions;

	if (yieldPerG > 0 && ((input.starterVolumeLiters ?? 0) > 0 || deficitCellsBillions > 0)) {
		if (input.starterVolumeLiters && input.starterVolumeLiters > 0) {
			recommendedStarterVolumeLiters = Number(input.starterVolumeLiters.toFixed(2));
		} else if (deficitCellsBillions > 0) {
			const neededDme = deficitCellsBillions / yieldPerG;
			recommendedStarterVolumeLiters = Number(
				Math.min(5.0, Math.max(0.5, neededDme / 100)).toFixed(2)
			);
		}

		if (recommendedStarterVolumeLiters > 0) {
			starterDmeGrams = Math.round(recommendedStarterVolumeLiters * 100);
			const rawGrowth = starterDmeGrams * yieldPerG;
			const maxCeiling = initialViableCellsBillions * 5.5;
			projectedStarterCellsBillions = Number(
				Math.min(initialViableCellsBillions + rawGrowth, maxCeiling).toFixed(1)
			);
		}
	}

	const needsMultiStepStarter =
		yieldPerG > 0 &&
		((deficitCellsBillions > 0 && projectedStarterCellsBillions < requiredCellsBillions) ||
			recommendedStarterVolumeLiters > 3.0);

	const recommendationSummaryKey =
		deficitCellsBillions <= 0
			? 'PitchSufficient'
			: yieldPerG <= 0
				? 'PitchMorePacks'
				: needsMultiStepStarter
					? 'StepUpStarterRecommended'
					: 'StarterRecommended';

	return {
		plato,
		targetPitchRateMillionPerMlPlato: pitchRate,
		requiredCellsBillions,
		estimatedViabilityPercent,
		viableCellsPerUnitBillions,
		initialViableCellsBillions,
		deficitCellsBillions,
		dryPacksExact,
		dryPacksRecommended,
		requiredSlurryMl,
		recommendedStarterVolumeLiters,
		starterDmeGrams,
		projectedStarterCellsBillions,
		needsMultiStepStarter,
		recommendationSummaryKey
	};
}
