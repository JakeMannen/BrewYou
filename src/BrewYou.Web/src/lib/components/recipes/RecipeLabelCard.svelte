<script lang="ts">
	import { onMount } from 'svelte';
	import { api, ApiClientError, resolveApiUrl } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import type { RecipeLabelDto } from '$lib/types/api';
	import LabelCropEditorModal from './LabelCropEditorModal.svelte';
	import ConfirmModal from '$lib/components/ui/ConfirmModal.svelte';
	import {
		Image as ImageIcon,
		Upload,
		Printer,
		SlidersHorizontal,
		Trash2,
		Loader2,
		Sparkles,
		CheckCircle2
	} from '@lucide/svelte';

	interface Props {
		recipeId: string;
		isOwner: boolean;
		onLabelUpdated?: (label: RecipeLabelDto) => void;
	}

	let { recipeId, isOwner, onLabelUpdated }: Props = $props();

	let label = $state<RecipeLabelDto | null>(null);
	let loading = $state(true);
	let uploading = $state(false);
	let resetting = $state(false);
	let errorMessage = $state<string | null>(null);
	let successMessage = $state<string | null>(null);

	let showCropModal = $state(false);
	let showResetConfirm = $state(false);
	let fileInputRef = $state<HTMLInputElement | null>(null);

	let activePreviewVariant = $state<'Bottle' | 'RoundDisplay' | 'RectDisplay'>('Bottle');

	async function fetchLabel() {
		loading = true;
		errorMessage = null;
		try {
			label = await api.recipes.label.get(recipeId);
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

	onMount(() => {
		fetchLabel();
	});

	async function handleFileSelected(event: Event) {
		const target = event.target as HTMLInputElement;
		const file = target.files?.[0];
		if (!file) return;

		// Client-side file size and type validation
		if (file.size > 8 * 1024 * 1024) {
			errorMessage = t('labels.err_file_too_large');
			target.value = '';
			return;
		}

		const validTypes = ['image/png', 'image/jpeg', 'image/webp'];
		if (!validTypes.includes(file.type)) {
			errorMessage = t('labels.err_invalid_type');
			target.value = '';
			return;
		}

		uploading = true;
		errorMessage = null;
		successMessage = null;

		try {
			const formData = new FormData();
			formData.append('file', file);
			const updated = await api.recipes.label.upload(recipeId, formData);
			label = updated;
			successMessage = t('labels.upload_success');
			onLabelUpdated?.(updated);
		} catch (err) {
			if (err instanceof ApiClientError) {
				errorMessage = err.message;
			} else {
				errorMessage = err instanceof Error ? err.message : t('common.error_generic');
			}
		} finally {
			uploading = false;
			target.value = '';
		}
	}

	async function handleResetToDefault() {
		resetting = true;
		errorMessage = null;
		successMessage = null;
		try {
			await api.recipes.label.delete(recipeId);
			await fetchLabel();
			successMessage = t('labels.reset_success');
			if (label) {
				onLabelUpdated?.(label);
			}
		} catch (err) {
			if (err instanceof ApiClientError) {
				errorMessage = err.message;
			} else {
				errorMessage = err instanceof Error ? err.message : t('common.error_generic');
			}
		} finally {
			resetting = false;
			showResetConfirm = false;
		}
	}

	function handleSavedSettings(updated: RecipeLabelDto) {
		label = updated;
		successMessage = t('labels.settings_saved');
		onLabelUpdated?.(updated);
	}
</script>

<div
	class="glass-panel space-y-5 rounded-2xl border border-zinc-200/80 p-6 dark:border-white/[0.08]"
>
	<!-- Header -->
	<div
		class="flex flex-wrap items-center justify-between gap-3 border-b border-zinc-200/60 pb-4 dark:border-white/5"
	>
		<div class="flex items-center gap-2.5">
			<div
				class="flex h-9 w-9 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-500"
			>
				<ImageIcon class="h-4 w-4" />
			</div>
			<div>
				<h2 class="text-base font-bold text-zinc-900 dark:text-white">
					{t('labels.card_title')}
				</h2>
				<p class="text-xs text-zinc-500 dark:text-zinc-400">
					{t('labels.card_desc')}
				</p>
			</div>
		</div>

		<!-- Status Badge -->
		{#if label}
			<span
				class="inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-semibold {label.isDefault
					? 'bg-zinc-100 text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300'
					: 'bg-amber-500/10 text-amber-800 dark:bg-amber-500/20 dark:text-amber-300'}"
			>
				<Sparkles class="h-3 w-3" />
				{label.isDefault ? t('labels.status_default') : t('labels.status_custom')}
			</span>
		{/if}
	</div>

	<!-- Notifications -->
	{#if errorMessage}
		<div
			class="rounded-xl border border-red-500/20 bg-red-500/10 p-3 text-xs text-red-600 dark:text-red-400"
		>
			{errorMessage}
		</div>
	{/if}
	{#if successMessage}
		<div
			class="flex items-center gap-2 rounded-xl border border-emerald-500/20 bg-emerald-500/10 p-3 text-xs text-emerald-600 dark:text-emerald-400"
		>
			<CheckCircle2 class="h-4 w-4" />
			<span>{successMessage}</span>
		</div>
	{/if}

	{#if loading}
		<div class="flex h-48 items-center justify-center">
			<Loader2 class="h-6 w-6 animate-spin text-amber-500" />
		</div>
	{:else if label}
		<!-- Variant Selector Tabs -->
		<div class="flex gap-2 border-b border-zinc-200/80 pb-2 dark:border-white/5">
			<button
				type="button"
				onclick={() => (activePreviewVariant = 'Bottle')}
				class="rounded-lg px-3 py-1.5 text-xs font-semibold transition-colors {activePreviewVariant ===
				'Bottle'
					? 'bg-amber-500/15 text-amber-800 dark:bg-amber-500/20 dark:text-amber-300'
					: 'text-zinc-500 hover:bg-zinc-100 dark:text-zinc-400 dark:hover:bg-zinc-800'}"
			>
				{t('labels.variant_bottle')}
			</button>
			<button
				type="button"
				onclick={() => (activePreviewVariant = 'RoundDisplay')}
				class="rounded-lg px-3 py-1.5 text-xs font-semibold transition-colors {activePreviewVariant ===
				'RoundDisplay'
					? 'bg-amber-500/15 text-amber-800 dark:bg-amber-500/20 dark:text-amber-300'
					: 'text-zinc-500 hover:bg-zinc-100 dark:text-zinc-400 dark:hover:bg-zinc-800'}"
			>
				{t('labels.variant_round')}
			</button>
			<button
				type="button"
				onclick={() => (activePreviewVariant = 'RectDisplay')}
				class="rounded-lg px-3 py-1.5 text-xs font-semibold transition-colors {activePreviewVariant ===
				'RectDisplay'
					? 'bg-amber-500/15 text-amber-800 dark:bg-amber-500/20 dark:text-amber-300'
					: 'text-zinc-500 hover:bg-zinc-100 dark:text-zinc-400 dark:hover:bg-zinc-800'}"
			>
				{t('labels.variant_rect')}
			</button>
		</div>

		<!-- Interactive Preview Area -->
		<div
			class="flex flex-col items-center justify-center rounded-2xl border border-zinc-200/80 bg-zinc-950 p-6 dark:border-white/5"
		>
			<div
				class="relative overflow-hidden border border-zinc-800 bg-zinc-900 shadow-xl {activePreviewVariant ===
				'RoundDisplay'
					? 'h-60 w-60 rounded-full'
					: activePreviewVariant === 'Bottle'
						? 'h-64 w-44 rounded-xl'
						: 'h-44 w-72 rounded-xl'}"
			>
				<img
					src={resolveApiUrl(
						activePreviewVariant === 'Bottle'
							? (label.imageUrls['Bottle'] ?? '')
							: activePreviewVariant === 'RoundDisplay'
								? (label.imageUrls['Round'] ?? '')
								: (label.imageUrls['Rect'] ?? '')
					)}
					alt="Recipe label preview"
					class="h-full w-full object-cover"
				/>
			</div>
			<p class="mt-3 text-center text-xs text-zinc-400">
				{activePreviewVariant === 'Bottle'
					? t('labels.preview_bottle_desc')
					: activePreviewVariant === 'RoundDisplay'
						? t('labels.preview_round_desc')
						: t('labels.preview_rect_desc')}
			</p>
		</div>

		<!-- Actions Toolbar -->
		<div class="flex flex-wrap items-center justify-between gap-3 pt-2">
			<!-- Print Bottle Labels Button -->
			<a
				href="/recipes/{recipeId}/labels/print"
				class="flex items-center gap-2 rounded-xl border border-zinc-200/80 bg-white px-3.5 py-2 text-xs font-semibold text-zinc-700 shadow-xs transition-colors hover:border-amber-500/40 hover:bg-amber-50 hover:text-amber-700 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-200 dark:hover:bg-zinc-800 dark:hover:text-amber-400"
			>
				<Printer class="h-4 w-4 text-amber-500" />
				<span>{t('labels.print_labels_btn')}</span>
			</a>

			<!-- Owner Management Actions -->
			{#if isOwner}
				<div class="flex flex-wrap items-center gap-2">
					<!-- Hidden File Input -->
					<input
						type="file"
						accept=".png,.jpg,.jpeg,.webp"
						bind:this={fileInputRef}
						onchange={handleFileSelected}
						class="hidden"
					/>

					<button
						type="button"
						onclick={() => fileInputRef?.click()}
						disabled={uploading}
						class="flex items-center gap-2 rounded-xl bg-amber-500/10 px-3.5 py-2 text-xs font-bold text-amber-700 transition-colors hover:bg-amber-500 hover:text-zinc-950 disabled:opacity-50 dark:bg-amber-500/15 dark:text-amber-400 dark:hover:bg-amber-400 dark:hover:text-zinc-950"
					>
						{#if uploading}
							<Loader2 class="h-4 w-4 animate-spin" />
							<span>{t('common.uploading')}</span>
						{:else}
							<Upload class="h-4 w-4" />
							<span>{t('labels.upload_btn')}</span>
						{/if}
					</button>

					<button
						type="button"
						onclick={() => (showCropModal = true)}
						class="flex items-center gap-2 rounded-xl border border-zinc-200/80 bg-white px-3.5 py-2 text-xs font-semibold text-zinc-700 shadow-xs transition-colors hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-200 dark:hover:bg-zinc-800"
					>
						<SlidersHorizontal class="h-4 w-4 text-zinc-500" />
						<span>{t('labels.adjust_crops_btn')}</span>
					</button>

					{#if !label.isDefault}
						<button
							type="button"
							onclick={() => (showResetConfirm = true)}
							disabled={resetting}
							class="flex items-center gap-2 rounded-xl border border-zinc-200/80 bg-white px-3 py-2 text-xs font-semibold text-zinc-500 shadow-xs transition-colors hover:border-red-500/40 hover:bg-red-50 hover:text-red-600 dark:border-zinc-800 dark:bg-zinc-900 dark:hover:bg-red-950/40 dark:hover:text-red-400"
							title={t('labels.reset_to_default_btn')}
							aria-label={t('labels.reset_to_default_btn')}
						>
							<Trash2 class="h-4 w-4" />
						</button>
					{/if}
				</div>
			{/if}
		</div>
	{/if}
</div>

<!-- Crop Editor Modal -->
{#if label}
	<LabelCropEditorModal
		open={showCropModal}
		{recipeId}
		{label}
		onClose={() => (showCropModal = false)}
		onSaved={handleSavedSettings}
	/>
{/if}

<!-- Reset Confirmation Modal -->
<ConfirmModal
	open={showResetConfirm}
	title={t('labels.reset_confirm_title')}
	message={t('labels.reset_confirm_msg')}
	confirmText={t('labels.reset_confirm_btn')}
	cancelText={t('common.cancel')}
	variant="danger"
	onConfirm={handleResetToDefault}
	onClose={() => (showResetConfirm = false)}
/>
