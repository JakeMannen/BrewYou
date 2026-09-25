import { browser } from '$app/environment';
import { api } from '$lib/api/client';
import { auth, onAuthCleared } from '$lib/stores/auth.svelte';
import { theme, type ThemePreference as ThemeStorePreference } from '$lib/stores/theme.svelte';
import { formatNumber } from '$lib/utils/formatNumber';
import type {
	GravityUnit,
	TemperatureUnit,
	ThemePreference,
	UserPreferencesDto,
	VolumeUnit,
	WeightUnit
} from '$lib/types/api';

export type ColorUnit = 'SRM' | 'EBC';
export type HopWeightUnit = 'g' | 'oz';
export type GrainWeightUnit = 'kg' | 'lb';
export type CarbonationUnit = 'Volumes' | 'GramsPerLiter';

export type BitternessFormula = 'Tinseth' | 'Rager' | 'Daniels';
export type ColorFormula = 'Morey' | 'Mosher' | 'Daniels';
export type AbvFormula = 'Linear' | 'Advanced';

export type UnitPreset = 'European Metric' | 'US Craft' | 'UK Traditional' | 'Custom';
export const UNIT_PRESETS: Exclude<UnitPreset, 'Custom'>[] = [
	'European Metric',
	'US Craft',
	'UK Traditional'
];

// Volume conversion constants
export const LITERS_TO_GALLONS = 0.264172052;
export const GALLONS_TO_LITERS = 3.785411784;

// Weight conversion constants
export const KG_TO_POUNDS = 2.20462262185;
export const POUNDS_TO_KG = 0.45359237;
export const GRAMS_TO_OUNCES = 0.03527396195;
export const OUNCES_TO_GRAMS = 28.349523125;

// Color & Carbonation conversion constants
export const SRM_TO_EBC = 1.97;
export const EBC_TO_SRM = 1 / 1.97;
export const VOLUMES_TO_G_L = 1.96;
export const G_L_TO_VOLUMES = 1 / 1.96;

// Volume helpers
export function litersToGallons(liters: number): number {
	if (isNaN(liters)) return 0;
	return Math.round(liters * LITERS_TO_GALLONS * 100) / 100;
}

export function gallonsToLiters(gallons: number): number {
	if (isNaN(gallons)) return 0;
	return Math.round(gallons * GALLONS_TO_LITERS * 100) / 100;
}

export function formatVolume(
	liters: number | null | undefined,
	unit: VolumeUnit,
	decimals = 1
): string {
	if (liters == null || isNaN(liters)) return '-';
	if (unit === 'Gallons') {
		const gal = liters * LITERS_TO_GALLONS;
		return `${formatNumber(gal, decimals)} gal`;
	}
	return `${formatNumber(liters, decimals)} L`;
}

export function toDisplayVolume(liters: number, unit: VolumeUnit): number {
	if (unit === 'Gallons') {
		return litersToGallons(liters);
	}
	return Math.round(liters * 100) / 100;
}

export function fromDisplayVolume(displayValue: number, unit: VolumeUnit): number {
	if (unit === 'Gallons') {
		return gallonsToLiters(displayValue);
	}
	return Math.round(displayValue * 100) / 100;
}

// Weight helpers (Fermentables in kg/lb, Hops/salts in g/oz)
export function kgToPounds(kg: number): number {
	if (isNaN(kg)) return 0;
	return Math.round(kg * KG_TO_POUNDS * 100) / 100;
}

export function poundsToKg(lb: number): number {
	if (isNaN(lb)) return 0;
	return Math.round(lb * POUNDS_TO_KG * 100) / 100;
}

export function gramsToOunces(grams: number): number {
	if (isNaN(grams)) return 0;
	return Math.round(grams * GRAMS_TO_OUNCES * 100) / 100;
}

export function ouncesToGrams(oz: number): number {
	if (isNaN(oz)) return 0;
	return Math.round(oz * OUNCES_TO_GRAMS * 100) / 100;
}

export function formatGrainWeight(
	kg: number | null | undefined,
	unit: WeightUnit | GrainWeightUnit = 'kg',
	decimals = 2
): string {
	if (kg == null || isNaN(kg)) return '-';
	if (unit === 'Imperial' || unit === 'lb') {
		const lb = kg * KG_TO_POUNDS;
		return `${formatNumber(lb, decimals)} lb`;
	}
	return `${formatNumber(kg, decimals)} kg`;
}

export function formatHopWeight(
	grams: number | null | undefined,
	unit: WeightUnit | HopWeightUnit = 'g',
	decimals = 1
): string {
	if (grams == null || isNaN(grams)) return '-';
	if (unit === 'Imperial' || unit === 'oz') {
		const oz = grams * GRAMS_TO_OUNCES;
		return `${formatNumber(oz, decimals)} oz`;
	}
	return `${formatNumber(grams, decimals)} g`;
}

// Color helpers
export function srmToEbc(srm: number): number {
	if (isNaN(srm) || srm < 0) return 0;
	return Math.round(srm * SRM_TO_EBC * 10) / 10;
}

export function ebcToSrm(ebc: number): number {
	if (isNaN(ebc) || ebc < 0) return 0;
	return Math.round(ebc * EBC_TO_SRM * 10) / 10;
}

export function formatColor(
	srm: number | null | undefined,
	unit: ColorUnit = 'SRM',
	decimals = 1
): string {
	if (srm == null || isNaN(srm)) return '-';
	if (unit === 'EBC') {
		const val = srm * SRM_TO_EBC;
		return `${formatNumber(val, decimals)} EBC`;
	}
	return `${formatNumber(srm, decimals)} SRM`;
}

export function toDisplayColor(srm: number, unit: ColorUnit): number {
	return unit === 'EBC' ? srmToEbc(srm) : Math.round(srm * 10) / 10;
}

export function fromDisplayColor(displayVal: number, unit: ColorUnit): number {
	return unit === 'EBC' ? ebcToSrm(displayVal) : Math.round(displayVal * 10) / 10;
}

// Carbonation helpers
export function volumesToGramsPerLiter(vols: number): number {
	if (isNaN(vols) || vols < 0) return 0;
	return Math.round(vols * VOLUMES_TO_G_L * 100) / 100;
}

export function gramsPerLiterToVolumes(gPerL: number): number {
	if (isNaN(gPerL) || gPerL < 0) return 0;
	return Math.round(gPerL * G_L_TO_VOLUMES * 100) / 100;
}

export function formatCarbonation(
	vols: number | null | undefined,
	unit: CarbonationUnit = 'Volumes',
	decimals = 2
): string {
	if (vols == null || isNaN(vols)) return '-';
	if (unit === 'GramsPerLiter') {
		const gL = vols * VOLUMES_TO_G_L;
		return `${formatNumber(gL, decimals)} g/L`;
	}
	return `${formatNumber(vols, decimals)} vol`;
}

// Temperature helpers
export function celsiusToFahrenheit(celsius: number): number {
	if (isNaN(celsius)) return 0;
	return Math.round(((celsius * 9) / 5 + 32) * 10) / 10;
}

export function fahrenheitToCelsius(fahrenheit: number): number {
	if (isNaN(fahrenheit)) return 0;
	return Math.round((((fahrenheit - 32) * 5) / 9) * 10) / 10;
}

export function formatTemperature(
	celsius: number | null | undefined,
	unit: TemperatureUnit,
	decimals = 1
): string {
	if (celsius == null || isNaN(celsius)) return '-';
	if (unit === 'Fahrenheit') {
		const f = (celsius * 9) / 5 + 32;
		return `${formatNumber(f, decimals)} °F`;
	}
	return `${formatNumber(celsius, decimals)} °C`;
}

// Gravity helpers (Specific Gravity vs Degrees Plato)
export function sgToPlato(sg: number): number {
	if (isNaN(sg) || sg <= 1.0) return 0;
	// ASBC standard polynomial
	const plato = -616.868 + 1111.14 * sg - 630.272 * Math.pow(sg, 2) + 135.997 * Math.pow(sg, 3);
	return Math.round(Math.max(0, plato) * 10) / 10;
}

export function platoToSg(plato: number): number {
	if (isNaN(plato) || plato <= 0) return 1.0;
	// Standard Lincoln inverse formula
	const sg = 1 + plato / (258.6 - (plato / 258.2) * 227.1);
	return Math.round(sg * 1000) / 1000;
}

export function formatGravity(
	sg: number | null | undefined,
	unit: GravityUnit,
	decimals = 3
): string {
	if (sg == null || isNaN(sg)) return '-';
	if (unit === 'Plato') {
		const p = sgToPlato(sg);
		return `${formatNumber(p, 1)} °P`;
	}
	return formatNumber(sg, decimals);
}

class SettingsState {
	volumeUnit = $state<VolumeUnit>('Liters');
	grainWeightUnit = $state<GrainWeightUnit>('kg');
	hopWeightUnit = $state<HopWeightUnit>('g');
	colorUnit = $state<ColorUnit>('SRM');
	carbonationUnit = $state<CarbonationUnit>('Volumes');
	temperatureUnit = $state<TemperatureUnit>('Celsius');
	gravityUnit = $state<GravityUnit>('SpecificGravity');
	themePreference = $state<ThemePreference>('Dark');

	bitternessFormula = $state<BitternessFormula>('Tinseth');
	colorFormula = $state<ColorFormula>('Morey');
	abvFormula = $state<AbvFormula>('Linear');

	defaultBatchSize = $state<number>(20.0);
	defaultEfficiency = $state<number>(75.0);
	defaultBoilTime = $state<number>(60);
	mqttHost = $state<string>('');
	mqttPort = $state<number>(1883);
	mqttUsername = $state<string>('');
	mqttPassword = $state<string>('');
	mqttCertificate = $state<string>('');
	mqttTopicPrefix = $state<string>('brewyou/equipment');
	hasMqttPassword = $state<boolean>(false);
	mqttConnected = $state<boolean | null>(null);
	mqttChecking = $state<boolean>(false);

	get hasMqttConfigured(): boolean {
		return !!this.mqttHost.trim();
	}

	get weightUnit(): WeightUnit {
		return this.grainWeightUnit === 'lb' || this.hopWeightUnit === 'oz' ? 'Imperial' : 'Metric';
	}

	get unitShort(): string {
		return this.volumeUnit === 'Gallons' ? 'gal' : 'L';
	}

	get weightShort(): string {
		return this.grainWeightUnit;
	}

	get grainShort(): string {
		return this.grainWeightUnit;
	}

	get hopWeightShort(): string {
		return this.hopWeightUnit;
	}

	get hopShort(): string {
		return this.hopWeightUnit;
	}

	get colorShort(): string {
		return this.colorUnit;
	}

	get carbShort(): string {
		return this.carbonationUnit === 'Volumes' ? 'vol' : 'g/L';
	}

	get tempShort(): string {
		return this.temperatureUnit === 'Fahrenheit' ? '°F' : '°C';
	}

	get gravityShort(): string {
		return this.gravityUnit === 'Plato' ? '°P' : 'SG';
	}

	get defaultBatchSizeLiters(): number {
		return this.defaultBatchSize;
	}

	get defaultEfficiencyPercent(): number {
		return this.defaultEfficiency;
	}

	get defaultBoilTimeMinutes(): number {
		return this.defaultBoilTime;
	}

	formatVolume(liters: number | null | undefined, decimals = 1): string {
		return formatVolume(liters, this.volumeUnit, decimals);
	}

	formatTemperature(celsius: number | null | undefined, decimals = 1): string {
		return formatTemperature(celsius, this.temperatureUnit, decimals);
	}

	formatGravity(sg: number | null | undefined, decimals = 3): string {
		return formatGravity(sg, this.gravityUnit, decimals);
	}

	formatGrainWeight(kg: number | null | undefined, decimals = 2): string {
		return formatGrainWeight(kg, this.grainWeightUnit, decimals);
	}

	formatHopWeight(grams: number | null | undefined, decimals = 1): string {
		return formatHopWeight(grams, this.hopWeightUnit, decimals);
	}

	formatColor(srm: number | null | undefined, decimals = 1): string {
		return formatColor(srm, this.colorUnit, decimals);
	}

	formatCarb(vols: number | null | undefined, decimals = 2): string {
		return formatCarbonation(vols, this.carbonationUnit, decimals);
	}

	formatCarbonation(vols: number | null | undefined, decimals = 2): string {
		return formatCarbonation(vols, this.carbonationUnit, decimals);
	}

	toDisplayColor(srm: number): number {
		return toDisplayColor(srm, this.colorUnit);
	}

	fromDisplayColor(value: number): number {
		return fromDisplayColor(value, this.colorUnit);
	}

	sgToPlato(sg: number): number {
		return sgToPlato(sg);
	}

	platoToSg(plato: number): number {
		return platoToSg(plato);
	}

	init() {
		if (browser) {
			const savedVolume = localStorage.getItem('brewyou_volume_unit') as VolumeUnit | null;
			if (savedVolume === 'Liters' || savedVolume === 'Gallons') {
				this.volumeUnit = savedVolume;
			}

			const savedWeight = localStorage.getItem('brewyou_weight_unit') as WeightUnit | null;
			const savedGrain = localStorage.getItem(
				'brewyou_grain_weight_unit'
			) as GrainWeightUnit | null;
			if (savedGrain === 'kg' || savedGrain === 'lb') {
				this.grainWeightUnit = savedGrain;
			} else if (savedWeight) {
				this.grainWeightUnit = savedWeight === 'Imperial' ? 'lb' : 'kg';
			}

			const savedHop = localStorage.getItem('brewyou_hop_weight_unit') as HopWeightUnit | null;
			if (savedHop === 'g' || savedHop === 'oz') {
				this.hopWeightUnit = savedHop;
			} else if (savedWeight) {
				this.hopWeightUnit = savedWeight === 'Imperial' ? 'oz' : 'g';
			}

			const savedColor = localStorage.getItem('brewyou_color_unit') as ColorUnit | null;
			if (savedColor === 'SRM' || savedColor === 'EBC') {
				this.colorUnit = savedColor;
			}

			const savedCarb = localStorage.getItem('brewyou_carbonation_unit') as CarbonationUnit | null;
			if (savedCarb === 'Volumes' || savedCarb === 'GramsPerLiter') {
				this.carbonationUnit = savedCarb;
			}

			const savedBitterness = localStorage.getItem(
				'brewyou_bitterness_formula'
			) as BitternessFormula | null;
			if (
				savedBitterness === 'Tinseth' ||
				savedBitterness === 'Rager' ||
				savedBitterness === 'Daniels'
			) {
				this.bitternessFormula = savedBitterness;
			}

			const savedColorForm = localStorage.getItem('brewyou_color_formula') as ColorFormula | null;
			if (
				savedColorForm === 'Morey' ||
				savedColorForm === 'Mosher' ||
				savedColorForm === 'Daniels'
			) {
				this.colorFormula = savedColorForm;
			}

			const savedAbv = localStorage.getItem('brewyou_abv_formula') as AbvFormula | null;
			if (savedAbv === 'Linear' || savedAbv === 'Advanced') {
				this.abvFormula = savedAbv;
			}

			const savedTemp = localStorage.getItem('brewyou_temperature_unit') as TemperatureUnit | null;
			if (savedTemp === 'Celsius' || savedTemp === 'Fahrenheit') {
				this.temperatureUnit = savedTemp;
			}

			const savedGravity = localStorage.getItem('brewyou_gravity_unit') as GravityUnit | null;
			if (savedGravity === 'SpecificGravity' || savedGravity === 'Plato') {
				this.gravityUnit = savedGravity;
			}

			const savedTheme = localStorage.getItem('brewyou_theme_pref') as ThemePreference | null;
			if (
				savedTheme === 'Dark' ||
				savedTheme === 'Light' ||
				savedTheme === 'System' ||
				savedTheme === 'ImperialStout' ||
				savedTheme === 'ChocolatePorter' ||
				savedTheme === 'Obsidian'
			) {
				this.themePreference = savedTheme;
			}

			const savedBatch = localStorage.getItem('brewyou_default_batch_size');
			if (savedBatch && !isNaN(Number(savedBatch))) {
				this.defaultBatchSize = Number(savedBatch);
			}

			const savedEff = localStorage.getItem('brewyou_default_efficiency');
			if (savedEff && !isNaN(Number(savedEff))) {
				this.defaultEfficiency = Number(savedEff);
			}

			const savedBoil = localStorage.getItem('brewyou_default_boil_time');
			if (savedBoil && !isNaN(Number(savedBoil))) {
				this.defaultBoilTime = Number(savedBoil);
			}

			const savedMqttHost = localStorage.getItem('brewyou_mqtt_host');
			if (savedMqttHost !== null) this.mqttHost = savedMqttHost;

			const savedMqttPort = localStorage.getItem('brewyou_mqtt_port');
			if (savedMqttPort && !isNaN(Number(savedMqttPort))) {
				this.mqttPort = Number(savedMqttPort);
			}

			const savedMqttUser = localStorage.getItem('brewyou_mqtt_username');
			if (savedMqttUser !== null) this.mqttUsername = savedMqttUser;

			// Security: Remove any legacy plaintext MQTT password from localStorage
			localStorage.removeItem('brewyou_mqtt_password');

			const savedMqttCert = localStorage.getItem('brewyou_mqtt_certificate');
			if (savedMqttCert !== null) this.mqttCertificate = savedMqttCert;

			const savedMqttTopicPrefix = localStorage.getItem('brewyou_mqtt_topic_prefix');
			if (savedMqttTopicPrefix !== null) this.mqttTopicPrefix = savedMqttTopicPrefix;
		}

		if (auth.user?.preferences) {
			this.hydrateFromPreferences(auth.user.preferences);
		} else if (auth.user?.preferredVolumeUnit) {
			this.volumeUnit = auth.user.preferredVolumeUnit;
		}
	}

	hydrateFromPreferences(pref: UserPreferencesDto) {
		if (pref.volumeUnit) this.volumeUnit = pref.volumeUnit;
		if (pref.weightUnit) {
			if (!browser || !localStorage.getItem('brewyou_grain_weight_unit')) {
				this.grainWeightUnit = pref.weightUnit === 'Imperial' ? 'lb' : 'kg';
			}
			if (!browser || !localStorage.getItem('brewyou_hop_weight_unit')) {
				this.hopWeightUnit = pref.weightUnit === 'Imperial' ? 'oz' : 'g';
			}
		}
		if (pref.temperatureUnit) this.temperatureUnit = pref.temperatureUnit;
		if (pref.gravityUnit) this.gravityUnit = pref.gravityUnit;
		if (pref.theme) {
			this.themePreference = pref.theme;
			const clientTheme = (
				pref.theme === 'ImperialStout'
					? 'imperial-stout'
					: pref.theme === 'ChocolatePorter'
						? 'chocolate-porter'
						: pref.theme === 'Obsidian'
							? 'obsidian'
							: pref.theme.toLowerCase()
			) as ThemeStorePreference;
			theme.setTheme(clientTheme);
		}
		if (pref.defaultBatchSizeLiters) this.defaultBatchSize = pref.defaultBatchSizeLiters;
		if (pref.defaultEfficiencyPercent) this.defaultEfficiency = pref.defaultEfficiencyPercent;
		if (pref.defaultBoilTimeMinutes) this.defaultBoilTime = pref.defaultBoilTimeMinutes;
		if (pref.mqttHost !== undefined) this.mqttHost = pref.mqttHost ?? '';
		if (pref.mqttPort !== undefined && pref.mqttPort !== null) this.mqttPort = pref.mqttPort;
		if (pref.mqttUsername !== undefined) this.mqttUsername = pref.mqttUsername ?? '';
		if (pref.hasMqttPassword !== undefined) this.hasMqttPassword = !!pref.hasMqttPassword;
		if (pref.mqttCertificate !== undefined) this.mqttCertificate = pref.mqttCertificate ?? '';
		if (pref.mqttTopicPrefix !== undefined) {
			this.mqttTopicPrefix = pref.mqttTopicPrefix ?? 'brewyou/equipment';
		}

		this.persistToLocalStorage();

		if (this.hasMqttConfigured) {
			void this.checkMqttStatus();
			this.startMqttHeartbeat();
		} else {
			this.mqttConnected = null;
			this.stopMqttHeartbeat();
		}
	}

	setVolumeUnitFromAuth(unit: VolumeUnit) {
		this.volumeUnit = unit;
		if (browser) {
			localStorage.setItem('brewyou_volume_unit', unit);
		}
	}

	async setVolumeUnit(unit: VolumeUnit): Promise<void> {
		this.volumeUnit = unit;
		if (browser) {
			localStorage.setItem('brewyou_volume_unit', unit);
		}

		if (auth.isAuthenticated) {
			try {
				await api.auth.updateVolumeUnit(unit);
				if (auth.user) {
					auth.user.preferredVolumeUnit = unit;
					if (auth.user.preferences) {
						auth.user.preferences.volumeUnit = unit;
					}
				}
			} catch (err) {
				console.warn('Could not sync volume unit preference to profile:', err);
			}
		}
	}

	setWeightUnit(unit: WeightUnit): void {
		this.grainWeightUnit = unit === 'Imperial' ? 'lb' : 'kg';
		this.hopWeightUnit = unit === 'Imperial' ? 'oz' : 'g';
		if (browser) {
			localStorage.setItem('brewyou_weight_unit', unit);
			localStorage.setItem('brewyou_grain_weight_unit', this.grainWeightUnit);
			localStorage.setItem('brewyou_hop_weight_unit', this.hopWeightUnit);
		}
		void this.syncWeightUnitToBackend();
	}

	setGrainWeightUnit(unit: GrainWeightUnit): void {
		this.grainWeightUnit = unit;
		if (browser) {
			localStorage.setItem('brewyou_grain_weight_unit', unit);
			localStorage.setItem('brewyou_weight_unit', this.weightUnit);
		}
		void this.syncWeightUnitToBackend();
	}

	setHopWeightUnit(unit: HopWeightUnit): void {
		this.hopWeightUnit = unit;
		if (browser) {
			localStorage.setItem('brewyou_hop_weight_unit', unit);
			localStorage.setItem('brewyou_weight_unit', this.weightUnit);
		}
		void this.syncWeightUnitToBackend();
	}

	setColorUnit(unit: ColorUnit): void {
		this.colorUnit = unit;
		if (browser) {
			localStorage.setItem('brewyou_color_unit', unit);
		}
	}

	setCarbonationUnit(unit: CarbonationUnit): void {
		this.carbonationUnit = unit;
		if (browser) {
			localStorage.setItem('brewyou_carbonation_unit', unit);
		}
	}

	setBitternessFormula(formula: BitternessFormula): void {
		this.bitternessFormula = formula;
		if (browser) {
			localStorage.setItem('brewyou_bitterness_formula', formula);
		}
	}

	setColorFormula(formula: ColorFormula): void {
		this.colorFormula = formula;
		if (browser) {
			localStorage.setItem('brewyou_color_formula', formula);
		}
	}

	setAbvFormula(formula: AbvFormula): void {
		this.abvFormula = formula;
		if (browser) {
			localStorage.setItem('brewyou_abv_formula', formula);
		}
	}

	async applyPreset(preset: Exclude<UnitPreset, 'Custom'>): Promise<void> {
		if (preset === 'European Metric') {
			this.volumeUnit = 'Liters';
			this.grainWeightUnit = 'kg';
			this.hopWeightUnit = 'g';
			this.colorUnit = 'EBC';
			this.temperatureUnit = 'Celsius';
			this.gravityUnit = 'SpecificGravity';
			this.carbonationUnit = 'Volumes';
			this.bitternessFormula = 'Tinseth';
			this.colorFormula = 'Morey';
			this.abvFormula = 'Linear';
		} else if (preset === 'US Craft') {
			this.volumeUnit = 'Gallons';
			this.grainWeightUnit = 'lb';
			this.hopWeightUnit = 'oz';
			this.colorUnit = 'SRM';
			this.temperatureUnit = 'Fahrenheit';
			this.gravityUnit = 'SpecificGravity';
			this.carbonationUnit = 'Volumes';
			this.bitternessFormula = 'Tinseth';
			this.colorFormula = 'Morey';
			this.abvFormula = 'Linear';
		} else if (preset === 'UK Traditional') {
			this.volumeUnit = 'Liters';
			this.grainWeightUnit = 'kg';
			this.hopWeightUnit = 'oz';
			this.colorUnit = 'EBC';
			this.temperatureUnit = 'Celsius';
			this.gravityUnit = 'SpecificGravity';
			this.carbonationUnit = 'Volumes';
			this.bitternessFormula = 'Tinseth';
			this.colorFormula = 'Morey';
			this.abvFormula = 'Linear';
		}
		this.persistToLocalStorage();
		try {
			await this.syncAllToBackend();
		} catch (err) {
			console.warn('Could not sync preset preferences to backend:', err);
		}
	}

	private async syncWeightUnitToBackend(): Promise<void> {
		if (auth.isAuthenticated) {
			try {
				await api.auth.updatePreferences({
					weightUnit: this.weightUnit
				});
				if (auth.user?.preferences) {
					auth.user.preferences.weightUnit = this.weightUnit;
				}
			} catch (err) {
				console.warn('Could not sync weight unit preference to profile:', err);
			}
		}
	}

	setTemperatureUnit(unit: TemperatureUnit): void {
		this.temperatureUnit = unit;
		if (browser) {
			localStorage.setItem('brewyou_temperature_unit', unit);
		}
	}

	setGravityUnit(unit: GravityUnit): void {
		this.gravityUnit = unit;
		if (browser) {
			localStorage.setItem('brewyou_gravity_unit', unit);
		}
	}

	setThemePreference(themePref: ThemePreference): void {
		this.themePreference = themePref;
		const clientTheme = (
			themePref === 'ImperialStout'
				? 'imperial-stout'
				: themePref === 'ChocolatePorter'
					? 'chocolate-porter'
					: themePref === 'Obsidian'
						? 'obsidian'
						: themePref.toLowerCase()
		) as ThemeStorePreference;
		theme.setTheme(clientTheme);
		if (browser) {
			localStorage.setItem('brewyou_theme_pref', themePref);
		}
	}

	cycleTheme(): void {
		let next: ThemePreference;
		if (this.themePreference === 'ImperialStout') {
			next = 'ChocolatePorter';
		} else if (this.themePreference === 'ChocolatePorter') {
			next = 'Obsidian';
		} else if (this.themePreference === 'Obsidian') {
			next = 'Dark';
		} else if (this.themePreference === 'Dark') {
			next = 'Light';
		} else {
			next = 'ImperialStout';
		}
		this.setThemePreference(next);
		if (auth.isAuthenticated) {
			void auth.updatePreferences({ theme: next });
		}
	}

	async setMqttSettings(
		host: string,
		port: number,
		username?: string,
		password?: string,
		certificate?: string,
		topicPrefix?: string
	): Promise<void> {
		this.mqttHost = host;
		this.mqttPort = port;
		this.mqttUsername = username ?? '';
		if (password !== undefined) {
			this.mqttPassword = password;
			if (password.trim().length > 0) {
				this.hasMqttPassword = true;
			}
		}
		this.mqttCertificate = certificate ?? '';
		if (topicPrefix !== undefined) {
			this.mqttTopicPrefix = topicPrefix.trim() || 'brewyou/equipment';
		}
		await this.syncAllToBackend();
		if (this.hasMqttConfigured) {
			await this.checkMqttStatus();
			this.startMqttHeartbeat();
		} else {
			this.mqttConnected = null;
			this.stopMqttHeartbeat();
		}
	}

	async syncAllToBackend(): Promise<void> {
		this.persistToLocalStorage();

		if (auth.isAuthenticated) {
			const updatedUser = await api.auth.updatePreferences({
				volumeUnit: this.volumeUnit,
				weightUnit: this.weightUnit,
				temperatureUnit: this.temperatureUnit,
				gravityUnit: this.gravityUnit,
				theme: this.themePreference,
				defaultBatchSizeLiters: this.defaultBatchSize,
				defaultEfficiencyPercent: this.defaultEfficiency,
				defaultBoilTimeMinutes: this.defaultBoilTime,
				mqttHost: this.mqttHost.trim() || null,
				mqttPort: Number(this.mqttPort) || 1883,
				mqttUsername: this.mqttUsername.trim() || null,
				mqttPassword: this.mqttPassword ? this.mqttPassword : null,
				mqttCertificate: this.mqttCertificate.trim() || null,
				mqttTopicPrefix: this.mqttTopicPrefix.trim() || 'brewyou/equipment'
			});

			if (auth.user && updatedUser.preferences) {
				auth.user.preferences = updatedUser.preferences;
				auth.user.preferredVolumeUnit = updatedUser.preferredVolumeUnit;
				auth.user.preferredLanguage = updatedUser.preferredLanguage;
				if (updatedUser.preferences.hasMqttPassword !== undefined) {
					this.hasMqttPassword = !!updatedUser.preferences.hasMqttPassword;
				}
			}
		}
	}

	private persistToLocalStorage(): void {
		if (browser) {
			localStorage.setItem('brewyou_volume_unit', this.volumeUnit);
			localStorage.setItem('brewyou_weight_unit', this.weightUnit);
			localStorage.setItem('brewyou_grain_weight_unit', this.grainWeightUnit);
			localStorage.setItem('brewyou_hop_weight_unit', this.hopWeightUnit);
			localStorage.setItem('brewyou_color_unit', this.colorUnit);
			localStorage.setItem('brewyou_carbonation_unit', this.carbonationUnit);
			localStorage.setItem('brewyou_bitterness_formula', this.bitternessFormula);
			localStorage.setItem('brewyou_color_formula', this.colorFormula);
			localStorage.setItem('brewyou_abv_formula', this.abvFormula);
			localStorage.setItem('brewyou_temperature_unit', this.temperatureUnit);
			localStorage.setItem('brewyou_gravity_unit', this.gravityUnit);
			localStorage.setItem('brewyou_theme_pref', this.themePreference);
			localStorage.setItem('brewyou_default_batch_size', String(this.defaultBatchSize));
			localStorage.setItem('brewyou_default_efficiency', String(this.defaultEfficiency));
			localStorage.setItem('brewyou_default_boil_time', String(this.defaultBoilTime));
			localStorage.setItem('brewyou_mqtt_host', this.mqttHost);
			localStorage.setItem('brewyou_mqtt_port', String(this.mqttPort));
			localStorage.setItem('brewyou_mqtt_username', this.mqttUsername);
			localStorage.setItem('brewyou_mqtt_certificate', this.mqttCertificate);
			localStorage.setItem('brewyou_mqtt_topic_prefix', this.mqttTopicPrefix);
		}
	}

	format(liters: number | null | undefined, decimals = 1): string {
		return formatVolume(liters, this.volumeUnit, decimals);
	}

	formatGrain(kg: number | null | undefined, decimals = 2): string {
		return formatGrainWeight(kg, this.grainWeightUnit, decimals);
	}

	formatHop(grams: number | null | undefined, decimals = 1): string {
		return formatHopWeight(grams, this.hopWeightUnit, decimals);
	}

	formatTemp(celsius: number | null | undefined, decimals = 1): string {
		return formatTemperature(celsius, this.temperatureUnit, decimals);
	}

	formatGrav(sg: number | null | undefined, decimals = 3): string {
		return formatGravity(sg, this.gravityUnit, decimals);
	}

	private inFlightCheck: Promise<boolean> | null = null;
	private heartbeatTimer: ReturnType<typeof setInterval> | null = null;

	startMqttHeartbeat(intervalMs = 30000): void {
		if (!browser) return;
		if (this.heartbeatTimer) return;

		this.heartbeatTimer = setInterval(() => {
			if (auth.isAuthenticated && this.hasMqttConfigured) {
				void this.checkMqttStatus();
			}
		}, intervalMs);
	}

	stopMqttHeartbeat(): void {
		if (this.heartbeatTimer) {
			clearInterval(this.heartbeatTimer);
			this.heartbeatTimer = null;
		}
	}

	async checkMqttStatus(host?: string, port?: number): Promise<boolean> {
		if (browser && !auth.isAuthenticated && auth.isLoading) {
			await auth.ready();
		}

		if (!auth.isAuthenticated) {
			this.mqttConnected = null;
			return false;
		}

		const targetHost = host !== undefined ? host : this.mqttHost;
		const targetPort = port !== undefined ? port : this.mqttPort;

		if (!targetHost || !targetHost.trim()) {
			if (host === undefined) {
				this.mqttConnected = null;
			}
			return false;
		}

		// Deduplicate concurrent in-flight checks for default user settings broker
		if (host === undefined && this.inFlightCheck) {
			return this.inFlightCheck;
		}

		const promise = (async () => {
			this.mqttChecking = true;
			try {
				const res = await api.auth.getMqttStatus(targetHost, targetPort);
				const isConn = res?.connected ?? false;
				if (host === undefined || host === this.mqttHost) {
					this.mqttConnected = isConn;
				}
				return isConn;
			} catch {
				if (host === undefined || host === this.mqttHost) {
					this.mqttConnected = false;
				}
				return false;
			} finally {
				this.mqttChecking = false;
				if (host === undefined) {
					this.inFlightCheck = null;
				}
			}
		})();

		if (host === undefined) {
			this.inFlightCheck = promise;
		}

		return promise;
	}

	clearMqttSettings(): void {
		this.stopMqttHeartbeat();
		this.mqttHost = '';
		this.mqttPort = 1883;
		this.mqttUsername = '';
		this.mqttPassword = '';
		this.mqttCertificate = '';
		this.hasMqttPassword = false;
		this.mqttTopicPrefix = 'brewyou/equipment';
		this.mqttConnected = null;
		if (browser) {
			localStorage.removeItem('brewyou_mqtt_host');
			localStorage.removeItem('brewyou_mqtt_port');
			localStorage.removeItem('brewyou_mqtt_username');
			localStorage.removeItem('brewyou_mqtt_certificate');
			localStorage.removeItem('brewyou_mqtt_password');
			localStorage.removeItem('brewyou_mqtt_topic_prefix');
		}
	}
}

export const settings = new SettingsState();

onAuthCleared(() => {
	settings.clearMqttSettings();
});
