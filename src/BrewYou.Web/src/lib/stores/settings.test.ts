import { beforeEach, describe, expect, it, vi } from 'vitest';
import { i18n } from '$lib/i18n/index.svelte';

vi.mock('$app/environment', () => ({
	browser: true,
	dev: true,
	building: false,
	version: '1'
}));

import {
	celsiusToFahrenheit,
	ebcToSrm,
	fahrenheitToCelsius,
	formatCarbonation,
	formatColor,
	formatGrainWeight,
	formatGravity,
	formatHopWeight,
	formatTemperature,
	formatVolume,
	fromDisplayColor,
	fromDisplayVolume,
	gallonsToLiters,
	gramsPerLiterToVolumes,
	gramsToOunces,
	kgToPounds,
	litersToGallons,
	ouncesToGrams,
	platoToSg,
	poundsToKg,
	settings,
	sgToPlato,
	srmToEbc,
	toDisplayColor,
	toDisplayVolume,
	volumesToGramsPerLiter
} from './settings.svelte';
import { auth } from './auth.svelte';
import { api } from '$lib/api/client';

describe('Volume Unit Conversion Helpers', () => {
	it('converts liters to US gallons accurately', () => {
		expect(litersToGallons(0)).toBe(0);
		expect(litersToGallons(20)).toBeCloseTo(5.28, 2);
		expect(litersToGallons(40)).toBeCloseTo(10.57, 2);
		expect(litersToGallons(50)).toBeCloseTo(13.21, 2);
	});

	it('converts US gallons to liters accurately', () => {
		expect(gallonsToLiters(0)).toBe(0);
		expect(gallonsToLiters(5)).toBeCloseTo(18.93, 2);
		expect(gallonsToLiters(7)).toBeCloseTo(26.5, 2);
		expect(gallonsToLiters(10)).toBeCloseTo(37.85, 2);
	});

	it('handles NaN or invalid input safely in conversions', () => {
		expect(litersToGallons(NaN)).toBe(0);
		expect(gallonsToLiters(NaN)).toBe(0);
	});

	it('formats volumes with correct suffix based on unit', () => {
		expect(formatVolume(null, 'Liters')).toBe('-');
		expect(formatVolume(undefined, 'Gallons')).toBe('-');
		expect(formatVolume(40, 'Liters')).toBe('40.0 L');
		expect(formatVolume(40, 'Gallons')).toBe('10.6 gal');
		expect(formatVolume(20, 'Liters', 2)).toBe('20.00 L');
	});

	it('formats volumes respecting locale decimal separator', () => {
		i18n.setLocale('sv');
		expect(formatVolume(40.5, 'Liters')).toBe('40,5 L');
		i18n.setLocale('en');
		expect(formatVolume(40.5, 'Liters')).toBe('40.5 L');
	});

	it('converts to/from display values correctly', () => {
		expect(toDisplayVolume(40, 'Liters')).toBe(40);
		expect(toDisplayVolume(40, 'Gallons')).toBeCloseTo(10.57, 2);
		expect(fromDisplayVolume(10.57, 'Gallons')).toBeCloseTo(40.01, 1);
		expect(fromDisplayVolume(50, 'Liters')).toBe(50);
	});
});

describe('Weight Unit Conversion Helpers', () => {
	it('converts kg to pounds accurately', () => {
		expect(kgToPounds(0)).toBe(0);
		expect(kgToPounds(5)).toBeCloseTo(11.02, 2);
		expect(kgToPounds(10)).toBeCloseTo(22.05, 2);
		expect(kgToPounds(NaN)).toBe(0);
	});

	it('converts pounds to kg accurately', () => {
		expect(poundsToKg(0)).toBe(0);
		expect(poundsToKg(11.02)).toBeCloseTo(5.0, 1);
		expect(poundsToKg(22.05)).toBeCloseTo(10.0, 1);
		expect(poundsToKg(NaN)).toBe(0);
	});

	it('converts grams to ounces accurately', () => {
		expect(gramsToOunces(0)).toBe(0);
		expect(gramsToOunces(50)).toBeCloseTo(1.76, 2);
		expect(gramsToOunces(100)).toBeCloseTo(3.53, 2);
		expect(gramsToOunces(NaN)).toBe(0);
	});

	it('converts ounces to grams accurately', () => {
		expect(ouncesToGrams(0)).toBe(0);
		expect(ouncesToGrams(1.76)).toBeCloseTo(49.9, 0);
		expect(ouncesToGrams(3.53)).toBeCloseTo(100.07, 1);
		expect(ouncesToGrams(NaN)).toBe(0);
	});

	it('formats grain and hop weights according to active unit', () => {
		expect(formatGrainWeight(null, 'Metric')).toBe('-');
		expect(formatGrainWeight(5, 'Metric')).toBe('5.00 kg');
		expect(formatGrainWeight(5, 'Imperial')).toBe('11.02 lb');

		expect(formatHopWeight(undefined, 'Metric')).toBe('-');
		expect(formatHopWeight(50, 'Metric')).toBe('50.0 g');
		expect(formatHopWeight(50, 'Imperial')).toBe('1.8 oz');
	});
});

describe('Temperature Unit Conversion Helpers', () => {
	it('converts Celsius to Fahrenheit accurately', () => {
		expect(celsiusToFahrenheit(0)).toBe(32);
		expect(celsiusToFahrenheit(20)).toBe(68);
		expect(celsiusToFahrenheit(67)).toBe(152.6);
		expect(celsiusToFahrenheit(100)).toBe(212);
		expect(celsiusToFahrenheit(NaN)).toBe(0);
	});

	it('converts Fahrenheit to Celsius accurately', () => {
		expect(fahrenheitToCelsius(32)).toBe(0);
		expect(fahrenheitToCelsius(68)).toBe(20);
		expect(fahrenheitToCelsius(152.6)).toBe(67);
		expect(fahrenheitToCelsius(212)).toBe(100);
		expect(fahrenheitToCelsius(NaN)).toBe(0);
	});

	it('formats temperature with correct unit suffix', () => {
		expect(formatTemperature(null, 'Celsius')).toBe('-');
		expect(formatTemperature(67, 'Celsius')).toBe('67.0 °C');
		expect(formatTemperature(67, 'Fahrenheit')).toBe('152.6 °F');
	});
});

describe('Gravity Unit Conversion Helpers', () => {
	it('converts Specific Gravity to Degrees Plato accurately', () => {
		expect(sgToPlato(1.0)).toBe(0);
		expect(sgToPlato(1.05)).toBeCloseTo(12.4, 1);
		expect(sgToPlato(1.08)).toBeCloseTo(19.3, 1);
		expect(sgToPlato(NaN)).toBe(0);
	});

	it('converts Degrees Plato to Specific Gravity accurately', () => {
		expect(platoToSg(0)).toBe(1.0);
		expect(platoToSg(12.4)).toBeCloseTo(1.05, 3);
		expect(platoToSg(NaN)).toBe(1.0);
	});

	it('formats gravity according to chosen scale', () => {
		expect(formatGravity(null, 'SpecificGravity')).toBe('-');
		expect(formatGravity(1.054, 'SpecificGravity')).toBe('1.054');
		expect(formatGravity(1.054, 'Plato')).toBe('13.3 °P');
	});
});

class LocalStorageMock {
	private store: Record<string, string> = {};

	clear() {
		this.store = {};
	}

	getItem(key: string): string | null {
		return this.store[key] ?? null;
	}

	setItem(key: string, value: string) {
		this.store[key] = String(value);
	}

	removeItem(key: string) {
		delete this.store[key];
	}
}

const mockStorage = new LocalStorageMock();

describe('Settings State Store', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		mockStorage.clear();
		vi.stubGlobal('localStorage', mockStorage);
	});

	it('has sensible brewing defaults', () => {
		settings.setVolumeUnitFromAuth('Liters');
		expect(settings.volumeUnit).toBe('Liters');
		expect(settings.unitShort).toBe('L');
		expect(settings.weightShort).toBe('kg');
		expect(settings.hopWeightShort).toBe('g');
		expect(settings.tempShort).toBe('°C');
		expect(settings.gravityShort).toBe('SG');
	});

	it('updates units reactively', async () => {
		await settings.setVolumeUnit('Gallons');
		expect(settings.volumeUnit).toBe('Gallons');
		expect(settings.unitShort).toBe('gal');

		settings.setWeightUnit('Imperial');
		expect(settings.weightUnit).toBe('Imperial');
		expect(settings.weightShort).toBe('lb');
		expect(settings.hopWeightShort).toBe('oz');

		settings.setTemperatureUnit('Fahrenheit');
		expect(settings.temperatureUnit).toBe('Fahrenheit');
		expect(settings.tempShort).toBe('°F');

		settings.setGravityUnit('Plato');
		expect(settings.gravityUnit).toBe('Plato');
		expect(settings.gravityShort).toBe('°P');

		// Reset back
		await settings.setVolumeUnit('Liters');
		settings.setWeightUnit('Metric');
		settings.setTemperatureUnit('Celsius');
		settings.setGravityUnit('SpecificGravity');
	});

	it('hydrates state from user preferences object', async () => {
		settings.hydrateFromPreferences({
			language: 'sv',
			volumeUnit: 'Gallons',
			weightUnit: 'Imperial',
			temperatureUnit: 'Fahrenheit',
			gravityUnit: 'Plato',
			theme: 'Light',
			defaultBatchSizeLiters: 40,
			defaultEfficiencyPercent: 80,
			defaultBoilTimeMinutes: 90
		});

		expect(settings.volumeUnit).toBe('Gallons');
		expect(settings.weightUnit).toBe('Imperial');
		expect(settings.temperatureUnit).toBe('Fahrenheit');
		expect(settings.gravityUnit).toBe('Plato');
		expect(settings.themePreference).toBe('Light');
		expect(settings.defaultBatchSize).toBe(40);
		expect(settings.defaultBatchSizeLiters).toBe(40);
		expect(settings.defaultEfficiency).toBe(80);
		expect(settings.defaultEfficiencyPercent).toBe(80);
		expect(settings.defaultBoilTime).toBe(90);
		expect(settings.defaultBoilTimeMinutes).toBe(90);

		// Format methods bound to active store settings
		expect(settings.formatVolume(20)).toBe('5.3 gal');
		expect(settings.formatTemperature(65)).toBe('149.0 °F');
		expect(settings.formatGravity(1.058)).toBe('14.3 °P');
		expect(settings.formatGrainWeight(5)).toBe('11.02 lb');
		expect(settings.formatHopWeight(50)).toBe('1.8 oz');
		expect(settings.sgToPlato(1.058)).toBeCloseTo(14.3, 1);
		expect(settings.platoToSg(14.3)).toBeCloseTo(1.058, 2);

		// Reset back to Metric for other tests
		await settings.setVolumeUnit('Liters');
		settings.setWeightUnit('Metric');
		settings.setTemperatureUnit('Celsius');
		settings.setGravityUnit('SpecificGravity');
		expect(settings.formatVolume(20)).toBe('20.0 L');
		expect(settings.formatTemperature(65)).toBe('65.0 °C');
		expect(settings.formatGravity(1.058)).toBe('1.058');
	});

	it('hydrates and manages MQTT connectivity configuration securely', async () => {
		expect(settings.hasMqttConfigured).toBe(false);
		expect(settings.hasMqttPassword).toBe(false);

		settings.hydrateFromPreferences({
			language: 'en',
			volumeUnit: 'Liters',
			weightUnit: 'Metric',
			temperatureUnit: 'Celsius',
			gravityUnit: 'SpecificGravity',
			theme: 'Dark',
			defaultBatchSizeLiters: 20,
			defaultEfficiencyPercent: 75,
			defaultBoilTimeMinutes: 60,
			mqttHost: 'broker.hivemq.com',
			mqttPort: 8883,
			mqttUsername: 'brewer_one',
			hasMqttPassword: true,
			mqttCertificate: '-----BEGIN CERTIFICATE-----\nMIIB...',
			mqttTopicPrefix: 'cellar/vessels'
		});

		expect(settings.hasMqttConfigured).toBe(true);
		expect(settings.hasMqttPassword).toBe(true);
		expect(settings.mqttHost).toBe('broker.hivemq.com');
		expect(settings.mqttPort).toBe(8883);
		expect(settings.mqttUsername).toBe('brewer_one');
		expect(settings.mqttCertificate).toContain('BEGIN CERTIFICATE');
		expect(settings.mqttTopicPrefix).toBe('cellar/vessels');

		// Security: Check localStorage does NOT retain plaintext password
		expect(localStorage.getItem('brewyou_mqtt_password')).toBeNull();

		// Clear MQTT
		settings.clearMqttSettings();
		expect(settings.hasMqttConfigured).toBe(false);
		expect(settings.mqttTopicPrefix).toBe('brewyou/equipment');
	});

	it('checks MQTT connectivity status accurately', async () => {
		auth.user = {
			id: 'test-user',
			email: 'test@example.com',
			displayName: 'Test Brewer',
			preferredLanguage: 'en',
			preferredVolumeUnit: 'Liters',
			createdAt: new Date().toISOString(),
			preferences: {
				language: 'en',
				volumeUnit: 'Liters',
				weightUnit: 'Metric',
				temperatureUnit: 'Celsius',
				gravityUnit: 'SpecificGravity',
				theme: 'Dark',
				defaultBatchSizeLiters: 20,
				defaultEfficiencyPercent: 75,
				defaultBoilTimeMinutes: 60
			}
		};

		settings.mqttHost = 'mqtt.brewyou.local';
		settings.mqttPort = 1883;

		// 1. Connected
		const mockStatus = vi.spyOn(api.auth, 'getMqttStatus').mockResolvedValue({
			configured: true,
			connected: true,
			host: 'mqtt.brewyou.local',
			port: 1883
		});

		const connectedRes = await settings.checkMqttStatus();
		expect(connectedRes).toBe(true);
		expect(settings.mqttConnected).toBe(true);

		// 2. Disconnected
		mockStatus.mockResolvedValueOnce({
			configured: true,
			connected: false,
			host: 'mqtt.brewyou.local',
			port: 1883
		});

		const disconnectedRes = await settings.checkMqttStatus();
		expect(disconnectedRes).toBe(false);
		expect(settings.mqttConnected).toBe(false);

		// 3. Network Failure
		mockStatus.mockRejectedValueOnce(new Error('Connection timed out'));
		const failureRes = await settings.checkMqttStatus();
		expect(failureRes).toBe(false);
		expect(settings.mqttConnected).toBe(false);

		// Cleanup
		auth.user = null;
		settings.mqttHost = '';
		settings.mqttConnected = null;
	});
});

describe('Color Unit Conversion Helpers', () => {
	it('converts SRM to EBC accurately (1 SRM ≈ 1.97 EBC)', () => {
		expect(srmToEbc(0)).toBe(0);
		expect(srmToEbc(5.4)).toBeCloseTo(10.6, 1);
		expect(srmToEbc(10)).toBeCloseTo(19.7, 1);
		expect(srmToEbc(NaN)).toBe(0);
		expect(srmToEbc(-5)).toBe(0);
	});

	it('converts EBC to SRM accurately (1 EBC ≈ 0.5076 SRM)', () => {
		expect(ebcToSrm(0)).toBe(0);
		expect(ebcToSrm(10.6)).toBeCloseTo(5.4, 1);
		expect(ebcToSrm(19.7)).toBeCloseTo(10.0, 1);
		expect(ebcToSrm(NaN)).toBe(0);
		expect(ebcToSrm(-2)).toBe(0);
	});

	it('formats color according to active unit', () => {
		expect(formatColor(null, 'SRM')).toBe('-');
		expect(formatColor(undefined, 'EBC')).toBe('-');
		expect(formatColor(5.4, 'SRM')).toBe('5.4 SRM');
		expect(formatColor(5.4, 'EBC')).toBe('10.6 EBC');
	});

	it('converts to/from display color correctly', () => {
		expect(toDisplayColor(10, 'SRM')).toBe(10);
		expect(toDisplayColor(10, 'EBC')).toBe(19.7);
		expect(fromDisplayColor(19.7, 'EBC')).toBe(10);
		expect(fromDisplayColor(10, 'SRM')).toBe(10);
	});
});

describe('Carbonation Unit Conversion Helpers', () => {
	it('converts volumes CO2 to g/L accurately (1 vol ≈ 1.96 g/L)', () => {
		expect(volumesToGramsPerLiter(0)).toBe(0);
		expect(volumesToGramsPerLiter(2.5)).toBeCloseTo(4.9, 1);
		expect(volumesToGramsPerLiter(NaN)).toBe(0);
	});

	it('converts g/L to volumes CO2 accurately', () => {
		expect(gramsPerLiterToVolumes(0)).toBe(0);
		expect(gramsPerLiterToVolumes(4.9)).toBeCloseTo(2.5, 1);
		expect(gramsPerLiterToVolumes(NaN)).toBe(0);
	});

	it('formats carbonation with active unit', () => {
		expect(formatCarbonation(null, 'Volumes')).toBe('-');
		expect(formatCarbonation(undefined, 'GramsPerLiter')).toBe('-');
		expect(formatCarbonation(2.45, 'Volumes')).toBe('2.45 vol');
		expect(formatCarbonation(2.5, 'GramsPerLiter')).toBe('4.90 g/L');
	});
});

describe('Decoupled Weights & Regional Presets in Settings Store', () => {
	it('formats decoupled grain and hop weights independently', () => {
		expect(formatGrainWeight(4.5, 'kg')).toBe('4.50 kg');
		expect(formatGrainWeight(4.5, 'lb')).toBe('9.92 lb');
		expect(formatHopWeight(28.35, 'g')).toBe('28.4 g');
		expect(formatHopWeight(28.35, 'oz')).toBe('1.0 oz');
	});

	it('applies European Metric preset correctly', () => {
		settings.applyPreset('European Metric');
		expect(settings.volumeUnit).toBe('Liters');
		expect(settings.grainWeightUnit).toBe('kg');
		expect(settings.hopWeightUnit).toBe('g');
		expect(settings.colorUnit).toBe('EBC');
		expect(settings.temperatureUnit).toBe('Celsius');
		expect(settings.gravityUnit).toBe('SpecificGravity');
		expect(settings.carbonationUnit).toBe('Volumes');
		expect(settings.bitternessFormula).toBe('Tinseth');
		expect(settings.colorFormula).toBe('Morey');
		expect(settings.abvFormula).toBe('Linear');
	});

	it('applies US Craft preset correctly', () => {
		settings.applyPreset('US Craft');
		expect(settings.volumeUnit).toBe('Gallons');
		expect(settings.grainWeightUnit).toBe('lb');
		expect(settings.hopWeightUnit).toBe('oz');
		expect(settings.colorUnit).toBe('SRM');
		expect(settings.temperatureUnit).toBe('Fahrenheit');
		expect(settings.gravityUnit).toBe('SpecificGravity');
		expect(settings.carbonationUnit).toBe('Volumes');
	});

	it('applies UK Traditional preset correctly', () => {
		settings.applyPreset('UK Traditional');
		expect(settings.volumeUnit).toBe('Liters');
		expect(settings.grainWeightUnit).toBe('kg');
		expect(settings.hopWeightUnit).toBe('oz');
		expect(settings.colorUnit).toBe('EBC');
		expect(settings.temperatureUnit).toBe('Celsius');
		expect(settings.gravityUnit).toBe('SpecificGravity');
		expect(settings.carbonationUnit).toBe('Volumes');
	});

	it('allows decoupled customization such as Liters with Ounces', () => {
		settings.applyPreset('European Metric');
		settings.setHopWeightUnit('oz');
		expect(settings.volumeUnit).toBe('Liters');
		expect(settings.temperatureUnit).toBe('Celsius');
		expect(settings.grainWeightUnit).toBe('kg');
		expect(settings.hopWeightUnit).toBe('oz');
		expect(settings.hopShort).toBe('oz');
		expect(settings.grainShort).toBe('kg');
	});

	it('initializes from localStorage and clears MQTT settings cleanly', () => {
		mockStorage.setItem('brewyou_volume_unit', 'Gallons');
		mockStorage.setItem('brewyou_weight_unit', 'Imperial');
		mockStorage.setItem('brewyou_mqtt_host', 'broker.local');

		settings.init();
		expect(settings.volumeUnit).toBe('Gallons');

		settings.clearMqttSettings();
		expect(settings.mqttHost).toBe('');
		expect(mockStorage.getItem('brewyou_mqtt_host')).toBeNull();
	});
});
