import { beforeEach, describe, expect, it } from 'vitest';
import { render } from 'svelte/server';
import EquipmentHealthWidget from './EquipmentHealthWidget.svelte';
import { getOccupiedEquipmentCount, isEquipmentOccupied } from '$lib/utils/batchEquipment';
import { dashboard } from '$lib/stores/dashboard.svelte';
import type { BatchSummaryDto, EquipmentDto } from '$lib/types/api';

describe('EquipmentHealthWidget', () => {
	beforeEach(() => {
		dashboard.batches = [];
		dashboard.equipment = [];
	});

	it('renders accessible equipment link label and stage-aware vessel availability', () => {
		const boiler: EquipmentDto = {
			id: 'boiler-1',
			brewerySetupId: 's1',
			name: 'G30 Boiler',
			type: 'Boiler',
			subtype: 'AllInOne',
			capacity: 30,
			unit: 'Liters',
			capacityLiters: 30,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '',
			updatedAt: ''
		};
		const fermenter: EquipmentDto = {
			id: 'ferm-1',
			brewerySetupId: 's1',
			name: 'Conical 1',
			type: 'Fermenter',
			subtype: 'ConicalFermenter',
			capacity: 30,
			unit: 'Liters',
			capacityLiters: 30,
			currentVolume: 20,
			currentVolumeLiters: 20,
			fillPercentage: 67,
			createdAt: '',
			updatedAt: ''
		};
		const fermentBatch: BatchSummaryDto = {
			id: 'b-1',
			batchCode: 'B-001',
			name: 'IPA',
			beerStyle: 'IPA',
			status: 'Fermenting',
			currentStage: 'Ferment',
			brewDate: '2026-11-28',
			daysActive: 2,
			targetOg: 1.06,
			targetFg: 1.012,
			colorSrm: 6,
			targetBatchSizeLiters: 20,
			boilerId: boiler.id,
			fermenterId: fermenter.id,
			createdAt: '',
			updatedAt: ''
		};

		dashboard.equipment = [boiler, fermenter];
		dashboard.batches = [fermentBatch];

		expect(isEquipmentOccupied(boiler.id, [fermentBatch])).toBe(false);
		expect(isEquipmentOccupied(fermenter.id, [fermentBatch])).toBe(true);
		expect(getOccupiedEquipmentCount([boiler, fermenter], [fermentBatch])).toBe(1);

		const { body } = render(EquipmentHealthWidget);
		expect(body).toContain('href="/inventory/equipment"');
		expect(body).toContain('aria-label="View all equipment"');
		expect(body).toContain('G30 Boiler');
		expect(body).toContain('Conical 1');
	});
});
