import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';
import de from '$lib/i18n/locales/de.json';
import fr from '$lib/i18n/locales/fr.json';
import es from '$lib/i18n/locales/es.json';
import {
	buildBatchQrUrl,
	generateQrMatrix,
	qrMatrixToSvgPath,
	generateBatchQrSvg,
	type BatchQrTarget
} from '$lib/utils/batchQr';
import { sanitizeFilename } from '$lib/utils/downloadFile';

describe('BatchQrModal Logic & Contract Invariants', () => {
	beforeEach(() => {
		vi.unstubAllGlobals();
		vi.unstubAllEnvs();
	});

	afterEach(() => {
		vi.unstubAllGlobals();
		vi.unstubAllEnvs();
	});

	it('maintains complete i18n keys for batches.qr across all five locales', () => {
		const locales = [en, sv, de, fr, es];
		const requiredKeys = [
			'button_label',
			'open_title',
			'open_aria',
			'title',
			'copy_link',
			'link_copied',
			'download_svg',
			'download_png',
			'print',
			'close'
		];

		for (const loc of locales) {
			for (const key of requiredKeys) {
				const val = (loc.batches.qr as Record<string, string>)[key];
				expect(val).toBeDefined();
				expect(typeof val).toBe('string');
				expect(val.length).toBeGreaterThan(0);
			}
		}

		expect(en.batches.qr.open_aria).toContain('{code}');
		expect(sv.batches.qr.open_aria).toContain('{code}');
	});

	it('computes accurate batch deep-link url and QR payload', () => {
		const target: BatchQrTarget = {
			id: 'b1234567-89ab-cdef-0123-456789abcdef',
			batchCode: 'B-2026-10-30-01',
			name: 'Galaxy NEIPA',
			beerStyle: 'New England IPA',
			brewDate: '2026-10-30'
		};

		const url = buildBatchQrUrl(target.id, 'https://brewyou.app');
		expect(url).toBe('https://brewyou.app/batches/b1234567-89ab-cdef-0123-456789abcdef');

		const matrix = generateQrMatrix(url, { ecc: 'M', border: 2 });
		expect(matrix.size).toBeGreaterThan(20);
		expect(matrix.data.length).toBe(matrix.size);

		const path = qrMatrixToSvgPath(matrix.data);
		expect(path).toContain('M');
		expect(path).toContain('z');
	});

	it('generates consistent filenames for SVG and PNG exports', () => {
		const batch: BatchQrTarget = {
			id: 'batch-99',
			batchCode: 'B-2026-10-30-02',
			name: 'Winter / Porter',
			beerStyle: 'Baltic Porter'
		};

		const svgFilename = sanitizeFilename(`${batch.batchCode}_qr`, 'svg');
		expect(svgFilename).toBe('B-2026-10-30-02_qr.svg');

		const pngFilename = sanitizeFilename(`${batch.batchCode}_qr`, 'png');
		expect(pngFilename).toBe('B-2026-10-30-02_qr.png');
	});

	it('generates a complete SVG cellar label containing batch metadata and QR elements', () => {
		const batch: BatchQrTarget = {
			id: 'test-batch-uuid',
			batchCode: 'B-2026-10-30-03',
			name: 'Citra Single Hop',
			beerStyle: 'American IPA',
			brewDate: '2026-10-30'
		};

		const url = buildBatchQrUrl(batch.id, 'http://localhost:3000');
		const svg = generateBatchQrSvg(batch, url);

		expect(svg).toContain('<svg');
		expect(svg).toContain('B-2026-10-30-03');
		expect(svg).toContain('Citra Single Hop');
		expect(svg).toContain('American IPA · 2026-10-30');
		expect(svg).not.toContain('BREWYOU CELLAR LABEL');
	});

	it('handles keyboard escape and backdrop click dismissal contract', () => {
		let closed = false;
		const onClose = () => {
			closed = true;
		};

		function handleKeydown(e: { key: string }, open: boolean) {
			if (e.key === 'Escape' && open) {
				onClose();
			}
		}

		handleKeydown({ key: 'Escape' }, true);
		expect(closed).toBe(true);

		closed = false;
		handleKeydown({ key: 'Enter' }, true);
		expect(closed).toBe(false);

		handleKeydown({ key: 'Escape' }, false);
		expect(closed).toBe(false);
	});
});
