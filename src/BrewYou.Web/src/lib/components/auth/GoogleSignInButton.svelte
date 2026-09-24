<script lang="ts">
	import { onMount } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { t, i18n } from '$lib/i18n/index.svelte';
	import {
		getGoogle,
		getGoogleClientId,
		isGoogleAuthAvailable,
		loadGisScript,
		type GoogleCredentialResponse
	} from '$lib/auth/gis';
	import { AlertTriangle, Loader2 } from '@lucide/svelte';

	interface Props {
		mode?: 'signin' | 'signup' | 'continue';
		onSuccess?: () => void;
		onError?: (msg: string) => void;
	}

	let { mode = 'signin', onSuccess, onError }: Props = $props();

	let isConfigured = $state(false);
	let isScriptLoading = $state(true);
	let isAuthenticating = $state(false);
	let scriptError = $state<string | null>(null);
	let nativeRenderSucceeded = $state(false);
	let googleButtonContainer = $state<HTMLDivElement | null>(null);

	const buttonText = $derived(
		mode === 'signup'
			? t('auth.google_signup')
			: mode === 'continue'
				? t('auth.google_continue')
				: t('auth.google_signin')
	);

	async function handleCredential(response: GoogleCredentialResponse) {
		if (!response.credential) {
			const err = t('auth.google_auth_failed');
			onError?.(err);
			return;
		}

		isAuthenticating = true;
		const success = await auth.loginWithGoogle(response.credential, i18n.locale);
		isAuthenticating = false;

		if (success) {
			onSuccess?.();
		} else if (auth.error) {
			onError?.(auth.error);
		}
	}

	onMount(async () => {
		isConfigured = isGoogleAuthAvailable();
		if (!isConfigured) {
			isScriptLoading = false;
			return;
		}

		try {
			await loadGisScript();
			isScriptLoading = false;

			const clientId = getGoogleClientId();
			const google = getGoogle();
			if (google?.accounts?.id && clientId) {
				google.accounts.id.initialize({
					client_id: clientId,
					callback: handleCredential,
					auto_select: false,
					cancel_on_tap_outside: true,
					context: mode === 'signup' ? 'signup' : 'signin'
				});

				if (googleButtonContainer) {
					try {
						const availableWidth =
							typeof window !== 'undefined'
								? Math.min(380, Math.max(240, window.innerWidth - 64))
								: 320;

						google.accounts.id.renderButton(googleButtonContainer, {
							type: 'standard',
							theme: 'filled_black',
							size: 'large',
							text: mode === 'signup' ? 'signup_with' : 'signin_with',
							shape: 'rectangular',
							logo_alignment: 'left',
							width: availableWidth,
							locale: i18n.locale
						});
						nativeRenderSucceeded = true;
					} catch {
						nativeRenderSucceeded = false;
					}
				}
			}
		} catch {
			isScriptLoading = false;
			scriptError = t('auth.google_script_blocked');
		}
	});

	function triggerSignIn() {
		if (isAuthenticating || isScriptLoading) return;

		if (!isConfigured) {
			onError?.(t('auth.google_not_configured'));
			return;
		}

		const google = getGoogle();
		if (google?.accounts?.id) {
			google.accounts.id.prompt();
		}
	}
</script>

{#if !isConfigured}
	<!-- Graceful fallback when Google Client ID is not configured -->
	<div
		class="flex items-center gap-2 rounded-xl border border-dashed border-slate-800 bg-slate-950/60 p-3 text-center text-xs text-slate-500"
		role="note"
	>
		<AlertTriangle class="h-4 w-4 flex-shrink-0 text-amber-500/70" />
		<span>{t('auth.google_not_configured')}</span>
	</div>
{:else if scriptError}
	<!-- Graceful fallback when ad-blocker or network blocks GIS -->
	<div
		class="flex items-center gap-2 rounded-xl border border-amber-900/40 bg-amber-950/20 p-3 text-xs text-amber-400"
		role="alert"
	>
		<AlertTriangle class="h-4 w-4 flex-shrink-0" />
		<span>{scriptError}</span>
	</div>
{:else}
	<!-- Branded Google Button with accessible focus state and dark slate theme -->
	<div class="relative w-full">
		<div
			bind:this={googleButtonContainer}
			class={nativeRenderSucceeded ? 'flex justify-center' : 'hidden'}
		></div>

		{#if !nativeRenderSucceeded}
			<button
				type="button"
				onclick={triggerSignIn}
				disabled={isScriptLoading || isAuthenticating}
				aria-busy={isAuthenticating || isScriptLoading}
				aria-label={buttonText}
				class="group relative flex h-11 w-full cursor-pointer items-center justify-center gap-3 rounded-xl border border-slate-700 bg-slate-950 px-4 text-sm font-medium text-slate-200 shadow-sm transition-all duration-150 hover:border-slate-600 hover:bg-slate-900 hover:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-amber-500 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-900 disabled:cursor-not-allowed disabled:opacity-50"
			>
				{#if isAuthenticating || isScriptLoading}
					<Loader2 class="h-5 w-5 animate-spin text-amber-400" aria-hidden="true" />
					<span class="text-xs text-slate-400">{t('common.loading')}</span>
				{:else}
					<!-- Official Google 4-Color 'G' SVG -->
					<svg
						class="h-5 w-5 flex-shrink-0 transition-transform duration-150 group-hover:scale-105"
						viewBox="0 0 24 24"
						aria-hidden="true"
					>
						<path
							fill="#4285F4"
							d="M23.745 12.27c0-.7-.06-1.4-.19-2.07H12v4.51h6.6c-.29 1.52-1.14 2.82-2.4 3.68v3.05h3.88c2.27-2.09 3.665-5.17 3.665-9.17z"
						/>
						<path
							fill="#34A853"
							d="M12 24c3.24 0 5.95-1.08 7.93-2.91l-3.88-3.05c-1.08.72-2.45 1.16-4.05 1.16-3.12 0-5.77-2.1-6.72-4.93H1.25v3.15C3.26 21.36 7.35 24 12 24z"
						/>
						<path
							fill="#FBBC05"
							d="M5.28 14.27c-.25-.72-.38-1.49-.38-2.27s.13-1.55.38-2.27V6.58H1.25C.45 8.18 0 9.99 0 12s.45 3.82 1.25 5.42l4.03-3.15z"
						/>
						<path
							fill="#EA4335"
							d="M12 4.75c1.77 0 3.35.61 4.6 1.8l3.42-3.42C17.95 1.19 15.24 0 12 0 7.35 0 3.26 2.64 1.25 6.58l4.03 3.15c.95-2.83 3.6-4.98 6.72-4.98z"
						/>
					</svg>
					<span>{buttonText}</span>
				{/if}
			</button>
		{/if}
	</div>
{/if}
