import { describe, it, expect } from 'vitest';

export function calculateGaugeNormalizedPercent(value: number, min: number, max: number): number {
	if (max <= min) return 0;
	return Math.max(0, Math.min(1, (value - min) / (max - min)));
}

export function isGaugeInTarget(value: number, targetMin?: number, targetMax?: number): boolean {
	if (targetMin === undefined || targetMax === undefined) return true;
	return value >= targetMin && value <= targetMax;
}

export function polarToCartesian(cx: number, cy: number, r: number, angleDegrees: number) {
	const rad = ((angleDegrees - 90) * Math.PI) / 180.0;
	return {
		x: cx + r * Math.cos(rad),
		y: cy + r * Math.sin(rad)
	};
}

describe('VitalGauge Calculations & SVG Geometry', () => {
	it('calculates accurate normalized percentage within bounds', () => {
		// ABV 0 to 12%, value 6% -> 50%
		expect(calculateGaugeNormalizedPercent(6, 0, 12)).toBe(0.5);
		// IBU 0 to 100, value 25 -> 25%
		expect(calculateGaugeNormalizedPercent(25, 0, 100)).toBe(0.25);
		// OG 1.020 to 1.100, value 1.060 -> 50%
		expect(calculateGaugeNormalizedPercent(1.06, 1.02, 1.1)).toBeCloseTo(0.5);
	});

	it('clamps underflow and overflow gracefully', () => {
		expect(calculateGaugeNormalizedPercent(-5, 0, 10)).toBe(0);
		expect(calculateGaugeNormalizedPercent(15, 0, 10)).toBe(1);
	});

	it('protects against degenerate division by zero when max <= min', () => {
		expect(calculateGaugeNormalizedPercent(5, 10, 10)).toBe(0);
		expect(calculateGaugeNormalizedPercent(5, 12, 10)).toBe(0);
	});

	it('evaluates target range compliance accurately', () => {
		// Target ABV 5.8 - 6.8
		expect(isGaugeInTarget(6.2, 5.8, 6.8)).toBe(true);
		expect(isGaugeInTarget(5.0, 5.8, 6.8)).toBe(false);
		expect(isGaugeInTarget(7.5, 5.8, 6.8)).toBe(false);
		// No target bounds specified
		expect(isGaugeInTarget(4.5)).toBe(true);
	});

	it('computes valid polar coordinates for radial arc rendering', () => {
		const center = 65;
		const radius = 50;
		// 0 degrees is top
		const top = polarToCartesian(center, center, radius, 0);
		expect(top.x).toBeCloseTo(65);
		expect(top.y).toBeCloseTo(15);

		// 90 degrees is right
		const right = polarToCartesian(center, center, radius, 90);
		expect(right.x).toBeCloseTo(115);
		expect(right.y).toBeCloseTo(65);
	});
});
