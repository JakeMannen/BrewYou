import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { BatchTimerStore } from './batch-timer.svelte';

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

describe('BatchTimerStore', () => {
	let store: BatchTimerStore;

	beforeEach(() => {
		vi.useFakeTimers();
		mockStorage.clear();

		Object.defineProperty(globalThis, 'localStorage', {
			value: mockStorage,
			writable: true,
			configurable: true
		});

		Object.defineProperty(globalThis, 'window', {
			value: {
				addEventListener: vi.fn(),
				removeEventListener: vi.fn()
			},
			writable: true,
			configurable: true
		});

		store = new BatchTimerStore();
	});

	afterEach(() => {
		vi.restoreAllMocks();
		vi.useRealTimers();
	});

	it('initializes timer with planned duration when no state exists', () => {
		const timer = store.getTimer('batch-1', 'Boil', 3600);
		expect(timer.remainingSeconds).toBe(3600);
		expect(timer.running).toBe(false);
		expect(timer.totalSeconds).toBe(3600);
	});

	it('starts timer and counts down using wall-clock time', () => {
		store.start('batch-1', 'Boil', 3600);

		let timer = store.getTimer('batch-1', 'Boil', 3600);
		expect(timer.running).toBe(true);
		expect(timer.remainingSeconds).toBe(3600);

		// Advance 10 seconds
		vi.advanceTimersByTime(10000);

		timer = store.getTimer('batch-1', 'Boil', 3600);
		expect(timer.running).toBe(true);
		expect(timer.remainingSeconds).toBe(3590);
	});

	it('pauses timer and preserves remaining duration', () => {
		store.start('batch-1', 'Boil', 3600);
		vi.advanceTimersByTime(30000); // 30s elapsed

		store.pause('batch-1', 'Boil');

		let timer = store.getTimer('batch-1', 'Boil', 3600);
		expect(timer.running).toBe(false);
		expect(timer.remainingSeconds).toBe(3570);

		// Advancing time further while paused does not deplete remaining time
		vi.advanceTimersByTime(60000);
		timer = store.getTimer('batch-1', 'Boil', 3600);
		expect(timer.running).toBe(false);
		expect(timer.remainingSeconds).toBe(3570);
	});

	it('resets timer to initial planned duration', () => {
		store.start('batch-1', 'Boil', 3600);
		vi.advanceTimersByTime(60000);

		store.reset('batch-1', 'Boil', 3600);

		const timer = store.getTimer('batch-1', 'Boil', 3600);
		expect(timer.running).toBe(false);
		expect(timer.remainingSeconds).toBe(3600);
		expect(timer.totalSeconds).toBe(3600);
	});

	it('elastically expands totalSeconds when adjusting timer (+5m)', () => {
		store.getTimer('batch-1', 'Boil', 3600);
		store.adjust('batch-1', 'Boil', 300); // +5m (3900s)

		const timer = store.getTimer('batch-1', 'Boil', 3600);
		expect(timer.remainingSeconds).toBe(3900);
		expect(timer.totalSeconds).toBe(3900);
	});

	it('persists and restores active timer state across page unmount and remount (leaving & returning)', () => {
		// User starts boil timer on batch page
		store.start('batch-42', 'Boil', 3600);

		// User leaves batch page (unmount). 15 minutes (900 seconds) pass while browsing inventory.
		vi.advanceTimersByTime(900 * 1000);

		// User navigates back to batch page (new store rehydrated from localStorage)
		const reloadedStore = new BatchTimerStore();
		reloadedStore.init();

		const restoredTimer = reloadedStore.getTimer('batch-42', 'Boil', 3600);
		expect(restoredTimer.running).toBe(true);
		expect(restoredTimer.remainingSeconds).toBe(2700); // 3600 - 900
	});

	it('marks timer as completed if time elapsed completely while away', () => {
		store.start('batch-42', 'Boil', 300); // 5 min boil

		// User leaves page for 10 minutes (600 seconds)
		vi.advanceTimersByTime(600 * 1000);

		const reloadedStore = new BatchTimerStore();
		reloadedStore.init();

		const restoredTimer = reloadedStore.getTimer('batch-42', 'Boil', 300);
		expect(restoredTimer.running).toBe(false);
		expect(restoredTimer.remainingSeconds).toBe(0);
	});

	it('maintains independent timers for different stages on the same batch', () => {
		store.start('batch-1', 'Boil', 3600);
		store.start('batch-1', 'Mash', 1800);

		vi.advanceTimersByTime(30000); // 30s

		const boil = store.getTimer('batch-1', 'Boil', 3600);
		const mash = store.getTimer('batch-1', 'Mash', 1800);

		expect(boil.remainingSeconds).toBe(3570);
		expect(mash.remainingSeconds).toBe(1770);
	});

	it('clears timer state when requested', () => {
		store.start('batch-1', 'Boil', 3600);
		store.clear('batch-1', 'Boil');

		const timer = store.getTimer('batch-1', 'Boil', 3600);
		expect(timer.running).toBe(false);
		expect(timer.remainingSeconds).toBe(3600);
	});
});
