<script lang="ts">
	import { api, ApiClientError, resolveApiUrl } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import {
		type RecipeLabelDto,
		type LabelCropDto,
		type DisplayOrientation,
		type UpdateRecipeLabelSettingsRequest,
		getLabelImageUrl
	} from '$lib/types/api';
	import { X, Loader2, Check, SlidersHorizontal, RotateCcw } from '@lucide/svelte';

	interface Props {
		open: boolean;
		recipeId: string;
		label: RecipeLabelDto;
		onClose: () => void;
		onSaved: (updated: RecipeLabelDto) => void;
	}

	let { open, recipeId, label, onClose, onSaved }: Props = $props();

	type ActiveCropTab = 'bottle' | 'round' | 'rect';
	let activeTab = $state<ActiveCropTab>('bottle');
	let isSaving = $state(false);
	let errorMessage = $state<string | null>(null);

	// Working copies of crops & settings
	let bottleCrop = $state<LabelCropDto>({ x: 0, y: 0, width: 1, height: 1 });
	let roundCrop = $state<LabelCropDto>({ x: 0, y: 0, width: 1, height: 1 });
	let rectCrop = $state<LabelCropDto>({ x: 0, y: 0, width: 1, height: 1 });
	let rectOrientation = $state<DisplayOrientation>('Landscape');
	let bottleWidthMm = $state<number>(90);
	let bottleHeightMm = $state<number>(120);

	$effect(() => {
		if (open && label) {
			bottleCrop = label.bottleCrop ? { ...label.bottleCrop } : { x: 0, y: 0, width: 1, height: 1 };
			roundCrop = label.roundCrop ? { ...label.roundCrop } : { x: 0, y: 0, width: 1, height: 1 };
			rectCrop = label.rectCrop ? { ...label.rectCrop } : { x: 0, y: 0, width: 1, height: 1 };
			rectOrientation = label.rectOrientation ?? 'Landscape';
			bottleWidthMm = label.bottleWidthMm ?? 90;
			bottleHeightMm = label.bottleHeightMm ?? 120;
			errorMessage = null;
		}
	});

	function getCurrentCrop(): LabelCropDto {
		if (activeTab === 'bottle') return bottleCrop;
		if (activeTab === 'round') return roundCrop;
		return rectCrop;
	}

	function updateCropOffset(axis: 'x' | 'y', percentValue: number) {
		const decimal = Math.max(0, Math.min(1, percentValue / 100));
		if (activeTab === 'bottle') {
			bottleCrop = { ...bottleCrop, [axis]: decimal };
		} else if (activeTab === 'round') {
			roundCrop = { ...roundCrop, [axis]: decimal };
		} else {
			rectCrop = { ...rectCrop, [axis]: decimal };
		}
	}

	function updateCropZoom(zoomPercent: number) {
		// Zoom 100% means width=1, zoom 200% means width=0.5
		const widthRatio = Math.max(0.1, Math.min(1, 100 / zoomPercent));
		const heightRatio = widthRatio;
		if (activeTab === 'bottle') {
			bottleCrop = { ...bottleCrop, width: widthRatio, height: heightRatio };
		} else if (activeTab === 'round') {
			roundCrop = { ...roundCrop, width: widthRatio, height: heightRatio };
		} else {
			rectCrop = { ...rectCrop, width: widthRatio, height: heightRatio };
		}
	}

	function handleResetCurrentCrop() {
		if (activeTab === 'bottle') {
			bottleCrop = { x: 0, y: 0, width: 1, height: 1 };
		} else if (activeTab === 'round') {
			roundCrop = { x: 0, y: 0, width: 1, height: 1 };
		} else if (activeTab === 'rect') {
			rectCrop = { x: 0, y: 0, width: 1, height: 1 };
		}
	}

	async function handleSave() {
		isSaving = true;
		errorMessage = null;
		try {
			const req: UpdateRecipeLabelSettingsRequest = {
				bottleCrop,
				roundCrop,
				rectCrop,
				rectOrientation,
				bottleWidthMm,
				bottleHeightMm
			};
			const updated = await api.recipes.label.updateSettings(recipeId, req);
			onSaved(updated);
			onClose();
		} catch (err) {
			if (err instanceof ApiClientError) {
				errorMessage = err.message;
			} else {
				errorMessage = err instanceof Error ? err.message : t('common.error_generic');
			}
		} finally {
			isSaving = false;
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && open) {
			onClose();
		}
	}

	let currentCrop = $derived(getCurrentCrop());
	let originalUrl = $derived(
		getLabelImageUrl(label.imageUrls, 'original') ||
			getLabelImageUrl(label.imageUrls, 'bottle') ||
			getLabelImageUrl(label.imageUrls, 'card')
	);
	let currentZoom = $derived(Math.round(100 / Math.max(0.1, currentCrop.width)));
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4 sm:p-6"
		role="dialog"
		aria-modal="true"
		aria-labelledby="crop-editor-title"
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
			class="glass-panel relative flex max-h-[90vh] w-full max-w-3xl flex-col overflow-hidden rounded-3xl border border-zinc-200/80 shadow-2xl dark:border-white/[0.08]"
		>
			<!-- Header -->
			<div
				class="flex items-center justify-between border-b border-zinc-200/80 px-6 py-4 dark:border-white/[0.08]"
			>
				<div class="flex items-center gap-3">
					<div
						class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-500"
					>
						<SlidersHorizontal class="h-5 w-5" />
					</div>
					<div>
						<h2
							id="crop-editor-title"
							class="font-editorial text-lg font-bold text-zinc-900 dark:text-white"
						>
							{t('labels.adjust_crops_title')}
						</h2>
						<p class="text-xs text-zinc-500 dark:text-zinc-400">
							{t('labels.adjust_crops_desc')}
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

			<!-- Tabs -->
			<div
				class="flex border-b border-zinc-200/80 bg-zinc-50/50 px-6 pt-2 dark:border-white/[0.08] dark:bg-zinc-900/40"
			>
				<button
					type="button"
					onclick={() => (activeTab = 'bottle')}
					class="border-b-2 px-4 py-2 text-xs font-semibold transition-colors {activeTab ===
					'bottle'
						? 'border-amber-500 text-amber-600 dark:text-amber-400'
						: 'border-transparent text-zinc-500 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-white'}"
				>
					{t('labels.variant_bottle')}
				</button>
				<button
					type="button"
					onclick={() => (activeTab = 'round')}
					class="border-b-2 px-4 py-2 text-xs font-semibold transition-colors {activeTab === 'round'
						? 'border-amber-500 text-amber-600 dark:text-amber-400'
						: 'border-transparent text-zinc-500 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-white'}"
				>
					{t('labels.variant_round')}
				</button>
				<button
					type="button"
					onclick={() => (activeTab = 'rect')}
					class="border-b-2 px-4 py-2 text-xs font-semibold transition-colors {activeTab === 'rect'
						? 'border-amber-500 text-amber-600 dark:text-amber-400'
						: 'border-transparent text-zinc-500 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-white'}"
				>
					{t('labels.variant_rect')}
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

				<div class="grid grid-cols-1 gap-6 md:grid-cols-2">
					<!-- Preview Graphic with Simulated Viewport Mask -->
					<div
						class="flex flex-col items-center justify-center rounded-2xl border border-zinc-200/80 bg-zinc-950 p-4 dark:border-white/5"
					>
						<div
							class="relative overflow-hidden border border-zinc-800 bg-zinc-900 shadow-inner {activeTab ===
							'round'
								? 'h-56 w-56 rounded-full'
								: activeTab === 'bottle'
									? 'h-64 w-44 rounded-xl'
									: rectOrientation === 'Portrait'
										? 'h-64 w-40 rounded-xl'
										: 'h-40 w-64 rounded-xl'}"
						>
							{#if originalUrl}
								<img
									src={resolveApiUrl(originalUrl)}
									alt="Source preview"
									class="absolute h-full w-full object-cover transition-all"
									style="object-position: {Math.round(currentCrop.x * 100)}% {Math.round(
										currentCrop.y * 100
									)}%; transform: scale({(currentZoom / 100).toFixed(2)});"
								/>
							{/if}
						</div>
						<span class="mt-3 text-center text-[11px] text-zinc-400">
							{activeTab === 'round'
								? t('labels.preview_round_desc')
								: activeTab === 'bottle'
									? t('labels.preview_bottle_desc')
									: t('labels.preview_rect_desc')}
						</span>
					</div>

					<!-- Crop Controls -->
					<div class="space-y-4">
						<div class="flex items-center justify-between">
							<span class="text-xs font-semibold text-zinc-700 dark:text-zinc-300">
								{t('labels.position_controls')}
							</span>
							<button
								type="button"
								onclick={handleResetCurrentCrop}
								class="flex items-center gap-1 text-[11px] font-medium text-amber-600 hover:text-amber-500 dark:text-amber-400"
							>
								<RotateCcw class="h-3 w-3" />
								{t('labels.reset_crop')}
							</button>
						</div>

						<!-- Horizontal Position (Center X) -->
						<div class="space-y-1">
							<div class="flex justify-between text-xs text-zinc-500">
								<span>{t('labels.horizontal_offset')}</span>
								<span class="font-mono">{Math.round(currentCrop.x * 100)}%</span>
							</div>
							<input
								type="range"
								min="0"
								max="100"
								value={Math.round(currentCrop.x * 100)}
								oninput={(e) => updateCropOffset('x', Number((e.target as HTMLInputElement).value))}
								class="h-2 w-full cursor-pointer accent-amber-500"
							/>
						</div>

						<!-- Vertical Position (Center Y) -->
						<div class="space-y-1">
							<div class="flex justify-between text-xs text-zinc-500">
								<span>{t('labels.vertical_offset')}</span>
								<span class="font-mono">{Math.round(currentCrop.y * 100)}%</span>
							</div>
							<input
								type="range"
								min="0"
								max="100"
								value={Math.round(currentCrop.y * 100)}
								oninput={(e) => updateCropOffset('y', Number((e.target as HTMLInputElement).value))}
								class="h-2 w-full cursor-pointer accent-amber-500"
							/>
						</div>

						<!-- Zoom / Scale -->
						<div class="space-y-1">
							<div class="flex justify-between text-xs text-zinc-500">
								<span>{t('labels.crop_zoom')}</span>
								<span class="font-mono">{currentZoom}%</span>
							</div>
							<input
								type="range"
								min="100"
								max="250"
								value={currentZoom}
								oninput={(e) => updateCropZoom(Number((e.target as HTMLInputElement).value))}
								class="h-2 w-full cursor-pointer accent-amber-500"
							/>
						</div>

						<!-- Rectangular Orientation (Only in Rect tab) -->
						{#if activeTab === 'rect'}
							<div class="space-y-1.5 border-t border-zinc-200/80 pt-2 dark:border-white/5">
								<span class="text-xs font-semibold text-zinc-700 dark:text-zinc-300">
									Orientation
								</span>
								<div class="grid grid-cols-2 gap-2">
									<button
										type="button"
										onclick={() => (rectOrientation = 'Landscape')}
										class="rounded-xl border px-3 py-2 text-xs font-medium transition-colors {rectOrientation ===
										'Landscape'
											? 'border-amber-500 bg-amber-500/10 font-bold text-amber-700 dark:text-amber-400'
											: 'border-zinc-200 bg-white text-zinc-600 hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300'}"
									>
										Landscape (16:9)
									</button>
									<button
										type="button"
										onclick={() => (rectOrientation = 'Portrait')}
										class="rounded-xl border px-3 py-2 text-xs font-medium transition-colors {rectOrientation ===
										'Portrait'
											? 'border-amber-500 bg-amber-500/10 font-bold text-amber-700 dark:text-amber-400'
											: 'border-zinc-200 bg-white text-zinc-600 hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300'}"
									>
										Portrait (9:16)
									</button>
								</div>
							</div>
						{/if}

						<!-- Bottle Dimensions (Only in Bottle tab) -->
						{#if activeTab === 'bottle'}
							<div
								class="grid grid-cols-2 gap-3 border-t border-zinc-200/80 pt-2 dark:border-white/5"
							>
								<div class="space-y-1">
									<label for="bottle-w-input" class="text-xs text-zinc-500">
										{t('labels.width_mm')}
									</label>
									<input
										id="bottle-w-input"
										type="number"
										min="20"
										max="300"
										bind:value={bottleWidthMm}
										class="w-full rounded-xl border border-zinc-200 bg-white px-3 py-1.5 font-mono text-xs text-zinc-900 dark:border-zinc-800 dark:bg-zinc-900 dark:text-white"
									/>
								</div>
								<div class="space-y-1">
									<label for="bottle-h-input" class="text-xs text-zinc-500">
										{t('labels.height_mm')}
									</label>
									<input
										id="bottle-h-input"
										type="number"
										min="20"
										max="300"
										bind:value={bottleHeightMm}
										class="w-full rounded-xl border border-zinc-200 bg-white px-3 py-1.5 font-mono text-xs text-zinc-900 dark:border-zinc-800 dark:bg-zinc-900 dark:text-white"
									/>
								</div>
							</div>
						{/if}

						<div
							class="rounded-xl border border-zinc-200/80 bg-zinc-50 p-3 text-[11px] leading-relaxed text-zinc-500 dark:border-zinc-800 dark:bg-zinc-900/40 dark:text-zinc-400"
						>
							{t('labels.crop_tip')}
						</div>
					</div>
				</div>

				<!-- Master Brewer Compliance Notice -->
				<div
					class="rounded-xl border border-amber-500/20 bg-amber-500/5 p-4 text-xs text-amber-800 dark:border-amber-500/20 dark:bg-amber-500/10 dark:text-amber-300"
				>
					<strong>{t('labels.bjcp_accuracy_notice')}:</strong>
					{t('labels.bjcp_accuracy_notice_desc')}
				</div>
			</div>

			<!-- Footer -->
			<div
				class="flex items-center justify-end gap-3 border-t border-zinc-200/80 px-6 py-4 dark:border-white/[0.08]"
			>
				<button
					type="button"
					onclick={onClose}
					disabled={isSaving}
					class="rounded-xl border border-zinc-200/80 bg-white px-4 py-2 text-xs font-semibold text-zinc-700 transition-colors hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800"
				>
					{t('common.cancel')}
				</button>
				<button
					type="button"
					onclick={handleSave}
					disabled={isSaving}
					class="flex items-center gap-2 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-5 py-2 text-xs font-bold text-zinc-950 shadow-md transition-all hover:from-amber-400 hover:to-amber-500 disabled:opacity-50"
				>
					{#if isSaving}
						<Loader2 class="h-4 w-4 animate-spin" />
						<span>{t('common.saving')}</span>
					{:else}
						<Check class="h-4 w-4" />
						<span>{t('common.save')}</span>
					{/if}
				</button>
			</div>
		</div>
	</div>
{/if}
