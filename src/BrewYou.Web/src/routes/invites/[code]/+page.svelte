<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { t } from '$lib/i18n/index.svelte';
	import { api } from '$lib/api/client';
	import { auth } from '$lib/stores/auth.svelte';
	import { brewery } from '$lib/stores/brewery.svelte';
	import type { BreweryInviteDto } from '$lib/types/api';
	import {
		Users,
		Beer,
		Crown,
		Eye,
		CheckCircle2,
		AlertCircle,
		ArrowRight,
		LogIn
	} from '@lucide/svelte';

	let inviteCode = $derived(page.params.code);
	let invite = $state<BreweryInviteDto | null>(null);
	let isLoading = $state(true);
	let error = $state<string | null>(null);
	let isAccepting = $state(false);
	let acceptSuccess = $state(false);

	onMount(async () => {
		if (!inviteCode) {
			error = t('brewery.invite_invalid_or_expired');
			isLoading = false;
			return;
		}

		try {
			const res = await api.breweryCollaboration.validateInvite(inviteCode);
			invite = res;
		} catch (err) {
			console.error('Failed to validate invite:', err);
			error = t('brewery.invite_invalid_or_expired');
		} finally {
			isLoading = false;
		}
	});

	async function handleAccept() {
		if (!inviteCode || isAccepting) return;
		isAccepting = true;
		error = null;

		try {
			const setup = await api.breweryCollaboration.acceptInvite({ code: inviteCode });
			acceptSuccess = true;
			await brewery.syncFromBackend();
			brewery.selectSetup(setup.id);
			setTimeout(() => {
				goto('/inventory/equipment');
			}, 1500);
		} catch (err) {
			console.error('Failed to accept invite:', err);
			error = err instanceof Error ? err.message : t('brewery.err_accept_invite');
		} finally {
			isAccepting = false;
		}
	}
</script>

<svelte:head>
	<title>{t('brewery.accept_invite_title')} — BrewYou</title>
</svelte:head>

<div class="flex min-h-[80vh] items-center justify-center p-4">
	<div
		class="glass-panel w-full max-w-md rounded-3xl border border-zinc-200/80 bg-white p-8 text-center shadow-2xl dark:border-white/10 dark:bg-zinc-900"
	>
		{#if isLoading}
			<div class="space-y-4 py-12">
				<div
					class="mx-auto h-12 w-12 animate-spin rounded-full border-4 border-amber-500 border-t-transparent"
				></div>
				<p class="text-xs text-zinc-500">{t('brewery.loading_invitation')}</p>
			</div>
		{:else if error}
			<div class="space-y-4 py-6">
				<div
					class="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-red-500/10 text-red-500"
				>
					<AlertCircle class="h-8 w-8" />
				</div>
				<h2 class="text-base font-bold text-zinc-900 dark:text-white">
					{t('brewery.accept_invite_title')}
				</h2>
				<p class="text-xs text-zinc-600 dark:text-zinc-400">
					{error}
				</p>
				<div class="pt-4">
					<a
						href="/"
						class="inline-flex items-center gap-2 rounded-xl bg-zinc-100 px-4 py-2 text-xs font-semibold text-zinc-700 hover:bg-zinc-200 dark:bg-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-700"
					>
						{t('brewery.go_to_dashboard')}
					</a>
				</div>
			</div>
		{:else if acceptSuccess && invite}
			<div class="space-y-4 py-6">
				<div
					class="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-green-500/10 text-green-500"
				>
					<CheckCircle2 class="h-8 w-8" />
				</div>
				<h2 class="text-lg font-bold text-zinc-900 dark:text-white">
					{t('brewery.invite_accepted_success', { name: invite.setupName })}
				</h2>
				<p class="text-xs text-zinc-500">
					{t('brewery.redirecting_to_equipment')}
				</p>
			</div>
		{:else if invite}
			<div class="space-y-6">
				<!-- Icon header -->
				<div
					class="mx-auto flex h-16 w-16 items-center justify-center rounded-3xl bg-amber-500/10 text-amber-500 shadow-inner dark:bg-amber-500/20"
				>
					<Users class="h-8 w-8" />
				</div>

				<!-- Details -->
				<div>
					<h2 class="text-lg font-bold text-zinc-900 dark:text-white">
						{t('brewery.accept_invite_title')}
					</h2>
					<p class="mt-2 text-xs text-zinc-600 dark:text-zinc-400">
						{t('brewery.accept_invite_subtitle', {
							name: invite.setupName,
							role: invite.role
						})}
					</p>
				</div>

				<!-- Setup card preview -->
				<div
					class="rounded-2xl border border-zinc-200/80 bg-zinc-50/80 p-4 text-left dark:border-white/5 dark:bg-zinc-800/40"
				>
					<div class="flex items-center justify-between">
						<div>
							<h3 class="text-sm font-bold text-zinc-900 dark:text-white">
								{invite.setupName}
							</h3>
							{#if invite.invitedByName}
								<p class="text-[11px] text-zinc-500">
									{t('brewery.invited_by', { name: invite.invitedByName })}
								</p>
							{/if}
						</div>

						<!-- Role badge -->
						{#if invite.role === 'Owner'}
							<span
								class="inline-flex items-center gap-1 rounded-md bg-amber-500/10 px-2 py-0.5 text-xs font-semibold text-amber-600 dark:text-amber-400"
							>
								<Crown class="h-3.5 w-3.5" />
								{t('brewery.role_badge_owner')}
							</span>
						{:else if invite.role === 'Brewer'}
							<span
								class="inline-flex items-center gap-1 rounded-md bg-emerald-500/10 px-2 py-0.5 text-xs font-semibold text-emerald-600 dark:text-emerald-400"
							>
								<Beer class="h-3.5 w-3.5" />
								{t('brewery.role_badge_brewer')}
							</span>
						{:else}
							<span
								class="inline-flex items-center gap-1 rounded-md bg-zinc-500/10 px-2 py-0.5 text-xs font-semibold text-zinc-600 dark:text-zinc-400"
							>
								<Eye class="h-3.5 w-3.5" />
								{t('brewery.role_badge_viewer')}
							</span>
						{/if}
					</div>
				</div>

				<!-- Actions -->
				{#if auth.isAuthenticated}
					<button
						type="button"
						onclick={handleAccept}
						disabled={isAccepting}
						class="flex w-full items-center justify-center gap-2 rounded-2xl border border-amber-500/40 bg-gradient-to-r from-amber-500 to-amber-600 px-5 py-3 text-xs font-bold text-zinc-950 shadow-md transition hover:from-amber-400 hover:to-amber-500 active:scale-[0.98] disabled:opacity-50"
					>
						<span>{t('brewery.accept_invite_button')}</span>
						<ArrowRight class="h-4 w-4" />
					</button>
				{:else}
					<div class="space-y-3">
						<p class="text-xs text-zinc-500">
							{t('brewery.accept_invite_login_required')}
						</p>
						<a
							href="/login?returnUrl=/invites/{inviteCode}"
							class="flex w-full items-center justify-center gap-2 rounded-2xl border border-amber-500/40 bg-gradient-to-r from-amber-500 to-amber-600 px-5 py-3 text-xs font-bold text-zinc-950 shadow-md transition hover:from-amber-400 hover:to-amber-500 active:scale-[0.98]"
						>
							<LogIn class="h-4 w-4" />
							<span>{t('brewery.login_to_join')}</span>
						</a>
					</div>
				{/if}
			</div>
		{/if}
	</div>
</div>
