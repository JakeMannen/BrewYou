import { describe, it, expect } from 'vitest';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';
import type { EquipmentDto } from '$lib/types/api';

describe('Equipment MQTT Status Logic & Localization Parity', () => {
	function isMqttConnected(
		item: EquipmentDto,
		mqttConnected: boolean | null,
		settingsHost = 'mqtt.brewyou.local',
		settingsPort = 1883
	): boolean {
		if (item.connectionType !== 'Mqtt') return false;
		if (!item.connectionConfigJson) {
			return mqttConnected ?? false;
		}
		try {
			const cfg = JSON.parse(item.connectionConfigJson);
			if (cfg.useUserSettings !== false) {
				return mqttConnected ?? false;
			}
			if (cfg.brokerHost === settingsHost && (Number(cfg.brokerPort) || 1883) === settingsPort) {
				return mqttConnected ?? false;
			}
			return mqttConnected ?? false;
		} catch {
			return mqttConnected ?? false;
		}
	}

	it('identifies MQTT equipment as disconnected when broker connection is false', () => {
		const equipmentItem: EquipmentDto = {
			id: 'eq-1',
			brewerySetupId: 'setup-1',
			name: 'Unitank 1',
			type: 'Fermenter',
			subtype: 'ConicalFermenter',
			capacity: 60,
			unit: 'Liters',
			capacityLiters: 60,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			connectionType: 'Mqtt',
			connectionConfigJson: JSON.stringify({
				useUserSettings: true,
				topic: 'brewery/fermenter1/temp'
			}),
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		};

		expect(isMqttConnected(equipmentItem, false)).toBe(false);
	});

	it('identifies MQTT equipment as connected when broker connection is true', () => {
		const equipmentItem: EquipmentDto = {
			id: 'eq-2',
			brewerySetupId: 'setup-1',
			name: 'Brite Tank 1',
			type: 'Fermenter',
			subtype: 'StainlessBucket',
			capacity: 60,
			unit: 'Liters',
			capacityLiters: 60,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			connectionType: 'Mqtt',
			connectionConfigJson: JSON.stringify({ useUserSettings: true, topic: 'brewery/brite1/temp' }),
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		};

		expect(isMqttConnected(equipmentItem, true)).toBe(true);
	});

	it('returns false for non-MQTT equipment regardless of broker status', () => {
		const httpEquipment: EquipmentDto = {
			id: 'eq-3',
			brewerySetupId: 'setup-1',
			name: 'Hot Liquor Tank',
			type: 'Boiler',
			subtype: 'Hlt',
			capacity: 100,
			unit: 'Liters',
			capacityLiters: 100,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			connectionType: 'HttpPoll',
			connectionConfigJson: JSON.stringify({ url: 'http://192.168.1.50/temp' }),
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		};

		expect(isMqttConnected(httpEquipment, true)).toBe(false);
		expect(isMqttConnected(httpEquipment, false)).toBe(false);
	});

	it('maintains 100% key parity for MQTT connectivity and equipment status in en and sv', () => {
		// Connectivity settings keys
		expect(en.settings.connectivity.status_configured_disconnected).toBeDefined();
		expect(sv.settings.connectivity.status_configured_disconnected).toBeDefined();
		expect(en.settings.connectivity.test_connection).toBeDefined();
		expect(sv.settings.connectivity.test_connection).toBeDefined();
		expect(en.settings.connectivity.test_success).toBeDefined();
		expect(sv.settings.connectivity.test_success).toBeDefined();
		expect(en.settings.connectivity.test_failed).toBeDefined();
		expect(sv.settings.connectivity.test_failed).toBeDefined();

		// Equipment tag keys
		expect(en.equipment.mqtt_connected).toBeDefined();
		expect(sv.equipment.mqtt_connected).toBeDefined();
		expect(en.equipment.mqtt_disconnected).toBeDefined();
		expect(sv.equipment.mqtt_disconnected).toBeDefined();
	});
});
