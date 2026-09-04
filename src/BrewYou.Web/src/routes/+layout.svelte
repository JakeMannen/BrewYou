<script lang="ts">
  import './layout.css';
  import { onMount } from 'svelte';
  import { auth } from '$lib/stores/auth.svelte';
  import { i18n, t } from '$lib/i18n/index.svelte';
  import LanguageSelector from '$lib/components/LanguageSelector.svelte';
  import {
    Beer,
    BookOpen,
    Layers,
    PlusCircle,
    LogOut,
    LogIn,
    UserPlus,
    User
  } from '@lucide/svelte';

  let { children } = $props();

  onMount(() => {
    i18n.init();
    auth.init();
  });
</script>

<svelte:head>
  <title>BrewYou — {t('home.tagline')}</title>
</svelte:head>

<div class="min-h-screen bg-slate-950 text-slate-100 flex flex-col font-sans selection:bg-amber-500 selection:text-black">
  <!-- Top Navigation Header -->
  <header class="border-b border-slate-800 bg-slate-900/80 backdrop-blur sticky top-0 z-50">
    <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-16 flex items-center justify-between">
      <!-- Logo -->
      <a href="/" class="flex items-center gap-2 text-amber-400 hover:text-amber-300 font-bold text-xl tracking-tight transition-colors">
        <div class="p-2 bg-amber-500/10 rounded-lg border border-amber-500/30">
          <Beer class="w-6 h-6 text-amber-400" />
        </div>
        <span>Brew<span class="text-white">You</span></span>
      </a>

      <!-- Primary Navigation Links -->
      <nav class="hidden md:flex items-center gap-1">
        <a
          href="/"
          class="px-3 py-2 rounded-md text-sm font-medium text-slate-300 hover:text-white hover:bg-slate-800/60 transition-colors"
        >
          {t('nav.dashboard')}
        </a>
        <a
          href="/recipes"
          class="px-3 py-2 rounded-md text-sm font-medium text-slate-300 hover:text-white hover:bg-slate-800/60 transition-colors flex items-center gap-1.5"
        >
          <BookOpen class="w-4 h-4" />
          {t('nav.recipes')}
        </a>
        <a
          href="/recipes/new"
          class="px-3 py-2 rounded-md text-sm font-medium text-slate-300 hover:text-white hover:bg-slate-800/60 transition-colors flex items-center gap-1.5"
        >
          <PlusCircle class="w-4 h-4 text-amber-400" />
          {t('nav.formulator')}
        </a>
        <a
          href="/ingredients"
          class="px-3 py-2 rounded-md text-sm font-medium text-slate-300 hover:text-white hover:bg-slate-800/60 transition-colors flex items-center gap-1.5"
        >
          <Layers class="w-4 h-4" />
          {t('nav.ingredients')}
        </a>
      </nav>

      <!-- Right Controls: Language Selector & Auth -->
      <div class="flex items-center gap-3">
        <LanguageSelector />

        {#if auth.isAuthenticated && auth.user}
          <div class="flex items-center gap-2">
            <div class="hidden sm:flex items-center gap-2 px-3 py-1.5 bg-slate-800/80 rounded-full border border-slate-700 text-xs">
              <User class="w-3.5 h-3.5 text-amber-400" />
              <span class="text-slate-200 font-medium">{auth.user.displayName}</span>
            </div>
            <button
              onclick={() => auth.logout()}
              class="p-2 rounded-lg text-slate-400 hover:text-red-400 hover:bg-slate-800/60 transition-colors cursor-pointer"
              title={t('nav.logout')}
            >
              <LogOut class="w-5 h-5" />
            </button>
          </div>
        {:else}
          <div class="flex items-center gap-2">
            <a
              href="/login"
              class="px-3 py-1.5 rounded-lg text-sm font-medium text-slate-300 hover:text-white hover:bg-slate-800 transition-colors flex items-center gap-1"
            >
              <LogIn class="w-4 h-4" />
              <span>{t('nav.login')}</span>
            </a>
            <a
              href="/register"
              class="px-3 py-1.5 rounded-lg text-sm font-medium bg-amber-500 hover:bg-amber-400 text-slate-950 font-semibold transition-colors flex items-center gap-1"
            >
              <UserPlus class="w-4 h-4" />
              <span>{t('nav.signup')}</span>
            </a>
          </div>
        {/if}
      </div>
    </div>
  </header>

  <!-- Main Body -->
  <main class="flex-1 max-w-7xl w-full mx-auto px-4 sm:px-6 lg:px-8 py-8">
    {@render children()}
  </main>

  <!-- Footer -->
  <footer class="border-t border-slate-900 bg-slate-950 py-6 text-center text-xs text-slate-500">
    <div class="max-w-7xl mx-auto px-4">
      BrewYou &mdash; Crafted with .NET 10, Aspire, Svelte 5, & PostgreSQL.
    </div>
  </footer>
</div>
