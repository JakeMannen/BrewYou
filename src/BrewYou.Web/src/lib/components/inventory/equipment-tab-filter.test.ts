import { describe, it, expect } from 'vitest';
import {
	type EquipmentType,
	type EquipmentSubtype,
	type EquipmentDto,
	type VolumeUnit,
	DEFAULT_SUBTYPE_BY_TYPE
} from '$lib/types/api';
import { litersToGallons } from '$lib/stores/settings.svelte';

const DEFAULT_CAPACITIES_LITERS: Record<EquipmentSubtype, number> = {
	AllInOne: 35,
	Pan: 50,
	Hlt: 30,
	HermsRims: 15,
	Bucket: 30,
	ConicalFermenter: 27,
	Carboy: 20,
	PressureFermenter: 30,
	StainlessBucket: 26,
	Cornelius: 19,
	Minikeg: 5,
	MiniBarrel: 10,
	PetKeg: 8,
	Other: 20,
	ISpindel: 0,
	Tilt: 0,
	GenericSensor: 0
};

function resolveInitialEquipmentState(
	selectedCategory: string,
	preferredUnit: VolumeUnit = 'Liters'
) {
	const initialCategory: EquipmentType =
		selectedCategory !== 'All' ? (selectedCategory as EquipmentType) : 'Boiler';
	const initialSubtype: EquipmentSubtype = DEFAULT_SUBTYPE_BY_TYPE[initialCategory] ?? 'AllInOne';
	const defLiters = DEFAULT_CAPACITIES_LITERS[initialSubtype] ?? 20;
	const capacity =
		preferredUnit === 'Gallons' ? parseFloat(litersToGallons(defLiters).toFixed(1)) : defLiters;

	return {
		type: initialCategory,
		subtype: initialSubtype,
		capacity,
		currentVolume: 0,
		unit: preferredUnit
	};
}

function handleSavedEquipmentList(
	existingList: EquipmentDto[],
	saved: EquipmentDto,
	selectedCategory: string
): EquipmentDto[] {
	const index = existingList.findIndex((e) => e.id === saved.id);
	if (index >= 0) {
		if (selectedCategory === 'All' || selectedCategory === saved.type) {
			const updated = [...existingList];
			updated[index] = saved;
			return updated;
		} else {
			return existingList.filter((e) => e.id !== saved.id);
		}
	} else {
		if (selectedCategory === 'All' || selectedCategory === saved.type) {
			return [saved, ...existingList];
		}
		return existingList;
	}
}

describe('Equipment Modal Default Category from Active Tab', () => {
	it('automatically selects Fermenter when adding from the Fermenters tab', () => {
		const state = resolveInitialEquipmentState('Fermenter');
		expect(state.type).toBe('Fermenter');
		expect(state.subtype).toBe('Bucket');
		expect(state.capacity).toBe(30);
	});

	it('automatically selects Keg when adding from the Kegs tab', () => {
		const state = resolveInitialEquipmentState('Keg');
		expect(state.type).toBe('Keg');
		expect(state.subtype).toBe('Cornelius');
		expect(state.capacity).toBe(19);
	});

	it('automatically selects Boiler when adding from the Boilers tab', () => {
		const state = resolveInitialEquipmentState('Boiler');
		expect(state.type).toBe('Boiler');
		expect(state.subtype).toBe('AllInOne');
		expect(state.capacity).toBe(35);
	});

	it('automatically selects Sensor when adding from the Sensors tab', () => {
		const state = resolveInitialEquipmentState('Sensor');
		expect(state.type).toBe('Sensor');
		expect(state.subtype).toBe('GenericSensor');
		expect(state.capacity).toBe(0);
	});

	it('automatically selects Other when adding from the Other tab', () => {
		const state = resolveInitialEquipmentState('Other');
		expect(state.type).toBe('Other');
		expect(state.subtype).toBe('Other');
		expect(state.capacity).toBe(20);
	});

	it('falls back to Boiler when adding from the All tab', () => {
		const state = resolveInitialEquipmentState('All');
		expect(state.type).toBe('Boiler');
		expect(state.subtype).toBe('AllInOne');
		expect(state.capacity).toBe(35);
	});

	it('correctly converts default capacity when user volume unit is Gallons', () => {
		const fermenterGal = resolveInitialEquipmentState('Fermenter', 'Gallons');
		expect(fermenterGal.type).toBe('Fermenter');
		expect(fermenterGal.unit).toBe('Gallons');
		expect(fermenterGal.capacity).toBe(parseFloat(litersToGallons(30).toFixed(1)));

		const kegGal = resolveInitialEquipmentState('Keg', 'Gallons');
		expect(kegGal.type).toBe('Keg');
		expect(kegGal.unit).toBe('Gallons');
		expect(kegGal.capacity).toBe(parseFloat(litersToGallons(19).toFixed(1)));
	});
});

describe('Equipment Tab List Update On Save', () => {
	const existingList: EquipmentDto[] = [
		{
			id: 'eq-f1',
			brewerySetupId: 'setup-1',
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
		}
	];

	it('prepends new Fermenter to list when Fermenter tab is active', () => {
		const newFermenter: EquipmentDto = {
			id: 'eq-f2',
			brewerySetupId: 'setup-1',
			name: 'Secondary Conical',
			type: 'Fermenter',
			subtype: 'ConicalFermenter',
			capacity: 27,
			unit: 'Liters',
			capacityLiters: 27,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '2026-01-02T00:00:00Z',
			updatedAt: '2026-01-02T00:00:00Z'
		};

		const result = handleSavedEquipmentList(existingList, newFermenter, 'Fermenter');
		expect(result).toHaveLength(2);
		expect(result[0].id).toBe('eq-f2');
	});

	it('does not prepend non-matching item when specific tab is active', () => {
		const newBoiler: EquipmentDto = {
			id: 'eq-b1',
			brewerySetupId: 'setup-1',
			name: 'Brew Kettle',
			type: 'Boiler',
			subtype: 'Pan',
			capacity: 50,
			unit: 'Liters',
			capacityLiters: 50,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '2026-01-02T00:00:00Z',
			updatedAt: '2026-01-02T00:00:00Z'
		};

		const result = handleSavedEquipmentList(existingList, newBoiler, 'Fermenter');
		expect(result).toHaveLength(1);
		expect(result[0].id).toBe('eq-f1');
	});

	it('prepends any equipment type when All tab is active', () => {
		const newBoiler: EquipmentDto = {
			id: 'eq-b1',
			brewerySetupId: 'setup-1',
			name: 'Brew Kettle',
			type: 'Boiler',
			subtype: 'Pan',
			capacity: 50,
			unit: 'Liters',
			capacityLiters: 50,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '2026-01-02T00:00:00Z',
			updatedAt: '2026-01-02T00:00:00Z'
		};

		const result = handleSavedEquipmentList(existingList, newBoiler, 'All');
		expect(result).toHaveLength(2);
		expect(result[0].id).toBe('eq-b1');
	});
});

describe('Batch Equipment & Sensor Filtering', () => {
	const sampleEquipment: EquipmentDto[] = [
		{
			id: 'boiler-1',
			brewerySetupId: 'setup-1',
			name: 'Brew Kettle',
			type: 'Boiler',
			subtype: 'Pan',
			capacity: 50,
			unit: 'Liters',
			capacityLiters: 50,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		},
		{
			id: 'fermenter-1',
			brewerySetupId: 'setup-1',
			name: 'Conical Fermenter',
			type: 'Fermenter',
			subtype: 'ConicalFermenter',
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
			id: 'keg-1',
			brewerySetupId: 'setup-1',
			name: 'Corny Keg 19L',
			type: 'Keg',
			subtype: 'Cornelius',
			capacity: 19,
			unit: 'Liters',
			capacityLiters: 19,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		},
		{
			id: 'other-vessel-1',
			brewerySetupId: 'setup-1',
			name: 'Serving Vessel',
			type: 'Other',
			subtype: 'Other',
			capacity: 20,
			unit: 'Liters',
			capacityLiters: 20,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		},
		{
			id: 'ispindel-1',
			brewerySetupId: 'setup-1',
			name: 'Fermenting iSpindel',
			type: 'Other',
			subtype: 'ISpindel',
			capacity: 0,
			unit: 'Liters',
			capacityLiters: 0,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		},
		{
			id: 'tilt-1',
			brewerySetupId: 'setup-1',
			name: 'Red Tilt',
			type: 'Other',
			subtype: 'Tilt',
			capacity: 0,
			unit: 'Liters',
			capacityLiters: 0,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		},
		{
			id: 'generic-sensor-1',
			brewerySetupId: 'setup-1',
			name: 'Thermowell Probe',
			type: 'Other',
			subtype: 'GenericSensor',
			capacity: 0,
			unit: 'Liters',
			capacityLiters: 0,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		}
	];

	it('packaging vessels filter excludes sensors and includes fillable vessels', async () => {
		const { isVesselFillable } = await import('$lib/components/inventory/vessel-geometry');

		const packagingVessels = sampleEquipment.filter(
			(e) => e.type === 'Keg' || (e.type === 'Other' && isVesselFillable(e.subtype))
		);

		const ids = packagingVessels.map((v) => v.id);
		expect(ids).toContain('keg-1');
		expect(ids).toContain('other-vessel-1');
		expect(ids).not.toContain('ispindel-1');
		expect(ids).not.toContain('tilt-1');
		expect(ids).not.toContain('generic-sensor-1');
	});

	it('fermentation sensors filter captures only non-fillable sensors', async () => {
		const { isVesselFillable } = await import('$lib/components/inventory/vessel-geometry');

		const sensors = sampleEquipment.filter((e) => !isVesselFillable(e.subtype));

		const ids = sensors.map((s) => s.id);
		expect(ids).toEqual(['ispindel-1', 'tilt-1', 'generic-sensor-1']);
		expect(ids).not.toContain('keg-1');
		expect(ids).not.toContain('other-vessel-1');
		expect(ids).not.toContain('fermenter-1');
		expect(ids).not.toContain('boiler-1');
	});
});
