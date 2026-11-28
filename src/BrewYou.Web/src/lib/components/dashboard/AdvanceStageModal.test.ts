import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render } from 'svelte/server';
import AdvanceStageModal from './AdvanceStageModal.svelte';
import { dashboard } from '$lib/stores/dashboard.svelte';
import { settings } from '$lib/stores/settings.svelte';
import type { BatchSummaryDto, EquipmentDto } from '$lib/types/api';

describe('AdvanceStageModal', () => {
	beforeEach(() => {
		dashboard.batches = [];
		dashboard.equipment = [];
		settings.temperatureUnit = 'Celsius';
		settings.volumeUnit = 'Liters';
	});

	it('renders optional fermenter, sensor, and unit-aware pitch temp inputs when advancing Boil to Ferment', () => {
		settings.temperatureUnit = 'Fahrenheit';
		const ferm: EquipmentDto = {
			id: 'ferm-1',
			brewerySetupId: 's1',
			name: 'Unitank 30L',
			type: 'Fermenter',
			subtype: 'ConicalFermenter',
			capacity: 30,
			unit: 'Liters',
			capacityLiters: 30,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '',
			updatedAt: ''
		};
		const sensor: EquipmentDto = {
			id: 'sensor-1',
			brewerySetupId: 's1',
			name: 'iSpindel Blue',
			type: 'Sensor',
			subtype: 'ISpindel',
			capacity: 0,
			unit: 'Liters',
			capacityLiters: 0,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '',
			updatedAt: ''
		};
		const boilingBatch: BatchSummaryDto = {
			id: 'b-boil',
			batchCode: 'B-010',
			name: 'West Coast IPA',
			beerStyle: 'American IPA',
			status: 'Brewing',
			currentStage: 'Boil',
			brewDate: '2026-11-28',
			daysActive: 0,
			targetOg: 1.064,
			targetFg: 1.011,
			colorSrm: 7,
			targetBatchSizeLiters: 20,
			createdAt: '',
			updatedAt: ''
		};

		dashboard.equipment = [ferm, sensor];
		dashboard.batches = [boilingBatch];

		const { body } = render(AdvanceStageModal, {
			props: {
				isOpen: true,
				batch: boilingBatch,
				onClose: vi.fn()
			}
		});

		expect(body).toContain('aria-labelledby="advance-modal-title"');
		expect(body).toContain('data-testid="dashboard-advance-fermenter-select"');
		expect(body).toContain('data-testid="dashboard-advance-sensor-select"');
		expect(body).toContain('data-testid="dashboard-advance-pitch-temp-input"');
		expect(body).toContain('Unitank 30L');
		expect(body).toContain('iSpindel Blue');
		expect(body).toContain('°F');
	});
});
