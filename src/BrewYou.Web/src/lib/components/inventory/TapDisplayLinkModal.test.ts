import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';
import de from '$lib/i18n/locales/de.json';
import fr from '$lib/i18n/locales/fr.json';
import es from '$lib/i18n/locales/es.json';
import { settings } from '$lib/stores/settings.svelte';
import { isLocalhostUrl } from '$lib/utils/batchQr';

function extractPath(urlString: string | null | undefined): string {
	if (!urlString) return '';
	try {
		if (urlString.startsWith('http://') || urlString.startsWith('https://')) {
			const parsed = new URL(urlString);
			return `${parsed.pathname}${parsed.search}`;
		}
	} catch {
		// ignore
	}
	return urlString.startsWith('/') ? urlString : `/${urlString}`;
}

function resolveTapLinks(
	linkData: { kioskUrl: string; imageUrl: string; manifestUrl: string },
	options?: {
		customAppBase?: string;
		customApiBase?: string;
	}
) {
	const activeAppBaseUrl = options?.customAppBase?.trim()
		? options.customAppBase.trim().replace(/\/+$/, '')
		: settings.resolvedAppBaseUrl;

	let activeApiBaseUrl: string;
	if (options?.customApiBase?.trim()) {
		activeApiBaseUrl = options.customApiBase.trim().replace(/\/+$/, '');
	} else if (options?.customAppBase?.trim()) {
		const trimmed = options.customAppBase.trim().replace(/\/+$/, '');
		if (!isLocalhostUrl(trimmed) && trimmed.includes(':3000')) {
			activeApiBaseUrl = trimmed.replace(':3000', ':5000');
		} else {
			activeApiBaseUrl = trimmed;
		}
	} else {
		activeApiBaseUrl = settings.resolvedWebhookBaseUrl;
	}

	const isLocalhost = isLocalhostUrl(activeAppBaseUrl) || isLocalhostUrl(activeApiBaseUrl);

	const kioskUrl = linkData.kioskUrl ? `${activeAppBaseUrl}${extractPath(linkData.kioskUrl)}` : '';
	const imageUrl = linkData.imageUrl ? `${activeApiBaseUrl}${extractPath(linkData.imageUrl)}` : '';
	const manifestUrl = linkData.manifestUrl
		? `${activeApiBaseUrl}${extractPath(linkData.manifestUrl)}`
		: '';

	return {
		activeAppBaseUrl,
		activeApiBaseUrl,
		isLocalhost,
		kioskUrl,
		imageUrl,
		manifestUrl
	};
}

describe('TapDisplayLinkModal Network Settings & Contract Invariants', () => {
	beforeEach(() => {
		vi.unstubAllGlobals();
		vi.unstubAllEnvs();
		settings.appBaseUrl = '';
		settings.webhookBaseUrl = '';
	});

	afterEach(() => {
		vi.unstubAllGlobals();
		vi.unstubAllEnvs();
		settings.appBaseUrl = '';
		settings.webhookBaseUrl = '';
	});

	it('maintains complete i18n keys for taps across all five locales', () => {
		const locales = [en, sv, de, fr, es];
		const requiredKeys = [
			'display_link_title',
			'display_link_desc',
			'kiosk_web_link',
			'open_kiosk_tab',
			'kiosk_web_desc',
			'rendered_image_url',
			'rendered_image_desc',
			'manifest_api_url',
			'manifest_api_desc',
			'security_protected_title',
			'security_protected_desc',
			'display_link_disabled',
			'display_link_disabled_desc',
			'enable_display_link_btn',
			'revoke_link_btn',
			'regenerate_token_btn',
			'regen_confirm_title',
			'regen_confirm_msg',
			'regen_confirm_btn',
			'revoke_confirm_title',
			'revoke_confirm_msg',
			'revoke_confirm_btn',
			'localhost_warning',
			'override_host_btn',
			'override_app_host_label',
			'override_api_host_label',
			'open_settings',
			'reset_override'
		];

		for (const loc of locales) {
			for (const key of requiredKeys) {
				const val = (loc.taps as Record<string, string>)[key];
				expect(val).toBeDefined();
				expect(typeof val).toBe('string');
				expect(val.length).toBeGreaterThan(0);
			}
		}

		expect(en.taps.display_link_title).toContain('{tapName}');
		expect(sv.taps.display_link_title).toContain('{tapName}');
	});

	it('extracts URL paths correctly from full or relative link inputs', () => {
		expect(extractPath('http://localhost:3000/tap/token-123')).toBe('/tap/token-123');
		expect(extractPath('http://localhost:5000/api/v1/tap/token-123/image')).toBe(
			'/api/v1/tap/token-123/image'
		);
		expect(extractPath('http://localhost:5000/api/v1/tap/token-123')).toBe('/api/v1/tap/token-123');
		expect(extractPath('https://mybrewery.com/tap/token-123?fullscreen=true')).toBe(
			'/tap/token-123?fullscreen=true'
		);
		expect(extractPath('/tap/token-123')).toBe('/tap/token-123');
		expect(extractPath('')).toBe('');
	});

	it('resolves tap links with default settings and flags localhost accurately', () => {
		const rawLinks = {
			kioskUrl: 'http://localhost:3000/tap/plain-tok-01',
			imageUrl: 'http://localhost:5000/api/v1/tap/plain-tok-01/image',
			manifestUrl: 'http://localhost:5000/api/v1/tap/plain-tok-01'
		};

		const resolved = resolveTapLinks(rawLinks);
		expect(resolved.isLocalhost).toBe(true);
		expect(resolved.kioskUrl).toBe('http://localhost:3000/tap/plain-tok-01');
		expect(resolved.imageUrl).toBe('http://localhost:5000/api/v1/tap/plain-tok-01/image');
		expect(resolved.manifestUrl).toBe('http://localhost:5000/api/v1/tap/plain-tok-01');
	});

	it('takes advantage of user configured network settings in the settings store', () => {
		settings.appBaseUrl = 'http://192.168.1.188:3000';
		settings.webhookBaseUrl = 'http://192.168.1.188:5000';

		const rawLinks = {
			kioskUrl: 'http://localhost:3000/tap/token-lan-99',
			imageUrl: 'http://localhost:5000/api/v1/tap/token-lan-99/image',
			manifestUrl: 'http://localhost:5000/api/v1/tap/token-lan-99'
		};

		const resolved = resolveTapLinks(rawLinks);
		expect(resolved.isLocalhost).toBe(false);
		expect(resolved.kioskUrl).toBe('http://192.168.1.188:3000/tap/token-lan-99');
		expect(resolved.imageUrl).toBe('http://192.168.1.188:5000/api/v1/tap/token-lan-99/image');
		expect(resolved.manifestUrl).toBe('http://192.168.1.188:5000/api/v1/tap/token-lan-99');
	});

	it('supports modal inline host overrides and derives API port when omitted', () => {
		const rawLinks = {
			kioskUrl: '/tap/token-override-42',
			imageUrl: '/api/v1/tap/token-override-42/image',
			manifestUrl: '/api/v1/tap/token-override-42'
		};

		// 1. App base override only (auto-derives :5000 for API)
		const overrideAppOnly = resolveTapLinks(rawLinks, {
			customAppBase: 'http://brewery.lan:3000/'
		});
		expect(overrideAppOnly.isLocalhost).toBe(false);
		expect(overrideAppOnly.kioskUrl).toBe('http://brewery.lan:3000/tap/token-override-42');
		expect(overrideAppOnly.imageUrl).toBe(
			'http://brewery.lan:5000/api/v1/tap/token-override-42/image'
		);
		expect(overrideAppOnly.manifestUrl).toBe(
			'http://brewery.lan:5000/api/v1/tap/token-override-42'
		);

		// 2. Both app and api custom overrides
		const overrideBoth = resolveTapLinks(rawLinks, {
			customAppBase: 'https://kiosk.brewery.com',
			customApiBase: 'https://api.brewery.com'
		});
		expect(overrideBoth.isLocalhost).toBe(false);
		expect(overrideBoth.kioskUrl).toBe('https://kiosk.brewery.com/tap/token-override-42');
		expect(overrideBoth.imageUrl).toBe(
			'https://api.brewery.com/api/v1/tap/token-override-42/image'
		);
		expect(overrideBoth.manifestUrl).toBe('https://api.brewery.com/api/v1/tap/token-override-42');
	});
});
