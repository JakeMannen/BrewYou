<script module lang="ts">
	import type { BatchIngredientDto } from '$lib/types/api';

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

	export interface NextAdditionResult extends BatchIngredientDto {
		isDueNow: boolean;
	}

	export function getNextAddition(
		stageIngredients: BatchIngredientDto[] = [],
		timerRemainingSeconds: number
	): NextAdditionResult | null {
		const unchecked = stageIngredients.filter((i) => !i.isChecked);
		if (unchecked.length === 0) return null;

		const dueNow = unchecked.find(
			(i) => timerRemainingSeconds <= (i.additionTimeMinutes ?? 0) * 60
		);
		if (dueNow) {
			return { ...dueNow, isDueNow: true };
		}

		const upcoming = unchecked
			.filter((i) => (i.additionTimeMinutes ?? 0) * 60 < timerRemainingSeconds)
			.sort((a, b) => (b.additionTimeMinutes ?? 0) - (a.additionTimeMinutes ?? 0))[0];

		return upcoming ? { ...upcoming, isDueNow: false } : { ...unchecked[0], isDueNow: false };
	}
</script>

<script lang="ts">
	import type { BrewStage, EquipmentDto } from '$lib/types/api';
	import { api } from '$lib/api/client';
	import StageProgressBar from '$lib/components/batches/StageProgressBar.svelte';
	import BrewLinkControlPanel from '$lib/components/brewery/BrewLinkControlPanel.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import { t } from '$lib/i18n/index.svelte';
	import { formatNumber } from '$lib/utils/formatNumber';
	import { getInteractionActions } from '$lib/utils/brewlink';
	import {
		Play,
		Pause,
		RotateCcw,
		X,
		Flame,
		Thermometer,
		StepForward,
		Sparkles,
		Droplets,
		Waves,
		Radio,
		ChevronDown,
		ChevronUp,
		AlertTriangle
	} from '@lucide/svelte';

	interface Props {
		open: boolean;
		batchName: string;
		beerStyle?: string;
		currentStage: BrewStage;
		timerRemainingSeconds: number;
		timerRunning: boolean;
		stageTotalSeconds?: number;
		stageIngredients?: BatchIngredientDto[];
		targetTemperatureC?: number | null;
		currentTemperatureC?: number | null;
		activeStepName?: string | null;
		activeMashStepIndex?: number;
		totalMashSteps?: number;
		spargeEnabled?: boolean;
		spargeWaterTempC?: number | null;
		spargeLiveTempC?: number | null;
		spargeEquipmentName?: string | null;
		targetPostBoilVolumeLiters?: number | null;
		boilOffRatePerHour?: number | null;
		brewLinkEquipment?: EquipmentDto | null;
		onBrewLinkUpdate?: (updated: EquipmentDto) => void;
		onToggleTimer: () => void;
		onResetTimer: () => void;
		onAdjustTimer?: (seconds: number) => void;
		onAdvanceStep?: () => void;
		onToggleIngredient?: (ingredientId: string, currentChecked: boolean) => void;
		onClose: () => void;
	}

	let {
		open,
		batchName,
		beerStyle,
		currentStage,
		timerRemainingSeconds,
		timerRunning,
		stageTotalSeconds,
		stageIngredients = [],
		targetTemperatureC,
		currentTemperatureC,
		activeStepName,
		activeMashStepIndex,
		totalMashSteps,
		spargeEnabled = true,
		spargeWaterTempC,
		spargeLiveTempC,
		spargeEquipmentName,
		targetPostBoilVolumeLiters,
		boilOffRatePerHour,
		brewLinkEquipment = null,
		onBrewLinkUpdate,
		onToggleTimer,
		onResetTimer,
		onAdjustTimer,
		onAdvanceStep,
		onToggleIngredient,
		onClose
	}: Props = $props();

	let showBrewLinkPanel = $state(false);
	let isTogglingHeat = $state(false);
	let isTogglingPump = $state(false);

	let brewLinkMetrics = $derived.by(() => {
		if (!brewLinkEquipment?.latestMetricsJson) return null;
		try {
			const raw = JSON.parse(brewLinkEquipment.latestMetricsJson);
			if (typeof raw !== 'object' || raw === null) return null;
			const parseBool = (v: unknown): boolean | undefined => {
				if (typeof v === 'boolean') return v;
				if (typeof v === 'string') return v.toLowerCase() === 'true';
				return undefined;
			};
			const parseNum = (v: unknown): number | null => {
				if (typeof v === 'number' && !isNaN(v)) return v;
				if (typeof v === 'string' && !isNaN(Number(v))) return Number(v);
				return null;
			};
			const heatActive =
				parseBool(raw.heatActive) ??
				parseBool(raw.heat_active) ??
				parseBool(raw.heating) ??
				parseBool(raw.heater_on) ??
				parseBool(raw.heaterOn) ??
				false;
			const pumpActive =
				parseBool(raw.pumpActive) ??
				parseBool(raw.pump_active) ??
				parseBool(raw.pumpOn) ??
				parseBool(raw.pump_on) ??
				parseBool(raw.pumping) ??
				false;
			const heatPowerPct =
				parseNum(raw.heatPowerPct) ??
				parseNum(raw.heat_power_pct) ??
				parseNum(raw.heatPct) ??
				parseNum(raw.heat_pct) ??
				parseNum(raw.power_pct) ??
				parseNum(raw.power) ??
				(heatActive ? 100 : 0);
			const interactionRequired =
				parseBool(raw.interactionRequired) ??
				parseBool(raw.interaction_required) ??
				parseBool(raw.interaction?.required) ??
				false;
			const interactionCode =
				parseNum(raw.interactionCode) ??
				parseNum(raw.interaction_code) ??
				parseNum(raw.interaction?.code);
			const interactionMessage = (raw.interactionMessage ??
				raw.interaction_message ??
				raw.interaction?.message ??
				null) as string | null;

			return {
				heatActive,
				heatPowerPct,
				pumpActive,
				interaction: {
					required: interactionRequired,
					code: interactionCode,
					message: interactionMessage
				}
			};
		} catch {
			return null;
		}
	});

	let hudInteractionPlan = $derived(getInteractionActions(brewLinkMetrics?.interaction.code));

	async function quickToggleHeater() {
		if (!brewLinkEquipment) return;
		isTogglingHeat = true;
		try {
			const nextState = !brewLinkMetrics?.heatActive;
			const res = await api.equipment.sendBrewLinkControl(brewLinkEquipment.id, {
				action: 'set_heat',
				state: nextState,
				params: {
					state: nextState
				}
			});
			if (res.latestMetricsJson) {
				brewLinkEquipment = {
					...brewLinkEquipment,
					latestMetricsJson: res.latestMetricsJson
				};
			}
			onBrewLinkUpdate?.(brewLinkEquipment);
		} catch (err: unknown) {
			console.error('Failed to toggle heater from HUD', err);
		} finally {
			isTogglingHeat = false;
		}
	}

	async function quickTogglePump() {
		if (!brewLinkEquipment) return;
		isTogglingPump = true;
		try {
			const nextState = !brewLinkMetrics?.pumpActive;
			const res = await api.equipment.sendBrewLinkControl(brewLinkEquipment.id, {
				action: 'set_pump',
				state: nextState,
				params: {
					state: nextState
				}
			});
			if (res.latestMetricsJson) {
				brewLinkEquipment = {
					...brewLinkEquipment,
					latestMetricsJson: res.latestMetricsJson
				};
			}
			onBrewLinkUpdate?.(brewLinkEquipment);
		} catch (err: unknown) {
			console.error('Failed to toggle pump from HUD', err);
		} finally {
			isTogglingPump = false;
		}
	}

	const nextAddition = $derived.by(() => {
		if (currentStage !== 'Boil' || !stageIngredients || stageIngredients.length === 0) {
			return null;
		}
		return getNextAddition(stageIngredients, timerRemainingSeconds);
	});

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

			<!-- BrewLink Equipment Interaction / Alert Prompt Banner in HUD -->
			{#if brewLinkMetrics?.interaction?.required}
				<div
					class="mb-3 flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-amber-500/50 bg-amber-500/20 px-4 py-2 text-amber-200 shadow-lg"
					data-testid="hud-brewlink-interaction-banner"
				>
					<div class="flex items-center gap-2">
						<AlertTriangle class="h-5 w-5 animate-bounce text-amber-400" />
						<span class="text-xs font-bold">
							{brewLinkMetrics.interaction.message || t('equipment.brewlink_interaction_alert')}
						</span>
					</div>
					<div class="flex items-center gap-2">
						<button
							type="button"
							onclick={async () => {
								if (!brewLinkEquipment) return;
								await api.equipment.sendBrewLinkControl(brewLinkEquipment.id, {
									action: hudInteractionPlan.confirmAction
								});
								const res = await api.equipment.getById(brewLinkEquipment.id);
								onBrewLinkUpdate?.(res);
							}}
							class="rounded-lg bg-amber-500 px-3 py-1 text-xs font-bold text-zinc-950 shadow hover:bg-amber-400 active:scale-95"
							data-testid="hud-ack-interaction-btn"
						>
							{t('common.yes')} — {t(hudInteractionPlan.confirmLabelKey)}
						</button>
						<button
							type="button"
							onclick={async () => {
								if (!brewLinkEquipment) return;
								await api.equipment.sendBrewLinkControl(brewLinkEquipment.id, {
									action: hudInteractionPlan.dismissAction
								});
								const res = await api.equipment.getById(brewLinkEquipment.id);
								onBrewLinkUpdate?.(res);
							}}
							class="rounded-lg border border-amber-400/40 bg-zinc-900/60 px-2.5 py-1 text-xs font-semibold text-amber-200 hover:bg-zinc-900 active:scale-95"
							data-testid="hud-dismiss-alert-btn"
						>
							{t('common.no')} — {t(hudInteractionPlan.dismissLabelKey)}
						</button>
					</div>
				</div>
			{/if}

			<!-- Giant Countdown Display -->
			<div
				class="relative my-2 flex w-full max-w-3xl flex-col items-center justify-center rounded-3xl border border-white/10 bg-zinc-900/60 px-6 py-6 shadow-2xl backdrop-blur-md sm:px-14 sm:py-8"
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
						{timerRunning
							? t('batches.workspace.timer_running_hint')
							: t('batches.workspace.timer_paused_hint')}
					</span>
				{/if}

				<!-- HUD Stage Progress Bar -->
				{#if stageTotalSeconds && (currentStage === 'Boil' || currentStage === 'Mash')}
					<div class="mt-4 w-full border-t border-white/10 pt-3">
						<StageProgressBar
							totalSeconds={stageTotalSeconds}
							remainingSeconds={timerRemainingSeconds}
							running={timerRunning}
							stage={currentStage}
							milestones={stageIngredients}
							onToggleMilestone={onToggleIngredient}
							variant="hud"
						/>
					</div>
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
						<span>{t('batches.workspace.hud_pause')}</span>
					{:else}
						<Play class="h-6 w-6 fill-current stroke-none" />
						<span>{t('batches.workspace.hud_start')}</span>
					{/if}
				</button>

				<!-- Reset Timer Action -->
				<button
					type="button"
					data-testid="hud-reset-timer-btn"
					onclick={onResetTimer}
					class="flex min-h-[64px] min-w-[64px] cursor-pointer items-center justify-center rounded-2xl border border-white/20 bg-zinc-900 text-zinc-300 transition-all hover:border-white/40 hover:bg-zinc-800 active:scale-95"
					title={t('batches.workspace.reset_timer_title')}
					aria-label={t('batches.workspace.reset_timer')}
				>
					<RotateCcw class="h-6 w-6" />
				</button>

				<!-- Quick Adjustments (+5m, -5m) if supported -->
				{#if onAdjustTimer}
					<button
						type="button"
						onclick={() => onAdjustTimer(300)}
						title={t('batches.workspace.adjust_timer_forward')}
						aria-label={t('batches.workspace.adjust_timer_forward')}
						class="flex min-h-[64px] cursor-pointer items-center justify-center rounded-2xl border border-white/10 bg-zinc-900 px-4 font-mono text-sm font-bold text-zinc-300 transition-all hover:bg-zinc-800 active:scale-95"
					>
						+5m
					</button>
					<button
						type="button"
						onclick={() => onAdjustTimer(-300)}
						title={t('batches.workspace.adjust_timer_backward')}
						aria-label={t('batches.workspace.adjust_timer_backward')}
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
						aria-label={t('batches.workspace.next_step')}
						class="flex min-h-[64px] cursor-pointer items-center gap-2 rounded-2xl border border-amber-500/30 bg-amber-500/10 px-5 text-sm font-bold text-amber-300 transition-all hover:bg-amber-500/20 active:scale-95"
					>
						<StepForward class="h-5 w-5" />
						<span>{t('batches.workspace.next_step')}</span>
					</button>
				{/if}
			</div>

			<!-- BrewLink Tactile Hardware Controls (Steam & Wet Hands Friendly) -->
			{#if brewLinkEquipment && brewLinkEquipment.connectionType === 'BrewLink'}
				<div
					class="mt-4 rounded-2xl border border-sky-500/30 bg-sky-500/10 p-3"
					data-testid="hud-brewlink-section"
				>
					<div class="flex flex-wrap items-center justify-between gap-3">
						<div class="flex items-center gap-2">
							<Radio class="h-4 w-4 text-sky-400" />
							<span class="text-xs font-bold text-sky-300">
								{brewLinkEquipment.name} ({t('equipment.brewlink_tag')})
							</span>
						</div>

						<div class="flex flex-wrap items-center gap-2">
							<!-- Quick Heater Toggle -->
							<button
								type="button"
								disabled={isTogglingHeat}
								onclick={quickToggleHeater}
								class="flex min-h-[48px] items-center gap-2 rounded-xl px-4 text-xs font-bold shadow transition {brewLinkMetrics?.heatActive
									? 'bg-rose-600 text-white hover:bg-rose-700'
									: 'border border-white/20 bg-zinc-800 text-zinc-300 hover:bg-zinc-700'}"
								data-testid="hud-brewlink-heat-btn"
							>
								<Flame
									class="h-4 w-4 {brewLinkMetrics?.heatActive
										? 'animate-pulse text-amber-300'
										: ''}"
								/>
								<span
									>{brewLinkMetrics?.heatActive
										? `${t('equipment.brewlink_heat_on')} (${brewLinkMetrics?.heatPowerPct ?? 100}%)`
										: t('equipment.brewlink_heat_off')}</span
								>
							</button>

							<!-- Quick Pump Toggle -->
							<button
								type="button"
								disabled={isTogglingPump}
								onclick={quickTogglePump}
								class="flex min-h-[48px] items-center gap-2 rounded-xl px-4 text-xs font-bold shadow transition {brewLinkMetrics?.pumpActive
									? 'bg-cyan-600 text-white hover:bg-cyan-700'
									: 'border border-white/20 bg-zinc-800 text-zinc-300 hover:bg-zinc-700'}"
								data-testid="hud-brewlink-pump-btn"
							>
								<Waves
									class="h-4 w-4 {brewLinkMetrics?.pumpActive ? 'animate-spin text-white' : ''}"
								/>
								<span
									>{brewLinkMetrics?.pumpActive
										? t('equipment.brewlink_pump_on')
										: t('equipment.brewlink_pump_off')}</span
								>
							</button>

							<!-- Toggle Full Control Panel Drawer -->
							<button
								type="button"
								onclick={() => (showBrewLinkPanel = !showBrewLinkPanel)}
								class="flex min-h-[48px] items-center gap-1.5 rounded-xl border border-sky-400/30 bg-sky-950/60 px-3 text-xs font-bold text-sky-300 hover:bg-sky-900/60"
								data-testid="hud-brewlink-panel-toggle"
							>
								<span>{t('equipment.brewlink_controls')}</span>
								{#if showBrewLinkPanel}
									<ChevronUp class="h-4 w-4" />
								{:else}
									<ChevronDown class="h-4 w-4" />
								{/if}
							</button>
						</div>
					</div>

					{#if showBrewLinkPanel}
						<div class="mt-3 border-t border-sky-500/20 pt-3 text-left">
							<BrewLinkControlPanel
								equipment={brewLinkEquipment}
								compact={true}
								onUpdate={(updated) => onBrewLinkUpdate?.(updated)}
							/>
						</div>
					{/if}
				</div>
			{/if}
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
					{t('batches.workspace.live_sensor_hud')}
				</span>
				<div class="mt-1 flex items-baseline gap-1.5 font-mono text-2xl font-bold text-emerald-400">
					<Thermometer class="h-5 w-5 text-emerald-500" />
					<span>{getDisplayTemp(currentTemperatureC)}</span>
				</div>
			</div>

			<!-- Dynamic Stage-Contextual Tile: Sparge Water (Mash) / Next Addition & Post-Boil Metrics (Boil) -->
			{#if currentStage === 'Mash'}
				<div
					data-testid="hud-sparge-temp-tile"
					class="col-span-2 flex flex-col items-center justify-center rounded-2xl border border-white/10 bg-zinc-900/70 p-3 sm:col-span-1"
				>
					<span class="text-[11px] font-bold tracking-wider text-zinc-400 uppercase">
						{t('batches.workspace.sparge_water_hud')}
					</span>
					{#if spargeEnabled}
						<div
							data-testid="hud-sparge-temp-value"
							class="mt-1 flex items-baseline gap-1.5 font-mono text-2xl font-bold text-cyan-400"
						>
							<Droplets class="h-5 w-5 text-cyan-500" />
							<span>{getDisplayTemp(spargeLiveTempC ?? spargeWaterTempC)}</span>
							{#if spargeLiveTempC !== null && spargeLiveTempC !== undefined && spargeWaterTempC !== null && spargeWaterTempC !== undefined && spargeLiveTempC !== spargeWaterTempC}
								<span class="text-xs font-normal text-zinc-400">
									/ {getDisplayTemp(spargeWaterTempC)}
								</span>
							{/if}
						</div>
						{#if spargeEquipmentName}
							<span class="mt-0.5 max-w-full truncate text-[10px] font-medium text-zinc-400">
								{spargeEquipmentName}
							</span>
						{/if}
					{:else}
						<div
							data-testid="hud-sparge-temp-value"
							class="mt-1 flex items-baseline gap-1.5 font-mono text-2xl font-bold text-zinc-500"
						>
							<span>N/A</span>
						</div>
						<span class="mt-0.5 text-[10px] font-medium text-zinc-400">
							{t('batches.workspace.sparge_temp_na')}
						</span>
					{/if}
				</div>
			{:else if currentStage === 'Boil'}
				<div
					data-testid="hud-boil-info-tile"
					class="col-span-2 flex flex-col items-center justify-center rounded-2xl border border-white/10 bg-zinc-900/70 p-3 sm:col-span-1"
				>
					{#if nextAddition}
						<div class="flex items-center gap-1.5">
							<span class="text-[11px] font-bold tracking-wider text-zinc-400 uppercase">
								{t('batches.workspace.next_addition_hud')}
							</span>
							{#if nextAddition.isDueNow}
								<span
									class="py-0.2 animate-pulse rounded-md bg-amber-500/20 px-1.5 text-[10px] font-bold text-amber-400 uppercase"
								>
									{t('batches.workspace.due_now_badge')}
								</span>
							{/if}
						</div>
						<div
							data-testid="hud-boil-info-value"
							class="mt-1 flex max-w-full items-baseline gap-1.5 font-mono text-xl font-bold sm:text-2xl {nextAddition.isDueNow
								? 'animate-pulse text-amber-400'
								: 'text-amber-300'}"
						>
							<Flame class="h-5 w-5 shrink-0 text-amber-500" />
							<span class="truncate">{nextAddition.additionTimeMinutes}m • {nextAddition.name}</span
							>
						</div>
						<div
							class="mt-0.5 flex max-w-full items-center gap-1.5 truncate text-[10px] font-medium text-zinc-400"
						>
							<span>{nextAddition.amount}{nextAddition.unit}</span>
							{#if targetPostBoilVolumeLiters}
								<span>•</span>
								<span>
									{t('batches.workspace.target_post_boil_short')}: {formatNumber(
										targetPostBoilVolumeLiters,
										1
									)}L
								</span>
							{/if}
						</div>
					{:else}
						<span class="text-[11px] font-bold tracking-wider text-zinc-400 uppercase">
							{t('batches.workspace.post_boil_vol_hud')}
						</span>
						<div
							data-testid="hud-boil-info-value"
							class="mt-1 flex items-baseline gap-1.5 font-mono text-2xl font-bold text-amber-400"
						>
							<Droplets class="h-5 w-5 shrink-0 text-amber-500" />
							<span>
								{targetPostBoilVolumeLiters
									? `${formatNumber(targetPostBoilVolumeLiters, 1)}L`
									: '—'}
							</span>
						</div>
						<span class="mt-0.5 text-[10px] font-medium text-zinc-400">
							{#if boilOffRatePerHour}
								{t('batches.workspace.boil_off_rate_hint', {
									rate: formatNumber(boilOffRatePerHour, 1)
								})}
							{:else}
								{t('batches.workspace.all_milestones_completed')}
							{/if}
						</span>
					{/if}
				</div>
			{:else}
				<!-- Fallback / generic stage confirmation -->
				<div
					class="col-span-2 flex flex-col items-center justify-center rounded-2xl border border-white/10 bg-zinc-900/70 p-3 sm:col-span-1"
				>
					<span class="text-[11px] font-bold tracking-wider text-zinc-400 uppercase">
						{t('batches.workspace.steam_friendly_mode')}
					</span>
					<span class="mt-1 flex items-center gap-1 font-mono text-xs font-bold text-amber-300">
						<Sparkles class="h-3.5 w-3.5" />
						{t('batches.workspace.high_contrast_active')}
					</span>
				</div>
			{/if}
		</footer>
	</div>
{/if}
