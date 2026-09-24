<script lang="ts">
	import { api } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import type { BatchDetailDto, BatchSummaryDto } from '$lib/types/api';
	import { AlertTriangle, Loader2, X, Trash2 } from '@lucide/svelte';

	interface Props {
		open: boolean;
		batch: BatchDetailDto | BatchSummaryDto | null;
		onClose: () => void;
		onDeleted: (batchId: string) => void;
	}

	let { open, batch, onClose, onDeleted }: Props = $props();

	let isDeleting = $state(false);
	let errorMessage = $state<string | null>(null);

	$effect(() => {
		if (open && batch) {
			errorMessage = null;
			isDeleting = false;
		}
	});

	async function handleConfirmDelete() {
		if (!batch || isDeleting) return;

		isDeleting = true;
		errorMessage = null;
		try {
			await api.batches.delete(batch.id);
			onDeleted(batch.id);
			onClose();
		} catch (err: unknown) {
			errorMessage = (err as Error).message || t('batches.delete_modal.error');
		} finally {
			isDeleting = false;
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && open && !isDeleting) {
			onClose();
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open && batch}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4"
		role="dialog"
		aria-modal="true"
		aria-labelledby="delete-batch-modal-title"
		data-testid="delete-batch-modal"
	>
		<!-- Backdrop -->
		<div
			class="fixed inset-0 bg-black/70 backdrop-blur-sm transition-opacity"
			onclick={() => {
				if (!isDeleting) onClose();
			}}
			aria-hidden="true"
			data-testid="delete-batch-modal-backdrop"
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
						class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-red-500/30 bg-red-500/10 text-red-600 dark:text-red-400"
					>
						<Trash2 class="h-5 w-5" />
					</div>
					<div>
						<h2
							id="delete-batch-modal-title"
							class="text-lg font-bold text-zinc-900 dark:text-white"
						>
							{t('batches.delete_modal.title')}
						</h2>
						<p class="text-xs text-zinc-500 dark:text-zinc-400">
							{batch.batchCode ? `${batch.batchCode} — ` : ''}{batch.name}
						</p>
					</div>
				</div>

				<button
					type="button"
					onclick={onClose}
					disabled={isDeleting}
					class="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 disabled:opacity-50 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('batches.delete_modal.cancel_button')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<!-- Body -->
			<div class="space-y-4 py-4">
				<p class="text-sm text-zinc-600 dark:text-zinc-300">
					{t('batches.delete_modal.confirm_prompt', { name: batch.name })}
				</p>

				{#if errorMessage}
					<div
						class="flex items-start gap-2 rounded-xl border border-red-200 bg-red-50 p-3 text-xs text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
						role="alert"
					>
						<AlertTriangle class="h-4 w-4 shrink-0 text-red-500" />
						<span>{errorMessage}</span>
					</div>
				{/if}
			</div>

			<!-- Footer -->
			<div
				class="flex items-center justify-end gap-3 border-t border-zinc-200 pt-4 dark:border-zinc-800"
			>
				<button
					type="button"
					data-testid="cancel-delete-batch-btn"
					onclick={onClose}
					disabled={isDeleting}
					class="rounded-xl border border-zinc-200 px-4 py-2.5 text-sm font-semibold text-zinc-700 transition-colors hover:bg-zinc-100 disabled:opacity-50 dark:border-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-900"
				>
					{t('batches.delete_modal.cancel_button')}
				</button>

				<button
					type="button"
					data-testid="confirm-delete-batch-btn"
					onclick={handleConfirmDelete}
					disabled={isDeleting}
					class="flex min-h-[42px] items-center gap-2 rounded-xl bg-red-600 px-4 py-2.5 text-sm font-bold text-white shadow-sm transition-all hover:bg-red-500 disabled:opacity-50"
				>
					{#if isDeleting}
						<Loader2 class="h-4 w-4 animate-spin" />
						<span>{t('batches.delete_modal.deleting')}</span>
					{:else}
						<Trash2 class="h-4 w-4" />
						<span>{t('batches.delete_modal.confirm_button')}</span>
					{/if}
				</button>
			</div>
		</div>
	</div>
{/if}
