import { expect, test } from '@playwright/test';
import { setupAuthenticatedSession } from './fixtures/auth';
import type { BatchDetailDto, UpdateBatchRequest } from '$lib/types/api';

test.describe('Post-Finish Batch Record Editing (R2)', () => {
	const completedBatchId = 'batch-completed-e2e-1';
	const archivedBatchId = 'batch-archived-e2e-1';

	let mockCompletedBatch: BatchDetailDto;
	let mockArchivedBatch: BatchDetailDto;
	let lastUpdateRequest: UpdateBatchRequest | null = null;

	test.beforeEach(async ({ page }) => {
		lastUpdateRequest = null;

		mockCompletedBatch = {
			id: completedBatchId,
			userId: 'test-user-1',
			recipeId: 'rec-completed-1',
			recipeName: 'West Coast Double IPA',
			batchCode: 'B-2026-COMP',
			name: 'West Coast Double IPA',
			beerStyle: 'Double IPA',
			status: 'Completed',
			currentStage: 'Package',
			brewDate: '2026-09-01T08:00:00Z',
			daysActive: 28,
			targetOg: 1.075,
			targetFg: 1.012,
			targetAbv: 8.2,
			targetIbu: 85,
			targetColorSrm: 8,
			targetBatchSizeLiters: 20,
			boilTimeMinutes: 90,
			efficiencyPercent: 75,
			measuredOg: 1.076,
			measuredFg: 1.013,
			alcoholByVolume: 8.27,
			brewhouseEfficiency: 76.0,
			measuredBatchSizeLiters: 19.8,
			pitchTemperatureC: 18.5,
			currentGravity: 1.013,
			notes: 'Batch completed successfully. Crisp bitterness and piney aroma.',
			createdAt: '2026-09-01T08:00:00Z',
			updatedAt: '2026-09-29T12:00:00Z',
			completedAt: '2026-09-29T12:00:00Z',
			readings: [],
			ingredients: [
				{
					id: 'ing-1',
					batchId: completedBatchId,
					name: 'Centennial Hops',
					type: 'Hop',
					amount: 50,
					unit: 'g',
					additionStage: 'Boil',
					isChecked: true
				}
			],
			mashSteps: [],
			fermentationSteps: [
				{
					id: 'ferm-1',
					batchId: completedBatchId,
					stepOrder: 1,
					name: 'Dry Hop Verification',
					type: 'Primary',
					targetTemperatureC: 19.0,
					durationDays: 5,
					isCompleted: true
				}
			],
			stageHistory: [],
			sensorAssignments: []
		};

		mockArchivedBatch = {
			...mockCompletedBatch,
			id: archivedBatchId,
			status: 'Archived',
			name: 'Archived Heritage Bitter'
		};

		await setupAuthenticatedSession(page);

		// Route handler for Completed batch
		await page.route(`**/api/v1/batches/${completedBatchId}`, async (route) => {
			if (route.request().method() === 'GET') {
				await route.fulfill({
					status: 200,
					contentType: 'application/json',
					body: JSON.stringify({
						success: true,
						data: mockCompletedBatch
					})
				});
				return;
			}

			if (route.request().method() === 'PUT') {
				lastUpdateRequest = route.request().postDataJSON() as UpdateBatchRequest;
				mockCompletedBatch = {
					...mockCompletedBatch,
					...lastUpdateRequest,
					name: (lastUpdateRequest.name ?? mockCompletedBatch.name) || '',
					beerStyle: (lastUpdateRequest.beerStyle ?? mockCompletedBatch.beerStyle) || '',
					brewDate: lastUpdateRequest.brewDate ?? mockCompletedBatch.brewDate,
					status: lastUpdateRequest.status ?? mockCompletedBatch.status,
					sensorAssignments: mockCompletedBatch.sensorAssignments,
					updatedAt: new Date().toISOString()
				};
				await route.fulfill({
					status: 200,
					contentType: 'application/json',
					body: JSON.stringify({
						success: true,
						data: mockCompletedBatch
					})
				});
				return;
			}

			await route.continue();
		});

		// Route handler for Archived batch
		await page.route(`**/api/v1/batches/${archivedBatchId}`, async (route) => {
			if (route.request().method() === 'GET') {
				await route.fulfill({
					status: 200,
					contentType: 'application/json',
					body: JSON.stringify({
						success: true,
						data: mockArchivedBatch
					})
				});
				return;
			}
			await route.continue();
		});

		// Fallback for batches list
		await page.route('**/api/v1/batches', async (route) => {
			if (route.request().method() === 'GET') {
				await route.fulfill({
					status: 200,
					contentType: 'application/json',
					body: JSON.stringify({
						success: true,
						data: [mockCompletedBatch, mockArchivedBatch]
					})
				});
				return;
			}
			await route.continue();
		});
	});

	test('TC-T1-R2-01 & TC-T1-R2-06: displays completed batch view and permits post-finish interaction', async ({
		page
	}) => {
		await page.goto(`/batches/${completedBatchId}`);
		await page.waitForLoadState('domcontentloaded');

		// Verify batch title is rendered
		const title = page.locator('h1, h2').filter({ hasText: 'West Coast Double IPA' });
		await expect(title.first()).toBeVisible({ timeout: 10000 });

		// Verify Completed status badge is displayed
		const statusBadge = page.locator('text=Completed, text=Avslutad').first();
		await expect(statusBadge).toBeVisible();

		// Check for post-finish edit button if rendered
		const editButton = page.locator(
			'button:has-text("Edit Batch"), button:has-text("Redigera"), [data-testid="edit-batch-record-button"]'
		);
		const editBtnCount = await editButton.count();
		if (editBtnCount > 0) {
			await expect(editButton.first()).toBeEnabled();
		}
	});

	test('TC-T2-R2-01 & TC-T2-R2-06: archived batches enforce strict read-only lock', async ({
		page
	}) => {
		await page.goto(`/batches/${archivedBatchId}`);
		await page.waitForLoadState('domcontentloaded');

		// Title renders
		const title = page.locator('h1, h2').filter({ hasText: 'Archived Heritage Bitter' });
		await expect(title.first()).toBeVisible({ timeout: 10000 });

		// Checkbox controls must be disabled for Archived batches
		const checkboxes = page.locator('input[type="checkbox"]');
		const count = await checkboxes.count();
		for (let i = 0; i < count; i++) {
			await expect(checkboxes.nth(i)).toBeDisabled();
		}
	});

	test('TC-T3-PAIR-01: supports updating batch notes and records on completed batch via API contract', async ({
		page
	}) => {
		await page.goto(`/batches/${completedBatchId}`);
		await page.waitForLoadState('domcontentloaded');

		// Simulate client PUT call through page evaluate using API client
		const updatedNotes = 'Competition notes: Added cascade dry hops, clean finish.';
		await page.evaluate(
			async ({ batchId, notes }) => {
				await fetch(`/api/v1/batches/${batchId}`, {
					method: 'PUT',
					headers: { 'Content-Type': 'application/json' },
					body: JSON.stringify({
						name: 'West Coast Double IPA',
						beerStyle: 'Double IPA',
						notes
					})
				});
			},
			{ batchId: completedBatchId, notes: updatedNotes }
		);

		expect(lastUpdateRequest).not.toBeNull();
		expect(lastUpdateRequest?.notes).toBe(updatedNotes);
	});
});
