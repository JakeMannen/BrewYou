import { describe, it, expect } from 'vitest';
import { getHopPhase } from './HopScheduleVisualizer.svelte';

describe('HopScheduleVisualizer', () => {
	it('categorizes Mash additions as aroma/other', () => {
		const phase = getHopPhase('Mash', 60);
		expect(phase.key).toBe('formulator.phase_aroma');
		expect(phase.barColor).toBe('#84cc16');
	});

	it('categorizes long boils (>=45 min) as bittering', () => {
		const phase = getHopPhase('Boil', 60);
		expect(phase.key).toBe('formulator.phase_bittering');
		expect(phase.barColor).toBe('#ea580c');
	});

	it('categorizes mid boils (20-40 min) as flavor', () => {
		const phase = getHopPhase('Boil', 30);
		expect(phase.key).toBe('formulator.phase_flavor');
		expect(phase.barColor).toBe('#f59e0b');
	});

	it('categorizes short boils (<20 min) as aroma', () => {
		const phase = getHopPhase('Boil', 10);
		expect(phase.key).toBe('formulator.phase_aroma');
		expect(phase.barColor).toBe('#10b981');
	});

	it('categorizes Whirlpool additions', () => {
		const phase = getHopPhase('Whirlpool', 0);
		expect(phase.key).toBe('formulator.phase_whirlpool');
		expect(phase.barColor).toBe('#059669');
	});

	it('categorizes DryHop additions', () => {
		const phase = getHopPhase('DryHop', 0);
		expect(phase.key).toBe('formulator.phase_dry_hop');
		expect(phase.barColor).toBe('#6366f1');
	});
});
