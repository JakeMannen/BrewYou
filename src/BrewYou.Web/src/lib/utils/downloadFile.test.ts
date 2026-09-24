import { describe, it, expect, vi } from 'vitest';
import { sanitizeFilename, triggerFileDownload } from './downloadFile';

describe('downloadFile utils', () => {
	describe('sanitizeFilename', () => {
		it('returns default fallback when name is empty or not a string', () => {
			expect(sanitizeFilename(null, 'xml')).toBe('recipe.xml');
			expect(sanitizeFilename('', 'json')).toBe('recipe.json');
			expect(sanitizeFilename(undefined, '.xml')).toBe('recipe.xml');
		});

		it('sanitizes illegal filesystem characters and replaces whitespace with underscores', () => {
			const sanitized = sanitizeFilename('My Best / Pale:Ale * "Special" <2026>?', 'xml');
			expect(sanitized).toBe('My_Best___Pale_Ale____Special___2026.xml');
		});

		it('prefixes reserved Windows device names', () => {
			expect(sanitizeFilename('CON', 'xml')).toBe('recipe_CON.xml');
			expect(sanitizeFilename('NUL', 'json')).toBe('recipe_NUL.json');
			expect(sanitizeFilename('AUX', 'xml')).toBe('recipe_AUX.xml');
		});

		it('truncates filename exceeding 80 characters', () => {
			const longName = 'a'.repeat(100);
			const sanitized = sanitizeFilename(longName, 'json');
			const baseName = sanitized.replace('.json', '');
			expect(baseName.length).toBeLessThanOrEqual(80);
		});
	});

	describe('triggerFileDownload', () => {
		it('creates blob and dispatches click event in DOM environment', () => {
			const createObjectURLMock = vi.fn().mockReturnValue('blob:http://localhost/123');
			const revokeObjectURLMock = vi.fn();
			vi.stubGlobal('URL', {
				createObjectURL: createObjectURLMock,
				revokeObjectURL: revokeObjectURLMock
			});

			const clickMock = vi.fn();
			const appendChildMock = vi.fn();
			const removeChildMock = vi.fn();

			const mockAnchor = {
				href: '',
				download: '',
				style: { display: '' },
				click: clickMock,
				parentNode: { removeChild: removeChildMock }
			};

			vi.stubGlobal('window', {});
			vi.stubGlobal('document', {
				createElement: vi.fn().mockReturnValue(mockAnchor),
				body: { appendChild: appendChildMock }
			});

			triggerFileDownload('<xml></xml>', 'recipe.xml', 'application/xml');

			expect(createObjectURLMock).toHaveBeenCalled();
			expect(appendChildMock).toHaveBeenCalledWith(mockAnchor);
			expect(clickMock).toHaveBeenCalled();
		});
	});
});
