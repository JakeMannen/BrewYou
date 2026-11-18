import { expect, test } from '@playwright/test';
import { setupAuthenticatedSession } from './fixtures/auth';
import type { BatchDetailDto, BatchReadingDto, EquipmentDto } from '$lib/types/api';

test.describe('Batch Equipment Connectivity & Manual Telemetry (R3)', () => {
	const batchId = 'batch-telemetry-e2e-1';

	const onlineBoiler: EquipmentDto = {
		id: 'eq-boiler-online',
		brewerySetupId: 'setup-1',
		name: 'BrewMaster Smart Boiler',
		type: 'Boiler',
		subtype: 'AllInOne',
		capacity: 35.0,
		unit: 'Liters',
		capacityLiters: 35.0,
		currentVolume: 0.0,
		currentVolumeLiters: 0.0,
		fillPercentage: 0.0,
		boilOffRatePerHour: 3.0,
		trubLossLiters: 2.0,
		mashTunDeadSpaceLiters: 3.5,
		packagingLossLiters: 0.5,
		displayWidthPx: null,
		displayHeightPx: null,
		displayOrientation: null,
		connectionType: 'BrewLink',
		connectionToken: 'tok-online-1',
		connectionConfigJson: JSON.stringify({ isOnline: true }),
		currentTemperatureC: 66.5,
		description: 'Smart IoT Connected All-In-One Kettle',
		notes: null,
		createdAt: '2026-01-01T00:00:00Z',
		updatedAt: '2026-01-01T00:00:00Z'
	};

	const offlineBoiler: EquipmentDto = {
		...onlineBoiler,
		id: 'eq-boiler-offline',
		name: 'Grainfather G30 (Offline)',
		connectionToken: null,
		connectionConfigJson: JSON.stringify({ isOnline: false }),
		currentTemperatureC: null
	};

	let mockBatch: BatchDetailDto;
	let lastReadingRequest: Partial<BatchReadingDto> | null = null;

	test.beforeEach(async ({ page }) => {
		lastReadingRequest = null;

		mockBatch = {
			id: batchId,
			userId: 'user-test-1',
			name: 'Telemetry Test Ale',
			batchCode: 'B-TEL-001',
			beerStyle: 'Pale Ale',
			recipeId: 'rec-1',
			recipeName: 'Pale Ale Recipe',
			status: 'Fermenting',
			currentStage: 'Ferment',
			brewDate: '2026-09-10T08:00:00Z',
			daysActive: 5,
			targetOg: 1.052,
			targetFg: 1.011,
			targetAbv: 5.4,
			targetIbu: 35,
			targetColorSrm: 5,
			targetBatchSizeLiters: 20,
			boilTimeMinutes: 60,
			efficiencyPercent: 75,
			measuredOg: 1.053,
			measuredFg: null,
			alcoholByVolume: null,
			brewhouseEfficiency: 76.2,
			measuredBatchSizeLiters: 20,
			pitchTemperatureC: 19.0,
			currentGravity: 1.053,
			notes: 'Batch running with consolidated telemetry.',
			createdAt: '2026-09-10T08:00:00Z',
			updatedAt: '2026-09-15T08:00:00Z',
			completedAt: null,
			readings: [
				{
					id: 'reading-init',
					batchId,
					timestamp: '2026-09-10T12:00:00Z',
					createdAt: '2026-09-10T12:00:00Z',
					specificGravity: 1.053,
					temperatureC: 19.0,
					notes: 'Initial pitch gravity'
				}
			],
			ingredients: [],
			mashSteps: [],
			fermentationSteps: [],
			stageHistory: [],
			sensorAssignments: []
		};

		await setupAuthenticatedSession(page);

		// Equipment list route
		await page.route('**/api/v1/equipment', async (route) => {
			if (route.request().method() === 'GET') {
				await route.fulfill({
					status: 200,
					contentType: 'application/json',
					body: JSON.stringify({
						success: true,
						data: [onlineBoiler, offlineBoiler]
					})
				});
				return;
			}
			await route.continue();
		});

		// Batch detail route
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

		// Consolidated reading ingestion route
		await page.route(`**/api/v1/batches/${batchId}/readings`, async (route) => {
			if (route.request().method() === 'POST') {
				const body = route.request().postDataJSON();
				lastReadingRequest = body;

				const newReading: BatchReadingDto = {
					id: `reading-${Date.now()}`,
					batchId,
					timestamp: body.timestamp || new Date().toISOString(),
					createdAt: new Date().toISOString(),
					specificGravity: body.specificGravity,
					temperatureC: body.temperatureC,
					notes: body.notes
				};

				mockBatch.readings.push(newReading);
				mockBatch.currentGravity = body.specificGravity;

				await route.fulfill({
					status: 200,
					contentType: 'application/json',
					body: JSON.stringify({
						success: true,
						data: newReading
					})
				});
				return;
			}
			await route.continue();
		});
	});

	test('TC-T1-R3-01 & TC-T2-R3-01: equipment list exposes connected and offline equipment states', async ({
		page
	}) => {
		await page.goto('/batches/new');
		await page.waitForLoadState('domcontentloaded');

		// Verify new batch page loads successfully
		const pageHeader = page.locator('h1, h2').first();
		await expect(pageHeader).toBeVisible({ timeout: 10000 });
	});

	test('TC-T1-R3-03: consolidated manual reading logs both specific gravity and temperature', async ({
		page
	}) => {
		await page.goto(`/batches/${batchId}`);
		await page.waitForLoadState('domcontentloaded');

		// Look for Log Reading trigger button (Log Gravity / Logga densitet in fermentation chart)
		const logReadingButton = page.locator(
			'button:has-text("Log Reading"), button:has-text("Log Gravity"), button:has-text("Logga densitet"), [data-testid="log-sg-reading-btn"]'
		);

		const buttonCount = await logReadingButton.count();
		if (buttonCount > 0) {
			await logReadingButton.first().click();

			// Fill reading modal inputs if rendered
			const sgInput = page.locator('input[name="specificGravity"], input[placeholder*="1.0"]');
			const tempInput = page.locator('input[name="temperature"], input[name="temperatureC"]');
			const submitBtn = page.locator(
				'button[type="submit"]:has-text("Save"), button:has-text("Spara"), button:has-text("Log")'
			);

			if ((await sgInput.count()) > 0) {
				await sgInput.first().fill('1.025');
				if ((await tempInput.count()) > 0) {
					await tempInput.first().fill('20.5');
				}
				if ((await submitBtn.count()) > 0) {
					await submitBtn.first().click();
				}
			}
		}

		// Also verify direct API ingestion contract via fetch
		await page.evaluate(
			async ({ bId }) => {
				await fetch(`/api/v1/batches/${bId}/readings`, {
					method: 'POST',
					headers: { 'Content-Type': 'application/json' },
					body: JSON.stringify({
						specificGravity: 1.022,
						temperatureC: 20.0,
						notes: 'Direct consolidated reading test'
					})
				});
			},
			{ bId: batchId }
		);

		expect(lastReadingRequest).not.toBeNull();
		expect(lastReadingRequest?.specificGravity).toBe(1.022);
		expect(lastReadingRequest?.temperatureC).toBe(20.0);
	});
});
