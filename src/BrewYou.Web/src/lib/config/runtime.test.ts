import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import {
	resolveRuntimeConfig,
	applyRuntimeConfigToWindow,
	sanitizeRuntimeHttpUrl
} from './runtime';
import { getGoogleClientId, isGoogleAuthAvailable } from '$lib/auth/gis';

describe('Runtime Environment Configuration', () => {
	let originalWindow: unknown;

	beforeEach(() => {
		originalWindow = (globalThis as Record<string, unknown>).window;
	});

	afterEach(() => {
		if (originalWindow === undefined) {
			delete (globalThis as Record<string, unknown>).window;
		} else {
			(globalThis as Record<string, unknown>).window = originalWindow;
		}
	});

	it('resolves PUBLIC_GOOGLE_CLIENT_ID from SvelteKit public env first', () => {
		const config = resolveRuntimeConfig(
			{ PUBLIC_GOOGLE_CLIENT_ID: '  public-client-id.apps.googleusercontent.com  ' },
			{
				PUBLIC_GOOGLE_CLIENT_ID: 'process-public-id',
				GOOGLE_CLIENT_ID: 'process-fallback-id'
			}
		);

		expect(config.googleClientId).toBe('public-client-id.apps.googleusercontent.com');
	});

	it('falls back to GOOGLE_CLIENT_ID when PUBLIC_GOOGLE_CLIENT_ID is empty', () => {
		const config = resolveRuntimeConfig(
			{ PUBLIC_GOOGLE_CLIENT_ID: '   ' },
			{ PUBLIC_GOOGLE_CLIENT_ID: '', GOOGLE_CLIENT_ID: '  direct-google-id  ' }
		);

		expect(config.googleClientId).toBe('direct-google-id');
	});

	it('resolves PUBLIC_API_URL and PUBLIC_APP_URL from runtime env', () => {
		const config = resolveRuntimeConfig(
			{
				PUBLIC_API_URL: 'https://api.brewyou.example',
				PUBLIC_APP_URL: 'https://app.brewyou.example'
			},
			{}
		);

		expect(config.apiUrl).toBe('https://api.brewyou.example');
		expect(config.appUrl).toBe('https://app.brewyou.example');
	});

	it('hydrates window globals so GIS helpers detect runtime GOOGLE_CLIENT_ID', () => {
		(globalThis as Record<string, unknown>).window = {
			location: { origin: 'https://brew.example.com', hostname: 'brew.example.com' }
		};

		applyRuntimeConfigToWindow({
			googleClientId: 'runtime-client-123.apps.googleusercontent.com',
			apiUrl: 'https://api.brew.example.com/',
			appUrl: 'https://brew.example.com/'
		});

		expect(getGoogleClientId()).toBe('runtime-client-123.apps.googleusercontent.com');
		expect(isGoogleAuthAvailable()).toBe(true);
		expect((window as unknown as { __API_URL__?: string }).__API_URL__).toBe(
			'https://api.brew.example.com'
		);
		expect((window as unknown as { __APP_URL__?: string }).__APP_URL__).toBe(
			'https://brew.example.com'
		);
	});

	it('does not set empty GOOGLE_CLIENT_ID or overwrite existing window globals', () => {
		(globalThis as Record<string, unknown>).window = {
			__GOOGLE_CLIENT_ID__: 'existing-id',
			location: { origin: 'http://localhost:3000', hostname: 'localhost' }
		};

		applyRuntimeConfigToWindow({
			googleClientId: 'new-id',
			apiUrl: '',
			appUrl: ''
		});

		expect(getGoogleClientId()).toBe('existing-id');
		expect((window as unknown as { __API_URL__?: string }).__API_URL__).toBeUndefined();
	});

	it('ignores default localhost URLs when accessed from a LAN hostname', () => {
		(globalThis as Record<string, unknown>).window = {
			location: { protocol: 'http:', hostname: '192.168.1.50', port: '3000' }
		};

		applyRuntimeConfigToWindow({
			googleClientId: 'lan-google-client-id',
			apiUrl: 'http://localhost:5000',
			appUrl: 'http://localhost:3000'
		});

		expect(getGoogleClientId()).toBe('lan-google-client-id');
		expect((window as unknown as { __API_URL__?: string }).__API_URL__).toBeUndefined();
		expect((window as unknown as { __APP_URL__?: string }).__APP_URL__).toBeUndefined();
	});

	it('rejects non-HTTP(S) URL schemes and credential-bearing URLs', () => {
		expect(sanitizeRuntimeHttpUrl('javascript:alert(1)')).toBe('');
		expect(sanitizeRuntimeHttpUrl('data:text/html,<script>alert(1)</script>')).toBe('');
		expect(sanitizeRuntimeHttpUrl('file:///etc/passwd')).toBe('');
		expect(sanitizeRuntimeHttpUrl('https://user:pass@brew.example.com')).toBe('');
		expect(sanitizeRuntimeHttpUrl('not-a-url')).toBe('');
		expect(sanitizeRuntimeHttpUrl('https://brew.example.com///')).toBe('https://brew.example.com');

		const resolved = resolveRuntimeConfig(
			{
				PUBLIC_API_URL: 'javascript:alert(1)',
				PUBLIC_APP_URL: 'https://user:secret@brew.example.com'
			},
			{}
		);
		expect(resolved.apiUrl).toBe('');
		expect(resolved.appUrl).toBe('');

		(globalThis as Record<string, unknown>).window = {
			location: { origin: 'https://brew.example.com', hostname: 'brew.example.com' }
		};
		applyRuntimeConfigToWindow({
			apiUrl: 'javascript:alert(1)',
			appUrl: 'data:text/html,evil'
		});
		expect((window as unknown as { __API_URL__?: string }).__API_URL__).toBeUndefined();
		expect((window as unknown as { __APP_URL__?: string }).__APP_URL__).toBeUndefined();
	});

	it('overwrites non-string DOM-clobbered window globals with valid runtime values', () => {
		const fakeDomElement = { tagName: 'DIV' };
		(globalThis as Record<string, unknown>).window = {
			__GOOGLE_CLIENT_ID__: fakeDomElement,
			__API_URL__: fakeDomElement,
			__APP_URL__: fakeDomElement,
			location: { origin: 'https://brew.example.com', hostname: 'brew.example.com' }
		};

		applyRuntimeConfigToWindow({
			googleClientId: 'safe-client-id',
			apiUrl: 'https://api.brew.example.com',
			appUrl: 'https://brew.example.com'
		});

		expect(getGoogleClientId()).toBe('safe-client-id');
		expect((window as unknown as { __API_URL__?: string }).__API_URL__).toBe(
			'https://api.brew.example.com'
		);
		expect((window as unknown as { __APP_URL__?: string }).__APP_URL__).toBe(
			'https://brew.example.com'
		);
	});
});
