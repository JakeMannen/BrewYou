import { expect, test } from '@playwright/test';

test.describe('Equipment Single-Unit Display & Delete Confirmation Modal', () => {
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
							equipmentCount: 1,
							createdAt: '2026-01-01T00:00:00Z',
							updatedAt: '2026-01-01T00:00:00Z'
						}
					]
				})
			});
		});

		// Mock active batches for equipment (must match before general equipment route or with glob pattern)
		await page.route('**/api/v1/inventory/equipment/*/active-batches', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: [
						{
							id: 'batch-999',
							batchCode: 'B-2026-09-07-01',
							name: 'Citra Single Hop IPA',
							status: 'Fermenting',
							currentStage: 'Ferment'
						}
					]
				})
			});
		});

		// Mock equipment list with one existing item
		await page.route('**/api/v1/inventory/equipment?*', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: [
						{
							id: 'eq-fermenter-1',
							brewerySetupId: mockSetupId,
							name: 'Conical Fermenter 1',
							type: 'Fermenter',
							subtype: 'ConicalFermenter',
							capacity: 30,
							unit: 'Liters',
							capacityLiters: 30,
							currentVolume: 20,
							currentVolumeLiters: 20,
							fillPercentage: 67,
							description: 'Primary fermenter',
							notes: null,
							createdAt: '2026-01-01T00:00:00Z',
							updatedAt: '2026-01-01T00:00:00Z'
						}
					]
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

	test('Equipment card displays only the user selected unit without dual secondary unit', async ({
		page
	}) => {
		await expect(page.locator('main h1')).toContainText('Equipment Inventory');

		// The card must show 20.0 L / 30.0 L
		await expect(page.locator('text=20.0 L')).toBeVisible();
		await expect(page.locator('text=30.0 L')).toBeVisible();

		// And MUST NOT show dual unit conversions (e.g. "gal" in Liters mode)
		const card = page.locator('[data-testid="equipment-card"]').first();
		await expect(card).toBeVisible();
		const cardText = await card.innerText();
		expect(cardText).not.toContain('gal');
	});

	test('Clicking delete button opens DeleteEquipmentModal with active batch warning and batch link', async ({
		page
	}) => {
		let dialogOpened = false;
		page.on('dialog', () => {
			dialogOpened = true;
		});

		await expect(page.locator('main h1')).toContainText('Equipment Inventory');

		// Click the delete button on the fermenter card
		const deleteBtn = page.locator('[data-testid="delete-equipment-btn"]').first();
		await deleteBtn.click();

		// Ensure NO native browser alert/confirm fired
		expect(dialogOpened).toBe(false);

		// Ensure the custom DeleteEquipmentModal opened
		const modal = page.locator('div[role="dialog"]');
		await expect(modal).toBeVisible();
		await expect(modal.locator('#delete-equipment-modal-title')).toContainText('Delete Equipment');

		// Verify active batch warning alert is displayed
		await expect(modal.locator('text=Active Batch Warning')).toBeVisible();
		await expect(modal.locator('text=Citra Single Hop IPA')).toBeVisible();
		await expect(modal.locator('text=(B-2026-09-07-01)')).toBeVisible();

		// Verify link to batch
		const batchLink = modal.locator('a[href="/batches/batch-999"]');
		await expect(batchLink).toBeVisible();
		await expect(batchLink).toContainText('View Batch');

		// Test Cancel closes the modal
		const cancelBtn = modal.locator('[data-testid="cancel-delete-btn"]');
		await cancelBtn.click();
		await expect(modal).not.toBeVisible();
	});
});
