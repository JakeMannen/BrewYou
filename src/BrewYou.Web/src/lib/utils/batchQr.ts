import { encode } from 'uqr';

export interface BatchQrTarget {
	id: string;
	batchCode: string;
	name: string;
	beerStyle?: string | null;
	brewDate?: string | null;
}

export interface BatchQrMatrix {
	size: number;
	data: boolean[][];
}

/**
 * Resolves the public base URL for batch links.
 * Prefers `PUBLIC_APP_URL` (configured for LAN or public domain access),
 * falls back to `window.location.origin` in browser,
 * and defaults to `http://localhost:3000` in SSR / test environments.
 */
export function getAppBaseUrl(): string {
	const metaEnv =
		typeof import.meta !== 'undefined'
			? (import.meta as unknown as { env?: Record<string, string> }).env
			: undefined;

	const configured =
		metaEnv?.PUBLIC_APP_URL ||
		(typeof process !== 'undefined' ? process.env?.PUBLIC_APP_URL : undefined);

	if (configured && typeof configured === 'string' && configured.trim().length > 0) {
		return configured.trim().replace(/\/+$/, '');
	}

	if (typeof window !== 'undefined' && window.location?.origin) {
		return window.location.origin.replace(/\/+$/, '');
	}

	return 'http://localhost:3000';
}

/**
 * Builds the deep-link URL for a batch.
 */
export function buildBatchQrUrl(batchId: string, customBaseUrl?: string): string {
	const base = customBaseUrl ? customBaseUrl.trim().replace(/\/+$/, '') : getAppBaseUrl();
	return `${base}/batches/${encodeURIComponent(batchId.trim())}`;
}

/**
 * Generates the QR boolean module matrix using `uqr`.
 */
export function generateQrMatrix(
	text: string,
	options: { ecc?: 'L' | 'M' | 'Q' | 'H'; border?: number } = {}
): BatchQrMatrix {
	const ecc = options.ecc ?? 'M';
	const border = options.border ?? 2;
	const result = encode(text, { ecc, border });
	return {
		size: result.size,
		data: result.data
	};
}

/**
 * Converts a QR boolean matrix to an optimized SVG path d attribute.
 * Combines consecutive horizontal modules in each row into single rectangles for compact rendering.
 */
export function qrMatrixToSvgPath(matrix: boolean[][]): string {
	const commands: string[] = [];
	const rows = matrix.length;

	for (let y = 0; y < rows; y++) {
		const row = matrix[y];
		let x = 0;
		while (x < row.length) {
			if (row[x]) {
				const startX = x;
				while (x < row.length && row[x]) {
					x++;
				}
				const width = x - startX;
				commands.push(`M${startX},${y}h${width}v1h-${width}z`);
			} else {
				x++;
			}
		}
	}

	return commands.join(' ');
}

function escapeXml(str: string): string {
	return str
		.replace(/&/g, '&amp;')
		.replace(/</g, '&lt;')
		.replace(/>/g, '&gt;')
		.replace(/"/g, '&quot;')
		.replace(/'/g, '&apos;');
}

/**
 * Generates a clean standalone SVG document string with QR code and brewery label typography.
 */
export function generateBatchQrSvg(batch: BatchQrTarget, url: string): string {
	const matrix = generateQrMatrix(url, { ecc: 'M', border: 2 });
	const pathD = qrMatrixToSvgPath(matrix.data);
	const qrSize = matrix.size;

	const cardWidth = 360;
	const cardHeight = 460;
	const qrDisplaySize = 250;
	const qrX = Math.round((cardWidth - qrDisplaySize) / 2);
	const qrY = 75;

	const safeCode = escapeXml(batch.batchCode || 'BATCH');
	const safeName = escapeXml(batch.name || 'Unnamed Batch');
	const safeStyle = escapeXml(batch.beerStyle || 'Craft Beer');
	const safeDate = batch.brewDate ? escapeXml(batch.brewDate) : '';

	return `<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${cardWidth} ${cardHeight}" width="${cardWidth}" height="${cardHeight}">
  <defs>
    <style>
      .bg { fill: #ffffff; }
      .border { stroke: #e4e4e7; stroke-width: 2; rx: 16; }
      .header { font-family: 'JetBrains Mono', monospace; font-size: 22px; font-weight: 700; fill: #d97706; text-anchor: middle; }
      .title { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; font-size: 17px; font-weight: 700; fill: #18181b; text-anchor: middle; }
      .meta { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; font-size: 13px; font-weight: 500; fill: #52525b; text-anchor: middle; }
      .qr-module { fill: #000000; }
    </style>
  </defs>
  <rect class="bg border" x="2" y="2" width="${cardWidth - 4}" height="${cardHeight - 4}" rx="16" />
  <text class="header" x="${cardWidth / 2}" y="48">${safeCode}</text>
  <g transform="translate(${qrX}, ${qrY}) scale(${qrDisplaySize / qrSize})">
    <rect width="${qrSize}" height="${qrSize}" fill="#ffffff" />
    <path class="qr-module" d="${pathD}" />
  </g>
  <text class="title" x="${cardWidth / 2}" y="${qrY + qrDisplaySize + 36}">${safeName}</text>
  <text class="meta" x="${cardWidth / 2}" y="${qrY + qrDisplaySize + 60}">${safeStyle}${safeDate ? ' · ' + safeDate : ''}</text>
</svg>`;
}

/**
 * Renders a label onto an HTML canvas for high-resolution PNG export.
 */
export function renderBatchQrToCanvas(
	canvas: HTMLCanvasElement,
	batch: BatchQrTarget,
	url: string
): void {
	const ctx = canvas.getContext('2d');
	if (!ctx) return;

	const matrix = generateQrMatrix(url, { ecc: 'M', border: 2 });
	const { size, data } = matrix;

	const width = 720;
	const height = 920;
	canvas.width = width;
	canvas.height = height;

	// Background
	ctx.fillStyle = '#ffffff';
	ctx.fillRect(0, 0, width, height);

	// Card Border
	ctx.strokeStyle = '#e4e4e7';
	ctx.lineWidth = 4;
	ctx.strokeRect(10, 10, width - 20, height - 20);

	// Header: Batch Code
	ctx.fillStyle = '#d97706';
	ctx.font = 'bold 42px "JetBrains Mono", monospace';
	ctx.textAlign = 'center';
	ctx.fillText(batch.batchCode || 'BATCH', width / 2, 85);

	// Draw QR Code
	const qrSize = 500;
	const qrX = (width - qrSize) / 2;
	const qrY = 145;
	const moduleSize = qrSize / size;

	ctx.fillStyle = '#000000';
	for (let y = 0; y < size; y++) {
		for (let x = 0; x < size; x++) {
			if (data[y][x]) {
				ctx.fillRect(
					Math.round(qrX + x * moduleSize),
					Math.round(qrY + y * moduleSize),
					Math.ceil(moduleSize),
					Math.ceil(moduleSize)
				);
			}
		}
	}

	// Batch Name
	ctx.fillStyle = '#18181b';
	ctx.font = 'bold 34px -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';
	const maxTextWidth = width - 80;
	let name = batch.name || 'Unnamed Batch';
	if (ctx.measureText(name).width > maxTextWidth) {
		while (name.length > 3 && ctx.measureText(name + '...').width > maxTextWidth) {
			name = name.slice(0, -1);
		}
		name += '...';
	}
	ctx.fillText(name, width / 2, qrY + qrSize + 70);

	// Style & Brew Date
	ctx.fillStyle = '#52525b';
	ctx.font = '500 24px -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';
	const meta = `${batch.beerStyle || 'Craft Beer'}${batch.brewDate ? ' · ' + batch.brewDate : ''}`;
	ctx.fillText(meta, width / 2, qrY + qrSize + 115);
}
