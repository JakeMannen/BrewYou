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
	const mockEquipment = [
		{
			id: 'eq-grainfather-g30',
			brewerySetupId: 'setup-bryggan',
			name: 'Grainfather G30',
			type: 'Boiler',
			subtype: 'AllInOne',
			capacity: 30,
			unit: 'Liters',
			capacityLiters: 30,
			currentVolume: 0,
			currentVolumeLiters: 0,
			boilOffRatePerHour: 3,
			trubLossLiters: 1.5,
			createdAt: new Date().toISOString()
		},
		{
			id: 'eq-grainfather-gf30',
			brewerySetupId: 'setup-bryggan',
			name: 'Grainfather GF30',
			type: 'Fermenter',
			subtype: 'ConicalFermenter',
			capacity: 30,
			unit: 'Liters',
			capacityLiters: 30,
			currentVolume: 0,
			currentVolumeLiters: 0,
			createdAt: new Date().toISOString()
		},
		{
			id: 'eq-corneliusfat-1',
			brewerySetupId: 'setup-bryggan',
			name: 'Corneliusfat 1',
			type: 'Keg',
			subtype: 'Cornelius',
			capacity: 19,
			unit: 'Liters',
			capacityLiters: 19,
			currentVolume: 0,
			currentVolumeLiters: 0,
			createdAt: new Date().toISOString()
		}
	];

	await page.route('**/api/v1/equipment*', async (route) => {
		await route.fulfill({
			status: 200,
			contentType: 'application/json',
			body: JSON.stringify({
				success: true,
				data: mockEquipment
			})
		});
	});

	await page.route('**/api/v1/backup/preview', async (route) => {
		await route.fulfill({
			status: 200,
			contentType: 'application/json',
			body: JSON.stringify({
				success: true,
				data: {
					isValid: true,
					checksumVerified: true,
					backupSchemaVersion: 2,
					currentSchemaVersion: 2,
					wasUpgraded: false,
					appVersion: '1.6.1',
					exportedAtUtc: new Date().toISOString(),
					counts: {
						userSettingsPresent: true,
						ownedBrewerySetups: 1,
						sharedSetupMemberships: 0,
						equipment: 3,
						customIngredients: 11,
						ingredientStocks: 0,
						recipes: 3,
						referencedExternalRecipes: 0,
						batches: 0,
						equipmentReadings: 0
					},
					conflicts: {
						matchingSetups: 0,
						matchingEquipment: 0,
						matchingCustomIngredients: 0,
						matchingRecipes: 0,
						matchingBatches: 0
					},
					sharedSetups: [],
					migrationNotes: [],
					warnings: [],
					errors: []
				}
			})
		});
	});

	await page.route('**/api/v1/backup/restore', async (route) => {
		mockSetups = [
			{
				id: 'setup-bryggan',
				name: 'Bryggan',
				description: 'Restored brewery setup',
				isDefault: true,
				equipmentCount: 3,
				createdAt: new Date().toISOString()
			}
		];
		await route.fulfill({
			status: 200,
			contentType: 'application/json',
			body: JSON.stringify({
				success: true,
				data: {
					succeeded: true,
					settingsRestored: true,
					summary: {
						setupsCreated: 0,
						setupsUpdated: 1,
						setupsSkipped: 0,
						equipmentCreated: 3,
						equipmentUpdated: 0,
						equipmentSkipped: 0,
						customIngredientsCreated: 11,
						customIngredientsUpdated: 0,
						customIngredientsSkipped: 0,
						ingredientStocksCreatedOrUpdated: 0,
						recipesCreated: 3,
						recipesUpdated: 0,
						recipesSkipped: 0,
						batchesCreated: 0,
						batchesUpdated: 0,
						batchesSkipped: 0,
						readingsRestored: 0,
						collaboratorsRestored: 0
					},
					warnings: [],
					errors: [],
					generatedInvites: []
				}
			})
		});
	});

	await page.route('**/api/v1/inventory/**', async (route) => {
		if (route.request().url().includes('/inventory/equipment')) {
			const url = new URL(route.request().url());
			const setupId = url.searchParams.get('setupId');
			let filtered = mockEquipment;
			if (setupId) {
				filtered = mockEquipment.filter((e) => e.brewerySetupId === setupId);
			}
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: filtered
				})
			});
			return;
		}
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
