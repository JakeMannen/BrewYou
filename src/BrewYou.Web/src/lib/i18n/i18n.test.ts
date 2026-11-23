import fs from 'fs';
import path from 'path';
import { describe, expect, it } from 'vitest';
import de from './locales/de.json';
import en from './locales/en.json';
import es from './locales/es.json';
import fr from './locales/fr.json';
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

function getLeafEntries(obj: Record<string, unknown>, prefix = ''): Record<string, string> {
	const entries: Record<string, string> = {};
	for (const [k, v] of Object.entries(obj)) {
		const fullKey = prefix ? `${prefix}.${k}` : k;
		if (typeof v === 'object' && v !== null && !Array.isArray(v)) {
			Object.assign(entries, getLeafEntries(v as Record<string, unknown>, fullKey));
		} else {
			entries[fullKey] = String(v);
		}
	}
	return entries;
}

function extractPlaceholders(value: string): string[] {
	return [...value.matchAll(/\{([^}]+)\}/g)].map((m) => m[1]).sort();
}

describe('i18n Translation Engine', () => {
	it('has 100% key parity across English, Swedish, German, French, and Spanish dictionaries', () => {
		const enKeys = getAllKeys(en);
		const svKeys = getAllKeys(sv);
		const deKeys = getAllKeys(de);
		const frKeys = getAllKeys(fr);
		const esKeys = getAllKeys(es);

		expect(svKeys).toEqual(enKeys);
		expect(deKeys).toEqual(enKeys);
		expect(frKeys).toEqual(enKeys);
		expect(esKeys).toEqual(enKeys);
	});

	it('has 100% {param} placeholder parity across English, Swedish, German, French, and Spanish dictionaries', () => {
		const enEntries = getLeafEntries(en);
		const otherLocales = [
			['sv', getLeafEntries(sv)],
			['de', getLeafEntries(de)],
			['fr', getLeafEntries(fr)],
			['es', getLeafEntries(es)]
		] as const;

		const mismatches: string[] = [];
		for (const [key, enVal] of Object.entries(enEntries)) {
			const expectedParams = extractPlaceholders(enVal);
			for (const [loc, entries] of otherLocales) {
				const actualParams = extractPlaceholders(entries[key] ?? '');
				if (JSON.stringify(actualParams) !== JSON.stringify(expectedParams)) {
					mismatches.push(
						`${loc}.${key}: expected [${expectedParams.join(', ')}] got [${actualParams.join(', ')}]`
					);
				}
			}
		}

		expect(mismatches).toEqual([]);
	});

	it('contains no duplicate top-level keys in any locale file', () => {
		const locales = ['en', 'sv', 'de', 'fr', 'es'];
		for (const loc of locales) {
			const topKeys = getTopLevelKeysFromRawJson(path.resolve(__dirname, `./locales/${loc}.json`));
			const unique = new Set(topKeys);
			expect(topKeys.length).toBe(unique.size);
			expect(topKeys.length).toBe(21);
		}
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
		expect(t('dashboard.equipment_health')).toBe('Utrustning & Sensorer');
	});

	it('translates nested keys accurately in German', () => {
		i18n.setLocale('de');
		expect(t('nav.dashboard')).toBe('Übersicht');
		expect(t('metrics.og')).toBe('Stammwürze (OG)');
		expect(t('metrics.ibu')).toBe('Bittere (IBU)');
		expect(t('metrics.color', { unit: 'EBC' })).toBe('Farbe (EBC)');
		expect(t('metrics.color', { unit: 'SRM' })).toBe('Farbe (SRM)');
		expect(t('formulator.save_recipe')).toBe('Rezept speichern');
		expect(t('formulator.title_new')).toBe('Neue Rezeptformulierung');
		expect(t('formulator.title_edit')).toBe('Rezept bearbeiten');
		expect(t('formulator.update_recipe')).toBe('Rezept aktualisieren');
		expect(t('dashboard.equipment_health')).toBe('Ausrüstung & Sensoren');
	});

	it('translates nested keys accurately in French', () => {
		i18n.setLocale('fr');
		expect(t('nav.dashboard')).toBe('Tableau de bord');
		expect(t('metrics.og')).toBe('Densité initiale (OG)');
		expect(t('metrics.ibu')).toBe('Amertume (IBU)');
		expect(t('metrics.color', { unit: 'EBC' })).toBe('Couleur (EBC)');
		expect(t('metrics.color', { unit: 'SRM' })).toBe('Couleur (SRM)');
		expect(t('formulator.save_recipe')).toBe('Enregistrer la recette');
		expect(t('formulator.title_new')).toBe('Nouvelle formulation de recette');
		expect(t('formulator.title_edit')).toBe('Modifier la recette');
		expect(t('formulator.update_recipe')).toBe('Mettre à jour la recette');
		expect(t('dashboard.equipment_health')).toBe('Équipement & Capteurs');
	});

	it('translates nested keys accurately in Spanish', () => {
		i18n.setLocale('es');
		expect(t('nav.dashboard')).toBe('Panel');
		expect(t('metrics.og')).toBe('Densidad inicial (OG)');
		expect(t('metrics.ibu')).toBe('Amargor (IBU)');
		expect(t('metrics.color', { unit: 'EBC' })).toBe('Color (EBC)');
		expect(t('metrics.color', { unit: 'SRM' })).toBe('Color (SRM)');
		expect(t('formulator.save_recipe')).toBe('Guardar receta');
		expect(t('formulator.title_new')).toBe('Nueva formulación de receta');
		expect(t('formulator.title_edit')).toBe('Editar receta');
		expect(t('formulator.update_recipe')).toBe('Actualizar receta');
		expect(t('dashboard.equipment_health')).toBe('Equipamiento y sensores');
	});

	it('translates batch process brew stages accurately across authentic brewing terminology', () => {
		// Swedish
		i18n.setLocale('sv');
		expect(t('batches.stages.mash')).toBe('Mäskning');
		expect(t('batches.stages.boil')).toBe('Kokning');
		expect(t('batches.stages.ferment')).toBe('Jäsning');
		expect(t('batches.stages.condition')).toBe('Lagring');
		expect(t('batches.stages.package')).toBe('Tappning');
		expect(t('batches.workspace.complete_package_button')).toBe('Slutför och tappa upp batchen');
		expect(t('batches.equipment.packaging_vessel')).toBe('Tappningskärl');

		// German
		i18n.setLocale('de');
		expect(t('batches.stages.mash')).toBe('Maischen');
		expect(t('batches.stages.boil')).toBe('Kochen');
		expect(t('batches.stages.ferment')).toBe('Gärung');
		expect(t('batches.stages.condition')).toBe('Reifung');
		expect(t('batches.stages.package')).toBe('Abfüllung');
		expect(t('batches.workspace.complete_package_button')).toBe('Sud abschließen & abfüllen');
		expect(t('batches.equipment.packaging_vessel')).toBe('Abfüllgefäß');

		// French
		i18n.setLocale('fr');
		expect(t('batches.stages.mash')).toBe('Empâtage');
		expect(t('batches.stages.boil')).toBe('Ébullition');
		expect(t('batches.stages.ferment')).toBe('Fermentation');
		expect(t('batches.stages.condition')).toBe('Garde');
		expect(t('batches.stages.package')).toBe('Conditionnement');
		expect(t('batches.workspace.complete_package_button')).toBe(
			'Terminer et conditionner le brassin'
		);
		expect(t('batches.equipment.packaging_vessel')).toBe('Cuve de conditionnement');

		// Spanish
		i18n.setLocale('es');
		expect(t('batches.stages.mash')).toBe('Maceración');
		expect(t('batches.stages.boil')).toBe('Hervor');
		expect(t('batches.stages.ferment')).toBe('Fermentación');
		expect(t('batches.stages.condition')).toBe('Maduración');
		expect(t('batches.stages.package')).toBe('Envasado');
		expect(t('batches.workspace.complete_package_button')).toBe('Completar y envasar el lote');
		expect(t('batches.equipment.packaging_vessel')).toBe('Recipiente de envasado');

		// English
		i18n.setLocale('en');
		expect(t('batches.stages.mash')).toBe('Mash');
		expect(t('batches.stages.boil')).toBe('Boil');
		expect(t('batches.stages.ferment')).toBe('Ferment');
		expect(t('batches.stages.condition')).toBe('Condition');
		expect(t('batches.stages.package')).toBe('Package');
		expect(t('batches.workspace.complete_package_button')).toBe('Complete & Package Batch');
		expect(t('batches.equipment.packaging_vessel')).toBe('Packaging Vessel');
	});

	it('translates equipment types and statuses accurately across all locales', () => {
		i18n.setLocale('en');
		expect(t('equipment.types.Boiler')).toBe('Boiler');
		expect(t('equipment.types.Fermenter')).toBe('Fermenter');
		expect(t('equipment.types.Keg')).toBe('Keg');
		expect(t('equipment.types.Other')).toBe('Other');
		expect(t('equipment.in_use')).toBe('In Use');
		expect(t('equipment.occupied')).toBe('Occupied');

		i18n.setLocale('sv');
		expect(t('equipment.types.Boiler')).toBe('Bryggverk');
		expect(t('equipment.types.Fermenter')).toBe('Jäskärl');
		expect(t('equipment.types.Keg')).toBe('Fat');
		expect(t('equipment.types.Other')).toBe('Övrigt');
		expect(t('equipment.in_use')).toBe('I bruk');
		expect(t('equipment.occupied')).toBe('Upptaget');

		i18n.setLocale('de');
		expect(t('equipment.types.Boiler')).toBe('Sudwerk');
		expect(t('equipment.types.Fermenter')).toBe('Gärbehälter');
		expect(t('equipment.types.Keg')).toBe('Fass');
		expect(t('equipment.types.Other')).toBe('Sonstiges');
		expect(t('equipment.in_use')).toBe('In Betrieb');
		expect(t('equipment.occupied')).toBe('Belegt');

		i18n.setLocale('fr');
		expect(t('equipment.types.Boiler')).toBe('Cuve de brassage');
		expect(t('equipment.types.Fermenter')).toBe('Fermenteur');
		expect(t('equipment.types.Keg')).toBe('Fût');
		expect(t('equipment.types.Other')).toBe('Autre');
		expect(t('equipment.in_use')).toBe('En service');
		expect(t('equipment.occupied')).toBe('Occupé');

		i18n.setLocale('es');
		expect(t('equipment.types.Boiler')).toBe('Olla de cocción');
		expect(t('equipment.types.Fermenter')).toBe('Fermentador');
		expect(t('equipment.types.Keg')).toBe('Barril');
		expect(t('equipment.types.Other')).toBe('Otro');
		expect(t('equipment.in_use')).toBe('En uso');
		expect(t('equipment.occupied')).toBe('Ocupado');
	});

	it('keeps theme names in English across all locales', () => {
		const locales = ['en', 'sv', 'de', 'fr', 'es'] as const;
		for (const loc of locales) {
			i18n.setLocale(loc);
			expect(t('settings.appearance.theme_imperial_stout')).toBe('Imperial Stout');
			expect(t('settings.appearance.theme_chocolate_porter')).toBe('Chocolate Porter');
			expect(t('settings.appearance.theme_obsidian')).toBe('Obsidian Ember');
			expect(t('settings.appearance.theme_dark')).toBe('Modern Amber');
			expect(t('settings.appearance.theme_light')).toBe('Pilsner Clean');
		}
	});

	it('translates sidebar sections and breadcrumb labels accurately across all locales', () => {
		i18n.setLocale('en');
		expect(t('nav.brewing_section')).toBe('Brewing');
		expect(t('nav.inventory_section')).toBe('Inventory');
		expect(t('nav.calculations_section')).toBe('Calculations');
		expect(t('nav.equipment')).toBe('Equipment');
		expect(t('nav.batches')).toBe('Batches');

		i18n.setLocale('sv');
		expect(t('nav.brewing_section')).toBe('Bryggning');
		expect(t('nav.inventory_section')).toBe('Lager');
		expect(t('nav.calculations_section')).toBe('Beräkningar');
		expect(t('nav.equipment')).toBe('Utrustning');
		expect(t('nav.batches')).toBe('Bryggningar');

		i18n.setLocale('de');
		expect(t('nav.brewing_section')).toBe('Brauen');
		expect(t('nav.inventory_section')).toBe('Lagerbestand');
		expect(t('nav.calculations_section')).toBe('Berechnungen');
		expect(t('nav.equipment')).toBe('Ausrüstung');
		expect(t('nav.batches')).toBe('Sudvorgänge');

		i18n.setLocale('fr');
		expect(t('nav.brewing_section')).toBe('Brassage');
		expect(t('nav.inventory_section')).toBe('Inventaire');
		expect(t('nav.calculations_section')).toBe('Calculs');
		expect(t('nav.equipment')).toBe('Équipement');
		expect(t('nav.batches')).toBe('Brassins');

		i18n.setLocale('es');
		expect(t('nav.brewing_section')).toBe('Elaboración');
		expect(t('nav.inventory_section')).toBe('Inventario');
		expect(t('nav.calculations_section')).toBe('Cálculos');
		expect(t('nav.equipment')).toBe('Equipamiento');
		expect(t('nav.batches')).toBe('Lotes');
	});

	it('interpolates dynamic parameters correctly across languages', () => {
		i18n.setLocale('en');
		expect(t('batches.equipment.capacity', { liters: 30 })).toBe('Capacity: 30L');

		i18n.setLocale('sv');
		expect(t('batches.equipment.capacity', { liters: 30 })).toBe('Kapacitet: 30 l');

		i18n.setLocale('de');
		expect(t('batches.equipment.capacity', { liters: 30 })).toBe('Kapazität: 30 L');

		i18n.setLocale('fr');
		expect(t('batches.equipment.capacity', { liters: 30 })).toBe('Capacité : 30 L');

		i18n.setLocale('es');
		expect(t('batches.equipment.capacity', { liters: 30 })).toBe('Capacidad: 30 L');
	});

	it('translates brewery collaboration and membership roles accurately across all locales', () => {
		i18n.setLocale('en');
		expect(t('brewery.collaboration')).toBe('Collaboration & Members');
		expect(t('brewery.role_owner')).toBe('Co-Owner');
		expect(t('brewery.role_brewer')).toBe('Brewer');
		expect(t('brewery.role_viewer')).toBe('Viewer');
		expect(t('brewery.accept_invite_subtitle', { name: 'Acme', role: 'Brewer' })).toBe(
			'You have been invited to join "Acme" as a Brewer.'
		);

		i18n.setLocale('sv');
		expect(t('brewery.collaboration')).toBe('Samarbete & medlemmar');
		expect(t('brewery.role_owner')).toBe('Medägare');
		expect(t('brewery.role_brewer')).toBe('Bryggare');
		expect(t('brewery.role_viewer')).toBe('Åskådare');
		expect(t('brewery.accept_invite_subtitle', { name: 'Acme', role: 'Bryggare' })).toBe(
			'Du har bjudits in att gå med i "Acme" som Bryggare.'
		);

		i18n.setLocale('de');
		expect(t('brewery.collaboration')).toBe('Zusammenarbeit & Mitglieder');
		expect(t('brewery.role_owner')).toBe('Miteigentümer');
		expect(t('brewery.role_brewer')).toBe('Brauer');
		expect(t('brewery.role_viewer')).toBe('Beobachter');
		expect(t('brewery.accept_invite_subtitle', { name: 'Acme', role: 'Brauer' })).toBe(
			'Sie wurden eingeladen, „Acme“ als Brauer beizutreten.'
		);

		i18n.setLocale('fr');
		expect(t('brewery.collaboration')).toBe('Collaboration & Membres');
		expect(t('brewery.role_owner')).toBe('Co-propriétaire');
		expect(t('brewery.role_brewer')).toBe('Brasseur');
		expect(t('brewery.role_viewer')).toBe('Observateur');
		expect(t('brewery.accept_invite_subtitle', { name: 'Acme', role: 'Brasseur' })).toBe(
			'Vous avez été invité à rejoindre « Acme » en tant que Brasseur.'
		);

		i18n.setLocale('es');
		expect(t('brewery.collaboration')).toBe('Colaboración y miembros');
		expect(t('brewery.role_owner')).toBe('Copropietario');
		expect(t('brewery.role_brewer')).toBe('Cervecero');
		expect(t('brewery.role_viewer')).toBe('Observador');
		expect(t('brewery.accept_invite_subtitle', { name: 'Acme', role: 'Cervecero' })).toBe(
			'Has sido invitado a unirte a "Acme" como Cervecero.'
		);
	});

	it('translates common action and generic feedback keys across all locales', () => {
		i18n.setLocale('en');
		expect(t('common.copy')).toBe('Copy');
		expect(t('common.copied')).toBe('Copied!');
		expect(t('common.back')).toBe('Back');
		expect(t('common.uploading')).toBe('Uploading...');
		expect(t('common.error_generic')).toBe('An unexpected error occurred. Please try again.');
		expect(t('settings.language.select_label')).toBe('Select language');

		i18n.setLocale('sv');
		expect(t('common.copy')).toBe('Kopiera');
		expect(t('common.copied')).toBe('Kopierad!');
		expect(t('common.back')).toBe('Tillbaka');
		expect(t('common.uploading')).toBe('Laddar upp...');
		expect(t('common.error_generic')).toBe('Ett oväntat fel uppstod. Försök igen.');
		expect(t('settings.language.select_label')).toBe('Välj språk');

		i18n.setLocale('de');
		expect(t('common.copy')).toBe('Kopieren');
		expect(t('common.copied')).toBe('Kopiert!');
		expect(t('common.back')).toBe('Zurück');
		expect(t('common.uploading')).toBe('Wird hochgeladen...');
		expect(t('common.error_generic')).toBe(
			'Ein unerwarteter Fehler ist aufgetreten. Bitte versuchen Sie es erneut.'
		);
		expect(t('settings.language.select_label')).toBe('Sprache auswählen');

		i18n.setLocale('fr');
		expect(t('common.copy')).toBe('Copier');
		expect(t('common.copied')).toBe('Copié !');
		expect(t('common.back')).toBe('Retour');
		expect(t('common.uploading')).toBe('Téléversement...');
		expect(t('common.error_generic')).toBe(
			'Une erreur inattendue est survenue. Veuillez réessayer.'
		);
		expect(t('settings.language.select_label')).toBe('Sélectionner la langue');

		i18n.setLocale('es');
		expect(t('common.copy')).toBe('Copiar');
		expect(t('common.copied')).toBe('¡Copiado!');
		expect(t('common.back')).toBe('Volver');
		expect(t('common.uploading')).toBe('Subiendo...');
		expect(t('common.error_generic')).toBe(
			'Ocurrió un error inesperado. Por favor, inténtelo de nuevo.'
		);
		expect(t('settings.language.select_label')).toBe('Seleccionar idioma');
	});

	it('ensures all static t(...) keys used in source code exist in translation dictionary', () => {
		const enKeys = new Set(getAllKeys(en));
		const srcDir = path.resolve(__dirname, '../../');

		function scanDir(dir: string): string[] {
			let files: string[] = [];
			for (const item of fs.readdirSync(dir)) {
				const full = path.join(dir, item);
				if (fs.statSync(full).isDirectory()) {
					files = files.concat(scanDir(full));
				} else if (
					full.endsWith('.svelte') ||
					(full.endsWith('.ts') && !full.endsWith('.d.ts') && !full.endsWith('.test.ts'))
				) {
					files.push(full);
				}
			}
			return files;
		}

		const files = scanDir(srcDir);
		const missing: { key: string; file: string }[] = [];
		const tRegex = /\bt\(\s*['"]([a-zA-Z0-9_.]+)['"]/g;

		for (const file of files) {
			const content = fs.readFileSync(file, 'utf8');
			let match;
			while ((match = tRegex.exec(content)) !== null) {
				const key = match[1];
				if (!enKeys.has(key)) {
					missing.push({ key, file: path.relative(srcDir, file) });
				}
			}
		}

		expect(missing).toEqual([]);
	});

	it('TC-T1-R1-01: distinguishes Swedish measured_og and target_og without collision', () => {
		// Swedish translation parity check for measured vs target OG
		i18n.setLocale('sv');
		const svMeasured = t('batches.measured_og');
		const svTarget = t('batches.telemetry.target_og');

		expect(svMeasured).toBe('Uppmätt OG');
		expect(svTarget).toBe('Mål-OG');
		expect(svMeasured).not.toEqual(svTarget);

		// English translation distinction check
		i18n.setLocale('en');
		const enMeasured = t('batches.measured_og');
		const enTarget = t('batches.telemetry.target_og');

		expect(enMeasured).toBe('Measured OG');
		expect(enTarget).toBe('Target OG');
		expect(enMeasured).not.toEqual(enTarget);
	});

	it('TC-T1-R1-02: dynamically interpolates temperature unit tokens ({unit}) across all 5 locales and avoids hardcoded °C', () => {
		const unitCases = [
			{
				locale: 'en',
				pitch: (u: string) => `Yeast Pitch Temp (${u})`,
				reading: (u: string) => `Vessel Temperature (${u})`,
				telemetry: (u: string) => `Temperature (${u})`,
				wizardPitch: (u: string) => `Pitch Temp (${u}, Optional)`
			},
			{
				locale: 'sv',
				pitch: (u: string) => `Jästtillsättningstemp (${u})`,
				reading: (u: string) => `Kärltemperatur (${u})`,
				telemetry: (u: string) => `Temperatur (${u})`,
				wizardPitch: (u: string) => `Jästtillsättningstemp (${u}, valfritt)`
			},
			{
				locale: 'de',
				pitch: (u: string) => `Anstelltemperatur (${u})`,
				reading: (u: string) => `Kesseltemperatur (${u})`,
				telemetry: (u: string) => `Temperatur (${u})`,
				wizardPitch: (u: string) => `Anstelltemperatur (${u}, optional)`
			},
			{
				locale: 'es',
				pitch: (u: string) => `Temperatura de inoculación (${u})`,
				reading: (u: string) => `Temperatura del tanque (${u})`,
				telemetry: (u: string) => `Temperatura (${u})`,
				wizardPitch: (u: string) => `Temperatura de inoculación (${u}, opcional)`
			},
			{
				locale: 'fr',
				pitch: (u: string) => `Température d'ensemencement (${u})`,
				reading: (u: string) => `Température de cuve (${u})`,
				telemetry: (u: string) => `Température (${u})`,
				wizardPitch: (u: string) => `Température d'ensemencement (${u}, facultatif)`
			}
		] as const;

		for (const c of unitCases) {
			i18n.setLocale(c.locale);
			for (const unit of ['°C', '°F']) {
				expect(t('batches.workspace.pitch_temp_label', { unit })).toBe(c.pitch(unit));
				expect(t('batches.workspace.reading_temp_label', { unit })).toBe(c.reading(unit));
				expect(t('batches.telemetry.temperature', { unit })).toBe(c.telemetry(unit));
				expect(t('batches.wizard.pitch_temp_optional', { unit })).toBe(c.wizardPitch(unit));
			}
			expect(t('batches.telemetry.temp_input_label')).not.toContain('°C');
			expect(t('batches.wizard.strike_temp')).not.toContain('°C');
			expect(t('batches.wizard.sparge_temp')).not.toContain('°C');
		}
	});

	it('TC-T2-R1-01: handles missing parameter interpolation gracefully without crashes', () => {
		i18n.setLocale('en');
		// Missing parameter does not throw
		const rawPitch = t('batches.workspace.pitch_temp_label');
		expect(rawPitch).toBeDefined();
		expect(rawPitch).toContain('Pitch Temp');

		i18n.setLocale('sv');
		const rawPitchSv = t('batches.workspace.pitch_temp_label');
		expect(rawPitchSv).toBeDefined();
		expect(rawPitchSv).toContain('Jästtillsättningstemp');
	});

	it('translates batch progression and stage measurement keys accurately in Swedish', () => {
		i18n.setLocale('sv');
		expect(t('batches.workspace.stage_nav_label')).toBe('Navigering för batchsteg');
		expect(t('batches.workspace.stage_status_completed')).toBe('Slutförd');
		expect(t('batches.workspace.stage_status_in_progress')).toBe('Pågår');
		expect(t('batches.workspace.stage_status_pending')).toBe('Kommande');
		expect(t('batches.workspace.stage_status_skipped')).toBe('Överhoppad');
		expect(t('batches.workspace.stage_measurements_title')).toBe('Stegmätningar & manuella värden');
		expect(t('batches.workspace.stage_measurements_desc')).toBe(
			'Registrera och justera manuella mätvärden för detta steg.'
		);
		expect(t('batches.workspace.edit_stage_inputs')).toBe('Redigera registrerade värden');
		expect(t('batches.workspace.save_stage_inputs')).toBe('Spara värden');
		expect(t('batches.workspace.cancel_edit_stage_inputs')).toBe('Avbryt');
		expect(t('batches.workspace.offline_equipment_notice')).toBe(
			'Tilldelad utrustning är offline — manuell stegprogression och mätloggning är aktiverad.'
		);
	});

	it('translates webhook supported formats and MQTT disconnect labels cleanly across locales', () => {
		i18n.setLocale('en');
		expect(t('equipment.supported_formats')).toBe('Compatible devices & JSON payloads');
		expect(t('equipment.generic_json')).toBe('Generic JSON');
		expect(t('settings.connectivity.disconnect')).toBe('Disconnect');

		i18n.setLocale('sv');
		expect(t('equipment.supported_formats')).toBe('Kompatibla enheter och JSON-format');
		expect(t('equipment.generic_json')).toBe('Generisk JSON');
		expect(t('settings.connectivity.disconnect')).toBe('Koppla från');

		for (const loc of ['de', 'fr', 'es'] as const) {
			i18n.setLocale(loc);
			expect(t('equipment.supported_formats')).not.toContain('Tilt');
			expect(t('equipment.supported_formats')).not.toContain('iSpindel');
		}
	});

	it('falls back to English when a key is absent', () => {
		i18n.setLocale('de');
		expect(t('nonexistent.key')).toBe('nonexistent.key');
	});
});
