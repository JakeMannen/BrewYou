import { describe, it, expect } from 'vitest';
import { formatHudTime } from './BrewStationHudModal.svelte';

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
});
