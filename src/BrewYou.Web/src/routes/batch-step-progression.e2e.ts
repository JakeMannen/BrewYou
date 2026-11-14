import { expect, test } from '@playwright/test';
import { setupAuthenticatedSession } from './fixtures/auth';
import type { BatchDetailDto, EquipmentDto, RecipeDetailDto } from '$lib/types/api';

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
				hltDeadSpaceLiters: 0.0,
				plumbingLossLiters: 0.0,
				spargeMethod: 'Batch',
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

		// Mash timer panel is hidden in Boil inspection view, and boil timer is set to boil time value (60 mins)
		const mashTimer = page.locator('[data-testid="mash-timer-panel"]');
		await expect(mashTimer).toBeHidden();
		const boilTimerDisplay = page.locator('[data-testid="boil-timer-display"]');
		await expect(boilTimerDisplay).toHaveText('60:00');

		// Click 'Return to active stage' button
		const returnBtn = page.locator('[data-testid="return-to-active-stage-btn"]');
		await expect(returnBtn).toBeVisible();
		await returnBtn.click();

		// Banner is hidden and Mash workspace returns with mash step timer
		await expect(banner).toBeHidden();
		await expect(mashTimer).toBeVisible();
		const mashTimerDisplay = page.locator('[data-testid="mash-timer-display"]');
		await expect(mashTimerDisplay).toHaveText('20:00');
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

		// Batch status remains Brewing during boil stage
		await expect(page.getByText(/Brewing|Brygger/i).first()).toBeVisible();

		// Boil timer is set to boilTimeMinutes (60:00) when moving batch step from Mash to Boil
		const boilTimerDisplay = page.locator('[data-testid="boil-timer-display"]');
		await expect(boilTimerDisplay).toBeVisible();
		await expect(boilTimerDisplay).toHaveText('60:00');
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

	test('preserves active Mash Step 1 timer when inspecting Mash Step 2 and syncs Mash checklist on step completion', async ({
		page
	}) => {
		// Set Mash ingredient unchecked initially so we can verify auto-check on Mash step completion
		mockBatch.ingredients = [
			{
				id: 'ing-mash-1',
				batchId,
				name: 'Pilsner Malt',
				type: 'Fermentable',
				amount: 5.0,
				unit: 'kg',
				additionStage: 'Mash',
				isChecked: false,
				isDeducted: false
			},
			{
				id: 'ing-boil-sugar',
				batchId,
				name: 'Candi Sugar',
				type: 'Fermentable',
				amount: 0.5,
				unit: 'kg',
				additionStage: 'Boil',
				additionTimeMinutes: 10,
				isChecked: false,
				isDeducted: false
			}
		];

		await page.goto(`/batches/${batchId}`);
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		const timerDisplay = page.locator('[data-testid="mash-timer-display"]');
		await expect(timerDisplay).toHaveText('20:00');

		// Start Mash Step 1 timer
		const timerPanel = page.locator('[data-testid="mash-timer-panel"]');
		await timerPanel.getByRole('button', { name: /Start|Starta/i }).click();
		await page.waitForTimeout(1150);

		// Inspect Mash Step 2 (60m) -> shows 60:00 unstarted without resetting Step 1
		await page.locator('[data-testid="mash-step-select-mash-2"]').click();
		await expect(timerDisplay).toHaveText('60:00');

		// Return to Mash Step 1 -> timer is still running (< 20:00, not reset to 20:00)
		await page.locator('[data-testid="mash-step-select-mash-1"]').click();
		await expect(timerDisplay).not.toHaveText('20:00');

		// Complete Mash Step 1 -> Mash ingredient checkbox becomes checked immediately
		const step1Checkbox = page.locator('[data-testid="mash-step-checkbox-mash-1"]');
		await step1Checkbox.check();
		await expect(step1Checkbox).toBeChecked();

		const mashChecklistCounter = page.getByText('1 / 1');
		await expect(mashChecklistCounter).toBeVisible();
	});

	test('renders TriggerGravity readiness badge when currentGravity reaches step triggerGravity', async ({
		page
	}) => {
		mockBatch.currentStage = 'Ferment';
		mockBatch.status = 'Fermenting';
		mockBatch.measuredOg = 1.054;
		mockBatch.currentGravity = 1.018;
		mockBatch.fermentationSteps[0].triggerGravity = 1.015;

		await page.goto(`/batches/${batchId}`);
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		const triggerBadge = page.locator('[data-testid="fermentation-step-trigger-ready-ferm-1"]');
		await expect(triggerBadge).toBeHidden();

		// Log SG reading 1.014 (<= 1.015 triggerGravity)
		await page.locator('[data-testid="log-sg-reading-btn"]').click();
		const readingModal = page.locator('div[role="dialog"]');
		await readingModal.locator('input[type="number"]').first().fill('1.014');
		await readingModal.locator('[data-testid="submit-reading-btn"]').click();
		await expect(readingModal).toBeHidden();

		await expect(triggerBadge).toBeVisible();
	});
});

test.describe('Printable Recipe Brew Sheet, Past-Date Batch & Offline Progression', () => {
	const recipeId = 'recipe-print-test-1';
	const offlineBatchId = 'batch-offline-test-1';
	const offlineBoilerId = 'boiler-brewlink-offline-1';

	test('scales printable brew sheet and carries target settings to past-date new batch wizard', async ({
		page
	}) => {
		const mockRecipe: RecipeDetailDto = {
			id: recipeId,
			name: 'Nordic Lager',
			description: 'Crisp Bohemian pilsner for brew sheet verification.',
			beerStyle: 'Czech Premium Pale Lager',
			batchSizeLiters: 20,
			originalGravity: 1.049,
			finalGravity: 1.011,
			alcoholByVolume: 5.0,
			bitternessIbu: 36,
			colorSrm: 4,
			isPublic: false,
			createdAt: '2026-01-01T10:00:00Z',
			updatedAt: '2026-01-01T10:00:00Z',
			boilTimeMinutes: 60,
			efficiencyPercent: 75,
			ingredients: [
				{
					id: 'ri-1',
					ingredientId: 'ing-pils',
					ingredientName: 'Bohemian Pilsner Malt',
					ingredientType: 'Fermentable',
					amount: 5.0,
					unit: 'kg',
					usage: 'Mash'
				},
				{
					id: 'ri-2',
					ingredientId: 'ing-saaz',
					ingredientName: 'Saaz',
					ingredientType: 'Hop',
					amount: 40,
					unit: 'g',
					durationMinutes: 60,
					usage: 'Boil',
					form: 'Pellet'
				}
			],
			mashSteps: [
				{
					id: 'rms-1',
					recipeId,
					stepOrder: 1,
					name: 'Beta Amylase Rest',
					type: 'Temperature',
					temperatureC: 63,
					durationMinutes: 45
				}
			],
			fermentationSteps: [
				{
					id: 'rfs-1',
					recipeId,
					stepOrder: 1,
					name: 'Lager Primary',
					type: 'Primary',
					targetTemperatureC: 10,
					durationDays: 14
				}
			]
		};

		const offlineBoiler: EquipmentDto = {
			id: offlineBoilerId,
			brewerySetupId: 'setup-1',
			name: 'Grainfather G30',
			type: 'Boiler',
			subtype: 'AllInOne',
			capacity: 30,
			unit: 'Liters',
			capacityLiters: 30,
			currentVolume: 0,
			currentVolumeLiters: 0,
			boilOffRatePerHour: 3.0,
			trubLossLiters: 1.5,
			mashTunDeadSpaceLiters: 3.5,
			fillPercentage: 0,
			connectionType: 'BrewLink',
			latestMetricsJson: null,
			createdAt: '2026-01-01T10:00:00Z',
			updatedAt: '2026-01-01T10:00:00Z'
		};

		await setupAuthenticatedSession(page);

		await page.route(`**/api/v1/recipes/${recipeId}`, async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: mockRecipe })
			});
		});

		await page.route('**/api/v1/recipes', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: [mockRecipe] })
			});
		});

		await page.route('**/api/v1/ingredients*', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: [] })
			});
		});

		await page.route('**/api/v1/inventory/equipment*', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: [offlineBoiler] })
			});
		});

		await page.route('**/api/v1/batches/next-code*', async (route) => {
			const url = new URL(route.request().url());
			const date = url.searchParams.get('date') || '2026-10-01';
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: { batchCode: `B-${date}-01` } })
			});
		});

		await page.route('**/api/v1/batches/check-stock', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: { hasShortage: false, shortages: [] } })
			});
		});

		// 1. Navigate to Recipe Detail and click Print Brew Sheet button
		await page.goto(`/recipes/${recipeId}`);
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		const printBtn = page.locator('[data-testid="print-recipe-btn"]');
		await expect(printBtn).toBeVisible();
		await printBtn.click();

		await page.waitForURL(`**/recipes/${recipeId}/print`);
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		const sheet = page.locator('[data-testid="printable-brew-sheet"]');
		await expect(sheet).toBeVisible();
		await expect(page.locator('[data-testid="print-mash-steps-table"]')).toBeVisible();
		await expect(page.locator('[data-testid="print-gravity-log-table"]')).toBeVisible();

		// 2. Scale batch size to 40L and switch basis to packaged
		await page.locator('[data-testid="print-batch-size-input"]').fill('40');
		await page.locator('[data-testid="print-volume-basis-select"]').selectOption('packaged');

		await expect(page.locator('[data-testid="print-mash-section"]')).toContainText('10');

		const startBatchLink = page.locator('[data-testid="print-start-batch-btn"]');
		await expect(startBatchLink).toHaveAttribute(
			'href',
			/\/batches\/new\?recipeId=recipe-print-test-1&batchSize=40(?:\.00)?&basis=packaged/
		);
		await startBatchLink.click();

		// 3. Verify Batch New wizard pre-fills batchSize and handles past brew date
		await page.waitForURL('**/batches/new*');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		await expect(page.locator('#batch-size')).toHaveValue('40');

		await page.locator('#brew-date').fill('2025-05-10');
		await page.locator('#brew-date').dispatchEvent('change');

		await expect(page.locator('[data-testid="past-brew-date-notice"]')).toBeVisible();
		await expect(page.locator('#batch-code')).toHaveValue('B-2025-05-10-01');

		// Proceed to Step 2 and confirm BrewLink send toggle is disabled for past date
		await page
			.locator('button')
			.filter({ hasText: /Next|Nästa/i })
			.first()
			.click();
		await page.locator('#boiler-select').selectOption(offlineBoilerId);
		const sendToggle = page.locator('[data-testid="brewlink-send-toggle"]');
		await expect(sendToggle).toBeDisabled();
		await expect(sendToggle).not.toBeChecked();
	});

	test('allows progressing mash steps, advancing stage, timer, and historical metric logging when BrewLink is offline', async ({
		page
	}) => {
		const capturedStageForces: boolean[] = [];
		const capturedTempTimestamps: Array<string | null> = [];
		const capturedReadingPayloads: Array<{
			specificGravity: number;
			temperatureC?: number | null;
			timestamp?: string | null;
		}> = [];

		const offlineBoiler: EquipmentDto = {
			id: offlineBoilerId,
			brewerySetupId: 'setup-1',
			name: 'Grainfather G30',
			type: 'Boiler',
			subtype: 'AllInOne',
			capacity: 30,
			unit: 'Liters',
			capacityLiters: 30,
			currentVolume: 20,
			currentVolumeLiters: 20,
			boilOffRatePerHour: 3.0,
			trubLossLiters: 1.5,
			mashTunDeadSpaceLiters: 3.5,
			fillPercentage: 67,
			connectionType: 'BrewLink',
			latestMetricsJson: JSON.stringify({
				connected: true,
				timer: { active: true, remainingSeconds: 600, totalSeconds: 3600 },
				interaction: { required: true, message: 'Stale prompt' }
			}),
			createdAt: '2026-01-01T10:00:00Z',
			updatedAt: '2026-01-01T10:00:00Z'
		};

		const offlineBatch: BatchDetailDto = {
			id: offlineBatchId,
			userId: 'test-user-1',
			recipeId: 'recipe-test-1',
			recipeName: 'Offline Brew Day Ale',
			batchCode: 'B-2025-05-10-01',
			name: 'Offline Brew Day Ale',
			beerStyle: 'American Pale Ale',
			status: 'Brewing',
			currentStage: 'Mash',
			brewDate: '2025-05-10T09:00:00Z',
			daysActive: 100,
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
			boilerId: offlineBoilerId,
			boilerName: 'Grainfather G30',
			notes: 'Offline paper recovery',
			createdAt: '2025-05-10T09:00:00Z',
			updatedAt: '2025-05-10T09:00:00Z',
			readings: [],
			ingredients: [],
			mashSteps: [
				{
					id: 'omash-1',
					batchId: offlineBatchId,
					stepOrder: 1,
					name: 'Protein Rest',
					type: 'Temperature',
					targetTemperatureC: 50,
					durationMinutes: 20,
					isCompleted: false
				},
				{
					id: 'omash-2',
					batchId: offlineBatchId,
					stepOrder: 2,
					name: 'Saccharification',
					type: 'Temperature',
					targetTemperatureC: 65,
					durationMinutes: 60,
					isCompleted: false
				}
			],
			fermentationSteps: [
				{
					id: 'oferm-1',
					batchId: offlineBatchId,
					stepOrder: 1,
					name: 'Primary',
					type: 'Primary',
					targetTemperatureC: 19,
					durationDays: 10,
					isCompleted: false
				}
			],
			stageHistory: [],
			sensorAssignments: []
		};

		await setupAuthenticatedSession(page);

		await page.route(`**/api/v1/batches/${offlineBatchId}`, async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: offlineBatch })
			});
		});

		await page.route('**/api/v1/inventory/equipment*', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: [offlineBoiler] })
			});
		});

		await page.route(`**/api/v1/batches/${offlineBatchId}/mash-steps/*`, async (route) => {
			const stepId = route.request().url().split('/').pop()?.split('?')[0];
			const postData = route.request().postDataJSON() || {};
			const step = offlineBatch.mashSteps.find((s) => s.id === stepId);
			if (step) {
				step.isCompleted =
					postData.isCompleted !== undefined ? postData.isCompleted : !step.isCompleted;
				if (postData.actualTemperatureC !== undefined) {
					step.actualTemperatureC = postData.actualTemperatureC;
				}
			}
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: step })
			});
		});

		await page.route(`**/api/v1/batches/${offlineBatchId}/stage*`, async (route) => {
			const postData = route.request().postDataJSON() || {};
			capturedStageForces.push(Boolean(postData.force));
			offlineBatch.currentStage = postData.targetStage || 'Boil';
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: offlineBatch })
			});
		});

		await page.route(`**/api/v1/batches/${offlineBatchId}/equipment-readings*`, async (route) => {
			if (route.request().method() === 'POST') {
				const postData = route.request().postDataJSON() || {};
				capturedTempTimestamps.push(postData.timestamp ?? null);
				await route.fulfill({
					status: 200,
					contentType: 'application/json',
					body: JSON.stringify({
						success: true,
						data: {
							id: 'eq-read-1',
							equipmentId: offlineBoilerId,
							equipmentName: 'Grainfather G30',
							batchId: offlineBatchId,
							stage: postData.stage ?? 'Mash',
							batchMashStepId: postData.batchMashStepId ?? null,
							stepName: postData.stepName ?? 'Protein Rest',
							temperatureC: postData.temperatureC,
							timestamp: postData.timestamp ?? '2025-05-10T10:15:00Z',
							source: 'Manual',
							targetTemperatureC: postData.targetTemperatureC ?? null,
							notes: postData.notes ?? null
						}
					})
				});
				return;
			}
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: [] })
			});
		});

		await page.route(`**/api/v1/batches/${offlineBatchId}/readings*`, async (route) => {
			if (route.request().method() === 'POST') {
				const postData = route.request().postDataJSON() || {};
				capturedReadingPayloads.push(postData);
				const reading = {
					id: 'read-offline-1',
					batchId: offlineBatchId,
					timestamp: postData.timestamp ?? '2025-05-10T16:30:00Z',
					specificGravity: postData.specificGravity,
					temperatureC: postData.temperatureC ?? null,
					notes: postData.notes ?? null,
					createdAt: '2025-05-10T16:30:00Z',
					alcoholByVolume: 5.2
				};
				offlineBatch.readings.push(reading);
				offlineBatch.currentGravity = reading.specificGravity;
				await route.fulfill({
					status: 201,
					contentType: 'application/json',
					body: JSON.stringify({ success: true, data: reading })
				});
				return;
			}
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: [] })
			});
		});

		await page.goto(`/batches/${offlineBatchId}`);
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		// Offline/past-date BrewLink equipment shows informative banner, hides stale hardware prompts, and does NOT block stage advancement
		await expect(page.locator('[data-testid="offline-equipment-notice"]')).toBeVisible();
		await expect(page.locator('[data-testid="advance-stage-blocked-banner"]')).toBeHidden();
		await expect(page.getByText('Stale prompt')).toBeHidden();

		// Local timer starts cleanly even with assigned BrewLink equipment offline
		const timerPanel = page.locator('[data-testid="mash-timer-panel"]');
		await timerPanel.getByRole('button', { name: /Start|Starta/i }).click();

		// Advance Mash Step 1 directly without hardware confirm modal
		await page.locator('[data-testid="advance-mash-step-btn"]').click();
		await expect(page.locator('[data-testid="mash-step-checkbox-omash-1"]')).toBeChecked();
		await expect(page.locator('[data-testid="active-step-name"]')).toHaveText('Saccharification');

		// Log step temperature with historical timestamp for active step 2
		const step2Row = page.locator('[data-testid="mash-step-row-omash-2"]');
		await step2Row.locator('button[title]').first().click();
		const tempModal = page.locator('div[role="dialog"]');
		await tempModal.locator('[data-testid="log-temp-timestamp-input"]').fill('2025-05-10T10:15');
		await tempModal.locator('button[type="submit"]').click();
		await expect(tempModal).toBeHidden();
		expect(capturedTempTimestamps).toHaveLength(1);
		expect(capturedTempTimestamps[0]).toContain('2025-05-10');

		// Advance stage from Mash -> Boil even though Mash step 2 is incomplete (forced offline override)
		await page.locator('[data-testid="advance-stage-btn"]').click();
		await page.locator('[data-testid="confirm-advance-stage-btn"]').click();
		expect(capturedStageForces).toContain(true);
		await expect(page.locator('[data-testid="stage-node-boil"]')).toHaveAttribute(
			'aria-current',
			'step'
		);
		await expect(page.locator('[data-testid="boil-brewlink-interaction-banner"]')).toBeHidden();

		// Advance stage from Boil -> Ferment without triggering stale active-boil-timer confirmation modal
		await page.locator('[data-testid="advance-stage-btn"]').click();
		await page.locator('[data-testid="confirm-advance-stage-btn"]').click();
		await expect(page.locator('[data-testid="stage-node-ferment"]')).toHaveAttribute(
			'aria-current',
			'step'
		);

		// Log gravity reading with temperature & historical timestamp
		await page.locator('[data-testid="log-sg-reading-btn"]').click();
		const sgModal = page.locator('div[role="dialog"]');
		await sgModal.locator('input[type="number"]').first().fill('1.050');
		await sgModal.locator('[data-testid="reading-temp-input"]').fill('20.5');
		await sgModal.locator('[data-testid="reading-timestamp-input"]').fill('2025-05-10T16:30');
		await sgModal.locator('[data-testid="submit-reading-btn"]').click();
		await expect(sgModal).toBeHidden();

		expect(capturedReadingPayloads).toHaveLength(1);
		expect(capturedReadingPayloads[0].specificGravity).toBe(1.05);
		expect(capturedReadingPayloads[0].temperatureC).toBe(20.5);
		expect(capturedReadingPayloads[0].timestamp).toContain('2025-05-10');
	});

	test('hides equipment occupancy badges and warnings for past brew dates and allows starting with occupied vessels', async ({
		page
	}) => {
		const occupiedBoilerId = 'eq-occupied-boiler-1';
		const occupiedFermenterId = 'eq-occupied-fermenter-1';
		const occupiedKegId = 'eq-occupied-keg-1';
		const capturedCreatePayloads: Array<Record<string, unknown>> = [];

		const occupiedBoiler: EquipmentDto = {
			id: occupiedBoilerId,
			brewerySetupId: 'setup-1',
			name: 'Busy Kettle 40L',
			type: 'Boiler',
			subtype: 'AllInOne',
			capacity: 40,
			unit: 'Liters',
			capacityLiters: 40,
			currentVolume: 28,
			currentVolumeLiters: 28,
			boilOffRatePerHour: 3.0,
			trubLossLiters: 1.5,
			mashTunDeadSpaceLiters: 3.5,
			fillPercentage: 70,
			connectionType: 'None',
			latestMetricsJson: null,
			createdAt: '2026-01-01T10:00:00Z',
			updatedAt: '2026-01-01T10:00:00Z'
		};

		const occupiedFermenter: EquipmentDto = {
			id: occupiedFermenterId,
			brewerySetupId: 'setup-1',
			name: 'Busy Fermenter 30L',
			type: 'Fermenter',
			subtype: 'ConicalFermenter',
			capacity: 30,
			unit: 'Liters',
			capacityLiters: 30,
			currentVolume: 20,
			currentVolumeLiters: 20,
			fillPercentage: 67,
			connectionType: 'None',
			latestMetricsJson: null,
			createdAt: '2026-01-01T10:00:00Z',
			updatedAt: '2026-01-01T10:00:00Z'
		};

		const occupiedKeg: EquipmentDto = {
			id: occupiedKegId,
			brewerySetupId: 'setup-1',
			name: 'Busy Corny Keg 19L',
			type: 'Keg',
			subtype: 'Cornelius',
			capacity: 19,
			unit: 'Liters',
			capacityLiters: 19,
			currentVolume: 18,
			currentVolumeLiters: 18,
			packagingLossLiters: 0.5,
			fillPercentage: 95,
			connectionType: 'None',
			latestMetricsJson: null,
			createdAt: '2026-01-01T10:00:00Z',
			updatedAt: '2026-01-01T10:00:00Z'
		};

		const createdPastBatch: BatchDetailDto = {
			id: 'past-occupied-batch-1',
			userId: 'test-user-1',
			batchCode: 'B-2025-05-10-01',
			name: 'Past Batch With Occupied Vessels',
			beerStyle: 'American IPA',
			status: 'Brewing',
			currentStage: 'Mash',
			brewDate: '2025-05-10T00:00:00Z',
			daysActive: 100,
			targetOg: 1.05,
			targetFg: 1.01,
			targetAbv: 5.25,
			targetIbu: 35,
			targetColorSrm: 6,
			targetBatchSizeLiters: 20,
			boilTimeMinutes: 60,
			efficiencyPercent: 72,
			boilerId: occupiedBoilerId,
			fermenterId: occupiedFermenterId,
			packagingVesselId: occupiedKegId,
			createdAt: '2025-05-10T00:00:00Z',
			updatedAt: '2025-05-10T00:00:00Z',
			readings: [],
			ingredients: [],
			mashSteps: [],
			fermentationSteps: [],
			stageHistory: [],
			sensorAssignments: []
		};

		await setupAuthenticatedSession(page);

		await page.route('**/api/v1/recipes*', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: [] })
			});
		});

		await page.route('**/api/v1/ingredients*', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: [] })
			});
		});

		await page.route('**/api/v1/inventory/equipment*', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: [occupiedBoiler, occupiedFermenter, occupiedKeg]
				})
			});
		});

		await page.route('**/api/v1/batches/next-code*', async (route) => {
			const url = new URL(route.request().url());
			const date = url.searchParams.get('date') || '2026-11-14';
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: { batchCode: `B-${date}-01` } })
			});
		});

		await page.route('**/api/v1/batches/check-stock', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: { hasShortage: false, shortages: [] } })
			});
		});

		await page.route('**/api/v1/batches', async (route) => {
			if (route.request().method() === 'POST') {
				const postData = route.request().postDataJSON() as Record<string, unknown>;
				capturedCreatePayloads.push(postData);
				await route.fulfill({
					status: 201,
					contentType: 'application/json',
					body: JSON.stringify({
						success: true,
						data: createdPastBatch
					})
				});
				return;
			}
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: [] })
			});
		});

		await page.route('**/api/v1/batches/past-occupied-batch-1*', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: createdPastBatch
				})
			});
		});

		await page.goto('/batches/new');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		// Step 1: Ad-hoc mode with today's date first
		await page.locator('#batch-name').fill('Past Batch With Occupied Vessels');
		await page
			.locator('button')
			.filter({ hasText: /Next|Nästa/i })
			.first()
			.click();

		// Step 2 (today's date): selecting occupied vessels shows In Use / Occupied badges and warning
		await page.locator('#boiler-select').selectOption(occupiedBoilerId);
		await page.locator('#fermenter-select').selectOption(occupiedFermenterId);
		await page.locator('#packaging-vessel-select').selectOption(occupiedKegId);

		await expect(page.locator('#boiler-summary')).toContainText(/In Use|Används/i);
		await expect(page.locator('#fermenter-summary')).toContainText(/Occupied|Upptagen/i);
		await expect(page.locator('#packaging-vessel-summary')).toContainText(/In Use|Används/i);
		await expect(page.locator('[data-testid="fermenter-occupied-warning"]')).toBeVisible();

		// Go back to Step 1 and switch Brew Date to a past date
		await page
			.locator('button')
			.filter({ hasText: /Previous|Back|Föregående|Tillbaka/i })
			.first()
			.click();
		await page.locator('#brew-date').fill('2025-05-10');
		await page.locator('#brew-date').dispatchEvent('change');
		await expect(page.locator('[data-testid="past-brew-date-notice"]')).toBeVisible();

		await page
			.locator('button')
			.filter({ hasText: /Next|Nästa/i })
			.first()
			.click();

		// Step 2 (past date): occupied badges, suffixes, and warning are hidden for the same vessels
		await expect(page.locator('#boiler-summary')).not.toContainText(/In Use|Används/i);
		await expect(page.locator('#fermenter-summary')).not.toContainText(/Occupied|Upptagen/i);
		await expect(page.locator('#packaging-vessel-summary')).not.toContainText(/In Use|Används/i);
		await expect(page.locator('[data-testid="fermenter-occupied-warning"]')).toBeHidden();
		await expect(page.locator('#boiler-select option:checked')).not.toContainText(
			/In use|Används/i
		);
		await expect(page.locator('#fermenter-select option:checked')).not.toContainText(
			/Occupied|Upptagen/i
		);

		// Step 3: Submit batch and confirm occupied vessels are accepted for past date
		await page
			.locator('button')
			.filter({ hasText: /Next|Nästa/i })
			.first()
			.click();
		await page
			.locator('button')
			.filter({ hasText: /Start Batch|Starta bryggning/i })
			.first()
			.click();

		await page.waitForURL('**/batches/past-occupied-batch-1');
		expect(capturedCreatePayloads).toHaveLength(1);
		expect(capturedCreatePayloads[0].boilerId).toBe(occupiedBoilerId);
		expect(capturedCreatePayloads[0].fermenterId).toBe(occupiedFermenterId);
		expect(capturedCreatePayloads[0].packagingVesselId).toBe(occupiedKegId);
		expect(capturedCreatePayloads[0].brewDate).toBe('2025-05-10');
	});
});
