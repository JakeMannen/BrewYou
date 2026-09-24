import type {
	FermentationStepType,
	IngredientType,
	IngredientUsage,
	MashStepType
} from '$lib/types/api';

export type ExportFormat = 'beerxml' | 'beerjson';

export type CompatibilitySeverity = 'warning' | 'info';

export interface CompatibilityWarning {
	code: string;
	field: string;
	severity: CompatibilitySeverity;
	i18nKey: string;
	params?: Record<string, string | number>;
	fallbackText: string;
}

export interface CompatibilityReport {
	format: ExportFormat;
	isCompatible: boolean;
	warnings: CompatibilityWarning[];
}

export interface ExportableIngredient {
	id?: string;
	ingredientId?: string;
	name?: string;
	ingredientName?: string;
	type?: IngredientType;
	ingredientType?: IngredientType;
	amount: number;
	unit: string;
	durationMinutes?: number | null;
	usage: IngredientUsage;
	notes?: string | null;
	potentialGravity?: number | null;
	colorSrm?: number | null;
	alphaAcidPercent?: number | null;
	attenuationPercent?: number | null;
	form?: string | null;
}

export interface ExportableMashStep {
	id?: string;
	stepOrder: number;
	name: string;
	type: MashStepType;
	temperatureC: number;
	durationMinutes: number;
	rampTimeMinutes?: number | null;
	infuseAmountLiters?: number | null;
	notes?: string | null;
}

export interface ExportableFermentationStep {
	id?: string;
	stepOrder: number;
	name: string;
	type: FermentationStepType;
	targetTemperatureC: number;
	durationDays: number;
	rampTimeHours?: number | null;
	triggerGravity?: number | null;
	notes?: string | null;
}

export interface ExportableRecipe {
	id?: string;
	name: string;
	description?: string | null;
	beerStyle?: string | null;
	batchSizeLiters: number;
	boilTimeMinutes: number;
	efficiencyPercent: number;
	originalGravity?: number | null;
	finalGravity?: number | null;
	alcoholByVolume?: number | null;
	bitternessIbu?: number | null;
	colorSrm?: number | null;
	brewer?: string | null;
	ingredients: ExportableIngredient[];
	mashSteps: ExportableMashStep[];
	fermentationSteps?: ExportableFermentationStep[] | null;
}

export interface ExportResult {
	format: ExportFormat;
	mimeType: string;
	fileExtension: string;
	filename: string;
	content: string;
	report: CompatibilityReport;
}
