import { describe, it, expect } from 'vitest';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';
import de from '$lib/i18n/locales/de.json';
import es from '$lib/i18n/locales/es.json';
import fr from '$lib/i18n/locales/fr.json';
import { extractInteraction } from '$lib/utils/brewlink';

describe('BrewLinkInteractionModal & Telemetry Handshake Logic', () => {
	it('maintains 100% key parity across all 5 locales for BrewLink interaction dialog', () => {
		const locales = [
			{ code: 'en', json: en },
			{ code: 'sv', json: sv },
			{ code: 'de', json: de },
			{ code: 'es', json: es },
			{ code: 'fr', json: fr }
		];

		const requiredKeys = [
			'brewlink_action_required',
			'brewlink_action_hint',
			'brewlink_hardware_confirmed',
			'brewlink_ack_prompt',
			'brewlink_dismiss_alert',
			'brewlink_interaction_alert',
			'brewlink_auto_mode',
			'brewlink_manual_mode',
			'brewlink_ramp_active',
			'brewlink_delayed_heat',
			'brewlink_schedule_delayed_heat',
			'brewlink_delayed_heat_min',
			'brewlink_delayed_heat_sec',
			'brewlink_sparge_alert',
			'brewlink_dismiss_sparge',
			'brewlink_error_code',
			'brewlink_auto_mode_locked',
			'brewlink_power_readonly',
			'brewlink_cancel_session',
			'brewlink_confirm_cancel_session_title',
			'brewlink_confirm_cancel_session_message',
			'brewlink_disconnect_auto',
			'brewlink_disconnect_manual'
		];

		for (const { code, json } of locales) {
			for (const k of requiredKeys) {
				const val = (json.equipment as unknown as Record<string, string>)[k];
				expect(val, `Missing ${k} in ${code}.json`).toBeDefined();
				expect(typeof val, `${k} in ${code}.json should be string`).toBe('string');
				expect(val.length, `${k} in ${code}.json should not be empty`).toBeGreaterThan(0);
			}
		}
	});

	it('correctly extracts interaction required and message for "Add water"', () => {
		const payload = JSON.stringify({
			temp: 20.5,
			interaction: {
				required: true,
				code: 1,
				message: 'Add water'
			}
		});

		const result = extractInteraction(payload);
		expect(result.required).toBe(true);
		expect(result.message).toBe('Add water');
		expect(result.code).toBe(1);
	});

	it('correctly extracts interaction required and message for "Add grain"', () => {
		const payload = JSON.stringify({
			temp: 65.0,
			interactionRequired: true,
			interactionCode: 2,
			interactionMessage: 'Add grain'
		});

		const result = extractInteraction(payload);
		expect(result.required).toBe(true);
		expect(result.message).toBe('Add grain');
		expect(result.code).toBe(2);
	});

	it('handles hardware dismissal when user confirms physically on BrewLink device', () => {
		// Telemetry before pressing physical button
		const activePrompt = JSON.stringify({
			interaction: { required: true, code: 2, message: 'Add grain' }
		});
		expect(extractInteraction(activePrompt).required).toBe(true);

		// Hardware button pressed: device transitions to required = false and code = 0
		const clearedPrompt = JSON.stringify({
			interaction: { required: false, code: 0, message: '' }
		});
		const result = extractInteraction(clearedPrompt);
		expect(result.required).toBe(false);
		expect(result.message).toBe('');
	});

	it('gracefully handles empty, missing, or malformed metricsJson without throwing', () => {
		expect(extractInteraction(null).required).toBe(false);
		expect(extractInteraction(undefined).required).toBe(false);
		expect(extractInteraction('').required).toBe(false);
		expect(extractInteraction('not-json').required).toBe(false);
		expect(extractInteraction('{}').required).toBe(false);
	});
});
