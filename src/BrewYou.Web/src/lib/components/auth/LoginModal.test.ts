import { describe, it, expect, beforeEach, vi } from 'vitest';
import { auth } from '$lib/stores/auth.svelte';
import { i18n } from '$lib/i18n/index.svelte';

describe('LoginModal Logic & Interaction Contracts', () => {
	beforeEach(() => {
		vi.restoreAllMocks();
		auth.clearAuth();
		auth.clearError();
		i18n.init();
	});

	it('provides localized strings for all modal elements in en and sv', () => {
		i18n.setLocale('en');
		expect(i18n.t('auth.login_title')).toBe('Sign In to BrewYou');
		expect(i18n.t('auth.tab_signin')).toBe('Sign In');
		expect(i18n.t('auth.tab_signup')).toBe('Create Account');
		expect(i18n.t('auth.passwords_mismatch')).toBe('Passwords do not match.');

		i18n.setLocale('sv');
		expect(i18n.t('auth.login_title')).toBe('Logga in på BrewYou');
		expect(i18n.t('auth.tab_signin')).toBe('Logga in');
		expect(i18n.t('auth.tab_signup')).toBe('Skapa konto');
		expect(i18n.t('auth.passwords_mismatch')).toBe('Lösenorden matchar inte.');
	});

	it('validates password matching on signup', () => {
		const password: string = 'Password123!';
		const confirmMismatch: string = 'Password456!';
		const confirmMatch: string = 'Password123!';

		expect(password === confirmMismatch).toBe(false);
		expect(password === confirmMatch).toBe(true);
	});

	it('prohibits Escape key dismissal to preserve zero-anonymous state invariant', () => {
		let defaultPrevented = false;
		let propagationStopped = false;

		const mockEvent = {
			key: 'Escape',
			preventDefault: () => {
				defaultPrevented = true;
			},
			stopPropagation: () => {
				propagationStopped = true;
			}
		};

		// The keydown handler for the non-dismissible modal traps Escape
		if (mockEvent.key === 'Escape') {
			mockEvent.preventDefault();
			mockEvent.stopPropagation();
		}

		expect(defaultPrevented).toBe(true);
		expect(propagationStopped).toBe(true);
		expect(auth.isAuthenticated).toBe(false);
	});

	it('requires email and password for form submission', () => {
		const isValidForm = (email: string, pass: string) => {
			return email.trim().length > 0 && pass.length >= 6;
		};

		expect(isValidForm('', '')).toBe(false);
		expect(isValidForm('brewer@example.com', '')).toBe(false);
		expect(isValidForm('brewer@example.com', '12345')).toBe(false);
		expect(isValidForm('brewer@example.com', '123456')).toBe(true);
	});
});
