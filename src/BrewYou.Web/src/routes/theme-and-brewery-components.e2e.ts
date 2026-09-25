import { expect, test } from '@playwright/test';
import { setupAuthenticatedSession } from './fixtures/auth';

test.beforeEach(async ({ page }) => {
	await setupAuthenticatedSession(page);
});

test.describe('Theme Switching & Persistence', () => {
	test('toggles theme between dark and light on settings page, updating DOM and localStorage', async ({
		page
	}) => {
		await page.goto('/settings');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		// Check initial theme is dark by default
		const html = page.locator('html');
		await expect(html).toHaveClass(/dark/);

		// Switch to Light theme on settings page
		const lightThemeBtn = page.locator('[data-testid="theme-card-light"]');
		await lightThemeBtn.click();

		// Assert dark class is removed
		await expect(html).not.toHaveClass(/dark/);
		await expect(html).toHaveAttribute('data-theme', 'light');

		// Check localStorage
		const storedTheme = await page.evaluate(() => localStorage.getItem('brewyou-theme'));
		expect(storedTheme).toBe('light');

		// Reload page to verify persistence without FOUC
		await page.reload();
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });
		await expect(html).not.toHaveClass(/dark/);
		await expect(html).toHaveAttribute('data-theme', 'light');

		// Toggle back to dark
		const darkThemeBtn = page.locator('[data-testid="theme-card-dark"]');
		await darkThemeBtn.click();
		await expect(html).toHaveClass(/dark/);
		await expect(html).toHaveAttribute('data-theme', 'dark');
	});
});

test.describe('Dashboard Live Batch Telemetry & Brewery Components', () => {
	test('renders live batch telemetry with SRM swatch, vital gauges, and brew stage tracker', async ({
		page
	}) => {
		await page.goto('/');
		await page.waitForLoadState('domcontentloaded');

		// Verify SRM Swatch
		const srmSwatch = page.locator('[role="img"][aria-label*="SRM"]');
		await expect(srmSwatch).toBeVisible();
		await expect(srmSwatch).toContainText('6.5 SRM');

		// Verify Vital Gauges
		const gauges = page.locator('[role="meter"]');
		const gaugeCount = await gauges.count();
		expect(gaugeCount).toBeGreaterThanOrEqual(4);

		const abvGauge = page.locator('[role="meter"][aria-label*="Alcohol (ABV)"]');
		await expect(abvGauge).toBeVisible();
		await expect(abvGauge).toHaveAttribute('aria-valuenow', '6.2');

		// Verify BrewStageTracker
		const stageTracker = page.locator('nav[aria-label="Brewing Progress"]');
		await expect(stageTracker).toBeVisible();
		await expect(stageTracker).toContainText('Mash');
		await expect(stageTracker).toContainText('Boil');
		await expect(stageTracker).toContainText('Ferment');
	});
});

test.describe('Desktop Collapsible Sidebar', () => {
	test('collapses and expands via toggle button on desktop viewports', async ({ page }) => {
		const viewport = page.viewportSize();
		if (!viewport || viewport.width < 1024) {
			test.skip(true, 'Test is for desktop viewports');
		}

		await page.goto('/');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		const sidebar = page.locator('aside[aria-label="Brewery Sidebar"]');
		await expect(sidebar).toBeVisible();
		await expect(sidebar).toHaveClass(/w-64/);

		const collapseBtn = sidebar.locator('button[aria-controls="brewery-sidebar"]');
		await expect(collapseBtn).toBeVisible();
		await collapseBtn.click();

		// Should become compact rail
		await expect(sidebar).toHaveClass(/w-18/);
		const storedCollapsed = await page.evaluate(() =>
			localStorage.getItem('brewyou-sidebar-collapsed')
		);
		expect(storedCollapsed).toBe('true');

		// Click to expand again
		await collapseBtn.click();
		await expect(sidebar).toHaveClass(/w-64/);
	});
});

test.describe('Website Favicon & Bookmark Icon', () => {
	test('links amber beer jug SVG favicon and serves it successfully', async ({ page, request }) => {
		await page.goto('/');
		await page.waitForLoadState('domcontentloaded');

		const faviconHref = await page.evaluate(() => {
			const link = document.querySelector('link[rel*="icon"]');
			return link ? link.getAttribute('href') : null;
		});
		expect(faviconHref).toContain('favicon.svg');

		const response = await request.get('/favicon.svg');
		expect(response.status()).toBe(200);
		const text = await response.text();
		expect(text).toContain('<svg');
		expect(text).toContain('#f59e0b');
	});
});

test.describe('Brand Logo & Light Mode Palette', () => {
	test('serves both dark and light logo assets successfully', async ({ request }) => {
		const resDark = await request.get('/logo-dark.png');
		expect(resDark.status()).toBe(200);

		const resLight = await request.get('/logo-light.png');
		expect(resLight.status()).toBe(200);
	});

	test('switches brand logo in navigation between dark and light mode', async ({ page }) => {
		await page.goto('/');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		const logoDark = page.locator('#brewery-sidebar [data-testid="sidebar-logo-dark"]');
		const logoLight = page.locator('#brewery-sidebar [data-testid="sidebar-logo-light"]');

		// On desktop sidebar, default is dark mode
		if (await logoDark.isVisible()) {
			await expect(logoDark).toBeVisible();
			await expect(logoLight).toBeHidden();

			// Switch to light mode
			await page.evaluate(() => {
				document.documentElement.classList.remove('dark');
				document.documentElement.setAttribute('data-theme', 'light');
			});
			await expect(page.locator('html')).not.toHaveClass(/dark/);

			// In light mode, light logo is visible and dark logo is hidden
			await expect(logoLight).toBeVisible();
			await expect(logoDark).toBeHidden();

			// Switch back to dark mode
			await page.evaluate(() => {
				document.documentElement.classList.add('dark');
				document.documentElement.setAttribute('data-theme', 'imperial-stout');
			});
			await expect(logoDark).toBeVisible();
			await expect(logoLight).toBeHidden();
		}
	});

	test('displays corresponding brand logo on login page based on theme', async ({ page }) => {
		await page.goto('/login');
		await page.waitForLoadState('domcontentloaded');

		const loginLogoDark = page.locator('[data-testid="login-logo-dark"]');
		const loginLogoLight = page.locator('[data-testid="login-logo-light"]');

		// By default (dark mode), dark logo is visible
		await expect(loginLogoDark).toBeVisible();
		await expect(loginLogoLight).toBeHidden();

		// Emulate light theme via class removal
		await page.evaluate(() => {
			document.documentElement.classList.remove('dark');
			document.documentElement.setAttribute('data-theme', 'light');
		});

		await expect(loginLogoLight).toBeVisible();
		await expect(loginLogoDark).toBeHidden();
	});

	test('applies logo-derived artisanal linen & craft malt colors in light mode', async ({
		page
	}) => {
		await page.goto('/settings');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		// Toggle to light mode
		const lightThemeBtn = page.locator('[data-testid="theme-card-light"]');
		await lightThemeBtn.click();
		await expect(page.locator('html')).not.toHaveClass(/dark/);

		// Verify computed CSS variable for canvas matches logo cream (#f9f6ee -> rgb(249, 246, 238))
		const canvasBg = await page.evaluate(() => {
			return getComputedStyle(document.documentElement).getPropertyValue('--canvas-bg').trim();
		});
		expect(canvasBg).toBe('#f9f6ee');

		// Verify primary text color matches logo line charcoal (#27241d)
		const textPrimary = await page.evaluate(() => {
			return getComputedStyle(document.documentElement).getPropertyValue('--text-primary').trim();
		});
		expect(textPrimary).toBe('#27241d');
	});
});
