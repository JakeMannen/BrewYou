import { describe, it, expect } from 'vitest';
import type { BatchSummaryDto, EquipmentDto, BrewStage } from '$lib/types/api';

export function calculateCellarUtilization(
	activeBatchCount: number,
	fermenters: EquipmentDto[]
): number {
	const totalFermenters = fermenters.length;
	if (totalFermenters === 0) {
		return activeBatchCount > 0 ? 100 : 0;
	}
	const occupiedCount = Math.min(activeBatchCount, totalFermenters);
	return Math.min(100, Math.round((occupiedCount / totalFermenters) * 100));
}

export function deriveMilestones(batches: BatchSummaryDto[]): {
	id: string;
	batchId: string;
	text: string;
}[] {
	const list: { id: string; batchId: string; text: string }[] = [];

	for (const batch of batches) {
		const batchLabel = batch.batchCode || batch.name;

		if (batch.currentStage === 'Ferment') {
			if (batch.daysActive >= 3 && batch.daysActive <= 6) {
				list.push({
					id: `milestone-${batch.id}-dryhop`,
					batchId: batch.id,
					text: `${batchLabel}: Add dry hop additions (Day ${batch.daysActive})`
				});
			}

			if (batch.daysActive >= 7) {
				list.push({
					id: `milestone-${batch.id}-gravity`,
					batchId: batch.id,
					text: `${batchLabel}: Measure terminal gravity & test diacetyl`
				});
			}
		} else if (batch.currentStage === 'Condition') {
			list.push({
				id: `milestone-${batch.id}-package`,
				batchId: batch.id,
				text: `${batchLabel}: Prepare kegs or bottles for packaging`
			});
		}
	}

	return list;
}

export function getNextStage(current: BrewStage): BrewStage | null {
	const stageOrder: BrewStage[] = ['Mash', 'Boil', 'Ferment', 'Condition', 'Package'];
	const idx = stageOrder.indexOf(current);
	if (idx >= 0 && idx < stageOrder.length - 1) {
		return stageOrder[idx + 1];
	}
	return null;
}

export function validateGravityReading(sg: number): { valid: boolean; error?: string } {
	if (isNaN(sg) || sg <= 0) {
		return { valid: false, error: 'Please enter a valid gravity reading.' };
	}
	if (sg < 0.98 || sg > 1.25) {
		return {
			valid: false,
			error: 'Gravity reading is outside normal brewing range (0.980 - 1.250 SG).'
		};
	}
	return { valid: true };
}

describe('Dashboard Operational Logic & Calculations', () => {
	const mockFermenters: EquipmentDto[] = [
		{
			id: 'f1',
			brewerySetupId: 's1',
			name: 'FV-1 30L',
			type: 'Fermenter',
			subtype: 'ConicalFermenter',
			capacity: 35,
			unit: 'Liters',
			capacityLiters: 35,
			currentVolume: 25,
			currentVolumeLiters: 25,
			fillPercentage: 71,
			createdAt: '',
			updatedAt: ''
		},
		{
			id: 'f2',
			brewerySetupId: 's1',
			name: 'FV-2 60L',
			type: 'Fermenter',
			subtype: 'ConicalFermenter',
			capacity: 65,
			unit: 'Liters',
			capacityLiters: 65,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			createdAt: '',
			updatedAt: ''
		}
	];

	it('calculates cellar utilization accurately based on fermenters count', () => {
		expect(calculateCellarUtilization(0, mockFermenters)).toBe(0);
		expect(calculateCellarUtilization(1, mockFermenters)).toBe(50);
		expect(calculateCellarUtilization(2, mockFermenters)).toBe(100);
		// Cap at 100% when active batches exceed fermenters count
		expect(calculateCellarUtilization(3, mockFermenters)).toBe(100);
	});

	it('handles zero fermenters gracefully without division by zero', () => {
		expect(calculateCellarUtilization(0, [])).toBe(0);
		expect(calculateCellarUtilization(1, [])).toBe(100);
	});

	it('generates accurate automated milestones from batch stages and active days', () => {
		const batches: BatchSummaryDto[] = [
			{
				id: 'b1',
				batchCode: '#B-2026-01',
				name: 'Citra Hazy IPA',
				beerStyle: 'Hazy IPA',
				status: 'Fermenting',
				currentStage: 'Ferment',
				brewDate: '2026-09-10',
				daysActive: 4,
				targetOg: 1.065,
				targetFg: 1.014,
				colorSrm: 4.5,
				targetBatchSizeLiters: 20,
				createdAt: '',
				updatedAt: ''
			},
			{
				id: 'b2',
				batchCode: '#B-2026-02',
				name: 'German Pils',
				beerStyle: 'Pilsner',
				status: 'Fermenting',
				currentStage: 'Ferment',
				brewDate: '2026-09-01',
				daysActive: 13,
				targetOg: 1.048,
				targetFg: 1.01,
				colorSrm: 3.0,
				targetBatchSizeLiters: 50,
				createdAt: '',
				updatedAt: ''
			},
			{
				id: 'b3',
				batchCode: '#B-2026-03',
				name: 'Oatmeal Stout',
				beerStyle: 'Stout',
				status: 'Conditioning',
				currentStage: 'Condition',
				brewDate: '2026-08-25',
				daysActive: 20,
				targetOg: 1.055,
				targetFg: 1.016,
				colorSrm: 32,
				targetBatchSizeLiters: 20,
				createdAt: '',
				updatedAt: ''
			}
		];

		const milestones = deriveMilestones(batches);
		expect(milestones).toHaveLength(3);

		// b1 Day 4 -> dry hop addition
		expect(milestones[0].id).toBe('milestone-b1-dryhop');
		expect(milestones[0].text).toContain('dry hop');

		// b2 Day 13 -> terminal gravity
		expect(milestones[1].id).toBe('milestone-b2-gravity');
		expect(milestones[1].text).toContain('Measure terminal gravity');

		// b3 Condition -> packaging preparation
		expect(milestones[2].id).toBe('milestone-b3-package');
		expect(milestones[2].text).toContain('packaging');
	});

	it('determines the next brewing stage in proper sequence', () => {
		expect(getNextStage('Mash')).toBe('Boil');
		expect(getNextStage('Boil')).toBe('Ferment');
		expect(getNextStage('Ferment')).toBe('Condition');
		expect(getNextStage('Condition')).toBe('Package');
		expect(getNextStage('Package')).toBeNull();
	});

	it('validates gravity readings within plausible brewing limits', () => {
		expect(validateGravityReading(1.055).valid).toBe(true);
		expect(validateGravityReading(0.998).valid).toBe(true);
		expect(validateGravityReading(1.12).valid).toBe(true);

		// Invalid values
		expect(validateGravityReading(0.5).valid).toBe(false);
		expect(validateGravityReading(1.5).valid).toBe(false);
		expect(validateGravityReading(NaN).valid).toBe(false);
		expect(validateGravityReading(-1).valid).toBe(false);
	});
});
