import { describe, it, expect, beforeEach, vi } from 'vitest';
import { auth } from './auth.svelte';
import { api, ApiClientError } from '$lib/api/client';
import type { AuthResponse, UserDto } from '$lib/types/api';

class LocalStorageMock {
	private store: Record<string, string> = {};
	clear() {
		this.store = {};
	}
	getItem(key: string) {
		return this.store[key] ?? null;
	}
	setItem(key: string, value: string) {
		this.store[key] = String(value);
	}
	removeItem(key: string) {
		delete this.store[key];
	}
}

const mockStorage = new LocalStorageMock();

const mockUser: UserDto = {
	id: 'user-456',
	email: 'brewer@example.com',
	displayName: 'Brew Master',
	preferredLanguage: 'en',
	preferredVolumeUnit: 'Liters'
};

const mockAuthResponse: AuthResponse = {
	accessToken: 'test-access-token-jwt',
	refreshToken: 'test-refresh-token',
	expiresAt: '2026-09-07T20:00:00Z',
	user: mockUser
};

describe('AuthState LifeCycle & Zero-Anonymous Gating', () => {
	beforeEach(() => {
		vi.restoreAllMocks();
		mockStorage.clear();
		auth.clearAuth();
		auth.clearError();
		Object.defineProperty(globalThis, 'localStorage', {
			value: mockStorage,
			writable: true,
			configurable: true
		});
		Object.defineProperty(globalThis, 'window', {
			value: globalThis,
			writable: true,
			configurable: true
		});
	});

	it('starts in unauthenticated status on cold boot without token', async () => {
		vi.spyOn(api.auth, 'refresh').mockRejectedValue(new Error('No refresh token'));

		await auth.init();

		expect(auth.status).toBe('unauthenticated');
		expect(auth.isAuthenticated).toBe(false);
		expect(auth.user).toBeNull();
		expect(auth.token).toBeNull();
	});

	it('hydrates to authenticated status on boot with valid token', async () => {
		mockStorage.setItem('brewyou_token', 'valid-saved-jwt');
		vi.spyOn(api.auth, 'me').mockResolvedValue(mockUser);

		await auth.init();

		expect(auth.status).toBe('authenticated');
		expect(auth.isAuthenticated).toBe(true);
		expect(auth.user).toEqual(mockUser);
		expect(auth.token).toBe('valid-saved-jwt');
	});

	it('attempts silent cookie refresh if saved token is expired', async () => {
		mockStorage.setItem('brewyou_token', 'expired-jwt');
		vi.spyOn(api.auth, 'me').mockRejectedValue(new ApiClientError('Token expired', 401));
		vi.spyOn(api.auth, 'refresh').mockResolvedValue(mockAuthResponse);

		await auth.init();

		expect(auth.status).toBe('authenticated');
		expect(auth.isAuthenticated).toBe(true);
		expect(auth.token).toBe('test-access-token-jwt');
		expect(mockStorage.getItem('brewyou_token')).toBe('test-access-token-jwt');
	});

	it('falls back to unauthenticated when both token and refresh fail', async () => {
		mockStorage.setItem('brewyou_token', 'corrupted-jwt');
		vi.spyOn(api.auth, 'me').mockRejectedValue(new ApiClientError('Token invalid', 401));
		vi.spyOn(api.auth, 'refresh').mockRejectedValue(new ApiClientError('Refresh invalid', 401));

		await auth.init();

		expect(auth.status).toBe('unauthenticated');
		expect(auth.isAuthenticated).toBe(false);
		expect(auth.user).toBeNull();
		expect(mockStorage.getItem('brewyou_token')).toBeNull();
	});

	it('transitions to authenticated on successful login', async () => {
		vi.spyOn(api.auth, 'login').mockResolvedValue(mockAuthResponse);

		const success = await auth.login('brewer@example.com', 'ValidPass123!');

		expect(success).toBe(true);
		expect(auth.status).toBe('authenticated');
		expect(auth.isAuthenticated).toBe(true);
		expect(auth.user).toEqual(mockUser);
		expect(auth.token).toBe('test-access-token-jwt');
		expect(auth.error).toBeNull();
		expect(mockStorage.getItem('brewyou_token')).toBe('test-access-token-jwt');
	});

	it('records structured error and stays unauthenticated on login failure', async () => {
		vi.spyOn(api.auth, 'login').mockRejectedValue(
			new ApiClientError('Invalid email or password.', 401, 'INVALID_CREDENTIALS')
		);

		const success = await auth.login('brewer@example.com', 'WrongPass!');

		expect(success).toBe(false);
		expect(auth.status).toBe('unauthenticated');
		expect(auth.isAuthenticated).toBe(false);
		expect(auth.error).toBe('Invalid email or password.');
		expect(auth.errorCode).toBe('INVALID_CREDENTIALS');
	});

	it('transitions to authenticated on successful register', async () => {
		vi.spyOn(api.auth, 'register').mockResolvedValue(mockAuthResponse);

		const success = await auth.register('new@example.com', 'Secure123!', 'New Brewer', 'en');

		expect(success).toBe(true);
		expect(auth.status).toBe('authenticated');
		expect(auth.isAuthenticated).toBe(true);
		expect(auth.user).toEqual(mockUser);
	});

	it('transitions to unauthenticated and wipes tokens on logout', async () => {
		mockStorage.setItem('brewyou_token', 'active-token');
		vi.spyOn(api.auth, 'me').mockResolvedValue(mockUser);
		await auth.init();
		expect(auth.isAuthenticated).toBe(true);

		vi.spyOn(api.auth, 'logout').mockResolvedValue(undefined as unknown as void);

		await auth.logout();

		expect(auth.status).toBe('unauthenticated');
		expect(auth.isAuthenticated).toBe(false);
		expect(auth.user).toBeNull();
		expect(auth.token).toBeNull();
		expect(mockStorage.getItem('brewyou_token')).toBeNull();
	});

	it('immediately revokes auth and populates session expired message on handleSessionExpired', () => {
		mockStorage.setItem('brewyou_token', 'active-token');
		auth.handleSessionExpired();

		expect(auth.status).toBe('unauthenticated');
		expect(auth.isAuthenticated).toBe(false);
		expect(auth.user).toBeNull();
		expect(auth.token).toBeNull();
		expect(auth.errorCode).toBe('TOKEN_EXPIRED');
		expect(auth.error).toContain('session');
	});

	it('flags needsBreweryOnboarding on register and allows completing it', async () => {
		vi.spyOn(api.auth, 'register').mockResolvedValue({
			...mockAuthResponse,
			isNewUser: true
		});

		const success = await auth.register('new@example.com', 'Secure123!', 'New Brewer', 'en');

		expect(success).toBe(true);
		expect(auth.needsBreweryOnboarding).toBe(true);
		expect(mockStorage.getItem('brewyou_pending_onboarding')).toBe('true');

		auth.completeOnboarding();
		expect(auth.needsBreweryOnboarding).toBe(false);
		expect(mockStorage.getItem('brewyou_pending_onboarding')).toBeNull();
	});

	it('does not flag needsBreweryOnboarding on normal login', async () => {
		vi.spyOn(api.auth, 'login').mockResolvedValue({
			...mockAuthResponse,
			isNewUser: false
		});

		const success = await auth.login('brewer@example.com', 'ValidPass123!');

		expect(success).toBe(true);
		expect(auth.needsBreweryOnboarding).toBe(false);
		expect(mockStorage.getItem('brewyou_pending_onboarding')).toBeNull();
	});

	it('self-bootstraps init and returns authentication status on ready()', async () => {
		mockStorage.setItem('brewyou_token', 'valid-jwt');
		vi.spyOn(api.auth, 'me').mockResolvedValue(mockUser);

		const isAuth = await auth.ready();

		expect(isAuth).toBe(true);
		expect(auth.status).toBe('authenticated');
		expect(auth.user).toEqual(mockUser);
	});

	it('deduplicates concurrent calls to init() returning the same promise', async () => {
		mockStorage.setItem('brewyou_token', 'valid-jwt');
		const meSpy = vi.spyOn(api.auth, 'me').mockResolvedValue(mockUser);

		const [p1, p2] = [auth.init(), auth.init()];
		await Promise.all([p1, p2]);

		expect(meSpy).toHaveBeenCalledTimes(1);
	});

	it('does not show session expired error on anonymous visitor expiry call', () => {
		// Clean visitor without prior token or user
		auth.clearAuth();
		auth.clearError();

		auth.handleSessionExpired();

		expect(auth.status).toBe('unauthenticated');
		expect(auth.error).toBeNull();
		expect(auth.errorCode).toBeNull();
	});
});
