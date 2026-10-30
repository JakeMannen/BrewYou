import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
	getAppBaseUrl,
	buildBatchQrUrl,
	generateQrMatrix,
	qrMatrixToSvgPath,
	generateBatchQrSvg,
	renderBatchQrToCanvas
} from './batchQr';

describe('batchQr utils', () => {
	beforeEach(() => {
		vi.unstubAllGlobals();
		vi.unstubAllEnvs();
	});

	afterEach(() => {
		vi.unstubAllGlobals();
		vi.unstubAllEnvs();
	});

	describe('getAppBaseUrl', () => {
		it('returns http://localhost:3000 by default when window and env are missing', () => {
			vi.stubGlobal('window', undefined);
			expect(getAppBaseUrl()).toBe('http://localhost:3000');
		});

		it('prefers process.env.PUBLIC_APP_URL when present and strips trailing slashes', () => {
			vi.stubEnv('PUBLIC_APP_URL', 'https://craftbrew.example.com///');
			expect(getAppBaseUrl()).toBe('https://craftbrew.example.com');
		});

		it('uses window.location.origin in browser when PUBLIC_APP_URL is not set', () => {
			vi.stubGlobal('window', {
				location: { origin: 'http://192.168.1.50:3000/' }
			});
			expect(getAppBaseUrl()).toBe('http://192.168.1.50:3000');
		});
	});

	describe('buildBatchQrUrl', () => {
		it('builds canonical batch URL using default base URL', () => {
			const batchId = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890';
			const url = buildBatchQrUrl(batchId, 'https://brewyou.local/');
			expect(url).toBe('https://brewyou.local/batches/a1b2c3d4-e5f6-7890-abcd-ef1234567890');
		});

		it('encodes special characters in batchId if any', () => {
			const url = buildBatchQrUrl('batch 101#alpha', 'https://brewyou.local');
			expect(url).toBe('https://brewyou.local/batches/batch%20101%23alpha');
		});
	});

	describe('generateQrMatrix', () => {
		it('generates a valid square matrix with expected border and ecc', () => {
			const matrix = generateQrMatrix('https://example.com/test', { ecc: 'M', border: 2 });
			expect(matrix.size).toBeGreaterThan(20);
			expect(matrix.data.length).toBe(matrix.size);
			expect(matrix.data[0].length).toBe(matrix.size);

			// Border check: with border 2, outer rows should be quiet zones (all false)
			expect(matrix.data[0].every((val) => val === false)).toBe(true);
			expect(matrix.data[1].every((val) => val === false)).toBe(true);
		});
	});

	describe('qrMatrixToSvgPath', () => {
		it('combines consecutive horizontal modules into single rect commands', () => {
			const matrix = [
				[false, true, true, true, false],
				[true, false, true, true, false]
			];
			const path = qrMatrixToSvgPath(matrix);
			// Row 0 has 3 consecutive true from col 1: M1,0h3v1h-3z
			expect(path).toContain('M1,0h3v1h-3z');
			// Row 1 has single true at 0, then 2 consecutive at 2: M0,1h1v1h-1z M2,1h2v1h-2z
			expect(path).toContain('M0,1h1v1h-1z');
			expect(path).toContain('M2,1h2v1h-2z');
		});
	});

	describe('generateBatchQrSvg', () => {
		it('generates standalone SVG document with escaped text and QR path', () => {
			const svg = generateBatchQrSvg(
				{
					id: '123',
					batchCode: 'B-2026-10-30-01',
					name: 'Hazy & Hoppy <Special>',
					beerStyle: 'NEIPA "Citra"',
					brewDate: '2026-10-30'
				},
				'https://brewyou.example.com/batches/123'
			);

			expect(svg).toContain('<?xml version="1.0" encoding="UTF-8"?>');
			expect(svg).toContain('<svg xmlns="http://www.w3.org/2000/svg"');
			expect(svg).toContain('B-2026-10-30-01');
			expect(svg).toContain('Hazy &amp; Hoppy &lt;Special&gt;');
			expect(svg).toContain('NEIPA &quot;Citra&quot;');
			expect(svg).toContain('2026-10-30');
			expect(svg).toContain('class="qr-module"');
		});
	});

	describe('renderBatchQrToCanvas', () => {
		it('draws label elements onto canvas 2D context', () => {
			const fillRectMock = vi.fn();
			const strokeRectMock = vi.fn();
			const fillTextMock = vi.fn();
			const measureTextMock = vi.fn().mockReturnValue({ width: 100 });

			const mockCtx = {
				fillStyle: '',
				strokeStyle: '',
				lineWidth: 0,
				font: '',
				textAlign: '',
				fillRect: fillRectMock,
				strokeRect: strokeRectMock,
				fillText: fillTextMock,
				measureText: measureTextMock
			};

			const mockCanvas = {
				width: 0,
				height: 0,
				getContext: vi.fn().mockReturnValue(mockCtx)
			} as unknown as HTMLCanvasElement;

			renderBatchQrToCanvas(
				mockCanvas,
				{
					id: 'test-id',
					batchCode: 'B-01',
					name: 'Pale Ale',
					beerStyle: 'American Pale Ale',
					brewDate: '2026-10-30'
				},
				'http://localhost:3000/batches/test-id'
			);

			expect(mockCanvas.width).toBe(720);
			expect(mockCanvas.height).toBe(920);
			expect(fillRectMock).toHaveBeenCalled();
			expect(strokeRectMock).toHaveBeenCalled();
			expect(fillTextMock).toHaveBeenCalledWith('B-01', 360, 85);
		});
	});
});
