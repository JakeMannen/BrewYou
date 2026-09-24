import type {
	FermentationStepType,
	IngredientType,
	IngredientUsage,
	MashStepType
} from '$lib/types/api';
import type {
	ParsedFermentationStep,
	ParsedIngredient,
	ParsedMashStep,
	ParsedRecipe
} from './types';
import { smartPotentialToSg } from '$lib/calculators/brewing';

const MAX_PAYLOAD_SIZE = 2 * 1024 * 1024; // 2 MB

function parseVolumeLiters(obj: unknown): number {
	if (!obj || typeof obj !== 'object') return 20.0;
	const measure = obj as { value?: number; unit?: string };
	if (typeof measure.value !== 'number' || isNaN(measure.value)) return 20.0;

	const unit = (measure.unit || 'l').toLowerCase();
	if (unit === 'gal' || unit === 'gallon' || unit === 'gallons') {
		return Math.round(measure.value * 3.78541 * 10) / 10;
	}
	return Math.round(measure.value * 10) / 10;
}

function parseDurationMinutes(obj: unknown): number {
	if (!obj) return 60;
	if (typeof obj === 'number') return obj;
	if (typeof obj === 'object') {
		const measure = obj as { value?: number; unit?: string };
		if (typeof measure.value === 'number') {
			const unit = (measure.unit || 'min').toLowerCase();
			if (unit === 'hr' || unit === 'hour' || unit === 'hours') {
				return measure.value * 60;
			}
			if (unit === 'day' || unit === 'days') {
				return measure.value * 1440;
			}
			return measure.value;
		}
	}
	return 60;
}

function parseTemperatureC(obj: unknown, fallback: number = 65.0): number {
	if (!obj) return fallback;
	if (typeof obj === 'number') return Math.round(obj * 10) / 10;
	if (typeof obj === 'object') {
		const measure = obj as { value?: number; unit?: string };
		if (typeof measure.value === 'number') {
			const unit = (measure.unit || 'c').toLowerCase();
			if (unit === 'f' || unit === 'fahrenheit') {
				return Math.round((((measure.value - 32) * 5) / 9) * 10) / 10;
			}
			return Math.round(measure.value * 10) / 10;
		}
	}
	return fallback;
}

function parseMass(obj: unknown, defaultUnit: 'kg' | 'g'): { amount: number; unit: string } {
	if (!obj) return { amount: defaultUnit === 'kg' ? 1.0 : 25.0, unit: defaultUnit };
	if (typeof obj === 'number') {
		return { amount: obj, unit: defaultUnit };
	}

	const measure = obj as { value?: number; unit?: string };
	const val = typeof measure.value === 'number' ? measure.value : 1.0;
	const unit = (measure.unit || defaultUnit).toLowerCase();

	if (defaultUnit === 'kg') {
		if (unit === 'lb' || unit === 'lbs') {
			return { amount: Math.round(val * 0.453592 * 100) / 100, unit: 'kg' };
		}
		if (unit === 'g') {
			return { amount: Math.round((val / 1000) * 100) / 100, unit: 'kg' };
		}
		return { amount: Math.round(val * 100) / 100, unit: 'kg' };
	} else {
		// defaultUnit === 'g'
		if (unit === 'kg') {
			return { amount: Math.round(val * 1000 * 10) / 10, unit: 'g' };
		}
		if (unit === 'oz') {
			return { amount: Math.round(val * 28.3495 * 10) / 10, unit: 'g' };
		}
		if (unit === 'lb' || unit === 'lbs') {
			return { amount: Math.round(val * 453.592 * 10) / 10, unit: 'g' };
		}
		return { amount: Math.round(val * 10) / 10, unit: 'g' };
	}
}

function parseColorSrm(obj: unknown): number {
	if (!obj) return 2.0;
	if (typeof obj === 'number') {
		return isNaN(obj) ? 2.0 : obj;
	}
	if (typeof obj === 'object') {
		const measure = obj as { value?: number; unit?: string };
		if (typeof measure.value === 'number' && !isNaN(measure.value)) {
			const unit = (measure.unit || 'srm').toLowerCase().trim();
			if (unit === 'ebc') {
				return Math.round((measure.value / 1.97) * 10) / 10;
			}
			if (unit === 'lovibond' || unit === 'l') {
				return measure.value <= 10
					? Math.round(measure.value * 10) / 10
					: Math.round((1.3546 * measure.value - 0.76) * 10) / 10;
			}
			return Math.round(measure.value * 10) / 10;
		}
	}
	return 2.0;
}

function cleanString(str: unknown): string {
	if (typeof str !== 'string') return '';
	return str.replace(/[<>]/g, '').trim();
}

function mapBeerJsonMashStepType(type: unknown): MashStepType {
	const str = cleanString(type).toLowerCase();
	if (str.includes('decoct')) return 'Decoction';
	if (str.includes('temp')) return 'Temperature';
	return 'Infusion';
}

function mapBeerJsonUsage(use: unknown): IngredientUsage {
	const str = cleanString(use).toLowerCase();
	if (str.includes('first') || str.includes('fwh')) return 'Boil';
	if (str.includes('whirlpool') || str.includes('aroma')) return 'Whirlpool';
	if (str.includes('dry') || str.includes('fermentation')) return 'DryHop';
	if (str.includes('primary')) return 'Primary';
	if (str.includes('secondary')) return 'Secondary';
	if (str.includes('bottling') || str.includes('package')) return 'Bottling';
	if (str.includes('mash')) return 'Mash';
	return 'Boil';
}

function parseFermentablePotential(f: Record<string, unknown>): number {
	if (f.yield && typeof f.yield === 'object') {
		const y = f.yield as Record<string, unknown>;
		if (y.potential) {
			if (typeof y.potential === 'number' && !isNaN(y.potential)) {
				const norm = smartPotentialToSg(y.potential);
				if (norm !== undefined) return norm;
			}
			if (typeof y.potential === 'object' && y.potential !== null) {
				const val = (y.potential as { value?: number }).value;
				if (typeof val === 'number' && !isNaN(val)) {
					const norm = smartPotentialToSg(val);
					if (norm !== undefined) return norm;
				}
			}
		}
		if (y.fine_grind) {
			let fgVal: number | undefined;
			if (typeof y.fine_grind === 'number' && !isNaN(y.fine_grind)) {
				fgVal = y.fine_grind;
			} else if (typeof y.fine_grind === 'object' && y.fine_grind !== null) {
				fgVal = (y.fine_grind as { value?: number }).value;
			}
			if (typeof fgVal === 'number' && !isNaN(fgVal)) {
				const pot = 1.0 + (fgVal * 0.46) / 1000.0;
				return Math.round(pot * 1000) / 1000;
			}
		}
	} else if (typeof f.yield === 'number' && !isNaN(f.yield)) {
		const pot = 1.0 + (f.yield * 0.46) / 1000.0;
		return Math.round(pot * 1000) / 1000;
	}
	if (typeof f.potential_gravity === 'number' && !isNaN(f.potential_gravity)) {
		const norm = smartPotentialToSg(f.potential_gravity);
		if (norm !== undefined) return norm;
	}
	if (f.potential && typeof f.potential === 'object') {
		const val = (f.potential as { value?: number }).value;
		if (typeof val === 'number' && !isNaN(val)) {
			const norm = smartPotentialToSg(val);
			if (norm !== undefined) return norm;
		}
	} else if (typeof f.potential === 'number' && !isNaN(f.potential)) {
		const norm = smartPotentialToSg(f.potential);
		if (norm !== undefined) return norm;
	}
	return 1.036;
}

function mapBeerJsonFermentationStepType(type: unknown, name: string): FermentationStepType {
	const combined = `${cleanString(type)} ${cleanString(name)}`.toLowerCase();
	if (combined.includes('free') || combined.includes('rise')) {
		return 'FreeRise';
	}
	if (combined.includes('ramp')) {
		return 'Ramp';
	}
	if (combined.includes('diacetyl') || combined.includes('d-rest') || combined.includes('rest')) {
		return 'DiacetylRest';
	}
	if (combined.includes('crash') || combined.includes('cold')) return 'ColdCrash';
	if (combined.includes('second') || combined.includes('dry hop')) return 'Secondary';
	if (combined.includes('condition') || combined.includes('age') || combined.includes('lager')) {
		return 'Conditioning';
	}
	return 'Primary';
}

function parseDurationDays(obj: unknown, fallback: number = 14): number {
	if (!obj) return fallback;
	if (typeof obj === 'number') return Math.max(1, Math.round(obj));
	if (typeof obj === 'object') {
		const measure = obj as { value?: number; unit?: string };
		if (typeof measure.value === 'number' && !isNaN(measure.value)) {
			const unit = (measure.unit || 'day').toLowerCase();
			if (unit.startsWith('hr') || unit.startsWith('hour')) {
				return Math.max(1, Math.round(measure.value / 24));
			}
			if (unit.startsWith('week')) {
				return Math.max(1, Math.round(measure.value * 7));
			}
			return Math.max(1, Math.round(measure.value));
		}
	}
	return fallback;
}

export function parseBeerJson(rawJson: string): ParsedRecipe[] {
	if (!rawJson || typeof rawJson !== 'string') {
		throw new Error('Invalid BeerJSON: Content is empty.');
	}

	if (rawJson.length > MAX_PAYLOAD_SIZE) {
		throw new Error('BeerJSON payload exceeds maximum allowed size of 2MB.');
	}

	// Prototype Pollution Defense: Reviver strips hazardous keys
	let parsed: unknown;
	try {
		parsed = JSON.parse(rawJson, (key, value) => {
			if (key === '__proto__' || key === 'constructor' || key === 'prototype') {
				return undefined;
			}
			return value;
		});
	} catch (e: unknown) {
		throw new Error('Malformed JSON syntax: ' + ((e as Error).message || 'Parse error'), {
			cause: e
		});
	}

	if (!parsed || typeof parsed !== 'object') {
		throw new Error('Invalid BeerJSON: Root must be a JSON object.');
	}

	const root = parsed as Record<string, unknown>;
	let recipeList: unknown[] = [];

	if (root.beerjson && typeof root.beerjson === 'object') {
		const bj = root.beerjson as Record<string, unknown>;
		if (Array.isArray(bj.recipes)) {
			recipeList = bj.recipes;
		}
	} else if (Array.isArray(root.recipes)) {
		recipeList = root.recipes;
	} else if (root.name && (root.ingredients || root.batch_size)) {
		recipeList = [root];
	}

	if (recipeList.length === 0) {
		throw new Error('No recipe records found in BeerJSON file.');
	}

	return recipeList.map((rObj) => {
		const rec = (rObj || {}) as Record<string, unknown>;
		const name = cleanString(rec.name) || 'Imported BeerJSON Recipe';

		let beerStyle = 'American IPA';
		if (typeof rec.style === 'string') {
			beerStyle = cleanString(rec.style);
		} else if (rec.style && typeof rec.style === 'object') {
			beerStyle = cleanString((rec.style as Record<string, unknown>).name) || 'American IPA';
		}

		const description = cleanString(rec.description || rec.notes);
		const batchSizeLiters = parseVolumeLiters(rec.batch_size);

		let boilTimeMinutes = 60;
		if (rec.boil && typeof rec.boil === 'object') {
			boilTimeMinutes = parseDurationMinutes((rec.boil as Record<string, unknown>).boil_time);
		} else if (rec.boil_time) {
			boilTimeMinutes = parseDurationMinutes(rec.boil_time);
		}

		let efficiencyPercent = 72.0;
		if (rec.efficiency && typeof rec.efficiency === 'object') {
			const eff = (rec.efficiency as Record<string, unknown>).brewhouse;
			if (typeof eff === 'number') {
				efficiencyPercent = eff;
			} else if (
				eff &&
				typeof eff === 'object' &&
				typeof (eff as { value?: number }).value === 'number'
			) {
				efficiencyPercent = (eff as { value: number }).value;
			}
		} else if (typeof rec.efficiency === 'number') {
			efficiencyPercent = rec.efficiency;
		}

		const ingredients: ParsedIngredient[] = [];
		const ingSection = (rec.ingredients || {}) as Record<string, unknown>;

		// 1. Fermentable additions
		const fermentables = (ingSection.fermentable_additions || []) as Array<Record<string, unknown>>;
		for (const f of fermentables) {
			const ingName = cleanString(f.name);
			if (!ingName) continue;

			const { amount, unit } = parseMass(f.amount, 'kg');
			ingredients.push({
				name: ingName,
				type: 'Fermentable' as IngredientType,
				amount,
				unit,
				durationMinutes: boilTimeMinutes,
				usage: 'Mash',
				colorSrm: parseColorSrm(f.color),
				potentialGravity: parseFermentablePotential(f),
				notes: cleanString(f.notes)
			});
		}

		// 2. Hop additions
		const hops = (ingSection.hop_additions || []) as Array<Record<string, unknown>>;
		for (const h of hops) {
			const ingName = cleanString(h.name);
			if (!ingName) continue;

			const { amount, unit } = parseMass(h.amount, 'g');
			const timingObj =
				h.timing && typeof h.timing === 'object' ? (h.timing as Record<string, unknown>) : null;
			let duration = 60;
			if (timingObj) {
				duration = parseDurationMinutes(timingObj.time || timingObj.duration || h.time);
			} else if (h.time) {
				duration = parseDurationMinutes(h.time);
			}

			let alpha = 5.0;
			if (typeof h.alpha_acid === 'number' && !isNaN(h.alpha_acid)) {
				alpha = h.alpha_acid;
			} else if (h.alpha_acid && typeof h.alpha_acid === 'object') {
				const val = (h.alpha_acid as { value?: number }).value;
				if (typeof val === 'number' && !isNaN(val)) {
					alpha = val;
				}
			}

			const rawUse = timingObj?.use || h.use;

			const rawForm = cleanString(h.form).toLowerCase();
			let form = 'Pellet';
			if (rawForm.includes('plug')) {
				form = 'Plug';
			} else if (rawForm.includes('leaf') || rawForm.includes('whole')) {
				form = 'Leaf';
			}

			ingredients.push({
				name: ingName,
				type: 'Hop' as IngredientType,
				amount,
				unit,
				durationMinutes: duration,
				usage: mapBeerJsonUsage(rawUse),
				alphaAcidPercent: alpha,
				notes: cleanString(h.notes),
				form
			});
		}

		// 3. Culture / Yeast additions
		const yeasts = (ingSection.culture_additions || []) as Array<Record<string, unknown>>;
		for (const y of yeasts) {
			const ingName = cleanString(y.name);
			if (!ingName) continue;

			let atten = 75.0;
			if (typeof y.attenuation === 'number' && !isNaN(y.attenuation)) {
				atten = y.attenuation;
			} else if (y.attenuation && typeof y.attenuation === 'object') {
				const val = (y.attenuation as { value?: number }).value;
				if (typeof val === 'number' && !isNaN(val)) {
					atten = val;
				}
			}

			const rawForm = cleanString(y.form).toLowerCase();
			let form = 'Dry';
			if (rawForm.includes('liquid')) {
				form = 'Liquid';
			} else if (rawForm.includes('slant')) {
				form = 'Slant';
			} else if (rawForm.includes('culture')) {
				form = 'Culture';
			}

			let amount = form === 'Liquid' ? 1 : 11.5;
			let unit = form === 'Liquid' ? 'pkg' : 'g';

			if (y.amount) {
				if (typeof y.amount === 'number' && !isNaN(y.amount) && y.amount > 0) {
					if (y.amount <= 1.0 && form !== 'Liquid') {
						amount = Math.round(y.amount * 1000 * 10) / 10;
						unit = 'g';
					} else {
						amount = y.amount;
						unit = form === 'Liquid' ? (y.amount <= 5 ? 'pkg' : 'ml') : 'g';
					}
				} else if (typeof y.amount === 'object') {
					const measure = y.amount as { value?: number; unit?: string };
					if (typeof measure.value === 'number' && !isNaN(measure.value) && measure.value > 0) {
						const u = (measure.unit || '').toLowerCase().trim();
						if (u === 'kg') {
							amount = Math.round(measure.value * 1000 * 10) / 10;
							unit = 'g';
						} else if (u === 'l' || u === 'liter' || u === 'liters') {
							amount = Math.round(measure.value * 1000 * 10) / 10;
							unit = 'ml';
						} else if (u === 'ml') {
							amount = Math.round(measure.value * 10) / 10;
							unit = 'ml';
						} else if (u === 'pkg' || u === 'pack' || u === 'package' || u === 'vial') {
							amount = measure.value;
							unit = 'pkg';
						} else {
							amount = Math.round(measure.value * 10) / 10;
							unit = u || (form === 'Liquid' ? 'pkg' : 'g');
						}
					}
				}
			}

			ingredients.push({
				name: ingName,
				type: 'Yeast' as IngredientType,
				amount,
				unit,
				durationMinutes: 0,
				usage: 'Primary',
				attenuationPercent: atten,
				notes: cleanString(y.notes),
				form
			});
		}

		// 4. Miscellaneous additions
		const miscs = (ingSection.miscellaneous_additions || []) as Array<Record<string, unknown>>;
		for (const m of miscs) {
			const ingName = cleanString(m.name);
			if (!ingName) continue;

			const { amount, unit } = parseMass(m.amount, 'g');
			ingredients.push({
				name: ingName,
				type: 'Other' as IngredientType,
				amount,
				unit,
				durationMinutes: 15,
				usage: mapBeerJsonUsage(m.use),
				notes: cleanString(m.notes)
			});
		}

		// 5. Mash Steps
		const mashSteps: ParsedMashStep[] = [];
		const mashObj = (rec.mash || rec.mash_profile || {}) as Record<string, unknown>;
		const rawSteps = (mashObj.mash_steps || rec.mash_steps || []) as Array<Record<string, unknown>>;

		if (Array.isArray(rawSteps) && rawSteps.length > 0) {
			let order = 1;
			for (const s of rawSteps) {
				const stepName = cleanString(s.name) || `Mash Step ${order}`;
				const stepType = mapBeerJsonMashStepType(s.type);
				const stepTemp = parseTemperatureC(s.step_temperature || s.temperature, 65.0);
				const stepTime = parseDurationMinutes(s.step_time || s.duration);

				let rampTime: number | undefined;
				if (s.ramp_time) {
					rampTime = parseDurationMinutes(s.ramp_time);
				}

				let infuseAmount: number | undefined;
				if (s.infuse_amount || s.amount) {
					infuseAmount = parseVolumeLiters(s.infuse_amount || s.amount);
				}

				const notes = cleanString(s.notes || s.description);

				mashSteps.push({
					stepOrder: order++,
					name: stepName,
					type: stepType,
					temperatureC: stepTemp,
					durationMinutes: stepTime,
					rampTimeMinutes: rampTime,
					infuseAmountLiters: infuseAmount,
					notes: notes || undefined
				});
			}
		}

		if (mashSteps.length === 0) {
			mashSteps.push({
				stepOrder: 1,
				name: 'Saccharification Rest',
				type: 'Infusion',
				temperatureC: 65.0,
				durationMinutes: 60
			});
		}

		// 6. Fermentation Steps
		const fermentationSteps: ParsedFermentationStep[] = [];
		const fermObj = (rec.fermentation ||
			rec.fermentations ||
			rec.fermentation_profile ||
			{}) as Record<string, unknown>;
		const rawFSteps = (fermObj.fermentation_steps || rec.fermentation_steps || []) as Array<
			Record<string, unknown>
		>;

		if (Array.isArray(rawFSteps) && rawFSteps.length > 0) {
			let fOrder = 1;
			for (const fs of rawFSteps) {
				const stepName = cleanString(fs.name) || `Fermentation Step ${fOrder}`;
				const stepType = mapBeerJsonFermentationStepType(fs.type, stepName);
				const stepTemp = parseTemperatureC(fs.step_temperature || fs.temperature, 19.0);
				const durationDays = parseDurationDays(fs.step_time || fs.duration || fs.time, 14);

				let rampHours: number | undefined;
				if (fs.ramp_time) {
					const rMinutes = parseDurationMinutes(fs.ramp_time);
					rampHours = Math.round(rMinutes / 60);
				}

				let triggerGravity: number | undefined;
				if (fs.free_rise_gravity || fs.trigger_gravity) {
					const tg = parseFloat(cleanString(fs.free_rise_gravity || fs.trigger_gravity));
					if (!isNaN(tg) && tg > 0) {
						triggerGravity = Math.round(tg * 1000) / 1000;
					}
				}

				const notes = cleanString(fs.notes || fs.description);

				fermentationSteps.push({
					stepOrder: fOrder++,
					name: stepName,
					type: stepType,
					targetTemperatureC: stepTemp,
					durationDays,
					rampTimeHours: rampHours,
					triggerGravity,
					notes: notes || undefined
				});
			}
		}

		if (fermentationSteps.length === 0) {
			const isLagerStyle =
				/lager|pils|kellerbier|m[äa]rzen|marzen|bock|helles|dunkel|schwarzbier|vienna|baltic\s*porter/i.test(
					beerStyle
				);
			const isLagerCulture = yeasts.some((y) => {
				const typeStr = cleanString(y.type).toLowerCase();
				const nameStr = cleanString(y.name).toLowerCase();
				return (
					typeStr.includes('lager') ||
					/lager|w-34\/70|34\/70|s-23|s-189|diamond|urquell|bavarian\s*lager/i.test(nameStr)
				);
			});
			const isLager = isLagerStyle || isLagerCulture;

			fermentationSteps.push({
				stepOrder: 1,
				name: 'Primary Fermentation',
				type: 'Primary' as FermentationStepType,
				targetTemperatureC: isLager ? 12.0 : 19.0,
				durationDays: isLager ? 21 : 14
			});
		}

		return {
			name,
			beerStyle,
			description,
			batchSizeLiters,
			boilTimeMinutes,
			efficiencyPercent,
			ingredients,
			mashSteps,
			fermentationSteps
		};
	});
}
