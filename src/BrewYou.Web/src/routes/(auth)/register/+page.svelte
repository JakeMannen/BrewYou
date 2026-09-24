<script lang="ts">
	import { goto } from '$app/navigation';
	import { auth } from '$lib/stores/auth.svelte';
	import { t } from '$lib/i18n/index.svelte';
	import { UserPlus, AlertCircle } from '@lucide/svelte';
	import GoogleSignInButton from '$lib/components/auth/GoogleSignInButton.svelte';

	let email = $state('');
	let password = $state('');
	let displayName = $state('');
	let submitting = $state(false);

	async function handleSubmit(e: Event) {
		e.preventDefault();
		if (!email || !password) return;

		submitting = true;
		const success = await auth.register(email, password, displayName || email.split('@')[0]);
		submitting = false;

		if (success) {
			goto('/recipes/new');
		}
	}

	function handleOAuthSuccess() {
		goto('/recipes/new');
	}

	$effect(() => {
		if (auth.isAuthenticated) {
			goto('/');
		}
	});
</script>

<div class="mx-auto max-w-md py-6 sm:py-12">
	<div class="glass-panel space-y-6 rounded-2xl p-5 shadow-xl sm:p-8">
		<div class="space-y-3 text-center">
			<div class="mx-auto flex items-center justify-center">
				<img
					data-testid="register-logo-light"
					src="/logo-light.png"
					alt="BrewYou Logo"
					class="block h-28 w-28 object-contain drop-shadow-md dark:hidden"
				/>
				<img
					data-testid="register-logo-dark"
					src="/logo-dark.png"
					alt="BrewYou Logo"
					class="hidden h-28 w-28 object-contain drop-shadow-[0_0_25px_rgba(245,158,11,0.25)] dark:block"
				/>
			</div>
			<div class="space-y-1">
				<h1 class="text-2xl font-bold tracking-tight text-zinc-900 dark:text-white">
					{t('auth.register_title')}
				</h1>
				<p class="text-xs text-zinc-500 dark:text-zinc-400">{t('auth.register_subtitle')}</p>
			</div>
		</div>

		{#if auth.error}
			<div
				class="flex items-center gap-2 rounded-xl border border-red-800 bg-red-950/50 p-3.5 text-xs text-red-300"
			>
				<AlertCircle class="h-4 w-4 flex-shrink-0" />
				<span>{auth.error}</span>
			</div>
		{/if}

		<GoogleSignInButton mode="signup" onSuccess={handleOAuthSuccess} />

		<div class="relative flex items-center justify-center">
			<div class="w-full border-t border-zinc-200 dark:border-zinc-800"></div>
			<span
				class="absolute bg-[var(--panel-bg)] px-3 text-[11px] font-semibold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
			>
				{t('auth.or_divider')}
			</span>
		</div>

		<form onsubmit={handleSubmit} class="space-y-4">
			<div>
				<label
					for="displayName"
					class="mb-1.5 block text-xs font-medium text-zinc-700 dark:text-zinc-300"
					>{t('auth.display_name')}</label
				>
				<input
					id="displayName"
					type="text"
					autocomplete="name"
					bind:value={displayName}
					placeholder="e.g. Master Brewer"
					class="h-11 w-full rounded-xl border border-zinc-200 bg-white/70 px-3.5 py-2.5 text-base text-zinc-900 focus:border-amber-500/50 focus:outline-none sm:text-sm dark:border-zinc-800 dark:bg-zinc-950 dark:text-white"
				/>
			</div>

			<div>
				<label for="email" class="mb-1.5 block text-xs font-medium text-zinc-700 dark:text-zinc-300"
					>{t('auth.email')}</label
				>
				<input
					id="email"
					type="email"
					required
					autocomplete="email"
					autocapitalize="none"
					bind:value={email}
					placeholder="brewer@example.com"
					class="h-11 w-full rounded-xl border border-zinc-200 bg-white/70 px-3.5 py-2.5 text-base text-zinc-900 focus:border-amber-500/50 focus:outline-none sm:text-sm dark:border-zinc-800 dark:bg-zinc-950 dark:text-white"
				/>
			</div>

			<div>
				<label
					for="password"
					class="mb-1.5 block text-xs font-medium text-zinc-700 dark:text-zinc-300"
					>{t('auth.password')}</label
				>
				<input
					id="password"
					type="password"
					required
					minlength="6"
					autocomplete="new-password"
					autocapitalize="none"
					autocorrect="off"
					spellcheck="false"
					bind:value={password}
					placeholder="••••••••"
					class="h-11 w-full rounded-xl border border-zinc-200 bg-white/70 px-3.5 py-2.5 text-base text-zinc-900 focus:border-amber-500/50 focus:outline-none sm:text-sm dark:border-zinc-800 dark:bg-zinc-950 dark:text-white"
				/>
			</div>

			<button
				type="submit"
				disabled={submitting}
				class="flex min-h-[44px] w-full cursor-pointer items-center justify-center gap-2 rounded-xl bg-amber-500 py-3 text-sm font-bold text-zinc-950 shadow-lg shadow-amber-500/20 transition-all hover:bg-amber-400 disabled:opacity-50"
			>
				<UserPlus class="h-4 w-4" />
				<span>{submitting ? t('auth.submitting_signup') : t('auth.signup_btn')}</span>
			</button>
		</form>

		<div class="pt-2 text-center text-xs text-zinc-500 dark:text-zinc-400">
			{t('auth.have_account')}&nbsp;<a
				href="/login"
				class="font-semibold text-amber-600 hover:underline dark:text-amber-400"
				>{t('auth.signin_link')}</a
			>
		</div>
	</div>
</div>
