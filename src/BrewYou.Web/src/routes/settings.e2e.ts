import { expect, test } from '@playwright/test';
import { setupAuthenticatedSession } from './fixtures/auth';

test.describe('Brewer Personalization Settings Page', () => {
	test.beforeEach(async ({ page }) => {
		await setupAuthenticatedSession(page);
		await page.goto('/settings');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });
	});

	test('renders settings page with tabs and customization sections', async ({ page }) => {
		// Header title
		const heading = page.locator('h1');
		await expect(heading).toBeVisible();

		// Check navigation tabs exist
		await expect(page.locator('[data-testid="tab-btn-units"]')).toBeVisible();
		await expect(page.locator('[data-testid="tab-btn-defaults"]')).toBeVisible();
		await expect(page.locator('[data-testid="tab-btn-connectivity"]')).toBeVisible();
		await expect(page.locator('[data-testid="tab-btn-account"]')).toBeVisible();

		// Check sections on Display & Units tab exist
		await expect(page.getByRole('heading', { name: /Appearance|Utseende/i })).toBeVisible();
		await expect(page.getByRole('heading', { name: /Language|Språk/i })).toBeVisible();
		await expect(page.getByRole('heading', { name: /Brewery Units|Måttenheter/i })).toBeVisible();

		// Switch to Equipment Defaults tab and verify heading
		await page.locator('[data-testid="tab-btn-defaults"]').click();
		await expect(
			page.getByRole('heading', { name: /Brewing Equipment Defaults|Standardvärden/i })
		).toBeVisible();
	});

	test('navigates to settings page when clicking cog icon below username in sidebar on desktop', async ({
		page
	}) => {
		const viewport = page.viewportSize();
		if (viewport && viewport.width < 768) {
			test.skip(true, 'Desktop sidebar test; mobile navigation is handled by drawer');
		}

		await page.goto('/');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		// Click the cog icon button located below the user profile
		const settingsCog = page.locator('[data-testid="sidebar-settings-link"]');
		await expect(settingsCog).toBeVisible();
		await settingsCog.click();

		await expect(page).toHaveURL(/\/settings$/);
		await expect(page.locator('h1')).toBeVisible();
	});

	test('navigates to settings page from mobile drawer on small screens', async ({ page }) => {
		const viewport = page.viewportSize();
		if (!viewport || viewport.width >= 768) {
			test.skip(true, 'Mobile test for viewports < 768px');
		}

		await page.goto('/');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		const mobileToggle = page.locator('button[aria-controls="mobile-nav-drawer"]');
		await mobileToggle.click();

		const drawer = page.locator('[data-testid="mobile-nav-drawer"]');
		await expect(drawer).toBeVisible();

		const settingsLink = drawer.getByRole('link', { name: /settings|inställningar/i });
		await settingsLink.click();

		await expect(page).toHaveURL(/\/settings$/);
		await expect(drawer).toBeHidden();
	});

	test('switches theme across Light, Imperial Stout, Chocolate Porter, Obsidian, and Dark via settings cards', async ({
		page
	}) => {
		const html = page.locator('html');

		// Click Light theme radio card
		const lightThemeBtn = page.locator('[data-testid="theme-card-light"]');
		await lightThemeBtn.click();

		// Assert dark class removed from <html>
		await expect(html).not.toHaveClass(/dark/);
		await expect(html).toHaveAttribute('data-theme', 'light');

		// Verify stored theme in localStorage
		let storedTheme = await page.evaluate(() => localStorage.getItem('brewyou-theme'));
		expect(storedTheme).toBe('light');

		// Click Imperial Stout theme radio card
		const stoutThemeBtn = page.locator('[data-testid="theme-card-imperial-stout"]');
		await stoutThemeBtn.click();

		await expect(html).toHaveClass(/dark/);
		await expect(html).toHaveAttribute('data-theme', 'imperial-stout');
		storedTheme = await page.evaluate(() => localStorage.getItem('brewyou-theme'));
		expect(storedTheme).toBe('imperial-stout');

		// Click Chocolate Porter theme radio card
		const porterThemeBtn = page.locator('[data-testid="theme-card-chocolate-porter"]');
		await porterThemeBtn.click();

		await expect(html).toHaveClass(/dark/);
		await expect(html).toHaveAttribute('data-theme', 'chocolate-porter');
		storedTheme = await page.evaluate(() => localStorage.getItem('brewyou-theme'));
		expect(storedTheme).toBe('chocolate-porter');

		// Click Obsidian Stout theme radio card
		const obsidianThemeBtn = page.locator('[data-testid="theme-card-obsidian"]');
		await obsidianThemeBtn.click();

		await expect(html).toHaveClass(/dark/);
		await expect(html).toHaveAttribute('data-theme', 'obsidian');
		storedTheme = await page.evaluate(() => localStorage.getItem('brewyou-theme'));
		expect(storedTheme).toBe('obsidian');

		// Click Dark theme radio card
		const darkThemeBtn = page.locator('[data-testid="theme-card-dark"]');
		await darkThemeBtn.click();

		await expect(html).toHaveClass(/dark/);
		await expect(html).toHaveAttribute('data-theme', 'dark');
	});

	test('switches language between English and Swedish updating UI and document lang via dropdown', async ({
		page
	}) => {
		const langSelect = page.locator('[data-testid="language-select"]');
		await expect(langSelect).toBeVisible();

		// Switch to Swedish
		await langSelect.selectOption('sv');

		// Verify <html> lang attribute
		await expect(page.locator('html')).toHaveAttribute('lang', 'sv');
		await expect(page.locator('h1')).toHaveText('Bryggarinställningar');

		// Switch back to English
		await langSelect.selectOption('en');

		await expect(page.locator('html')).toHaveAttribute('lang', 'en');
		await expect(page.locator('h1')).toHaveText('Brewer Settings');
	});

	test('toggles measurement units across metric, imperial, and alternative scales', async ({
		page
	}) => {
		const volLiters = page.locator('[data-testid="unit-volume-liters"]');
		const volGallons = page.locator('[data-testid="unit-volume-gallons"]');
		const weightMetric = page.locator('[data-testid="unit-weight-metric"]');
		const weightImperial = page.locator('[data-testid="unit-weight-imperial"]');
		const tempCelsius = page.locator('[data-testid="unit-temp-celsius"]');
		const tempFahrenheit = page.locator('[data-testid="unit-temp-fahrenheit"]');
		const gravSg = page.locator('[data-testid="unit-gravity-sg"]');
		const gravPlato = page.locator('[data-testid="unit-gravity-plato"]');
		const colorSrm = page.locator('[data-testid="unit-color-srm"]');
		const colorEbc = page.locator('[data-testid="unit-color-ebc"]');

		// Initial Metric defaults
		await expect(volLiters).toHaveAttribute('aria-checked', 'true');
		await expect(volGallons).toHaveAttribute('aria-checked', 'false');
		await expect(weightMetric).toHaveAttribute('aria-checked', 'true');
		await expect(tempCelsius).toHaveAttribute('aria-checked', 'true');
		await expect(gravSg).toHaveAttribute('aria-checked', 'true');
		await expect(colorSrm).toHaveAttribute('aria-checked', 'true');

		// Toggle Volume to US Gallons
		await volGallons.click();
		await expect(volGallons).toHaveAttribute('aria-checked', 'true');
		await expect(volLiters).toHaveAttribute('aria-checked', 'false');

		// Toggle Weight to Imperial
		await weightImperial.click();
		await expect(weightImperial).toHaveAttribute('aria-checked', 'true');
		await expect(weightMetric).toHaveAttribute('aria-checked', 'false');

		// Toggle Temperature to Fahrenheit
		await tempFahrenheit.click();
		await expect(tempFahrenheit).toHaveAttribute('aria-checked', 'true');
		await expect(tempCelsius).toHaveAttribute('aria-checked', 'false');

		// Toggle Gravity to Plato
		await gravPlato.click();
		await expect(gravPlato).toHaveAttribute('aria-checked', 'true');
		await expect(gravSg).toHaveAttribute('aria-checked', 'false');

		// Toggle Color to European EBC
		await colorEbc.click();
		await expect(colorEbc).toHaveAttribute('aria-checked', 'true');
		await expect(colorSrm).toHaveAttribute('aria-checked', 'false');

		// Toggle back to Metric Volume
		await volLiters.click();
		await expect(volLiters).toHaveAttribute('aria-checked', 'true');
		await expect(volGallons).toHaveAttribute('aria-checked', 'false');
	});

	test('saves equipment defaults and maintains local persistence', async ({ page }) => {
		// Switch to Equipment Defaults tab
		await page.locator('[data-testid="tab-btn-defaults"]').click();
		await expect(page.locator('#default-batch-size-input')).toBeVisible();

		const batchInput = page.locator('#default-batch-size-input');
		const effInput = page.locator('#default-efficiency-input');
		const boilInput = page.locator('#default-boil-time-input');

		await batchInput.fill('25.5');
		await effInput.fill('78');
		await boilInput.fill('75');

		const saveBtn = page.locator('[data-testid="save-defaults-btn"]');
		await expect(saveBtn).toBeEnabled();
		await saveBtn.click();

		// Success confirmation appears
		await expect(
			page.getByText(
				/Equipment defaults saved|Standardvärden har sparats|Preferences saved successfully|Inställningarna sparades/i
			)
		).toBeVisible();

		// Verify persistence in localStorage
		const savedBatch = await page.evaluate(() =>
			localStorage.getItem('brewyou_default_batch_size')
		);
		const savedEff = await page.evaluate(() => localStorage.getItem('brewyou_default_efficiency'));
		const savedBoil = await page.evaluate(() => localStorage.getItem('brewyou_default_boil_time'));

		expect(Number(savedBatch)).toBeCloseTo(25.5, 1);
		expect(Number(savedEff)).toBe(78);
		expect(Number(savedBoil)).toBe(75);
	});

	test('switches tabs and reflects active tab in query parameters', async ({ page }) => {
		// Switch to Hardware & IoT tab
		await page.locator('[data-testid="tab-btn-connectivity"]').click();
		await expect(page).toHaveURL(/tab=connectivity/);
		await expect(page.locator('#mqtt-host-input')).toBeVisible();

		// Switch to Account & Profile tab
		await page.locator('[data-testid="tab-btn-account"]').click();
		await expect(page).toHaveURL(/tab=account/);
		await expect(page.locator('#display-name-input')).toBeVisible();
		await expect(page.locator('[data-testid="session-logout-btn"]')).toBeVisible();

		// Switch back to Display & Units tab (units is default, tab param deleted)
		await page.locator('[data-testid="tab-btn-units"]').click();
		await expect(page).not.toHaveURL(/tab=account/);
		await expect(page.getByRole('heading', { name: /Brewery Units|Måttenheter/i })).toBeVisible();
	});

	test('navigates directly to hardware tab via hash link #connectivity', async ({ page }) => {
		await page.goto('/settings#connectivity');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });
		await expect(page.locator('#mqtt-host-input')).toBeVisible();
		await expect(page.locator('[data-testid="tab-btn-connectivity"]')).toHaveAttribute(
			'aria-selected',
			'true'
		);
	});
});
