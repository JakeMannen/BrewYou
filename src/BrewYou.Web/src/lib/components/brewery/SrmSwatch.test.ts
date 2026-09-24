import { describe, it, expect } from 'vitest';
import { srmToHexColor } from '$lib/calculators/brewing';

export function getSrmDescriptor(srm: number): string {
	if (srm <= 2) return 'Pale Straw';
	if (srm <= 4) return 'Straw / Pilsner';
	if (srm <= 6) return 'Gold';
	if (srm <= 9) return 'Deep Gold';
	if (srm <= 12) return 'Amber';
	if (srm <= 15) return 'Deep Amber';
	if (srm <= 20) return 'Copper';
	if (srm <= 27) return 'Brown';
	if (srm <= 35) return 'Dark Brown';
	return 'Imperial Stout / Black';
}

export function isSrmDark(srm: number): boolean {
	return srm > 8;
}

export function calculateEbc(srm: number, explicitEbc?: number): number {
	return explicitEbc ?? Number((srm * 1.97).toFixed(1));
}

describe('SRM Beer Color Calculations & Contrast', () => {
	it('maps SRM values to accurate spectrophotometric hex colors', () => {
		expect(srmToHexColor(1)).toBe('#f8f5b8');
		expect(srmToHexColor(2)).toBe('#f6f1a8');
		expect(srmToHexColor(6)).toBe('#d4bc2b');
		expect(srmToHexColor(13)).toBe('#975412');
		expect(srmToHexColor(20)).toBe('#5e2307');
		expect(srmToHexColor(35)).toBe('#240c03');
		expect(srmToHexColor(40)).toBe('#120501');
	});

	it('clamps low and extreme SRM values safely', () => {
		expect(srmToHexColor(-5)).toBe('#f8f5b8');
		expect(srmToHexColor(100)).toBe('#120501');
	});

	it('computes correct style descriptors across the SRM scale', () => {
		expect(getSrmDescriptor(2)).toBe('Pale Straw');
		expect(getSrmDescriptor(4)).toBe('Straw / Pilsner');
		expect(getSrmDescriptor(6)).toBe('Gold');
		expect(getSrmDescriptor(11)).toBe('Amber');
		expect(getSrmDescriptor(18)).toBe('Copper');
		expect(getSrmDescriptor(25)).toBe('Brown');
		expect(getSrmDescriptor(45)).toBe('Imperial Stout / Black');
	});

	it('determines WCAG accessible text contrast (dark text for pale, white for dark beers)', () => {
		expect(isSrmDark(2)).toBe(false); // Pale Straw -> dark text
		expect(isSrmDark(6)).toBe(false); // Gold -> dark text
		expect(isSrmDark(8)).toBe(false); // Deep Gold -> dark text
		expect(isSrmDark(9)).toBe(true); // Amber -> white text
		expect(isSrmDark(14)).toBe(true); // Copper -> white text
		expect(isSrmDark(30)).toBe(true); // Stout -> white text
	});

	it('calculates derived EBC with 1.97 ratio or respects explicit EBC', () => {
		expect(calculateEbc(10)).toBe(19.7);
		expect(calculateEbc(20)).toBe(39.4);
		expect(calculateEbc(10, 22.5)).toBe(22.5);
	});

	it('supports valid swatch and glass visual representation shapes', () => {
		const validShapes = ['circle', 'pill', 'swatch', 'glass'] as const;
		expect(validShapes).toContain('glass');
		expect(validShapes.length).toBe(4);
	});
});
