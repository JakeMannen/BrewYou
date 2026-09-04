<script lang="ts">
  import { i18n, supportedLocales, type LocaleCode } from '$lib/i18n/index.svelte';
  import { auth } from '$lib/stores/auth.svelte';
  import { api } from '$lib/api/client';
  import { Globe } from '@lucide/svelte';

  let isOpen = $state(false);

  async function selectLocale(code: LocaleCode) {
    i18n.setLocale(code);
    isOpen = false;

    if (auth.isAuthenticated) {
      try {
        const updated = await api.auth.updateLanguage(code);
        if (auth.user) {
          auth.user.preferredLanguage = updated.preferredLanguage;
        }
      } catch (err) {
        console.warn('Could not save language preference to user profile:', err);
      }
    }
  }
</script>

<div class="relative">
  <button
    type="button"
    onclick={() => (isOpen = !isOpen)}
    class="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg bg-slate-800/80 hover:bg-slate-800 border border-slate-700/80 text-xs font-medium text-slate-200 transition-colors cursor-pointer"
    title="Change language / Byt språk"
  >
    <Globe class="w-3.5 h-3.5 text-amber-400" />
    <span class="uppercase tracking-wider font-bold">{i18n.locale}</span>
  </button>

  {#if isOpen}
    <!-- Backdrop to close on outside click -->
    <div
      tabindex="-1"
      role="button"
      aria-label="Close language selector"
      class="fixed inset-0 z-40"
      onclick={() => (isOpen = false)}
      onkeydown={(e) => { if (e.key === 'Escape') isOpen = false; }}
    ></div>

    <div class="absolute right-0 mt-2 w-36 py-1.5 bg-slate-900 border border-slate-800 rounded-xl shadow-2xl z-50 overflow-hidden">
      {#each supportedLocales as loc}
        <button
          type="button"
          onclick={() => selectLocale(loc.code)}
          class="w-full px-3 py-2 text-left text-xs flex items-center justify-between transition-colors {i18n.locale === loc.code ? 'bg-amber-500/15 text-amber-400 font-bold' : 'text-slate-300 hover:bg-slate-800/60 hover:text-white'}"
        >
          <span class="flex items-center gap-2">
            <span>{loc.flag}</span>
            <span>{loc.label}</span>
          </span>
          {#if i18n.locale === loc.code}
            <span class="w-1.5 h-1.5 rounded-full bg-amber-400"></span>
          {/if}
        </button>
      {/each}
    </div>
  {/if}
</div>
