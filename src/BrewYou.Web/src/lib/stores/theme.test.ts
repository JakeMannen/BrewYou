import { describe, it, expect, beforeEach, vi } from 'vitest';
import { theme } from './theme.svelte';

class LocalStorageMock {
	private store: Record<string, string> = {};
	clear() {
		this.store = {};
	}
	getItem(key: string) {
		return this.store[key] ?? null;
	}
	setItem(key: string, value: string) {
		this.store[key] = String(value);
	}
	removeItem(key: string) {
		delete this.store[key];
	}
}

const mockStorage = new LocalStorageMock();

describe('Theme Store (theme.svelte.ts)', () => {
	let classListMock: Set<string>;
	let attributesMock: Record<string, string>;

	beforeEach(() => {
		vi.restoreAllMocks();
		mockStorage.clear();
		classListMock = new Set<string>();
		attributesMock = {};

		const mockDocElement = {
			classList: {
				add: (cls: string) => classListMock.add(cls),
				remove: (cls: string) => classListMock.delete(cls),
				toggle: (cls: string, force?: boolean) => {
					if (force !== undefined) {
						if (force) classListMock.add(cls);
						else classListMock.delete(cls);
					} else {
						if (classListMock.has(cls)) classListMock.delete(cls);
						else classListMock.add(cls);
					}
					return classListMock.has(cls);
				},
				contains: (cls: string) => classListMock.has(cls)
			},
			setAttribute: (name: string, val: string) => {
				attributesMock[name] = val;
			},
			getAttribute: (name: string) => attributesMock[name] ?? null,
			style: {
				colorScheme: ''
			}
		};

		const matchMediaMock = (query: string) => ({
			matches: query.includes('dark'),
			media: query,
			onchange: null,
			addEventListener: vi.fn(),
			removeEventListener: vi.fn(),
			dispatchEvent: vi.fn()
		});

		Object.defineProperty(globalThis, 'localStorage', {
			value: mockStorage,
			writable: true,
			configurable: true
		});

		Object.defineProperty(globalThis, 'document', {
			value: { documentElement: mockDocElement },
			writable: true,
			configurable: true
		});

		Object.defineProperty(globalThis, 'window', {
			value: {
				matchMedia: matchMediaMock,
				addEventListener: vi.fn()
			},
			writable: true,
			configurable: true
		});
	});

	it('initializes with dark preference when no localStorage entry exists', () => {
		theme.init();
		expect(theme.preference).toBe('dark');
		expect(theme.current).toBe('dark');
		expect(classListMock.has('dark')).toBe(true);
		expect(attributesMock['data-theme']).toBe('dark');
	});

	it('initializes with stored preference if available in localStorage', () => {
		mockStorage.setItem('brewyou-theme', 'light');
		theme.init();
		expect(theme.preference).toBe('light');
		expect(theme.current).toBe('light');
		expect(classListMock.has('dark')).toBe(false);
		expect(attributesMock['data-theme']).toBe('light');
	});

	it('toggles theme between dark and light, persisting to storage', () => {
		theme.setTheme('dark');
		expect(theme.current).toBe('dark');
		expect(mockStorage.getItem('brewyou-theme')).toBe('dark');

		theme.toggle();
		expect(theme.current).toBe('light');
		expect(mockStorage.getItem('brewyou-theme')).toBe('light');
		expect(classListMock.has('dark')).toBe(false);

		theme.toggle();
		expect(theme.current).toBe('dark');
		expect(mockStorage.getItem('brewyou-theme')).toBe('dark');
		expect(classListMock.has('dark')).toBe(true);
	});

	it('explicitly sets theme to system, light, dark, or obsidian', () => {
		theme.setTheme('light');
		expect(theme.current).toBe('light');
		expect(attributesMock['data-theme']).toBe('light');
		expect(classListMock.has('dark')).toBe(false);

		theme.setTheme('dark');
		expect(theme.current).toBe('dark');
		expect(attributesMock['data-theme']).toBe('dark');
		expect(classListMock.has('dark')).toBe(true);

		theme.setTheme('obsidian');
		expect(theme.current).toBe('obsidian');
		expect(attributesMock['data-theme']).toBe('obsidian');
		expect(classListMock.has('dark')).toBe(true);

		theme.setTheme('system');
		expect(theme.preference).toBe('system');
		expect(theme.current).toBe('dark'); // system prefers dark in our mock
	});

	it('initializes with obsidian preference if stored in localStorage', () => {
		mockStorage.setItem('brewyou-theme', 'obsidian');
		theme.init();
		expect(theme.preference).toBe('obsidian');
		expect(theme.current).toBe('obsidian');
		expect(classListMock.has('dark')).toBe(true);
		expect(attributesMock['data-theme']).toBe('obsidian');
	});

	it('responds to media query change and storage event', () => {
		let mediaListener: ((e: { matches: boolean }) => void) | undefined;
		let storageListener: ((e: { key: string; newValue: string }) => void) | undefined;

		const matchMediaMock = (query: string) => ({
			matches: query.includes('dark'),
			media: query,
			onchange: null,
			addEventListener: (_event: string, cb: unknown) => {
				mediaListener = cb as (e: { matches: boolean }) => void;
			},
			removeEventListener: vi.fn(),
			dispatchEvent: vi.fn()
		});

		(window as unknown as { matchMedia: unknown }).matchMedia = matchMediaMock;
		(
			window as unknown as { addEventListener: (event: string, cb: unknown) => void }
		).addEventListener = (event: string, cb: unknown) => {
			if (event === 'storage')
				storageListener = cb as (e: { key: string; newValue: string }) => void;
		};

		theme.setTheme('system');
		theme.init();

		mediaListener?.({ matches: false });
		expect(theme.current).toBe('light');

		storageListener?.({ key: 'brewyou-theme', newValue: 'light' });
		expect(theme.preference).toBe('light');
	});
});
