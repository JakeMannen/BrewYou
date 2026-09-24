import type {
	CompatibilityReport,
	CompatibilityWarning,
	ExportableIngredient,
	ExportableRecipe,
	ExportResult
} from './types';
import { sanitizeFilename } from '$lib/utils/downloadFile';

export function getIngName(i: ExportableIngredient): string {
	return i.name || i.ingredientName || 'Unnamed Ingredient';
}

export function getIngType(i: ExportableIngredient): string {
	return i.type || i.ingredientType || 'Other';
}

/**
 * Safely escapes XML text nodes to prevent XML injection or malformed output.
 */
function escapeXml(text: string | null | undefined): string {
	if (!text) return '';
	return String(text)
		.replace(/&/g, '&amp;')
		.replace(/</g, '&lt;')
		.replace(/>/g, '&gt;')
		.replace(/"/g, '&quot;')
		.replace(/'/g, '&apos;');
}

/**
 * Format floating-point numbers cleanly without trailing zeroes unless necessary.
 */
function formatXmlNumber(num: number | null | undefined, maxDecimals: number = 4): string {
	if (num === null || num === undefined || isNaN(num)) return '0';
	return Number(num.toFixed(maxDecimals)).toString();
}

/**
 * Maps BrewYou hop usages to valid BeerXML 1.0 <USE> enums:
 * Boil, Dry Hop, Mash, First Wort, Aroma.
 */
function mapBeerXmlHopUse(usage: string): string {
	switch (usage) {
		case 'Mash':
			return 'Mash';
		case 'DryHop':
		case 'Primary':
		case 'Secondary':
			return 'Dry Hop';
		case 'Whirlpool':
			return 'Aroma';
		case 'Boil':
		default:
			return 'Boil';
	}
}

/**
 * Maps BrewYou misc/other usages to valid BeerXML 1.0 <USE> enums:
 * Boil, Mash, Primary, Secondary, Bottling.
 */
function mapBeerXmlMiscUse(usage: string): string {
	switch (usage) {
		case 'Mash':
			return 'Mash';
		case 'Primary':
			return 'Primary';
		case 'Secondary':
		case 'DryHop':
			return 'Secondary';
		case 'Bottling':
			return 'Bottling';
		case 'Boil':
		case 'Whirlpool':
		default:
			return 'Boil';
	}
}

/**
 * Converts specific gravity potential (e.g. 1.037) to BeerXML dry-basis yield percentage.
 * Formula: ((SG - 1.0) * 1000) / 0.46
 */
export function potentialToYieldPercent(potential: number | null | undefined): number {
	if (!potential || potential < 1.0) return 78.0;
	const yieldVal = ((potential - 1.0) * 1000) / 0.46;
	return Math.round(yieldVal * 10) / 10;
}

export function detectFermentationType(
	nameOrStyle: string | null | undefined
): 'Lager' | 'Wheat' | 'Ale' {
	const s = (nameOrStyle || '').toLowerCase();
	if (
		s.includes('lager') ||
		s.includes('pils') ||
		s.includes('kellerbier') ||
		s.includes('märzen') ||
		s.includes('marzen') ||
		s.includes('bock') ||
		s.includes('helles') ||
		s.includes('dunkel') ||
		s.includes('schwarzbier') ||
		s.includes('vienna') ||
		s.includes('baltic porter') ||
		s.includes('w-34/70') ||
		s.includes('34/70') ||
		s.includes('s-23') ||
		s.includes('s-189')
	) {
		return 'Lager';
	}
	if (s.includes('wheat') || s.includes('weizen') || s.includes('weiss') || s.includes('wit')) {
		return 'Wheat';
	}
	return 'Ale';
}

/**
 * Inspects a recipe against BeerXML 1.0 specification constraints and produces
 * a structured list of format limitations, data loss, and adaptations.
 */
export function analyzeBeerXmlCompatibility(recipe: ExportableRecipe): CompatibilityReport {
	const warnings: CompatibilityWarning[] = [];

	// 1. Whirlpool hop additions
	const whirlpoolHops = recipe.ingredients.filter(
		(i) => getIngType(i) === 'Hop' && i.usage === 'Whirlpool'
	);
	if (whirlpoolHops.length > 0) {
		warnings.push({
			code: 'BEERXML_WHIRLPOOL_MAPPED',
			field: 'hops.usage',
			severity: 'warning',
			i18nKey: 'recipes.export_modal.warning_whirlpool_temp',
			params: {
				count: whirlpoolHops.length,
				names: whirlpoolHops.map((h) => getIngName(h)).join(', ')
			},
			fallbackText:
				'BeerXML 1.0 does not support whirlpool stand temperatures. Whirlpool hop additions are exported as "Aroma" additions without temperature kinetics.'
		});
	}

	// 2. Dry hop duration in days
	const dryHops = recipe.ingredients.filter(
		(i) =>
			getIngType(i) === 'Hop' &&
			(i.usage === 'DryHop' || i.usage === 'Primary' || i.usage === 'Secondary')
	);
	const subDayDryHops = dryHops.filter((h) => {
		const mins = h.durationMinutes ?? 0;
		return mins > 0 && mins % 1440 !== 0;
	});
	if (subDayDryHops.length > 0) {
		warnings.push({
			code: 'BEERXML_DRY_HOP_DAYS',
			field: 'hops.time',
			severity: 'info',
			i18nKey: 'recipes.export_modal.warning_dry_hop_days',
			fallbackText:
				'Dry hop durations are converted from minutes to fractional days to comply with the BeerXML 1.0 standard.'
		});
	}

	// 3. Missing Hop Alpha Acid %
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
				names: missingAlphaHops.map((h) => getIngName(h)).join(', ')
			},
			fallbackText:
				'Some hops have no alpha acid percentage specified; defaulted to standard 5.0% AA in the exported file.'
		});
	}

	// 4. Missing Fermentable Potential / Color
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
				names: missingPotentialGrains.map((g) => getIngName(g)).join(', ')
			},
			fallbackText:
				'Some fermentables have no gravity potential specified; defaulted to 1.037 (80% yield).'
		});
	}

	// 5. Missing Yeast Attenuation %
	const missingAttenuationYeast = recipe.ingredients.filter(
		(i) =>
			getIngType(i) === 'Yeast' &&
			(i.attenuationPercent === undefined || i.attenuationPercent === null)
	);
	if (missingAttenuationYeast.length > 0) {
		warnings.push({
			code: 'MISSING_YEAST_ATTENUATION',
			field: 'yeast.attenuationPercent',
			severity: 'warning',
			i18nKey: 'recipes.export_modal.warning_missing_attenuation',
			params: {
				names: missingAttenuationYeast.map((y) => getIngName(y)).join(', ')
			},
			fallbackText: 'Yeast attenuation was not specified; defaulted to 75.0%.'
		});
	}

	return {
		format: 'beerxml',
		isCompatible: warnings.filter((w) => w.severity === 'warning').length === 0,
		warnings
	};
}

/**
 * Serializes an ExportableRecipe into strict, standards-compliant BeerXML 1.0.
 */
export function serializeBeerXml(recipe: ExportableRecipe): ExportResult {
	const report = analyzeBeerXmlCompatibility(recipe);
	const lines: string[] = [];

	lines.push('<?xml version="1.0" encoding="UTF-8"?>');
	lines.push('<RECIPES>');
	lines.push('  <RECIPE>');
	lines.push(`    <NAME>${escapeXml(recipe.name || 'Untitled Recipe')}</NAME>`);
	lines.push('    <VERSION>1</VERSION>');
	lines.push('    <TYPE>All Grain</TYPE>');
	lines.push(`    <BREWER>${escapeXml(recipe.brewer || 'BrewYou User')}</BREWER>`);
	lines.push(`    <BATCH_SIZE>${formatXmlNumber(recipe.batchSizeLiters, 2)}</BATCH_SIZE>`);

	// Calculate boil size with standard boil-off rate (3.0 L/hr) if not explicit
	const boilOffLiters = ((recipe.boilTimeMinutes || 60) / 60) * 3.0;
	const boilSizeLiters = (recipe.batchSizeLiters || 20) + boilOffLiters;
	lines.push(`    <BOIL_SIZE>${formatXmlNumber(boilSizeLiters, 2)}</BOIL_SIZE>`);
	lines.push(`    <BOIL_TIME>${formatXmlNumber(recipe.boilTimeMinutes || 60, 1)}</BOIL_TIME>`);
	lines.push(`    <EFFICIENCY>${formatXmlNumber(recipe.efficiencyPercent || 72, 1)}</EFFICIENCY>`);

	if (recipe.originalGravity) {
		lines.push(`    <OG>${formatXmlNumber(recipe.originalGravity, 4)}</OG>`);
	}
	if (recipe.finalGravity) {
		lines.push(`    <FG>${formatXmlNumber(recipe.finalGravity, 4)}</FG>`);
	}
	if (recipe.bitternessIbu !== undefined && recipe.bitternessIbu !== null) {
		lines.push(`    <IBU>${formatXmlNumber(recipe.bitternessIbu, 1)}</IBU>`);
	}
	if (recipe.colorSrm !== undefined && recipe.colorSrm !== null) {
		lines.push(`    <EST_COLOR>${formatXmlNumber(recipe.colorSrm, 1)}</EST_COLOR>`);
	}
	if (recipe.description) {
		lines.push(`    <NOTES>${escapeXml(recipe.description)}</NOTES>`);
	}

	// Fermentation profile
	if (recipe.fermentationSteps && recipe.fermentationSteps.length > 0) {
		const sortedSteps = [...recipe.fermentationSteps].sort((a, b) => a.stepOrder - b.stepOrder);
		const primary = sortedSteps.find((s) => s.type === 'Primary') || sortedSteps[0];
		if (primary) {
			lines.push(`    <PRIMARY_AGE>${formatXmlNumber(primary.durationDays, 1)}</PRIMARY_AGE>`);
			lines.push(
				`    <PRIMARY_TEMP>${formatXmlNumber(primary.targetTemperatureC, 1)}</PRIMARY_TEMP>`
			);
		}
		const secondary = sortedSteps.find((s) => s.type === 'Secondary' || s.type === 'Ramp');
		if (secondary && secondary !== primary) {
			lines.push(
				`    <SECONDARY_AGE>${formatXmlNumber(secondary.durationDays, 1)}</SECONDARY_AGE>`
			);
			lines.push(
				`    <SECONDARY_TEMP>${formatXmlNumber(secondary.targetTemperatureC, 1)}</SECONDARY_TEMP>`
			);
		}
		const tertiary = sortedSteps.find(
			(s) => s.type === 'ColdCrash' || (s.type as string) === 'Tertiary'
		);
		if (tertiary && tertiary !== primary && tertiary !== secondary) {
			lines.push(`    <TERTIARY_AGE>${formatXmlNumber(tertiary.durationDays, 1)}</TERTIARY_AGE>`);
			lines.push(
				`    <TERTIARY_TEMP>${formatXmlNumber(tertiary.targetTemperatureC, 1)}</TERTIARY_TEMP>`
			);
		}
		const conditioning = sortedSteps.find((s) => s.type === 'Conditioning');
		if (
			conditioning &&
			conditioning !== primary &&
			conditioning !== secondary &&
			conditioning !== tertiary
		) {
			lines.push(`    <AGE>${formatXmlNumber(conditioning.durationDays, 1)}</AGE>`);
			lines.push(`    <AGE_TEMP>${formatXmlNumber(conditioning.targetTemperatureC, 1)}</AGE_TEMP>`);
		}
	}

	// Style Record
	const styleType = detectFermentationType(recipe.beerStyle);
	lines.push('    <STYLE>');
	lines.push(`      <NAME>${escapeXml(recipe.beerStyle || 'Custom')}</NAME>`);
	lines.push('      <VERSION>1</VERSION>');
	lines.push('      <CATEGORY>Custom</CATEGORY>');
	lines.push('      <CATEGORY_NUMBER>1</CATEGORY_NUMBER>');
	lines.push('      <STYLE_LETTER>A</STYLE_LETTER>');
	lines.push('      <STYLE_GUIDE>BJCP</STYLE_GUIDE>');
	lines.push(`      <TYPE>${styleType}</TYPE>`);
	lines.push('    </STYLE>');

	// Fermentables
	const fermentables = recipe.ingredients.filter((i) => getIngType(i) === 'Fermentable');
	lines.push('    <FERMENTABLES>');
	for (const f of fermentables) {
		// Amount in kg
		const amountKg = f.unit.toLowerCase() === 'g' ? f.amount / 1000.0 : f.amount;
		const yieldVal = potentialToYieldPercent(f.potentialGravity);
		const colorVal = f.colorSrm ?? 2.0;

		lines.push('      <FERMENTABLE>');
		lines.push(`        <NAME>${escapeXml(getIngName(f))}</NAME>`);
		lines.push('        <VERSION>1</VERSION>');
		lines.push('        <TYPE>Grain</TYPE>');
		lines.push(`        <AMOUNT>${formatXmlNumber(amountKg, 4)}</AMOUNT>`);
		lines.push(`        <YIELD>${formatXmlNumber(yieldVal, 1)}</YIELD>`);
		lines.push(`        <COLOR>${formatXmlNumber(colorVal, 1)}</COLOR>`);
		if (f.notes) {
			lines.push(`        <NOTES>${escapeXml(f.notes)}</NOTES>`);
		}
		lines.push('      </FERMENTABLE>');
	}
	lines.push('    </FERMENTABLES>');

	// Hops
	const hops = recipe.ingredients.filter((i) => getIngType(i) === 'Hop');
	lines.push('    <HOPS>');
	for (const h of hops) {
		// Amount in kg (BeerXML standard strictly requires kg, formatted with 5 decimal precision)
		const amountKg = h.unit.toLowerCase() === 'kg' ? h.amount : h.amount / 1000.0;
		const alphaVal = h.alphaAcidPercent ?? 5.0;
		const useVal = mapBeerXmlHopUse(h.usage);

		// Time: days for Dry Hop, minutes for other usages
		let timeVal = h.durationMinutes ?? (useVal === 'Dry Hop' ? 3 : 60);
		if (useVal === 'Dry Hop') {
			timeVal = Math.round((timeVal / 1440) * 100) / 100;
			if (timeVal <= 0) timeVal = 3.0; // fallback to 3 days
		}

		const hopForm = h.form || 'Pellet';

		lines.push('      <HOP>');
		lines.push(`        <NAME>${escapeXml(getIngName(h))}</NAME>`);
		lines.push('        <VERSION>1</VERSION>');
		lines.push(`        <ALPHA>${formatXmlNumber(alphaVal, 2)}</ALPHA>`);
		lines.push(`        <AMOUNT>${formatXmlNumber(amountKg, 5)}</AMOUNT>`);
		lines.push(`        <USE>${useVal}</USE>`);
		lines.push(`        <TIME>${formatXmlNumber(timeVal, 2)}</TIME>`);
		lines.push(`        <FORM>${escapeXml(hopForm)}</FORM>`);
		if (h.notes) {
			lines.push(`        <NOTES>${escapeXml(h.notes)}</NOTES>`);
		}
		lines.push('      </HOP>');
	}
	lines.push('    </HOPS>');

	// Yeasts
	const yeasts = recipe.ingredients.filter((i) => getIngType(i) === 'Yeast');
	lines.push('    <YEASTS>');
	if (yeasts.length > 0) {
		for (const y of yeasts) {
			const attenVal = y.attenuationPercent ?? 75.0;
			const yeastDetected = detectFermentationType(getIngName(y));
			const yeastType = yeastDetected !== 'Ale' ? yeastDetected : styleType;
			const yeastForm = y.form || 'Dry';

			let yeastAmountKgOrL: number;
			const rawUnit = (y.unit || 'g').toLowerCase().trim();
			if (rawUnit === 'g') {
				yeastAmountKgOrL = y.amount / 1000.0;
			} else if (rawUnit === 'kg' || rawUnit === 'l') {
				yeastAmountKgOrL = y.amount;
			} else if (rawUnit === 'ml') {
				yeastAmountKgOrL = y.amount / 1000.0;
			} else if (rawUnit === 'pkg') {
				yeastAmountKgOrL = yeastForm === 'Liquid' ? y.amount * 0.125 : y.amount * 0.0115;
			} else {
				yeastAmountKgOrL = y.amount <= 1.0 ? y.amount : y.amount / 1000.0;
			}

			const amountIsWeight = yeastForm !== 'Liquid' && rawUnit !== 'ml' && rawUnit !== 'l';

			lines.push('      <YEAST>');
			lines.push(`        <NAME>${escapeXml(getIngName(y))}</NAME>`);
			lines.push('        <VERSION>1</VERSION>');
			lines.push(`        <TYPE>${yeastType}</TYPE>`);
			lines.push(`        <FORM>${escapeXml(yeastForm)}</FORM>`);
			lines.push(`        <AMOUNT>${formatXmlNumber(yeastAmountKgOrL, 5)}</AMOUNT>`);
			lines.push(
				`        <AMOUNT_IS_WEIGHT>${amountIsWeight ? 'TRUE' : 'FALSE'}</AMOUNT_IS_WEIGHT>`
			);
			lines.push(`        <ATTENUATION>${formatXmlNumber(attenVal, 1)}</ATTENUATION>`);
			if (y.notes) {
				lines.push(`        <NOTES>${escapeXml(y.notes)}</NOTES>`);
			}
			lines.push('      </YEAST>');
		}
	} else {
		// Emit fallback standard yeast if recipe has none
		const fallbackType = styleType;
		lines.push('      <YEAST>');
		lines.push(`        <NAME>Standard ${fallbackType} Yeast</NAME>`);
		lines.push('        <VERSION>1</VERSION>');
		lines.push(`        <TYPE>${fallbackType}</TYPE>`);
		lines.push('        <FORM>Dry</FORM>');
		lines.push('        <AMOUNT>0.011</AMOUNT>');
		lines.push('        <AMOUNT_IS_WEIGHT>TRUE</AMOUNT_IS_WEIGHT>');
		lines.push('        <ATTENUATION>75.0</ATTENUATION>');
		lines.push('      </YEAST>');
	}
	lines.push('    </YEASTS>');

	// Miscs (Other ingredients)
	const miscs = recipe.ingredients.filter((i) => getIngType(i) === 'Other');
	if (miscs.length > 0) {
		lines.push('    <MISCS>');
		for (const m of miscs) {
			const amountVal = m.unit.toLowerCase() === 'kg' ? m.amount : m.amount / 1000.0;
			const useVal = mapBeerXmlMiscUse(m.usage);
			const timeVal = m.durationMinutes ?? 15;

			lines.push('      <MISC>');
			lines.push(`        <NAME>${escapeXml(getIngName(m))}</NAME>`);
			lines.push('        <VERSION>1</VERSION>');
			lines.push('        <TYPE>Other</TYPE>');
			lines.push(`        <USE>${useVal}</USE>`);
			lines.push(`        <TIME>${formatXmlNumber(timeVal, 1)}</TIME>`);
			lines.push(`        <AMOUNT>${formatXmlNumber(amountVal, 4)}</AMOUNT>`);
			lines.push('        <AMOUNT_IS_WEIGHT>TRUE</AMOUNT_IS_WEIGHT>');
			if (m.notes) {
				lines.push(`        <NOTES>${escapeXml(m.notes)}</NOTES>`);
			}
			lines.push('      </MISC>');
		}
		lines.push('    </MISCS>');
	}

	// Mash Steps
	lines.push('    <MASH>');
	lines.push('      <NAME>Mash Profile</NAME>');
	lines.push('      <VERSION>1</VERSION>');
	lines.push('      <GRAIN_TEMP>20.0</GRAIN_TEMP>');
	lines.push('      <MASH_STEPS>');
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

	for (const step of steps) {
		lines.push('        <MASH_STEP>');
		lines.push(`          <NAME>${escapeXml(step.name || `Step ${step.stepOrder}`)}</NAME>`);
		lines.push('          <VERSION>1</VERSION>');
		lines.push(`          <TYPE>${step.type || 'Temperature'}</TYPE>`);
		lines.push(`          <STEP_TEMP>${formatXmlNumber(step.temperatureC, 1)}</STEP_TEMP>`);
		lines.push(`          <STEP_TIME>${formatXmlNumber(step.durationMinutes, 1)}</STEP_TIME>`);
		if (step.rampTimeMinutes) {
			lines.push(`          <RAMP_TIME>${formatXmlNumber(step.rampTimeMinutes, 1)}</RAMP_TIME>`);
		}
		if (step.infuseAmountLiters) {
			lines.push(
				`          <INFUSE_AMOUNT>${formatXmlNumber(step.infuseAmountLiters, 2)}</INFUSE_AMOUNT>`
			);
		}
		if (step.notes) {
			lines.push(`          <DESCRIPTION>${escapeXml(step.notes)}</DESCRIPTION>`);
		}
		lines.push('        </MASH_STEP>');
	}
	lines.push('      </MASH_STEPS>');
	lines.push('    </MASH>');

	lines.push('  </RECIPE>');
	lines.push('</RECIPES>');

	const content = lines.join('\n');
	const filename = sanitizeFilename(recipe.name, 'xml');

	return {
		format: 'beerxml',
		mimeType: 'application/xml;charset=utf-8',
		fileExtension: 'xml',
		filename,
		content,
		report
	};
}
