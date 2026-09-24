import { describe, it, expect, beforeEach, vi } from 'vitest';
import { auth } from './auth.svelte';
import { api } from '$lib/api/client';
import { isGoogleAuthAvailable, getGoogleClientId } from '$lib/auth/gis';
import type { AuthResponse } from '$lib/types/api';

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

describe('Google OAuth Frontend Store & Utilities', () => {
	beforeEach(() => {
		vi.restoreAllMocks();
		mockStorage.clear();
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

	it('detects when Google Client ID is not configured by default', () => {
		(window as unknown as { __GOOGLE_CLIENT_ID__?: string }).__GOOGLE_CLIENT_ID__ = '';
		try {
			expect(isGoogleAuthAvailable()).toBe(false);
			expect(getGoogleClientId()).toBe('');
		} finally {
			delete (window as unknown as { __GOOGLE_CLIENT_ID__?: string }).__GOOGLE_CLIENT_ID__;
		}
	});

	it('successfully logs in with Google and populates auth store', async () => {
		const mockResponse: AuthResponse = {
			accessToken: 'google-jwt-access-token',
			refreshToken: 'google-refresh-token',
			expiresAt: '2026-09-04T19:00:00Z',
			user: {
				id: 'google-user-123',
				email: 'brewer@google.com',
				displayName: 'Google Brewer',
				preferredLanguage: 'sv',
				preferredVolumeUnit: 'Liters'
			}
		};

		vi.spyOn(api.auth, 'google').mockResolvedValue(mockResponse);

		const result = await auth.loginWithGoogle('mock-id-token-abc', 'sv');

		expect(result).toBe(true);
		expect(auth.isAuthenticated).toBe(true);
		expect(auth.user).toEqual(mockResponse.user);
		expect(auth.token).toBe('google-jwt-access-token');
		expect(auth.error).toBeNull();
		expect(mockStorage.getItem('brewyou_token')).toBe('google-jwt-access-token');
	});

	it('handles failure from Google login API cleanly and records error', async () => {
		vi.spyOn(api.auth, 'google').mockRejectedValue(
			new Error('The provided Google ID token is invalid or has expired.')
		);

		const result = await auth.loginWithGoogle('invalid-token');

		expect(result).toBe(false);
		expect(auth.error).toBe('The provided Google ID token is invalid or has expired.');
	});
});
