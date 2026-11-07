import { describe, expect, it } from 'vitest';
import { ALL_IN_ONE_PRESETS } from './equipment-presets';
import { litersToGallons } from '$lib/stores/settings.svelte';

describe('All-In-One Equipment Presets', () => {
	it('defines all 8 required standard craft brewing models', () => {
		expect(ALL_IN_ONE_PRESETS).toHaveLength(8);
		const names = ALL_IN_ONE_PRESETS.map((p) => p.name);
		expect(names).toContain('Grainfather G30');
		expect(names).toContain('Grainfather G40');
		expect(names).toContain('Grainfather G70');
		expect(names).toContain('BrewZilla 35L Gen 4');
		expect(names).toContain('BrewZilla 65L Gen 4');
		expect(names).toContain('Speidel Braumeister 20L');
		expect(names).toContain('Brewtools B40pro');
		expect(names).toContain('Anvil Foundry 10.5');
	});

	it('configures exact factory specifications for Grainfather models', () => {
		const g30 = ALL_IN_ONE_PRESETS.find((p) => p.name === 'Grainfather G30')!;
		expect(g30.capacityLiters).toBe(30);
		expect(g30.mashTunDeadSpaceLiters).toBe(3.5);
		expect(g30.boilOffRatePerHour).toBe(3.0);
		expect(g30.trubLossLiters).toBe(2.0);

		const g40 = ALL_IN_ONE_PRESETS.find((p) => p.name === 'Grainfather G40')!;
		expect(g40.capacityLiters).toBe(40);
		expect(g40.mashTunDeadSpaceLiters).toBe(4.5);
		expect(g40.boilOffRatePerHour).toBe(3.5);
		expect(g40.trubLossLiters).toBe(2.5);

		const g70 = ALL_IN_ONE_PRESETS.find((p) => p.name === 'Grainfather G70')!;
		expect(g70.capacityLiters).toBe(70);
		expect(g70.mashTunDeadSpaceLiters).toBe(8.0);
		expect(g70.boilOffRatePerHour).toBe(4.0);
		expect(g70.trubLossLiters).toBe(3.5);
	});

	it('configures exact factory specifications for BrewZilla models', () => {
		const bz35 = ALL_IN_ONE_PRESETS.find((p) => p.name === 'BrewZilla 35L Gen 4')!;
		expect(bz35.capacityLiters).toBe(35);
		expect(bz35.mashTunDeadSpaceLiters).toBe(3.5);
		expect(bz35.boilOffRatePerHour).toBe(3.0);
		expect(bz35.trubLossLiters).toBe(2.0);

		const bz65 = ALL_IN_ONE_PRESETS.find((p) => p.name === 'BrewZilla 65L Gen 4')!;
		expect(bz65.capacityLiters).toBe(65);
		expect(bz65.mashTunDeadSpaceLiters).toBe(6.0);
		expect(bz65.boilOffRatePerHour).toBe(4.5);
		expect(bz65.trubLossLiters).toBe(3.5);
	});

	it('configures exact factory specifications for Speidel, Brewtools, and Anvil models', () => {
		const speidel = ALL_IN_ONE_PRESETS.find((p) => p.name === 'Speidel Braumeister 20L')!;
		expect(speidel.capacityLiters).toBe(25);
		expect(speidel.mashTunDeadSpaceLiters).toBe(2.5);
		expect(speidel.boilOffRatePerHour).toBe(2.5);
		expect(speidel.trubLossLiters).toBe(1.5);

		const brewtools = ALL_IN_ONE_PRESETS.find((p) => p.name === 'Brewtools B40pro')!;
		expect(brewtools.capacityLiters).toBe(46);
		expect(brewtools.mashTunDeadSpaceLiters).toBe(3.0);
		expect(brewtools.boilOffRatePerHour).toBe(3.5);
		expect(brewtools.trubLossLiters).toBe(2.0);

		const anvil = ALL_IN_ONE_PRESETS.find((p) => p.name === 'Anvil Foundry 10.5')!;
		expect(anvil.capacityLiters).toBe(40);
		expect(anvil.mashTunDeadSpaceLiters).toBe(2.5);
		expect(anvil.boilOffRatePerHour).toBe(3.0);
		expect(anvil.trubLossLiters).toBe(1.5);
	});

	it('accurately converts metric capacity to US Gallons for imperial settings', () => {
		const g30 = ALL_IN_ONE_PRESETS.find((p) => p.name === 'Grainfather G30')!;
		const galG30 = parseFloat(litersToGallons(g30.capacityLiters).toFixed(1));
		expect(galG30).toBeCloseTo(7.9, 1);

		const g70 = ALL_IN_ONE_PRESETS.find((p) => p.name === 'Grainfather G70')!;
		const galG70 = parseFloat(litersToGallons(g70.capacityLiters).toFixed(1));
		expect(galG70).toBeCloseTo(18.5, 1);
	});
});
