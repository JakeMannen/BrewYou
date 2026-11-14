import { expect, test } from '@playwright/test';

test.describe('BrewLink Gateway Card Equipment Search', () => {
	const mockSetupId = '00000000-0000-0000-0000-000000000001';
	const mockGatewayUuid = '11111111-1111-1111-1111-111111111111';

	test('Clicking Search equipment on BrewLink gateway card loads devices once without looping', async ({
		page
	}) => {
		let gatewayEquipmentCalls = 0;
		let listGatewaysCalls = 0;

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

		await page.route('**/api/v1/inventory/equipment/telemetry-stream', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'text/event-stream',
				body: ''
			});
		});

		await page.route('**/api/v1/inventory/equipment?*', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: [
						{
							id: 'eq-gw-1',
							brewerySetupId: mockSetupId,
							name: 'BrewLink Gateway',
							type: 'Sensor',
							subtype: 'BrewLink',
							capacity: 0,
							unit: 'Liters',
							capacityLiters: 0,
							currentVolume: 0,
							currentVolumeLiters: 0,
							fillPercentage: 0,
							connectionType: 'BrewLink',
							connectionConfigJson: JSON.stringify({
								brewLinkGatewayId: mockGatewayUuid
							}),
							createdAt: '2026-01-01T00:00:00Z',
							updatedAt: '2026-01-01T00:00:00Z'
						}
					]
				})
			});
		});

		await page.route('**/api/v1/brewlink/gateways/*/equipment*', async (route) => {
			gatewayEquipmentCalls += 1;
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: [
						{
							slot: 1,
							paired: true,
							connected: false,
							name: 'G30-8B22',
							model: 'Grainfather G30 Connect',
							equipmentType: 'boiler',
							mac: 'C4:4F:33:1A:8B:22',
							rssi: null,
							capabilities: {
								recipeUpload: true,
								mashProfile: true,
								fermentationProfile: false,
								pumpControl: true,
								heatControl: true
							},
							state: null,
							existingEquipmentId: null,
							existingEquipmentName: null,
							mappedEquipmentType: 'Boiler',
							mappedSubtype: 'AllInOne',
							recommendedCapacityLiters: 30
						}
					]
				})
			});
		});

		await page.route('**/api/v1/brewlink/gateways', async (route) => {
			listGatewaysCalls += 1;
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify({
					success: true,
					data: [
						{
							id: mockGatewayUuid,
							name: 'BrewLink Gateway',
							host: '192.168.1.50',
							gatewayId: 'C44F331A8B20',
							clientName: 'BrewYou',
							pairedAt: '2026-01-01T00:00:00Z',
							updatedAt: '2026-01-01T00:00:00Z'
						}
					]
				})
			});
		});

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

		const checkBtn = page.locator('[data-testid="check-brewlink-equipment-btn"]').first();
		await expect(checkBtn).toBeVisible();

		const callsBeforeClick = gatewayEquipmentCalls;
		await checkBtn.click();

		const modal = page.locator('[data-testid="brewlink-provision-modal"]');
		await expect(modal).toBeVisible();
		await expect(modal.locator('[data-testid="brewlink-provision-device-1"]')).toBeVisible();
		await expect(modal.locator('[role="alert"]')).toHaveCount(0);
		await expect(modal).toContainText('192.168.1.50');
		await expect(modal).toContainText('C44F331A8B20');

		// Wait briefly and verify no reactive effect loop re-fires requests
		await page.waitForTimeout(300);
		expect(gatewayEquipmentCalls - callsBeforeClick).toBe(1);
		expect(listGatewaysCalls).toBe(1);
	});
});
