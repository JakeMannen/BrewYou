import type {
	CompatibilityReport,
	CompatibilityWarning,
	ExportableRecipe,
	ExportResult
} from './types';
import { sanitizeFilename } from '$lib/utils/downloadFile';
import { potentialToYieldPercent, detectFermentationType, getIngName, getIngType } from './beerxml';

/**
 * Maps BrewYou hop usage to BeerJSON 2.x timing.use enums:
 * add_to_boil, add_to_whirlpool, add_to_fermentation, add_to_mash, add_to_first_wort, add_to_package
 */
function mapBeerJsonHopUse(usage: string): string {
	switch (usage) {
		case 'Mash':
			return 'add_to_mash';
		case 'Whirlpool':
			return 'add_to_whirlpool';
		case 'DryHop':
		case 'Primary':
		case 'Secondary':
			return 'add_to_fermentation';
		case 'Bottling':
			return 'add_to_package';
		case 'Boil':
		default:
			return 'add_to_boil';
	}
}

/**
 * Inspects a recipe against BeerJSON 2.x specification constraints.
 */
export function analyzeBeerJsonCompatibility(recipe: ExportableRecipe): CompatibilityReport {
	const warnings: CompatibilityWarning[] = [];

	// Check missing hop alpha acid %
	const missingAlphaHops = recipe.ingredients.filter(
		(i) =>
			getIngType(i) === 'Hop' && (i.alphaAcidPercent === undefined || i.alphaAcidPercent === null)
	);
	if (missingAlphaHops.length > 0) {
		warnings.push({
			code: 'MISSING_HOP_ALPHA',
			field: 'hops.alphaAcidPercent',
			severity: 'warning',
			i18nKey: 'recipes.export_modal.warning_missing_alpha',
			params: {
				names: missingAlphaHops.map(getIngName).join(', ')
			},
			fallbackText:
				'Some hops have no alpha acid percentage specified; defaulted to standard 5.0% AA in the exported file.'
		});
	}

	// Check missing fermentable potential
	const missingPotentialGrains = recipe.ingredients.filter(
		(i) =>
			getIngType(i) === 'Fermentable' &&
			(i.potentialGravity === undefined || i.potentialGravity === null)
	);
	if (missingPotentialGrains.length > 0) {
		warnings.push({
			code: 'MISSING_FERMENTABLE_POTENTIAL',
			field: 'fermentables.potentialGravity',
			severity: 'warning',
			i18nKey: 'recipes.export_modal.warning_missing_potential',
			params: {
				names: missingPotentialGrains.map(getIngName).join(', ')
			},
			fallbackText:
				'Some fermentables have no gravity potential specified; defaulted to 1.037 (80% yield).'
		});
	}

	return {
		format: 'beerjson',
		isCompatible: warnings.filter((w) => w.severity === 'warning').length === 0,
		warnings
	};
}

/**
 * Serializes an ExportableRecipe into valid BeerJSON 2.x specification standard.
 */
export function serializeBeerJson(recipe: ExportableRecipe): ExportResult {
	const report = analyzeBeerJsonCompatibility(recipe);

	const boilOffLiters = ((recipe.boilTimeMinutes || 60) / 60) * 3.0;
	const boilSizeLiters = (recipe.batchSizeLiters || 20) + boilOffLiters;

	// Fermentable additions
	const fermentable_additions = recipe.ingredients
		.filter((i) => getIngType(i) === 'Fermentable')
		.map((f) => {
			const amountKg = f.unit.toLowerCase() === 'g' ? f.amount / 1000.0 : f.amount;
			const potentialVal = f.potentialGravity ?? 1.037;
			const yieldPercent = potentialToYieldPercent(potentialVal);
			const colorVal = f.colorSrm ?? 2.0;

			return {
				name: getIngName(f),
				type: 'grain',
				amount: { value: Math.round(amountKg * 10000) / 10000, unit: 'kg' },
				yield: {
					fine_grind: { value: yieldPercent, unit: '%' },
					potential: { value: Math.round(potentialVal * 1000) / 1000, unit: 'sg' }
				},
				color: { value: colorVal, unit: 'SRM' },
				notes: f.notes || undefined
			};
		});

	// Hop additions
	const hop_additions = recipe.ingredients
		.filter((i) => getIngType(i) === 'Hop')
		.map((h) => {
			const amountGrams = h.unit.toLowerCase() === 'kg' ? h.amount * 1000.0 : h.amount;
			const alphaAcid = h.alphaAcidPercent ?? 5.0;
			const useVal = mapBeerJsonHopUse(h.usage);
			const isDryHop = useVal === 'add_to_fermentation';

			let timeValue = h.durationMinutes ?? (isDryHop ? 4320 : 60);
			let timeUnit = 'min';

			if (isDryHop && timeValue >= 1440) {
				timeValue = Math.round((timeValue / 1440) * 10) / 10;
				timeUnit = 'day';
			}

			const hopForm = (h.form || 'pellet').toLowerCase();

			return {
				name: getIngName(h),
				form: hopForm,
				amount: { value: Math.round(amountGrams * 10) / 10, unit: 'g' },
				alpha_acid: { value: Math.round(alphaAcid * 100) / 100, unit: '%' },
				timing: {
					use: useVal,
					time: { value: timeValue, unit: timeUnit }
				},
				notes: h.notes || undefined
			};
		});

	// Culture additions (Yeast)
	const styleType = detectFermentationType(recipe.beerStyle).toLowerCase();
	const culture_additions = recipe.ingredients
		.filter((i) => getIngType(i) === 'Yeast')
		.map((y) => {
			const attenVal = y.attenuationPercent ?? 75.0;
			const yeastDetected = detectFermentationType(getIngName(y)).toLowerCase();
			const cultureType = yeastDetected !== 'ale' ? yeastDetected : styleType;
			const yeastForm = (y.form || 'dry').toLowerCase();
			return {
				name: getIngName(y),
				type: cultureType,
				form: yeastForm,
				amount: { value: y.amount, unit: y.unit || 'g' },
				attenuation: { value: attenVal, unit: '%' },
				notes: y.notes || undefined
			};
		});

	if (culture_additions.length === 0) {
		const fallbackType = styleType;
		culture_additions.push({
			name: `Standard ${fallbackType === 'lager' ? 'Lager' : fallbackType === 'wheat' ? 'Wheat' : 'Ale'} Yeast`,
			type: fallbackType,
			form: 'dry',
			amount: { value: 11.5, unit: 'g' },
			attenuation: { value: 75.0, unit: '%' },
			notes: undefined
		});
	}

	// Misc additions
	const miscellaneous_additions = recipe.ingredients
		.filter((i) => getIngType(i) === 'Other')
		.map((m) => {
			const amountGrams = m.unit.toLowerCase() === 'kg' ? m.amount * 1000.0 : m.amount;
			return {
				name: getIngName(m),
				type: 'other',
				amount: { value: amountGrams, unit: 'g' },
				timing: {
					use: m.usage === 'Boil' ? 'add_to_boil' : 'add_to_fermentation',
					time: { value: m.durationMinutes ?? 15, unit: 'min' }
				},
				notes: m.notes || undefined
			};
		});

	// Mash Steps
	const steps =
		recipe.mashSteps && recipe.mashSteps.length > 0
			? [...recipe.mashSteps].sort((a, b) => a.stepOrder - b.stepOrder)
			: [
					{
						stepOrder: 1,
						name: 'Saccharification Rest',
						type: 'Infusion' as const,
						temperatureC: 65.0,
						durationMinutes: 60
					}
				];

	const mash_steps = steps.map((s) => ({
		name: s.name,
		type: (s.type || 'temperature').toLowerCase(),
		step_temperature: { value: Math.round(s.temperatureC * 10) / 10, unit: 'C' },
		step_time: { value: s.durationMinutes, unit: 'min' },
		ramp_time: s.rampTimeMinutes ? { value: s.rampTimeMinutes, unit: 'min' } : undefined,
		infuse_amount: s.infuseAmountLiters ? { value: s.infuseAmountLiters, unit: 'l' } : undefined,
		description: s.notes || undefined
	}));

	// Fermentation Steps
	const fermentation_steps =
		recipe.fermentationSteps && recipe.fermentationSteps.length > 0
			? [...recipe.fermentationSteps]
					.sort((a, b) => a.stepOrder - b.stepOrder)
					.map((s) => ({
						name: s.name,
						step_temperature: {
							value: Math.round(s.targetTemperatureC * 10) / 10,
							unit: 'C'
						},
						step_time: { value: s.durationDays, unit: 'day' },
						free_rise: s.type === 'FreeRise' ? true : undefined,
						description: s.notes || undefined
					}))
			: undefined;

	const fermentation = fermentation_steps
		? {
				name: 'Fermentation Profile',
				fermentation_steps
			}
		: undefined;

	const jsonDoc = {
		beerjson: {
			version: 2.06,
			recipes: [
				{
					name: recipe.name || 'Untitled Recipe',
					type: 'all grain',
					author: recipe.brewer || 'BrewYou User',
					batch_size: {
						value: Math.round(recipe.batchSizeLiters * 100) / 100,
						unit: 'l'
					},
					efficiency: {
						brewhouse: {
							value: Math.round(recipe.efficiencyPercent * 10) / 10,
							unit: '%'
						}
					},
					boil: {
						pre_boil_size: {
							value: Math.round(boilSizeLiters * 100) / 100,
							unit: 'l'
						},
						boil_time: {
							value: recipe.boilTimeMinutes || 60,
							unit: 'min'
						}
					},
					style: {
						name: recipe.beerStyle || 'Custom'
					},
					original_gravity: recipe.originalGravity
						? { value: Math.round(recipe.originalGravity * 1000) / 1000, unit: 'sg' }
						: undefined,
					final_gravity: recipe.finalGravity
						? { value: Math.round(recipe.finalGravity * 1000) / 1000, unit: 'sg' }
						: undefined,
					alcohol_by_volume: recipe.alcoholByVolume
						? { value: Math.round(recipe.alcoholByVolume * 100) / 100, unit: '%' }
						: undefined,
					notes: recipe.description || undefined,
					ingredients: {
						fermentable_additions,
						hop_additions,
						culture_additions,
						miscellaneous_additions
					},
					mash: {
						name: 'Mash Profile',
						grain_temperature: { value: 20.0, unit: 'C' },
						mash_steps
					},
					fermentation
				}
			]
		}
	};

	const content = JSON.stringify(jsonDoc, null, 2);
	const filename = sanitizeFilename(recipe.name, 'json');

	return {
		format: 'beerjson',
		mimeType: 'application/json;charset=utf-8',
		fileExtension: 'json',
		filename,
		content,
		report
	};
}
