import type { BrewLinkControlAction } from '$lib/types/api';

export interface ParsedInteraction {
	required: boolean;
	message: string;
	code?: number;
}

export interface BrewLinkInteractionActionPlan {
	confirmAction: BrewLinkControlAction;
	dismissAction: BrewLinkControlAction;
	confirmLabelKey:
		| 'equipment.brewlink_press_set'
		| 'equipment.brewlink_ack_prompt'
		| 'equipment.brewlink_dismiss_alert';
	dismissLabelKey: 'equipment.brewlink_dismiss_sparge' | 'equipment.brewlink_dismiss_alert';
}

const AUTO_MODE_LOCKED_ACTIONS = new Set<string>([
	'set_timer',
	'cancel_timer',
	'set_target_temp',
	'temp_up',
	'temp_down'
]);

export function isG30ActionBlockedInAutoMode(
	action: BrewLinkControlAction | string,
	autoMode?: boolean | null
): boolean {
	return autoMode === true && AUTO_MODE_LOCKED_ACTIONS.has(action.toLowerCase());
}

/**
 * Maps Grainfather G30 interaction codes (0-6 from brewlink-external-client Section 6.1.1)
 * to their expected confirm & dismiss actions.
 */
export function getInteractionActions(code?: number | null): BrewLinkInteractionActionPlan {
	switch (code) {
		case 1:
			// Add mash water, then press SET to start heating
			return {
				confirmAction: 'press_set',
				dismissAction: 'dismiss_alert',
				confirmLabelKey: 'equipment.brewlink_press_set',
				dismissLabelKey: 'equipment.brewlink_dismiss_alert'
			};
		case 3:
			// Mash finished: sparge alert
			return {
				confirmAction: 'ack_interaction',
				dismissAction: 'dismiss_sparge_alert',
				confirmLabelKey: 'equipment.brewlink_ack_prompt',
				dismissLabelKey: 'equipment.brewlink_dismiss_sparge'
			};
		case 4:
			// Sparge: add sparge water, SET skips to boil
			return {
				confirmAction: 'press_set',
				dismissAction: 'dismiss_sparge_alert',
				confirmLabelKey: 'equipment.brewlink_press_set',
				dismissLabelKey: 'equipment.brewlink_dismiss_sparge'
			};
		case 5:
			// Boil addition alert (hops/adjuncts)
			return {
				confirmAction: 'dismiss_alert',
				dismissAction: 'dismiss_alert',
				confirmLabelKey: 'equipment.brewlink_dismiss_alert',
				dismissLabelKey: 'equipment.brewlink_dismiss_alert'
			};
		case 2:
		case 6:
		default:
			// Add grain (2), boil finished start cooling (6), or generic prompt
			return {
				confirmAction: 'ack_interaction',
				dismissAction: 'dismiss_alert',
				confirmLabelKey: 'equipment.brewlink_ack_prompt',
				dismissLabelKey: 'equipment.brewlink_dismiss_alert'
			};
	}
}

export function extractInteraction(metricsJson?: string | null): ParsedInteraction {
	if (!metricsJson) return { required: false, message: '' };
	try {
		const raw = typeof metricsJson === 'string' ? JSON.parse(metricsJson) : metricsJson;
		if (!raw || typeof raw !== 'object') return { required: false, message: '' };

		const parseBool = (v: unknown): boolean => {
			if (typeof v === 'boolean') return v;
			if (typeof v === 'number') return v === 1;
			if (typeof v === 'string') return v.toLowerCase() === 'true' || v === '1';
			return false;
		};

		const parseNum = (v: unknown): number | null => {
			if (typeof v === 'number') return isNaN(v) ? null : v;
			if (typeof v === 'string') {
				const n = Number(v);
				return isNaN(n) ? null : n;
			}
			return null;
		};

		const code =
			parseNum(raw.interaction?.code) ??
			parseNum(raw.interaction_code) ??
			parseNum(raw.interactionCode);
		const hasActiveCode = code != null && code > 0;

		const required =
			parseBool(raw.interaction?.required) ||
			parseBool(raw.interaction_required) ||
			parseBool(raw.interactionRequired) ||
			hasActiveCode;

		const message =
			raw.interaction?.message ??
			raw.interaction_message ??
			raw.interactionMessage ??
			raw.prompt ??
			raw.alert ??
			'';

		return { required, message: String(message), code: code ?? undefined };
	} catch {
		return { required: false, message: '' };
	}
}
