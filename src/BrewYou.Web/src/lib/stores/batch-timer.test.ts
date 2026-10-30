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

	it('is a pure reader in getTimer and does not mutate reactive state or localStorage', () => {
		const initialStorageCount = Object.keys(
			JSON.parse(mockStorage.getItem('brewyou-batch-timers') ?? '{}')
		).length;

		// Calling getTimer for an unstarted timer must be pure read
		const timer = store.getTimer('batch-unstarted', 'Boil', 3600);
		expect(timer.remainingSeconds).toBe(3600);
		expect(timer.running).toBe(false);
		expect(timer.totalSeconds).toBe(3600);

		// Must not have mutated timers map or localStorage
		expect(store.timers['batch-unstarted:Boil']).toBeUndefined();
		const finalStorageCount = Object.keys(
			JSON.parse(mockStorage.getItem('brewyou-batch-timers') ?? '{}')
		).length;
		expect(finalStorageCount).toBe(initialStorageCount);
	});

	it('maintains independent per-step Mash timers and pauses all running Mash timers via pauseStage', () => {
		store.start('batch-1', 'Mash', 1200, 'mash-step-1'); // 20m step 1
		vi.advanceTimersByTime(15000); // 15s elapsed on step 1

		// Inspecting step 2 does not disturb step 1
		const step2Initial = store.getTimer('batch-1', 'Mash', 3600, 'mash-step-2');
		expect(step2Initial.running).toBe(false);
		expect(step2Initial.remainingSeconds).toBe(3600);

		const step1Running = store.getTimer('batch-1', 'Mash', 1200, 'mash-step-1');
		expect(step1Running.running).toBe(true);
		expect(step1Running.remainingSeconds).toBe(1185);

		// Start step 2 concurrently, advance 10s, then pause entire Mash stage
		store.start('batch-1', 'Mash', 3600, 'mash-step-2');
		vi.advanceTimersByTime(10000);
		store.pauseStage('batch-1', 'Mash');

		const step1Paused = store.getTimer('batch-1', 'Mash', 1200, 'mash-step-1');
		const step2Paused = store.getTimer('batch-1', 'Mash', 3600, 'mash-step-2');
		expect(step1Paused.running).toBe(false);
		expect(step1Paused.remainingSeconds).toBe(1175);
		expect(step2Paused.running).toBe(false);
		expect(step2Paused.remainingSeconds).toBe(3590);
	});
});
