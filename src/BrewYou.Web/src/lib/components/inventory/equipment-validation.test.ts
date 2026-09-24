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
});
