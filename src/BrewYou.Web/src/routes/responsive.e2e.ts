import { expect, test } from '@playwright/test';
import { setupAuthenticatedSession } from './fixtures/auth';

test.describe('Responsive Navigation & Drawer Lifecycle', () => {
	test.beforeEach(async ({ page }) => {
		await setupAuthenticatedSession(page);
	});
	test('adapts navigation elements based on device viewport', async ({ page }) => {
		await page.goto('/');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		const desktopNav = page.locator('nav[aria-label="Main Navigation"]');
		const mobileToggle = page.locator('button[aria-controls="mobile-nav-drawer"]');
		const viewport = page.viewportSize();
		const isSmallScreen = viewport ? viewport.width < 768 : false;

		if (isSmallScreen) {
			await expect(desktopNav).toBeHidden();
			await expect(mobileToggle).toBeVisible();

			// Open drawer
			await mobileToggle.click();
			await expect(mobileToggle).toHaveAttribute('aria-expanded', 'true');
			const drawer = page.locator('[data-testid="mobile-nav-drawer"]');
			await expect(drawer).toBeVisible();

			// Close via Escape key
			await page.keyboard.press('Escape');
			await expect(drawer).toBeHidden();
			await expect(mobileToggle).toHaveAttribute('aria-expanded', 'false');
		} else {
			await expect(desktopNav).toBeVisible();
			await expect(mobileToggle).toBeHidden();
		}
	});

	test('navigates and auto-closes drawer on mobile link click', async ({ page }) => {
		const viewport = page.viewportSize();
		if (!viewport || viewport.width >= 768) {
			test.skip(true, 'Test is for mobile/tablet portrait viewports under 768px');
		}

		await page.goto('/');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });
		const mobileToggle = page.locator('button[aria-controls="mobile-nav-drawer"]');
		await mobileToggle.click();

		const drawer = page.locator('[data-testid="mobile-nav-drawer"]');
		await expect(drawer).toBeVisible();

		// Click recipes link in mobile drawer
		const recipesLink = drawer.getByRole('link', { name: /recipes|recept/i });
		await recipesLink.click();

		await expect(page).toHaveURL(/\/recipes$/);
		await expect(drawer).toBeHidden();
	});

	test('navigates to /batches and auto-closes drawer on mobile link click', async ({ page }) => {
		const viewport = page.viewportSize();
		if (!viewport || viewport.width >= 768) {
			test.skip(true, 'Test is for mobile/tablet portrait viewports under 768px');
		}

		await page.goto('/');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });
		const mobileToggle = page.locator('button[aria-controls="mobile-nav-drawer"]');
		await mobileToggle.click();

		const drawer = page.locator('[data-testid="mobile-nav-drawer"]');
		await expect(drawer).toBeVisible();

		// Click batches link in mobile drawer
		const batchesLink = drawer.getByRole('link', { name: /batches|bryggningar/i });
		await batchesLink.click();

		await expect(page).toHaveURL(/\/batches$/);
		await expect(drawer).toBeHidden();
	});
});

test.describe('Multi-Device Horizontal Overflow Defense', () => {
	const routes = [
		'/',
		'/recipes',
		'/recipes/new',
		'/batches',
		'/ingredients',
		'/login',
		'/register',
		'/settings'
	];

	for (const route of routes) {
		test(`page ${route} does not cause horizontal overflow on viewport`, async ({ page }) => {
			await page.goto(route);
			await page.waitForLoadState('domcontentloaded');

			const overflow = await page.evaluate(() => {
				const docWidth = document.documentElement.clientWidth;
				const scrollWidth = document.documentElement.scrollWidth;
				return {
					hasOverflow: scrollWidth > docWidth,
					docWidth,
					scrollWidth
				};
			});

			expect(
				overflow.hasOverflow,
				`Horizontal overflow on ${route}: scrollWidth=${overflow.scrollWidth}px > clientWidth=${overflow.docWidth}px`
			).toBe(false);
		});
	}
});

test.describe('Formulator Live Metrics HUD Responsiveness', () => {
	test('switches between compact mobile bar and full desktop grid', async ({ page }) => {
		await page.goto('/recipes/new');
		await page.waitForLoadState('domcontentloaded');

		const viewport = page.viewportSize();
		const isSmallScreen = viewport ? viewport.width < 768 : false;

		const fullHud = page.locator('.md\\:grid');
		const detailsBtn = page.getByRole('button', { name: /details|detaljer|dölj|hide/i });

		if (isSmallScreen) {
			await expect(fullHud).toBeHidden();
			await expect(detailsBtn).toBeVisible();

			// Toggle details
			await detailsBtn.click();
			await expect(detailsBtn).toHaveAttribute('aria-expanded', 'true');

			await detailsBtn.click();
			await expect(detailsBtn).toHaveAttribute('aria-expanded', 'false');
		} else {
			await expect(fullHud).toBeVisible();
			await expect(detailsBtn).toBeHidden();
		}
	});
});
