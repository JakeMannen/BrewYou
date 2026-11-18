/**
 * Master Brewer Carbonation Cheat Sheet Data & Physics
 * Grounded in BJCP 2021 style profiles, ASBC / Zahm & Nagel CO2 equilibrium regressions,
 * Henry's law residual CO2 solubility, and draft line balancing hydrodynamics.
 */

export const VOLUMES_TO_GRAMS_PER_LITER = 1.96;
export const PSI_TO_BAR = 0.0689476;
export const BAR_TO_PSI = 14.5038;
export const KPA_PER_PSI = 6.89476;
export const FEET_TO_METERS = 0.3048;

export type CarbonationZone = 'low' | 'standard' | 'high' | 'effervescent';
export type BottleSafetyLevel = 'standard' | 'caution' | 'heavy_glass_required';

export interface CarbonationStyleEntry {
	id: string;
	nameKey: string;
	examplesKey: string;
	notesKey: string;
	minVol: number;
	maxVol: number;
	defaultVol: number;
	zone: CarbonationZone;
	isNitro?: boolean;
	requiresHeavyGlass?: boolean;
}

export interface PrimingAgentGuideEntry {
	id: 'sucrose' | 'dextrose' | 'dme' | 'honey' | 'candi_syrup';
	nameKey: string;
	notesKey: string;
	yieldFactor: number;
	multiplierVsSucrose: number;
	gramsPerLiterPerVol: number;
}

export interface ForceCarbMethodEntry {
	id: 'set_and_forget' | 'burst' | 'gentle_roll' | 'carb_stone' | 'spunding';
	titleKey: string;
	timingKey: string;
	summaryKey: string;
	cautionKey?: string;
}

export interface DraftTubingSpec {
	id: 'vinyl_3_16' | 'evabarrier_4mm' | 'evabarrier_5mm' | 'vinyl_1_4';
	nameKey: string;
	resistancePsiPerFoot: number;
	resistanceBarPerMeter: number;
	typicalLengthKey: string;
}

export interface DraftTroubleshootEntry {
	id: string;
	symptomKey: string;
	causeKey: string;
	fixKey: string;
}

export interface DraftLineLengthInput {
	kegPsi: number;
	verticalRiseFeet?: number;
	residualFaucetPsi?: number;
	resistancePsiPerFoot: number;
}

export interface DraftLineLengthResult {
	lengthFeet: number;
	lengthMeters: number;
	effectivePressurePsi: number;
}

export const FORCE_CARB_TEMPS_C = [0, 2, 4, 6, 8, 10, 12] as const;
export const FORCE_CARB_TEMPS_F = [32, 36, 40, 44, 48, 52, 56] as const;
export const FORCE_CARB_VOLUMES = [1.8, 2.0, 2.2, 2.4, 2.6, 2.8, 3.0, 3.2, 3.5] as const;

/**
 * Classifies a CO2 carbonation target (in volumes) into a brewing intensity zone.
 */
export function getCarbonationZone(co2Volumes: number): CarbonationZone {
	if (!Number.isFinite(co2Volumes) || co2Volumes < 2.15) {
		return 'low';
	}
	if (co2Volumes <= 2.65) {
		return 'standard';
	}
	if (co2Volumes <= 3.05) {
		return 'high';
	}
	return 'effervescent';
}

/**
 * Evaluates glass bottle pressure safety for natural bottle conditioning.
 */
export function getBottleSafetyLevel(co2Volumes: number): BottleSafetyLevel {
	if (!Number.isFinite(co2Volumes) || co2Volumes <= 3.0) {
		return 'standard';
	}
	if (co2Volumes <= 3.2) {
		return 'caution';
	}
	return 'heavy_glass_required';
}

export function psiToBar(psi: number): number {
	if (!Number.isFinite(psi) || psi <= 0) return 0;
	return Number((psi * PSI_TO_BAR).toFixed(2));
}

export function barToPsi(bar: number): number {
	if (!Number.isFinite(bar) || bar <= 0) return 0;
	return Number((bar * BAR_TO_PSI).toFixed(1));
}

/**
 * Calculates dissolved residual CO2 (in volumes) after unpressurized fermentation
 * using Henry's law quadratic regression. Clamps above 34.6°C (94.3°F) where the
 * empirical quadratic hits its mathematical minimum (0.678 vol).
 */
export function calculateResidualCo2(beerTempC: number): number {
	if (!Number.isFinite(beerTempC)) return 0.86;
	const tempF = (beerTempC * 9) / 5 + 32;
	const boundedTempF = Math.min(94.3, Math.max(30, tempF));
	const residual = 3.0378 - 0.050062 * boundedTempF + 0.00026555 * Math.pow(boundedTempF, 2);
	return Number(Math.max(0.5, residual).toFixed(2));
}

/**
 * Calculates equilibrium keg regulator gauge pressure (PSI at sea level) using the
 * ASBC / Zahm & Nagel solubility regression.
 */
export function calculateKegEquilibriumPsi(tempC: number, targetCo2Volumes: number): number {
	if (!Number.isFinite(tempC) || !Number.isFinite(targetCo2Volumes) || targetCo2Volumes <= 0) {
		return 0;
	}
	const tempF = (tempC * 9) / 5 + 32;
	const v = Math.max(0, targetCo2Volumes);
	const psi =
		-16.6999 -
		0.0101059 * tempF +
		0.00116512 * Math.pow(tempF, 2) +
		0.173354 * tempF * v +
		4.24267 * v -
		0.0684226 * Math.pow(v, 2);

	return Number(Math.max(0, psi).toFixed(1));
}

/**
 * Inverse ASBC / Zahm & Nagel equation: resolves dissolved CO2 volumes from
 * equilibrium gauge pressure (PSI) and beer temperature (°C).
 */
export function calculateCo2VolumesFromPsi(tempC: number, psi: number): number {
	if (!Number.isFinite(tempC) || !Number.isFinite(psi)) {
		return 0;
	}
	const tempF = (tempC * 9) / 5 + 32;
	const safePsi = Math.max(0, psi);
	const a = -16.6999 - 0.0101059 * tempF + 0.00116512 * Math.pow(tempF, 2);
	const b = 4.24267 + 0.173354 * tempF;
	const c = 0.0684226;

	const discriminant = Math.max(0, b * b - 4 * c * (safePsi - a));
	const volumes = (b - Math.sqrt(discriminant)) / (2 * c);
	return Number(Math.max(0, volumes).toFixed(2));
}

/**
 * Calculates balanced draft beverage line length accounting for static vertical lift
 * (~0.5 PSI/ft) and target faucet residual pressure (1–2 PSI).
 */
export function calculateDraftLineLength(input: DraftLineLengthInput): DraftLineLengthResult {
	const kegPsi = Number.isFinite(input.kegPsi) ? Math.max(0, input.kegPsi) : 0;
	const verticalRiseFeet = Number.isFinite(input.verticalRiseFeet ?? 2)
		? (input.verticalRiseFeet ?? 2)
		: 2;
	const residualFaucetPsi = Number.isFinite(input.residualFaucetPsi ?? 1.5)
		? Math.max(0, input.residualFaucetPsi ?? 1.5)
		: 1.5;
	const resistance = Number.isFinite(input.resistancePsiPerFoot)
		? Math.max(0.2, input.resistancePsiPerFoot)
		: 2.2;

	const effectivePressurePsi = Math.max(0, kegPsi - verticalRiseFeet * 0.5 - residualFaucetPsi);
	const lengthFeet = effectivePressurePsi / resistance;
	const lengthMeters = lengthFeet * FEET_TO_METERS;

	return {
		lengthFeet: Number(lengthFeet.toFixed(1)),
		lengthMeters: Number(lengthMeters.toFixed(2)),
		effectivePressurePsi: Number(effectivePressurePsi.toFixed(1))
	};
}

/**
 * BJCP 2021 aligned carbonation targets across major craft beer style families.
 */
export const CARBONATION_STYLE_GUIDE: CarbonationStyleEntry[] = [
	{
		id: 'cask-british',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.cask_british.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.cask_british.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.cask_british.notes',
		minVol: 1.5,
		maxVol: 2.0,
		defaultVol: 1.8,
		zone: 'low'
	},
	{
		id: 'irish-stout-nitro',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.irish_stout_nitro.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.irish_stout_nitro.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.irish_stout_nitro.notes',
		minVol: 1.2,
		maxVol: 1.5,
		defaultVol: 1.4,
		zone: 'low',
		isNitro: true
	},
	{
		id: 'irish-stout-co2',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.irish_stout_co2.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.irish_stout_co2.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.irish_stout_co2.notes',
		minVol: 1.8,
		maxVol: 2.1,
		defaultVol: 2.0,
		zone: 'low'
	},
	{
		id: 'british-porter-stout',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.british_porter_stout.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.british_porter_stout.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.british_porter_stout.notes',
		minVol: 1.8,
		maxVol: 2.3,
		defaultVol: 2.1,
		zone: 'low'
	},
	{
		id: 'imperial-barleywine',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.imperial_barleywine.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.imperial_barleywine.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.imperial_barleywine.notes',
		minVol: 1.8,
		maxVol: 2.3,
		defaultVol: 2.1,
		zone: 'low'
	},
	{
		id: 'american-ale-ipa',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.american_ale_ipa.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.american_ale_ipa.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.american_ale_ipa.notes',
		minVol: 2.2,
		maxVol: 2.6,
		defaultVol: 2.4,
		zone: 'standard'
	},
	{
		id: 'neipa-hazy',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.neipa_hazy.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.neipa_hazy.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.neipa_hazy.notes',
		minVol: 2.2,
		maxVol: 2.4,
		defaultVol: 2.3,
		zone: 'standard'
	},
	{
		id: 'german-amber-bock',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.german_amber_bock.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.german_amber_bock.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.german_amber_bock.notes',
		minVol: 2.2,
		maxVol: 2.6,
		defaultVol: 2.4,
		zone: 'standard'
	},
	{
		id: 'czech-pilsner',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.czech_pilsner.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.czech_pilsner.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.czech_pilsner.notes',
		minVol: 2.2,
		maxVol: 2.5,
		defaultVol: 2.4,
		zone: 'standard'
	},
	{
		id: 'helles-kolsch',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.helles_kolsch.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.helles_kolsch.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.helles_kolsch.notes',
		minVol: 2.4,
		maxVol: 2.6,
		defaultVol: 2.5,
		zone: 'standard'
	},
	{
		id: 'german-intl-pilsner',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.german_intl_pilsner.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.german_intl_pilsner.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.german_intl_pilsner.notes',
		minVol: 2.5,
		maxVol: 2.8,
		defaultVol: 2.7,
		zone: 'high'
	},
	{
		id: 'belgian-blond-dubbel',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.belgian_blond_dubbel.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.belgian_blond_dubbel.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.belgian_blond_dubbel.notes',
		minVol: 2.4,
		maxVol: 2.9,
		defaultVol: 2.7,
		zone: 'high'
	},
	{
		id: 'belgian-wit-tripel-quad',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.belgian_wit_tripel_quad.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.belgian_wit_tripel_quad.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.belgian_wit_tripel_quad.notes',
		minVol: 2.7,
		maxVol: 3.3,
		defaultVol: 3.0,
		zone: 'high',
		requiresHeavyGlass: true
	},
	{
		id: 'saison',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.saison.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.saison.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.saison.notes',
		minVol: 2.8,
		maxVol: 3.5,
		defaultVol: 3.2,
		zone: 'effervescent',
		requiresHeavyGlass: true
	},
	{
		id: 'hefeweizen',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.hefeweizen.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.hefeweizen.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.hefeweizen.notes',
		minVol: 2.8,
		maxVol: 3.5,
		defaultVol: 3.2,
		zone: 'effervescent',
		requiresHeavyGlass: true
	},
	{
		id: 'flanders-oud-bruin',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.flanders_oud_bruin.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.flanders_oud_bruin.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.flanders_oud_bruin.notes',
		minVol: 2.0,
		maxVol: 2.5,
		defaultVol: 2.3,
		zone: 'standard'
	},
	{
		id: 'straight-lambic',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.straight_lambic.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.straight_lambic.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.straight_lambic.notes',
		minVol: 1.0,
		maxVol: 1.5,
		defaultVol: 1.2,
		zone: 'low'
	},
	{
		id: 'berliner-gueuze',
		nameKey: 'calculations.carbonation_cheat_sheet.styles.berliner_gueuze.name',
		examplesKey: 'calculations.carbonation_cheat_sheet.styles.berliner_gueuze.examples',
		notesKey: 'calculations.carbonation_cheat_sheet.styles.berliner_gueuze.notes',
		minVol: 2.8,
		maxVol: 4.0,
		defaultVol: 3.4,
		zone: 'effervescent',
		requiresHeavyGlass: true
	}
];

export const PRIMING_SUGAR_GUIDE: PrimingAgentGuideEntry[] = [
	{
		id: 'sucrose',
		nameKey: 'calculations.carbonation_cheat_sheet.priming.agents.sucrose.name',
		notesKey: 'calculations.carbonation_cheat_sheet.priming.agents.sucrose.notes',
		yieldFactor: 1.0,
		multiplierVsSucrose: 1.0,
		gramsPerLiterPerVol: 4.01
	},
	{
		id: 'dextrose',
		nameKey: 'calculations.carbonation_cheat_sheet.priming.agents.dextrose.name',
		notesKey: 'calculations.carbonation_cheat_sheet.priming.agents.dextrose.notes',
		yieldFactor: 0.91,
		multiplierVsSucrose: 1.1,
		gramsPerLiterPerVol: 4.41
	},
	{
		id: 'dme',
		nameKey: 'calculations.carbonation_cheat_sheet.priming.agents.dme.name',
		notesKey: 'calculations.carbonation_cheat_sheet.priming.agents.dme.notes',
		yieldFactor: 0.68,
		multiplierVsSucrose: 1.47,
		gramsPerLiterPerVol: 5.9
	},
	{
		id: 'honey',
		nameKey: 'calculations.carbonation_cheat_sheet.priming.agents.honey.name',
		notesKey: 'calculations.carbonation_cheat_sheet.priming.agents.honey.notes',
		yieldFactor: 0.74,
		multiplierVsSucrose: 1.35,
		gramsPerLiterPerVol: 5.42
	},
	{
		id: 'candi_syrup',
		nameKey: 'calculations.carbonation_cheat_sheet.priming.agents.candi_syrup.name',
		notesKey: 'calculations.carbonation_cheat_sheet.priming.agents.candi_syrup.notes',
		yieldFactor: 0.8,
		multiplierVsSucrose: 1.25,
		gramsPerLiterPerVol: 5.02
	}
];

export const FORCE_CARB_METHODS: ForceCarbMethodEntry[] = [
	{
		id: 'set_and_forget',
		titleKey: 'calculations.carbonation_cheat_sheet.methods.set_and_forget.title',
		timingKey: 'calculations.carbonation_cheat_sheet.methods.set_and_forget.timing',
		summaryKey: 'calculations.carbonation_cheat_sheet.methods.set_and_forget.summary'
	},
	{
		id: 'burst',
		titleKey: 'calculations.carbonation_cheat_sheet.methods.burst.title',
		timingKey: 'calculations.carbonation_cheat_sheet.methods.burst.timing',
		summaryKey: 'calculations.carbonation_cheat_sheet.methods.burst.summary',
		cautionKey: 'calculations.carbonation_cheat_sheet.methods.burst.caution'
	},
	{
		id: 'gentle_roll',
		titleKey: 'calculations.carbonation_cheat_sheet.methods.gentle_roll.title',
		timingKey: 'calculations.carbonation_cheat_sheet.methods.gentle_roll.timing',
		summaryKey: 'calculations.carbonation_cheat_sheet.methods.gentle_roll.summary'
	},
	{
		id: 'carb_stone',
		titleKey: 'calculations.carbonation_cheat_sheet.methods.carb_stone.title',
		timingKey: 'calculations.carbonation_cheat_sheet.methods.carb_stone.timing',
		summaryKey: 'calculations.carbonation_cheat_sheet.methods.carb_stone.summary'
	},
	{
		id: 'spunding',
		titleKey: 'calculations.carbonation_cheat_sheet.methods.spunding.title',
		timingKey: 'calculations.carbonation_cheat_sheet.methods.spunding.timing',
		summaryKey: 'calculations.carbonation_cheat_sheet.methods.spunding.summary',
		cautionKey: 'calculations.carbonation_cheat_sheet.methods.spunding.caution'
	}
];

export const DRAFT_TUBING_SPECS: DraftTubingSpec[] = [
	{
		id: 'vinyl_3_16',
		nameKey: 'calculations.carbonation_cheat_sheet.draft.tubing.vinyl_3_16',
		resistancePsiPerFoot: 2.7,
		resistanceBarPerMeter: 0.61,
		typicalLengthKey: 'calculations.carbonation_cheat_sheet.draft.typical.vinyl_3_16'
	},
	{
		id: 'evabarrier_4mm',
		nameKey: 'calculations.carbonation_cheat_sheet.draft.tubing.evabarrier_4mm',
		resistancePsiPerFoot: 2.2,
		resistanceBarPerMeter: 0.5,
		typicalLengthKey: 'calculations.carbonation_cheat_sheet.draft.typical.evabarrier_4mm'
	},
	{
		id: 'evabarrier_5mm',
		nameKey: 'calculations.carbonation_cheat_sheet.draft.tubing.evabarrier_5mm',
		resistancePsiPerFoot: 1.0,
		resistanceBarPerMeter: 0.23,
		typicalLengthKey: 'calculations.carbonation_cheat_sheet.draft.typical.evabarrier_5mm'
	},
	{
		id: 'vinyl_1_4',
		nameKey: 'calculations.carbonation_cheat_sheet.draft.tubing.vinyl_1_4',
		resistancePsiPerFoot: 0.8,
		resistanceBarPerMeter: 0.18,
		typicalLengthKey: 'calculations.carbonation_cheat_sheet.draft.typical.vinyl_1_4'
	}
];

export const DRAFT_TROUBLESHOOTING_GUIDE: DraftTroubleshootEntry[] = [
	{
		id: 'warm_tower',
		symptomKey: 'calculations.carbonation_cheat_sheet.draft.issues.warm_tower.symptom',
		causeKey: 'calculations.carbonation_cheat_sheet.draft.issues.warm_tower.cause',
		fixKey: 'calculations.carbonation_cheat_sheet.draft.issues.warm_tower.fix'
	},
	{
		id: 'breakout_in_line',
		symptomKey: 'calculations.carbonation_cheat_sheet.draft.issues.breakout_in_line.symptom',
		causeKey: 'calculations.carbonation_cheat_sheet.draft.issues.breakout_in_line.cause',
		fixKey: 'calculations.carbonation_cheat_sheet.draft.issues.breakout_in_line.fix'
	},
	{
		id: 'insufficient_resistance',
		symptomKey: 'calculations.carbonation_cheat_sheet.draft.issues.insufficient_resistance.symptom',
		causeKey: 'calculations.carbonation_cheat_sheet.draft.issues.insufficient_resistance.cause',
		fixKey: 'calculations.carbonation_cheat_sheet.draft.issues.insufficient_resistance.fix'
	},
	{
		id: 'dip_tube_oring',
		symptomKey: 'calculations.carbonation_cheat_sheet.draft.issues.dip_tube_oring.symptom',
		causeKey: 'calculations.carbonation_cheat_sheet.draft.issues.dip_tube_oring.cause',
		fixKey: 'calculations.carbonation_cheat_sheet.draft.issues.dip_tube_oring.fix'
	},
	{
		id: 'flat_pour',
		symptomKey: 'calculations.carbonation_cheat_sheet.draft.issues.flat_pour.symptom',
		causeKey: 'calculations.carbonation_cheat_sheet.draft.issues.flat_pour.cause',
		fixKey: 'calculations.carbonation_cheat_sheet.draft.issues.flat_pour.fix'
	},
	{
		id: 'half_pull',
		symptomKey: 'calculations.carbonation_cheat_sheet.draft.issues.half_pull.symptom',
		causeKey: 'calculations.carbonation_cheat_sheet.draft.issues.half_pull.cause',
		fixKey: 'calculations.carbonation_cheat_sheet.draft.issues.half_pull.fix'
	}
];
