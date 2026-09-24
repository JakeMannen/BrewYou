<script lang="ts">
	import './layout.css';
	import { onMount } from 'svelte';
	import { browser } from '$app/environment';
	import { page } from '$app/state';
	import { auth } from '$lib/stores/auth.svelte';
	import { theme } from '$lib/stores/theme.svelte';
	import { i18n, t } from '$lib/i18n/index.svelte';
	import Sidebar from '$lib/components/navigation/Sidebar.svelte';
	import { isRouteActive } from '$lib/components/navigation/navUtils';
	import {
		Beer,
		BookOpen,
		Activity,
		Layers,
		LogOut,
		User,
		Menu,
		X,
		Wrench,
		Calculator,
		Settings
	} from '@lucide/svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import { brewery } from '$lib/stores/brewery.svelte';
	import BrewerySwitcher from '$lib/components/navigation/BrewerySwitcher.svelte';
	import AuthSplashCurtain from '$lib/components/auth/AuthSplashCurtain.svelte';
	import LoginModal from '$lib/components/auth/LoginModal.svelte';
	import BreweryOnboardingModal from '$lib/components/brewery/BreweryOnboardingModal.svelte';
	import { goto } from '$app/navigation';

	let { children } = $props();
	let sidebarCollapsed = $state(false);
	let mobileMenuOpen = $state(false);
	let isHydrated = $state(false);

	function handleAuthSuccess() {
		if (page.url.pathname === '/login' || page.url.pathname === '/register') {
			goto('/');
		}
	}

	function handleOnboardingComplete() {
		if (page.url.pathname === '/login' || page.url.pathname === '/register') {
			goto('/');
		}
	}

	onMount(() => {
		isHydrated = true;
		i18n.init();
		auth.init();
		theme.init();
		settings.init();
		brewery.init();

		if (browser) {
			const savedCollapsed = localStorage.getItem('brewyou-sidebar-collapsed');
			if (savedCollapsed !== null) {
				sidebarCollapsed = savedCollapsed === 'true';
			}
		}
	});

	$effect(() => {
		if (auth.user?.preferences) {
			settings.hydrateFromPreferences(auth.user.preferences);
		} else if (auth.user?.preferredVolumeUnit) {
			settings.setVolumeUnitFromAuth(auth.user.preferredVolumeUnit);
		}
	});

	$effect(() => {
		if (auth.status === 'initializing') {
			return;
		}
		if (auth.isAuthenticated) {
			brewery.syncFromBackend();
		} else {
			brewery.resetToDefault();
		}
	});

	// Automatically close mobile menu when route changes
	$effect(() => {
		void page.url.pathname;
		mobileMenuOpen = false;
	});

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && mobileMenuOpen) {
			mobileMenuOpen = false;
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

<svelte:head>
	<title>BrewYou — {t('home.tagline')}</title>
	<link rel="icon" href="/favicon.svg" type="image/svg+xml" />
	<link rel="apple-touch-icon" href="/favicon.svg" />
</svelte:head>

{#if !isHydrated || auth.status === 'initializing'}
	<AuthSplashCurtain />
{/if}

{#if isHydrated && auth.status === 'unauthenticated'}
	<LoginModal
		initialMode={page.url.pathname === '/register' ? 'signup' : 'signin'}
		onSuccess={handleAuthSuccess}
	/>
{/if}

{#if isHydrated && auth.isAuthenticated && auth.needsBreweryOnboarding}
	<BreweryOnboardingModal onComplete={handleOnboardingComplete} />
{/if}

<div
	data-hydrated={isHydrated}
	inert={!isHydrated || !auth.isAuthenticated || auth.needsBreweryOnboarding}
	aria-hidden={!isHydrated || !auth.isAuthenticated || auth.needsBreweryOnboarding}
	class="flex min-h-screen bg-[var(--canvas-bg)] font-sans text-[var(--text-primary)] transition-all duration-300 selection:bg-amber-500 selection:text-black {!auth.isAuthenticated ||
	auth.needsBreweryOnboarding
		? 'pointer-events-none opacity-40 blur-md select-none'
		: ''}"
>
	<!-- Desktop Collapsible SaaS Sidebar -->
	<Sidebar
		bind:collapsed={sidebarCollapsed}
		class="sticky top-0 z-30 hidden h-screen flex-shrink-0 md:flex"
	/>

	<!-- Main Content Area & Header Canvas -->
	<div class="flex min-w-0 flex-1 flex-col overflow-x-hidden">
		<!-- Top Sub-Header Bar -->
		<header
			class="glass-panel sticky top-0 z-20 border-b border-zinc-200/80 px-4 py-3 sm:px-6 lg:px-8 dark:border-white/[0.08]"
		>
			<div class="flex h-10 items-center justify-between">
				<!-- Mobile Brand & Title (Hidden on Desktop) -->
				<div class="flex items-center gap-2.5 md:hidden">
					<a href="/" class="flex items-center gap-2 text-base font-bold tracking-tight">
						<div
							class="flex h-8 w-8 items-center justify-center overflow-hidden rounded-lg border border-amber-500/30 bg-amber-500/10"
						>
							<img
								data-testid="mobile-logo-light"
								src="/logo-light.png"
								alt="BrewYou Logo"
								class="block h-full w-full object-contain p-0.5 dark:hidden"
							/>
							<img
								data-testid="mobile-logo-dark"
								src="/logo-dark.png"
								alt="BrewYou Logo"
								class="hidden h-full w-full object-contain p-0.5 dark:block"
							/>
						</div>
						<span class="text-zinc-900 dark:text-white"
							>Brew<span class="text-amber-500 dark:text-amber-400">You</span></span
						>
					</a>
				</div>

				<!-- Desktop Breadcrumbs & Context Title -->
				<div
					class="hidden items-center gap-2 text-xs font-medium text-zinc-500 md:flex dark:text-zinc-400"
				>
					<span class="text-zinc-800 dark:text-zinc-200">BrewYou</span>
					<span>/</span>
					{#if page.url.pathname === '/'}
						<span class="font-semibold text-amber-600 dark:text-amber-400"
							>{t('breadcrumbs.dashboard')}</span
						>
					{:else if page.url.pathname === '/recipes/new'}
						<a href="/recipes" class="hover:underline">{t('breadcrumbs.recipes')}</a>
						<span>/</span>
						<span class="font-semibold text-amber-600 dark:text-amber-400"
							>{t('breadcrumbs.new_formulator')}</span
						>
					{:else if page.url.pathname.startsWith('/recipes')}
						<span class="font-semibold text-amber-600 dark:text-amber-400"
							>{t('breadcrumbs.recipe_catalog')}</span
						>
					{:else if page.url.pathname.startsWith('/batches')}
						<span class="font-semibold text-amber-600 dark:text-amber-400"
							>{t('breadcrumbs.batches')}</span
						>
					{:else if page.url.pathname.startsWith('/ingredients')}
						<span class="font-semibold text-amber-600 dark:text-amber-400"
							>{t('breadcrumbs.ingredients')}</span
						>
					{:else if page.url.pathname.startsWith('/inventory/equipment')}
						<span class="text-zinc-600 dark:text-zinc-400">{t('breadcrumbs.inventory')}</span>
						<span>/</span>
						<span class="font-semibold text-amber-600 dark:text-amber-400"
							>{t('breadcrumbs.equipment')}</span
						>
					{:else if page.url.pathname.startsWith('/calculations')}
						<span class="font-semibold text-amber-600 dark:text-amber-400"
							>{t('breadcrumbs.calculations')}</span
						>
					{:else if page.url.pathname === '/login'}
						<span class="font-semibold text-amber-600 dark:text-amber-400"
							>{t('breadcrumbs.signin')}</span
						>
					{:else if page.url.pathname === '/register'}
						<span class="font-semibold text-amber-600 dark:text-amber-400"
							>{t('breadcrumbs.signup')}</span
						>
					{:else if page.url.pathname.startsWith('/settings')}
						<span class="font-semibold text-amber-600 dark:text-amber-400"
							>{t('settings.title')}</span
						>
					{:else}
						<span class="font-semibold text-amber-600 dark:text-amber-400"
							>{page.url.pathname.slice(1)}</span
						>
					{/if}
				</div>

				<!-- Header Right Controls -->
				<div class="flex items-center gap-2 sm:gap-3">
					<!-- Mobile Drawer Toggle Button -->
					<button
						type="button"
						onclick={() => (mobileMenuOpen = !mobileMenuOpen)}
						aria-controls="mobile-nav-drawer"
						aria-expanded={mobileMenuOpen}
						class="flex h-10 w-10 items-center justify-center rounded-xl border border-zinc-200/80 bg-zinc-100/80 text-zinc-700 transition-colors hover:bg-zinc-200 md:hidden dark:border-white/10 dark:bg-zinc-900/80 dark:text-zinc-200 dark:hover:bg-zinc-800"
						aria-label={mobileMenuOpen ? t('nav.close_menu') : t('nav.toggle_menu')}
					>
						{#if mobileMenuOpen}
							<X class="h-5 w-5" />
						{:else}
							<Menu class="h-5 w-5" />
						{/if}
					</button>
				</div>
			</div>
		</header>

		<!-- Main Page Canvas -->
		<main class="mx-auto w-full max-w-7xl flex-1 px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
			{@render children()}
		</main>
	</div>

	<!-- Mobile Slide-out Drawer Navigation -->
	{#if mobileMenuOpen}
		<!-- Backdrop -->
		<div
			tabindex="-1"
			role="button"
			aria-label={t('nav.close_menu')}
			class="fixed inset-0 z-50 bg-black/60 backdrop-blur-sm md:hidden"
			onclick={() => (mobileMenuOpen = false)}
			onkeydown={(e) => {
				if (e.key === 'Escape') mobileMenuOpen = false;
			}}
		></div>

		<!-- Drawer Panel -->
		<div
			id="mobile-nav-drawer"
			data-testid="mobile-nav-drawer"
			role="dialog"
			aria-modal="true"
			aria-label={t('nav.mobile_navigation')}
			class="glass-panel fixed inset-y-0 right-0 z-50 flex w-full max-w-xs flex-col justify-between border-l border-zinc-200/80 p-5 shadow-2xl md:hidden dark:border-white/10"
		>
			<!-- Drawer Header -->
			<div class="flex flex-col gap-5">
				<div class="flex items-center justify-between">
					<a
						href="/"
						onclick={() => (mobileMenuOpen = false)}
						class="flex items-center gap-2.5 text-base font-bold text-amber-500 dark:text-amber-400"
					>
						<div
							class="flex h-8 w-8 items-center justify-center overflow-hidden rounded-lg border border-amber-500/30 bg-amber-500/10"
						>
							<img
								data-testid="drawer-logo-light"
								src="/logo-light.png"
								alt="BrewYou Logo"
								class="block h-full w-full object-contain p-0.5 dark:hidden"
							/>
							<img
								data-testid="drawer-logo-dark"
								src="/logo-dark.png"
								alt="BrewYou Logo"
								class="hidden h-full w-full object-contain p-0.5 dark:block"
							/>
						</div>
						<span class="text-zinc-900 dark:text-white">BrewYou</span>
					</a>

					<button
						type="button"
						onclick={() => (mobileMenuOpen = false)}
						class="rounded-xl border border-zinc-200 p-2 text-zinc-500 hover:bg-zinc-100 dark:border-white/10 dark:text-zinc-400 dark:hover:bg-zinc-800"
						aria-label={t('nav.close_menu')}
					>
						<X class="h-5 w-5" />
					</button>
				</div>

				<!-- Brewery Switcher in Drawer -->
				<BrewerySwitcher onselect={() => (mobileMenuOpen = false)} />

				<!-- Navigation Links -->
				<nav class="flex flex-col gap-1.5" aria-label={t('nav.drawer_navigation')}>
					<!-- Dashboard -->
					<a
						href="/"
						onclick={() => (mobileMenuOpen = false)}
						aria-current={isRouteActive(page.url.pathname, '/') ? 'page' : undefined}
						class="flex min-h-[44px] items-center gap-3 rounded-xl px-3 text-sm font-medium transition-colors {isRouteActive(
							page.url.pathname,
							'/'
						)
							? 'border border-amber-500/30 bg-amber-500/10 font-bold text-amber-600 dark:text-amber-400'
							: 'text-zinc-700 hover:bg-zinc-100 dark:text-zinc-200 dark:hover:bg-zinc-800'}"
					>
						<Beer class="h-4 w-4 text-amber-500 dark:text-amber-400" />
						<span>{t('nav.dashboard')}</span>
					</a>

					<!-- Recipes -->
					<a
						href="/recipes"
						onclick={() => (mobileMenuOpen = false)}
						aria-current={isRouteActive(page.url.pathname, '/recipes') ? 'page' : undefined}
						class="flex min-h-[44px] items-center gap-3 rounded-xl px-3 text-sm font-medium transition-colors {isRouteActive(
							page.url.pathname,
							'/recipes'
						)
							? 'border border-amber-500/30 bg-amber-500/10 font-bold text-amber-600 dark:text-amber-400'
							: 'text-zinc-700 hover:bg-zinc-100 dark:text-zinc-200 dark:hover:bg-zinc-800'}"
					>
						<BookOpen class="h-4 w-4 text-amber-500 dark:text-amber-400" />
						<span>{t('nav.recipes')}</span>
					</a>

					<!-- Batches -->
					<a
						href="/batches"
						onclick={() => (mobileMenuOpen = false)}
						aria-current={isRouteActive(page.url.pathname, '/batches') ? 'page' : undefined}
						class="flex min-h-[44px] items-center gap-3 rounded-xl px-3 text-sm font-medium transition-colors {isRouteActive(
							page.url.pathname,
							'/batches'
						)
							? 'border border-amber-500/30 bg-amber-500/10 font-bold text-amber-600 dark:text-amber-400'
							: 'text-zinc-700 hover:bg-zinc-100 dark:text-zinc-200 dark:hover:bg-zinc-800'}"
					>
						<Activity class="h-4 w-4 text-amber-500 dark:text-amber-400" />
						<span>{t('nav.batches')}</span>
					</a>

					<!-- Ingredients -->
					<a
						href="/ingredients"
						onclick={() => (mobileMenuOpen = false)}
						aria-current={isRouteActive(page.url.pathname, '/ingredients') ? 'page' : undefined}
						class="flex min-h-[44px] items-center gap-3 rounded-xl px-3 text-sm font-medium transition-colors {isRouteActive(
							page.url.pathname,
							'/ingredients'
						)
							? 'border border-amber-500/30 bg-amber-500/10 font-bold text-amber-600 dark:text-amber-400'
							: 'text-zinc-700 hover:bg-zinc-100 dark:text-zinc-200 dark:hover:bg-zinc-800'}"
					>
						<Layers class="h-4 w-4 text-amber-500 dark:text-amber-400" />
						<span>{t('nav.ingredients')}</span>
					</a>

					<!-- Equipment -->
					<a
						href="/inventory/equipment"
						onclick={() => (mobileMenuOpen = false)}
						aria-current={isRouteActive(page.url.pathname, '/inventory/equipment')
							? 'page'
							: undefined}
						class="flex min-h-[44px] items-center gap-3 rounded-xl px-3 text-sm font-medium transition-colors {isRouteActive(
							page.url.pathname,
							'/inventory/equipment'
						)
							? 'border border-amber-500/30 bg-amber-500/10 font-bold text-amber-600 dark:text-amber-400'
							: 'text-zinc-700 hover:bg-zinc-100 dark:text-zinc-200 dark:hover:bg-zinc-800'}"
					>
						<Wrench class="h-4 w-4 text-amber-500 dark:text-amber-400" />
						<span>{t('nav.equipment')}</span>
					</a>

					<!-- Calculations -->
					<a
						href="/calculations"
						onclick={() => (mobileMenuOpen = false)}
						aria-current={isRouteActive(page.url.pathname, '/calculations') ? 'page' : undefined}
						class="flex min-h-[44px] items-center gap-3 rounded-xl px-3 text-sm font-medium transition-colors {isRouteActive(
							page.url.pathname,
							'/calculations'
						)
							? 'border border-amber-500/30 bg-amber-500/10 font-bold text-amber-600 dark:text-amber-400'
							: 'text-zinc-700 hover:bg-zinc-100 dark:text-zinc-200 dark:hover:bg-zinc-800'}"
					>
						<Calculator class="h-4 w-4 text-amber-500 dark:text-amber-400" />
						<span>{t('nav.calculations')}</span>
					</a>

					<!-- Settings -->
					<a
						href="/settings"
						onclick={() => (mobileMenuOpen = false)}
						aria-current={isRouteActive(page.url.pathname, '/settings') ? 'page' : undefined}
						class="flex min-h-[44px] items-center gap-3 rounded-xl px-3 text-sm font-medium transition-colors {isRouteActive(
							page.url.pathname,
							'/settings'
						)
							? 'border border-amber-500/30 bg-amber-500/10 font-bold text-amber-600 dark:text-amber-400'
							: 'text-zinc-700 hover:bg-zinc-100 dark:text-zinc-200 dark:hover:bg-zinc-800'}"
					>
						<Settings class="h-4 w-4 text-amber-500 dark:text-amber-400" />
						<span>{t('nav.settings')}</span>
					</a>
				</nav>
			</div>

			<!-- Drawer Footer (Auth) -->
			<div class="flex flex-col gap-3 border-t border-zinc-200/80 pt-4 dark:border-white/[0.08]">
				<!-- Auth / User Info -->
				{#if auth.isAuthenticated && auth.user}
					<div
						class="flex items-center gap-3 rounded-xl border border-zinc-200/80 bg-zinc-100/80 p-2.5 dark:border-white/5 dark:bg-zinc-900/60"
					>
						<div
							class="flex h-9 w-9 items-center justify-center rounded-lg border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
						>
							<User class="h-4 w-4" />
						</div>
						<div class="flex-1 overflow-hidden">
							<p class="truncate text-xs font-bold text-zinc-900 dark:text-white">
								{auth.user.displayName || auth.user.email}
							</p>
							<p class="truncate text-[10px] text-zinc-500 dark:text-zinc-400">{auth.user.email}</p>
						</div>
					</div>

					<button
						type="button"
						onclick={() => {
							auth.logout();
							mobileMenuOpen = false;
						}}
						class="flex min-h-[44px] w-full items-center justify-center gap-2 rounded-xl border border-red-500/30 bg-red-500/10 text-xs font-semibold text-red-500 transition-colors hover:bg-red-500/20"
					>
						<LogOut class="h-4 w-4" />
						<span>{t('nav.logout')}</span>
					</button>
				{/if}
			</div>
		</div>
	{/if}
</div>
