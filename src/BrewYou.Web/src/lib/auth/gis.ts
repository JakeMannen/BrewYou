interface CustomWindow extends Window {
	__GOOGLE_CLIENT_ID__?: string;
	google?: {
		accounts: {
			id: {
				initialize: (config: GoogleIdConfiguration) => void;
				prompt: (notification?: (notification: PromptMomentNotification) => void) => void;
				renderButton: (parent: HTMLElement, options: GoogleButtonOptions) => void;
				disableAutoSelect: () => void;
			};
		};
	};
}

export interface GoogleIdConfiguration {
	client_id: string;
	callback: (response: GoogleCredentialResponse) => void;
	auto_select?: boolean;
	cancel_on_tap_outside?: boolean;
	context?: 'signin' | 'signup' | 'use';
}

export interface GoogleButtonOptions {
	type?: 'standard' | 'icon';
	theme?: 'outline' | 'filled_blue' | 'filled_black';
	size?: 'large' | 'medium' | 'small';
	text?: 'signin_with' | 'signup_with' | 'continue_with';
	shape?: 'rectangular' | 'pill' | 'circle' | 'square';
	logo_alignment?: 'left' | 'center';
	width?: number;
	locale?: string;
}

export interface GoogleCredentialResponse {
	credential: string;
	select_by?: string;
}

export interface PromptMomentNotification {
	isNotDisplayed: () => boolean;
	getNotDisplayedReason: () => string;
	isSkippedMoment: () => boolean;
	getSkippedReason: () => string;
	isDismissedMoment: () => boolean;
	getDismissedReason: () => string;
}

let gisScriptPromise: Promise<void> | null = null;

export function getGoogleClientId(): string {
	if (typeof window !== 'undefined') {
		const win = window as unknown as CustomWindow;
		if (typeof win.__GOOGLE_CLIENT_ID__ === 'string') {
			return win.__GOOGLE_CLIENT_ID__;
		}
	}
	const metaEnv =
		typeof import.meta !== 'undefined'
			? (import.meta as unknown as { env?: Record<string, string> }).env
			: undefined;
	if (metaEnv?.PUBLIC_GOOGLE_CLIENT_ID) {
		return metaEnv.PUBLIC_GOOGLE_CLIENT_ID;
	}
	if (metaEnv?.VITE_GOOGLE_CLIENT_ID) {
		return metaEnv.VITE_GOOGLE_CLIENT_ID;
	}
	if (typeof process !== 'undefined' && process.env?.PUBLIC_GOOGLE_CLIENT_ID) {
		return process.env.PUBLIC_GOOGLE_CLIENT_ID;
	}
	return '';
}

export function isGoogleAuthAvailable(): boolean {
	const clientId = getGoogleClientId();
	return typeof clientId === 'string' && clientId.trim().length > 0;
}

export function loadGisScript(): Promise<void> {
	if (typeof window === 'undefined') {
		return Promise.reject(new Error('GIS cannot be loaded in non-browser environment.'));
	}

	const win = window as unknown as CustomWindow;
	if (win.google?.accounts?.id) {
		return Promise.resolve();
	}

	if (gisScriptPromise) {
		return gisScriptPromise;
	}

	gisScriptPromise = new Promise((resolve, reject) => {
		const existing = document.getElementById('google-gsi-client');
		if (existing) {
			existing.addEventListener('load', () => resolve());
			existing.addEventListener('error', () => reject(new Error('GIS_SCRIPT_LOAD_ERROR')));
			return;
		}

		const script = document.createElement('script');
		script.id = 'google-gsi-client';
		script.src = 'https://accounts.google.com/gsi/client';
		script.async = true;
		script.defer = true;
		script.onload = () => resolve();
		script.onerror = () => {
			gisScriptPromise = null;
			reject(new Error('GIS_SCRIPT_BLOCKED_OR_FAILED'));
		};
		document.head.appendChild(script);
	});

	return gisScriptPromise;
}

export function getGoogle() {
	if (typeof window !== 'undefined') {
		return (window as unknown as CustomWindow).google;
	}
	return undefined;
}
