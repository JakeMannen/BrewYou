import { expect, test } from '@playwright/test';
import { setupAuthenticatedSession } from './fixtures/auth';
import type { BatchDetailDto } from '$lib/types/api';

test.describe('Batch Step Progression & Stage Navigation', () => {
	const batchId = 'batch-progression-test-1';

	let mockBatch: BatchDetailDto;

	test.beforeEach(async ({ page }) => {
		// Initialize fresh mock batch state for each test
		mockBatch = {
			id: batchId,
			userId: 'test-user-1',
			recipeId: 'recipe-test-1',
			recipeName: 'Citra Pale Ale Recipe',
			batchCode: 'B-2026-001',
			name: 'Citra Pale Ale',
			beerStyle: 'American Pale Ale',
			status: 'Brewing',
			currentStage: 'Mash',
			brewDate: '2026-09-20T10:00:00Z',
			daysActive: 1,
			targetOg: 1.054,
			targetFg: 1.012,
			targetAbv: 5.5,
			targetIbu: 40,
			targetColorSrm: 6,
			targetBatchSizeLiters: 20,
			boilTimeMinutes: 60,
			efficiencyPercent: 75,
			measuredOg: null,
			currentGravity: null,
			measuredFg: null,
			alcoholByVolume: null,
			brewhouseEfficiency: null,
			measuredBatchSizeLiters: 20,
			pitchTemperatureC: 19.0,
			notes: 'Batch step progression UI test run',
			createdAt: '2026-09-20T10:00:00Z',
			updatedAt: '2026-09-20T10:00:00Z',
			readings: [],
			ingredients: [
				{
					id: 'ing-1',
					batchId,
					name: 'Pale Ale Malt',
					type: 'Fermentable',
					amount: 4.5,
					unit: 'kg',
					additionStage: 'Mash',
					isChecked: true
				}
			],
			mashSteps: [
				{
					id: 'mash-1',
					batchId,
					stepOrder: 1,
					name: 'Protein Rest',
					type: 'Temperature',
					targetTemperatureC: 50.0,
					durationMinutes: 20,
					isCompleted: false
				},
				{
					id: 'mash-2',
					batchId,
					stepOrder: 2,
					name: 'Saccharification Rest',
					type: 'Temperature',
					targetTemperatureC: 65.0,
					durationMinutes: 60,
					isCompleted: false
				},
				{
					id: 'mash-3',
					batchId,
					stepOrder: 3,
					name: 'Mash Out',
					type: 'Temperature',
					targetTemperatureC: 75.0,
					durationMinutes: 10,
					isCompleted: false
				}
			],
			fermentationSteps: [
				{
					id: 'ferm-1',
					batchId,
					stepOrder: 1,
					name: 'Primary Fermentation',
					type: 'Primary',
					targetTemperatureC: 19.0,
					durationDays: 7,
					isCompleted: false
				},
				{
					id: 'ferm-2',
					batchId,
					stepOrder: 2,
					name: 'Diacetyl Rest',
					type: 'DiacetylRest',
					targetTemperatureC: 21.0,
					durationDays: 3,
					isCompleted: false
				},
				{
					id: 'ferm-3',
					batchId,
					stepOrder: 3,
					name: 'Cold Crash',
					type: 'ColdCrash',
					targetTemperatureC: 2.0,
					durationDays: 3,
					isCompleted: false
				}
			],
			stageHistory: [],
			sensorAssignments: [],
			volumeProfile: {
				totalWaterLiters: 28,
				strikeWaterLiters: 15,
				spargeWaterLiters: 13,
				targetPreBoilVolumeLiters: 25,
				targetPostBoilVolumeLiters: 22,
				targetFermenterVolumeLiters: 20,
				targetPackagedVolumeLiters: 19,
				boilOffRatePerHour: 3,
				grainAbsorptionRateLPerKg: 1,
				kettleTrubLossLiters: 1.5,
				fermenterTrubLossLiters: 1,
				mashTunDeadSpaceLiters: 0.5,
				coolingShrinkagePercent: 4,
				packagingLossLiters: 0.5
			}
		};

		await setupAuthenticatedSession(page);

		// Mock batch endpoints
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

		// Mock mash step toggling
		await page.route(`**/api/v1/batches/${batchId}/mash-steps/*`, async (route) => {
			const stepId = route.request().url().split('/').pop()?.split('?')[0];
			const postData = route.request().postDataJSON() || {};
			const step = mockBatch.mashSteps.find((s) => s.id === stepId);
			if (step) {
				step.isCompleted =
					postData.isCompleted !== undefined ? postData.isCompleted : !step.isCompleted;
			}
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: step
				})
			});
		});

		// Mock fermentation step toggling
		await page.route(`**/api/v1/batches/${batchId}/fermentation-steps/*`, async (route) => {
			const stepId = route.request().url().split('/').pop()?.split('?')[0];
			const postData = route.request().postDataJSON() || {};
			const step = mockBatch.fermentationSteps.find((s) => s.id === stepId);
			if (step) {
				step.isCompleted =
					postData.isCompleted !== undefined ? postData.isCompleted : !step.isCompleted;
			}
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: step
				})
			});
		});

		// Mock stage advance
		await page.route(`**/api/v1/batches/${batchId}/stage*`, async (route) => {
			const postData = route.request().postDataJSON() || {};
			mockBatch.currentStage = postData.targetStage || 'Boil';
			if (mockBatch.currentStage === 'Boil') {
				mockBatch.status = 'Brewing';
			} else if (mockBatch.currentStage === 'Ferment') {
				mockBatch.status = 'Fermenting';
			}
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: mockBatch
				})
			});
		});

		// Mock equipment readings
		await page.route(`**/api/v1/batches/${batchId}/equipment-readings*`, async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: []
				})
			});
		});

		// Mock batch readings endpoint
		await page.route(`**/api/v1/batches/${batchId}/readings*`, async (route) => {
			if (route.request().method() === 'POST') {
				const postData = route.request().postDataJSON() || {};
				const reading = {
					id: `reading-${Date.now()}`,
					batchId,
					timestamp: new Date().toISOString(),
					specificGravity: postData.specificGravity ?? 1.012,
					temperatureC: postData.temperatureC ?? null,
					notes: postData.notes ?? null,
					createdAt: new Date().toISOString(),
					alcoholByVolume: 5.51
				};
				mockBatch.readings.push(reading);
				mockBatch.currentGravity = reading.specificGravity;
				mockBatch.alcoholByVolume = reading.alcoholByVolume;
				await route.fulfill({
					status: 201,
					contentType: 'application/json',
					body: JSON.stringify({
						success: true,
						data: reading
					})
				});
				return;
			}
			await route.continue();
		});

		// Open batch page and wait for hydration
		await page.goto(`/batches/${batchId}`);
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });
	});

	test('renders initial mash step progression with first step active', async ({ page }) => {
		// Verify batch title
		await expect(page.locator('h1')).toContainText('Citra Pale Ale');

		// Verify Mash stage is active in stage tracker
		const mashNode = page.locator('[data-testid="stage-node-mash"]');
		await expect(mashNode).toBeVisible();
		await expect(mashNode).toHaveAttribute('aria-current', 'step');

		// Verify Mash Rest Timer & Step Progression panel
		const timerPanel = page.locator('[data-testid="mash-timer-panel"]');
		await expect(timerPanel).toBeVisible();

		// Initial active step is Step 1 (Protein Rest)
		const stepOrderBadge = page.locator('[data-testid="active-step-order-badge"]');
		await expect(stepOrderBadge).toContainText('1');

		const activeStepName = page.locator('[data-testid="active-step-name"]');
		await expect(activeStepName).toHaveText('Protein Rest');

		// Check Mash Schedule timeline renders all 3 steps
		const scheduleCard = page.locator('[data-testid="mash-schedule-card"]');
		await expect(scheduleCard).toBeVisible();
		await expect(scheduleCard).toContainText('0 / 3');

		await expect(page.locator('[data-testid="mash-step-row-mash-1"]')).toBeVisible();
		await expect(page.locator('[data-testid="mash-step-row-mash-2"]')).toBeVisible();
		await expect(page.locator('[data-testid="mash-step-row-mash-3"]')).toBeVisible();

		// First step has the active step badge in schedule
		await expect(
			page.locator('[data-testid="mash-step-row-mash-1"]').getByText(/Active Step|Aktivt steg/i)
		).toBeVisible();
	});

	test('navigates between mash steps by selecting another step in the timeline', async ({
		page
	}) => {
		// Step 2 has a 'Select Step' button
		const selectStep2Btn = page.locator('[data-testid="mash-step-select-mash-2"]');
		await expect(selectStep2Btn).toBeVisible();
		await selectStep2Btn.click();

		// Verify active step updates in Timer Panel
		const activeStepName = page.locator('[data-testid="active-step-name"]');
		await expect(activeStepName).toHaveText('Saccharification Rest');

		const stepOrderBadge = page.locator('[data-testid="active-step-order-badge"]');
		await expect(stepOrderBadge).toContainText('2');

		// Step 2 now displays the active step indicator
		await expect(
			page.locator('[data-testid="mash-step-row-mash-2"]').getByText(/Active Step|Aktivt steg/i)
		).toBeVisible();

		// Now select Step 3 by clicking its name button
		const step3NameBtn = page.locator('[data-testid="mash-step-name-btn-mash-3"]');
		await step3NameBtn.click();

		await expect(activeStepName).toHaveText('Mash Out');
		await expect(stepOrderBadge).toContainText('3');
	});

	test('advances mash step progression using Complete & Next Step button', async ({ page }) => {
		// When Step 1 is active, clicking 'Complete & Next Step' completes Step 1 and advances to Step 2
		const advanceBtn = page.locator('[data-testid="advance-mash-step-btn"]');
		await expect(advanceBtn).toBeVisible();
		await advanceBtn.click();

		// Step 1 checkbox is now checked
		const step1Checkbox = page.locator('[data-testid="mash-step-checkbox-mash-1"]');
		await expect(step1Checkbox).toBeChecked();

		// Schedule completion counter updates to 1 / 3
		const scheduleCard = page.locator('[data-testid="mash-schedule-card"]');
		await expect(scheduleCard).toContainText('1 / 3');

		// Active step advances to Step 2 (Saccharification Rest)
		const activeStepName = page.locator('[data-testid="active-step-name"]');
		await expect(activeStepName).toHaveText('Saccharification Rest');

		const stepOrderBadge = page.locator('[data-testid="active-step-order-badge"]');
		await expect(stepOrderBadge).toContainText('2');
	});

	test('toggles mash step completion via direct checkbox interaction', async ({ page }) => {
		const step2Checkbox = page.locator('[data-testid="mash-step-checkbox-mash-2"]');
		await expect(step2Checkbox).not.toBeChecked();

		// Check Step 2
		await step2Checkbox.check();
		await expect(step2Checkbox).toBeChecked();

		// Strikethrough style is applied to step name
		const step2NameBtn = page.locator('[data-testid="mash-step-name-btn-mash-2"]');
		await expect(step2NameBtn).toHaveClass(/line-through/);

		// Completed counter updates to 1 / 3
		const scheduleCard = page.locator('[data-testid="mash-schedule-card"]');
		await expect(scheduleCard).toContainText('1 / 3');

		// Uncheck Step 2
		await step2Checkbox.uncheck();
		await expect(step2Checkbox).not.toBeChecked();
		await expect(step2NameBtn).not.toHaveClass(/line-through/);
		await expect(scheduleCard).toContainText('0 / 3');
	});

	test('navigates stage progression via BrewStageTracker and displays contextual inspection banner', async ({
		page
	}) => {
		const tracker = page.locator('[data-testid="brew-stage-tracker"]');
		await expect(tracker).toBeVisible();

		// Inspection banner should NOT be present initially
		const banner = page.locator('[data-testid="inspecting-stage-banner"]');
		await expect(banner).toBeHidden();

		// Click on Boil stage node to inspect Boil stage
		const boilNode = page.locator('[data-testid="stage-node-boil"]');
		await boilNode.click();

		// Inspection banner appears notifying brewer they are inspecting Boil while active is Mash
		await expect(banner).toBeVisible();
		await expect(banner).toContainText(/Boil|Kokning/i);

		// Mash timer panel is hidden in Boil inspection view
		const mashTimer = page.locator('[data-testid="mash-timer-panel"]');
		await expect(mashTimer).toBeHidden();

		// Click 'Return to active stage' button
		const returnBtn = page.locator('[data-testid="return-to-active-stage-btn"]');
		await expect(returnBtn).toBeVisible();
		await returnBtn.click();

		// Banner is hidden and Mash workspace returns
		await expect(banner).toBeHidden();
		await expect(mashTimer).toBeVisible();
	});

	test('supports keyboard navigation across BrewStageTracker stage progression', async ({
		page
	}) => {
		const mashNode = page.locator('[data-testid="stage-node-mash"]');
		await mashNode.focus();

		// Press ArrowRight to navigate to next stage (Boil)
		await page.keyboard.press('ArrowRight');

		const banner = page.locator('[data-testid="inspecting-stage-banner"]');
		await expect(banner).toBeVisible();
		await expect(banner).toContainText(/Boil|Kokning/i);

		// Press ArrowRight again to navigate to Ferment
		await page.keyboard.press('ArrowRight');
		await expect(banner).toContainText(/Ferment|Jäsning/i);

		// Press Home to jump back to first stage (Mash)
		await page.keyboard.press('Home');
		await expect(banner).toBeHidden();
		await expect(page.locator('[data-testid="mash-timer-panel"]')).toBeVisible();
	});

	test('navigates to Ferment stage and interacts with fermentation step progression', async ({
		page
	}) => {
		// Select Ferment node in BrewStageTracker
		const fermentNode = page.locator('[data-testid="stage-node-ferment"]');
		await fermentNode.click();

		// Verify Fermentation Steps card is rendered
		const fermCard = page.locator('[data-testid="fermentation-steps-card"]');
		await expect(fermCard).toBeVisible();

		// Verify active fermentation step badge shows Step 1 (Primary Fermentation)
		const activeFermBadge = page.locator('[data-testid="active-fermentation-step-badge"]');
		await expect(activeFermBadge).toContainText('Primary Fermentation');
		await expect(activeFermBadge).toContainText('19.0');

		// Toggle Step 1 completion
		const ferm1Checkbox = page.locator('[data-testid="fermentation-step-checkbox-ferm-1"]');
		await expect(ferm1Checkbox).not.toBeChecked();
		await ferm1Checkbox.check();
		await expect(ferm1Checkbox).toBeChecked();

		// Active fermentation step badge automatically advances to Step 2 (Diacetyl Rest)
		await expect(activeFermBadge).toContainText('Diacetyl Rest');
		await expect(activeFermBadge).toContainText('21.0');
	});

	test('advances batch stage workflow via Advance Stage modal', async ({ page }) => {
		// Header contains Advance Stage button
		const advanceStageBtn = page.locator('[data-testid="advance-stage-btn"]');
		await expect(advanceStageBtn).toBeVisible();
		await advanceStageBtn.click();

		// Advance modal dialog appears with target stage Boil
		const modal = page.locator('div[role="dialog"]');
		await expect(modal).toBeVisible();
		await expect(modal).toContainText(/Advance to Boil|Flytta till Kokning/i);

		// Submit the stage advance modal
		const confirmBtn = modal.locator('[data-testid="confirm-advance-stage-btn"]');
		await confirmBtn.click();

		// Modal closes
		await expect(modal).toBeHidden();

		// Stage tracker now marks Boil as the active stage
		const boilNode = page.locator('[data-testid="stage-node-boil"]');
		await expect(boilNode).toHaveAttribute('aria-current', 'step');

		// Batch status updates to Boiling
		await expect(page.getByText(/Boiling|Kokar/i)).toBeVisible();
	});

	test('recalculates current ABV immediately during fermentation step when a new SG reading comes in without reload', async ({
		page
	}) => {
		// Set batch to Ferment stage
		mockBatch.currentStage = 'Ferment';
		mockBatch.status = 'Fermenting';
		mockBatch.measuredOg = 1.054;
		mockBatch.currentGravity = null;
		mockBatch.alcoholByVolume = null;

		await page.goto(`/batches/${batchId}`);
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		// Verify initial state: Active step badge is visible for Primary Fermentation
		const activeFermBadge = page.locator('[data-testid="active-fermentation-step-badge"]');
		await expect(activeFermBadge).toContainText('Primary Fermentation');

		// Initial ABV in HUD displays dash / not calculated
		const abvTile = page.locator('[data-testid="hud-current-abv"]');
		await expect(abvTile).toHaveText('—');

		// Click "+ Log Gravity" button in AttenuationChart
		const logReadingBtn = page.locator('[data-testid="log-sg-reading-btn"]');
		await expect(logReadingBtn).toBeVisible();
		await logReadingBtn.click();

		// Reading modal appears
		const readingModal = page.locator('div[role="dialog"]');
		await expect(readingModal).toBeVisible();

		// Fill in SG value 1.012
		const sgInput = readingModal.locator('input[type="number"]').first();
		await sgInput.fill('1.012');

		// Save reading
		const saveBtn = readingModal.locator('[data-testid="submit-reading-btn"]');
		await saveBtn.click();

		// Modal closes
		await expect(readingModal).toBeHidden();

		// Verify that current ABV is recalculated and displayed immediately without reload!
		await expect(abvTile).toHaveText('5.51%');
	});
});
