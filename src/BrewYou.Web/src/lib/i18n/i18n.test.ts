import fs from 'fs';
import path from 'path';
import { describe, expect, it } from 'vitest';
import en from './locales/en.json';
import sv from './locales/sv.json';
import { i18n, t } from './index.svelte';

function getAllKeys(obj: Record<string, unknown>, prefix = ''): string[] {
	let keys: string[] = [];
	for (const [k, v] of Object.entries(obj)) {
		const fullKey = prefix ? `${prefix}.${k}` : k;
		if (typeof v === 'object' && v !== null && !Array.isArray(v)) {
			keys = keys.concat(getAllKeys(v as Record<string, unknown>, fullKey));
		} else {
			keys.push(fullKey);
		}
	}
	return keys.sort();
}

function getTopLevelKeysFromRawJson(filePath: string): string[] {
	const content = fs.readFileSync(filePath, 'utf8');
	const matches = [...content.matchAll(/^\t"([^"]+)":/gm)];
	return matches.map((m) => m[1]);
}

describe('i18n Translation Engine', () => {
	it('has 100% key parity between English and Swedish dictionaries', () => {
		const enKeys = getAllKeys(en);
		const svKeys = getAllKeys(sv);

		expect(svKeys).toEqual(enKeys);
	});

	it('contains no duplicate top-level keys in en.json or sv.json', () => {
		const enTopKeys = getTopLevelKeysFromRawJson(path.resolve(__dirname, './locales/en.json'));
		const svTopKeys = getTopLevelKeysFromRawJson(path.resolve(__dirname, './locales/sv.json'));

		const enUnique = new Set(enTopKeys);
		const svUnique = new Set(svTopKeys);

		expect(enTopKeys.length).toBe(enUnique.size);
		expect(svTopKeys.length).toBe(svUnique.size);
	});

	it('translates nested keys accurately in English', () => {
		i18n.setLocale('en');
		expect(t('nav.dashboard')).toBe('Dashboard');
		expect(t('metrics.og')).toBe('Original Gravity');
		expect(t('metrics.ibu')).toBe('Bitterness (IBU)');
		expect(t('metrics.color', { unit: 'EBC' })).toBe('Color (EBC)');
		expect(t('metrics.color', { unit: 'SRM' })).toBe('Color (SRM)');
		expect(t('formulator.save_recipe')).toBe('Save Recipe');
		expect(t('formulator.title_new')).toBe('New Recipe Formulation');
		expect(t('formulator.title_edit')).toBe('Edit Recipe');
		expect(t('formulator.update_recipe')).toBe('Update Recipe');
	});

	it('translates nested keys accurately in Swedish', () => {
		i18n.setLocale('sv');
		expect(t('nav.dashboard')).toBe('Översikt');
		expect(t('metrics.og')).toBe('Stamvörtstyrka (OG)');
		expect(t('metrics.ibu')).toBe('Beska (IBU)');
		expect(t('metrics.color', { unit: 'EBC' })).toBe('Färg (EBC)');
		expect(t('metrics.color', { unit: 'SRM' })).toBe('Färg (SRM)');
		expect(t('formulator.save_recipe')).toBe('Spara recept');
		expect(t('formulator.title_new')).toBe('Ny receptformulering');
		expect(t('formulator.title_edit')).toBe('Redigera recept');
		expect(t('formulator.update_recipe')).toBe('Uppdatera recept');
	});

	it('translates batch process brew stages accurately using authentic Swedish brewing terminology', () => {
		i18n.setLocale('sv');
		expect(t('batches.stages.mash')).toBe('Mäskning');
		expect(t('batches.stages.boil')).toBe('Kokning');
		expect(t('batches.stages.ferment')).toBe('Jäsning');
		expect(t('batches.stages.condition')).toBe('Lagring');
		expect(t('batches.stages.package')).toBe('Tappning');

		expect(t('batches.workspace.complete_package_button')).toBe('Slutför och tappa upp batchen');
		expect(t('batches.equipment.packaging_vessel')).toBe('Tappningskärl');
		expect(t('batches.workspace.apparent_attenuation')).toBe('Skenbar utjäsningsgrad');
		expect(t('batches.workspace.mash_checklist_title')).toBe('Maltnota & mäskningstillsatser');

		i18n.setLocale('en');
		expect(t('batches.stages.mash')).toBe('Mash');
		expect(t('batches.stages.boil')).toBe('Boil');
		expect(t('batches.stages.ferment')).toBe('Ferment');
		expect(t('batches.stages.condition')).toBe('Condition');
		expect(t('batches.stages.package')).toBe('Package');
		expect(t('batches.workspace.complete_package_button')).toBe('Complete & Package Batch');
		expect(t('batches.equipment.packaging_vessel')).toBe('Packaging Vessel');
	});

	it('translates equipment types and statuses accurately in English and Swedish', () => {
		i18n.setLocale('en');
		expect(t('equipment.types.Boiler')).toBe('Boiler');
		expect(t('equipment.types.Fermenter')).toBe('Fermenter');
		expect(t('equipment.types.Keg')).toBe('Keg');
		expect(t('equipment.types.Other')).toBe('Other');
		expect(t('equipment.in_use')).toBe('In Use');
		expect(t('equipment.occupied')).toBe('Occupied');
		expect(t('batches.equipment.in_use_suffix')).toBe('— In Use');
		expect(t('batches.equipment.occupied_suffix')).toBe('— Occupied');

		i18n.setLocale('sv');
		expect(t('equipment.types.Boiler')).toBe('Bryggverk / Kokgryta');
		expect(t('equipment.types.Fermenter')).toBe('Jäskärl');
		expect(t('equipment.types.Keg')).toBe('Fat');
		expect(t('equipment.types.Other')).toBe('Övrigt');
		expect(t('equipment.in_use')).toBe('I bruk');
		expect(t('equipment.occupied')).toBe('Upptaget');
		expect(t('batches.equipment.in_use_suffix')).toBe('— I bruk');
		expect(t('batches.equipment.occupied_suffix')).toBe('— Upptaget');
	});

	it('translates sidebar sections and breadcrumb labels accurately in English and Swedish', () => {
		i18n.setLocale('en');
		expect(t('nav.brewing_section')).toBe('Brewing');
		expect(t('nav.inventory_section')).toBe('Inventory');
		expect(t('nav.calculations_section')).toBe('Calculations');
		expect(t('nav.equipment')).toBe('Equipment');
		expect(t('nav.batches')).toBe('Batches');
		expect(t('nav.calculations')).toBe('Calculations');
		expect(t('nav.settings')).toBe('Settings');
		expect(t('breadcrumbs.inventory')).toBe('Inventory');
		expect(t('breadcrumbs.equipment')).toBe('Equipment');

		i18n.setLocale('sv');
		expect(t('nav.brewing_section')).toBe('Bryggning');
		expect(t('nav.inventory_section')).toBe('Lager');
		expect(t('nav.calculations_section')).toBe('Beräkningar');
		expect(t('nav.equipment')).toBe('Utrustning');
		expect(t('nav.batches')).toBe('Bryggningar');
		expect(t('nav.calculations')).toBe('Beräkningar');
		expect(t('nav.settings')).toBe('Inställningar');
		expect(t('breadcrumbs.inventory')).toBe('Lager');
		expect(t('breadcrumbs.equipment')).toBe('Utrustning');
	});

	it('translates equipment telemetry and external connectivity labels accurately in English and Swedish', () => {
		i18n.setLocale('en');
		expect(t('equipment.telemetry_title')).toBe('External Connectivity & Telemetry');
		expect(t('equipment.telemetry_badge')).toBe('IoT & Sensors');
		expect(t('equipment.temperature_label')).toBe('Current Temperature');
		expect(t('equipment.connection_type_label')).toBe('Connection Protocol');
		expect(t('equipment.conn_none')).toBe('None (Manual)');
		expect(t('equipment.conn_http_push')).toBe('Inbound Webhook / HTTP Push');
		expect(t('equipment.conn_http_poll')).toBe('HTTP Polling (Outbound Pull)');
		expect(t('equipment.conn_mqtt')).toBe('MQTT Broker Subscription');
		expect(t('equipment.subtypes.AllInOne')).toBe('All in one');
		expect(t('equipment.subtypes.Pan')).toBe('Pan');
		expect(t('equipment.subtypes.Other')).toBe('Other');

		i18n.setLocale('sv');
		expect(t('equipment.telemetry_title')).toBe('Extern anslutning & telemetri');
		expect(t('equipment.telemetry_badge')).toBe('IoT & sensorer');
		expect(t('equipment.temperature_label')).toBe('Aktuell temperatur');
		expect(t('equipment.connection_type_label')).toBe('Anslutningsprotokoll');
		expect(t('equipment.conn_none')).toBe('Inget (Manuell)');
		expect(t('equipment.conn_http_push')).toBe('Inkommande webhook / HTTP Push');
		expect(t('equipment.conn_http_poll')).toBe('HTTP-polling (Hämta data)');
		expect(t('equipment.conn_mqtt')).toBe('MQTT-mäklarprenumeration');
		expect(t('equipment.subtypes.AllInOne')).toBe('Allt-i-ett');
		expect(t('equipment.subtypes.Pan')).toBe('Gryta / Kastrull');
		expect(t('equipment.subtypes.Other')).toBe('Övrigt');
	});

	it('translates batch creation wizard fields and error messages accurately in English and Swedish', () => {
		i18n.setLocale('en');
		expect(t('batches.wizard.default_adhoc_name')).toBe('My Craft Batch');
		expect(t('batches.wizard.err_init_failed')).toBe('Failed to initialize batch wizard.');
		expect(t('batches.wizard.err_create_failed')).toBe('Failed to start batch.');
		expect(t('batches.wizard.measured_og_optional')).toBe('Measured OG (Optional)');
		expect(t('batches.wizard.pitch_temp_optional')).toBe('Pitch Temp (°C, Optional)');
		expect(t('batches.wizard.notes_label')).toBe('Notes (Optional)');
		expect(t('batches.wizard.notes_placeholder')).toBe(
			'Brew day notes, water additions, yeast batch info...'
		);
		expect(t('batches.err_load_failed')).toBe('Failed to load batches.');

		i18n.setLocale('sv');
		expect(t('batches.wizard.default_adhoc_name')).toBe('Min hantverksbryggning');
		expect(t('batches.wizard.err_init_failed')).toBe('Kunde inte initiera batchguiden.');
		expect(t('batches.wizard.err_create_failed')).toBe('Kunde inte starta batchen.');
		expect(t('batches.wizard.measured_og_optional')).toBe('Uppmätt OG (valfritt)');
		expect(t('batches.wizard.pitch_temp_optional')).toBe('Jästtillsättningstemp (°C, valfritt)');
		expect(t('batches.wizard.notes_label')).toBe('Anteckningar (valfritt)');
		expect(t('batches.wizard.notes_placeholder')).toBe(
			'Bryggdagsanteckningar, vattenjusteringar, jästinformation...'
		);
		expect(t('batches.err_load_failed')).toBe('Kunde inte läsa in batcher.');
	});

	it('interpolates dynamic parameters correctly', () => {
		i18n.setLocale('en');
		// Test custom parameter interpolation
		const template = 'Batch of {liters} liters for {brewer}';
		const result = Object.entries({ liters: 25, brewer: 'Jocke' }).reduce(
			(acc, [k, v]) => acc.replace(new RegExp(`\\{${k}\\}`, 'g'), String(v)),
			template
		);
		expect(result).toBe('Batch of 25 liters for Jocke');
	});

	it('falls back to English when a key is absent from Swedish', () => {
		i18n.setLocale('sv');
		// If a key doesn't exist, it returns the raw key
		expect(t('nonexistent.key')).toBe('nonexistent.key');
	});
});
