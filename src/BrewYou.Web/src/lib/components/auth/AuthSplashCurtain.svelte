<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { Loader2 } from '@lucide/svelte';
</script>

<div
	data-testid="auth-splash-curtain"
	class="fixed inset-0 z-50 flex flex-col items-center justify-center bg-[var(--canvas-bg)] transition-opacity duration-300"
	role="status"
	aria-live="polite"
	aria-label={t('common.loading')}
>
	<!-- Ambient Background Glow -->
	<div
		class="pointer-events-none absolute h-64 w-64 rounded-full bg-amber-500/10 blur-3xl dark:bg-amber-500/15"
		aria-hidden="true"
	></div>

	<div class="relative z-10 flex flex-col items-center gap-4 text-center">
		<!-- Brand Logo -->
		<div
			class="flex h-20 w-20 items-center justify-center overflow-hidden rounded-2xl border border-amber-500/30 bg-amber-500/10 shadow-[0_0_30px_rgba(245,158,11,0.25)]"
		>
			<img
				src="/logo-light.png"
				alt="BrewYou Logo"
				class="block h-full w-full object-contain p-2 dark:hidden"
			/>
			<img
				src="/logo-dark.png"
				alt="BrewYou Logo"
				class="hidden h-full w-full object-contain p-2 dark:block"
			/>
		</div>

		<!-- Brand Title -->
		<div class="space-y-1">
			<h1 class="text-xl font-bold tracking-tight text-zinc-900 dark:text-white">
				Brew<span class="text-amber-500 dark:text-amber-400">You</span>
			</h1>
			<p class="text-xs text-zinc-500 dark:text-zinc-400">
				{t('home.tagline')}
			</p>
		</div>

		<!-- Subtle Pulse Loader -->
		<div
			class="mt-2 flex items-center gap-2 text-xs font-medium text-amber-600 dark:text-amber-400"
		>
			<Loader2 class="h-4 w-4 animate-spin" />
			<span>{t('common.loading')}</span>
		</div>
	</div>
</div>
