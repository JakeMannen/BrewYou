import { describe, it, expect } from 'vitest';
import { getWaterBalance } from './WaterBalanceVisualizer.svelte';

describe('WaterBalanceVisualizer', () => {
	it('categorizes low ratio as very malty', () => {
		const res = getWaterBalance(50, 100);
		expect(res.ratio).toBe(0.5);
		expect(res.key).toBe('calculations.water_balance.very_malty');
		expect(res.positionPercent).toBeLessThan(50);
	});

	it('categorizes ratio around 1.0 as balanced', () => {
		const res = getWaterBalance(100, 100);
		expect(res.ratio).toBe(1.0);
		expect(res.key).toBe('calculations.water_balance.balanced');
		expect(res.positionPercent).toBe(50);
	});

	it('categorizes ratio around 2.0 as bitter-forward', () => {
		const res = getWaterBalance(200, 100);
		expect(res.ratio).toBe(2.0);
		expect(res.key).toBe('calculations.water_balance.bitter');
		expect(res.positionPercent).toBeGreaterThan(50);
	});

	it('categorizes high ratio (>2.5) as very bitter and dry', () => {
		const res = getWaterBalance(300, 100);
		expect(res.ratio).toBe(3.0);
		expect(res.key).toBe('calculations.water_balance.very_bitter');
		expect(res.positionPercent).toBeGreaterThan(70);
	});
});
