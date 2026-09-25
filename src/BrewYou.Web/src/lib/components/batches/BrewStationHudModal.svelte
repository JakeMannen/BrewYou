<script module lang="ts">
	export function formatHudTime(totalSeconds: number): string {
		const safeSec = Math.max(0, Math.floor(totalSeconds));
		const hours = Math.floor(safeSec / 3600);
		const minutes = Math.floor((safeSec % 3600) / 60);
		const seconds = safeSec % 60;

		if (hours > 0) {
			return `${hours}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
		}
		return `${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
	}
</script>

<script lang="ts">
	import type { BrewStage } from '$lib/types/api';
	import { settings } from '$lib/stores/settings.svelte';
	import { t } from '$lib/i18n/index.svelte';
	import {
		Play,
		Pause,
		RotateCcw,
		X,
		Flame,
		Thermometer,
		StepForward,
		Sparkles
	} from '@lucide/svelte';

	interface Props {
		open: boolean;
		batchName: string;
		beerStyle?: string;
		currentStage: BrewStage;
		timerRemainingSeconds: number;
		timerRunning: boolean;
		targetTemperatureC?: number | null;
		currentTemperatureC?: number | null;
		activeStepName?: string | null;
		activeMashStepIndex?: number;
		totalMashSteps?: number;
		onToggleTimer: () => void;
		onResetTimer: () => void;
		onAdjustTimer?: (seconds: number) => void;
		onAdvanceStep?: () => void;
		onClose: () => void;
	}

	let {
		open,
		batchName,
		beerStyle,
		currentStage,
		timerRemainingSeconds,
		timerRunning,
		targetTemperatureC,
		currentTemperatureC,
		activeStepName,
		activeMashStepIndex,
		totalMashSteps,
		onToggleTimer,
		onResetTimer,
		onAdjustTimer,
		onAdvanceStep,
		onClose
	}: Props = $props();

	function getDisplayTemp(celsius?: number | null): string {
		if (celsius === null || celsius === undefined) return '—';
		if (settings.temperatureUnit === 'Fahrenheit') {
			return `${Math.round(((celsius * 9) / 5 + 32) * 10) / 10}°F`;
		}
		return `${Math.round(celsius * 10) / 10}°C`;
	}

	function handleKeydown(e: KeyboardEvent) {
		if (!open) return;
		if (e.key === 'Escape') {
			onClose();
		} else if (e.code === 'Space' && (e.target as HTMLElement)?.tagName !== 'INPUT') {
			e.preventDefault();
			onToggleTimer();
		} else if (e.key.toLowerCase() === 'r' && (e.target as HTMLElement)?.tagName !== 'INPUT') {
			e.preventDefault();
			onResetTimer();
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open}
	<div
		data-testid="brew-station-hud-modal"
		role="dialog"
		aria-modal="true"
		aria-label="{t('batches.workspace.brew_station_hud')} - {batchName}"
		class="fixed inset-0 z-50 flex flex-col justify-between bg-zinc-950/98 p-4 text-white backdrop-blur-2xl select-none sm:p-6 md:p-8"
	>
		<!-- Top Bar: Batch info & Exit -->
		<header class="flex items-center justify-between border-b border-white/10 pb-4">
			<div class="flex items-center gap-3 sm:gap-4">
				<div
					class="flex h-12 w-12 items-center justify-center rounded-2xl border border-amber-500/40 bg-amber-500/20 text-amber-400 shadow-[0_0_20px_rgba(245,158,11,0.3)]"
				>
					<Flame class="h-6 w-6 animate-pulse" />
				</div>
				<div>
					<div class="flex flex-wrap items-center gap-2">
						<span
							class="rounded-md border border-amber-500/40 bg-amber-500/20 px-2 py-0.5 text-xs font-bold tracking-wider text-amber-300 uppercase"
						>
							{t(`batches.stages.${currentStage.toLowerCase()}`)}
						</span>
						{#if beerStyle}
							<span class="text-xs text-zinc-400">• {beerStyle}</span>
						{/if}
					</div>
					<h2 class="font-editorial text-xl font-bold tracking-tight text-white sm:text-2xl">
						{batchName}
					</h2>
				</div>
			</div>

			<!-- Exit HUD CTA -->
			<button
				type="button"
				data-testid="exit-hud-btn"
				onclick={onClose}
				class="flex min-h-[48px] cursor-pointer items-center gap-2 rounded-2xl border border-white/20 bg-zinc-900 px-4 py-2.5 text-sm font-bold text-zinc-200 shadow-md transition-all hover:border-white/40 hover:bg-zinc-800 active:scale-95"
				title="{t('batches.workspace.exit_hud')} (Esc)"
				aria-label={t('batches.workspace.exit_hud')}
			>
				<X class="h-5 w-5" />
				<span class="hidden sm:inline">{t('batches.workspace.exit_hud')}</span>
			</button>
		</header>

		<!-- Center Arena: Massive High-Visibility Timer & Active Rests -->
		<main class="my-auto flex flex-col items-center justify-center py-6 text-center">
			<!-- Stage & Step Context Badge -->
			{#if activeStepName}
				<div
					class="mb-3 inline-flex items-center gap-2 rounded-full border border-amber-500/30 bg-amber-500/10 px-4 py-1 text-sm font-semibold text-amber-300"
				>
					<Sparkles class="h-4 w-4" />
					<span>
						{#if totalMashSteps && activeMashStepIndex !== undefined}
							{t('mash_profile.step_order_badge', { order: activeMashStepIndex + 1 })} / {totalMashSteps}:
						{/if}
						{activeStepName}
					</span>
				</div>
			{/if}

			<!-- Giant Countdown Display -->
			<div
				class="relative my-2 flex flex-col items-center justify-center rounded-3xl border border-white/10 bg-zinc-900/60 px-6 py-6 shadow-2xl backdrop-blur-md sm:px-14 sm:py-10"
			>
				<span
					data-testid="hud-countdown-timer"
					class="font-mono text-7xl font-extrabold tracking-tighter sm:text-8xl md:text-9xl {timerRemainingSeconds ===
					0
						? 'animate-pulse text-amber-400'
						: timerRunning
							? 'text-emerald-400'
							: 'text-zinc-100'}"
				>
					{formatHudTime(timerRemainingSeconds)}
				</span>

				{#if timerRemainingSeconds === 0}
					<span class="mt-2 font-mono text-base font-bold text-amber-400 uppercase sm:text-lg">
						{t('batches.workspace.timer_complete')}
					</span>
				{:else}
					<span class="mt-1 font-mono text-xs font-medium text-zinc-400 uppercase sm:text-sm">
						{timerRunning ? t('common.loading') : 'PAUSED (Space to resume)'}
					</span>
				{/if}
			</div>

			<!-- Large Tactile Controls (Steam & Wet Hands Friendly, >=64px) -->
			<div class="mt-6 flex flex-wrap items-center justify-center gap-4">
				<!-- Play / Pause Main Action -->
				<button
					type="button"
					data-testid="hud-toggle-timer-btn"
					onclick={onToggleTimer}
					class="flex min-h-[64px] min-w-[160px] cursor-pointer items-center justify-center gap-3 rounded-2xl px-6 py-3 text-lg font-bold shadow-lg transition-all active:scale-95 {timerRunning
						? 'border border-amber-500/40 bg-amber-500 text-zinc-950 shadow-[0_0_25px_rgba(245,158,11,0.4)] hover:bg-amber-400'
						: 'border border-emerald-500/40 bg-emerald-500 text-zinc-950 shadow-[0_0_25px_rgba(16,185,129,0.4)] hover:bg-emerald-400'}"
				>
					{#if timerRunning}
						<Pause class="h-6 w-6 fill-current stroke-none" />
						<span>PAUSE</span>
					{:else}
						<Play class="h-6 w-6 fill-current stroke-none" />
						<span>START</span>
					{/if}
				</button>

				<!-- Reset Timer Action -->
				<button
					type="button"
					data-testid="hud-reset-timer-btn"
					onclick={onResetTimer}
					class="flex min-h-[64px] min-w-[64px] cursor-pointer items-center justify-center rounded-2xl border border-white/20 bg-zinc-900 text-zinc-300 transition-all hover:border-white/40 hover:bg-zinc-800 active:scale-95"
					title="Reset Timer (R)"
					aria-label="Reset Timer"
				>
					<RotateCcw class="h-6 w-6" />
				</button>

				<!-- Quick Adjustments (+5m, -5m) if supported -->
				{#if onAdjustTimer}
					<button
						type="button"
						onclick={() => onAdjustTimer(300)}
						class="flex min-h-[64px] cursor-pointer items-center justify-center rounded-2xl border border-white/10 bg-zinc-900 px-4 font-mono text-sm font-bold text-zinc-300 transition-all hover:bg-zinc-800 active:scale-95"
					>
						+5m
					</button>
					<button
						type="button"
						onclick={() => onAdjustTimer(-300)}
						class="flex min-h-[64px] cursor-pointer items-center justify-center rounded-2xl border border-white/10 bg-zinc-900 px-4 font-mono text-sm font-bold text-zinc-300 transition-all hover:bg-zinc-800 active:scale-95"
					>
						-5m
					</button>
				{/if}

				<!-- Advance Step Button -->
				{#if onAdvanceStep}
					<button
						type="button"
						data-testid="hud-advance-step-btn"
						onclick={onAdvanceStep}
						class="flex min-h-[64px] cursor-pointer items-center gap-2 rounded-2xl border border-amber-500/30 bg-amber-500/10 px-5 text-sm font-bold text-amber-300 transition-all hover:bg-amber-500/20 active:scale-95"
					>
						<StepForward class="h-5 w-5" />
						<span>Next Step</span>
					</button>
				{/if}
			</div>
		</main>

		<!-- Bottom Telemetry Tiles: Targets & Instrumentation -->
		<footer class="grid grid-cols-2 gap-3 border-t border-white/10 pt-4 sm:grid-cols-3">
			<!-- Target Temperature -->
			<div
				class="flex flex-col items-center justify-center rounded-2xl border border-white/10 bg-zinc-900/70 p-3"
			>
				<span class="text-[11px] font-bold tracking-wider text-zinc-400 uppercase">
					{t('batches.workspace.target_temp_hud')}
				</span>
				<div class="mt-1 flex items-baseline gap-1.5 font-mono text-2xl font-bold text-amber-400">
					<Thermometer class="h-5 w-5 text-copper-500" />
					<span>{getDisplayTemp(targetTemperatureC)}</span>
				</div>
			</div>

			<!-- Live Measured Temperature (if available) -->
			<div
				class="flex flex-col items-center justify-center rounded-2xl border border-white/10 bg-zinc-900/70 p-3"
			>
				<span class="text-[11px] font-bold tracking-wider text-zinc-400 uppercase">
					Live Wort Sensor
				</span>
				<div class="mt-1 flex items-baseline gap-1.5 font-mono text-2xl font-bold text-emerald-400">
					<Thermometer class="h-5 w-5 text-emerald-500" />
					<span>{getDisplayTemp(currentTemperatureC)}</span>
				</div>
			</div>

			<!-- Steam-Friendly Mode Confirmation Stamp -->
			<div
				class="col-span-2 flex flex-col items-center justify-center rounded-2xl border border-white/10 bg-zinc-900/70 p-3 sm:col-span-1"
			>
				<span class="text-[11px] font-bold tracking-wider text-zinc-400 uppercase">
					{t('batches.workspace.steam_friendly_mode')}
				</span>
				<span class="mt-1 flex items-center gap-1 font-mono text-xs font-bold text-amber-300">
					<Sparkles class="h-3.5 w-3.5" /> High-Contrast Active
				</span>
			</div>
		</footer>
	</div>
{/if}
