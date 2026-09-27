<script lang="ts">
	import { onMount } from 'svelte';
	import { t } from '$lib/i18n/index.svelte';
	import { api } from '$lib/api/client';
	import { brewery } from '$lib/stores/brewery.svelte';
	import type { BrewerySetup } from '$lib/types/brewery';
	import type { BreweryMemberDto, BreweryInviteDto, BreweryRole } from '$lib/types/api';
	import {
		Users,
		UserPlus,
		Crown,
		Beer,
		Eye,
		Copy,
		Check,
		Trash2,
		X,
		AlertCircle,
		LogOut,
		Clock
	} from '@lucide/svelte';
	import { portal } from '$lib/utils/portal';
	import { auth } from '$lib/stores/auth.svelte';

	let {
		setup,
		onclose
	}: {
		setup: BrewerySetup;
		onclose: () => void;
	} = $props();

	let members = $state<BreweryMemberDto[]>([]);
	let pendingInvites = $state<BreweryInviteDto[]>([]);
	let isLoading = $state(true);
	let error = $state<string | null>(null);

	// Invite form state
	let inviteEmail = $state('');
	let inviteRole = $state<BreweryRole>('Brewer');
	let isSendingInvite = $state(false);
	let inviteError = $state<string | null>(null);
	let lastCreatedInvite = $state<BreweryInviteDto | null>(null);
	let copiedInviteId = $state<string | null>(null);

	// Action confirmation states
	let confirmRemoveUserId = $state<string | null>(null);
	let showLeaveConfirm = $state(false);
	let isProcessingAction = $state(false);

	const isOwner = $derived(setup.isOwner ?? true);

	function isMemberLastOrPrimaryOwner(member: BreweryMemberDto): boolean {
		if (member.isPrimaryOwner) return true;
		const totalOwners = members.filter((m) => m.role === 'Owner').length;
		return member.role === 'Owner' && totalOwners <= 1;
	}

	function canModifyMember(member: BreweryMemberDto): boolean {
		if (!isOwner) return false;
		if (isMemberLastOrPrimaryOwner(member)) return false;
		if (auth.user && auth.user.id === member.userId) return false;
		return true;
	}

	onMount(async () => {
		await loadData();
	});

	async function loadData() {
		isLoading = true;
		error = null;
		try {
			const memberList = await api.breweryCollaboration.getMembers(setup.id);
			members = memberList;

			if (isOwner) {
				const inviteList = await api.breweryCollaboration.getPendingInvites(setup.id);
				pendingInvites = inviteList;
			}
		} catch (err) {
			console.error('Failed to load brewery members:', err);
			error = err instanceof Error ? err.message : t('brewery.err_load_members');
		} finally {
			isLoading = false;
		}
	}

	async function handleInvite(e: SubmitEvent) {
		e.preventDefault();
		const trimmedEmail = inviteEmail.trim();
		if (!trimmedEmail) return;

		isSendingInvite = true;
		inviteError = null;
		lastCreatedInvite = null;

		try {
			const invite = await api.breweryCollaboration.inviteMember(setup.id, {
				email: trimmedEmail,
				role: inviteRole
			});
			lastCreatedInvite = invite;
			inviteEmail = '';
			pendingInvites = [invite, ...pendingInvites];
		} catch (err) {
			inviteError = err instanceof Error ? err.message : t('brewery.err_send_invite');
		} finally {
			isSendingInvite = false;
		}
	}

	async function handleRevokeInvite(inviteId: string) {
		try {
			await api.breweryCollaboration.revokeInvite(setup.id, inviteId);
			pendingInvites = pendingInvites.filter((i) => i.id !== inviteId);
			if (lastCreatedInvite?.id === inviteId) {
				lastCreatedInvite = null;
			}
		} catch (err) {
			console.error('Failed to revoke invite:', err);
		}
	}

	async function handleRoleChange(targetUserId: string, newRole: BreweryRole) {
		try {
			const updated = await api.breweryCollaboration.updateMemberRole(setup.id, targetUserId, {
				role: newRole
			});
			members = members.map((m) => (m.userId === targetUserId ? updated : m));
		} catch (err) {
			console.error('Failed to update member role:', err);
		}
	}

	async function handleRemoveMember(targetUserId: string) {
		isProcessingAction = true;
		try {
			await api.breweryCollaboration.removeMember(setup.id, targetUserId);
			members = members.filter((m) => m.userId !== targetUserId);
			confirmRemoveUserId = null;
			await brewery.syncFromBackend();
		} catch (err) {
			console.error('Failed to remove member:', err);
		} finally {
			isProcessingAction = false;
		}
	}

	async function handleLeaveBrewery() {
		isProcessingAction = true;
		try {
			await api.breweryCollaboration.leaveBrewery(setup.id);
			await brewery.syncFromBackend();
			onclose();
		} catch (err) {
			console.error('Failed to leave brewery:', err);
		} finally {
			isProcessingAction = false;
		}
	}

	function copyInviteLink(inviteCode: string, id: string) {
		if (typeof window === 'undefined') return;
		const url = `${window.location.origin}/invites/${inviteCode}`;
		navigator.clipboard.writeText(url);
		copiedInviteId = id;
		setTimeout(() => {
			if (copiedInviteId === id) copiedInviteId = null;
		}, 3000);
	}

	function formatDate(iso: string): string {
		try {
			return new Date(iso).toLocaleDateString(undefined, {
				year: 'numeric',
				month: 'short',
				day: 'numeric'
			});
		} catch {
			return iso;
		}
	}
</script>

<svelte:window
	onkeydown={(e) => {
		if (e.key === 'Escape') onclose();
	}}
/>

<div
	use:portal
	class="fixed inset-0 z-50 flex items-center justify-center overflow-y-auto p-4"
	role="dialog"
	aria-modal="true"
	aria-labelledby="members-modal-title"
	tabindex="-1"
>
	<!-- Backdrop -->
	<button
		type="button"
		class="fixed inset-0 cursor-default bg-black/60 backdrop-blur-sm transition-opacity"
		onclick={onclose}
		tabindex="-1"
		aria-hidden="true"
		aria-label={t('brewery.cancel')}
	></button>

	<!-- Dialog Panel -->
	<div
		class="glass-panel relative z-10 flex max-h-[90vh] w-full max-w-xl flex-col rounded-2xl border border-zinc-200/80 bg-white p-6 shadow-2xl dark:border-white/10 dark:bg-zinc-900"
	>
		<!-- Header -->
		<div
			class="flex items-start justify-between border-b border-zinc-200/80 pb-4 dark:border-white/10"
		>
			<div class="flex items-center gap-3">
				<div
					class="flex h-10 w-10 items-center justify-center rounded-xl bg-amber-500/10 text-amber-600 dark:bg-amber-500/15 dark:text-amber-400"
				>
					<Users class="h-5 w-5" />
				</div>
				<div>
					<h3 id="members-modal-title" class="text-base font-bold text-zinc-900 dark:text-white">
						{setup.name} — {t('brewery.members_title')}
					</h3>
					<p class="text-xs text-zinc-500 dark:text-zinc-400">
						{t('brewery.members_subtitle')}
					</p>
				</div>
			</div>
			<button
				type="button"
				onclick={onclose}
				class="rounded-lg p-1.5 text-zinc-400 hover:bg-zinc-100 hover:text-zinc-600 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
				aria-label={t('brewery.cancel')}
			>
				<X class="h-5 w-5" />
			</button>
		</div>

		<!-- Scrollable Content -->
		<div class="flex-1 space-y-6 overflow-y-auto py-4 pr-1">
			{#if error}
				<div
					class="flex items-center gap-2 rounded-xl bg-red-500/10 p-3 text-xs text-red-600 dark:text-red-400"
				>
					<AlertCircle class="h-4 w-4 flex-shrink-0" />
					<span>{error}</span>
				</div>
			{/if}

			<!-- Invite Section (Owner Only) -->
			{#if isOwner}
				<div
					class="rounded-xl border border-zinc-200/70 bg-zinc-50/70 p-4 dark:border-white/5 dark:bg-zinc-800/40"
				>
					<h4
						class="flex items-center gap-2 text-xs font-bold tracking-wider text-zinc-900 uppercase dark:text-zinc-200"
					>
						<UserPlus class="h-4 w-4 text-amber-500" />
						<span>{t('brewery.invite_member')}</span>
					</h4>

					<form onsubmit={handleInvite} class="mt-3 space-y-3">
						<div class="grid grid-cols-1 gap-2 sm:grid-cols-3">
							<div class="sm:col-span-2">
								<label
									for="invite-email-input"
									class="block text-[11px] font-semibold text-zinc-600 dark:text-zinc-400"
								>
									{t('brewery.invite_email_label')}
								</label>
								<input
									id="invite-email-input"
									type="email"
									required
									bind:value={inviteEmail}
									placeholder={t('brewery.invite_email_placeholder')}
									class="mt-1 w-full rounded-xl border border-zinc-300 bg-white px-3 py-1.5 text-xs text-zinc-900 transition focus:border-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900 dark:text-white"
								/>
							</div>

							<div>
								<label
									for="invite-role-select"
									class="block text-[11px] font-semibold text-zinc-600 dark:text-zinc-400"
								>
									{t('brewery.invite_role_label')}
								</label>
								<select
									id="invite-role-select"
									bind:value={inviteRole}
									class="mt-1 w-full rounded-xl border border-zinc-300 bg-white px-3 py-1.5 text-xs text-zinc-900 transition focus:border-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900 dark:text-white"
								>
									<option value="Brewer">{t('brewery.role_brewer')}</option>
									<option value="Owner">{t('brewery.role_owner')}</option>
									<option value="Viewer">{t('brewery.role_viewer')}</option>
								</select>
							</div>
						</div>

						{#if inviteError}
							<div class="flex items-center gap-1.5 text-xs text-red-600 dark:text-red-400">
								<AlertCircle class="h-3.5 w-3.5" />
								<span>{inviteError}</span>
							</div>
						{/if}

						<div class="flex items-center justify-between pt-1">
							<p class="text-[11px] text-zinc-500 dark:text-zinc-400">
								{#if inviteRole === 'Owner'}
									{t('brewery.role_owner_desc')}
								{:else if inviteRole === 'Brewer'}
									{t('brewery.role_brewer_desc')}
								{:else}
									{t('brewery.role_viewer_desc')}
								{/if}
							</p>

							<button
								type="submit"
								disabled={isSendingInvite}
								class="flex items-center gap-1.5 rounded-xl border border-amber-500/40 bg-gradient-to-r from-amber-500 to-amber-600 px-3.5 py-1.5 text-xs font-semibold text-zinc-950 shadow-sm transition hover:from-amber-400 hover:to-amber-500 disabled:opacity-50"
							>
								{#if isSendingInvite}
									<span>{t('brewery.sending_invite')}</span>
								{:else}
									<UserPlus class="h-3.5 w-3.5" />
									<span>{t('brewery.send_invite')}</span>
								{/if}
							</button>
						</div>
					</form>

					<!-- Newly Created Invite Link Display -->
					{#if lastCreatedInvite}
						<div
							class="mt-3 rounded-xl border border-amber-500/30 bg-amber-500/10 p-3 text-xs dark:bg-amber-500/15"
						>
							<div class="flex items-center justify-between">
								<span class="font-medium text-amber-800 dark:text-amber-200">
									{t('brewery.copy_invite_link')}:
								</span>
								<button
									type="button"
									onclick={() => copyInviteLink(lastCreatedInvite!.inviteCode, 'new')}
									class="flex items-center gap-1 rounded-lg bg-amber-500/20 px-2 py-1 text-[11px] font-semibold text-amber-800 hover:bg-amber-500/30 dark:text-amber-300"
								>
									{#if copiedInviteId === 'new'}
										<Check class="h-3 w-3" />
										<span>{t('brewery.invite_link_copied')}</span>
									{:else}
										<Copy class="h-3 w-3" />
										<span>{t('brewery.copy_invite_link')}</span>
									{/if}
								</button>
							</div>
							<div
								class="mt-1 font-mono text-[11px] break-all text-amber-700 select-all dark:text-amber-300"
							>
								{typeof window !== 'undefined'
									? `${window.location.origin}/invites/${lastCreatedInvite.inviteCode}`
									: lastCreatedInvite.inviteCode}
							</div>
						</div>
					{/if}
				</div>
			{/if}

			<!-- Members List -->
			<div class="space-y-3">
				<h4
					class="flex items-center justify-between text-xs font-bold tracking-wider text-zinc-900 uppercase dark:text-zinc-200"
				>
					<span>{t('brewery.members_title')}</span>
					<span class="font-mono text-[11px] text-zinc-400">{members.length}</span>
				</h4>

				{#if isLoading}
					<div class="py-6 text-center text-xs text-zinc-400">{t('brewery.loading_members')}</div>
				{:else if members.length === 0}
					<div class="py-4 text-center text-xs text-zinc-500">{t('brewery.no_members_found')}</div>
				{:else}
					<div class="space-y-2">
						{#each members as member (member.userId)}
							<div
								class="flex items-center justify-between rounded-xl border border-zinc-200/60 bg-zinc-50/50 p-3 transition-colors dark:border-white/5 dark:bg-zinc-800/30"
							>
								<div class="flex items-center gap-3 overflow-hidden">
									<div
										class="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-amber-500/20 to-amber-600/30 text-xs font-bold text-amber-700 uppercase dark:text-amber-300"
									>
										{member.displayName
											? member.displayName.slice(0, 2)
											: member.email
												? member.email.slice(0, 2)
												: 'BR'}
									</div>
									<div class="overflow-hidden text-left">
										<div class="flex items-center gap-2">
											<span class="truncate text-xs font-semibold text-zinc-900 dark:text-white">
												{member.displayName || member.email || 'Brewer'}
											</span>
											{#if member.isPrimaryOwner}
												<span
													class="inline-flex items-center gap-1 rounded-md bg-amber-500/10 px-1.5 py-0.5 text-[10px] font-medium text-amber-600 dark:bg-amber-500/20 dark:text-amber-400"
												>
													<Crown class="h-2.5 w-2.5" />
													{t('brewery.role_badge_primary_owner')}
												</span>
											{:else if member.role === 'Owner'}
												<span
													class="inline-flex items-center gap-1 rounded-md bg-amber-500/10 px-1.5 py-0.5 text-[10px] font-medium text-amber-600 dark:bg-amber-500/20 dark:text-amber-400"
												>
													<Crown class="h-2.5 w-2.5" />
													{t('brewery.role_badge_owner')}
												</span>
											{:else if member.role === 'Brewer'}
												<span
													class="inline-flex items-center gap-1 rounded-md bg-emerald-500/10 px-1.5 py-0.5 text-[10px] font-medium text-emerald-600 dark:bg-emerald-500/20 dark:text-emerald-400"
												>
													<Beer class="h-2.5 w-2.5" />
													{t('brewery.role_badge_brewer')}
												</span>
											{:else}
												<span
													class="inline-flex items-center gap-1 rounded-md bg-zinc-500/10 px-1.5 py-0.5 text-[10px] font-medium text-zinc-600 dark:bg-zinc-500/20 dark:text-zinc-400"
												>
													<Eye class="h-2.5 w-2.5" />
													{t('brewery.role_badge_viewer')}
												</span>
											{/if}
										</div>
										<p class="truncate text-[11px] text-zinc-500 dark:text-zinc-400">
											{member.email ?? ''} • {t('brewery.joined_on', {
												date: formatDate(member.joinedAt)
											})}
										</p>
									</div>
								</div>

								<!-- Member Actions (Owner only, cannot edit or delete primary/last owner or oneself) -->
								{#if isOwner && canModifyMember(member)}
									<div class="flex items-center gap-2">
										<select
											value={member.role}
											onchange={(e) =>
												handleRoleChange(member.userId, e.currentTarget.value as BreweryRole)}
											class="rounded-lg border border-zinc-200 bg-white px-2 py-1 text-[11px] font-medium text-zinc-700 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-300"
										>
											<option value="Brewer">{t('brewery.role_brewer')}</option>
											<option value="Owner">{t('brewery.role_owner')}</option>
											<option value="Viewer">{t('brewery.role_viewer')}</option>
										</select>

										{#if confirmRemoveUserId === member.userId}
											<button
												type="button"
												onclick={() => handleRemoveMember(member.userId)}
												disabled={isProcessingAction}
												class="rounded-lg bg-red-600 px-2 py-1 text-[11px] font-semibold text-white hover:bg-red-700"
											>
												{t('brewery.confirm')}
											</button>
											<button
												type="button"
												onclick={() => (confirmRemoveUserId = null)}
												class="rounded-lg p-1 text-zinc-400 hover:text-zinc-600"
											>
												<X class="h-3.5 w-3.5" />
											</button>
										{:else}
											<button
												type="button"
												onclick={() => (confirmRemoveUserId = member.userId)}
												class="rounded-lg p-1 text-zinc-400 hover:bg-red-500/10 hover:text-red-500"
												title={t('brewery.remove_member')}
												aria-label={t('brewery.remove_member')}
												data-testid={`remove-member-btn-${member.userId}`}
											>
												<Trash2 class="h-3.5 w-3.5" />
											</button>
										{/if}
									</div>
								{:else if member.isPrimaryOwner}
									<span
										class="rounded-md bg-amber-500/10 px-2 py-1 text-[11px] font-semibold text-amber-600 dark:bg-amber-500/15 dark:text-amber-400"
									>
										{t('brewery.role_badge_primary_owner')}
									</span>
								{/if}
							</div>
						{/each}
					</div>
				{/if}
			</div>

			<!-- Pending Invites Section (Owner Only) -->
			{#if isOwner && pendingInvites.length > 0}
				<div class="space-y-2 border-t border-zinc-200/60 pt-2 dark:border-white/5">
					<h4
						class="flex items-center justify-between text-xs font-bold tracking-wider text-zinc-900 uppercase dark:text-zinc-200"
					>
						<span class="flex items-center gap-1.5">
							<Clock class="h-3.5 w-3.5 text-zinc-400" />
							{t('brewery.pending_invites')}
						</span>
						<span class="font-mono text-[11px] text-zinc-400">{pendingInvites.length}</span>
					</h4>

					<div class="space-y-1.5">
						{#each pendingInvites as invite (invite.id)}
							<div
								class="flex items-center justify-between rounded-xl border border-zinc-200/50 bg-zinc-50/30 px-3 py-2 text-xs dark:border-white/5 dark:bg-zinc-800/20"
							>
								<div>
									<span class="font-medium text-zinc-800 dark:text-zinc-200">
										{invite.invitedEmail}
									</span>
									<span
										class="ml-2 rounded bg-zinc-100 px-1.5 py-0.5 text-[10px] text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400"
									>
										{invite.role}
									</span>
								</div>

								<div class="flex items-center gap-2">
									<button
										type="button"
										onclick={() => copyInviteLink(invite.inviteCode, invite.id)}
										class="flex items-center gap-1 rounded-lg border border-zinc-200 px-2 py-1 text-[11px] font-medium text-zinc-600 hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-800"
									>
										{#if copiedInviteId === invite.id}
											<Check class="h-3 w-3 text-green-500" />
											<span>{t('brewery.invite_link_copied')}</span>
										{:else}
											<Copy class="h-3 w-3" />
											<span>{t('brewery.copy_invite_link')}</span>
										{/if}
									</button>

									<button
										type="button"
										onclick={() => handleRevokeInvite(invite.id)}
										class="rounded-lg p-1 text-zinc-400 hover:bg-red-500/10 hover:text-red-500"
										title={t('brewery.revoke_invite')}
									>
										<X class="h-3.5 w-3.5" />
									</button>
								</div>
							</div>
						{/each}
					</div>
				</div>
			{/if}

			<!-- Non-owner Member: Leave Brewery Section -->
			{#if !isOwner}
				<div class="border-t border-zinc-200/60 pt-4 dark:border-white/5">
					{#if showLeaveConfirm}
						<div class="space-y-3 rounded-xl border border-red-500/20 bg-red-500/5 p-4">
							<p class="text-xs text-red-600 dark:text-red-400">
								{t('brewery.leave_brewery_confirm')}
							</p>
							<div class="flex items-center justify-end gap-2">
								<button
									type="button"
									onclick={() => (showLeaveConfirm = false)}
									class="rounded-xl border border-zinc-300 px-3 py-1.5 text-xs font-semibold text-zinc-700 hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-800"
								>
									{t('brewery.cancel')}
								</button>
								<button
									type="button"
									onclick={handleLeaveBrewery}
									disabled={isProcessingAction}
									class="rounded-xl bg-red-600 px-3.5 py-1.5 text-xs font-semibold text-white hover:bg-red-700"
								>
									{t('brewery.leave_brewery')}
								</button>
							</div>
						</div>
					{:else}
						<button
							type="button"
							onclick={() => (showLeaveConfirm = true)}
							class="flex items-center gap-1.5 text-xs font-semibold text-red-600 hover:text-red-700 dark:text-red-400"
						>
							<LogOut class="h-3.5 w-3.5" />
							<span>{t('brewery.leave_brewery')}</span>
						</button>
					{/if}
				</div>
			{/if}
		</div>

		<!-- Footer -->
		<div
			class="flex items-center justify-end border-t border-zinc-200/80 pt-4 dark:border-white/10"
		>
			<button
				type="button"
				onclick={onclose}
				class="rounded-xl border border-zinc-300 px-4 py-2 text-xs font-semibold text-zinc-700 transition hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-800"
			>
				{t('brewery.cancel')}
			</button>
		</div>
	</div>
</div>
