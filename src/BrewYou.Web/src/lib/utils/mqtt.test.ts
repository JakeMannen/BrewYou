import { describe, expect, it } from 'vitest';
import { generateEquipmentTopic, slugifyEquipmentName } from './mqtt';

describe('slugifyEquipmentName', () => {
	it('converts to lowercase and trims whitespace', () => {
		expect(slugifyEquipmentName('  Grainfather G40  ')).toBe('grainfather-g40');
	});

	it('replaces spaces and special characters with hyphens', () => {
		expect(slugifyEquipmentName('Spike Trio 50L (Boil / Mash!)')).toBe('spike-trio-50l-boil-mash');
	});

	it('collapses multiple consecutive non-alphanumeric characters into a single hyphen', () => {
		expect(slugifyEquipmentName('Kettle---1 & #2')).toBe('kettle-1-2');
	});

	it('removes leading and trailing hyphens', () => {
		expect(slugifyEquipmentName('---Unitank 1---')).toBe('unitank-1');
	});

	it('handles empty or blank string gracefully', () => {
		expect(slugifyEquipmentName('')).toBe('');
		expect(slugifyEquipmentName('    ')).toBe('');
	});
});

describe('generateEquipmentTopic', () => {
	it('uses default prefix when prefix is undefined or null', () => {
		expect(generateEquipmentTopic(undefined, 'Main Kettle')).toBe(
			'brewyou/equipment/main-kettle/telemetry'
		);
		expect(generateEquipmentTopic(null, 'Main Kettle')).toBe(
			'brewyou/equipment/main-kettle/telemetry'
		);
	});

	it('uses custom prefix and strips leading/trailing slashes', () => {
		expect(generateEquipmentTopic('/mybrewery/cellar/', 'Fermenter #1')).toBe(
			'mybrewery/cellar/fermenter-1/telemetry'
		);
	});

	it('returns empty string when equipment name produces empty slug', () => {
		expect(generateEquipmentTopic('brewyou/equipment', '')).toBe('');
		expect(generateEquipmentTopic('brewyou/equipment', '   ')).toBe('');
		expect(generateEquipmentTopic('brewyou/equipment', '---')).toBe('');
	});
});
