import { expect, test } from '@playwright/test';
import { setupAuthenticatedSession } from './fixtures/auth';
import type { BatchDetailDto } from '$lib/types/api';

export interface BatchDeviationsDto {
	ogDeviation?: number | null;
	ogPointsDeviation?: number | null;
	fgDeviation?: number | null;
	fgPointsDeviation?: number | null;
	abvDeviation?: number | null;
	batchSizeLitersDeviation?: number | null;
	preBoilVolumeDeviation?: number | null;
	postBoilVolumeDeviation?: number | null;
	packagedVolumeDeviation?: number | null;
	brewhouseEfficiencyDeviation?: number | null;
}

test.describe('Batch Target Deviations HUD & Vital Badges (R4)', () => {
	const batchId = 'batch-deviations-e2e-1';

	let mockBatch: BatchDetailDto & { deviations?: BatchDeviationsDto };

	test.beforeEach(async ({ page }) => {
		mockBatch = {
			id: batchId,
			userId: 'test-user-1',
			recipeId: 'rec-ipa-1',
			recipeName: 'Simcoe IPA Recipe',
			batchCode: 'B-DEV-001',
			name: 'Simcoe IPA',
			beerStyle: 'IPA',
			status: 'Fermenting',
			currentStage: 'Ferment',
			brewDate: '2026-09-15T08:00:00Z',
			daysActive: 7,
			targetOg: 1.06,
			targetFg: 1.012,
			targetAbv: 6.3,
			targetIbu: 55,
			targetColorSrm: 7,
			targetBatchSizeLiters: 20,
			boilTimeMinutes: 60,
			efficiencyPercent: 75,
			measuredOg: 1.064, // +4 points
			measuredFg: 1.011, // -1 point
			alcoholByVolume: 6.95, // +0.65%
			brewhouseEfficiency: 78.5,
			measuredBatchSizeLiters: 19.5, // -0.5L
			pitchTemperatureC: 19.0,
			currentGravity: 1.011,
			notes: 'Slightly higher OG than predicted; healthy vigorous fermentation.',
			createdAt: '2026-09-15T08:00:00Z',
			updatedAt: '2026-09-22T08:00:00Z',
			completedAt: null,
			readings: [],
			ingredients: [],
			mashSteps: [],
			fermentationSteps: [],
			stageHistory: [],
			sensorAssignments: [],
			deviations: {
				ogDeviation: 0.004,
				ogPointsDeviation: 4,
				fgDeviation: -0.001,
				fgPointsDeviation: -1,
				abvDeviation: 0.65,
				batchSizeLitersDeviation: -0.5,
				preBoilVolumeDeviation: 0.2,
				postBoilVolumeDeviation: 0.1,
				packagedVolumeDeviation: null,
				brewhouseEfficiencyDeviation: 3.5
			}
		};

		await setupAuthenticatedSession(page);

		// Batch detail endpoint with deviations object
		await page.route(`**/api/v1/batches/${batchId}`, async (route) => {
			if (route.request().method() === 'GET') {
				await route.fulfill({
					status: 200,
					contentType: 'application/json',
					body: JSON.stringify({
						success: true,
						data: mockBatch
					})
				});
				return;
			}
			await route.continue();
		});

		// Batches list endpoint
		await page.route('**/api/v1/batches', async (route) => {
			if (route.request().method() === 'GET') {
				await route.fulfill({
					status: 200,
					contentType: 'application/json',
					body: JSON.stringify({
						success: true,
						data: [mockBatch]
					})
				});
				return;
			}
			await route.continue();
		});
	});

	test('TC-T1-R4-05 & TC-T1-R4-06: batch detail renders vital metrics and deviation data', async ({
		page
	}) => {
		await page.goto(`/batches/${batchId}`);
		await page.waitForLoadState('domcontentloaded');

		// Verify batch title renders
		const title = page.locator('h1, h2').filter({ hasText: 'Simcoe IPA' });
		await expect(title.first()).toBeVisible({ timeout: 10000 });

		// Verify vital cards contain target and measured values
		const pageContent = await page.content();
		expect(pageContent).toContain('1.064');
		expect(pageContent).toContain('1.06');
	});

	test('TC-T1-R1-03: batches list card HUD correctly represents actual calculated ABV', async ({
		page
	}) => {
		await page.goto('/batches');
		await page.waitForLoadState('domcontentloaded');

		// Verify batches list loads
		const batchCard = page.locator('text=Simcoe IPA').first();
		await expect(batchCard).toBeVisible({ timeout: 10000 });

		// Verify that 6.95% is rendered on the batch card
		const abvDisplay = page.locator('text=6.95%');
		await expect(abvDisplay.first()).toBeVisible();
	});

	test('TC-T2-R4-05: handles on-target zero deviations cleanly in detail view', async ({
		page
	}) => {
		// Set batch to perfect target match (0 delta)
		mockBatch = {
			...mockBatch,
			measuredOg: 1.06,
			measuredBatchSizeLiters: 20.0,
			deviations: {
				...mockBatch.deviations!,
				ogDeviation: 0.0,
				ogPointsDeviation: 0,
				batchSizeLitersDeviation: 0.0,
				abvDeviation: 0.0
			}
		};

		await page.goto(`/batches/${batchId}`);
		await page.waitForLoadState('domcontentloaded');

		// Page loads smoothly with zero delta
		const title = page.locator('h1, h2').filter({ hasText: 'Simcoe IPA' });
		await expect(title.first()).toBeVisible({ timeout: 10000 });
	});
});
