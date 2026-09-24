import { describe, it, expect } from 'vitest';
import { srmToHexColor } from '$lib/calculators/brewing';

export function getFoamColors(srm: number): { top: string; bottom: string; stroke: string } {
	if (srm <= 8) {
		return { top: '#FFFFFF', bottom: '#F3EFE0', stroke: '#E8E2CF' };
	} else if (srm <= 20) {
		return { top: '#FDF7EB', bottom: '#E4D5BC', stroke: '#D8C5A7' };
	} else {
		return { top: '#ECE1CE', bottom: '#C4A987', stroke: '#B2946E' };
	}
}

export function getSrmDescriptorKey(srm: number): string {
	if (srm <= 2) return 'pale_straw';
	if (srm <= 4) return 'straw';
	if (srm <= 6) return 'gold';
	if (srm <= 9) return 'deep_gold';
	if (srm <= 12) return 'amber';
	if (srm <= 15) return 'deep_amber';
	if (srm <= 20) return 'copper';
	if (srm <= 27) return 'brown';
	if (srm <= 35) return 'dark_brown';
	return 'black';
}

export function calculateBeerGlassDimensions(
	size: 'xs' | 'sm' | 'md' | 'lg' | 'xl' | 'full' | number
): {
	h?: number;
	w?: number;
	isFull?: boolean;
} {
	if (size === 'full') {
		return { isFull: true };
	}
	if (typeof size === 'number') {
		return { h: size, w: Number(((size * 34) / 63).toFixed(1)), isFull: false };
	}
	const preset: Record<string, { h: number; w: number }> = {
		xs: { h: 28, w: 15 },
		sm: { h: 48, w: 26 },
		md: { h: 62, w: 33 },
		lg: { h: 78, w: 42 },
		xl: { h: 104, w: 56 }
	};
	return { ...(preset[size] ?? preset.md), isFull: false };
}

describe('BeerGlass Component Specifications', () => {
	it('maps SRM values to spectrophotometric liquid hex colors', () => {
		expect(srmToHexColor(2)).toBe('#f6f1a8'); // Pale lager
		expect(srmToHexColor(6)).toBe('#d4bc2b'); // Blonde / Golden ale
		expect(srmToHexColor(12)).toBe('#975412'); // Amber / Pale ale
		expect(srmToHexColor(20)).toBe('#5e2307'); // Brown / Dubbel
		expect(srmToHexColor(35)).toBe('#240c03'); // Porter / Stout
		expect(srmToHexColor(40)).toBe('#120501'); // Imperial Stout
	});

	it('computes realistic foam head coloration adapting to beer styles', () => {
		// Pale beers (SRM <= 8) get crisp ivory foam
		const paleFoam = getFoamColors(4);
		expect(paleFoam.top).toBe('#FFFFFF');
		expect(paleFoam.bottom).toBe('#F3EFE0');

		// Amber / Copper beers (8 < SRM <= 20) get warm cream head
		const amberFoam = getFoamColors(14);
		expect(amberFoam.top).toBe('#FDF7EB');
		expect(amberFoam.bottom).toBe('#E4D5BC');

		// Dark beers (SRM > 20) get rich tan / mocha foam
		const stoutFoam = getFoamColors(30);
		expect(stoutFoam.top).toBe('#ECE1CE');
		expect(stoutFoam.bottom).toBe('#C4A987');
	});

	it('resolves correct descriptor keys across standard SRM spectrum', () => {
		expect(getSrmDescriptorKey(1)).toBe('pale_straw');
		expect(getSrmDescriptorKey(3)).toBe('straw');
		expect(getSrmDescriptorKey(5)).toBe('gold');
		expect(getSrmDescriptorKey(8)).toBe('deep_gold');
		expect(getSrmDescriptorKey(11)).toBe('amber');
		expect(getSrmDescriptorKey(14)).toBe('deep_amber');
		expect(getSrmDescriptorKey(19)).toBe('copper');
		expect(getSrmDescriptorKey(25)).toBe('brown');
		expect(getSrmDescriptorKey(32)).toBe('dark_brown');
		expect(getSrmDescriptorKey(45)).toBe('black');
	});

	it('calculates proportional dimensions for preset sizes and numeric overrides', () => {
		expect(calculateBeerGlassDimensions('xs')).toEqual({ h: 28, w: 15, isFull: false });
		expect(calculateBeerGlassDimensions('sm')).toEqual({ h: 48, w: 26, isFull: false });
		expect(calculateBeerGlassDimensions('md')).toEqual({ h: 62, w: 33, isFull: false });
		expect(calculateBeerGlassDimensions('lg')).toEqual({ h: 78, w: 42, isFull: false });
		expect(calculateBeerGlassDimensions('xl')).toEqual({ h: 104, w: 56, isFull: false });
		expect(calculateBeerGlassDimensions('full')).toEqual({ isFull: true });
		expect(calculateBeerGlassDimensions(63)).toEqual({ h: 63, w: 34, isFull: false });
	});
});
