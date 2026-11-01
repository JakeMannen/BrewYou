<script lang="ts">
	import { api, ApiClientError, getApiBaseUrl } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import type { EquipmentDto, TapDisplayLinkDto } from '$lib/types/api';
	import ConfirmModal from '$lib/components/ui/ConfirmModal.svelte';
	import {
		X,
		Tv,
		Copy,
		Check,
		ExternalLink,
		RefreshCw,
		Trash2,
		Loader2,
		ShieldCheck
	} from '@lucide/svelte';

	interface Props {
		open: boolean;
		tap: EquipmentDto | null;
		onClose: () => void;
		onUpdated?: (updated: EquipmentDto) => void;
	}

	let { open, tap, onClose, onUpdated }: Props = $props();

	let linkData = $state<TapDisplayLinkDto | null>(null);
	let loading = $state(true);
	let regenerating = $state(false);
	let revoking = $state(false);
	let errorMessage = $state<string | null>(null);
	let copiedKey = $state<string | null>(null);

	let showRegenConfirm = $state(false);
	let showRevokeConfirm = $state(false);

	$effect(() => {
		if (open && tap) {
			loadDisplayLink();
		}
	});

	async function loadDisplayLink() {
		if (!tap) return;
		loading = true;
		errorMessage = null;
		try {
			linkData = await api.equipment.getDisplayLink(tap.id);
		} catch (err) {
			if (err instanceof ApiClientError) {
				errorMessage = err.message;
			} else {
				errorMessage = err instanceof Error ? err.message : t('common.error_generic');
			}
		} finally {
			loading = false;
		}
	}

	async function handleRegenerate() {
		if (!tap) return;
		regenerating = true;
		errorMessage = null;
		try {
			linkData = await api.equipment.enableOrRegenerateDisplayLink(tap.id);
			showRegenConfirm = false;
			onUpdated?.(tap);
		} catch (err) {
			if (err instanceof ApiClientError) {
				errorMessage = err.message;
			} else {
				errorMessage = err instanceof Error ? err.message : t('common.error_generic');
			}
		} finally {
			regenerating = false;
		}
	}

	async function handleRevoke() {
		if (!tap) return;
		revoking = true;
		errorMessage = null;
		try {
			await api.equipment.revokeDisplayLink(tap.id);
			linkData = null;
			showRevokeConfirm = false;
			onUpdated?.(tap);
		} catch (err) {
			if (err instanceof ApiClientError) {
				errorMessage = err.message;
			} else {
				errorMessage = err instanceof Error ? err.message : t('common.error_generic');
			}
		} finally {
			revoking = false;
		}
	}

	let kioskUrl = $derived.by(() => {
		if (!linkData?.kioskUrl) return '';
		if (linkData.kioskUrl.startsWith('http://') || linkData.kioskUrl.startsWith('https://')) {
			return linkData.kioskUrl;
		}
		if (typeof window !== 'undefined') {
			const clean = linkData.kioskUrl.startsWith('/') ? linkData.kioskUrl : `/${linkData.kioskUrl}`;
			return `${window.location.origin}${clean}`;
		}
		return linkData.kioskUrl;
	});

	let fullImageUrl = $derived.by(() => {
		if (!linkData?.imageUrl) return '';
		if (linkData.imageUrl.startsWith('http://') || linkData.imageUrl.startsWith('https://')) {
			return linkData.imageUrl;
		}
		const base = getApiBaseUrl().replace(/\/+$/, '');
		return `${base}${linkData.imageUrl.startsWith('/') ? linkData.imageUrl : `/${linkData.imageUrl}`}`;
	});

	let fullManifestUrl = $derived.by(() => {
		if (!linkData?.manifestUrl) return '';
		if (linkData.manifestUrl.startsWith('http://') || linkData.manifestUrl.startsWith('https://')) {
			return linkData.manifestUrl;
		}
		const base = getApiBaseUrl().replace(/\/+$/, '');
		return `${base}${linkData.manifestUrl.startsWith('/') ? linkData.manifestUrl : `/${linkData.manifestUrl}`}`;
	});

	async function copyToClipboard(text: string, key: string) {
		try {
			await navigator.clipboard.writeText(text);
			copiedKey = key;
			setTimeout(() => {
				if (copiedKey === key) copiedKey = null;
			}, 2000);
		} catch {
			// Clipboard API failed
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && open) {
			onClose();
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open && tap}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4 sm:p-6"
		role="dialog"
		aria-modal="true"
		aria-labelledby="display-link-title"
	>
		<!-- Backdrop -->
		<button
			type="button"
			class="fixed inset-0 bg-black/70 backdrop-blur-sm transition-opacity"
			onclick={onClose}
			aria-label={t('common.cancel')}
		></button>

		<!-- Modal Container -->
		<div
			class="glass-panel relative flex max-h-[90vh] w-full max-w-xl flex-col overflow-hidden rounded-3xl border border-zinc-200/80 shadow-2xl dark:border-white/[0.08]"
		>
			<!-- Header -->
			<div
				class="flex items-center justify-between border-b border-zinc-200/80 px-6 py-4 dark:border-white/[0.08]"
			>
				<div class="flex items-center gap-3">
					<div
						class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-500"
					>
						<Tv class="h-5 w-5" />
					</div>
					<div>
						<h2
							id="display-link-title"
							class="font-editorial text-lg font-bold text-zinc-900 dark:text-white"
						>
							{t('taps.display_link_title', { tapName: tap.name })}
						</h2>
						<p class="text-xs text-zinc-500 dark:text-zinc-400">
							{t('taps.display_link_desc')}
						</p>
					</div>
				</div>

				<button
					type="button"
					onclick={onClose}
					class="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('common.cancel')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<!-- Body -->
			<div class="flex-1 space-y-6 overflow-y-auto p-6">
				{#if errorMessage}
					<div
						class="rounded-xl border border-red-500/20 bg-red-500/10 p-3 text-xs text-red-600 dark:text-red-400"
					>
						{errorMessage}
					</div>
				{/if}

				{#if loading}
					<div class="flex h-40 items-center justify-center">
						<Loader2 class="h-6 w-6 animate-spin text-amber-500" />
					</div>
				{:else if linkData?.kioskUrl}
					<!-- Live Kiosk Web View URL -->
					<div class="space-y-1.5">
						<div class="flex items-center justify-between">
							<span class="text-xs font-bold text-zinc-800 dark:text-zinc-200">
								{t('taps.kiosk_web_link')}
							</span>
							<a
								href={kioskUrl}
								target="_blank"
								rel="noreferrer"
								class="flex items-center gap-1 text-[11px] font-semibold text-amber-600 hover:text-amber-500 dark:text-amber-400"
							>
								<span>{t('taps.open_kiosk_tab')}</span>
								<ExternalLink class="h-3 w-3" />
							</a>
						</div>
						<div class="flex items-center gap-2">
							<input
								type="text"
								readonly
								value={kioskUrl}
								class="h-9 flex-1 rounded-xl border border-zinc-300 bg-zinc-50 px-3 font-mono text-xs text-zinc-900 select-all dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							/>
							<button
								type="button"
								onclick={() => copyToClipboard(kioskUrl, 'kiosk')}
								class="flex h-9 items-center gap-1.5 rounded-xl border border-zinc-200/80 bg-white px-3 text-xs font-semibold text-zinc-700 hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-200 dark:hover:bg-zinc-800"
							>
								{#if copiedKey === 'kiosk'}
									<Check class="h-3.5 w-3.5 text-emerald-500" />
									<span class="text-emerald-500">{t('common.copied')}</span>
								{:else}
									<Copy class="h-3.5 w-3.5" />
									<span>{t('common.copy')}</span>
								{/if}
							</button>
						</div>
						<p class="text-[11px] text-zinc-500 dark:text-zinc-400">
							{t('taps.kiosk_web_desc')}
						</p>
					</div>

					<!-- Direct Rendered Image URL -->
					<div class="space-y-1.5">
						<span class="text-xs font-bold text-zinc-800 dark:text-zinc-200">
							{t('taps.rendered_image_url')}
						</span>
						<div class="flex items-center gap-2">
							<input
								type="text"
								readonly
								value={fullImageUrl}
								class="h-9 flex-1 rounded-xl border border-zinc-300 bg-zinc-50 px-3 font-mono text-xs text-zinc-900 select-all dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							/>
							<button
								type="button"
								onclick={() => copyToClipboard(fullImageUrl, 'image')}
								class="flex h-9 items-center gap-1.5 rounded-xl border border-zinc-200/80 bg-white px-3 text-xs font-semibold text-zinc-700 hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-200 dark:hover:bg-zinc-800"
							>
								{#if copiedKey === 'image'}
									<Check class="h-3.5 w-3.5 text-emerald-500" />
									<span class="text-emerald-500">{t('common.copied')}</span>
								{:else}
									<Copy class="h-3.5 w-3.5" />
									<span>{t('common.copy')}</span>
								{/if}
							</button>
						</div>
						<p class="text-[11px] text-zinc-500 dark:text-zinc-400">
							{t('taps.rendered_image_desc')}
						</p>
					</div>

					<!-- JSON Manifest API URL -->
					<div class="space-y-1.5">
						<span class="text-xs font-bold text-zinc-800 dark:text-zinc-200">
							{t('taps.manifest_api_url')}
						</span>
						<div class="flex items-center gap-2">
							<input
								type="text"
								readonly
								value={fullManifestUrl}
								class="h-9 flex-1 rounded-xl border border-zinc-300 bg-zinc-50 px-3 font-mono text-xs text-zinc-900 select-all dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							/>
							<button
								type="button"
								onclick={() => copyToClipboard(fullManifestUrl, 'manifest')}
								class="flex h-9 items-center gap-1.5 rounded-xl border border-zinc-200/80 bg-white px-3 text-xs font-semibold text-zinc-700 hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-200 dark:hover:bg-zinc-800"
							>
								{#if copiedKey === 'manifest'}
									<Check class="h-3.5 w-3.5 text-emerald-500" />
									<span class="text-emerald-500">{t('common.copied')}</span>
								{:else}
									<Copy class="h-3.5 w-3.5" />
									<span>{t('common.copy')}</span>
								{/if}
							</button>
						</div>
						<p class="text-[11px] text-zinc-500 dark:text-zinc-400">
							{t('taps.manifest_api_desc')}
						</p>
					</div>

					<!-- Security Info Banner -->
					<div
						class="flex items-start gap-2.5 rounded-xl border border-amber-500/20 bg-amber-500/5 p-3.5 text-xs text-amber-800 dark:border-amber-500/20 dark:bg-amber-500/10 dark:text-amber-300"
					>
						<ShieldCheck class="h-4 w-4 shrink-0 text-amber-500" />
						<div class="space-y-1">
							<p class="font-bold">{t('taps.security_protected_title')}</p>
							<p class="text-[11px] leading-relaxed opacity-90">
								{t('taps.security_protected_desc')}
							</p>
						</div>
					</div>
				{:else}
					<!-- Disabled state -->
					<div class="flex flex-col items-center justify-center py-8 text-center">
						<Tv class="h-10 w-10 text-zinc-400 opacity-60" />
						<p class="mt-3 text-sm font-bold text-zinc-800 dark:text-zinc-200">
							{t('taps.display_link_disabled')}
						</p>
						<p class="mt-1 text-xs text-zinc-500 dark:text-zinc-400">
							{t('taps.display_link_disabled_desc')}
						</p>
						<button
							type="button"
							onclick={handleRegenerate}
							disabled={regenerating}
							class="mt-4 flex items-center gap-2 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-4 py-2 text-xs font-bold text-zinc-950 shadow-md transition-all hover:from-amber-400 hover:to-amber-500"
						>
							{#if regenerating}
								<Loader2 class="h-4 w-4 animate-spin" />
							{:else}
								<Tv class="h-4 w-4" />
							{/if}
							<span>{t('taps.enable_display_link_btn')}</span>
						</button>
					</div>
				{/if}
			</div>

			<!-- Footer -->
			<div
				class="flex items-center justify-between border-t border-zinc-200/80 px-6 py-4 dark:border-white/[0.08]"
			>
				<div>
					{#if linkData?.kioskUrl}
						<button
							type="button"
							onclick={() => (showRevokeConfirm = true)}
							disabled={revoking || regenerating}
							class="flex items-center gap-1.5 text-xs font-semibold text-red-600 hover:text-red-500 dark:text-red-400"
						>
							<Trash2 class="h-3.5 w-3.5" />
							<span>{t('taps.revoke_link_btn')}</span>
						</button>
					{/if}
				</div>

				<div class="flex items-center gap-3">
					{#if linkData?.kioskUrl}
						<button
							type="button"
							onclick={() => (showRegenConfirm = true)}
							disabled={regenerating || revoking}
							class="flex items-center gap-1.5 rounded-xl border border-zinc-200/80 bg-white px-3.5 py-2 text-xs font-semibold text-zinc-700 shadow-xs transition-colors hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-200 dark:hover:bg-zinc-800"
						>
							<RefreshCw class="h-3.5 w-3.5 text-zinc-500 {regenerating ? 'animate-spin' : ''}" />
							<span>{t('taps.regenerate_token_btn')}</span>
						</button>
					{/if}

					<button
						type="button"
						onclick={onClose}
						class="rounded-xl border border-zinc-200/80 bg-white px-4 py-2 text-xs font-semibold text-zinc-700 transition-colors hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800"
					>
						{t('common.close')}
					</button>
				</div>
			</div>
		</div>
	</div>
{/if}

<!-- Confirm Regenerate Modal -->
<ConfirmModal
	open={showRegenConfirm}
	title={t('taps.regen_confirm_title')}
	message={t('taps.regen_confirm_msg')}
	confirmText={t('taps.regen_confirm_btn')}
	cancelText={t('common.cancel')}
	variant="danger"
	onConfirm={handleRegenerate}
	onClose={() => (showRegenConfirm = false)}
/>

<!-- Confirm Revoke Modal -->
<ConfirmModal
	open={showRevokeConfirm}
	title={t('taps.revoke_confirm_title')}
	message={t('taps.revoke_confirm_msg')}
	confirmText={t('taps.revoke_confirm_btn')}
	cancelText={t('common.cancel')}
	variant="danger"
	onConfirm={handleRevoke}
	onClose={() => (showRevokeConfirm = false)}
/>
