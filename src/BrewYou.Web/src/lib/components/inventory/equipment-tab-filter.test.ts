import { describe, it, expect } from 'vitest';
import { sortEquipmentByName } from '$lib/utils/equipment-order';
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
	MashTun: 35,
	LauterTun: 35,
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
	GenericSensor: 0,
	StandardTap: 0,
	RoundDisplayTap: 0,
	RectDisplayTap: 0
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

describe('Equipment Tab Brewery Setup Filter and Cross-Setup Hints', () => {
	function resolveOtherSetupWithEquipment(
		currentEquipmentCount: number,
		setups: Array<{ id: string; name: string; equipmentCount?: number }>,
		activeSetupId: string
	) {
		if (currentEquipmentCount > 0 || setups.length <= 1) return null;
		return setups.find((s) => s.id !== activeSetupId && (s.equipmentCount ?? 0) > 0) ?? null;
	}

	function resolveSetupIdFilter(activeSetupId: string, defaultId: string) {
		return activeSetupId && activeSetupId !== defaultId ? activeSetupId : undefined;
	}

	it('returns null when current active setup already has equipment', () => {
		const setups = [
			{ id: 'setup-1', name: 'My brewery', equipmentCount: 2 },
			{ id: 'setup-2', name: 'Bryggan', equipmentCount: 3 }
		];
		const result = resolveOtherSetupWithEquipment(2, setups, 'setup-1');
		expect(result).toBeNull();
	});

	it('returns null when there is only one setup even if empty', () => {
		const setups = [{ id: 'setup-1', name: 'My brewery', equipmentCount: 0 }];
		const result = resolveOtherSetupWithEquipment(0, setups, 'setup-1');
		expect(result).toBeNull();
	});

	it('identifies other setup with equipment when current setup is empty', () => {
		const setups = [
			{ id: 'setup-old', name: 'My brewery', equipmentCount: 0 },
			{ id: 'setup-bryggan', name: 'Bryggan', equipmentCount: 3 }
		];
		const result = resolveOtherSetupWithEquipment(0, setups, 'setup-old');
		expect(result).not.toBeNull();
		expect(result?.id).toBe('setup-bryggan');
		expect(result?.name).toBe('Bryggan');
		expect(result?.equipmentCount).toBe(3);
	});

	it('resolves setupId filter correctly for api.equipment.list', () => {
		expect(resolveSetupIdFilter('default-1', 'default-1')).toBeUndefined();
		expect(resolveSetupIdFilter('setup-guid-123', 'default-1')).toBe('setup-guid-123');
	});
});

describe('Equipment Card List Alphabetical Ordering', () => {
	const sampleEquipment: EquipmentDto[] = [
		{
			id: 'eq-3',
			brewerySetupId: 'setup-1',
			name: 'Ss Brewtech 7 Gal Chronical',
			type: 'Fermenter',
			subtype: 'ConicalFermenter',
			capacity: 27,
			unit: 'Liters',
			capacityLiters: 27,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '',
			updatedAt: ''
		},
		{
			id: 'eq-1',
			brewerySetupId: 'setup-1',
			name: 'Anvil Foundry 10.5',
			type: 'Boiler',
			subtype: 'AllInOne',
			capacity: 40,
			unit: 'Liters',
			capacityLiters: 40,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '',
			updatedAt: ''
		},
		{
			id: 'eq-2',
			brewerySetupId: 'setup-1',
			name: 'Kegland Ferzilla 30L',
			type: 'Fermenter',
			subtype: 'PressureFermenter',
			capacity: 30,
			unit: 'Liters',
			capacityLiters: 30,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '',
			updatedAt: ''
		},
		{
			id: 'eq-4',
			brewerySetupId: 'setup-1',
			name: 'Corny Keg 19L #2',
			type: 'Keg',
			subtype: 'Cornelius',
			capacity: 19,
			unit: 'Liters',
			capacityLiters: 19,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '',
			updatedAt: ''
		},
		{
			id: 'eq-5',
			brewerySetupId: 'setup-1',
			name: 'Corny Keg 19L #1',
			type: 'Keg',
			subtype: 'Cornelius',
			capacity: 19,
			unit: 'Liters',
			capacityLiters: 19,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '',
			updatedAt: ''
		}
	];

	it('orders equipment card list alphabetically by name with natural numerical order', () => {
		const sorted = sortEquipmentByName(sampleEquipment);

		expect(sorted.map((e) => e.name)).toEqual([
			'Anvil Foundry 10.5',
			'Corny Keg 19L #1',
			'Corny Keg 19L #2',
			'Kegland Ferzilla 30L',
			'Ss Brewtech 7 Gal Chronical'
		]);
	});

	it('keeps items alphabetically sorted when new equipment is added', () => {
		const currentList = sortEquipmentByName(sampleEquipment);
		const newItem: EquipmentDto = {
			id: 'eq-new',
			brewerySetupId: 'setup-1',
			name: 'Braumeister 20L',
			type: 'Boiler',
			subtype: 'AllInOne',
			capacity: 25,
			unit: 'Liters',
			capacityLiters: 25,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '',
			updatedAt: ''
		};

		const updatedList = sortEquipmentByName([newItem, ...currentList]);

		expect(updatedList.map((e) => e.name)).toEqual([
			'Anvil Foundry 10.5',
			'Braumeister 20L',
			'Corny Keg 19L #1',
			'Corny Keg 19L #2',
			'Kegland Ferzilla 30L',
			'Ss Brewtech 7 Gal Chronical'
		]);
	});

	describe('Renaming tap or keg preserves attached batch and cascades name updates', () => {
		it('preserves attached batch when tap is renamed', () => {
			const initialTap: EquipmentDto = {
				id: 'tap-1',
				brewerySetupId: 'setup-1',
				name: 'Tap #1',
				type: 'Tap',
				subtype: 'StandardTap',
				capacity: 0,
				unit: 'Liters',
				capacityLiters: 0,
				currentVolume: 0,
				currentVolumeLiters: 0,
				fillPercentage: 100,
				assignedBatchId: 'batch-99',
				assignedBatch: {
					id: 'batch-99',
					batchCode: 'B-099',
					name: 'Citra Hazy IPA',
					beerStyle: 'Hazy IPA',
					actualAbv: 6.5,
					assignedAt: '2026-11-04T12:00:00Z'
				},
				createdAt: '',
				updatedAt: ''
			};

			const savedTapUpdate: EquipmentDto = {
				...initialTap,
				name: 'Main Bar Tap #1',
				updatedAt: '2026-11-04T13:00:00Z'
			};

			// Simulate handleSaved logic in equipment page
			const equipmentList: EquipmentDto[] = [initialTap];
			const index = equipmentList.findIndex((e) => e.id === savedTapUpdate.id);
			expect(index).toBe(0);

			const existing = equipmentList[index];
			const merged: EquipmentDto = {
				...savedTapUpdate,
				assignedBatch: savedTapUpdate.assignedBatch ?? existing.assignedBatch,
				assignedBatchId: savedTapUpdate.assignedBatchId ?? existing.assignedBatchId,
				assignedKeg: savedTapUpdate.assignedKeg ?? existing.assignedKeg,
				assignedKegId: savedTapUpdate.assignedKegId ?? existing.assignedKegId
			};
			equipmentList[index] = merged;

			expect(equipmentList[0].name).toBe('Main Bar Tap #1');
			expect(equipmentList[0].assignedBatchId).toBe('batch-99');
			expect(equipmentList[0].assignedBatch?.name).toBe('Citra Hazy IPA');
		});

		it('preserves attached batch on keg and cascades renamed keg to assigned tap', () => {
			const initialKeg: EquipmentDto = {
				id: 'keg-1',
				brewerySetupId: 'setup-1',
				name: 'Keg Alpha',
				type: 'Keg',
				subtype: 'Cornelius',
				capacity: 19,
				unit: 'Liters',
				capacityLiters: 19,
				currentVolume: 18.5,
				currentVolumeLiters: 18.5,
				fillPercentage: 97.4,
				assignedBatchId: 'batch-101',
				assignedBatch: {
					id: 'batch-101',
					batchCode: 'B-101',
					name: 'Nelson Sauvin Pils',
					beerStyle: 'German Pils',
					actualAbv: 5.1,
					assignedAt: '2026-11-04T10:00:00Z'
				},
				createdAt: '',
				updatedAt: ''
			};

			const initialTap: EquipmentDto = {
				id: 'tap-2',
				brewerySetupId: 'setup-1',
				name: 'Tap #2',
				type: 'Tap',
				subtype: 'StandardTap',
				capacity: 0,
				unit: 'Liters',
				capacityLiters: 0,
				currentVolume: 0,
				currentVolumeLiters: 0,
				fillPercentage: 97.4,
				assignedKegId: 'keg-1',
				assignedKeg: {
					id: 'keg-1',
					name: 'Keg Alpha',
					subtype: 'Cornelius',
					capacityLiters: 19,
					currentVolumeLiters: 18.5,
					unit: 'Liters',
					fillPercentage: 97.4,
					assignedBatch: initialKeg.assignedBatch
				},
				assignedBatch: initialKeg.assignedBatch,
				createdAt: '',
				updatedAt: ''
			};

			let equipmentList: EquipmentDto[] = [initialKeg, initialTap];

			// Keg renamed to "Keg Beta"
			const savedKegUpdate: EquipmentDto = {
				...initialKeg,
				name: 'Keg Beta',
				updatedAt: '2026-11-04T14:00:00Z'
			};

			// Simulate handleSaved
			const kegIdx = equipmentList.findIndex((e) => e.id === savedKegUpdate.id);
			const existingKeg = equipmentList[kegIdx];
			equipmentList[kegIdx] = {
				...savedKegUpdate,
				assignedBatch: savedKegUpdate.assignedBatch ?? existingKeg.assignedBatch,
				assignedBatchId: savedKegUpdate.assignedBatchId ?? existingKeg.assignedBatchId,
				assignedKeg: savedKegUpdate.assignedKeg ?? existingKeg.assignedKeg,
				assignedKegId: savedKegUpdate.assignedKegId ?? existingKeg.assignedKegId
			};

			if (savedKegUpdate.type === 'Keg') {
				equipmentList = equipmentList.map((item) => {
					if (
						item.type === 'Tap' &&
						(item.assignedKegId === savedKegUpdate.id || item.assignedKeg?.id === savedKegUpdate.id)
					) {
						const fillPct =
							savedKegUpdate.fillLevelPercent ??
							savedKegUpdate.fillPercentage ??
							(savedKegUpdate.capacityLiters > 0
								? Math.round(
										(savedKegUpdate.currentVolumeLiters / savedKegUpdate.capacityLiters) * 100
									)
								: 0);

						return {
							...item,
							assignedKegId: savedKegUpdate.id,
							assignedKeg: {
								id: savedKegUpdate.id,
								name: savedKegUpdate.name,
								subtype: savedKegUpdate.subtype,
								capacityLiters: savedKegUpdate.capacityLiters,
								currentVolumeLiters: savedKegUpdate.currentVolumeLiters,
								unit: savedKegUpdate.unit,
								fillPercentage: fillPct,
								assignedBatch: savedKegUpdate.assignedBatch,
								currentTemperatureC: savedKegUpdate.currentTemperatureC,
								temperatureUpdatedAt: savedKegUpdate.temperatureUpdatedAt
							},
							assignedBatch: savedKegUpdate.assignedBatch ?? item.assignedBatch,
							fillLevelPercent: fillPct
						};
					}
					return item;
				});
			}

			// Verify Keg itself
			expect(equipmentList[0].name).toBe('Keg Beta');
			expect(equipmentList[0].assignedBatchId).toBe('batch-101');
			expect(equipmentList[0].assignedBatch?.name).toBe('Nelson Sauvin Pils');

			// Verify Tap connected to the Keg
			expect(equipmentList[1].assignedKegId).toBe('keg-1');
			expect(equipmentList[1].assignedKeg?.name).toBe('Keg Beta');
			expect(equipmentList[1].assignedBatch?.name).toBe('Nelson Sauvin Pils');
		});
	});
});
