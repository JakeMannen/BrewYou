import { describe, it, expect } from 'vitest';
import { litersToGallons } from '$lib/stores/settings.svelte';
import { formatNumber } from '$lib/utils/formatNumber';
import type { EquipmentDto, VolumeUnit } from '$lib/types/api';

function formatDisplayMetrics(e: EquipmentDto, volumeUnitPref: VolumeUnit) {
	const pref = volumeUnitPref;
	const fillPct = typeof e.fillPercentage === 'number' ? Math.round(e.fillPercentage) : 0;
	if (pref === 'Gallons') {
		const capGal = litersToGallons(e.capacityLiters);
		const curGal = litersToGallons(e.currentVolumeLiters);
		return {
			currentFormatted: `${formatNumber(curGal, 1)} gal`,
			capacityFormatted: `${formatNumber(capGal, 1)} gal`,
			primaryVal: `${formatNumber(curGal, 1)} / ${formatNumber(capGal, 1)} gal`,
			fillPct,
			unit: 'gal'
		};
	} else {
		return {
			currentFormatted: `${formatNumber(e.currentVolumeLiters, 1)} L`,
			capacityFormatted: `${formatNumber(e.capacityLiters, 1)} L`,
			primaryVal: `${formatNumber(e.currentVolumeLiters, 1)} / ${formatNumber(e.capacityLiters, 1)} L`,
			fillPct,
			unit: 'L'
		};
	}
}

describe('Equipment Card Single-Unit Display', () => {
	const sampleEquipment: EquipmentDto = {
		id: 'eq-123',
		brewerySetupId: 'setup-1',
		name: 'Grainfather G40',
		type: 'Boiler',
		subtype: 'AllInOne',
		capacity: 40,
		unit: 'Liters',
		capacityLiters: 40,
		currentVolume: 20,
		currentVolumeLiters: 20,
		fillPercentage: 50,
		description: 'Main brewing rig',
		notes: null,
		createdAt: '2026-09-01T00:00:00Z',
		updatedAt: '2026-09-01T00:00:00Z'
	};

	it('formats strictly in Liters when user preference is Liters, without dual-unit string', () => {
		const metrics = formatDisplayMetrics(sampleEquipment, 'Liters');
		expect(metrics.currentFormatted).toBe('20.0 L');
		expect(metrics.capacityFormatted).toBe('40.0 L');
		expect(metrics.primaryVal).toBe('20.0 / 40.0 L');
		expect(metrics.unit).toBe('L');
		expect(metrics.fillPct).toBe(50);
		// Check that no secondaryVal or dual-unit exists in the object
		expect(metrics).not.toHaveProperty('secondaryVal');
	});

	it('formats strictly in Gallons when user preference is Gallons, without dual-unit string', () => {
		const metrics = formatDisplayMetrics(sampleEquipment, 'Gallons');
		expect(metrics.unit).toBe('gal');
		expect(metrics.currentFormatted).toContain('gal');
		expect(metrics.capacityFormatted).toContain('gal');
		expect(metrics.primaryVal).toContain('gal');
		expect(metrics.primaryVal).not.toContain('L');
		expect(metrics).not.toHaveProperty('secondaryVal');
	});
});
