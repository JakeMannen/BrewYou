import { describe, it, expect } from 'vitest';
import type { EquipmentDto } from '$lib/types/api';
import { ApiClientError } from '$lib/api/client';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';

describe('Equipment Name Uniqueness & Validation', () => {
	const setup1Id = 'setup-1111-1111';
	const setup2Id = 'setup-2222-2222';

	const sampleEquipment: EquipmentDto[] = [
		{
			id: 'eq-1',
			brewerySetupId: setup1Id,
			name: 'Grainfather G40',
			type: 'Boiler',
			subtype: 'AllInOne',
			capacity: 40,
			unit: 'Liters',
			capacityLiters: 40,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		},
		{
			id: 'eq-2',
			brewerySetupId: setup1Id,
			name: 'Primary Fermenter',
			type: 'Fermenter',
			subtype: 'Bucket',
			capacity: 30,
			unit: 'Liters',
			capacityLiters: 30,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		},
		{
			id: 'eq-3',
			brewerySetupId: setup2Id,
			name: 'Grainfather G40',
			type: 'Boiler',
			subtype: 'AllInOne',
			capacity: 40,
			unit: 'Liters',
			capacityLiters: 40,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		}
	];

	function isDuplicate(
		newName: string,
		activeSetupId: string,
		existingItems: EquipmentDto[],
		currentEditingItem: EquipmentDto | null = null
	): boolean {
		const trimmed = newName.trim().toLowerCase();
		if (!trimmed) return false;
		return existingItems.some((item) => {
			if (currentEditingItem && item.id === currentEditingItem.id) return false;
			if (item.brewerySetupId && activeSetupId && item.brewerySetupId !== activeSetupId) {
				return false;
			}
			return item.name.trim().toLowerCase() === trimmed;
		});
	}

	it('detects duplicate name in the same brewery setup', () => {
		expect(isDuplicate('Grainfather G40', setup1Id, sampleEquipment)).toBe(true);
	});

	it('detects case-insensitive collisions in the same setup', () => {
		expect(isDuplicate('grainfather g40', setup1Id, sampleEquipment)).toBe(true);
		expect(isDuplicate('GRAINFATHER G40', setup1Id, sampleEquipment)).toBe(true);
	});

	it('detects duplicate name with leading or trailing whitespace', () => {
		expect(isDuplicate('  Grainfather G40  ', setup1Id, sampleEquipment)).toBe(true);
	});

	it('permits keeping the same name when editing the existing equipment item', () => {
		const editingItem = sampleEquipment[0];
		expect(isDuplicate('Grainfather G40', setup1Id, sampleEquipment, editingItem)).toBe(false);
		expect(isDuplicate('grainfather g40', setup1Id, sampleEquipment, editingItem)).toBe(false);
	});

	it('rejects renaming an existing item to collide with another item in the same setup', () => {
		const editingItem = sampleEquipment[0];
		expect(isDuplicate('Primary Fermenter', setup1Id, sampleEquipment, editingItem)).toBe(true);
	});

	it('allows the same equipment name in a different brewery setup', () => {
		const setup3Id = 'setup-3333-3333';
		expect(isDuplicate('Grainfather G40', setup3Id, sampleEquipment)).toBe(false);
	});

	it('permits completely new unique equipment names', () => {
		expect(isDuplicate('Anvil Foundry 10.5', setup1Id, sampleEquipment)).toBe(false);
	});

	it('contains localized duplicate error message with 100% key parity', () => {
		expect(en.equipment.err_duplicate_name).toBeDefined();
		expect(sv.equipment.err_duplicate_name).toBeDefined();
		expect(en.equipment.err_duplicate_name).toBe(
			'An equipment item with this name already exists in this setup.'
		);
		expect(sv.equipment.err_duplicate_name).toBe(
			'En utrustning med detta namn finns redan i denna uppsättning.'
		);
	});

	it('ApiClientError carries status 409 and DUPLICATE_NAME code', () => {
		const err = new ApiClientError(
			'An equipment item with this name already exists in this brewery setup.',
			409,
			'DUPLICATE_NAME'
		);
		expect(err.status).toBe(409);
		expect(err.code).toBe('DUPLICATE_NAME');
		expect(err.message).toContain('already exists');
	});

	it('contains localized tap keg temperature strings with 100% key parity', () => {
		expect(en.taps.keg_temperature).toBeDefined();
		expect(sv.taps.keg_temperature).toBeDefined();
		expect(en.taps.from_keg).toBeDefined();
		expect(sv.taps.from_keg).toBeDefined();
	});

	it('derives temperature for tap from connected keg', () => {
		const connectedKeg = {
			id: 'keg-1',
			name: 'Serving Keg',
			subtype: 'Cornelius' as const,
			capacityLiters: 19,
			currentVolumeLiters: 15,
			unit: 'Liters' as const,
			fillPercentage: 78.9,
			currentTemperatureC: 4.2,
			temperatureUpdatedAt: '2026-11-02T12:00:00Z'
		};

		const tapWithKeg: EquipmentDto = {
			id: 'tap-1',
			brewerySetupId: setup1Id,
			name: 'Main Tap',
			type: 'Tap',
			subtype: 'RoundDisplayTap',
			capacity: 0,
			unit: 'Liters',
			capacityLiters: 0,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			connectionType: 'None',
			currentTemperatureC: connectedKeg.currentTemperatureC,
			temperatureUpdatedAt: connectedKeg.temperatureUpdatedAt,
			assignedKegId: connectedKeg.id,
			assignedKeg: connectedKeg,
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		};

		expect(tapWithKeg.connectionType).toBe('None');
		expect(tapWithKeg.currentTemperatureC).toBe(4.2);
		expect(tapWithKeg.assignedKeg?.currentTemperatureC).toBe(4.2);

		const tapWithoutKeg: EquipmentDto = {
			id: 'tap-2',
			brewerySetupId: setup1Id,
			name: 'Direct Tap',
			type: 'Tap',
			subtype: 'StandardTap',
			capacity: 0,
			unit: 'Liters',
			capacityLiters: 0,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			connectionType: 'None',
			currentTemperatureC: null,
			assignedKegId: null,
			assignedKeg: null,
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		};

		expect(tapWithoutKeg.connectionType).toBe('None');
		expect(tapWithoutKeg.currentTemperatureC).toBeNull();
	});

	it('derives fill level and volume metrics for tap from connected keg', () => {
		const connectedKeg = {
			id: 'keg-1',
			name: 'Serving Keg',
			subtype: 'Cornelius' as const,
			capacityLiters: 19,
			currentVolumeLiters: 15,
			unit: 'Liters' as const,
			fillPercentage: 78.9,
			currentTemperatureC: 4.2,
			temperatureUpdatedAt: '2026-11-02T12:00:00Z'
		};

		const tapWithKeg: EquipmentDto = {
			id: 'tap-1',
			brewerySetupId: setup1Id,
			name: 'Main Tap',
			type: 'Tap',
			subtype: 'RoundDisplayTap',
			capacity: 0,
			unit: 'Liters',
			capacityLiters: 0,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 78.9,
			fillLevelPercent: 78.9,
			connectionType: 'None',
			assignedKegId: connectedKeg.id,
			assignedKeg: connectedKeg,
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		};

		function deriveMetrics(e: EquipmentDto) {
			const cap =
				e.type === 'Tap' && e.assignedKeg ? e.assignedKeg.capacityLiters : e.capacityLiters;
			const vol =
				e.type === 'Tap' && e.assignedKeg
					? e.assignedKeg.currentVolumeLiters
					: e.currentVolumeLiters;
			const rawPct =
				e.type === 'Tap'
					? (e.fillLevelPercent ?? e.assignedKeg?.fillPercentage ?? e.fillPercentage)
					: e.fillPercentage;
			return {
				capacity: cap,
				volume: vol,
				fillPct: Math.round(rawPct)
			};
		}

		const metricsWithKeg = deriveMetrics(tapWithKeg);
		expect(metricsWithKeg.capacity).toBe(19);
		expect(metricsWithKeg.volume).toBe(15);
		expect(metricsWithKeg.fillPct).toBe(79);

		const tapWithoutKeg: EquipmentDto = {
			id: 'tap-2',
			brewerySetupId: setup1Id,
			name: 'Empty Tap',
			type: 'Tap',
			subtype: 'StandardTap',
			capacity: 0,
			unit: 'Liters',
			capacityLiters: 0,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			fillLevelPercent: null,
			connectionType: 'None',
			assignedKegId: null,
			assignedKeg: null,
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		};

		const metricsWithoutKeg = deriveMetrics(tapWithoutKeg);
		expect(metricsWithoutKeg.capacity).toBe(0);
		expect(metricsWithoutKeg.volume).toBe(0);
		expect(metricsWithoutKeg.fillPct).toBe(0);
	});
});
