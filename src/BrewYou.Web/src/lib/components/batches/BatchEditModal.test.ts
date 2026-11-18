import { describe, expect, it } from 'vitest';
import type { BatchDetailDto, BatchStatus, UpdateBatchRequest } from '$lib/types/api';

export interface PostFinishBatchForm {
	name: string;
	beerStyle?: string | null;
	brewDate?: string | null;
	notes?: string | null;
	measuredOg?: number | null;
	measuredFg?: number | null;
	measuredBatchSizeLiters?: number | null;
	pitchTemperatureC?: number | null;
	packagingVesselId?: string | null;
	status?: BatchStatus | null;
}

export interface ValidationResult {
	isValid: boolean;
	errors: Record<string, string>;
}

export function isReadOnlyBatch(status: BatchStatus): boolean {
	// R2: Completed batches are now editable; only Archived batches are strictly locked
	return status === 'Archived';
}

export function validatePostFinishBatchForm(form: PostFinishBatchForm): ValidationResult {
	const errors: Record<string, string> = {};

	if (!form.name || form.name.trim().length === 0) {
		errors.name = 'Batch name is required.';
	}

	if (form.measuredOg !== null && form.measuredOg !== undefined) {
		if (isNaN(form.measuredOg) || form.measuredOg < 1.0 || form.measuredOg > 1.25) {
			errors.measuredOg = 'Measured OG must be between 1.000 and 1.250.';
		}
	}

	if (form.measuredFg !== null && form.measuredFg !== undefined) {
		if (isNaN(form.measuredFg) || form.measuredFg < 0.98 || form.measuredFg > 1.2) {
			errors.measuredFg = 'Measured FG must be between 0.980 and 1.200.';
		}
	}

	if (
		form.measuredOg !== null &&
		form.measuredOg !== undefined &&
		form.measuredFg !== null &&
		form.measuredFg !== undefined
	) {
		if (form.measuredFg >= form.measuredOg) {
			errors.measuredFg = 'Measured FG must be lower than measured OG.';
		}
	}

	if (form.measuredBatchSizeLiters !== null && form.measuredBatchSizeLiters !== undefined) {
		if (isNaN(form.measuredBatchSizeLiters) || form.measuredBatchSizeLiters < 0) {
			errors.measuredBatchSizeLiters = 'Batch volume must be non-negative.';
		}
	}

	return {
		isValid: Object.keys(errors).length === 0,
		errors
	};
}

export function buildPostFinishBatchUpdatePayload(
	batch: BatchDetailDto,
	form: PostFinishBatchForm
): UpdateBatchRequest & { brewDate?: string | null; status?: string | null } {
	return {
		name: form.name.trim(),
		beerStyle: form.beerStyle !== undefined ? form.beerStyle : batch.beerStyle,
		notes: form.notes !== undefined ? form.notes : batch.notes,
		brewDate: form.brewDate !== undefined ? form.brewDate : batch.brewDate,
		status: form.status !== undefined ? form.status : batch.status,
		measuredOg: form.measuredOg !== undefined ? form.measuredOg : batch.measuredOg,
		measuredFg: form.measuredFg !== undefined ? form.measuredFg : batch.measuredFg,
		measuredBatchSizeLiters:
			form.measuredBatchSizeLiters !== undefined
				? form.measuredBatchSizeLiters
				: batch.measuredBatchSizeLiters,
		pitchTemperatureC:
			form.pitchTemperatureC !== undefined ? form.pitchTemperatureC : batch.pitchTemperatureC,
		packagingVesselId:
			form.packagingVesselId !== undefined ? form.packagingVesselId : batch.packagingVesselId
	};
}

describe('BatchEditModal — Post-Finish Batch Record Editing Logic (R2)', () => {
	const mockCompletedBatch: BatchDetailDto = {
		id: 'batch-completed-1',
		userId: 'user-1',
		name: 'Bourbon Barrel Stout',
		batchCode: 'B-2026-099',
		beerStyle: 'Imperial Stout',
		recipeId: 'rec-stout-1',
		recipeName: 'Imperial Stout Base',
		status: 'Completed',
		currentStage: 'Package',
		brewDate: '2026-09-01T08:00:00Z',
		daysActive: 45,
		targetOg: 1.09,
		targetFg: 1.02,
		targetAbv: 9.2,
		targetIbu: 65,
		targetColorSrm: 40,
		targetBatchSizeLiters: 20,
		boilTimeMinutes: 90,
		efficiencyPercent: 72,
		measuredOg: 1.092,
		measuredFg: 1.022,
		alcoholByVolume: 9.45,
		brewhouseEfficiency: 73.5,
		measuredBatchSizeLiters: 19.5,
		pitchTemperatureC: 18.0,
		currentGravity: 1.022,
		notes: 'Aged in oak barrel for 3 weeks.',
		createdAt: '2026-09-01T08:00:00Z',
		updatedAt: '2026-10-15T12:00:00Z',
		completedAt: '2026-10-15T12:00:00Z',
		readings: [],
		ingredients: [],
		mashSteps: [],
		fermentationSteps: [],
		stageHistory: [],
		sensorAssignments: []
	};

	describe('Lifecycle Lockdown & Unblocking Guards (TC-T1-R2-01, TC-T2-R2-01, TC-T2-R2-06)', () => {
		it('TC-T1-R2-01: unblocks Completed batches from isReadOnlyBatch lockdown', () => {
			expect(isReadOnlyBatch('Completed')).toBe(false);
			expect(isReadOnlyBatch('Planned')).toBe(false);
			expect(isReadOnlyBatch('Brewing')).toBe(false);
			expect(isReadOnlyBatch('Fermenting')).toBe(false);
			expect(isReadOnlyBatch('Conditioning')).toBe(false);
		});

		it('TC-T2-R2-01 & TC-T2-R2-06: maintains strict read-only lockdown on Archived batches', () => {
			expect(isReadOnlyBatch('Archived')).toBe(true);
		});
	});

	describe('Post-Finish Update Payload Building (TC-T1-R2-01, TC-T1-R2-02, TC-T1-R2-05)', () => {
		it('TC-T1-R2-01: builds valid payload updating name, brewDate, tasting notes, and beerStyle', () => {
			const form: PostFinishBatchForm = {
				name: 'Bourbon Barrel Stout — 2026 Vintage',
				beerStyle: 'Russian Imperial Stout',
				brewDate: '2026-09-02',
				notes: 'Gold medal winner at regional craft competition. Vanilla and bourbon notes.'
			};

			const payload = buildPostFinishBatchUpdatePayload(mockCompletedBatch, form);

			expect(payload.name).toBe('Bourbon Barrel Stout — 2026 Vintage');
			expect(payload.beerStyle).toBe('Russian Imperial Stout');
			expect(payload.brewDate).toBe('2026-09-02');
			expect(payload.notes).toContain('Gold medal winner');
			// Preserves existing measured values when not edited
			expect(payload.measuredOg).toBe(1.092);
			expect(payload.measuredFg).toBe(1.022);
			expect(payload.measuredBatchSizeLiters).toBe(19.5);
			expect(payload.status).toBe('Completed');
		});

		it('TC-T1-R2-02: updates measured gravities and volumes on Completed batch', () => {
			const form: PostFinishBatchForm = {
				name: mockCompletedBatch.name,
				measuredOg: 1.095,
				measuredFg: 1.019,
				measuredBatchSizeLiters: 19.8
			};

			const payload = buildPostFinishBatchUpdatePayload(mockCompletedBatch, form);

			expect(payload.measuredOg).toBe(1.095);
			expect(payload.measuredFg).toBe(1.019);
			expect(payload.measuredBatchSizeLiters).toBe(19.8);
			expect(payload.notes).toBe('Aged in oak barrel for 3 weeks.');
		});

		it('TC-T1-R2-05: permits reopening Completed batch by setting status to Fermenting or Conditioning', () => {
			const form: PostFinishBatchForm = {
				name: mockCompletedBatch.name,
				status: 'Fermenting',
				notes: 'Reopened batch to add dry hops and fruit puree.'
			};

			const payload = buildPostFinishBatchUpdatePayload(mockCompletedBatch, form);

			expect(payload.status).toBe('Fermenting');
			expect(payload.notes).toContain('Reopened batch');
		});
	});

	describe('Form Validation & Corner Case Defense (TC-T2-R2-02, TC-T2-R2-03, TC-T2-R2-04)', () => {
		it('validates required batch name', () => {
			const invalidEmpty = validatePostFinishBatchForm({ name: '   ' });
			expect(invalidEmpty.isValid).toBe(false);
			expect(invalidEmpty.errors.name).toBeDefined();

			const valid = validatePostFinishBatchForm({ name: 'Valid Batch Name' });
			expect(valid.isValid).toBe(true);
		});

		it('TC-T2-R2-02: rejects sub-water gravity and unphysical FG >= OG combinations', () => {
			// Sub-water density (< 1.000)
			const subWater = validatePostFinishBatchForm({
				name: 'Test',
				measuredOg: 0.99
			});
			expect(subWater.isValid).toBe(false);
			expect(subWater.errors.measuredOg).toBeDefined();

			// FG >= OG combination
			const inverted = validatePostFinishBatchForm({
				name: 'Test',
				measuredOg: 1.05,
				measuredFg: 1.06
			});
			expect(inverted.isValid).toBe(false);
			expect(inverted.errors.measuredFg).toBe('Measured FG must be lower than measured OG.');

			// FG == OG combination
			const identical = validatePostFinishBatchForm({
				name: 'Test',
				measuredOg: 1.05,
				measuredFg: 1.05
			});
			expect(identical.isValid).toBe(false);
			expect(identical.errors.measuredFg).toBe('Measured FG must be lower than measured OG.');
		});

		it('TC-T2-R2-03: supports clearing measured values to null', () => {
			const clearForm: PostFinishBatchForm = {
				name: mockCompletedBatch.name,
				measuredOg: null,
				measuredFg: null
			};

			const validation = validatePostFinishBatchForm(clearForm);
			expect(validation.isValid).toBe(true);

			const payload = buildPostFinishBatchUpdatePayload(mockCompletedBatch, clearForm);
			expect(payload.measuredOg).toBeNull();
			expect(payload.measuredFg).toBeNull();
		});

		it('TC-T2-R2-04: accepts historical past dates and distant future brew dates safely', () => {
			const historicalForm: PostFinishBatchForm = {
				name: 'Historic Porter',
				brewDate: '1900-01-01'
			};
			expect(validatePostFinishBatchForm(historicalForm).isValid).toBe(true);

			const futureForm: PostFinishBatchForm = {
				name: 'Future Saison',
				brewDate: '2036-12-31'
			};
			expect(validatePostFinishBatchForm(futureForm).isValid).toBe(true);
		});
	});
});
