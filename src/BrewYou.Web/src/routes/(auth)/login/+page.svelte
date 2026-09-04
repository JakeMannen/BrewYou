<script lang="ts">
  import { goto } from '$app/navigation';
  import { auth } from '$lib/stores/auth.svelte';
  import { Beer, LogIn, AlertCircle } from '@lucide/svelte';

  let email = $state('');
  let password = $state('');
  let submitting = $state(false);

  async function handleSubmit(e: Event) {
    e.preventDefault();
    if (!email || !password) return;

    submitting = true;
    const success = await auth.login(email, password);
    submitting = false;

    if (success) {
      goto('/recipes/new');
    }
  }
</script>

<div class="max-w-md mx-auto py-12">
  <div class="p-8 bg-slate-900/80 rounded-2xl border border-slate-800 shadow-xl space-y-6">
    <div class="text-center space-y-2">
      <div class="w-12 h-12 rounded-xl bg-amber-500/10 border border-amber-500/30 mx-auto flex items-center justify-center text-amber-400">
        <Beer class="w-6 h-6" />
      </div>
      <h1 class="text-2xl font-bold text-white">Sign In to BrewYou</h1>
      <p class="text-xs text-slate-400">Access your saved formulations and brewing history</p>
    </div>

    {#if auth.error}
      <div class="p-3.5 rounded-xl bg-red-950/50 border border-red-800 text-red-300 text-xs flex items-center gap-2">
        <AlertCircle class="w-4 h-4 flex-shrink-0" />
        <span>{auth.error}</span>
      </div>
    {/if}

    <form onsubmit={handleSubmit} class="space-y-4">
      <div>
        <label for="email" class="block text-xs font-medium text-slate-300 mb-1.5">Email Address</label>
        <input
          id="email"
          type="email"
          required
          bind:value={email}
          placeholder="brewer@example.com"
          class="w-full px-3.5 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/50"
        />
      </div>

      <div>
        <label for="password" class="block text-xs font-medium text-slate-300 mb-1.5">Password</label>
        <input
          id="password"
          type="password"
          required
          bind:value={password}
          placeholder="••••••••"
          class="w-full px-3.5 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/50"
        />
      </div>

      <button
        type="submit"
        disabled={submitting}
        class="w-full py-2.5 rounded-xl bg-amber-500 hover:bg-amber-400 text-slate-950 font-bold text-sm flex items-center justify-center gap-2 shadow-lg shadow-amber-500/20 transition-all disabled:opacity-50 cursor-pointer"
      >
        <LogIn class="w-4 h-4" />
        <span>{submitting ? 'Signing In...' : 'Sign In'}</span>
      </button>
    </form>

    <div class="text-center text-xs text-slate-500 pt-2">
      Don't have an account?{' '}
      <a href="/register" class="text-amber-400 hover:underline font-medium">Create one now</a>
    </div>
  </div>
</div>
