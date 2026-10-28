import { describe, it, expect } from 'vitest';
import { formatHudTime } from './BrewStationHudModal.svelte';
import { calculateProgress, groupMilestones } from './StageProgressBar.svelte';
import type { BatchIngredientDto } from '$lib/types/api';

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
});
