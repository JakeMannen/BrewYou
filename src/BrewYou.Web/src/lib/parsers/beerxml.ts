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

function extractTagContent(xml: string, tagName: string): string | null {
	const regex = new RegExp(`<${tagName}[^>]*>([\\s\\S]*?)<\\/${tagName}>`, 'i');
	const match = xml.match(regex);
	return match ? match[1].trim() : null;
}

function extractAllTags(xml: string, tagName: string): string[] {
	const regex = new RegExp(`<${tagName}[^>]*>([\\s\\S]*?)<\\/${tagName}>`, 'gi');
	const results: string[] = [];
	let match: RegExpExecArray | null;
	while ((match = regex.exec(xml)) !== null) {
		results.push(match[1].trim());
	}
	return results;
}

function cleanText(text: string | null): string {
	if (!text) return '';
	return text
		.replace(/<!\[CDATA\[([\s\S]*?)\]\]>/gi, '$1')
		.replace(/&amp;/g, '&')
		.replace(/&lt;/g, '<')
		.replace(/&gt;/g, '>')
		.replace(/&quot;/g, '"')
		.replace(/&#39;/g, "'")
		.replace(/[<>]/g, '') // Strip residual HTML tags to prevent Stored XSS
		.trim();
}

function mapMashStepType(type: string | null): MashStepType {
	const normalized = (type || '').toLowerCase().trim();
	if (normalized.includes('decoct')) return 'Decoction';
	if (normalized.includes('temp')) return 'Temperature';
	return 'Infusion';
}

function mapHopUsage(use: string | null): IngredientUsage {
	const normalized = (use || '').toLowerCase().trim();
	if (normalized.includes('first') || normalized.includes('fwh')) return 'Boil';
	if (normalized.includes('whirlpool') || normalized.includes('aroma')) return 'Whirlpool';
	if (normalized.includes('dry')) return 'DryHop';
	if (normalized.includes('mash')) return 'Mash';
	return 'Boil';
}

function normalizeTemperatureC(temp: number | undefined | null, isMash = false): number | null {
	if (temp === undefined || temp === null || isNaN(temp)) return null;
	const fahrenheitThreshold = isMash ? 100 : 45;
	let celsius = temp;
	if (celsius > fahrenheitThreshold) {
		celsius = ((celsius - 32) * 5) / 9;
	}
	return Math.round(celsius * 10) / 10;
}

function mapFermentableUsage(use: string | null): IngredientUsage {
	const normalized = (use || '').toLowerCase().trim();
	if (normalized.includes('boil')) return 'Boil';
	return 'Mash';
}

function mapMiscUsage(use: string | null): IngredientUsage {
	const normalized = (use || '').toLowerCase().trim();
	if (normalized.includes('mash')) return 'Mash';
	if (normalized.includes('primary')) return 'Primary';
	if (normalized.includes('secondary')) return 'Secondary';
	if (normalized.includes('bottling')) return 'Bottling';
	return 'Boil';
}

export function parseBeerXml(rawXml: string): ParsedRecipe[] {
	if (!rawXml || typeof rawXml !== 'string') {
		throw new Error('Invalid BeerXML: Content is empty.');
	}

	if (rawXml.length > MAX_PAYLOAD_SIZE) {
		throw new Error('BeerXML payload exceeds maximum allowed size of 2MB.');
	}

	// Security Defense: Reject DOCTYPE or ENTITY to block XXE and entity bombs
	if (/<!DOCTYPE/i.test(rawXml) || /<!ENTITY/i.test(rawXml)) {
		throw new Error('BeerXML files containing DOCTYPE or ENTITY definitions are not permitted.');
	}

	// Check for root structure
	if (!/<RECIPES/i.test(rawXml) && !/<RECIPE/i.test(rawXml)) {
		throw new Error('Invalid BeerXML: Missing <RECIPES> or <RECIPE> root tag.');
	}

	const recipeBlocks = extractAllTags(rawXml, 'RECIPE');
	if (recipeBlocks.length === 0) {
		throw new Error('No <RECIPE> entries found in the BeerXML file.');
	}

	return recipeBlocks.map((recipeXml) => {
		const name = cleanText(extractTagContent(recipeXml, 'NAME')) || 'Imported Recipe';
		const styleBlock = extractTagContent(recipeXml, 'STYLE');
		const beerStyle =
			cleanText(styleBlock ? extractTagContent(styleBlock, 'NAME') : null) || 'American IPA';
		const styleType = cleanText(styleBlock ? extractTagContent(styleBlock, 'TYPE') : null);
		const isLagerStyle =
			styleType.toLowerCase().includes('lager') ||
			/lager|pils|kellerbier|m[äa]rzen|marzen|bock|helles|dunkel|schwarzbier|vienna|baltic\s*porter/i.test(
				beerStyle
			);
		const description = cleanText(
			extractTagContent(recipeXml, 'NOTES') || extractTagContent(recipeXml, 'DESCRIPTION')
		);

		const batchSizeRaw = parseFloat(extractTagContent(recipeXml, 'BATCH_SIZE') || '20.0');
		const batchSizeLiters =
			isNaN(batchSizeRaw) || batchSizeRaw <= 0 ? 20.0 : Math.round(batchSizeRaw * 10) / 10;

		const boilTimeRaw = parseInt(extractTagContent(recipeXml, 'BOIL_TIME') || '60', 10);
		const boilTimeMinutes = isNaN(boilTimeRaw) || boilTimeRaw < 0 ? 60 : boilTimeRaw;

		const effRaw = parseFloat(extractTagContent(recipeXml, 'EFFICIENCY') || '72.0');
		const efficiencyPercent =
			isNaN(effRaw) || effRaw <= 0 ? 72.0 : Math.min(100, Math.round(effRaw * 10) / 10);

		const ingredients: ParsedIngredient[] = [];

		// 1. Fermentables
		const fermentablesContainer = extractTagContent(recipeXml, 'FERMENTABLES');
		if (fermentablesContainer) {
			const fermentableBlocks = extractAllTags(fermentablesContainer, 'FERMENTABLE');
			for (const block of fermentableBlocks) {
				const ingName = cleanText(extractTagContent(block, 'NAME'));
				if (!ingName) continue;

				const amountKg = parseFloat(extractTagContent(block, 'AMOUNT') || '1.0');
				const rawColor = parseFloat(extractTagContent(block, 'COLOR') || '2.0');
				const colorUnit = (extractTagContent(block, 'COLOR_UNIT') || '').toLowerCase().trim();
				let colorSrm = isNaN(rawColor) ? 2.0 : rawColor;
				if (colorUnit === 'ebc' || (colorUnit === '' && rawColor > 600)) {
					colorSrm = Math.round((rawColor / 1.97) * 10) / 10;
				}
				const explicitPotentialStr =
					extractTagContent(block, 'POTENTIAL') || extractTagContent(block, 'POTENTIAL_GRAVITY');
				let potentialGravity = 1.036;

				if (explicitPotentialStr) {
					const rawPot = parseFloat(explicitPotentialStr);
					const norm = smartPotentialToSg(rawPot);
					if (norm !== undefined) {
						potentialGravity = norm;
					}
				} else {
					const yieldPercent = parseFloat(extractTagContent(block, 'YIELD') || '75.0');
					const potential = 1.0 + (yieldPercent * 0.46) / 1000.0;
					potentialGravity = isNaN(potential) ? 1.036 : Math.round(potential * 1000) / 1000;
				}

				ingredients.push({
					name: ingName,
					type: 'Fermentable' as IngredientType,
					amount: isNaN(amountKg) || amountKg <= 0 ? 1.0 : Math.round(amountKg * 100) / 100,
					unit: 'kg',
					durationMinutes: boilTimeMinutes,
					usage: mapFermentableUsage(extractTagContent(block, 'USE')),
					colorSrm,
					potentialGravity,
					notes: cleanText(extractTagContent(block, 'NOTES'))
				});
			}
		}

		// 2. Hops (Convert kilograms to grams!)
		const hopsContainer = extractTagContent(recipeXml, 'HOPS');
		if (hopsContainer) {
			const hopBlocks = extractAllTags(hopsContainer, 'HOP');
			for (const block of hopBlocks) {
				const ingName = cleanText(extractTagContent(block, 'NAME'));
				if (!ingName) continue;

				const rawAmount = parseFloat(extractTagContent(block, 'AMOUNT') || '0.025');
				// BeerXML standard defines hop amount in kg (e.g. 0.025 kg = 25 g).
				// If amount is small (<= 1.0), it is definitely in kg -> convert to g.
				// If exporter already used grams (> 1.0), retain grams.
				const amountG = rawAmount <= 1.0 ? rawAmount * 1000 : rawAmount;

				const alpha = parseFloat(extractTagContent(block, 'ALPHA') || '5.0');
				const time = parseFloat(extractTagContent(block, 'TIME') || '60');
				const usage = mapHopUsage(extractTagContent(block, 'USE'));

				const rawForm = cleanText(extractTagContent(block, 'FORM')).toLowerCase();
				let form = 'Pellet';
				if (rawForm.includes('plug')) {
					form = 'Plug';
				} else if (rawForm.includes('leaf') || rawForm.includes('whole')) {
					form = 'Leaf';
				}

				ingredients.push({
					name: ingName,
					type: 'Hop' as IngredientType,
					amount: isNaN(amountG) || amountG <= 0 ? 25 : Math.round(amountG * 10) / 10,
					unit: 'g',
					durationMinutes: isNaN(time) ? 60 : Math.round(time),
					usage,
					alphaAcidPercent: isNaN(alpha) ? 5.0 : alpha,
					notes: cleanText(extractTagContent(block, 'NOTES')),
					form
				});
			}
		}

		// 3. Yeasts
		const yeastsContainer = extractTagContent(recipeXml, 'YEASTS');
		if (yeastsContainer) {
			const yeastBlocks = extractAllTags(yeastsContainer, 'YEAST');
			for (const block of yeastBlocks) {
				const ingName = cleanText(extractTagContent(block, 'NAME'));
				if (!ingName) continue;

				const rawForm = cleanText(extractTagContent(block, 'FORM')).toLowerCase();
				let form = 'Dry';
				if (rawForm.includes('liquid')) {
					form = 'Liquid';
				} else if (rawForm.includes('slant')) {
					form = 'Slant';
				} else if (rawForm.includes('culture')) {
					form = 'Culture';
				}

				const rawAmountStr = extractTagContent(block, 'AMOUNT');
				const amountIsWeightStr = extractTagContent(block, 'AMOUNT_IS_WEIGHT');
				const atten = parseFloat(extractTagContent(block, 'ATTENUATION') || '75.0');

				let amount: number;
				let unit: string;

				if (rawAmountStr) {
					const rawAmount = parseFloat(rawAmountStr);
					if (!isNaN(rawAmount) && rawAmount > 0) {
						// BeerXML 1.0 yeast amount standard:
						// If AMOUNT_IS_WEIGHT is TRUE or (not specified and form is Dry/Slant/Culture), amount is in kg.
						// If AMOUNT_IS_WEIGHT is FALSE or (not specified and form is Liquid), amount is in liters.
						const isWeight =
							amountIsWeightStr?.toUpperCase() === 'TRUE' ||
							(amountIsWeightStr?.toUpperCase() !== 'FALSE' && form !== 'Liquid');

						if (isWeight) {
							// Standard BeerXML defines weight in kg (e.g. 0.0115 kg = 11.5 g, 0.023 kg = 23 g).
							// If amount <= 1.0, convert kg -> g (* 1000). If > 1.0, it was already exported in grams.
							const amountG = rawAmount <= 1.0 ? rawAmount * 1000 : rawAmount;
							amount = Math.round(amountG * 10) / 10;
							unit = 'g';
						} else {
							// Volume in liters or package count:
							// If rawAmount < 1.0: it's in liters (e.g. 0.125 L = 125 ml) -> convert to ml (* 1000).
							// If rawAmount >= 1.0: if <= 5.0 and integer, it represents packages ('pkg').
							// If rawAmount > 5.0, it represents ml.
							if (rawAmount < 1.0) {
								amount = Math.round(rawAmount * 1000 * 10) / 10;
								unit = 'ml';
							} else if (rawAmount <= 5.0 && Number.isInteger(rawAmount)) {
								amount = rawAmount;
								unit = 'pkg';
							} else {
								amount = Math.round(rawAmount * 10) / 10;
								unit = 'ml';
							}
						}
					} else {
						if (form === 'Liquid') {
							amount = 1;
							unit = 'pkg';
						} else {
							amount = 11.5;
							unit = 'g';
						}
					}
				} else {
					if (form === 'Liquid') {
						amount = 1;
						unit = 'pkg';
					} else {
						amount = 11.5;
						unit = 'g';
					}
				}

				ingredients.push({
					name: ingName,
					type: 'Yeast' as IngredientType,
					amount,
					unit,
					durationMinutes: 0,
					usage: 'Primary',
					attenuationPercent: isNaN(atten) ? 75.0 : atten,
					notes: cleanText(extractTagContent(block, 'NOTES')),
					form
				});
			}
		}

		// 4. Miscs
		const miscsContainer = extractTagContent(recipeXml, 'MISCS');
		if (miscsContainer) {
			const miscBlocks = extractAllTags(miscsContainer, 'MISC');
			for (const block of miscBlocks) {
				const ingName = cleanText(extractTagContent(block, 'NAME'));
				if (!ingName) continue;

				const rawAmount = parseFloat(extractTagContent(block, 'AMOUNT') || '1.0');
				const amountUnit =
					cleanText(extractTagContent(block, 'AMOUNT_IS_WEIGHT')) === 'TRUE' ? 'g' : 'g';
				const time = parseFloat(extractTagContent(block, 'TIME') || '15');

				ingredients.push({
					name: ingName,
					type: 'Other' as IngredientType,
					amount: isNaN(rawAmount) ? 1.0 : rawAmount,
					unit: amountUnit,
					durationMinutes: isNaN(time) ? 15 : Math.round(time),
					usage: mapMiscUsage(extractTagContent(block, 'USE')),
					notes: cleanText(extractTagContent(block, 'NOTES'))
				});
			}
		}

		// 5. Mash Steps
		const mashSteps: ParsedMashStep[] = [];
		const mashContainer = extractTagContent(recipeXml, 'MASH');
		if (mashContainer) {
			const mashStepsContainer = extractTagContent(mashContainer, 'MASH_STEPS') || mashContainer;
			const stepBlocks = extractAllTags(mashStepsContainer, 'MASH_STEP');
			let order = 1;
			for (const block of stepBlocks) {
				const stepName = cleanText(extractTagContent(block, 'NAME')) || `Mash Step ${order}`;
				const stepType = mapMashStepType(extractTagContent(block, 'TYPE'));
				const tempRaw = parseFloat(extractTagContent(block, 'STEP_TEMP') || '65.0');
				const normalizedTemp = normalizeTemperatureC(tempRaw, true);
				const stepTemp = normalizedTemp ?? 65.0;
				const timeRaw = parseFloat(extractTagContent(block, 'STEP_TIME') || '60.0');
				const stepTime = isNaN(timeRaw) ? 60 : Math.round(timeRaw);

				const rampRaw = parseFloat(extractTagContent(block, 'RAMP_TIME') || '');
				const rampTime = isNaN(rampRaw) ? undefined : Math.round(rampRaw);

				const infuseRaw = parseFloat(extractTagContent(block, 'INFUSE_AMOUNT') || '');
				const infuseAmount = isNaN(infuseRaw) ? undefined : Math.round(infuseRaw * 10) / 10;

				const notes = cleanText(extractTagContent(block, 'DESCRIPTION'));

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
		let fOrder = 1;

		const primaryTempRaw = parseFloat(extractTagContent(recipeXml, 'PRIMARY_TEMP') || '');
		const primaryTemp = normalizeTemperatureC(primaryTempRaw, false);
		const primaryAgeRaw = parseFloat(extractTagContent(recipeXml, 'PRIMARY_AGE') || '');

		const secondaryTempRaw = parseFloat(extractTagContent(recipeXml, 'SECONDARY_TEMP') || '');
		const secondaryTemp = normalizeTemperatureC(secondaryTempRaw, false);
		const secondaryAgeRaw = parseFloat(extractTagContent(recipeXml, 'SECONDARY_AGE') || '');

		const tertiaryTempRaw = parseFloat(extractTagContent(recipeXml, 'TERTIARY_TEMP') || '');
		const tertiaryTemp = normalizeTemperatureC(tertiaryTempRaw, false);
		const tertiaryAgeRaw = parseFloat(extractTagContent(recipeXml, 'TERTIARY_AGE') || '');

		const ageTempRaw = parseFloat(extractTagContent(recipeXml, 'AGE_TEMP') || '');
		const ageTemp = normalizeTemperatureC(ageTempRaw, false);
		const ageRaw = parseFloat(extractTagContent(recipeXml, 'AGE') || '');

		if (primaryTemp !== null) {
			fermentationSteps.push({
				stepOrder: fOrder++,
				name: 'Primary Fermentation',
				type: 'Primary' as FermentationStepType,
				targetTemperatureC: primaryTemp,
				durationDays: !isNaN(primaryAgeRaw) && primaryAgeRaw > 0 ? Math.round(primaryAgeRaw) : 14
			});
		}

		if (secondaryTemp !== null) {
			fermentationSteps.push({
				stepOrder: fOrder++,
				name: 'Secondary Fermentation',
				type: 'Secondary' as FermentationStepType,
				targetTemperatureC: secondaryTemp,
				durationDays:
					!isNaN(secondaryAgeRaw) && secondaryAgeRaw > 0 ? Math.round(secondaryAgeRaw) : 7
			});
		}

		if (tertiaryTemp !== null) {
			fermentationSteps.push({
				stepOrder: fOrder++,
				name: 'Tertiary Fermentation',
				type:
					tertiaryTemp <= 4.0
						? ('ColdCrash' as FermentationStepType)
						: ('Conditioning' as FermentationStepType),
				targetTemperatureC: tertiaryTemp,
				durationDays: !isNaN(tertiaryAgeRaw) && tertiaryAgeRaw > 0 ? Math.round(tertiaryAgeRaw) : 7
			});
		}

		if (ageTemp !== null) {
			fermentationSteps.push({
				stepOrder: fOrder,
				name: 'Conditioning & Aging',
				type: 'Conditioning' as FermentationStepType,
				targetTemperatureC: ageTemp,
				durationDays: !isNaN(ageRaw) && ageRaw > 0 ? Math.round(ageRaw) : 14
			});
		}

		if (fermentationSteps.length === 0) {
			let isLagerYeast = false;
			let yeastDerivedTemp: number | null = null;
			if (yeastsContainer) {
				const yeastBlocks = extractAllTags(yeastsContainer, 'YEAST');
				for (const yBlock of yeastBlocks) {
					const yType = cleanText(extractTagContent(yBlock, 'TYPE')).toLowerCase();
					const yName = cleanText(extractTagContent(yBlock, 'NAME')).toLowerCase();
					if (
						yType.includes('lager') ||
						/lager|w-34\/70|34\/70|s-23|s-189|diamond|urquell|bavarian\s*lager/i.test(yName)
					) {
						isLagerYeast = true;
					}

					const minTRaw = parseFloat(extractTagContent(yBlock, 'MIN_TEMPERATURE') || '');
					const maxTRaw = parseFloat(extractTagContent(yBlock, 'MAX_TEMPERATURE') || '');
					const minT = normalizeTemperatureC(minTRaw, false);
					const maxT = normalizeTemperatureC(maxTRaw, false);
					if (minT !== null && maxT !== null) {
						yeastDerivedTemp = Math.round(((minT + maxT) / 2) * 10) / 10;
						break;
					} else if (minT !== null) {
						yeastDerivedTemp = minT;
						break;
					}
				}
			}

			const isLager = isLagerStyle || isLagerYeast;
			const defaultTemp = yeastDerivedTemp ?? (isLager ? 12.0 : 19.0);
			const defaultDays =
				!isNaN(primaryAgeRaw) && primaryAgeRaw > 0 ? Math.round(primaryAgeRaw) : isLager ? 21 : 14;

			fermentationSteps.push({
				stepOrder: 1,
				name: 'Primary Fermentation',
				type: 'Primary' as FermentationStepType,
				targetTemperatureC: defaultTemp,
				durationDays: defaultDays
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
