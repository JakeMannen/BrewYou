import type { EquipmentSubtype } from '$lib/types/api';

/**
 * Chamber coordinate boundaries for liquid fill calculations (y: 0 to 48).
 * Defines the physical interior reservoir bottom and top liquid fill limits
 * within the standard 48x48 icon viewport.
 */
export const CHAMBER_BOUNDS: Record<
	EquipmentSubtype | 'Bottle',
	{ bottomY: number; topY: number }
> = {
	AllInOne: { bottomY: 39, topY: 13 },
	Pan: { bottomY: 38, topY: 16 },
	Hlt: { bottomY: 39, topY: 13 },
	HermsRims: { bottomY: 38, topY: 14 },
	Bucket: { bottomY: 39.5, topY: 13.5 },
	ConicalFermenter: { bottomY: 37, topY: 12 },
	Carboy: { bottomY: 40, topY: 14 },
	PressureFermenter: { bottomY: 37, topY: 11 },
	StainlessBucket: { bottomY: 39.5, topY: 13.5 },
	Cornelius: { bottomY: 36.5, topY: 13.5 },
	Minikeg: { bottomY: 40, topY: 12 },
	MiniBarrel: { bottomY: 39, topY: 9 },
	PetKeg: { bottomY: 39, topY: 13 },
	ISpindel: { bottomY: 38, topY: 10 },
	Tilt: { bottomY: 39, topY: 9 },
	GenericSensor: { bottomY: 38, topY: 14 },
	Bottle: { bottomY: 41, topY: 14 },
	Other: { bottomY: 37, topY: 13 }
};

/**
 * Sensor and probe subtypes that are not liquid-bearing vessels.
 */
export const SENSOR_SUBTYPES: EquipmentSubtype[] = ['GenericSensor', 'Tilt', 'ISpindel'];

/**
 * Checks whether an equipment subtype represents a fillable vessel (holds liquid volume).
 * Sensors, probes, and hydrometers are not fillable and have no fill levels.
 */
export function isVesselFillable(subtype: EquipmentSubtype | string): boolean {
	return !['GenericSensor', 'Tilt', 'ISpindel'].includes(subtype as EquipmentSubtype);
}

/**
 * Calculates clamped fill percentage, usable height, liquid height, and top Y coordinate
 * for a vessel chamber.
 */
export function calculateLiquidGeometry(
	subtype: EquipmentSubtype | 'Bottle' | string,
	fillPercent: number
): { liquidHeight: number; liquidY: number; clampedFill: number; usableHeight: number } {
	const clampedFill =
		typeof fillPercent !== 'number' || isNaN(fillPercent)
			? 0
			: Math.max(0, Math.min(100, fillPercent));
	const bounds = CHAMBER_BOUNDS[subtype as keyof typeof CHAMBER_BOUNDS] ?? CHAMBER_BOUNDS.Other;
	const usableHeight = bounds.bottomY - bounds.topY;
	const liquidHeight = (clampedFill / 100) * usableHeight;
	const liquidY = bounds.bottomY - liquidHeight;

	return { liquidHeight, liquidY, clampedFill, usableHeight };
}

/**
 * Resolves the effective animated brewing effect based on vessel subtype and stage.
 */
export function resolveVesselAnimationType(
	subtype: EquipmentSubtype | 'Bottle' | string,
	options?: {
		active?: boolean;
		animated?: boolean;
		animationStage?: string;
	}
): string | null {
	const active = options?.active ?? false;
	const animated = options?.animated ?? false;
	if (!active && !animated) return null;

	if (['ISpindel', 'Tilt', 'GenericSensor'].includes(subtype as string)) {
		return 'sensor';
	}

	if (options?.animationStage) {
		return options.animationStage.toLowerCase();
	}

	if (['Pan', 'AllInOne', 'Hlt', 'HermsRims'].includes(subtype as string)) return 'boil';
	if (
		['Bucket', 'ConicalFermenter', 'Carboy', 'PressureFermenter', 'StainlessBucket'].includes(
			subtype as string
		)
	) {
		return 'ferment';
	}
	if (['Cornelius', 'Minikeg', 'MiniBarrel', 'PetKeg', 'Bottle'].includes(subtype as string)) {
		return 'package';
	}
	return 'boil';
}
