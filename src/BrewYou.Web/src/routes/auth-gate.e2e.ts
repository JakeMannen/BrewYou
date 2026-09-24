import { expect, test } from '@playwright/test';

test.describe('Zero-Anonymous State & Mandatory Login Modal Gate', () => {
	test.beforeEach(async ({ page }) => {
		// Ensure clean unauthenticated state
		await page.goto('/');
		await page.evaluate(() => localStorage.clear());
		await page.reload();
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });
	});

	test('presents non-dismissible login modal on initial unauthenticated visit', async ({
		page
	}) => {
		const modal = page.locator('[data-testid="auth-modal"]');
		await expect(modal).toBeVisible();

		// Check modal title
		await expect(modal.locator('#auth-modal-title')).toBeVisible();

		// App shell behind modal should be marked inert
		const appShell = page.locator('[data-hydrated="true"]');
		await expect(appShell).toHaveAttribute('inert', '');
	});

	test('prohibits Escape key dismissal to enforce zero-anonymous access', async ({ page }) => {
		const modal = page.locator('[data-testid="auth-modal"]');
		await expect(modal).toBeVisible();

		// Press Escape
		await page.keyboard.press('Escape');

		// Modal must still remain visible
		await expect(modal).toBeVisible();
	});

	test('allows toggling between Sign In and Create Account tabs', async ({ page }) => {
		const modal = page.locator('[data-testid="auth-modal"]');
		await expect(modal).toBeVisible();

		// Default is Sign In
		const signinTab = modal.locator('#tab-signin');
		const signupTab = modal.locator('#tab-signup');
		await expect(signinTab).toHaveAttribute('aria-selected', 'true');
		await expect(modal.locator('#auth-confirm-password')).toHaveCount(0);

		// Switch to Create Account
		await signupTab.click();
		await expect(signupTab).toHaveAttribute('aria-selected', 'true');
		await expect(modal.locator('#auth-confirm-password')).toBeVisible();
		await expect(modal.locator('#auth-display-name')).toBeVisible();

		// Switch back to Sign In
		await signinTab.click();
		await expect(signinTab).toHaveAttribute('aria-selected', 'true');
		await expect(modal.locator('#auth-confirm-password')).toHaveCount(0);
	});

	test('toggles language between English and Swedish inside modal', async ({ page }) => {
		const modal = page.locator('[data-testid="auth-modal"]');
		await expect(modal).toBeVisible();

		const langSwitcher = modal.locator('[data-testid="auth-lang-switcher"]');
		await expect(langSwitcher).toBeVisible();

		// Click to toggle
		const initialText = await langSwitcher.innerText();
		await langSwitcher.click();
		const updatedText = await langSwitcher.innerText();

		expect(initialText).not.toEqual(updatedText);
	});
});
