<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { i18n, t, type LocaleCode } from '$lib/i18n/index.svelte';
	import { LogIn, UserPlus, AlertCircle, Globe } from '@lucide/svelte';
	import GoogleSignInButton from '$lib/components/auth/GoogleSignInButton.svelte';

	interface Props {
		initialMode?: 'signin' | 'signup';
		onSuccess?: () => void;
	}

	let { initialMode = 'signin', onSuccess }: Props = $props();

	type AuthMode = 'signin' | 'signup';
	let modeOverride = $state<AuthMode | null>(null);
	let mode = $derived<AuthMode>(modeOverride ?? initialMode);

	// Form fields
	let email = $state('');
	let password = $state('');
	let confirmPassword = $state('');
	let displayName = $state('');
	let localError = $state<string | null>(null);
	let submitting = $state(false);

	let modalElement = $state<HTMLDivElement | null>(null);

	function switchMode(newMode: AuthMode) {
		modeOverride = newMode;
		auth.clearError();
		localError = null;
	}

	async function handleSubmit(e: Event) {
		e.preventDefault();
		localError = null;

		if (!email.trim() || !password) {
			return;
		}

		if (mode === 'signup') {
			if (password !== confirmPassword) {
				localError = t('auth.passwords_mismatch');
				return;
			}
		}

		submitting = true;
		try {
			let success = false;
			if (mode === 'signin') {
				success = await auth.login(email.trim(), password);
			} else {
				const name = displayName.trim() || email.split('@')[0];
				success = await auth.register(email.trim(), password, name, i18n.locale);
			}

			if (success && onSuccess) {
				onSuccess();
			}
		} finally {
			submitting = false;
		}
	}

	function handleOAuthSuccess() {
		auth.clearError();
		localError = null;
		if (onSuccess) {
			onSuccess();
		}
	}

	function toggleLanguage() {
		const nextLocale: LocaleCode = i18n.locale === 'en' ? 'sv' : 'en';
		i18n.setLocale(nextLocale);
	}

	// Focus trapping & blocking Escape dismissal
	function handleKeydown(e: KeyboardEvent) {
		// Strictly block Escape dismissal because there is no anonymous state
		if (e.key === 'Escape') {
			e.preventDefault();
			e.stopPropagation();
			return;
		}

		if (e.key === 'Tab' && modalElement) {
			const focusable = modalElement.querySelectorAll<HTMLElement>(
				'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
			);
			if (focusable.length === 0) return;

			const first = focusable[0];
			const last = focusable[focusable.length - 1];

			if (e.shiftKey) {
				if (document.activeElement === first) {
					e.preventDefault();
					last.focus();
				}
			} else {
				if (document.activeElement === last) {
					e.preventDefault();
					first.focus();
				}
			}
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

<!-- Fullscreen backdrop (non-dismissible) -->
<div
	data-testid="auth-modal-backdrop"
	class="fixed inset-0 z-50 flex items-center justify-center bg-black/65 p-4 backdrop-blur-xl transition-all select-none"
	role="presentation"
>
	<!-- Modal Dialog Card -->
	<div
		bind:this={modalElement}
		data-testid="auth-modal"
		role="dialog"
		aria-modal="true"
		aria-labelledby="auth-modal-title"
		aria-describedby="auth-modal-desc"
		class="glass-panel relative w-full max-w-md space-y-5 rounded-3xl border border-zinc-200/80 p-5 shadow-2xl sm:p-7 dark:border-white/10 dark:bg-zinc-950/90 dark:shadow-[0_0_50px_rgba(245,158,11,0.15)]"
	>
		<!-- Header Controls: Language Toggle -->
		<div class="flex items-center justify-between">
			<div class="flex items-center gap-2">
				<div
					class="flex h-7 w-7 items-center justify-center overflow-hidden rounded-lg border border-amber-500/30 bg-amber-500/10"
				>
					<img
						src="/logo-light.png"
						alt="BrewYou Logo"
						class="block h-full w-full object-contain p-0.5 dark:hidden"
					/>
					<img
						src="/logo-dark.png"
						alt="BrewYou Logo"
						class="hidden h-full w-full object-contain p-0.5 dark:block"
					/>
				</div>
				<span class="text-xs font-bold tracking-tight text-zinc-900 dark:text-white">
					Brew<span class="text-amber-500 dark:text-amber-400">You</span>
				</span>
			</div>

			<!-- Language Switcher -->
			<button
				type="button"
				onclick={toggleLanguage}
				data-testid="auth-lang-switcher"
				class="inline-flex items-center gap-1.5 rounded-lg border border-zinc-200/80 bg-zinc-100/80 px-2.5 py-1 text-xs font-semibold text-zinc-700 transition-colors hover:bg-zinc-200 dark:border-white/10 dark:bg-zinc-900/80 dark:text-zinc-300 dark:hover:bg-zinc-800"
				title={i18n.locale === 'en' ? 'Växla till Svenska' : 'Switch to English'}
			>
				<Globe class="h-3.5 w-3.5 text-amber-500 dark:text-amber-400" />
				<span class="uppercase">{i18n.locale}</span>
			</button>
		</div>

		<!-- Title and Subtitle -->
		<div class="space-y-1 text-center">
			<h2
				id="auth-modal-title"
				class="text-xl font-bold tracking-tight text-zinc-900 sm:text-2xl dark:text-white"
			>
				{mode === 'signin' ? t('auth.login_title') : t('auth.register_title')}
			</h2>
			<p id="auth-modal-desc" class="text-xs text-zinc-500 dark:text-zinc-400">
				{mode === 'signin' ? t('auth.login_subtitle') : t('auth.register_subtitle')}
			</p>
		</div>

		<!-- Segmented Mode Control (Tabs) -->
		<div
			role="tablist"
			aria-label="Authentication Mode"
			class="flex rounded-xl border border-zinc-200/80 bg-zinc-100/90 p-1 dark:border-white/5 dark:bg-zinc-900/90"
		>
			<button
				type="button"
				role="tab"
				id="tab-signin"
				aria-selected={mode === 'signin'}
				aria-controls="auth-form-panel"
				onclick={() => switchMode('signin')}
				class="flex-1 rounded-lg py-2 text-xs font-semibold transition-all {mode === 'signin'
					? 'bg-white text-zinc-950 shadow-xs dark:bg-zinc-800 dark:text-white'
					: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-200'}"
			>
				{t('auth.tab_signin')}
			</button>
			<button
				type="button"
				role="tab"
				id="tab-signup"
				aria-selected={mode === 'signup'}
				aria-controls="auth-form-panel"
				onclick={() => switchMode('signup')}
				class="flex-1 rounded-lg py-2 text-xs font-semibold transition-all {mode === 'signup'
					? 'bg-white text-zinc-950 shadow-xs dark:bg-zinc-800 dark:text-white'
					: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-200'}"
			>
				{t('auth.tab_signup')}
			</button>
		</div>

		<!-- Error Alert Banner -->
		{#if localError || auth.error}
			<div
				data-testid="auth-error-alert"
				role="alert"
				class="flex items-center gap-2 rounded-xl border border-red-500/30 bg-red-500/10 p-3 text-xs text-red-600 dark:border-red-500/20 dark:text-red-400"
			>
				<AlertCircle class="h-4 w-4 flex-shrink-0" />
				<span>{localError || auth.error}</span>
			</div>
		{/if}

		<!-- Google OAuth Action -->
		<div class="space-y-3">
			<GoogleSignInButton
				mode={mode === 'signup' ? 'signup' : 'signin'}
				onSuccess={handleOAuthSuccess}
			/>

			<div class="relative flex items-center justify-center">
				<div class="w-full border-t border-zinc-200/80 dark:border-zinc-800"></div>
				<span
					class="absolute bg-white px-3 text-[10px] font-semibold tracking-wider text-zinc-400 uppercase dark:bg-zinc-950 dark:text-zinc-500"
				>
					{t('auth.or_divider')}
				</span>
			</div>
		</div>

		<!-- Primary Form -->
		<form id="auth-form-panel" onsubmit={handleSubmit} class="space-y-3.5">
			{#if mode === 'signup'}
				<div>
					<label
						for="auth-display-name"
						class="mb-1 block text-xs font-medium text-zinc-700 dark:text-zinc-300"
					>
						{t('auth.display_name')}
					</label>
					<input
						id="auth-display-name"
						type="text"
						autocomplete="name"
						bind:value={displayName}
						placeholder="e.g. Master Brewer"
						class="h-10 w-full rounded-xl border border-zinc-200/90 bg-white/80 px-3 py-2 text-sm text-zinc-900 transition-colors focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900/90 dark:text-white"
					/>
				</div>
			{/if}

			<div>
				<label
					for="auth-email"
					class="mb-1 block text-xs font-medium text-zinc-700 dark:text-zinc-300"
				>
					{t('auth.email')}
				</label>
				<input
					id="auth-email"
					type="email"
					required
					autocomplete="email"
					autocapitalize="none"
					bind:value={email}
					placeholder="brewer@example.com"
					class="h-10 w-full rounded-xl border border-zinc-200/90 bg-white/80 px-3 py-2 text-sm text-zinc-900 transition-colors focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900/90 dark:text-white"
				/>
			</div>

			<div>
				<label
					for="auth-password"
					class="mb-1 block text-xs font-medium text-zinc-700 dark:text-zinc-300"
				>
					{t('auth.password')}
				</label>
				<input
					id="auth-password"
					type="password"
					required
					minlength="6"
					autocomplete={mode === 'signin' ? 'current-password' : 'new-password'}
					bind:value={password}
					placeholder="••••••••"
					class="h-10 w-full rounded-xl border border-zinc-200/90 bg-white/80 px-3 py-2 text-sm text-zinc-900 transition-colors focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900/90 dark:text-white"
				/>
			</div>

			{#if mode === 'signup'}
				<div>
					<label
						for="auth-confirm-password"
						class="mb-1 block text-xs font-medium text-zinc-700 dark:text-zinc-300"
					>
						{t('auth.confirm_password')}
					</label>
					<input
						id="auth-confirm-password"
						type="password"
						required
						minlength="6"
						autocomplete="new-password"
						bind:value={confirmPassword}
						placeholder="••••••••"
						class="h-10 w-full rounded-xl border border-zinc-200/90 bg-white/80 px-3 py-2 text-sm text-zinc-900 transition-colors focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900/90 dark:text-white"
					/>
				</div>
			{/if}

			<button
				type="submit"
				disabled={submitting}
				data-testid="auth-submit-btn"
				class="flex h-11 w-full cursor-pointer items-center justify-center gap-2 rounded-xl bg-amber-500 py-2.5 text-sm font-bold text-zinc-950 shadow-lg shadow-amber-500/20 transition-all hover:bg-amber-400 active:scale-[0.98] disabled:opacity-50"
			>
				{#if mode === 'signin'}
					<LogIn class="h-4 w-4" />
					<span>{submitting ? t('auth.submitting_signin') : t('auth.signin_btn')}</span>
				{:else}
					<UserPlus class="h-4 w-4" />
					<span>{submitting ? t('auth.submitting_signup') : t('auth.signup_btn')}</span>
				{/if}
			</button>
		</form>

		<!-- Subtext Switcher Link -->
		<div class="pt-1 text-center text-xs text-zinc-500 dark:text-zinc-400">
			{#if mode === 'signin'}
				{t('auth.no_account')}&nbsp;
				<button
					type="button"
					onclick={() => switchMode('signup')}
					class="font-semibold text-amber-600 hover:underline dark:text-amber-400"
				>
					{t('auth.create_account_link')}
				</button>
			{:else}
				{t('auth.have_account')}&nbsp;
				<button
					type="button"
					onclick={() => switchMode('signin')}
					class="font-semibold text-amber-600 hover:underline dark:text-amber-400"
				>
					{t('auth.signin_link')}
				</button>
			{/if}
		</div>
	</div>
</div>
