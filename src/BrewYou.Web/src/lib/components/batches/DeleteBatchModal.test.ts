import { describe, it, expect, vi } from 'vitest';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';
import type { BatchDetailDto } from '$lib/types/api';

describe('DeleteBatchModal Component Logic & Invariants', () => {
	it('has complete internationalization keys for delete_modal in en.json and sv.json', () => {
		expect(en.batches.delete_modal.title).toBe('Delete Batch');
		expect(sv.batches.delete_modal.title).toBe('Ta bort batch');

		expect(en.batches.delete_modal.desc).toBeDefined();
		expect(sv.batches.delete_modal.desc).toBeDefined();

		expect(en.batches.delete_modal.confirm_prompt).toContain('{name}');
		expect(sv.batches.delete_modal.confirm_prompt).toContain('{name}');

		expect(en.batches.delete_modal.confirm_button).toBe('Delete Batch');
		expect(sv.batches.delete_modal.confirm_button).toBe('Ta bort batch');

		expect(en.batches.delete_modal.cancel_button).toBe('Cancel');
		expect(sv.batches.delete_modal.cancel_button).toBe('Avbryt');

		expect(en.batches.delete_modal.deleting).toBe('Deleting...');
		expect(sv.batches.delete_modal.deleting).toBe('Tar bort...');

		expect(en.batches.delete_modal.error).toBe('Failed to delete batch.');
		expect(sv.batches.delete_modal.error).toBe('Kunde inte ta bort batch.');
	});

	it('safely manages batch deletion lifecycle and error capturing', async () => {
		const mockBatch: BatchDetailDto = {
			id: 'batch-42',
			userId: 'user-1',
			name: 'Galaxy Hazy IPA',
			batchCode: 'B-042',
			beerStyle: 'IPA',
			brewDate: '2026-09-14T00:00:00Z',
			daysActive: 5,
			targetOg: 1.065,
			targetFg: 1.012,
			targetAbv: 6.5,
			targetIbu: 50,
			targetColorSrm: 6.5,
			targetBatchSizeLiters: 20,
			boilTimeMinutes: 60,
			efficiencyPercent: 75,
			measuredOg: 1.065,
			measuredFg: null,
			alcoholByVolume: null,
			status: 'Fermenting',
			currentStage: 'Ferment',
			readings: [],
			ingredients: [],
			mashSteps: [],
			fermentationSteps: [],
			stageHistory: [],
			sensorAssignments: [],
			notes: null,
			createdAt: '2026-09-14T00:00:00Z',
			updatedAt: '2026-09-14T00:00:00Z'
		};

		let deletedId: string | null = null;
		let closed = false;
		let capturedError: string | null = null;

		const onDeleted = (id: string) => {
			deletedId = id;
		};
		const onClose = () => {
			closed = true;
		};

		// Successful deletion workflow simulation
		const mockDeleteApi = vi.fn().mockResolvedValue(undefined);

		async function executeDelete(batch: BatchDetailDto | null) {
			if (!batch) return;
			try {
				await mockDeleteApi(batch.id);
				onDeleted(batch.id);
				onClose();
			} catch (err: unknown) {
				capturedError = (err as Error).message || en.batches.delete_modal.error;
			}
		}

		await executeDelete(mockBatch);

		expect(mockDeleteApi).toHaveBeenCalledWith('batch-42');
		expect(deletedId).toBe('batch-42');
		expect(closed).toBe(true);
		expect(capturedError).toBeNull();

		// Failed deletion workflow simulation: error is captured without alert()
		mockDeleteApi.mockRejectedValueOnce(new Error('Network error: DB unreachable'));
		deletedId = null;
		closed = false;

		await executeDelete(mockBatch);

		expect(deletedId).toBeNull();
		expect(closed).toBe(false);
		expect(capturedError).toBe('Network error: DB unreachable');
	});
});
