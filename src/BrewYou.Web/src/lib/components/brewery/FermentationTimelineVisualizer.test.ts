import { describe, it, expect } from 'vitest';
import { getStageStyle } from './FermentationTimelineVisualizer.svelte';

describe('FermentationTimelineVisualizer', () => {
	it('provides correct style for Primary fermentation', () => {
		const style = getStageStyle('Primary');
		expect(style.barColor).toBe('#10b981');
		expect(style.badgeClass).toContain('emerald');
	});

	it('provides correct style for Secondary fermentation', () => {
		const style = getStageStyle('Secondary');
		expect(style.barColor).toBe('#059669');
		expect(style.badgeClass).toContain('teal');
	});

	it('provides correct style for Ramp stage', () => {
		const style = getStageStyle('Ramp');
		expect(style.barColor).toBe('#f59e0b');
		expect(style.badgeClass).toContain('amber');
	});

	it('provides correct style for FreeRise stage', () => {
		const style = getStageStyle('FreeRise');
		expect(style.barColor).toBe('#f97316');
		expect(style.badgeClass).toContain('orange');
	});

	it('provides correct style for ColdCrash stage', () => {
		const style = getStageStyle('ColdCrash');
		expect(style.barColor).toBe('#38bdf8');
		expect(style.badgeClass).toContain('sky');
	});

	it('provides correct style for Conditioning stage', () => {
		const style = getStageStyle('Conditioning');
		expect(style.barColor).toBe('#8b5cf6');
		expect(style.badgeClass).toContain('purple');
	});
});
