import { afterEach, describe, expect, it, vi } from 'vitest';
import { copyTextToClipboard } from './clipboard';

describe('copyTextToClipboard', () => {
	afterEach(() => {
		vi.unstubAllGlobals();
	});

	it('returns false for empty text', async () => {
		expect(await copyTextToClipboard('')).toBe(false);
	});

	it('uses navigator.clipboard.writeText when available', async () => {
		const writeText = vi.fn().mockResolvedValue(undefined);
		vi.stubGlobal('navigator', { clipboard: { writeText } });

		const result = await copyTextToClipboard('secret-token-123');
		expect(result).toBe(true);
		expect(writeText).toHaveBeenCalledWith('secret-token-123');
	});

	it('falls back to document.execCommand("copy") when navigator.clipboard is unavailable on HTTP LAN origins', async () => {
		vi.stubGlobal('navigator', {});

		const appended: unknown[] = [];
		const removed: unknown[] = [];
		const fakeTextarea = {
			value: '',
			style: {} as Record<string, string>,
			parentNode: null as unknown,
			setAttribute: vi.fn(),
			focus: vi.fn(),
			select: vi.fn(),
			setSelectionRange: vi.fn()
		};
		const container = {
			appendChild: vi.fn((node: typeof fakeTextarea) => {
				node.parentNode = container;
				appended.push(node);
			}),
			removeChild: vi.fn((node: typeof fakeTextarea) => {
				node.parentNode = null;
				removed.push(node);
			})
		} as unknown as HTMLElement;

		vi.stubGlobal('HTMLElement', class {});
		vi.stubGlobal('document', {
			activeElement: null,
			createElement: vi.fn(() => fakeTextarea),
			execCommand: vi.fn(() => true),
			body: container
		});

		const result = await copyTextToClipboard('http-lan-token', container);
		expect(result).toBe(true);
		expect(fakeTextarea.value).toBe('http-lan-token');
		expect(fakeTextarea.select).toHaveBeenCalled();
		expect(appended).toHaveLength(1);
		expect(removed).toHaveLength(1);
	});

	it('falls back to execCommand when navigator.clipboard.writeText rejects', async () => {
		vi.stubGlobal('navigator', {
			clipboard: {
				writeText: vi.fn().mockRejectedValue(new Error('NotAllowedError'))
			}
		});

		const fakeTextarea = {
			value: '',
			style: {} as Record<string, string>,
			parentNode: null as unknown,
			setAttribute: vi.fn(),
			focus: vi.fn(),
			select: vi.fn(),
			setSelectionRange: vi.fn()
		};
		const fakeBody = {
			appendChild: vi.fn((node: typeof fakeTextarea) => {
				node.parentNode = fakeBody;
			}),
			removeChild: vi.fn((node: typeof fakeTextarea) => {
				node.parentNode = null;
			})
		};

		vi.stubGlobal('HTMLElement', class {});
		vi.stubGlobal('document', {
			activeElement: null,
			createElement: vi.fn(() => fakeTextarea),
			execCommand: vi.fn(() => true),
			body: fakeBody
		});

		const result = await copyTextToClipboard('fallback-token');
		expect(result).toBe(true);
	});
});
