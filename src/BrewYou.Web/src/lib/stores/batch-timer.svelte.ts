import type { BrewStage } from '$lib/types/api';

export interface BatchTimerRecord {
	batchId: string;
	stage: BrewStage;
	stepId?: string | null;
	totalSeconds: number;
	remainingSeconds: number;
	isRunning: boolean;
	targetEndTime: number | null;
	updatedAt: number;
}

export interface TimerView {
	remainingSeconds: number;
	running: boolean;
	totalSeconds: number;
}

const STORAGE_KEY = 'brewyou-batch-timers';

export class BatchTimerStore {
	timers = $state<Record<string, BatchTimerRecord>>({});
	private tickerInterval: ReturnType<typeof setInterval> | null = null;
	private initialized = false;

	constructor() {
		if (typeof window !== 'undefined') {
			this.init();
		}
	}

	init(): void {
		if (typeof window === 'undefined') return;

		this.loadFromStorage();

		// Check if any timers were running and update elapsed time
		const now = Date.now();
		let hasRunningTimers = false;

		for (const key of Object.keys(this.timers)) {
			const timer = this.timers[key];
			if (timer.isRunning && timer.targetEndTime) {
				const remaining = Math.max(0, Math.round((timer.targetEndTime - now) / 1000));
				timer.remainingSeconds = remaining;
				if (remaining === 0) {
					timer.isRunning = false;
					timer.targetEndTime = null;
				} else {
					hasRunningTimers = true;
				}
			}
		}

		this.saveToStorage();

		if (hasRunningTimers) {
			this.ensureTicker();
		}

		if (!this.initialized) {
			window.addEventListener('storage', (e) => {
				if (e.key === STORAGE_KEY) {
					this.loadFromStorage();
					this.ensureTicker();
				}
			});
			this.initialized = true;
		}
	}

	private getKey(batchId: string, stage: BrewStage, stepId?: string | null): string {
		if (stage === 'Mash' && stepId) {
			return `${batchId}:${stage}:${stepId}`;
		}
		return `${batchId}:${stage}`;
	}

	private loadFromStorage(): void {
		if (typeof window === 'undefined') return;
		try {
			const saved = localStorage.getItem(STORAGE_KEY);
			if (saved) {
				const parsed = JSON.parse(saved) as Record<string, BatchTimerRecord>;
				if (parsed && typeof parsed === 'object') {
					this.timers = parsed;
					return;
				}
			}
		} catch (err: unknown) {
			console.warn('Could not parse saved batch timers from localStorage', err);
		}
		this.timers = {};
	}

	private saveToStorage(): void {
		if (typeof window === 'undefined') return;
		try {
			localStorage.setItem(STORAGE_KEY, JSON.stringify(this.timers));
		} catch (err: unknown) {
			console.warn('Could not persist batch timers to localStorage', err);
		}
	}

	private ensureTicker(): void {
		if (this.tickerInterval) return;

		this.tickerInterval = setInterval(() => {
			const now = Date.now();
			let anyRunning = false;

			for (const key of Object.keys(this.timers)) {
				const timer = this.timers[key];
				if (timer.isRunning && timer.targetEndTime) {
					const remaining = Math.max(0, Math.round((timer.targetEndTime - now) / 1000));
					timer.remainingSeconds = remaining;

					if (remaining === 0) {
						timer.isRunning = false;
						timer.targetEndTime = null;
						this.saveToStorage();
					} else {
						anyRunning = true;
					}
				}
			}

			if (!anyRunning && this.tickerInterval) {
				clearInterval(this.tickerInterval);
				this.tickerInterval = null;
			}
		}, 1000);
	}

	getTimer(
		batchId: string,
		stage: BrewStage,
		defaultDurationSeconds: number,
		stepId?: string | null
	): TimerView {
		const key = this.getKey(batchId, stage, stepId);
		const timer = this.timers[key];

		if (!timer) {
			const safeDuration = Math.max(1, defaultDurationSeconds);
			return {
				remainingSeconds: safeDuration,
				running: false,
				totalSeconds: safeDuration
			};
		}

		let remaining = timer.remainingSeconds;
		let running = timer.isRunning;

		if (timer.isRunning && timer.targetEndTime) {
			const now = Date.now();
			remaining = Math.max(0, Math.round((timer.targetEndTime - now) / 1000));
			if (remaining === 0) {
				running = false;
			}
		}

		return {
			remainingSeconds: remaining,
			running,
			totalSeconds: timer.totalSeconds
		};
	}

	start(
		batchId: string,
		stage: BrewStage,
		defaultDurationSeconds = 3600,
		stepId?: string | null
	): void {
		const key = this.getKey(batchId, stage, stepId);
		let timer = this.timers[key];

		if (!timer) {
			const safeDuration = Math.max(1, defaultDurationSeconds);
			timer = {
				batchId,
				stage,
				stepId: stage === 'Mash' ? (stepId ?? null) : null,
				totalSeconds: safeDuration,
				remainingSeconds: safeDuration,
				isRunning: false,
				targetEndTime: null,
				updatedAt: Date.now()
			};
			this.timers[key] = timer;
		}

		if (timer.remainingSeconds <= 0) {
			timer.remainingSeconds = timer.totalSeconds;
		}

		timer.isRunning = true;
		timer.targetEndTime = Date.now() + timer.remainingSeconds * 1000;
		timer.updatedAt = Date.now();

		this.saveToStorage();
		this.ensureTicker();
	}

	pause(batchId: string, stage: BrewStage, stepId?: string | null): void {
		const key = this.getKey(batchId, stage, stepId);
		this.pauseByKey(key);
	}

	pauseStage(batchId: string, stage: BrewStage): void {
		const stageKey = `${batchId}:${stage}`;
		const stepPrefix = `${stageKey}:`;
		let changed = false;
		const now = Date.now();

		for (const key of Object.keys(this.timers)) {
			if (key === stageKey || key.startsWith(stepPrefix)) {
				const timer = this.timers[key];
				if (timer?.isRunning) {
					if (timer.targetEndTime) {
						timer.remainingSeconds = Math.max(0, Math.round((timer.targetEndTime - now) / 1000));
					}
					timer.isRunning = false;
					timer.targetEndTime = null;
					timer.updatedAt = now;
					changed = true;
				}
			}
		}

		if (changed) {
			this.saveToStorage();
		}
	}

	private pauseByKey(key: string): void {
		const timer = this.timers[key];
		if (!timer || !timer.isRunning) return;

		const now = Date.now();
		if (timer.targetEndTime) {
			timer.remainingSeconds = Math.max(0, Math.round((timer.targetEndTime - now) / 1000));
		}
		timer.isRunning = false;
		timer.targetEndTime = null;
		timer.updatedAt = now;

		this.saveToStorage();
	}

	reset(
		batchId: string,
		stage: BrewStage,
		defaultDurationSeconds: number,
		stepId?: string | null
	): void {
		const key = this.getKey(batchId, stage, stepId);
		const safeDuration = Math.max(1, defaultDurationSeconds);

		this.timers[key] = {
			batchId,
			stage,
			stepId: stage === 'Mash' ? (stepId ?? null) : null,
			totalSeconds: safeDuration,
			remainingSeconds: safeDuration,
			isRunning: false,
			targetEndTime: null,
			updatedAt: Date.now()
		};

		this.saveToStorage();
	}

	adjust(
		batchId: string,
		stage: BrewStage,
		seconds: number,
		defaultDurationSeconds = 3600,
		stepId?: string | null
	): void {
		const key = this.getKey(batchId, stage, stepId);
		let timer = this.timers[key];
		const now = Date.now();

		if (!timer) {
			const safeDuration = Math.max(1, defaultDurationSeconds);
			timer = {
				batchId,
				stage,
				stepId: stage === 'Mash' ? (stepId ?? null) : null,
				totalSeconds: safeDuration,
				remainingSeconds: safeDuration,
				isRunning: false,
				targetEndTime: null,
				updatedAt: now
			};
			this.timers[key] = timer;
		}

		const newRemaining = Math.max(0, timer.remainingSeconds + seconds);
		timer.remainingSeconds = newRemaining;

		if (newRemaining > timer.totalSeconds) {
			timer.totalSeconds = newRemaining;
		}

		if (timer.isRunning) {
			timer.targetEndTime = now + newRemaining * 1000;
		}
		timer.updatedAt = now;

		this.saveToStorage();
	}

	clear(batchId: string, stage?: BrewStage, stepId?: string | null): void {
		if (stage && stepId) {
			const key = this.getKey(batchId, stage, stepId);
			delete this.timers[key];
		} else if (stage) {
			const stageKey = `${batchId}:${stage}`;
			const stepPrefix = `${stageKey}:`;
			for (const key of Object.keys(this.timers)) {
				if (key === stageKey || key.startsWith(stepPrefix)) {
					delete this.timers[key];
				}
			}
		} else {
			for (const key of Object.keys(this.timers)) {
				if (key.startsWith(`${batchId}:`)) {
					delete this.timers[key];
				}
			}
		}
		this.saveToStorage();
	}
}

export const batchTimer = new BatchTimerStore();
