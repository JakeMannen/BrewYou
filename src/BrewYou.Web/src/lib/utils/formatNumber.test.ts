import { describe, expect, it } from 'vitest';
import { formatNumber } from './formatNumber';
import { i18n } from '$lib/i18n/index.svelte';

describe('formatNumber utility', () => {
	it('formats numbers with dot decimal separator in English', () => {
		i18n.setLocale('en');
		expect(formatNumber(20.5, 1)).toBe('20.5');
		expect(formatNumber(1.05, 3)).toBe('1.050');
		expect(formatNumber(100, 0)).toBe('100');
		expect(formatNumber(0, 1)).toBe('0.0');
	});

	it('formats numbers with comma decimal separator in Swedish', () => {
		i18n.setLocale('sv');
		expect(formatNumber(20.5, 1)).toBe('20,5');
		expect(formatNumber(1.05, 3)).toBe('1,050');
		expect(formatNumber(100, 0)).toBe('100');
		expect(formatNumber(0, 1)).toBe('0,0');
		// Reset back to English
		i18n.setLocale('en');
	});

	it('handles null, undefined, and NaN gracefully', () => {
		expect(formatNumber(null, 1)).toBe('-');
		expect(formatNumber(undefined, 1)).toBe('-');
		expect(formatNumber(NaN, 1)).toBe('-');
		expect(formatNumber(null, 1, 'N/A')).toBe('N/A');
	});
});
