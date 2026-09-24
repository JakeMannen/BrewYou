import { expect, test, type Page } from '@playwright/test';
import { setupAuthenticatedSession } from './fixtures/auth';

async function getSwitcherContainer(page: Page) {
	const viewport = page.viewportSize();
	const isSmallScreen = viewport ? viewport.width < 768 : false;
	if (isSmallScreen) {
		const mobileToggle = page.locator('button[aria-controls="mobile-nav-drawer"]');
		await expect(async () => {
			const isExpanded = (await mobileToggle.getAttribute('aria-expanded')) === 'true';
			if (!isExpanded) {
				await mobileToggle.click();
			}
			await expect(page.locator('[data-testid="mobile-nav-drawer"]')).toBeVisible({
				timeout: 1000
			});
		}).toPass({ timeout: 5000 });
		return page.locator('[data-testid="mobile-nav-drawer"]');
	} else {
		return page.locator('#brewery-sidebar');
	}
}

test.describe('Brewery Setup Switcher & Multi-Profile Management', () => {
	test.beforeEach(async ({ page }) => {
		await setupAuthenticatedSession(page);
		await page.goto('/');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });
	});

	test('renders default "Brewery 1" and replaces hardcoded "The Brew Shed"', async ({ page }) => {
		// "The Brew Shed" should no longer exist in the sidebar
		await expect(page.getByText('The Brew Shed')).toHaveCount(0);

		// Default setup name should be "Brewery 1"
		const container = await getSwitcherContainer(page);
		const activeName = container.locator('[data-testid="active-brewery-name"]');
		await expect(activeName).toBeVisible();
		await expect(activeName).toHaveText('Brewery 1');
	});

	test('allows adding a new setup, renaming it, and persisting across page reload', async ({
		page
	}) => {
		let container = await getSwitcherContainer(page);
		let trigger = container.locator('[data-testid="brewery-switcher-trigger"]');

		// Open dropdown
		await trigger.click();
		let menu = container.locator('[data-testid="brewery-switcher-menu"]');
		await expect(menu).toBeVisible();

		// Click Add Brewery
		const addBtn = menu.locator('[data-testid="add-brewery-btn"]');
		await expect(addBtn).toBeVisible();
		await addBtn.click();

		// Add Modal
		const nameInput = page.locator('[data-testid="new-brewery-name-input"]');
		await expect(nameInput).toBeVisible();
		await nameInput.fill('Pilot Shed 20L');
		await page.locator('[data-testid="submit-create-brewery-btn"]').click();
		await expect(page.locator('[data-testid="new-brewery-name-input"]')).toBeHidden();

		// On mobile, submitting adds & closes drawer, so re-fetch container
		container = await getSwitcherContainer(page);
		let activeName = container.locator('[data-testid="active-brewery-name"]');
		await expect(activeName).toHaveText('Pilot Shed 20L');

		// Rename setup
		trigger = container.locator('[data-testid="brewery-switcher-trigger"]');
		await trigger.click();
		menu = container.locator('[data-testid="brewery-switcher-menu"]');
		await expect(menu).toBeVisible();

		// Find the rename button for the active setup
		const renameBtn = menu.locator('[data-testid^="rename-brewery-btn-"]').last();
		await renameBtn.click();

		const renameInput = page.locator('[data-testid="rename-brewery-input"]');
		await expect(renameInput).toBeVisible();
		await renameInput.fill('Craft Pilot 20L');
		await page.locator('[data-testid="submit-rename-brewery-btn"]').click();
		await expect(page.locator('[data-testid="rename-brewery-input"]')).toBeHidden();

		// Active name should reflect updated name
		container = await getSwitcherContainer(page);
		activeName = container.locator('[data-testid="active-brewery-name"]');
		await expect(activeName).toHaveText('Craft Pilot 20L');

		// Persistence check across reload
		await page.reload();
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });
		container = await getSwitcherContainer(page);
		await expect(container.locator('[data-testid="active-brewery-name"]')).toHaveText(
			'Craft Pilot 20L'
		);
	});

	test('switches between setups and deletes a setup with confirmation dialog', async ({ page }) => {
		let container = await getSwitcherContainer(page);
		let trigger = container.locator('[data-testid="brewery-switcher-trigger"]');

		// Add second setup
		await trigger.click();
		await container.locator('[data-testid="add-brewery-btn"]').click();
		await page.locator('[data-testid="new-brewery-name-input"]').fill('Second Rig');
		await page.locator('[data-testid="submit-create-brewery-btn"]').click();
		await expect(page.locator('[data-testid="new-brewery-name-input"]')).toBeHidden();

		// Re-fetch container after addition
		container = await getSwitcherContainer(page);
		let activeName = container.locator('[data-testid="active-brewery-name"]');
		await expect(activeName).toHaveText('Second Rig');

		// Switch back to Brewery 1
		trigger = container.locator('[data-testid="brewery-switcher-trigger"]');
		await trigger.click();
		const brewery1Option = container.locator('[data-testid^="brewery-option-"]').first();
		await brewery1Option.getByRole('button').first().click();

		// Re-fetch container after selection
		container = await getSwitcherContainer(page);
		activeName = container.locator('[data-testid="active-brewery-name"]');
		await expect(activeName).toHaveText('Brewery 1');

		// Open menu and delete "Second Rig" with confirmation
		trigger = container.locator('[data-testid="brewery-switcher-trigger"]');
		await trigger.click();
		const deleteBtns = container.locator('[data-testid^="delete-brewery-btn-"]');
		await expect(deleteBtns).toHaveCount(2);

		// Click delete on second setup
		await deleteBtns.last().click();

		// Delete Confirmation Modal appears
		const warning = page.locator('[data-testid="delete-modal-warning"]');
		await expect(warning).toBeVisible();
		await expect(warning).toContainText('Second Rig');

		// Confirm deletion
		await page.locator('[data-testid="confirm-delete-brewery-btn"]').click();
		await expect(page.locator('[data-testid="delete-modal-warning"]')).toBeHidden();

		// After deletion, active is Brewery 1 and sole brewery cannot be deleted
		container = await getSwitcherContainer(page);
		activeName = container.locator('[data-testid="active-brewery-name"]');
		await expect(activeName).toHaveText('Brewery 1');

		trigger = container.locator('[data-testid="brewery-switcher-trigger"]');
		await trigger.click();
		const remainingDeleteBtns = container.locator('[data-testid^="delete-brewery-btn-"]');
		await expect(remainingDeleteBtns).toHaveCount(0);
	});

	test('displays active setup context on equipment page and updates when switching setups', async ({
		page
	}) => {
		await page.goto('/inventory/equipment');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		// Check active setup badge on equipment page
		await expect(page.getByText('Active Setup: Brewery 1')).toBeVisible();

		// Switch setup to "Pilot Rig"
		const container = await getSwitcherContainer(page);
		const trigger = container.locator('[data-testid="brewery-switcher-trigger"]');
		await trigger.click();
		await container.locator('[data-testid="add-brewery-btn"]').click();
		await page.locator('[data-testid="new-brewery-name-input"]').fill('Pilot Rig');
		await page.locator('[data-testid="submit-create-brewery-btn"]').click();
		await expect(page.locator('[data-testid="new-brewery-name-input"]')).toBeHidden();

		// Equipment page header badge should reactively update to Pilot Rig
		await expect(page.getByText('Active Setup: Pilot Rig')).toBeVisible();
	});
});
