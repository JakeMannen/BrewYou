import type {
	FermentationStepType,
	IngredientType,
	IngredientUsage,
	MashStepType
} from '$lib/types/api';

export interface ParsedIngredient {
	name: string;
	type: IngredientType;
	amount: number;
	unit: string;
	durationMinutes: number;
	usage: IngredientUsage;
	notes?: string;
	colorSrm?: number;
	potentialGravity?: number;
	alphaAcidPercent?: number;
	attenuationPercent?: number;
	form?: string;
}

export interface ParsedMashStep {
	stepOrder: number;
	name: string;
	type: MashStepType;
	temperatureC: number;
	durationMinutes: number;
	rampTimeMinutes?: number;
	infuseAmountLiters?: number;
	notes?: string;
}

export interface ParsedFermentationStep {
	stepOrder: number;
	name: string;
	type: FermentationStepType;
	targetTemperatureC: number;
	durationDays: number;
	rampTimeHours?: number;
	triggerGravity?: number;
	notes?: string;
}

export interface ParsedRecipe {
	name: string;
	beerStyle: string;
	description: string;
	batchSizeLiters: number;
	boilTimeMinutes: number;
	efficiencyPercent: number;
	ingredients: ParsedIngredient[];
	mashSteps: ParsedMashStep[];
	fermentationSteps: ParsedFermentationStep[];
}
