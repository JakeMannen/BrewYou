import { describe, it, expect } from 'vitest';
import type { EquipmentDto, EquipmentType, EquipmentSubtype, VolumeUnit } from '$lib/types/api';

function getParsedAttributes(item: EquipmentDto): Array<{ key: string; value: string }> {
	if (!item.latestMetricsJson) return [];
	try {
		const parsed = JSON.parse(item.latestMetricsJson);
		if (typeof parsed !== 'object' || parsed === null) return [];
		const result: Array<{ key: string; value: string }> = [];
		for (const [k, v] of Object.entries(parsed)) {
			let formattedVal = '';
			if (typeof v === 'object' && v !== null) {
				formattedVal = JSON.stringify(v);
			} else {
				formattedVal = String(v);
			}
			result.push({ key: k, value: formattedVal });
		}
		return result;
	} catch {
		return [];
	}
}

describe('Equipment Telemetry Attributes Parsing', () => {
	const baseEquipment: EquipmentDto = {
		id: 'eq-1',
		brewerySetupId: 'setup-1',
		name: 'Conical Fermenter',
		type: 'Fermenter' as EquipmentType,
		subtype: 'ConicalFermenter' as EquipmentSubtype,
		capacity: 60,
		unit: 'Liters' as VolumeUnit,
		capacityLiters: 60,
		currentVolume: 45,
		currentVolumeLiters: 45,
		fillPercentage: 75,
		createdAt: '2026-09-18T08:00:00Z',
		updatedAt: '2026-09-18T08:00:00Z'
	};

	it('returns empty array when latestMetricsJson is null or undefined', () => {
		expect(getParsedAttributes(baseEquipment)).toEqual([]);
	});

	it('returns empty array when latestMetricsJson is invalid JSON', () => {
		const eq = { ...baseEquipment, latestMetricsJson: 'invalid-json{' };
		expect(getParsedAttributes(eq)).toEqual([]);
	});

	it('extracts flat key-value pairs from valid JSON', () => {
		const eq = {
			...baseEquipment,
			latestMetricsJson: JSON.stringify({
				ph: 4.35,
				heaterDutyPct: 65,
				coolingLoop: 'active',
				flowRateLpm: 2.5
			})
		};

		const attrs = getParsedAttributes(eq);
		expect(attrs).toHaveLength(4);
		expect(attrs).toEqual([
			{ key: 'ph', value: '4.35' },
			{ key: 'heaterDutyPct', value: '65' },
			{ key: 'coolingLoop', value: 'active' },
			{ key: 'flowRateLpm', value: '2.5' }
		]);
	});

	it('handles nested objects and boolean values by stringifying', () => {
		const eq = {
			...baseEquipment,
			latestMetricsJson: JSON.stringify({
				valveStatus: { input: 'open', bypass: false },
				alarmActive: true
			})
		};

		const attrs = getParsedAttributes(eq);
		expect(attrs).toHaveLength(2);
		expect(attrs[0]).toEqual({
			key: 'valveStatus',
			value: JSON.stringify({ input: 'open', bypass: false })
		});
		expect(attrs[1]).toEqual({
			key: 'alarmActive',
			value: 'true'
		});
	});

	it('correctly associates multi-metric telemetry fields on EquipmentDto', () => {
		const eq: EquipmentDto = {
			...baseEquipment,
			currentTemperatureC: 19.8,
			currentSpecificGravity: 1.042,
			currentPressureBar: 1.25,
			currentBatteryPercent: 88,
			currentBatteryVoltage: 4.05,
			lastTelemetryAt: '2026-09-18T10:30:00Z',
			latestMetricsJson: JSON.stringify({ tilt: 42.5, rssi: -65 })
		};

		expect(eq.currentSpecificGravity).toBe(1.042);
		expect(eq.currentPressureBar).toBe(1.25);
		expect(eq.currentBatteryPercent).toBe(88);
		expect(eq.currentBatteryVoltage).toBe(4.05);
		expect(eq.lastTelemetryAt).toBe('2026-09-18T10:30:00Z');

		const parsed = getParsedAttributes(eq);
		expect(parsed).toEqual([
			{ key: 'tilt', value: '42.5' },
			{ key: 'rssi', value: '-65' }
		]);
	});
});
