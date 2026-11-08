<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { api } from '$lib/api/client';
	import type { EquipmentDto } from '$lib/types/api';
	import { Cpu, X, Loader2, CheckCircle2, AlertCircle, AlertTriangle } from '@lucide/svelte';
	import ConfirmModal from '$lib/components/ui/ConfirmModal.svelte';

	interface Props {
		open: boolean;
		recipeId: string;
		recipeName: string;
		boilers: EquipmentDto[];
		onClose: () => void;
		onSuccess?: (boilerName: string) => void;
	}

	let { open, recipeId, recipeName, boilers, onClose, onSuccess }: Props = $props();

	let selectedBoilerId = $state<string>('');
	let isSending = $state(false);
	let successMessage = $state<string | null>(null);
	let errorMessage = $state<string | null>(null);
	let showConfirmModal = $state(false);

	$effect(() => {
		if (open) {
			successMessage = null;
			errorMessage = null;
			showConfirmModal = false;
			if (boilers.length > 0 && !selectedBoilerId) {
				selectedBoilerId = boilers[0].id;
			}
		}
	});

	let selectedBoiler = $derived(boilers.find((b) => b.id === selectedBoilerId) || null);

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && open && !isSending && !showConfirmModal) {
			onClose();
		}
	}

	function handleOpenConfirm() {
		if (!selectedBoilerId || !recipeId) return;
		showConfirmModal = true;
	}

	async function handleSend() {
		if (!selectedBoilerId || !recipeId) return;

		isSending = true;
		errorMessage = null;
		successMessage = null;

		try {
			await api.equipment.sendBrewLinkRecipe(selectedBoilerId, { recipeId });
			const boilerName = selectedBoiler?.name || '';
			successMessage = t('recipes.recipe_sent_success', { boiler: boilerName });
			showConfirmModal = false;
			if (onSuccess) {
				onSuccess(boilerName);
			}
			setTimeout(() => {
				onClose();
				successMessage = null;
			}, 1500);
		} catch (err: unknown) {
			showConfirmModal = false;
			const clientErr = err as { status?: number; message?: string } | undefined;
			if (clientErr?.status === 409 || clientErr?.message?.includes('261')) {
				errorMessage = t('recipes.err_equipment_disconnected');
			} else {
				errorMessage = t('recipes.err_send_failed', {
					error: clientErr?.message || 'Request failed'
				});
			}
		} finally {
			isSending = false;
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-xs"
		role="dialog"
		aria-modal="true"
		aria-labelledby="send-to-boiler-modal-title"
	>
		<div
			class="w-full max-w-lg rounded-2xl border border-zinc-200 bg-white p-6 shadow-2xl transition-all dark:border-zinc-800 dark:bg-zinc-900"
		>
			<!-- Header -->
			<div
				class="flex items-start justify-between border-b border-zinc-100 pb-4 dark:border-zinc-800"
			>
				<div class="flex items-center gap-3">
					<div
						class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-sky-500/30 bg-sky-500/10 text-sky-600 dark:text-sky-400"
					>
						<Cpu class="h-5 w-5" />
					</div>
					<div>
						<h2
							id="send-to-boiler-modal-title"
							class="text-base font-bold text-zinc-900 dark:text-white"
						>
							{t('recipes.send_to_boiler_title')}
						</h2>
						<p class="text-xs text-zinc-500 dark:text-zinc-400">
							{t('recipes.send_to_boiler_desc')}
						</p>
					</div>
				</div>

				<button
					type="button"
					onclick={onClose}
					disabled={isSending}
					class="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 disabled:opacity-50 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<!-- Body -->
			<div class="space-y-4 py-4">
				<div
					class="rounded-xl border border-zinc-200/80 bg-zinc-50/60 p-3.5 dark:border-zinc-800 dark:bg-zinc-800/40"
				>
					<div class="text-xs font-semibold text-zinc-500 dark:text-zinc-400">
						{t('recipes.title')}
					</div>
					<div class="mt-0.5 text-sm font-bold text-zinc-900 dark:text-white">
						{recipeName}
					</div>
				</div>

				<!-- Boiler Selection -->
				<div>
					<label
						for="boiler-select"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('recipes.select_target_boiler')}
					</label>
					{#if boilers.length === 1}
						<div
							id="boiler-select"
							class="mt-1.5 flex items-center justify-between rounded-xl border border-zinc-200 bg-zinc-50 px-3.5 py-2.5 text-xs font-semibold text-zinc-800 dark:border-zinc-800 dark:bg-zinc-800 dark:text-zinc-200"
						>
							<span>{boilers[0].name}</span>
							<span
								class="rounded bg-sky-500/10 px-2 py-0.5 text-[10px] font-bold text-sky-600 dark:text-sky-400"
							>
								{t('equipment.brewlink_tag')}
							</span>
						</div>
					{:else}
						<select
							id="boiler-select"
							data-testid="send-to-boiler-select"
							bind:value={selectedBoilerId}
							class="mt-1.5 h-10 w-full rounded-xl border border-zinc-300 bg-white px-3 text-xs font-semibold text-zinc-900 focus:border-sky-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
						>
							{#each boilers as b (b.id)}
								<option value={b.id}>{b.name} ({b.capacityLiters} L)</option>
							{/each}
						</select>
					{/if}
				</div>

				<!-- Overwrite Warning Notice -->
				<div
					class="flex items-start gap-2.5 rounded-xl border border-amber-500/30 bg-amber-500/10 p-3 text-xs text-amber-800 dark:border-amber-500/20 dark:bg-amber-500/15 dark:text-amber-300"
				>
					<AlertTriangle class="mt-0.5 h-4 w-4 shrink-0 text-amber-600 dark:text-amber-400" />
					<p class="leading-relaxed">
						{t('recipes.confirm_transfer_warning')}
					</p>
				</div>

				{#if successMessage}
					<div
						class="flex items-center gap-2 rounded-xl border border-emerald-500/30 bg-emerald-500/10 px-3.5 py-2.5 text-xs font-medium text-emerald-600 dark:text-emerald-400"
						role="status"
					>
						<CheckCircle2 class="h-4 w-4 shrink-0" />
						<span>{successMessage}</span>
					</div>
				{/if}

				{#if errorMessage}
					<div
						class="flex items-center gap-2 rounded-xl border border-red-500/30 bg-red-500/10 px-3.5 py-2.5 text-xs font-medium text-red-600 dark:text-red-400"
						role="alert"
					>
						<AlertCircle class="h-4 w-4 shrink-0" />
						<span>{errorMessage}</span>
					</div>
				{/if}
			</div>

			<!-- Footer -->
			<div
				class="flex items-center justify-end gap-2.5 border-t border-zinc-100 pt-4 dark:border-zinc-800"
			>
				<button
					type="button"
					onclick={onClose}
					disabled={isSending}
					class="rounded-xl border border-zinc-300 px-4 py-2 text-xs font-semibold text-zinc-700 transition hover:bg-zinc-50 disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-700"
				>
					{t('common.cancel')}
				</button>
				<button
					type="button"
					data-testid="send-to-boiler-confirm-btn"
					onclick={handleOpenConfirm}
					disabled={isSending || !selectedBoilerId || !!successMessage}
					class="inline-flex items-center gap-2 rounded-xl bg-sky-600 px-4 py-2 text-xs font-bold text-white shadow-sm transition hover:bg-sky-500 active:scale-95 disabled:opacity-50"
				>
					{#if isSending}
						<Loader2 class="h-3.5 w-3.5 animate-spin" />
						<span>{t('recipes.sending_to_boiler')}</span>
					{:else}
						<Cpu class="h-4 w-4" />
						<span>{t('recipes.send_to_boiler')}</span>
					{/if}
				</button>
			</div>
		</div>
	</div>
{/if}

<ConfirmModal
	open={showConfirmModal}
	title={t('recipes.confirm_transfer_title')}
	message={t('recipes.confirm_transfer_message', {
		recipe: recipeName,
		boiler: selectedBoiler?.name || ''
	})}
	confirmText={t('common.yes')}
	cancelText={t('common.no')}
	confirmTestId="confirm-transfer-yes-btn"
	cancelTestId="confirm-transfer-no-btn"
	variant="warning"
	loading={isSending}
	onConfirm={handleSend}
	onClose={() => {
		if (!isSending) showConfirmModal = false;
	}}
/>
