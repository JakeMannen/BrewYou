import { describe, it, expect, beforeEach, vi } from 'vitest';
import { api, setClientAuthToken, getClientAuthToken } from './client';

describe('API Client', () => {
	beforeEach(() => {
		setClientAuthToken(null);
		vi.restoreAllMocks();
	});

	it('manages auth token correctly', () => {
		expect(getClientAuthToken()).toBeNull();
		setClientAuthToken('jwt-token-xyz');
		expect(getClientAuthToken()).toBe('jwt-token-xyz');
	});

	it('unwraps successful uniform response envelope', async () => {
		const mockData = [{ id: '1', name: 'Pale Ale' }];
		globalThis.fetch = vi.fn().mockResolvedValue({
			ok: true,
			status: 200,
			json: async () => ({
				success: true,
				data: mockData,
				pagination: { page: 1, limit: 20, total: 1, totalPages: 1 }
			})
		} as Response);

		const result = await api.recipes.list();
		expect(result).toEqual(mockData);
	});

	it('handles uniform error response envelope properly', async () => {
		globalThis.fetch = vi.fn().mockResolvedValue({
			ok: false,
			status: 400,
			json: async () => ({
				success: false,
				error: {
					code: 'VALIDATION_ERROR',
					message: 'Invalid recipe calculation request.'
				}
			})
		} as Response);

		await expect(
			api.recipes.calculate({
				batchSizeLiters: -1,
				efficiencyPercent: 70,
				boilTimeMinutes: 60,
				ingredients: []
			})
		).rejects.toThrow('Invalid recipe calculation request.');
	});

	it('includes Authorization header when token is set', async () => {
		setClientAuthToken('my-secret-bearer-token');

		let capturedHeaders: HeadersInit | undefined;
		globalThis.fetch = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
			capturedHeaders = init?.headers;
			return Promise.resolve({
				ok: true,
				status: 200,
				json: async () => ({
					success: true,
					data: { id: 'u1', email: 'user@test.com', displayName: 'Test', preferredLanguage: 'en' }
				})
			} as Response);
		});

		await api.auth.me();
		expect(capturedHeaders).toBeDefined();
		const headers = new Headers(capturedHeaders);
		expect(headers.get('Authorization')).toBe('Bearer my-secret-bearer-token');
	});

	it('targets port 5000 by default', async () => {
		let requestedUrl = '';
		globalThis.fetch = vi.fn().mockImplementation((url: string) => {
			requestedUrl = url;
			return Promise.resolve({
				ok: true,
				status: 200,
				json: async () => ({
					success: true,
					data: { id: 'u1', email: 'user@test.com', displayName: 'Test', preferredLanguage: 'en' }
				})
			} as Response);
		});

		await api.auth.me();
		expect(requestedUrl).toBe('http://localhost:5000/api/v1/auth/me');
	});

	it('automatically performs silent token refresh on 401 and retries original request', async () => {
		setClientAuthToken('expired-access-token');

		let callCount = 0;
		globalThis.fetch = vi.fn().mockImplementation((url: string, init?: RequestInit) => {
			callCount++;
			if (url.includes('/api/v1/recipes')) {
				const headers = new Headers(init?.headers);
				const authHeader = headers.get('Authorization');
				if (authHeader === 'Bearer expired-access-token') {
					return Promise.resolve({
						ok: false,
						status: 401,
						json: async () => ({
							success: false,
							error: { code: 'TOKEN_EXPIRED', message: 'Expired' }
						})
					} as Response);
				} else if (authHeader === 'Bearer refreshed-new-jwt') {
					return Promise.resolve({
						ok: true,
						status: 200,
						json: async () => ({
							success: true,
							data: [{ id: 'r1', name: 'Fresh IPA' }]
						})
					} as Response);
				}
			} else if (url.endsWith('/api/v1/auth/refresh')) {
				return Promise.resolve({
					ok: true,
					status: 200,
					json: async () => ({
						success: true,
						data: {
							accessToken: 'refreshed-new-jwt',
							refreshToken: 'new-refresh-token',
							expiresAt: '2026-09-12T15:00:00Z',
							user: { id: 'u1', email: 'user@test.com', displayName: 'Test' }
						}
					})
				} as Response);
			}
			return Promise.reject(new Error(`Unexpected call to ${url}`));
		});

		const recipes = await api.recipes.list(1, 100);
		expect(recipes).toEqual([{ id: 'r1', name: 'Fresh IPA' }]);
		expect(getClientAuthToken()).toBe('refreshed-new-jwt');
		expect(callCount).toBe(3); // 1st recipes call (401), refresh call, retry recipes call (200)
	});

	it('deduplicates concurrent 401 refreshes into a single /auth/refresh call', async () => {
		setClientAuthToken('expired-token');

		let refreshCallCount = 0;
		globalThis.fetch = vi.fn().mockImplementation((url: string, init?: RequestInit) => {
			const headers = new Headers(init?.headers);
			const authHeader = headers.get('Authorization');

			if (url.includes('/api/v1/recipes')) {
				if (authHeader === 'Bearer refreshed-singleton-jwt') {
					return Promise.resolve({
						ok: true,
						status: 200,
						json: async () => ({ success: true, data: [{ id: 'r1', name: 'Stout' }] })
					} as Response);
				}
				return Promise.resolve({
					ok: false,
					status: 401,
					json: async () => ({
						success: false,
						error: { code: 'TOKEN_EXPIRED', message: 'Expired' }
					})
				} as Response);
			}

			if (url.includes('/api/v1/ingredients')) {
				if (authHeader === 'Bearer refreshed-singleton-jwt') {
					return Promise.resolve({
						ok: true,
						status: 200,
						json: async () => ({ success: true, data: [{ id: 'i1', name: 'Citra' }] })
					} as Response);
				}
				return Promise.resolve({
					ok: false,
					status: 401,
					json: async () => ({
						success: false,
						error: { code: 'TOKEN_EXPIRED', message: 'Expired' }
					})
				} as Response);
			}

			if (url.endsWith('/api/v1/auth/refresh')) {
				refreshCallCount++;
				return Promise.resolve({
					ok: true,
					status: 200,
					json: async () => ({
						success: true,
						data: {
							accessToken: 'refreshed-singleton-jwt',
							refreshToken: 'new-refresh',
							expiresAt: '2026-09-12T15:00:00Z',
							user: { id: 'u1', email: 'user@test.com', displayName: 'Test' }
						}
					})
				} as Response);
			}

			return Promise.reject(new Error(`Unexpected ${url}`));
		});

		const [recipes, ingredients] = await Promise.all([
			api.recipes.list(1, 100),
			api.ingredients.list()
		]);

		expect(recipes).toEqual([{ id: 'r1', name: 'Stout' }]);
		expect(ingredients).toEqual([{ id: 'i1', name: 'Citra' }]);
		expect(refreshCallCount).toBe(1); // Crucial: Exactly one refresh call despite two concurrent 401s
	});

	it('does not attempt refresh and does not loop on /api/v1/auth/ endpoints', async () => {
		let refreshAttempted = false;
		globalThis.fetch = vi.fn().mockImplementation((url: string) => {
			if (url.endsWith('/api/v1/auth/login')) {
				return Promise.resolve({
					ok: false,
					status: 401,
					json: async () => ({
						success: false,
						error: { code: 'INVALID_CREDENTIALS', message: 'Bad password' }
					})
				} as Response);
			}
			if (url.endsWith('/api/v1/auth/refresh')) {
				refreshAttempted = true;
				return Promise.resolve({ ok: false, status: 401 } as Response);
			}
			return Promise.reject(new Error(`Unexpected ${url}`));
		});

		await expect(api.auth.login({ email: 'brewer@test.com', password: 'wrong' })).rejects.toThrow(
			'Bad password'
		);
		expect(refreshAttempted).toBe(false);
	});

	describe('Endpoint namespaces coverage', () => {
		const asPayload = <T>(val: unknown = {}): T => val as T;
		const mockResponse = (data: unknown) =>
			Promise.resolve({
				ok: true,
				status: 200,
				json: async () => ({
					success: true,
					data
				})
			} as Response);

		it('covers auth namespace endpoints', async () => {
			globalThis.fetch = vi.fn().mockImplementation(() => mockResponse({ id: 'u1' }));

			await api.auth.register({ email: 'a@b.com', password: 'pwd', displayName: 'User' });
			await api.auth.google({ idToken: 'token' });
			await api.auth.updateLanguage('sv');
			await api.auth.updateVolumeUnit('Liters');
			await api.auth.updatePreferences(asPayload({ preferredLanguage: 'sv' }));
			await api.auth.getMqttStatus('localhost', 1883);
			await api.auth.updateProfile({ displayName: 'New Name' });
			await api.auth.logout();

			expect(globalThis.fetch).toHaveBeenCalled();
		});

		it('covers ingredients namespace endpoints', async () => {
			globalThis.fetch = vi.fn().mockImplementation(() => mockResponse({ id: 'ing1' }));

			await api.ingredients.list('Fermentable', 'Malt', 1, 20, true);
			await api.ingredients.getById('ing1');
			await api.ingredients.create(asPayload({ name: 'Cascade' }));
			await api.ingredients.updateStock('ing1', asPayload({ inStockAmount: 100 }));

			expect(globalThis.fetch).toHaveBeenCalled();
		});

		it('covers equipment namespace endpoints', async () => {
			globalThis.fetch = vi.fn().mockImplementation(() => mockResponse({ id: 'eq1' }));

			await api.equipment.list('Fermenter', 'FV', 1, 10, 'setup-1');
			await api.equipment.getById('eq1');
			await api.equipment.create(asPayload({ name: 'Fermenter 1' }));
			await api.equipment.update('eq1', asPayload({ name: 'Fermenter 1 updated' }));
			await api.equipment.delete('eq1');
			await api.equipment.getActiveBatches('eq1');
			await api.equipment.regenerateToken('eq1');
			await api.equipment.getReadings('eq1', 5);
			await api.equipment.testPoll('eq1');

			expect(globalThis.fetch).toHaveBeenCalled();
		});

		it('covers brewerySetups namespace endpoints', async () => {
			globalThis.fetch = vi.fn().mockImplementation(() => mockResponse({ id: 'set1' }));

			await api.brewerySetups.list();
			await api.brewerySetups.getById('set1');
			await api.brewerySetups.create(asPayload({ name: 'Setup 1' }));
			await api.brewerySetups.update('set1', asPayload({ name: 'Setup 1 updated' }));
			await api.brewerySetups.delete('set1');
			await api.brewerySetups.setDefault('set1');

			expect(globalThis.fetch).toHaveBeenCalled();
		});

		it('covers recipes namespace endpoints', async () => {
			globalThis.fetch = vi.fn().mockImplementation(() => mockResponse({ id: 'rec1' }));

			await api.recipes.calculateWater(asPayload());
			await api.recipes.list(1, 10, 'mine');
			await api.recipes.getById('rec1');
			await api.recipes.create(asPayload({ name: 'Recipe 1' }));
			await api.recipes.update('rec1', asPayload({ name: 'Recipe 1 updated' }));
			await api.recipes.delete('rec1');

			expect(globalThis.fetch).toHaveBeenCalled();
		});

		it('covers batches namespace endpoints', async () => {
			globalThis.fetch = vi.fn().mockImplementation(() => mockResponse({ id: 'b1' }));

			await api.batches.list({ status: 'Fermenting', search: 'IPA', page: 1, limit: 10 });
			await api.batches.getNextCode('2026-09-21');
			await api.batches.getById('b1');
			await api.batches.create(asPayload({ name: 'Batch 1' }));
			await api.batches.advanceStage('b1', asPayload({ targetStage: 'Ferment' }));
			await api.batches.checkStock(asPayload());
			await api.batches.addReading('b1', asPayload());
			await api.batches.toggleIngredient('b1', 'ing1', true);
			await api.batches.toggleMashStep('b1', 'step1', asPayload());
			await api.batches.toggleFermentationStep('b1', 'fstep1', asPayload());
			await api.batches.update('b1', asPayload());
			await api.batches.delete('b1');
			await api.batches.getEquipmentReadings('b1', { stage: 'Ferment', stepId: 'step1' });
			await api.batches.updateSensors('b1', []);
			await api.batches.logEquipmentReading('b1', asPayload());

			expect(globalThis.fetch).toHaveBeenCalled();
		});

		it('covers equipment.streamTelemetry SSE parsing', async () => {
			const encoder = new TextEncoder();
			const stream = new ReadableStream({
				start(controller) {
					controller.enqueue(
						encoder.encode(
							'event: reading\ndata: {"equipmentId":"eq1","currentTemperatureC":21.5}\n\n'
						)
					);
					controller.close();
				}
			});

			globalThis.fetch = vi.fn().mockResolvedValue({
				ok: true,
				status: 200,
				body: stream
			} as Response);

			let received: unknown = null;
			await api.equipment.streamTelemetry({
				onReading: (r) => {
					received = r;
				}
			});

			expect(received).toEqual({ equipmentId: 'eq1', currentTemperatureC: 21.5 });
		});

		it('covers batches.streamEquipmentReadings SSE parsing', async () => {
			const encoder = new TextEncoder();
			const stream = new ReadableStream({
				start(controller) {
					controller.enqueue(
						encoder.encode('event: reading\ndata: {"id":"r1","temperatureC":19.2}\n\n')
					);
					controller.close();
				}
			});

			globalThis.fetch = vi.fn().mockResolvedValue({
				ok: true,
				status: 200,
				body: stream
			} as Response);

			let received: unknown = null;
			await api.batches.streamEquipmentReadings('b1', {
				stage: 'Ferment',
				onReading: (r) => {
					received = r;
				}
			});

			expect(received).toEqual({ id: 'r1', temperatureC: 19.2 });
		});
	});
});
