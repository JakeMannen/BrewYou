<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { AlertTriangle, Radio, Loader2, X } from '@lucide/svelte';

	interface Props {
		open: boolean;
		message: string;
		equipmentName?: string | null;
		loading?: boolean;
		onConfirm: () => void | Promise<void>;
		onDismiss: () => void | Promise<void>;
	}

	let {
		open,
		message,
		equipmentName = null,
		loading = false,
		onConfirm,
		onDismiss
	}: Props = $props();

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && open && !loading) {
			onDismiss();
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open}
	<div
		class="fixed inset-0 z-[70] flex items-center justify-center p-4"
		role="dialog"
		aria-modal="true"
		aria-labelledby="brewlink-interaction-modal-title"
		data-testid="brewlink-interaction-modal"
	>
		<!-- Backdrop -->
		<div
			class="fixed inset-0 bg-black/75 backdrop-blur-sm transition-opacity"
			onclick={() => {
				if (!loading) onDismiss();
			}}
			aria-hidden="true"
			data-testid="brewlink-interaction-backdrop"
		></div>

		<!-- Dialog Panel -->
		<div
			class="relative z-10 max-h-[90vh] w-full max-w-md overflow-hidden rounded-3xl border border-amber-500/40 bg-white p-6 shadow-2xl transition-all dark:border-amber-500/30 dark:bg-zinc-950"
		>
			<!-- Top Accent Glow -->
			<div
				class="pointer-events-none absolute -top-20 left-1/2 -z-10 h-40 w-72 -translate-x-1/2 rounded-full bg-amber-500/20 blur-3xl dark:bg-amber-500/15"
			></div>

			<!-- Header -->
			<div class="flex items-start justify-between gap-4">
				<div class="flex items-center gap-3">
					<div
						class="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
					>
						<AlertTriangle class="h-6 w-6 animate-bounce" />
					</div>
					<div>
						<h2
							id="brewlink-interaction-modal-title"
							class="text-base font-bold text-zinc-900 dark:text-zinc-100"
						>
							{t('equipment.brewlink_action_required')}
						</h2>
						{#if equipmentName}
							<div class="mt-0.5 flex items-center gap-1.5">
								<Radio class="h-3 w-3 text-sky-500" />
								<span class="text-xs font-semibold text-zinc-600 dark:text-zinc-400">
									{equipmentName}
								</span>
							</div>
						{/if}
					</div>
				</div>

				<button
					type="button"
					onclick={onDismiss}
					disabled={loading}
					aria-label={t('common.close_dialog')}
					class="rounded-lg p-1 text-zinc-400 transition hover:bg-zinc-100 hover:text-zinc-600 disabled:opacity-50 dark:hover:bg-zinc-800 dark:hover:text-zinc-300"
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<!-- Prompt Body -->
			<div class="mt-5 space-y-3">
				<div
					class="rounded-2xl border border-amber-500/30 bg-amber-500/10 p-4 text-center dark:bg-amber-500/15"
					data-testid="brewlink-interaction-prompt-box"
				>
					<span
						class="block text-lg font-extrabold tracking-tight text-amber-950 dark:text-amber-200"
						data-testid="brewlink-interaction-prompt-message"
					>
						{message || t('equipment.brewlink_interaction_alert')}
					</span>
				</div>

				<p class="text-center text-xs text-zinc-500 dark:text-zinc-400">
					{t('equipment.brewlink_action_hint')}
				</p>
			</div>

			<!-- Actions (YES / NO) -->
			<div class="mt-6 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
				<button
					type="button"
					onclick={onDismiss}
					disabled={loading}
					class="inline-flex min-h-[44px] items-center justify-center rounded-xl border border-zinc-200 bg-white px-4 py-2 text-xs font-semibold text-zinc-700 transition hover:bg-zinc-100 active:scale-95 disabled:opacity-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800"
					data-testid="brewlink-interaction-no-btn"
				>
					{t('common.no')} — {t('equipment.brewlink_dismiss_alert')}
				</button>

				<button
					type="button"
					onclick={onConfirm}
					disabled={loading}
					class="inline-flex min-h-[44px] items-center justify-center gap-2 rounded-xl bg-amber-600 px-5 py-2 text-xs font-bold text-white shadow-lg shadow-amber-600/20 transition hover:bg-amber-500 active:scale-95 disabled:opacity-50 dark:bg-amber-500 dark:text-zinc-950 dark:hover:bg-amber-400"
					data-testid="brewlink-interaction-yes-btn"
				>
					{#if loading}
						<Loader2 class="h-4 w-4 animate-spin" />
					{/if}
					{t('common.yes')} — {t('equipment.brewlink_ack_prompt')}
				</button>
			</div>
		</div>
	</div>
{/if}
