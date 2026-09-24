import {
	api,
	onUnauthorized,
	onTokenRefreshed,
	setClientAuthToken,
	ApiClientError
} from '$lib/api/client';
import { i18n, t, type LocaleCode } from '$lib/i18n/index.svelte';
import type {
	ApiErrorDetail,
	RegisterRequest,
	UpdateUserPreferencesRequest,
	UserDto,
	VolumeUnit
} from '$lib/types/api';

export type AuthStatus = 'initializing' | 'authenticated' | 'unauthenticated';

type AuthClearedCallback = () => void;
const authClearedCallbacks: AuthClearedCallback[] = [];

export function onAuthCleared(cb: AuthClearedCallback) {
	authClearedCallbacks.push(cb);
}

class AuthState {
	user = $state<UserDto | null>(null);
	token = $state<string | null>(
		typeof window !== 'undefined' ? localStorage.getItem('brewyou_token') : null
	);
	isLoading = $state<boolean>(true);
	error = $state<string | null>(null);
	errorCode = $state<string | null>(null);
	errorDetails = $state<ApiErrorDetail[] | null>(null);
	needsBreweryOnboarding = $state<boolean>(false);

	private initPromise: Promise<void> | null = null;

	constructor() {
		onUnauthorized(() => {
			this.handleSessionExpired();
		});

		onTokenRefreshed((data) => {
			this.setAuth(data.accessToken, data.user, data.isNewUser ?? false);
		});
	}

	get isAuthenticated(): boolean {
		return this.user !== null;
	}

	get status(): AuthStatus {
		if (this.isLoading) return 'initializing';
		return this.isAuthenticated ? 'authenticated' : 'unauthenticated';
	}

	async ready(): Promise<boolean> {
		if (!this.initPromise) {
			void this.init();
		}
		await this.initPromise;
		return this.isAuthenticated;
	}

	async init(): Promise<void> {
		if (this.initPromise) {
			return this.initPromise;
		}

		this.initPromise = (async () => {
			this.isLoading = true;
			this.clearError();

			if (typeof window !== 'undefined') {
				const pendingOnboarding = localStorage.getItem('brewyou_pending_onboarding') === 'true';
				const savedToken = localStorage.getItem('brewyou_token');
				if (savedToken) {
					this.token = savedToken;
					setClientAuthToken(savedToken);
					try {
						this.user = await api.auth.me();
						if (this.user.preferredLanguage) {
							i18n.setLocale(this.user.preferredLanguage as LocaleCode);
						}
						if (pendingOnboarding) {
							this.needsBreweryOnboarding = true;
						}
						this.isLoading = false;
						return;
					} catch {
						// Token expired or invalid, try refresh
					}
				}

				// Try cookie-based refresh
				try {
					const authData = await api.auth.refresh();
					this.setAuth(authData.accessToken, authData.user, authData.isNewUser ?? false);
					if (pendingOnboarding) {
						this.needsBreweryOnboarding = true;
					}
				} catch {
					this.clearAuth();
				}
			}

			this.isLoading = false;
		})();

		return this.initPromise;
	}

	clearError() {
		this.error = null;
		this.errorCode = null;
		this.errorDetails = null;
	}

	handleSessionExpired() {
		const hadSession =
			this.user !== null ||
			this.token !== null ||
			(typeof window !== 'undefined' && localStorage.getItem('brewyou_token') !== null);
		this.clearAuth();
		if (hadSession) {
			this.error = t('auth.session_expired');
			this.errorCode = 'TOKEN_EXPIRED';
		}
	}

	async login(email: string, password: string): Promise<boolean> {
		this.isLoading = true;
		this.clearError();
		try {
			const data = await api.auth.login({ email, password });
			this.setAuth(data.accessToken, data.user, data.isNewUser ?? false);
			return true;
		} catch (err: unknown) {
			this.extractError(err, 'Login failed. Please check your credentials.');
			return false;
		} finally {
			this.isLoading = false;
		}
	}

	async register(
		email: string,
		password: string,
		displayName: string,
		preferredLanguage?: string,
		preferredVolumeUnit?: VolumeUnit
	): Promise<boolean> {
		this.isLoading = true;
		this.clearError();
		try {
			const req: RegisterRequest = {
				email,
				password,
				displayName,
				preferredLanguage,
				preferredVolumeUnit
			};
			const data = await api.auth.register(req);
			this.setAuth(data.accessToken, data.user, data.isNewUser ?? true);
			return true;
		} catch (err: unknown) {
			this.extractError(err, 'Registration failed.');
			return false;
		} finally {
			this.isLoading = false;
		}
	}

	async loginWithGoogle(idToken: string, preferredLanguage?: string): Promise<boolean> {
		this.isLoading = true;
		this.clearError();
		try {
			const data = await api.auth.google({ idToken, preferredLanguage });
			this.setAuth(data.accessToken, data.user, data.isNewUser ?? false);
			return true;
		} catch (err: unknown) {
			this.extractError(err, 'Google sign-in failed. Please try again.');
			return false;
		} finally {
			this.isLoading = false;
		}
	}

	async logout() {
		try {
			await api.auth.logout();
		} catch {
			// Ignore network errors on logout
		} finally {
			this.clearAuth();
		}
	}

	async updateProfile(displayName: string): Promise<boolean> {
		this.clearError();
		try {
			const updated = await api.auth.updateProfile({ displayName });
			this.user = updated;
			return true;
		} catch (err: unknown) {
			this.extractError(err, 'Failed to update profile.');
			return false;
		}
	}

	async updatePreferences(req: UpdateUserPreferencesRequest): Promise<boolean> {
		this.clearError();
		try {
			const updated = await api.auth.updatePreferences(req);
			this.user = updated;
			if (updated.preferredLanguage) {
				i18n.setLocale(updated.preferredLanguage as LocaleCode);
			}
			return true;
		} catch (err: unknown) {
			this.extractError(err, 'Failed to update preferences.');
			return false;
		}
	}

	completeOnboarding() {
		this.needsBreweryOnboarding = false;
		if (typeof window !== 'undefined') {
			localStorage.removeItem('brewyou_pending_onboarding');
		}
	}

	private extractError(err: unknown, fallbackMessage: string) {
		if (err instanceof ApiClientError) {
			this.error = err.message || fallbackMessage;
			this.errorCode = err.code || null;
			this.errorDetails = err.details || null;
		} else {
			this.error = (err as Error).message || fallbackMessage;
			this.errorCode = null;
			this.errorDetails = null;
		}
	}

	private setAuth(token: string, user: UserDto, isNewUser = false) {
		this.token = token;
		this.user = user;
		setClientAuthToken(token);
		if (user.preferredLanguage) {
			i18n.setLocale(user.preferredLanguage as LocaleCode);
		}
		if (isNewUser) {
			this.needsBreweryOnboarding = true;
			if (typeof window !== 'undefined') {
				localStorage.setItem('brewyou_pending_onboarding', 'true');
			}
		}
		if (typeof window !== 'undefined') {
			localStorage.setItem('brewyou_token', token);
		}
	}

	clearAuth() {
		this.token = null;
		this.user = null;
		this.needsBreweryOnboarding = false;
		this.initPromise = null;
		setClientAuthToken(null);
		if (typeof window !== 'undefined') {
			localStorage.removeItem('brewyou_token');
			localStorage.removeItem('brewyou_active_setup_id');
			localStorage.removeItem('brewyou_brewery_setups');
			localStorage.removeItem('brewyou_pending_onboarding');
		}
		for (const cb of authClearedCallbacks) {
			try {
				cb();
			} catch {
				// Teardown callback error ignored
			}
		}
	}
}

export const auth = new AuthState();
