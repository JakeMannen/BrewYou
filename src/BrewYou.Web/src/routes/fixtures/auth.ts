import type { Page } from '@playwright/test';
import type { VolumeUnit } from '$lib/types/api';

export const defaultE2EUser = {
	id: 'test-user-1',
	email: 'brewer@example.com',
	displayName: 'Master Brewer',
	roles: ['Brewer'],
	preferredLanguage: 'en',
	preferredVolumeUnit: 'Liters',
	preferences: {
		volumeUnit: 'Liters',
		weightUnit: 'Metric',
		temperatureUnit: 'Celsius',
		gravityUnit: 'SpecificGravity',
		themePreference: 'Dark',
		defaultBatchSizeLiters: 20,
		defaultEfficiencyPercent: 75,
		defaultBoilTimeMinutes: 60
	}
};

export async function setupAuthenticatedSession(page: Page, user = defaultE2EUser) {
	let mockSetups = [
		{
			id: 'default-1',
			name: 'Brewery 1',
			description: 'Default setup',
			isDefault: true,
			equipmentCount: 1,
			createdAt: '2026-01-01T00:00:00Z'
		}
	];

	await page.addInitScript(() => {
		if (!localStorage.getItem('brewyou_token')) {
			localStorage.setItem('brewyou_token', 'mock-token');
		}
		if (!localStorage.getItem('brewyou_brewery_setups')) {
			localStorage.setItem(
				'brewyou_brewery_setups',
				JSON.stringify([
					{
						id: 'default-1',
						name: 'Brewery 1',
						isDefault: true,
						createdAt: '2026-01-01T00:00:00Z'
					}
				])
			);
		}
		if (!localStorage.getItem('brewyou_active_setup_id')) {
			localStorage.setItem('brewyou_active_setup_id', 'default-1');
		}
	});
	await page.route('**/api/v1/auth/me', async (route) => {
		await route.fulfill({
			status: 200,
			contentType: 'application/json',
			body: JSON.stringify({
				success: true,
				data: user
			})
		});
	});
	await page.route('**/api/v1/auth/me/**', async (route) => {
		const method = route.request().method();
		if (method === 'PUT' || method === 'POST') {
			const postData = (route.request().postDataJSON?.() || {}) as Record<string, unknown>;
			if (postData.language && typeof postData.language === 'string') {
				user.preferredLanguage = postData.language;
			}
			if (postData.preferredLanguage && typeof postData.preferredLanguage === 'string') {
				user.preferredLanguage = postData.preferredLanguage;
			}
			if (postData.volumeUnit && typeof postData.volumeUnit === 'string') {
				user.preferredVolumeUnit = postData.volumeUnit;
				if (user.preferences) user.preferences.volumeUnit = postData.volumeUnit as VolumeUnit;
			}
			if (user.preferences) {
				Object.assign(user.preferences, postData);
			}
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: user
				})
			});
			return;
		}
		await route.continue();
	});
	await page.route('**/api/v1/auth/refresh', async (route) => {
		await route.fulfill({
			status: 200,
			contentType: 'application/json',
			body: JSON.stringify({
				success: true,
				data: {
					accessToken: 'mock-token',
					user
				}
			})
		});
	});
	await page.route('**/api/v1/batches*', async (route) => {
		await route.fulfill({
			status: 200,
			contentType: 'application/json',
			body: JSON.stringify({
				success: true,
				data: []
			})
		});
	});
	await page.route('**/api/v1/equipment*', async (route) => {
		await route.fulfill({
			status: 200,
			contentType: 'application/json',
			body: JSON.stringify({
				success: true,
				data: []
			})
		});
	});
	await page.route('**/api/v1/inventory/**', async (route) => {
		await route.fulfill({
			status: 200,
			contentType: 'application/json',
			body: JSON.stringify({
				success: true,
				data: []
			})
		});
	});
	await page.route('**/api/v1/recipes*', async (route) => {
		await route.fulfill({
			status: 200,
			contentType: 'application/json',
			body: JSON.stringify({
				success: true,
				data: []
			})
		});
	});
	await page.route('**/api/v1/brewery-setups', async (route) => {
		if (route.request().method() === 'POST') {
			const postData = route.request().postDataJSON() || {};
			const newSetup = {
				id: `setup-${Date.now()}`,
				name: postData.name || 'New Brewery',
				description: postData.description || null,
				isDefault: false,
				equipmentCount: 0,
				createdAt: new Date().toISOString()
			};
			mockSetups.push(newSetup);
			await route.fulfill({
				status: 201,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: newSetup
				})
			});
			return;
		}

		await route.fulfill({
			status: 200,
			contentType: 'application/json',
			body: JSON.stringify({
				success: true,
				data: mockSetups
			})
		});
	});
	await page.route('**/api/v1/brewery-setups/**', async (route) => {
		const method = route.request().method();
		const url = route.request().url();
		const id = url.split('/').pop()?.split('?')[0];
		if (method === 'PUT') {
			const putData = route.request().postDataJSON() || {};
			const target = mockSetups.find((s) => s.id === id) ?? mockSetups[mockSetups.length - 1];
			if (target) {
				target.name = putData.name || target.name;
				target.description =
					putData.description !== undefined ? putData.description : target.description;
			}
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: target
				})
			});
			return;
		}
		if (method === 'DELETE') {
			mockSetups = mockSetups.filter((s) => s.id !== id);
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true
				})
			});
			return;
		}
		await route.continue();
	});
	await page.route('**/api/v1/ingredients*', async (route) => {
		await route.fulfill({
			status: 200,
			contentType: 'application/json',
			body: JSON.stringify({
				success: true,
				data: []
			})
		});
	});
}
