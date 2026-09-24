<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { AlertTriangle, AlertCircle, X, Loader2, FileEdit } from '@lucide/svelte';

	interface Props {
		open: boolean;
		currentName: string;
		onRename: (newName: string) => void | Promise<void>;
		onCancel: () => void;
		checkConflict?: (name: string) => Promise<boolean> | boolean;
	}

	let { open, currentName, onRename, onCancel, checkConflict }: Props = $props();

	let nameInput = $state('');
	let isSubmitting = $state(false);
	let validationError = $state<string | null>(null);
	let inputEl: HTMLInputElement | null = $state(null);

	$effect(() => {
		if (open) {
			const suffix = t('recipes.copy_suffix');
			nameInput = `${currentName} (${suffix})`;
			validationError = null;
			isSubmitting = false;
			// Focus input after modal renders
			setTimeout(() => {
				inputEl?.focus();
				inputEl?.select();
			}, 50);
		}
	});

	async function handleConfirm() {
		const trimmed = nameInput.trim();
		if (!trimmed) {
			validationError = t('recipes.rename_modal.error_required');
			return;
		}

		isSubmitting = true;
		validationError = null;

		try {
			if (checkConflict) {
				const hasConflict = await checkConflict(trimmed);
				if (hasConflict) {
					validationError = t('recipes.rename_modal.error_conflict');
					isSubmitting = false;
					return;
				}
			}

			await onRename(trimmed);
		} catch (err: unknown) {
			validationError = (err as Error).message || t('recipes.rename_modal.error_conflict');
		} finally {
			isSubmitting = false;
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && open && !isSubmitting) {
			onCancel();
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open}
	<div
		class="fixed inset-0 z-60 flex items-center justify-center p-4"
		role="dialog"
		aria-modal="true"
		aria-labelledby="recipe-rename-modal-title"
		data-testid="recipe-rename-modal"
	>
		<!-- Backdrop -->
		<div
			class="fixed inset-0 bg-black/70 backdrop-blur-sm transition-opacity"
			onclick={() => {
				if (!isSubmitting) onCancel();
			}}
			aria-hidden="true"
			data-testid="recipe-rename-modal-backdrop"
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
						class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
					>
						<AlertTriangle class="h-5 w-5" />
					</div>
					<div>
						<h2
							id="recipe-rename-modal-title"
							class="text-lg font-bold text-zinc-900 dark:text-white"
							data-testid="recipe-rename-modal-title"
						>
							{t('recipes.rename_modal.title')}
						</h2>
						<p class="text-xs text-zinc-500 dark:text-zinc-400">
							{t('recipes.rename_modal.subtitle', { name: currentName })}
						</p>
					</div>
				</div>

				<button
					type="button"
					onclick={() => {
						if (!isSubmitting) onCancel();
					}}
					disabled={isSubmitting}
					class="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 disabled:opacity-50 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('recipes.rename_modal.cancel_button')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<!-- Body -->
			<div class="space-y-4 py-4">
				<p class="text-xs text-zinc-600 dark:text-zinc-400">
					{t('recipes.rename_modal.prompt')}
				</p>

				<div>
					<label
						for="recipe-rename-input"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('recipes.rename_modal.input_label')}
					</label>
					<div class="relative mt-1.5">
						<input
							id="recipe-rename-input"
							bind:this={inputEl}
							type="text"
							bind:value={nameInput}
							disabled={isSubmitting}
							placeholder={t('recipes.rename_modal.input_placeholder')}
							onkeydown={(e) => {
								if (e.key === 'Enter') {
									e.preventDefault();
									handleConfirm();
								}
							}}
							data-testid="recipe-rename-input"
							class="w-full rounded-xl border border-zinc-300 bg-white px-3.5 py-2.5 text-sm text-zinc-900 shadow-xs transition-colors placeholder:text-zinc-400 focus:border-amber-500 focus:ring-2 focus:ring-amber-500/20 focus:outline-hidden disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-900 dark:text-white dark:placeholder:text-zinc-500"
						/>
					</div>
				</div>

				{#if validationError}
					<div
						class="flex items-start gap-2 rounded-xl border border-red-200 bg-red-50 p-3 text-xs text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
						data-testid="recipe-rename-error"
					>
						<AlertCircle class="mt-0.5 h-4 w-4 shrink-0 text-red-500" />
						<span>{validationError}</span>
					</div>
				{/if}
			</div>

			<!-- Footer -->
			<div
				class="flex items-center justify-end gap-3 border-t border-zinc-200 pt-4 dark:border-zinc-800"
			>
				<button
					type="button"
					onclick={onCancel}
					disabled={isSubmitting}
					class="rounded-xl border border-zinc-200 px-4 py-2.5 text-sm font-semibold text-zinc-700 transition-colors hover:bg-zinc-100 disabled:opacity-50 dark:border-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-900"
					data-testid="recipe-rename-cancel-btn"
				>
					{t('recipes.rename_modal.cancel_button')}
				</button>

				<button
					type="button"
					onclick={handleConfirm}
					disabled={isSubmitting || !nameInput.trim()}
					class="flex min-h-[42px] items-center gap-2 rounded-xl bg-amber-500 px-5 py-2.5 text-sm font-bold text-zinc-950 shadow-sm transition-all hover:bg-amber-400 disabled:opacity-50"
					data-testid="recipe-rename-confirm-btn"
				>
					{#if isSubmitting}
						<Loader2 class="h-4 w-4 animate-spin" />
					{:else}
						<FileEdit class="h-4 w-4" />
					{/if}
					<span>{t('recipes.rename_modal.continue_button')}</span>
				</button>
			</div>
		</div>
	</div>
{/if}
