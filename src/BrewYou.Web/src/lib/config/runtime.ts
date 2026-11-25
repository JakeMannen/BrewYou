import { isLocalhostUrl } from '$lib/utils/batchQr';

export interface RuntimeConfig {
	googleClientId: string;
	apiUrl: string;
	appUrl: string;
}

interface RuntimeWindow extends Window {
	__GOOGLE_CLIENT_ID__?: unknown;
	__API_URL__?: unknown;
	__APP_URL__?: unknown;
}

function firstNonEmpty(...values: Array<string | undefined>): string {
	for (const value of values) {
		if (typeof value === 'string' && value.trim().length > 0) {
			return value.trim();
		}
	}
	return '';
}

function hasNonEmptyString(value: unknown): value is string {
	return typeof value === 'string' && value.trim().length > 0;
}

/**
 * Validates that a runtime URL uses an explicit HTTP(S) scheme without embedded credentials.
 */
export function sanitizeRuntimeHttpUrl(value: string | undefined): string {
	if (typeof value !== 'string') return '';
	const trimmed = value.trim();
	if (!trimmed) return '';
	try {
		const parsed = new URL(trimmed);
		if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
			return '';
		}
		if (parsed.username || parsed.password) {
			return '';
		}
		return trimmed.replace(/\/+$/, '');
	} catch {
		return '';
	}
}

/**
 * Resolves public frontend configuration from runtime server environment variables.
 * Supports both `PUBLIC_GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_ID` so container stacks
 * (e.g. Docker Compose / Portainer) work regardless of which variable name is injected.
 */
export function resolveRuntimeConfig(
	publicEnv?: Record<string, string | undefined>,
	processEnv?: Record<string, string | undefined>
): RuntimeConfig {
	return {
		googleClientId: firstNonEmpty(
			publicEnv?.PUBLIC_GOOGLE_CLIENT_ID,
			processEnv?.PUBLIC_GOOGLE_CLIENT_ID,
			processEnv?.GOOGLE_CLIENT_ID
		),
		apiUrl: sanitizeRuntimeHttpUrl(
			firstNonEmpty(publicEnv?.PUBLIC_API_URL, processEnv?.PUBLIC_API_URL)
		),
		appUrl: sanitizeRuntimeHttpUrl(
			firstNonEmpty(publicEnv?.PUBLIC_APP_URL, processEnv?.PUBLIC_APP_URL)
		)
	};
}

/**
 * Hydrates browser globals from SSR runtime configuration without overwriting explicit string
 * overrides, guarding against DOM clobbering and non-HTTP(S) URL schemes while preserving LAN
 * auto-detection when URLs default to localhost.
 */
export function applyRuntimeConfigToWindow(config?: Partial<RuntimeConfig> | null): void {
	if (typeof window === 'undefined' || !config) return;
	const win = window as unknown as RuntimeWindow;
	const windowLocation = window.location?.origin || window.location?.hostname || '';
	const isLocalhostWindow = !windowLocation || isLocalhostUrl(windowLocation);

	const googleClientId = config.googleClientId?.trim();
	if (googleClientId && !hasNonEmptyString(win.__GOOGLE_CLIENT_ID__)) {
		win.__GOOGLE_CLIENT_ID__ = googleClientId;
	}

	const apiUrl = sanitizeRuntimeHttpUrl(config.apiUrl);
	if (
		apiUrl &&
		!hasNonEmptyString(win.__API_URL__) &&
		(!isLocalhostUrl(apiUrl) || isLocalhostWindow)
	) {
		win.__API_URL__ = apiUrl;
	}

	const appUrl = sanitizeRuntimeHttpUrl(config.appUrl);
	if (
		appUrl &&
		!hasNonEmptyString(win.__APP_URL__) &&
		(!isLocalhostUrl(appUrl) || isLocalhostWindow)
	) {
		win.__APP_URL__ = appUrl;
	}
}
