<script lang="ts">
	import { onMount } from 'svelte';
	import {
		Radio,
		Loader2,
		Check,
		AlertCircle,
		Pencil,
		Trash2
	} from 'lucide-svelte';
	import ConfirmModal from '$lib/components/ui/ConfirmModal.svelte';
	import { api } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import type { BrewLinkGatewayDto, BrewLinkDiscoveredGatewayDto } from '$lib/types/api';

	interface Props {
		onGatewaysChanged?: (gateways: BrewLinkGatewayDto[]) => void;
	}

	let { onGatewaysChanged }: Props = $props();

	let brewLinkGateways = $state<BrewLinkGatewayDto[]>([]);
	let isLoadingGateways = $state(false);
	let discoveredGateways = $state<BrewLinkDiscoveredGatewayDto[]>([]);
	let isDiscoveringGateways = $state(false);
	let claimingGatewayId = $state<string | null>(null);
	let pairingHost = $state('');
	let pairingPin = $state('');
	let pairingName = $state('');
	let isRequestingPin = $state(false);
	let requestPinSuccess = $state(false);
	let requestPinError = $state<string | null>(null);
	let isPairing = $state(false);
	let pairingError = $state<string | null>(null);

	// Edit gateway state
	let editingGateway = $state<BrewLinkGatewayDto | null>(null);
	let editGatewayName = $state('');
	let editGatewayHost = $state('');
	let isUpdatingGateway = $state(false);
	let updateGatewayError = $state<string | null>(null);

	// Delete gateway state
	let gatewayToDelete = $state<BrewLinkGatewayDto | null>(null);
	let isDeletingGateway = $state(false);

	async function loadGateways() {
		isLoadingGateways = true;
		try {
			const [paired, discovered] = await Promise.all([
				api.brewlink.listGateways(),
				api.brewlink.discoverGateways().catch(() => [])
			]);
			brewLinkGateways = paired;
			discoveredGateways = discovered;
			onGatewaysChanged?.(paired);
		} catch (err: unknown) {
			console.error('Failed to load BrewLink gateways', err);
		} finally {
			isLoadingGateways = false;
		}
	}

	async function discoverGateways() {
		isDiscoveringGateways = true;
		try {
			discoveredGateways = await api.brewlink.discoverGateways();
		} catch (err: unknown) {
			console.error('Failed to discover BrewLink gateways', err);
		} finally {
			isDiscoveringGateways = false;
		}
	}

	async function handleClaimDiscoveredGateway(beacon: BrewLinkDiscoveredGatewayDto) {
		pairingError = null;
		claimingGatewayId = beacon.gatewayId;
		try {
			await api.brewlink.pairGateway({
				host: beacon.host,
				name: pairingName.trim() || beacon.name || undefined
			});
			await loadGateways();
		} catch (err: unknown) {
			pairingError = (err as Error).message || 'Failed to pair gateway';
		} finally {
			claimingGatewayId = null;
		}
	}

	async function handleRequestPin() {
		requestPinError = null;
		requestPinSuccess = false;
		if (!pairingHost.trim()) {
			requestPinError = t('settings.brewlink.host_label');
			return;
		}
		isRequestingPin = true;
		try {
			await api.brewlink.startPairing(pairingHost.trim());
			requestPinSuccess = true;
		} catch (err: unknown) {
			requestPinError = (err as Error).message || 'Failed to request PIN';
		} finally {
			isRequestingPin = false;
		}
	}

	async function handlePairGateway(e: SubmitEvent) {
		e.preventDefault();
		pairingError = null;
		const cleanHost = pairingHost.trim();
		const cleanPin = pairingPin.replace(/[\s-_]/g, '').trim();

		if (!cleanHost) {
			pairingError = t('settings.brewlink.host_label');
			return;
		}

		if (cleanPin && (cleanPin.length !== 6 || !/^[0-9A-Za-z]{6}$/.test(cleanPin))) {
			pairingError = t('settings.brewlink.pin_invalid');
			return;
		}

		isPairing = true;
		try {
			await api.brewlink.pairGateway({
				host: cleanHost,
				pin: cleanPin || null,
				name: pairingName.trim() || undefined
			});
			pairingHost = '';
			pairingPin = '';
			pairingName = '';
			requestPinSuccess = false;
			await loadGateways();
		} catch (err: unknown) {
			pairingError = (err as Error).message || 'Failed to pair gateway';
		} finally {
			isPairing = false;
		}
	}

	function startEditGateway(gw: BrewLinkGatewayDto) {
		editingGateway = gw;
		editGatewayName = gw.name;
		editGatewayHost = gw.host;
		updateGatewayError = null;
	}

	function cancelEditGateway() {
		editingGateway = null;
		editGatewayName = '';
		editGatewayHost = '';
		updateGatewayError = null;
	}

	async function handleUpdateGateway(e: SubmitEvent) {
		e.preventDefault();
		if (!editingGateway) return;
		isUpdatingGateway = true;
		updateGatewayError = null;
		try {
			await api.brewlink.updateGateway(editingGateway.id, {
				name: editGatewayName.trim(),
				host: editGatewayHost.trim()
			});
			editingGateway = null;
			await loadGateways();
		} catch (err: unknown) {
			updateGatewayError = (err as Error).message || 'Failed to update gateway';
		} finally {
			isUpdatingGateway = false;
		}
	}

	function confirmDeleteGateway(gw: BrewLinkGatewayDto) {
		gatewayToDelete = gw;
	}

	async function handleDeleteGateway() {
		if (!gatewayToDelete) return;
		isDeletingGateway = true;
		try {
			await api.brewlink.deleteGateway(gatewayToDelete.id);
			gatewayToDelete = null;
			await loadGateways();
		} catch (err: unknown) {
			console.error('Failed to delete gateway', err);
		} finally {
			isDeletingGateway = false;
		}
	}

	onMount(() => {
		void loadGateways();
	});
</script>

<div class="space-y-4 text-xs" data-testid="brewlink-section">
	<div class="flex items-center justify-between border-b border-zinc-200/80 pb-3 dark:border-white/[0.08]">
		<div class="flex items-center gap-2.5">
			<div
				class="flex h-8 w-8 items-center justify-center rounded-lg border border-sky-500/30 bg-sky-500/10 text-sky-600 dark:text-sky-400"
			>
				<Radio class="h-4 w-4" />
			</div>
			<div>
				<h3 class="text-sm font-bold text-zinc-900 dark:text-white">
					{t('settings.brewlink.title')}
				</h3>
				<p class="text-[11px] text-zinc-500 dark:text-zinc-400">
					{t('settings.brewlink.description')}
				</p>
			</div>
		</div>
	</div>

	<!-- Paired Gateways List -->
	<div class="space-y-2">
		<h4 class="text-[11px] font-bold tracking-wider text-zinc-700 uppercase dark:text-zinc-300">
			{t('settings.brewlink.paired_gateways')}
		</h4>

		{#if isLoadingGateways}
			<div class="flex items-center justify-center py-4">
				<Loader2 class="h-4 w-4 animate-spin text-sky-500" />
			</div>
		{:else if brewLinkGateways.length === 0}
			<div
				class="rounded-xl border border-dashed border-zinc-300 p-4 text-center text-xs text-zinc-500 dark:border-zinc-700 dark:text-zinc-400"
			>
				{t('settings.brewlink.no_gateways')}
			</div>
		{:else}
			<div class="space-y-2">
				{#each brewLinkGateways as gw (gw.id)}
					<div
						class="flex flex-col justify-between gap-2.5 rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-3 sm:flex-row sm:items-center dark:border-white/5 dark:bg-white/[0.02]"
					>
						<div class="space-y-0.5 min-w-0">
							<div class="flex items-center gap-2 flex-wrap">
								<span class="text-xs font-bold text-zinc-900 dark:text-white truncate">{gw.name}</span>
								<span
									class="rounded-full bg-sky-500/15 px-2 py-0.5 text-[10px] font-bold text-sky-700 dark:text-sky-400"
								>
									{t('settings.brewlink.paired_badge')}
								</span>
							</div>
							<div class="flex flex-wrap items-center gap-2 font-mono text-[10px] text-zinc-500 dark:text-zinc-400">
								<span>{gw.host}</span>
								<span>•</span>
								<code>{gw.gatewayId}</code>
								{#if gw.pairedAt}
									<span>•</span>
									<span>{t('settings.brewlink.paired_at', { date: new Date(gw.pairedAt).toLocaleDateString() })}</span>
								{/if}
							</div>
						</div>

						<div class="flex shrink-0 items-center gap-1.5 self-end sm:self-center">
							<button
								type="button"
								data-testid="edit-gateway-btn-{gw.id}"
								onclick={() => startEditGateway(gw)}
								class="inline-flex items-center gap-1 rounded-lg border border-zinc-200 bg-white px-2 py-1 text-[11px] font-semibold text-zinc-700 shadow-2xs hover:bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-700"
							>
								<Pencil class="h-3 w-3" />
								<span>{t('settings.brewlink.edit_gateway')}</span>
							</button>
							<button
								type="button"
								data-testid="unpair-gateway-btn-{gw.id}"
								onclick={() => confirmDeleteGateway(gw)}
								class="inline-flex items-center gap-1 rounded-lg border border-red-200 bg-white px-2 py-1 text-[11px] font-semibold text-red-600 shadow-2xs hover:bg-red-50 dark:border-red-900/40 dark:bg-zinc-800 dark:text-red-400 dark:hover:bg-red-950/20"
							>
								<Trash2 class="h-3 w-3" />
								<span>{t('settings.brewlink.unpair_gateway')}</span>
							</button>
						</div>
					</div>
				{/each}
			</div>
		{/if}
	</div>

	<!-- Discovered Broadcasting Gateways -->
	<div
		class="rounded-xl border border-sky-500/30 bg-sky-500/5 p-3.5 dark:border-sky-500/20 dark:bg-sky-500/10"
	>
		<div class="flex flex-col justify-between gap-2 sm:flex-row sm:items-center">
			<div>
				<div class="flex items-center gap-2">
					<Radio class="h-3.5 w-3.5 text-sky-500" />
					<h4 class="text-xs font-bold text-zinc-900 dark:text-white">
						{t('settings.brewlink.discovered_gateways')}
					</h4>
				</div>
				<p class="mt-0.5 text-[11px] text-zinc-500 dark:text-zinc-400">
					{t('settings.brewlink.discover_hint')}
				</p>
			</div>
			<button
				type="button"
				data-testid="brewlink-scan-btn"
				disabled={isDiscoveringGateways}
				onclick={discoverGateways}
				class="inline-flex shrink-0 items-center gap-1.5 self-start rounded-xl border border-sky-500/30 bg-white px-3 py-1.5 text-xs font-semibold text-sky-700 shadow-2xs transition hover:bg-sky-50 disabled:opacity-50 sm:self-center dark:border-sky-500/30 dark:bg-zinc-900 dark:text-sky-300 dark:hover:bg-zinc-800"
			>
				{#if isDiscoveringGateways}
					<Loader2 class="h-3.5 w-3.5 animate-spin" />
					<span>{t('settings.brewlink.scanning')}</span>
				{:else}
					<Radio class="h-3.5 w-3.5" />
					<span>{t('settings.brewlink.scan_btn')}</span>
				{/if}
			</button>
		</div>

		{#if discoveredGateways.length === 0}
			<p class="mt-2 text-[11px] text-zinc-500 dark:text-zinc-400">
				{t('settings.brewlink.no_broadcasts')}
			</p>
		{:else}
			<div class="mt-2.5 space-y-2">
				{#each discoveredGateways as beacon (beacon.gatewayId)}
					<div
						data-testid="discovered-gateway-{beacon.gatewayId}"
						class="flex flex-col justify-between gap-2 rounded-xl border border-sky-500/30 bg-white p-2.5 sm:flex-row sm:items-center dark:border-sky-500/20 dark:bg-zinc-900/80"
					>
						<div class="space-y-0.5 min-w-0">
							<div class="flex items-center gap-2">
								<span class="text-xs font-bold text-zinc-900 dark:text-white">
									{beacon.name}
								</span>
								<span
									class="rounded-full bg-emerald-500/15 px-2 py-0.5 text-[10px] font-bold text-emerald-700 dark:text-emerald-400"
								>
									{t('settings.brewlink.remaining_window', {
										sec: beacon.remainingSec
									})}
								</span>
							</div>
							<div class="text-[11px] text-zinc-500 dark:text-zinc-400 font-mono">
								<span>{beacon.host}</span>
								<span class="mx-1">•</span>
								<code>{beacon.gatewayId}</code>
							</div>
						</div>
						<button
							type="button"
							data-testid="claim-gateway-btn-{beacon.gatewayId}"
							disabled={claimingGatewayId === beacon.gatewayId}
							onclick={() => handleClaimDiscoveredGateway(beacon)}
							class="inline-flex items-center gap-1.5 self-end rounded-xl bg-sky-600 px-3 py-1.5 text-xs font-bold text-white shadow-2xs transition hover:bg-sky-500 disabled:opacity-50 sm:self-center"
						>
							{#if claimingGatewayId === beacon.gatewayId}
								<Loader2 class="h-3 w-3 animate-spin" />
								<span>{t('settings.brewlink.pairing')}</span>
							{:else}
								<Check class="h-3 w-3" />
								<span>{t('settings.brewlink.claim_now_btn')}</span>
							{/if}
						</button>
					</div>
				{/each}
			</div>
		{/if}
	</div>

	<!-- Pair New Gateway Form -->
	<div
		class="rounded-xl border border-zinc-200/60 bg-zinc-50/50 p-3.5 dark:border-white/5 dark:bg-white/[0.02]"
	>
		<div class="flex items-center gap-2">
			<Radio class="h-3.5 w-3.5 text-sky-500" />
			<h4 class="text-xs font-bold tracking-wider text-zinc-900 uppercase dark:text-white">
				{t('settings.brewlink.pair_new_gateway')}
			</h4>
		</div>

		<form onsubmit={handlePairGateway} novalidate class="mt-3 space-y-3">
			<div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
				<!-- Host / IP -->
				<div>
					<label
						for="mgr-brewlink-host-input"
						class="block text-[11px] font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('settings.brewlink.host_label')} *
					</label>
					<div class="mt-1 flex gap-2">
						<input
							id="mgr-brewlink-host-input"
							data-testid="brewlink-host-input"
							type="text"
							bind:value={pairingHost}
							placeholder={t('settings.brewlink.host_placeholder')}
							required
							class="w-full rounded-xl border border-zinc-300 bg-white px-3 py-1.5 font-mono text-xs text-zinc-900 transition focus:border-sky-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900 dark:text-white"
						/>
						<button
							type="button"
							data-testid="brewlink-request-pin-btn"
							disabled={isRequestingPin || !pairingHost.trim()}
							onclick={handleRequestPin}
							class="shrink-0 rounded-xl border border-zinc-300 bg-white px-2.5 py-1.5 text-xs font-semibold text-zinc-700 shadow-2xs hover:bg-zinc-50 disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-200"
						>
							{#if isRequestingPin}
								<Loader2 class="h-3 w-3 animate-spin" />
							{:else}
								{t('settings.brewlink.request_pin_btn')}
							{/if}
						</button>
					</div>
					<span class="mt-0.5 block text-[10px] text-zinc-400 dark:text-zinc-500">
						{t('settings.brewlink.host_help')}
					</span>
				</div>

				<!-- 6-Digit PIN -->
				<div>
					<label
						for="mgr-brewlink-pin-input"
						class="block text-[11px] font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('settings.brewlink.pin_label')}
					</label>
					<input
						id="mgr-brewlink-pin-input"
						data-testid="brewlink-pin-input"
						type="text"
						inputmode="numeric"
						autocomplete="one-time-code"
						maxlength="12"
						bind:value={pairingPin}
						placeholder={t('settings.brewlink.pin_placeholder')}
						class="mt-1 w-full rounded-xl border border-zinc-300 bg-white px-3 py-1.5 font-mono text-xs tracking-widest text-zinc-900 transition focus:border-sky-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900 dark:text-white"
					/>
					<span class="mt-0.5 block text-[10px] text-zinc-400 dark:text-zinc-500">
						{t('settings.brewlink.pin_optional_help')}
					</span>
				</div>
			</div>

			<!-- Optional Gateway Name -->
			<div>
				<label
					for="mgr-brewlink-name-input"
					class="block text-[11px] font-semibold text-zinc-700 dark:text-zinc-300"
				>
					{t('settings.brewlink.gateway_name')}
				</label>
				<input
					id="mgr-brewlink-name-input"
					data-testid="brewlink-name-input"
					type="text"
					bind:value={pairingName}
					placeholder={t('settings.brewlink.gateway_name_placeholder')}
					class="mt-1 w-full rounded-xl border border-zinc-300 bg-white px-3 py-1.5 text-xs text-zinc-900 transition focus:border-sky-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900 dark:text-white"
				/>
			</div>

			{#if requestPinSuccess}
				<div
					class="flex items-center gap-2 rounded-xl border border-emerald-500/30 bg-emerald-500/10 px-3 py-2 text-xs font-medium text-emerald-700 dark:text-emerald-300"
				>
					<Check class="h-4 w-4 shrink-0 text-emerald-500" />
					<span>{t('settings.brewlink.pin_requested')}</span>
				</div>
			{/if}

			{#if requestPinError}
				<div
					class="flex items-center gap-2 rounded-xl border border-red-500/30 bg-red-500/10 px-3 py-2 text-xs font-medium text-red-600 dark:text-red-400"
				>
					<AlertCircle class="h-4 w-4 shrink-0" />
					<span>{requestPinError}</span>
				</div>
			{/if}

			{#if pairingError}
				<div
					class="flex items-center gap-2 rounded-xl border border-red-500/30 bg-red-500/10 px-3 py-2 text-xs font-medium text-red-600 dark:text-red-400"
				>
					<AlertCircle class="h-4 w-4 shrink-0" />
					<span>{pairingError}</span>
				</div>
			{/if}

			<div class="flex justify-end pt-1">
				<button
					type="submit"
					data-testid="brewlink-pair-btn"
					disabled={isPairing || !pairingHost.trim()}
					class="inline-flex items-center gap-2 rounded-xl bg-sky-600 px-4 py-2 text-xs font-bold text-white shadow-2xs transition hover:bg-sky-500 disabled:opacity-50"
				>
					{#if isPairing}
						<Loader2 class="h-3.5 w-3.5 animate-spin" />
						<span>{t('settings.brewlink.pairing')}</span>
					{:else}
						<Check class="h-3.5 w-3.5" />
						<span>{t('settings.brewlink.pair_btn')}</span>
					{/if}
				</button>
			</div>
		</form>
	</div>

	<!-- Edit Gateway Modal -->
	{#if editingGateway}
		<div
			class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-xs"
			role="dialog"
			aria-modal="true"
		>
			<div
				class="w-full max-w-md rounded-2xl border border-zinc-200 bg-white p-6 shadow-xl dark:border-zinc-800 dark:bg-zinc-900"
			>
				<h3 class="text-base font-bold text-zinc-900 dark:text-white">
					{t('settings.brewlink.edit_gateway')}
				</h3>
				<form onsubmit={handleUpdateGateway} class="mt-4 space-y-4">
					<div>
						<label
							for="edit-brewlink-name-input"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('settings.brewlink.gateway_name')}
						</label>
						<input
							id="edit-brewlink-name-input"
							type="text"
							bind:value={editGatewayName}
							required
							class="mt-1.5 w-full rounded-xl border border-zinc-300 px-3.5 py-2 text-sm text-zinc-900 dark:border-zinc-700 dark:bg-zinc-800 dark:text-white"
						/>
					</div>
					<div>
						<label
							for="edit-brewlink-host-input"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('settings.brewlink.host_label')}
						</label>
						<input
							id="edit-brewlink-host-input"
							type="text"
							bind:value={editGatewayHost}
							required
							class="mt-1.5 w-full rounded-xl border border-zinc-300 px-3.5 py-2 font-mono text-sm text-zinc-900 dark:border-zinc-700 dark:bg-zinc-800 dark:text-white"
						/>
					</div>

					{#if updateGatewayError}
						<div
							class="flex items-center gap-2 rounded-xl border border-red-500/30 bg-red-500/10 px-3 py-2 text-xs font-medium text-red-600 dark:text-red-400"
						>
							<AlertCircle class="h-4 w-4 shrink-0" />
							<span>{updateGatewayError}</span>
						</div>
					{/if}

					<div class="flex justify-end gap-2 pt-2">
						<button
							type="button"
							onclick={cancelEditGateway}
							class="rounded-xl border border-zinc-300 px-4 py-2 text-xs font-semibold text-zinc-700 hover:bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
						>
							{t('common.cancel')}
						</button>
						<button
							type="submit"
							disabled={isUpdatingGateway}
							class="inline-flex items-center gap-2 rounded-xl bg-sky-600 px-4 py-2 text-xs font-bold text-white hover:bg-sky-500"
						>
							{#if isUpdatingGateway}
								<Loader2 class="h-3.5 w-3.5 animate-spin" />
								<span>{t('settings.brewlink.updating')}</span>
							{:else}
								<span>{t('settings.brewlink.update_gateway')}</span>
							{/if}
						</button>
					</div>
				</form>
			</div>
		</div>
	{/if}

	<ConfirmModal
		open={!!gatewayToDelete}
		title={t('settings.brewlink.unpair_confirm_title')}
		message={gatewayToDelete
			? t('settings.brewlink.unpair_confirm_message', {
					name: gatewayToDelete.name,
					gatewayId: gatewayToDelete.gatewayId
				})
			: ''}
		confirmText={t('settings.brewlink.unpair_gateway')}
		cancelText={t('common.cancel')}
		variant="danger"
		loading={isDeletingGateway}
		onConfirm={handleDeleteGateway}
		onClose={() => (gatewayToDelete = null)}
	/>
</div>
