<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import {
		type BatchQrTarget,
		buildBatchQrUrl,
		generateQrMatrix,
		qrMatrixToSvgPath,
		generateBatchQrSvg,
		renderBatchQrToCanvas
	} from '$lib/utils/batchQr';
	import {
		sanitizeFilename,
		triggerFileDownload,
		triggerBlobDownload
	} from '$lib/utils/downloadFile';
	import { QrCode, X, Copy, Check, Download, Printer } from '@lucide/svelte';

	interface Props {
		open: boolean;
		batch: BatchQrTarget | null;
		onClose: () => void;
	}

	let { open, batch, onClose }: Props = $props();

	let copied = $state(false);
	let copyTimeout: ReturnType<typeof setTimeout> | null = null;
	let canvasRef = $state<HTMLCanvasElement | null>(null);

	let qrUrl = $derived(batch ? buildBatchQrUrl(batch.id) : '');
	let matrix = $derived(qrUrl ? generateQrMatrix(qrUrl, { ecc: 'M', border: 2 }) : null);
	let pathD = $derived(matrix ? qrMatrixToSvgPath(matrix.data) : '');
	let qrSize = $derived(matrix?.size ?? 25);

	async function handleCopy() {
		if (!qrUrl) return;

		try {
			if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
				await navigator.clipboard.writeText(qrUrl);
			}
		} catch {
			// Fallback if clipboard API is restricted
		}

		copied = true;
		if (copyTimeout) clearTimeout(copyTimeout);
		copyTimeout = setTimeout(() => {
			copied = false;
		}, 2000);
	}

	function handleDownloadSvg() {
		if (!batch || !qrUrl) return;
		const svgContent = generateBatchQrSvg(batch, qrUrl);
		const filename = sanitizeFilename(`${batch.batchCode || 'batch'}_qr`, 'svg');
		triggerFileDownload(svgContent, filename, 'image/svg+xml;charset=utf-8');
	}

	function handleDownloadPng() {
		if (!batch || !qrUrl) return;

		let canvas = canvasRef;
		if (!canvas && typeof document !== 'undefined') {
			canvas = document.createElement('canvas');
		}
		if (!canvas) return;

		renderBatchQrToCanvas(canvas, batch, qrUrl);

		if (canvas.toBlob) {
			canvas.toBlob((blob) => {
				if (blob) {
					const filename = sanitizeFilename(`${batch?.batchCode || 'batch'}_qr`, 'png');
					triggerBlobDownload(blob, filename);
				}
			}, 'image/png');
		}
	}

	function handlePrint() {
		if (typeof window !== 'undefined') {
			window.print();
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && open) {
			onClose();
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open && batch}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4 print:p-0"
		role="dialog"
		aria-modal="true"
		aria-labelledby="batch-qr-modal-title"
		data-testid="batch-qr-modal"
	>
		<!-- Backdrop (hidden when printing) -->
		<div
			class="fixed inset-0 bg-black/70 backdrop-blur-sm transition-opacity print:hidden"
			onclick={onClose}
			aria-hidden="true"
			data-testid="batch-qr-backdrop"
		></div>

		<!-- Dialog Panel -->
		<div
			class="relative z-10 max-h-[92vh] w-full max-w-lg overflow-y-auto rounded-3xl border border-zinc-200 bg-white p-5 shadow-2xl transition-all sm:p-6 dark:border-zinc-800 dark:bg-zinc-950 print:border-none print:p-0 print:shadow-none"
		>
			<!-- Header (hidden when printing) -->
			<div
				class="flex items-start justify-between border-b border-zinc-200 pb-4 dark:border-zinc-800 print:hidden"
			>
				<div class="flex items-center gap-3">
					<div
						class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
					>
						<QrCode class="h-5 w-5" />
					</div>
					<div>
						<h2 id="batch-qr-modal-title" class="text-lg font-bold text-zinc-900 dark:text-white">
							{t('batches.qr.title')}
						</h2>
					</div>
				</div>

				<button
					type="button"
					onclick={onClose}
					class="rounded-lg p-2 text-zinc-400 hover:bg-zinc-100 hover:text-zinc-600 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('batches.qr.close')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<!-- Modal Body -->
			<div class="mt-5 space-y-4">
				<!-- High-contrast Physical Cellar Label Card (always white for optical scanning & printing) -->
				<div
					class="print-label-card mx-auto flex w-full max-w-xs flex-col items-center rounded-2xl border border-zinc-200 bg-white p-5 shadow-sm sm:max-w-sm"
				>
					<!-- Batch Code Header -->
					<div class="text-center">
						<span class="block font-mono text-xl font-bold tracking-tight text-amber-600">
							{batch.batchCode}
						</span>
					</div>

					<!-- QR Code Vector (Scalable & Crisp) -->
					<div class="my-4 rounded-xl bg-white p-2">
						<svg
							viewBox="0 0 {qrSize} {qrSize}"
							class="h-52 w-52 sm:h-60 sm:w-60"
							role="img"
							aria-label="QR Code"
							data-testid="batch-qr-code"
							data-qr-payload={qrUrl}
						>
							<rect width={qrSize} height={qrSize} fill="#ffffff" />
							<path d={pathD} fill="#000000" />
						</svg>
					</div>

					<!-- Caption Information -->
					<div class="w-full space-y-1 text-center">
						<h3 class="truncate text-base font-bold text-zinc-900">
							{batch.name}
						</h3>
						<p class="truncate text-xs font-medium text-zinc-500">
							{batch.beerStyle || 'Craft Beer'}
							{#if batch.brewDate}
								<span class="mx-1">•</span>
								<span>{batch.brewDate}</span>
							{/if}
						</p>
					</div>
				</div>

				<!-- Hidden Canvas for PNG Generation -->
				<canvas bind:this={canvasRef} class="hidden" aria-hidden="true"></canvas>

				<!-- URL Bar with Copy Button (hidden when printing) -->
				<div
					class="flex items-center gap-2 rounded-xl border border-zinc-200/80 bg-zinc-100/60 p-2 dark:border-white/10 dark:bg-zinc-900/80 print:hidden"
				>
					<span class="truncate font-mono text-xs text-zinc-600 select-all dark:text-zinc-300">
						{qrUrl}
					</span>
					<button
						type="button"
						onclick={handleCopy}
						data-testid="copy-qr-link-btn"
						class="ml-auto flex shrink-0 items-center gap-1.5 rounded-lg bg-white px-2.5 py-1 text-xs font-semibold text-zinc-900 shadow-xs transition-colors hover:bg-zinc-50 dark:bg-zinc-800 dark:text-white dark:hover:bg-zinc-700"
						title={t('batches.qr.copy_link')}
					>
						{#if copied}
							<Check class="h-3.5 w-3.5 text-emerald-500" />
							<span class="text-emerald-600 dark:text-emerald-400"
								>{t('batches.qr.link_copied')}</span
							>
						{:else}
							<Copy class="h-3.5 w-3.5 text-zinc-500 dark:text-zinc-400" />
							<span>{t('batches.qr.copy_link')}</span>
						{/if}
					</button>
				</div>

				<!-- Action Buttons (filling width to match the link) -->
				<div class="grid grid-cols-1 gap-2.5 pt-1 sm:grid-cols-3 print:hidden">
					<button
						type="button"
						onclick={handleDownloadPng}
						data-testid="download-qr-png-btn"
						class="flex min-h-[40px] w-full items-center justify-center gap-1.5 rounded-xl border border-zinc-200 bg-white px-3 py-2 text-xs font-semibold text-zinc-700 transition-colors hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800"
					>
						<Download class="h-4 w-4 text-zinc-500" />
						<span>{t('batches.qr.download_png')}</span>
					</button>

					<button
						type="button"
						onclick={handleDownloadSvg}
						data-testid="download-qr-svg-btn"
						class="flex min-h-[40px] w-full items-center justify-center gap-1.5 rounded-xl border border-zinc-200 bg-white px-3 py-2 text-xs font-semibold text-zinc-700 transition-colors hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800"
					>
						<Download class="h-4 w-4 text-zinc-500" />
						<span>{t('batches.qr.download_svg')}</span>
					</button>

					<button
						type="button"
						onclick={handlePrint}
						data-testid="print-qr-btn"
						class="flex min-h-[40px] w-full items-center justify-center gap-1.5 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-3.5 py-2 text-xs font-bold text-zinc-950 shadow-xs transition-all hover:from-amber-400 hover:to-amber-500 active:scale-[0.98]"
					>
						<Printer class="h-4 w-4" />
						<span>{t('batches.qr.print')}</span>
					</button>
				</div>
			</div>
		</div>
	</div>
{/if}

<style>
	@media print {
		:global(body *) {
			visibility: hidden !important;
		}
		.print-label-card,
		.print-label-card * {
			visibility: visible !important;
		}
		.print-label-card {
			position: fixed !important;
			left: 50% !important;
			top: 50% !important;
			transform: translate(-50%, -50%) !important;
			width: 320px !important;
			max-width: 320px !important;
			border: 2px solid #e4e4e7 !important;
			box-shadow: none !important;
			background: white !important;
			color: black !important;
			padding: 24px !important;
			border-radius: 16px !important;
		}
	}
</style>
