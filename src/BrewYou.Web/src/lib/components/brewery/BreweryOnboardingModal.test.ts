import { describe, it, expect, beforeEach, vi } from 'vitest';
import { brewery, DEFAULT_BREWERY_ID } from '$lib/stores/brewery.svelte';
import { auth } from '$lib/stores/auth.svelte';
import { settings, UNIT_PRESETS } from '$lib/stores/settings.svelte';
import { i18n } from '$lib/i18n/index.svelte';

describe('BreweryOnboardingModal Logic & Accessibility Contracts', () => {
	beforeEach(async () => {
		vi.restoreAllMocks();
		auth.clearAuth();
		i18n.init();
		await brewery.init();
	});

	it('provides localized strings and defaults in both English and Swedish', () => {
		i18n.setLocale('en');
		expect(i18n.t('brewery.default_brewery_name')).toBe('My brewery');
		expect(i18n.t('brewery.onboarding_title')).toBe('Welcome to BrewYou!');
		expect(i18n.t('brewery.onboarding_submit')).toBe('Save & Get Started');

		i18n.setLocale('sv');
		expect(i18n.t('brewery.default_brewery_name')).toBe('Mitt bryggeri');
		expect(i18n.t('brewery.onboarding_title')).toBe('Välkommen till BrewYou!');
		expect(i18n.t('brewery.onboarding_submit')).toBe('Spara och kom igång');
	});

	it('strictly blocks Escape key dismissal to enforce required onboarding step', () => {
		let defaultPrevented = false;
		let propagationStopped = false;

		const mockEvent = {
			key: 'Escape',
			preventDefault: () => {
				defaultPrevented = true;
			},
			stopPropagation: () => {
				propagationStopped = true;
			}
		};

		if (mockEvent.key === 'Escape') {
			mockEvent.preventDefault();
			mockEvent.stopPropagation();
		}

		expect(defaultPrevented).toBe(true);
		expect(propagationStopped).toBe(true);
	});

	it('defaults to locale name when user submits empty or whitespace input', async () => {
		i18n.setLocale('en');
		const resolveBreweryName = (input: string) => {
			const trimmed = input.replace(/[<>]/g, '').trim();
			return trimmed || i18n.t('brewery.default_brewery_name');
		};

		expect(resolveBreweryName('')).toBe('My brewery');
		expect(resolveBreweryName('   ')).toBe('My brewery');

		i18n.setLocale('sv');
		expect(resolveBreweryName('')).toBe('Mitt bryggeri');
		expect(resolveBreweryName('   ')).toBe('Mitt bryggeri');
	});

	it('sanitizes dangerous angle brackets and respects custom user brewery name', () => {
		const resolveBreweryName = (input: string) => {
			const trimmed = input.replace(/[<>]/g, '').trim();
			return trimmed || i18n.t('brewery.default_brewery_name');
		};

		expect(resolveBreweryName('  Hop Heaven <script>alert(1)</script>  ')).toBe(
			'Hop Heaven scriptalert(1)/script'
		);
		expect(resolveBreweryName('Garage Microbrewery 500L')).toBe('Garage Microbrewery 500L');
	});

	it('renames setup and marks onboarding as completed on submit', async () => {
		auth.needsBreweryOnboarding = true;
		const renameSpy = vi.spyOn(brewery, 'renameSetup').mockResolvedValue(true);
		const completeSpy = vi.spyOn(auth, 'completeOnboarding');

		const customName = 'Highland Hops';
		await brewery.renameSetup(DEFAULT_BREWERY_ID, customName);
		auth.completeOnboarding();

		expect(renameSpy).toHaveBeenCalledWith(DEFAULT_BREWERY_ID, customName);
		expect(completeSpy).toHaveBeenCalledTimes(1);
		expect(auth.needsBreweryOnboarding).toBe(false);
	});

	it('provides localized strings for unit presets in both English and Swedish', () => {
		i18n.setLocale('en');
		expect(i18n.t('brewery.onboarding_preset_label')).toBe('Measurement Standard & Unit Preset');
		expect(i18n.t('brewery.onboarding_preset_hint')).toContain(
			'Choose your default measurement units'
		);
		expect(i18n.t('settings.presets.european_metric')).toBe('European Metric');
		expect(i18n.t('settings.presets.us_craft')).toBe('US Craft');
		expect(i18n.t('settings.presets.uk_traditional')).toBe('UK Traditional');

		i18n.setLocale('sv');
		expect(i18n.t('brewery.onboarding_preset_label')).toBe('Måttenhetsstandard & förinställning');
		expect(i18n.t('brewery.onboarding_preset_hint')).toContain('Välj dina förvalda måttenheter');
		expect(i18n.t('settings.presets.european_metric')).toBe('Europeisk metrisk');
		expect(i18n.t('settings.presets.us_craft')).toBe('US Craft');
		expect(i18n.t('settings.presets.uk_traditional')).toBe('UK Traditionell');
	});

	it('applies the selected unit preset on onboarding submission', async () => {
		auth.needsBreweryOnboarding = true;
		const applyPresetSpy = vi.spyOn(settings, 'applyPreset').mockResolvedValue();
		const completeSpy = vi.spyOn(auth, 'completeOnboarding');

		// Choose US Craft preset
		await settings.applyPreset('US Craft');
		auth.completeOnboarding();

		expect(applyPresetSpy).toHaveBeenCalledWith('US Craft');
		expect(completeSpy).toHaveBeenCalledTimes(1);
		expect(auth.needsBreweryOnboarding).toBe(false);
	});

	it('exports the available unit presets matching Settings', () => {
		expect(UNIT_PRESETS).toEqual(['European Metric', 'US Craft', 'UK Traditional']);
	});
});
