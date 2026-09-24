import { expect, test } from '@playwright/test';

test.describe('First-Time Account Creation Brewery Name Modal', () => {
	test.beforeEach(async ({ page }) => {
		await page.context().clearCookies();
		await page.route('**/api/v1/**', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({ success: true, data: [] })
			});
		});
		await page.goto('/');
		await page.evaluate(() => localStorage.clear());
		await page.reload();
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });
	});

	test('shows onboarding modal upon registration with default name "My brewery"', async ({
		page
	}) => {
		// Mock registration endpoint returning isNewUser: true
		await page.route('**/api/v1/auth/register', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: {
						accessToken: 'mock-token',
						refreshToken: 'mock-refresh',
						expiresAt: new Date(Date.now() + 3600000).toISOString(),
						isNewUser: true,
						user: {
							id: 'new-user-1',
							email: 'newbrewer@example.com',
							displayName: 'New Brewer',
							preferredLanguage: 'en',
							preferredVolumeUnit: 'Liters'
						}
					}
				})
			});
		});

		await page.route('**/api/v1/auth/me/preferences', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: {
						id: 'new-user-1',
						email: 'newbrewer@example.com',
						preferences: {
							volumeUnit: 'Gallons'
						}
					}
				})
			});
		});

		await page.route('**/api/v1/brewery-setups', async (route) => {
			if (route.request().method() === 'GET') {
				await route.fulfill({
					status: 200,
					contentType: 'application/json',
					body: JSON.stringify({
						success: true,
						data: [
							{
								id: 'setup-1',
								name: 'My brewery',
								isDefault: true,
								equipmentCount: 0,
								createdAt: new Date().toISOString()
							}
						]
					})
				});
			} else {
				await route.continue();
			}
		});

		await page.route('**/api/v1/brewery-setups/**', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: {
						id: 'setup-1',
						name: 'Custom Brewery',
						isDefault: true,
						equipmentCount: 0,
						createdAt: new Date().toISOString()
					}
				})
			});
		});

		// Open Create Account tab
		const authModal = page.locator('[data-testid="auth-modal"]');
		await expect(authModal).toBeVisible();
		const signupTab = authModal.locator('#tab-signup');
		await signupTab.click();
		await expect(signupTab).toHaveAttribute('aria-selected', 'true');
		await expect(authModal.locator('#auth-confirm-password')).toBeVisible();

		await authModal.locator('#auth-email').fill('newbrewer@example.com');
		await authModal.locator('#auth-password').fill('Secret123!');
		await authModal.locator('#auth-confirm-password').fill('Secret123!');
		await authModal.locator('[data-testid="auth-submit-btn"]').click();

		// Onboarding modal should be visible
		const onboardingModal = page.locator('[data-testid="onboarding-modal"]');
		await expect(onboardingModal).toBeVisible();

		// Default name should be "My brewery"
		const input = onboardingModal.locator('[data-testid="onboarding-brewery-name-input"]');
		await expect(input).toHaveValue('My brewery');

		// Unit preset selection should be present and default to European Metric
		const europeanPreset = onboardingModal.locator(
			'[data-testid="onboarding-preset-european-metric"]'
		);
		const usPreset = onboardingModal.locator('[data-testid="onboarding-preset-us-craft"]');
		const ukPreset = onboardingModal.locator('[data-testid="onboarding-preset-uk-traditional"]');
		await expect(europeanPreset).toBeVisible();
		await expect(usPreset).toBeVisible();
		await expect(ukPreset).toBeVisible();
		await expect(europeanPreset).toHaveAttribute('aria-checked', 'true');

		// Switch preset to US Craft
		await usPreset.click();
		await expect(usPreset).toHaveAttribute('aria-checked', 'true');
		await expect(europeanPreset).toHaveAttribute('aria-checked', 'false');

		// Escape does not dismiss
		await page.keyboard.press('Escape');
		await expect(onboardingModal).toBeVisible();

		// Submit with default name
		await onboardingModal.locator('[data-testid="onboarding-submit-btn"]').click();

		// Onboarding modal should close
		await expect(onboardingModal).toHaveCount(0);
	});

	test('shows default name "Mitt bryggeri" when language is Swedish', async ({ page }) => {
		// Mock registration
		await page.route('**/api/v1/auth/register', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: {
						accessToken: 'mock-token',
						refreshToken: 'mock-refresh',
						expiresAt: new Date(Date.now() + 3600000).toISOString(),
						isNewUser: true,
						user: {
							id: 'new-user-sv',
							email: 'svensk@example.com',
							displayName: 'Svensk Bryggare',
							preferredLanguage: 'sv',
							preferredVolumeUnit: 'Liters'
						}
					}
				})
			});
		});

		await page.route('**/api/v1/brewery-setups**', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: [
						{
							id: 'setup-sv-1',
							name: 'Mitt bryggeri',
							isDefault: true,
							equipmentCount: 0,
							createdAt: new Date().toISOString()
						}
					]
				})
			});
		});

		await page.route('**/api/v1/auth/me/preferences', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: {
						id: 'new-user-sv',
						email: 'svensk@example.com',
						preferences: {
							volumeUnit: 'Liters'
						}
					}
				})
			});
		});

		// Switch auth modal to Swedish
		const authModal = page.locator('[data-testid="auth-modal"]');
		await expect(authModal).toBeVisible();
		const langSwitcher = authModal.locator('[data-testid="auth-lang-switcher"]');
		if ((await langSwitcher.innerText()).includes('EN')) {
			await langSwitcher.click();
		}

		const signupTab = authModal.locator('#tab-signup');
		await signupTab.click();
		await expect(signupTab).toHaveAttribute('aria-selected', 'true');
		await expect(authModal.locator('#auth-confirm-password')).toBeVisible();

		await authModal.locator('#auth-email').fill('svensk@example.com');
		await authModal.locator('#auth-password').fill('Secret123!');
		await authModal.locator('#auth-confirm-password').fill('Secret123!');
		await authModal.locator('[data-testid="auth-submit-btn"]').click();

		// Onboarding modal should be visible with Swedish default
		const onboardingModal = page.locator('[data-testid="onboarding-modal"]');
		await expect(onboardingModal).toBeVisible();

		const input = onboardingModal.locator('[data-testid="onboarding-brewery-name-input"]');
		await expect(input).toHaveValue('Mitt bryggeri');

		// Swedish preset labels
		const europeanPresetSv = onboardingModal.locator(
			'[data-testid="onboarding-preset-european-metric"]'
		);
		await expect(europeanPresetSv).toContainText('Europeisk metrisk');
		await expect(
			onboardingModal.locator('[data-testid="onboarding-preset-uk-traditional"]')
		).toContainText('UK Traditionell');
	});
});
