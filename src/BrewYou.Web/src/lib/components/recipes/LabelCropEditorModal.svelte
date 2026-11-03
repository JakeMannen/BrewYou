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

	// Centered aspect-matching defaults with headroom for zooming and panning
	const DEFAULT_BOTTLE_CROP: LabelCropDto = { x: 0.181, y: 0.075, width: 0.6375, height: 0.85 };
	const DEFAULT_ROUND_CROP: LabelCropDto = { x: 0.125, y: 0.125, width: 0.75, height: 0.75 };
	const DEFAULT_RECT_CROP: LabelCropDto = { x: 0.06, y: 0.2525, width: 0.88, height: 0.495 };

	// Working copies of crops & settings
	let bottleCrop = $state<LabelCropDto>({ ...DEFAULT_BOTTLE_CROP });
	let roundCrop = $state<LabelCropDto>({ ...DEFAULT_ROUND_CROP });
	let rectCrop = $state<LabelCropDto>({ ...DEFAULT_RECT_CROP });
	let rectOrientation = $state<DisplayOrientation>('Landscape');
	let bottleWidthMm = $state<number>(90);
	let bottleHeightMm = $state<number>(120);

	function isDefaultOrUnset(crop?: LabelCropDto | null): boolean {
		if (!crop) return true;
		return (
			crop.x === 0 &&
			crop.y === 0 &&
			(crop.width === 1 || crop.width === 0) &&
			(crop.height === 1 || crop.height === 0)
		);
	}

	$effect(() => {
		if (open && label) {
			bottleCrop =
				label.bottleCrop && !isDefaultOrUnset(label.bottleCrop)
					? { ...label.bottleCrop }
					: { ...DEFAULT_BOTTLE_CROP };
			roundCrop =
				label.roundCrop && !isDefaultOrUnset(label.roundCrop)
					? { ...label.roundCrop }
					: { ...DEFAULT_ROUND_CROP };
			rectCrop =
				label.rectCrop && !isDefaultOrUnset(label.rectCrop)
					? { ...label.rectCrop }
					: { ...DEFAULT_RECT_CROP };
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

	function setCurrentCrop(crop: LabelCropDto) {
		if (activeTab === 'bottle') {
			bottleCrop = crop;
		} else if (activeTab === 'round') {
			roundCrop = crop;
		} else {
			rectCrop = crop;
		}
	}

	function round4(val: number): number {
		return Math.round(val * 10000) / 10000;
	}

	function getTargetAspect(): number {
		if (activeTab === 'round') return 1.0;
		if (activeTab === 'rect') return rectOrientation === 'Landscape' ? 16 / 9 : 9 / 16;
		return Math.max(10, bottleWidthMm) / Math.max(10, bottleHeightMm);
	}

	function getMaxDimensions(): { wMax: number; hMax: number } {
		const targetAspect = getTargetAspect();
		const srcAspect = label.widthPx && label.heightPx ? label.widthPx / label.heightPx : 1.0;
		const r = targetAspect / srcAspect;
		if (r >= 1.0) {
			return { wMax: 1.0, hMax: Math.min(1.0, 1.0 / r) };
		}
		return { wMax: Math.min(1.0, r), hMax: 1.0 };
	}

	// Sliders derived values
	let currentCrop = $derived(getCurrentCrop());
	let maxDims = $derived(getMaxDimensions());

	let currentZoom = $derived.by(() => {
		const k = Math.min(currentCrop.width / maxDims.wMax, currentCrop.height / maxDims.hMax);
		// k = 1.0 => zoom = 50 (most zoomed out)
		// k = 0.75 => zoom = 100 (middle default)
		// k = 0.5 => zoom = 150 (zoomed in)
		const calculated = 50 + (1.0 - Math.min(1.0, Math.max(0.4, k))) * 200;
		return Math.max(50, Math.min(150, Math.round(calculated)));
	});

	let currentHPos = $derived.by(() => {
		const maxShift = 1.0 - currentCrop.width;
		if (maxShift <= 0.001) return 50;
		return Math.max(0, Math.min(100, Math.round((currentCrop.x / maxShift) * 100)));
	});

	let currentVPos = $derived.by(() => {
		const maxShift = 1.0 - currentCrop.height;
		if (maxShift <= 0.001) return 50;
		return Math.max(0, Math.min(100, Math.round((currentCrop.y / maxShift) * 100)));
	});

	function handleZoomChange(newZoom: number) {
		const k = 1.0 - (newZoom - 50) * 0.005;
		const newW = Math.max(0.05, Math.min(1.0, maxDims.wMax * k));
		const newH = Math.max(0.05, Math.min(1.0, maxDims.hMax * k));
		const newX = (currentHPos / 100) * (1.0 - newW);
		const newY = (currentVPos / 100) * (1.0 - newH);

		setCurrentCrop({
			x: round4(newX),
			y: round4(newY),
			width: round4(newW),
			height: round4(newH)
		});
	}

	function handleHPosChange(newHPos: number) {
		const maxShift = 1.0 - currentCrop.width;
		const newX = (newHPos / 100) * maxShift;
		setCurrentCrop({
			...currentCrop,
			x: round4(newX)
		});
	}

	function handleVPosChange(newVPos: number) {
		const maxShift = 1.0 - currentCrop.height;
		const newY = (newVPos / 100) * maxShift;
		setCurrentCrop({
			...currentCrop,
			y: round4(newY)
		});
	}

	function handleResetCurrentCrop() {
		const k = 0.75; // 100% middle zoom
		const newW = Math.max(0.05, Math.min(1.0, maxDims.wMax * k));
		const newH = Math.max(0.05, Math.min(1.0, maxDims.hMax * k));
		const newX = 0.5 * (1.0 - newW);
		const newY = 0.5 * (1.0 - newH);

		setCurrentCrop({
			x: round4(newX),
			y: round4(newY),
			width: round4(newW),
			height: round4(newH)
		});
	}

	function handleOrientationChange(orientation: DisplayOrientation) {
		rectOrientation = orientation;
		const targetAspect = orientation === 'Landscape' ? 16 / 9 : 9 / 16;
		const srcAspect = label.widthPx && label.heightPx ? label.widthPx / label.heightPx : 1.0;
		const r = targetAspect / srcAspect;
		const wMax = r >= 1.0 ? 1.0 : Math.min(1.0, r);
		const hMax = r >= 1.0 ? Math.min(1.0, 1.0 / r) : 1.0;
		const k = 0.75;
		const newW = Math.max(0.05, Math.min(1.0, wMax * k));
		const newH = Math.max(0.05, Math.min(1.0, hMax * k));
		rectCrop = {
			x: round4(0.5 * (1.0 - newW)),
			y: round4(0.5 * (1.0 - newH)),
			width: round4(newW),
			height: round4(newH)
		};
	}

	function sanitizeCrop(c: LabelCropDto): LabelCropDto {
		const w = Math.max(0.05, Math.min(1.0, c.width));
		const h = Math.max(0.05, Math.min(1.0, c.height));
		const x = Math.max(0.0, Math.min(1.0 - w, c.x));
		const y = Math.max(0.0, Math.min(1.0 - h, c.y));
		return {
			x: round4(x),
			y: round4(y),
			width: round4(w),
			height: round4(h)
		};
	}

	async function handleSave() {
		isSaving = true;
		errorMessage = null;
		try {
			const req: UpdateRecipeLabelSettingsRequest = {
				bottleCrop: sanitizeCrop(bottleCrop),
				roundCrop: sanitizeCrop(roundCrop),
				rectCrop: sanitizeCrop(rectCrop),
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

	let originalUrl = $derived(
		getLabelImageUrl(label.imageUrls, 'original') ||
			getLabelImageUrl(label.imageUrls, 'bottle') ||
			getLabelImageUrl(label.imageUrls, 'card')
	);
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
					<!-- Preview Graphic with Mathematically Exact Viewport Mask -->
					<div
						class="flex flex-col items-center justify-center rounded-2xl border border-zinc-200/80 bg-zinc-950 p-4 dark:border-white/5"
					>
						<div
							class="relative overflow-hidden border border-zinc-800 bg-zinc-900 shadow-inner {activeTab ===
							'round'
								? 'h-56 w-56 rounded-full'
								: activeTab === 'bottle'
									? 'h-64 rounded-xl'
									: rectOrientation === 'Portrait'
										? 'h-64 w-36 rounded-xl'
										: 'h-40 w-64 rounded-xl'}"
							style={activeTab === 'bottle'
								? `width: ${Math.round(256 * (Math.max(10, bottleWidthMm) / Math.max(10, bottleHeightMm)))}px; max-width: 240px; min-width: 120px;`
								: ''}
						>
							{#if originalUrl}
								<img
									src={resolveApiUrl(originalUrl)}
									alt="Source preview"
									style="position: absolute; width: {(1 / Math.max(0.001, currentCrop.width)) *
										100}%; height: {(1 / Math.max(0.001, currentCrop.height)) * 100}%; left: {-(
										currentCrop.x / Math.max(0.001, currentCrop.width)
									) * 100}%; top: {-(currentCrop.y / Math.max(0.001, currentCrop.height)) *
										100}%; max-width: none; max-height: none; pointer-events: none;"
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
								<span class="font-mono">{currentHPos}%</span>
							</div>
							<input
								type="range"
								min="0"
								max="100"
								value={currentHPos}
								oninput={(e) => handleHPosChange(Number((e.target as HTMLInputElement).value))}
								class="h-2 w-full cursor-pointer accent-amber-500"
							/>
						</div>

						<!-- Vertical Position (Center Y) -->
						<div class="space-y-1">
							<div class="flex justify-between text-xs text-zinc-500">
								<span>{t('labels.vertical_offset')}</span>
								<span class="font-mono">{currentVPos}%</span>
							</div>
							<input
								type="range"
								min="0"
								max="100"
								value={currentVPos}
								oninput={(e) => handleVPosChange(Number((e.target as HTMLInputElement).value))}
								class="h-2 w-full cursor-pointer accent-amber-500"
							/>
						</div>

						<!-- Zoom / Scale (50% to 150%, 100% middle default allows zooming out down to 50%) -->
						<div class="space-y-1">
							<div class="flex justify-between text-xs text-zinc-500">
								<span>{t('labels.crop_zoom')}</span>
								<span class="font-mono">{currentZoom}%</span>
							</div>
							<input
								type="range"
								min="50"
								max="150"
								step="1"
								value={currentZoom}
								oninput={(e) => handleZoomChange(Number((e.target as HTMLInputElement).value))}
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
										onclick={() => handleOrientationChange('Landscape')}
										class="rounded-xl border px-3 py-2 text-xs font-medium transition-colors {rectOrientation ===
										'Landscape'
											? 'border-amber-500 bg-amber-500/10 font-bold text-amber-700 dark:text-amber-400'
											: 'border-zinc-200 bg-white text-zinc-600 hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300'}"
									>
										Landscape (16:9)
									</button>
									<button
										type="button"
										onclick={() => handleOrientationChange('Portrait')}
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
