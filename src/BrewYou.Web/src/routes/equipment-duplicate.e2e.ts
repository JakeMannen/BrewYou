import { expect, test } from '@playwright/test';

test.describe('Equipment Duplicate Name Prevention', () => {
	const mockSetupId = '00000000-0000-0000-0000-000000000001';

	test.beforeEach(async ({ page }) => {
		// Mock authentication so the equipment page renders full authenticated view
		await page.route('**/api/v1/auth/me', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: {
						id: 'user-test-1',
						email: 'brewer@brewyou.test',
						displayName: 'Test Brewer',
						preferredLanguage: 'en',
						preferredVolumeUnit: 'Liters'
					}
				})
			});
		});

		// Mock auth refresh
		await page.route('**/api/v1/auth/refresh', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: {
						accessToken: 'test-token-value',
						user: {
							id: 'user-test-1',
							email: 'brewer@brewyou.test',
							displayName: 'Test Brewer',
							preferredLanguage: 'en',
							preferredVolumeUnit: 'Liters'
						}
					}
				})
			});
		});

		// Mock setups
		await page.route('**/api/v1/brewery-setups', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: [
						{
							id: mockSetupId,
							name: 'Brewery 1',
							description: 'Default setup',
							isDefault: true,
							createdAt: '2026-01-01T00:00:00Z',
							updatedAt: '2026-01-01T00:00:00Z'
						}
					]
				})
			});
		});

		// Mock equipment list with one existing item and support POST
		await page.route('**/api/v1/inventory/equipment*', async (route) => {
			if (route.request().method() === 'POST') {
				const body = route.request().postDataJSON();
				// If brewerySetupId is 'default-1', simulate 404 error
				if (!body.brewerySetupId || body.brewerySetupId === 'default-1') {
					await route.fulfill({
						status: 404,
						contentType: 'application/json',
						body: JSON.stringify({
							success: false,
							error: { code: 'NOT_FOUND', message: 'Brewery setup not found.' }
						})
					});
					return;
				}
				await route.fulfill({
					status: 201,
					contentType: 'application/json',
					body: JSON.stringify({
						success: true,
						data: {
							id: 'eq-new-1',
							brewerySetupId: body.brewerySetupId,
							name: body.name,
							type: body.type,
							subtype: body.subtype ?? 'AllInOne',
							capacity: body.capacity,
							unit: body.unit ?? 'Liters',
							capacityLiters: body.capacity,
							currentVolume: body.currentVolume ?? 0,
							currentVolumeLiters: body.currentVolume ?? 0,
							fillPercentage: 0,
							description: body.description ?? null,
							notes: body.notes ?? null,
							createdAt: new Date().toISOString(),
							updatedAt: new Date().toISOString()
						}
					})
				});
				return;
			}

			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: [
						{
							id: 'eq-existing-1',
							brewerySetupId: mockSetupId,
							name: 'Grainfather G40',
							type: 'Boiler',
							subtype: 'AllInOne',
							capacity: 40,
							unit: 'Liters',
							capacityLiters: 40,
							currentVolume: 0,
							currentVolumeLiters: 0,
							fillPercentage: 0,
							description: 'Electric all-in-one system',
							notes: null,
							createdAt: '2026-01-01T00:00:00Z',
							updatedAt: '2026-01-01T00:00:00Z'
						}
					],
					pagination: {
						page: 1,
						limit: 20,
						total: 1,
						totalPages: 1
					}
				})
			});
		});

		// Prime localStorage token so auth store initializes before page loads
		await page.addInitScript((setupId) => {
			localStorage.setItem('brewyou_token', 'test-token-value');
			localStorage.setItem(
				'brewyou_setups',
				JSON.stringify([
					{
						id: setupId,
						name: 'Brewery 1',
						description: 'Default setup',
						isDefault: true
					}
				])
			);
			localStorage.setItem('brewyou_active_setup_id', setupId);
		}, mockSetupId);

		await page.goto('/inventory/equipment');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });
	});

	test('detects duplicate equipment name in real-time, displays accessible alert, and disables submit', async ({
		page
	}) => {
		// Open Add Equipment modal
		const addBtn = page.locator('[data-testid="add-equipment-btn"]');
		await expect(addBtn).toBeVisible();
		await addBtn.click();

		const nameInput = page.locator('#equipment-name');
		const submitBtn = page.locator('[data-testid="save-equipment-btn"]');

		// Fill fields
		await page.locator('#equipment-capacity').fill('40');

		// Type exact duplicate name
		await nameInput.fill('Grainfather G40');

		// Assert inline error alert is visible
		const errorMsg = page.locator('#equipment-name-error');
		await expect(errorMsg).toBeVisible();
		await expect(errorMsg).toContainText(
			'An equipment item with this name already exists in this setup.'
		);

		// Assert aria-invalid is set on input
		await expect(nameInput).toHaveAttribute('aria-invalid', 'true');
		await expect(nameInput).toHaveAttribute('aria-errormessage', 'equipment-name-error');

		// Assert submit button is disabled
		await expect(submitBtn).toBeDisabled();

		// Test case-insensitivity: lowercase
		await nameInput.fill('grainfather g40');
		await expect(errorMsg).toBeVisible();
		await expect(submitBtn).toBeDisabled();

		// Test whitespace trimming
		await nameInput.fill('   Grainfather G40   ');
		await expect(errorMsg).toBeVisible();
		await expect(submitBtn).toBeDisabled();

		// Change to unique name
		await nameInput.fill('Grainfather G70');

		// Error should disappear and submit should become enabled
		await expect(errorMsg).toHaveCount(0);
		await expect(nameInput).toHaveAttribute('aria-invalid', 'false');
		await expect(submitBtn).toBeEnabled();
	});

	test('displays Swedish duplicate error message when Swedish locale is selected', async ({
		page
	}) => {
		// Mock Swedish user language
		await page.route('**/api/v1/auth/me', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: {
						id: 'user-test-1',
						email: 'brewer@brewyou.test',
						displayName: 'Test Brewer',
						preferredLanguage: 'sv',
						preferredVolumeUnit: 'Liters'
					}
				})
			});
		});

		// Switch to Swedish in localStorage and reload
		await page.evaluate(() => {
			localStorage.setItem('brewyou_language', 'sv');
		});
		await page.reload();
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		// Open Add Equipment modal
		await page.locator('[data-testid="add-equipment-btn"]').click();
		const nameInput = page.locator('#equipment-name');
		await nameInput.fill('Grainfather G40');

		// Swedish error should display
		const errorMsg = page.locator('#equipment-name-error');
		await expect(errorMsg).toBeVisible();
		await expect(errorMsg).toContainText(
			'En utrustning med detta namn finns redan i denna uppsättning.'
		);
	});

	test('newly logged in user can add equipment immediately without refreshing the page', async ({
		page
	}) => {
		// Navigate to equipment page
		await page.goto('/inventory/equipment');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		// Open Add Equipment modal
		await page.locator('[data-testid="add-equipment-btn"]').click();
		const modalTitle = page.locator('#equipment-modal-title');
		await expect(modalTitle).toBeVisible();

		// Fill in equipment details
		const nameInput = page.locator('#equipment-name');
		await nameInput.fill('SS Brewtech Unitank 30L');

		// Submit the modal
		const submitBtn = page.locator('button[type="submit"]');
		await expect(submitBtn).toBeEnabled();
		await submitBtn.click();

		// The modal should close successfully without 404 Brewery setup not found error
		await expect(modalTitle).toHaveCount(0);
	});

	test('automatically preselects equipment category when opened from type-specific tab', async ({
		page
	}) => {
		await page.goto('/inventory/equipment');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		// Click the Fermenters tab
		const fermenterTab = page.locator('[data-testid="filter-tab-fermenter"]');
		await fermenterTab.click();

		// Open Add Equipment modal
		await page.locator('[data-testid="add-equipment-btn"]').click();
		await page.locator('#equipment-modal-title').waitFor({ state: 'visible' });

		// Fermenter button in modal should be active/selected
		const fermenterCategoryBtn = page.locator('[data-testid="category-tab-fermenter"]');
		await expect(fermenterCategoryBtn).toHaveClass(/border-amber-500/);

		// Press Escape to close modal
		await page.keyboard.press('Escape');
		await page.locator('#equipment-modal-title').waitFor({ state: 'detached' });

		// Click the Kegs tab
		const kegTab = page.locator('[data-testid="filter-tab-keg"]');
		await kegTab.click();

		// Open Add Equipment modal again
		await page.locator('[data-testid="add-equipment-btn"]').click();
		await page.locator('#equipment-modal-title').waitFor({ state: 'visible' });

		// Keg button in modal should be active/selected
		const kegCategoryBtn = page.locator('[data-testid="category-tab-keg"]');
		await expect(kegCategoryBtn).toHaveClass(/border-amber-500/);

		// Press Escape to close modal
		await page.keyboard.press('Escape');
		await page.locator('#equipment-modal-title').waitFor({ state: 'detached' });

		// Click the All tab
		const allTab = page.locator('[data-testid="filter-tab-all"]');
		await allTab.click();

		// Open Add Equipment modal again
		await page.locator('[data-testid="add-equipment-btn"]').click();
		await page.locator('#equipment-modal-title').waitFor({ state: 'visible' });

		// Boiler button in modal should be active/selected by default
		const boilerCategoryBtn = page.locator('[data-testid="category-tab-boiler"]');
		await expect(boilerCategoryBtn).toHaveClass(/border-amber-500/);
	});
});
