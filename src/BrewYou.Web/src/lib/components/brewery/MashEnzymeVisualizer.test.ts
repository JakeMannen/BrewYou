import { describe, it, expect } from 'vitest';
import { getEnzymeZone } from './MashEnzymeVisualizer.svelte';

describe('MashEnzymeVisualizer', () => {
	it('categorizes acid/protein rest below 55°C', () => {
		const zone = getEnzymeZone(50);
		expect(zone.key).toBe('mash_profile.zone_protein');
		expect(zone.barColor).toBe('#38bdf8');
	});

	it('categorizes beta-glucan rest between 55°C and 61°C', () => {
		const zone = getEnzymeZone(58);
		expect(zone.key).toBe('mash_profile.zone_beta_glucan');
		expect(zone.barColor).toBe('#34d399');
	});

	it('categorizes beta-amylase rest between 62°C and 65°C', () => {
		const zone = getEnzymeZone(64.5);
		expect(zone.key).toBe('mash_profile.zone_beta_amylase');
		expect(zone.barColor).toBe('#f59e0b');
	});

	it('categorizes balanced saccharification rest between 66°C and 68°C', () => {
		const zone = getEnzymeZone(67);
		expect(zone.key).toBe('mash_profile.zone_balanced');
		expect(zone.barColor).toBe('#f97316');
	});

	it('categorizes alpha-amylase rest between 69°C and 74°C', () => {
		const zone = getEnzymeZone(71);
		expect(zone.key).toBe('mash_profile.zone_alpha_amylase');
		expect(zone.barColor).toBe('#d97706');
	});

	it('categorizes mash-out above 74°C', () => {
		const zone = getEnzymeZone(76);
		expect(zone.key).toBe('mash_profile.zone_mash_out');
		expect(zone.barColor).toBe('#ef4444');
	});
});
