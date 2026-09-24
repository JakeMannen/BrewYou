<script lang="ts">
	import type { Snippet } from 'svelte';
	import { t } from '$lib/i18n/index.svelte';
	import { AlertTriangle, Info, Loader2, X } from '@lucide/svelte';

	interface Props {
		open: boolean;
		title: string;
		message?: string;
		confirmText?: string;
		cancelText?: string;
		showCancel?: boolean;
		variant?: 'danger' | 'warning' | 'info' | 'primary';
		loading?: boolean;
		errorMessage?: string | null;
		confirmTestId?: string;
		cancelTestId?: string;
		onConfirm: () => void | Promise<void>;
		onClose: () => void;
		children?: Snippet;
	}

	let {
		open,
		title,
		message,
		confirmText,
		cancelText,
		showCancel = true,
		variant = 'primary',
		loading = false,
		errorMessage = null,
		confirmTestId = 'confirm-modal-confirm-btn',
		cancelTestId = 'confirm-modal-cancel-btn',
		onConfirm,
		onClose,
		children
	}: Props = $props();

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && open && !loading) {
			onClose();
		}
	}

	function getVariantClasses(v: 'danger' | 'warning' | 'info' | 'primary') {
		switch (v) {
			case 'danger':
				return {
					iconBg: 'border-red-500/30 bg-red-500/10 text-red-600 dark:text-red-400',
					confirmBtn: 'bg-red-600 hover:bg-red-500 text-white'
				};
			case 'warning':
				return {
					iconBg: 'border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400',
					confirmBtn: 'bg-amber-600 hover:bg-amber-500 text-white'
				};
			case 'info':
				return {
					iconBg: 'border-sky-500/30 bg-sky-500/10 text-sky-600 dark:text-sky-400',
					confirmBtn: 'bg-sky-600 hover:bg-sky-500 text-white'
				};
			case 'primary':
			default:
				return {
					iconBg: 'border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400',
					confirmBtn: 'bg-amber-600 hover:bg-amber-500 text-white'
				};
		}
	}

	let variantStyles = $derived(getVariantClasses(variant));
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4"
		role="dialog"
		aria-modal="true"
		aria-labelledby="confirm-modal-title"
		data-testid="confirm-modal"
	>
		<!-- Backdrop -->
		<div
			class="fixed inset-0 bg-black/70 backdrop-blur-sm transition-opacity"
			onclick={() => {
				if (!loading) onClose();
			}}
			aria-hidden="true"
			data-testid="confirm-modal-backdrop"
		></div>

		<!-- Dialog Panel -->
		<div
			class="relative z-10 max-h-[90vh] w-full max-w-md overflow-y-auto rounded-2xl border border-zinc-200 bg-white p-6 shadow-2xl transition-all dark:border-zinc-800 dark:bg-zinc-950"
		>
			<!-- Header -->
			<div
				class="flex items-start justify-between border-b border-zinc-200 pb-4 dark:border-zinc-800"
			>
				<div class="flex items-center gap-3">
					<div
						class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border {variantStyles.iconBg}"
					>
						{#if variant === 'danger' || variant === 'warning'}
							<AlertTriangle class="h-5 w-5" />
						{:else}
							<Info class="h-5 w-5" />
						{/if}
					</div>
					<h2 id="confirm-modal-title" class="text-lg font-bold text-zinc-900 dark:text-zinc-100">
						{title}
					</h2>
				</div>

				<button
					type="button"
					onclick={onClose}
					disabled={loading}
					class="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 disabled:opacity-50 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('common.cancel')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<!-- Body -->
			<div class="space-y-4 py-4 text-sm text-zinc-600 dark:text-zinc-300">
				{#if message}
					<p class="leading-relaxed whitespace-pre-line">{message}</p>
				{/if}

				{#if children}
					{@render children()}
				{/if}

				{#if errorMessage}
					<div
						class="flex items-start gap-2 rounded-xl border border-red-500/30 bg-red-500/10 p-3 text-xs font-medium text-red-600 dark:text-red-400"
						role="alert"
					>
						<AlertTriangle class="mt-0.5 h-4 w-4 shrink-0" />
						<span>{errorMessage}</span>
					</div>
				{/if}
			</div>

			<!-- Footer -->
			<div
				class="flex items-center justify-end gap-3 border-t border-zinc-200 pt-4 dark:border-zinc-800"
			>
				{#if showCancel}
					<button
						type="button"
						data-testid={cancelTestId}
						onclick={onClose}
						disabled={loading}
						class="rounded-xl border border-zinc-300 bg-white px-4 py-2.5 text-sm font-semibold text-zinc-700 shadow-sm transition hover:bg-zinc-50 disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-700"
					>
						{cancelText ?? t('common.cancel')}
					</button>
				{/if}

				<button
					type="button"
					data-testid={confirmTestId}
					onclick={onConfirm}
					disabled={loading}
					class="inline-flex min-h-[42px] items-center gap-2 rounded-xl px-4 py-2.5 text-sm font-bold shadow-sm transition disabled:opacity-50 {variantStyles.confirmBtn}"
				>
					{#if loading}
						<Loader2 class="h-4 w-4 animate-spin" />
					{/if}
					<span>{confirmText ?? t('common.confirm')}</span>
				</button>
			</div>
		</div>
	</div>
{/if}
