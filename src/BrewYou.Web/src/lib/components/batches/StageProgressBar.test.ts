import { describe, it, expect } from 'vitest';
import {
	calculateProgress,
	calculateMilestonePosition,
	groupMilestones,
	formatProgressTime
} from './StageProgressBar.svelte';
import type { BatchIngredientDto } from '$lib/types/api';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';
import de from '$lib/i18n/locales/de.json';
import es from '$lib/i18n/locales/es.json';
import fr from '$lib/i18n/locales/fr.json';

describe('StageProgressBar Logic & Calculations', () => {
	it('calculates progress percentage accurately across lifecycle', () => {
		// Start of 60m timer (3600s remaining) -> 0%
		expect(calculateProgress(3600, 3600)).toBe(0);

		// Halfway (1800s remaining) -> 50%
		expect(calculateProgress(3600, 1800)).toBe(50);

		// Complete (0s remaining) -> 100%
		expect(calculateProgress(3600, 0)).toBe(100);

		// Negative remaining seconds clamps safely to 100%
		expect(calculateProgress(3600, -30)).toBe(100);

		// Zero duration boundary condition
		expect(calculateProgress(0, 0)).toBe(100);
	});

	it('gracefully handles elastic duration expansion on timer adjustments (+5m)', () => {
		// User added time such that remaining (3900s) exceeds initial planned (3600s)
		// Effective total expands to 3900s, elapsed = 0s -> 0%
		expect(calculateProgress(3600, 3900)).toBe(0);

		// At 30m elapsed (1800s remaining), user adds +5m (now 2100s remaining).
		// Effective total = max(3600, 2100) = 3600s. Elapsed = 3600 - 2100 = 1500s.
		// Progress = 1500 / 3600 = 41.666...%
		expect(calculateProgress(3600, 2100)).toBeCloseTo(41.667, 2);
	});

	it('calculates milestone positions along boil timeline correctly', () => {
		const boilTotalSeconds = 3600; // 60 min

		// 60m addition: added at minute 0 (start) -> 0%
		expect(calculateMilestonePosition(boilTotalSeconds, 60)).toBe(0);

		// 15m addition: added when 15m remain (elapsed 45m / 60m) -> 75%
		expect(calculateMilestonePosition(boilTotalSeconds, 15)).toBe(75);

		// 0m addition (flameout): added when 0m remain (elapsed 60m / 60m) -> 100%
		expect(calculateMilestonePosition(boilTotalSeconds, 0)).toBe(100);

		// Out-of-bounds addition clamped between 0% and 100%
		expect(calculateMilestonePosition(boilTotalSeconds, 90)).toBe(0);
		expect(calculateMilestonePosition(boilTotalSeconds, -10)).toBe(100);
	});

	it('groups multiple ingredients at the same addition timestamp', () => {
		const mockIngredients: BatchIngredientDto[] = [
			{
				id: 'hop-1',
				batchId: 'b-1',
				name: 'Citra',
				type: 'Hop',
				amount: 25,
				unit: 'g',
				additionStage: 'Boil',
				additionTimeMinutes: 15,
				isChecked: true
			},
			{
				id: 'hop-2',
				batchId: 'b-1',
				name: 'Mosaic',
				type: 'Hop',
				amount: 25,
				unit: 'g',
				additionStage: 'Boil',
				additionTimeMinutes: 15,
				isChecked: false
			},
			{
				id: 'hop-3',
				batchId: 'b-1',
				name: 'Magnum',
				type: 'Hop',
				amount: 20,
				unit: 'g',
				additionStage: 'Boil',
				additionTimeMinutes: 60,
				isChecked: true
			}
		];

		const groups = groupMilestones(mockIngredients, 3600, 1800); // 30m remaining

		// Should produce 2 distinct groups: 60m and 15m
		expect(groups).toHaveLength(2);

		// First group: 60m addition (position 0%)
		expect(groups[0].additionTimeMinutes).toBe(60);
		expect(groups[0].positionPercentage).toBe(0);
		expect(groups[0].ingredients).toHaveLength(1);
		expect(groups[0].allChecked).toBe(true);
		expect(groups[0].isDue).toBe(true); // 30m remaining <= 60m

		// Second group: 15m additions (position 75%)
		expect(groups[1].additionTimeMinutes).toBe(15);
		expect(groups[1].positionPercentage).toBe(75);
		expect(groups[1].ingredients).toHaveLength(2);
		expect(groups[1].allChecked).toBe(false); // Mosaic is unchecked
		expect(groups[1].isDue).toBe(false); // 30m remaining > 15m
	});

	it('formats progress time consistently with MM:SS and H:MM:SS', () => {
		expect(formatProgressTime(0)).toBe('00:00');
		expect(formatProgressTime(45)).toBe('00:45');
		expect(formatProgressTime(90)).toBe('01:30');
		expect(formatProgressTime(3599)).toBe('59:59');
		expect(formatProgressTime(3600)).toBe('1:00:00');
		expect(formatProgressTime(3665)).toBe('1:01:05');
	});

	it('maintains 100% localization key parity across all 5 supported languages', () => {
		const requiredKeys = [
			'progress_bar_label',
			'progress_title',
			'milestone_popover_title',
			'milestone_due',
			'milestone_completed',
			'milestone_items_count',
			'close_popover'
		] as const;

		for (const key of requiredKeys) {
			expect(en.batches.workspace[key], `Missing en.batches.workspace.${key}`).toBeDefined();
			expect(sv.batches.workspace[key], `Missing sv.batches.workspace.${key}`).toBeDefined();
			expect(de.batches.workspace[key], `Missing de.batches.workspace.${key}`).toBeDefined();
			expect(es.batches.workspace[key], `Missing es.batches.workspace.${key}`).toBeDefined();
			expect(fr.batches.workspace[key], `Missing fr.batches.workspace.${key}`).toBeDefined();
		}
	});
});
