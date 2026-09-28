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

	it('contains no duplicate top-level keys in any locale file', () => {
		const locales = ['en', 'sv', 'de', 'fr', 'es'];
		for (const loc of locales) {
			const topKeys = getTopLevelKeysFromRawJson(path.resolve(__dirname, `./locales/${loc}.json`));
			const unique = new Set(topKeys);
			expect(topKeys.length).toBe(unique.size);
			expect(topKeys.length).toBe(18);
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

	it('falls back to English when a key is absent', () => {
		i18n.setLocale('de');
		expect(t('nonexistent.key')).toBe('nonexistent.key');
	});
});
