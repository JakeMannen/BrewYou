<script lang="ts">
	import { browser } from '$app/environment';
	import { page } from '$app/state';
	import { auth } from '$lib/stores/auth.svelte';
	import { t } from '$lib/i18n/index.svelte';
	import { isRouteActive } from '$lib/components/navigation/navUtils';
	import BrewerySwitcher from './BrewerySwitcher.svelte';
	import {
		Beer,
		BookOpen,
		Activity,
		Layers,
		Wrench,
		Calculator,
		PanelLeftClose,
		PanelLeftOpen,
		Settings
	} from '@lucide/svelte';

	interface Props {
		collapsed?: boolean;
		onNavigate?: () => void;
		class?: string;
	}

	let { collapsed = $bindable(false), onNavigate, class: className = '' }: Props = $props();

	function toggleCollapse() {
		collapsed = !collapsed;
		if (browser) {
			localStorage.setItem('brewyou-sidebar-collapsed', String(collapsed));
		}
	}

	function handleLinkClick() {
		if (onNavigate) {
			onNavigate();
		}
	}
</script>

<svelte:window
	onkeydown={(e) => {
		if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'b') {
			e.preventDefault();
			toggleCollapse();
		}
	}}
/>

<aside
	id="brewery-sidebar"
	aria-label="Brewery Sidebar"
	class="glass-panel relative flex flex-col justify-between border-r border-zinc-200/80 transition-all duration-300 ease-out select-none dark:border-white/[0.08] {collapsed
		? 'w-18'
		: 'w-64'} {className}"
>
	<!-- Top Section: Logo, Workspace Switcher & Primary CTA -->
	<div class="flex flex-col gap-3 p-3 sm:p-4">
		<!-- Brand & Logo -->
		<div class="flex items-center {collapsed ? 'justify-center' : 'justify-between'}">
			<a
				href="/"
				onclick={handleLinkClick}
				class="group flex items-center gap-2.5 transition-colors"
				title="BrewYou — {t('nav.home')}"
			>
				<div
					class="flex h-9 w-9 items-center justify-center overflow-hidden rounded-xl border border-amber-500/30 bg-amber-500/10 shadow-[0_0_15px_rgba(245,158,11,0.2)] transition-all duration-200 group-hover:border-amber-400 group-hover:bg-amber-500/20"
				>
					<img
						data-testid="sidebar-logo-light"
						src="/logo-light.png"
						alt="BrewYou Logo"
						class="block h-full w-full object-contain p-0.5 dark:hidden"
					/>
					<img
						data-testid="sidebar-logo-dark"
						src="/logo-dark.png"
						alt="BrewYou Logo"
						class="hidden h-full w-full object-contain p-0.5 dark:block"
					/>
				</div>

				{#if !collapsed}
					<span class="text-base font-bold tracking-tight text-zinc-900 dark:text-white">
						Brew<span class="text-amber-500 dark:text-amber-400">You</span>
					</span>
				{/if}
			</a>
		</div>

		<!-- Workspace / Brewery Switcher -->
		<div class={collapsed ? 'flex justify-center' : 'w-full'}>
			<BrewerySwitcher {collapsed} onselect={handleLinkClick} />
		</div>

		<!-- Navigation Links -->
		<nav class="mt-2 flex flex-col gap-4" aria-label="Main Navigation">
			<!-- Group: Brewing -->
			<div class="flex flex-col gap-1">
				{#if !collapsed}
					<span
						class="px-2 text-[10px] font-bold tracking-wider text-zinc-400 uppercase dark:text-zinc-500"
					>
						{t('nav.brewing_section')}
					</span>
				{/if}

				<!-- Dashboard -->
				<a
					href="/"
					onclick={handleLinkClick}
					aria-current={isRouteActive(page.url.pathname, '/') ? 'page' : undefined}
					class="flex items-center rounded-xl transition-all duration-150 {collapsed
						? 'h-10 w-10 justify-center self-center'
						: 'h-10 gap-3 px-3 text-sm'} {isRouteActive(page.url.pathname, '/')
						? 'border border-amber-500/30 bg-amber-500/10 font-semibold text-amber-600 shadow-[inset_0_0_12px_rgba(245,158,11,0.1)] dark:text-amber-400'
						: 'text-zinc-600 hover:bg-zinc-200/60 hover:text-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800/60 dark:hover:text-white'}"
					title={t('nav.dashboard')}
				>
					<Beer
						class="h-4 w-4 flex-shrink-0 {isRouteActive(page.url.pathname, '/')
							? 'text-amber-500 dark:text-amber-400'
							: ''}"
					/>
					{#if !collapsed}
						<span class="truncate">{t('nav.dashboard')}</span>
					{/if}
				</a>

				<!-- Recipes -->
				<a
					href="/recipes"
					onclick={handleLinkClick}
					aria-current={isRouteActive(page.url.pathname, '/recipes') ? 'page' : undefined}
					class="flex items-center rounded-xl transition-all duration-150 {collapsed
						? 'h-10 w-10 justify-center self-center'
						: 'h-10 gap-3 px-3 text-sm'} {isRouteActive(page.url.pathname, '/recipes')
						? 'border border-amber-500/30 bg-amber-500/10 font-semibold text-amber-600 shadow-[inset_0_0_12px_rgba(245,158,11,0.1)] dark:text-amber-400'
						: 'text-zinc-600 hover:bg-zinc-200/60 hover:text-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800/60 dark:hover:text-white'}"
					title={t('nav.recipes')}
				>
					<BookOpen
						class="h-4 w-4 flex-shrink-0 {isRouteActive(page.url.pathname, '/recipes')
							? 'text-amber-500 dark:text-amber-400'
							: ''}"
					/>
					{#if !collapsed}
						<span class="truncate">{t('nav.recipes')}</span>
					{/if}
				</a>

				<!-- Batches -->
				<a
					href="/batches"
					onclick={handleLinkClick}
					aria-current={isRouteActive(page.url.pathname, '/batches') ? 'page' : undefined}
					class="flex items-center rounded-xl transition-all duration-150 {collapsed
						? 'h-10 w-10 justify-center self-center'
						: 'h-10 gap-3 px-3 text-sm'} {isRouteActive(page.url.pathname, '/batches')
						? 'border border-amber-500/30 bg-amber-500/10 font-semibold text-amber-600 shadow-[inset_0_0_12px_rgba(245,158,11,0.1)] dark:text-amber-400'
						: 'text-zinc-600 hover:bg-zinc-200/60 hover:text-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800/60 dark:hover:text-white'}"
					title={t('nav.batches')}
				>
					<Activity
						class="h-4 w-4 flex-shrink-0 {isRouteActive(page.url.pathname, '/batches')
							? 'text-amber-500 dark:text-amber-400'
							: ''}"
					/>
					{#if !collapsed}
						<span class="truncate">{t('nav.batches')}</span>
					{/if}
				</a>
			</div>

			<!-- Group: Inventory -->
			<div class="flex flex-col gap-1">
				{#if !collapsed}
					<span
						class="px-2 text-[10px] font-bold tracking-wider text-zinc-400 uppercase dark:text-zinc-500"
					>
						{t('nav.inventory_section')}
					</span>
				{/if}

				<!-- Ingredients -->
				<a
					href="/ingredients"
					onclick={handleLinkClick}
					aria-current={isRouteActive(page.url.pathname, '/ingredients') ? 'page' : undefined}
					class="flex items-center rounded-xl transition-all duration-150 {collapsed
						? 'h-10 w-10 justify-center self-center'
						: 'h-10 gap-3 px-3 text-sm'} {isRouteActive(page.url.pathname, '/ingredients')
						? 'border border-amber-500/30 bg-amber-500/10 font-semibold text-amber-600 shadow-[inset_0_0_12px_rgba(245,158,11,0.1)] dark:text-amber-400'
						: 'text-zinc-600 hover:bg-zinc-200/60 hover:text-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800/60 dark:hover:text-white'}"
					title={t('nav.ingredients')}
				>
					<Layers
						class="h-4 w-4 flex-shrink-0 {isRouteActive(page.url.pathname, '/ingredients')
							? 'text-amber-500 dark:text-amber-400'
							: ''}"
					/>
					{#if !collapsed}
						<span class="truncate">{t('nav.ingredients')}</span>
					{/if}
				</a>

				<!-- Equipment -->
				<a
					href="/inventory/equipment"
					onclick={handleLinkClick}
					aria-current={isRouteActive(page.url.pathname, '/inventory/equipment')
						? 'page'
						: undefined}
					class="flex items-center rounded-xl transition-all duration-150 {collapsed
						? 'h-10 w-10 justify-center self-center'
						: 'h-10 gap-3 px-3 text-sm'} {isRouteActive(page.url.pathname, '/inventory/equipment')
						? 'border border-amber-500/30 bg-amber-500/10 font-semibold text-amber-600 shadow-[inset_0_0_12px_rgba(245,158,11,0.1)] dark:text-amber-400'
						: 'text-zinc-600 hover:bg-zinc-200/60 hover:text-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800/60 dark:hover:text-white'}"
					title={t('nav.equipment')}
				>
					<Wrench
						class="h-4 w-4 flex-shrink-0 {isRouteActive(page.url.pathname, '/inventory/equipment')
							? 'text-amber-500 dark:text-amber-400'
							: ''}"
					/>
					{#if !collapsed}
						<span class="truncate">{t('nav.equipment')}</span>
					{/if}
				</a>
			</div>

			<!-- Group: Calculations -->
			<div class="flex flex-col gap-1">
				{#if !collapsed}
					<span
						class="px-2 text-[10px] font-bold tracking-wider text-zinc-400 uppercase dark:text-zinc-500"
					>
						{t('nav.calculations_section')}
					</span>
				{/if}

				<!-- Calculations -->
				<a
					href="/calculations"
					onclick={handleLinkClick}
					aria-current={isRouteActive(page.url.pathname, '/calculations') ? 'page' : undefined}
					class="flex items-center rounded-xl transition-all duration-150 {collapsed
						? 'h-10 w-10 justify-center self-center'
						: 'h-10 gap-3 px-3 text-sm'} {isRouteActive(page.url.pathname, '/calculations')
						? 'border border-amber-500/30 bg-amber-500/10 font-semibold text-amber-600 shadow-[inset_0_0_12px_rgba(245,158,11,0.1)] dark:text-amber-400'
						: 'text-zinc-600 hover:bg-zinc-200/60 hover:text-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800/60 dark:hover:text-white'}"
					title={t('nav.calculations')}
				>
					<Calculator
						class="h-4 w-4 flex-shrink-0 {isRouteActive(page.url.pathname, '/calculations')
							? 'text-amber-500 dark:text-amber-400'
							: ''}"
					/>
					{#if !collapsed}
						<span class="truncate">{t('nav.calculations')}</span>
					{/if}
				</a>
			</div>
		</nav>
	</div>

	<!-- Bottom Section: Settings & Collapse Toggle -->
	<div class="flex flex-col gap-2 border-t border-zinc-200/80 p-3 sm:p-4 dark:border-white/[0.08]">
		<!-- Settings link -->
		{#if auth.isAuthenticated && auth.user}
			<a
				href="/settings"
				onclick={handleLinkClick}
				aria-current={isRouteActive(page.url.pathname, '/settings') ? 'page' : undefined}
				class="flex items-center rounded-xl transition-all duration-150 {collapsed
					? 'h-10 w-10 justify-center self-center'
					: 'h-10 gap-3 px-3 text-sm'} {isRouteActive(page.url.pathname, '/settings')
					? 'border border-amber-500/30 bg-amber-500/10 font-semibold text-amber-600 shadow-[inset_0_0_12px_rgba(245,158,11,0.1)] dark:text-amber-400'
					: 'text-zinc-600 hover:bg-zinc-200/60 hover:text-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800/60 dark:hover:text-white'}"
				title={t('settings.title')}
				aria-label={t('settings.title')}
				data-testid="sidebar-settings-link"
			>
				<Settings
					class="h-4 w-4 flex-shrink-0 {isRouteActive(page.url.pathname, '/settings')
						? 'text-amber-500 dark:text-amber-400'
						: ''}"
				/>
				{#if !collapsed}
					<span class="truncate">{t('settings.title')}</span>
				{/if}
			</a>
		{/if}

		<!-- Desktop Collapse Toggle Button -->
		<button
			type="button"
			onclick={toggleCollapse}
			aria-expanded={!collapsed}
			aria-controls="brewery-sidebar"
			class="mt-1 hidden items-center rounded-xl border border-zinc-200/60 bg-zinc-100/50 text-zinc-500 transition-colors hover:bg-zinc-200 hover:text-zinc-900 md:flex dark:border-white/5 dark:bg-zinc-900/40 dark:text-zinc-400 dark:hover:bg-zinc-800 dark:hover:text-white {collapsed
				? 'h-8 w-8 justify-center self-center'
				: 'h-8 justify-between px-2.5 text-xs'}"
			title={collapsed ? t('nav.expand_sidebar') : t('nav.collapse_sidebar')}
		>
			{#if collapsed}
				<PanelLeftOpen class="h-4 w-4" />
			{:else}
				<span class="text-[11px] font-medium">{t('nav.collapse')}</span>
				<div class="flex items-center gap-1.5">
					<kbd
						class="rounded border border-zinc-300 bg-zinc-200 px-1 py-0.5 font-mono text-[9px] dark:border-zinc-700 dark:bg-zinc-800"
					>
						Ctrl+B
					</kbd>
					<PanelLeftClose class="h-3.5 w-3.5" />
				</div>
			{/if}
		</button>
	</div>
</aside>
