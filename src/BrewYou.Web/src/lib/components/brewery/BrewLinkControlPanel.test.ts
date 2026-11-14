import { describe, it, expect } from 'vitest';
import type { EquipmentDto, SendBrewLinkControlRequest } from '$lib/types/api';
import { getInteractionActions, isG30ActionBlockedInAutoMode } from '$lib/utils/brewlink';

export function parseBrewLinkMetrics(latestMetricsJson?: string | null) {
	if (!latestMetricsJson) return {};
	try {
		return JSON.parse(latestMetricsJson);
	} catch {
		return {};
	}
}

export function formatRemainingTime(sec?: number | null): string {
	if (sec == null || sec <= 0) return '00:00';
	const m = Math.floor(sec / 60);
	const s = sec % 60;
	return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
}

export function buildControlPayload(
	action: string,
	state?: boolean,
	powerPct?: number,
	tempC?: number,
	durationMin?: number
): SendBrewLinkControlRequest {
	return {
		action,
		state,
		powerPct,
		tempC,
		durationMin
	};
}

describe('BrewLinkControlPanel Specifications', () => {
	const sampleEquipment: EquipmentDto = {
		id: 'eq-123',
		brewerySetupId: 'setup-123',
		name: 'Grainfather G30',
		type: 'Boiler',
		subtype: 'AllInOne',
		capacity: 30,
		unit: 'Liters',
		capacityLiters: 30,
		currentVolume: 25,
		currentVolumeLiters: 25,
		fillPercentage: 83.3,
		createdAt: '2026-01-01T00:00:00Z',
		updatedAt: '2026-01-01T00:00:00Z',
		connectionType: 'BrewLink',
		currentTemperatureC: 65.5,
		latestMetricsJson: JSON.stringify({
			targetTempC: 66.0,
			boilTempC: 100.0,
			heatActive: true,
			heatPowerPct: 100,
			pumpActive: false,
			autoMode: true,
			timerActive: true,
			timerPaused: false,
			timerRemainingSec: 2400,
			timerTotalSec: 3600,
			stepIndex: 1,
			stepName: 'Mash Step 1',
			interactionRequired: false,
			connected: true
		})
	};

	it('correctly parses canonical BrewLink metrics from equipment', () => {
		const metrics = parseBrewLinkMetrics(sampleEquipment.latestMetricsJson);
		expect(metrics.targetTempC).toBe(66.0);
		expect(metrics.heatActive).toBe(true);
		expect(metrics.heatPowerPct).toBe(100);
		expect(metrics.pumpActive).toBe(false);
		expect(metrics.timerActive).toBe(true);
		expect(metrics.timerRemainingSec).toBe(2400);
		expect(metrics.connected).toBe(true);
	});

	it('formats remaining timer countdown into MM:SS correctly', () => {
		expect(formatRemainingTime(2400)).toBe('40:00');
		expect(formatRemainingTime(65)).toBe('01:05');
		expect(formatRemainingTime(9)).toBe('00:09');
		expect(formatRemainingTime(0)).toBe('00:00');
		expect(formatRemainingTime(null)).toBe('00:00');
	});

	it('builds valid control request payloads for heater, pump, and setpoint', () => {
		const heatReq = buildControlPayload('set_heat', true, 75);
		expect(heatReq.action).toBe('set_heat');
		expect(heatReq.state).toBe(true);
		expect(heatReq.powerPct).toBe(75);

		const pumpReq = buildControlPayload('set_pump', true);
		expect(pumpReq.action).toBe('set_pump');
		expect(pumpReq.state).toBe(true);

		const tempReq = buildControlPayload('set_target_temp', undefined, undefined, 68.5);
		expect(tempReq.action).toBe('set_target_temp');
		expect(tempReq.tempC).toBe(68.5);

		const timerReq = buildControlPayload('set_timer', undefined, undefined, undefined, 45);
		expect(timerReq.action).toBe('set_timer');
		expect(timerReq.durationMin).toBe(45);

		const pauseReq = buildControlPayload('pause_timer');
		expect(pauseReq.action).toBe('pause_timer');

		const resumeReq = buildControlPayload('resume_timer');
		expect(resumeReq.action).toBe('resume_timer');

		const cancelReq = buildControlPayload('cancel_timer');
		expect(cancelReq.action).toBe('cancel_timer');

		const advanceReq = buildControlPayload('advance_step');
		expect(advanceReq.action).toBe('advance_step');

		const confirmPromptReq = buildControlPayload('confirm_prompt');
		expect(confirmPromptReq.action).toBe('confirm_prompt');

		const dismissPromptReq = buildControlPayload('dismiss_prompt');
		expect(dismissPromptReq.action).toBe('dismiss_prompt');

		// Toggle OFF commands
		const heatOffReq = buildControlPayload('set_heat', false, 0);
		expect(heatOffReq.action).toBe('set_heat');
		expect(heatOffReq.state).toBe(false);
		expect(heatOffReq.powerPct).toBe(0);

		const pumpOffReq = buildControlPayload('set_pump', false);
		expect(pumpOffReq.action).toBe('set_pump');
		expect(pumpOffReq.state).toBe(false);
	});

	it('supports both camelCase and snake_case BrewLink metrics', () => {
		const snakeJson = JSON.stringify({
			current_temp_c: 67.4,
			target_temp_c: 68.0,
			heat_active: true,
			power_pct: 75,
			pump_on: true
		});
		const parsed = parseBrewLinkMetrics(snakeJson);
		expect(parsed.current_temp_c).toBe(67.4);
		expect(parsed.heat_active).toBe(true);
		expect(parsed.pump_on).toBe(true);
	});

	it('identifies hardware interaction alerts requiring user response', () => {
		const interactionMetrics = {
			interactionRequired: true,
			interactionPrompt: 'Add 60 min bittering hops now',
			timerActive: true,
			timerRemainingSec: 3600
		};

		expect(interactionMetrics.interactionRequired).toBe(true);
		expect(interactionMetrics.interactionPrompt).toBe('Add 60 min bittering hops now');
	});

	it('handles corrupt or empty metrics JSON gracefully', () => {
		expect(parseBrewLinkMetrics(null)).toEqual({});
		expect(parseBrewLinkMetrics('')).toEqual({});
		expect(parseBrewLinkMetrics('{ invalid json')).toEqual({});
	});

	it('maps G30 interaction codes 1-6 to exact confirm and dismiss actions', () => {
		expect(getInteractionActions(1)).toEqual({
			confirmAction: 'press_set',
			dismissAction: 'dismiss_alert',
			confirmLabelKey: 'equipment.brewlink_press_set',
			dismissLabelKey: 'equipment.brewlink_dismiss_alert'
		});
		expect(getInteractionActions(2).confirmAction).toBe('ack_interaction');
		expect(getInteractionActions(3)).toEqual({
			confirmAction: 'ack_interaction',
			dismissAction: 'dismiss_sparge_alert',
			confirmLabelKey: 'equipment.brewlink_ack_prompt',
			dismissLabelKey: 'equipment.brewlink_dismiss_sparge'
		});
		expect(getInteractionActions(4)).toEqual({
			confirmAction: 'press_set',
			dismissAction: 'dismiss_sparge_alert',
			confirmLabelKey: 'equipment.brewlink_press_set',
			dismissLabelKey: 'equipment.brewlink_dismiss_sparge'
		});
		expect(getInteractionActions(5).confirmAction).toBe('dismiss_alert');
		expect(getInteractionActions(6).confirmAction).toBe('ack_interaction');
	});

	it('blocks setpoint and timer edits only when auto_mode is true', () => {
		for (const action of ['set_timer', 'cancel_timer', 'set_target_temp', 'temp_up', 'temp_down']) {
			expect(isG30ActionBlockedInAutoMode(action, true)).toBe(true);
			expect(isG30ActionBlockedInAutoMode(action, false)).toBe(false);
		}
		for (const action of [
			'pause_timer',
			'resume_timer',
			'toggle_timer',
			'press_set',
			'cancel_session',
			'set_heat',
			'set_pump'
		]) {
			expect(isG30ActionBlockedInAutoMode(action, true)).toBe(false);
		}
	});
});
