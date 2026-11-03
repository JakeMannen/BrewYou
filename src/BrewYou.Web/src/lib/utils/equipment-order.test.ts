import { describe, it, expect } from 'vitest';
import { sortEquipmentByName } from './equipment-order';

describe('sortEquipmentByName', () => {
	it('sorts equipment items alphabetically by name', () => {
		const items = [
			{ id: '1', name: 'Grainfather G40' },
			{ id: '2', name: 'Braumeister 20L' },
			{ id: '3', name: 'Ss Brewtech Unitank' },
			{ id: '4', name: 'Anvil Foundry' }
		];

		const sorted = sortEquipmentByName(items);

		expect(sorted.map((i) => i.name)).toEqual([
			'Anvil Foundry',
			'Braumeister 20L',
			'Grainfather G40',
			'Ss Brewtech Unitank'
		]);
	});

	it('orders numeric suffixes naturally (e.g. Tap 1, Tap 2, Tap 10)', () => {
		const taps = [
			{ id: 't10', name: 'Tap 10' },
			{ id: 't1', name: 'Tap 1' },
			{ id: 't20', name: 'Tap 20' },
			{ id: 't2', name: 'Tap 2' },
			{ id: 't3', name: 'Tap 3' }
		];

		const sorted = sortEquipmentByName(taps);

		expect(sorted.map((t) => t.name)).toEqual(['Tap 1', 'Tap 2', 'Tap 3', 'Tap 10', 'Tap 20']);
	});

	it('sorts case-insensitively', () => {
		const items = [
			{ id: '1', name: 'chiller' },
			{ id: '2', name: 'Boiler' },
			{ id: '3', name: 'airlock' },
			{ id: '4', name: 'Fermenter' }
		];

		const sorted = sortEquipmentByName(items);

		expect(sorted.map((i) => i.name)).toEqual(['airlock', 'Boiler', 'chiller', 'Fermenter']);
	});

	it('handles empty arrays and single items gracefully', () => {
		expect(sortEquipmentByName([])).toEqual([]);
		expect(sortEquipmentByName([{ id: '1', name: 'Single Tap' }])).toEqual([
			{ id: '1', name: 'Single Tap' }
		]);
	});

	it('falls back to id ordering when names are identical', () => {
		const items = [
			{ id: 'b', name: 'Identical Vessel' },
			{ id: 'a', name: 'Identical Vessel' }
		];

		const sorted = sortEquipmentByName(items);

		expect(sorted.map((i) => i.id)).toEqual(['a', 'b']);
	});

	it('does not mutate the original array', () => {
		const original = [
			{ id: '2', name: 'Zeta' },
			{ id: '1', name: 'Alpha' }
		];
		const copy = [...original];

		const sorted = sortEquipmentByName(original);

		expect(original).toEqual(copy);
		expect(sorted[0].name).toBe('Alpha');
		expect(sorted[1].name).toBe('Zeta');
	});
});
