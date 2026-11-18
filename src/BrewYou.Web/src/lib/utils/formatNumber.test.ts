import { describe, expect, it } from 'vitest';
import { formatDecimalInput, formatNumber, parseDecimal } from './formatNumber';
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

	it('parses both comma and dot decimal notation in parseDecimal', () => {
		expect(parseDecimal('1,054')).toBe(1.054);
		expect(parseDecimal('1.054')).toBe(1.054);
		expect(parseDecimal(' 20,5 ')).toBe(20.5);
		expect(parseDecimal('-2,5')).toBe(-2.5);
		expect(parseDecimal(1.012)).toBe(1.012);
		expect(parseDecimal('')).toBeNull();
		expect(parseDecimal('   ')).toBeNull();
		expect(parseDecimal(',')).toBeNull();
		expect(parseDecimal('.')).toBeNull();
		expect(parseDecimal('1,2,3')).toBeNull();
		expect(parseDecimal('abc')).toBeNull();
		expect(parseDecimal(null)).toBeNull();
	});

	it('formats editable decimal strings empty when null and locale-aware when present', () => {
		i18n.setLocale('sv');
		expect(formatDecimalInput(null, 3)).toBe('');
		expect(formatDecimalInput(1.052, 3)).toBe('1,052');
		i18n.setLocale('en');
		expect(formatDecimalInput(1.052, 3)).toBe('1.052');
	});
});
