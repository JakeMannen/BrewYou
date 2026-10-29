import { describe, it, expect } from 'vitest';
import { formatHudTime, getNextAddition } from './BrewStationHudModal.svelte';
import { calculateProgress, groupMilestones } from './StageProgressBar.svelte';
import type { BatchIngredientDto } from '$lib/types/api';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';
import de from '$lib/i18n/locales/de.json';
import es from '$lib/i18n/locales/es.json';
import fr from '$lib/i18n/locales/fr.json';

describe('BrewStationHudModal', () => {
	it('formats zero seconds as 00:00', () => {
		expect(formatHudTime(0)).toBe('00:00');
	});

	it('formats negative numbers safely as 00:00', () => {
		expect(formatHudTime(-15)).toBe('00:00');
	});

	it('formats seconds under one hour with MM:SS', () => {
		expect(formatHudTime(45)).toBe('00:45');
		expect(formatHudTime(90)).toBe('01:30');
		expect(formatHudTime(3599)).toBe('59:59');
	});

	it('formats hours with H:MM:SS format', () => {
		expect(formatHudTime(3600)).toBe('1:00:00');
		expect(formatHudTime(3665)).toBe('1:01:05');
		expect(formatHudTime(7200)).toBe('2:00:00');
	});

	it('handles floating point seconds by flooring', () => {
		expect(formatHudTime(89.7)).toBe('01:29');
	});

	it('synchronizes HUD progress calculation with batch boil timeline', () => {
		const totalBoilSeconds = 3600;
		const remainingSeconds = 900; // 15 mins remaining
		const progress = calculateProgress(totalBoilSeconds, remainingSeconds);
		expect(progress).toBe(75);

		const mockAdditions: BatchIngredientDto[] = [
			{
				id: 'hop-bitter',
				batchId: 'b-101',
				name: 'Centennial',
				type: 'Hop',
				amount: 30,
				unit: 'g',
				additionStage: 'Boil',
				additionTimeMinutes: 60,
				isChecked: true
			},
			{
				id: 'hop-aroma',
				batchId: 'b-101',
				name: 'Amarillo',
				type: 'Hop',
				amount: 25,
				unit: 'g',
				additionStage: 'Boil',
				additionTimeMinutes: 15,
				isChecked: false
			}
		];

		const milestones = groupMilestones(mockAdditions, totalBoilSeconds, remainingSeconds);
		expect(milestones).toHaveLength(2);
		expect(milestones[0].isDue).toBe(true); // 60m addition passed
		expect(milestones[1].isDue).toBe(true); // 15m addition is due now at 900s remaining
		expect(milestones[1].allChecked).toBe(false);
	});

	it('identifies upcoming next hop addition during boil correctly', () => {
		const additions: BatchIngredientDto[] = [
			{
				id: 'hop-1',
				batchId: 'b-1',
				name: 'Magnum',
				type: 'Hop',
				amount: 20,
				unit: 'g',
				additionStage: 'Boil',
				additionTimeMinutes: 60,
				isChecked: true // already added at start
			},
			{
				id: 'hop-2',
				batchId: 'b-1',
				name: 'Cascade',
				type: 'Hop',
				amount: 30,
				unit: 'g',
				additionStage: 'Boil',
				additionTimeMinutes: 15,
				isChecked: false
			},
			{
				id: 'hop-3',
				batchId: 'b-1',
				name: 'Citra',
				type: 'Hop',
				amount: 40,
				unit: 'g',
				additionStage: 'Boil',
				additionTimeMinutes: 0,
				isChecked: false
			}
		];

		// At 30m remaining (1800s), next addition is Cascade at 15m (not due yet)
		const next = getNextAddition(additions, 1800);
		expect(next).not.toBeNull();
		expect(next?.name).toBe('Cascade');
		expect(next?.additionTimeMinutes).toBe(15);
		expect(next?.isDueNow).toBe(false);

		// At 15m remaining (900s), Cascade is due now
		const dueNow = getNextAddition(additions, 900);
		expect(dueNow?.name).toBe('Cascade');
		expect(dueNow?.isDueNow).toBe(true);

		// When Cascade is checked, Citra at 0m is next
		additions[1].isChecked = true;
		const nextAfterCascade = getNextAddition(additions, 600);
		expect(nextAfterCascade?.name).toBe('Citra');
		expect(nextAfterCascade?.isDueNow).toBe(false);

		// When all checked, returns null
		additions[2].isChecked = true;
		expect(getNextAddition(additions, 300)).toBeNull();
		expect(getNextAddition([], 300)).toBeNull();
	});

	it('maintains 100% localization key parity across all 5 supported languages for HUD keys', () => {
		const requiredHudKeys = [
			'brew_station_hud',
			'exit_hud',
			'timer_complete',
			'timer_running_hint',
			'timer_paused_hint',
			'hud_pause',
			'hud_start',
			'reset_timer',
			'reset_timer_title',
			'adjust_timer_forward',
			'adjust_timer_backward',
			'next_step',
			'target_temp_hud',
			'live_sensor_hud',
			'steam_friendly_mode',
			'high_contrast_active',
			'sparge_water_hud',
			'sparge_temp_na',
			'next_addition_hud',
			'post_boil_vol_hud',
			'target_post_boil_short',
			'boil_off_rate_hint',
			'all_milestones_completed',
			'due_now_badge'
		] as const;

		for (const key of requiredHudKeys) {
			expect(en.batches.workspace[key], `Missing en.batches.workspace.${key}`).toBeDefined();
			expect(sv.batches.workspace[key], `Missing sv.batches.workspace.${key}`).toBeDefined();
			expect(de.batches.workspace[key], `Missing de.batches.workspace.${key}`).toBeDefined();
			expect(es.batches.workspace[key], `Missing es.batches.workspace.${key}`).toBeDefined();
			expect(fr.batches.workspace[key], `Missing fr.batches.workspace.${key}`).toBeDefined();
		}
	});
});
