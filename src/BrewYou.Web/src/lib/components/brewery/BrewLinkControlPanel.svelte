<script lang="ts">
	import { api, ApiClientError } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import { settings, celsiusToFahrenheit, fahrenheitToCelsius } from '$lib/stores/settings.svelte';
	import type { EquipmentDto, SendBrewLinkControlRequest } from '$lib/types/api';
	import { getInteractionActions } from '$lib/utils/brewlink';
	import ConfirmModal from '$lib/components/ui/ConfirmModal.svelte';
	import {
		Flame,
		Waves,
		Thermometer,
		Timer,
		Play,
		Pause,
		Square,
		RefreshCw,
		AlertTriangle,
		Check,
		Radio
	} from '@lucide/svelte';

	interface Props {
		equipment: EquipmentDto;
		compact?: boolean;
		onUpdate?: (updated: EquipmentDto) => void;
	}

	let { equipment = $bindable(), compact = false, onUpdate }: Props = $props();

	interface ParsedMetrics {
		currentTempC?: number | null;
		targetTempC?: number | null;
		boilTempC?: number | null;
		heatActive?: boolean | null;
		heatPowerPct?: number | null;
		pumpActive?: boolean | null;
		autoMode?: boolean | null;
		rampActive?: boolean | null;
		timerActive?: boolean | null;
		timerPaused?: boolean | null;
		timerRemainingSec?: number | null;
		timerTotalSec?: number | null;
		stepIndex?: number | null;
		stepName?: string | null;
		interactionRequired?: boolean | null;
		interactionCode?: number | null;
		interactionMessage?: string | null;
		delayedHeat?: boolean | null;
		interrupted?: boolean | null;
		manualPower?: boolean | null;
		spargeAlert?: boolean | null;
		errorCode?: number | null;
		connected?: boolean | null;
	}

	function parseMetrics(json: string | null | undefined): ParsedMetrics {
		if (!json) return {};
		try {
			const raw = JSON.parse(json);
			if (typeof raw !== 'object' || raw === null) return {};

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

			return {
				currentTempC:
					parseNum(raw.currentTempC) ??
					parseNum(raw.current_temp_c) ??
					parseNum(raw.tempC) ??
					parseNum(raw.temp_c) ??
					parseNum(raw.temp),
				targetTempC:
					parseNum(raw.targetTempC) ?? parseNum(raw.target_temp_c) ?? parseNum(raw.target_temp),
				boilTempC: parseNum(raw.boilTempC) ?? parseNum(raw.boil_temp_c) ?? parseNum(raw.boil_temp),
				heatActive,
				heatPowerPct,
				pumpActive,
				autoMode: parseBool(raw.autoMode) ?? parseBool(raw.auto_mode),
				rampActive: parseBool(raw.rampActive) ?? parseBool(raw.ramp_active),
				timerActive:
					parseBool(raw.timerActive) ?? parseBool(raw.timer_active) ?? parseBool(raw.timer?.active),
				timerPaused:
					parseBool(raw.timerPaused) ?? parseBool(raw.timer_paused) ?? parseBool(raw.timer?.paused),
				timerRemainingSec:
					parseNum(raw.timerRemainingSec) ??
					parseNum(raw.timer_remaining_sec) ??
					parseNum(raw.timer?.remainingSeconds) ??
					parseNum(raw.timer?.remaining_sec),
				timerTotalSec:
					parseNum(raw.timerTotalSec) ??
					parseNum(raw.timer_total_sec) ??
					parseNum(raw.timer?.totalSeconds) ??
					parseNum(raw.timer?.total_sec),
				stepIndex:
					parseNum(raw.stepIndex) ??
					parseNum(raw.step_index) ??
					parseNum(raw.stage?.stepIndex) ??
					parseNum(raw.stage?.step_index),
				stepName: raw.stepName ?? raw.step_name ?? raw.stage?.stepName ?? raw.stage?.step_name,
				interactionRequired:
					parseBool(raw.interactionRequired) ??
					parseBool(raw.interaction_required) ??
					parseBool(raw.interaction?.required),
				interactionCode:
					parseNum(raw.interactionCode) ??
					parseNum(raw.interaction_code) ??
					parseNum(raw.interaction?.code),
				interactionMessage:
					raw.interactionMessage ?? raw.interaction_message ?? raw.interaction?.message,
				delayedHeat:
					parseBool(raw.delayedHeat) ??
					parseBool(raw.delayed_heat) ??
					parseBool(raw.flags?.delayed_heat),
				interrupted: parseBool(raw.interrupted) ?? parseBool(raw.flags?.interrupted),
				manualPower:
					parseBool(raw.manualPower) ??
					parseBool(raw.manual_power) ??
					parseBool(raw.flags?.manual_power),
				spargeAlert:
					parseBool(raw.spargeAlert) ??
					parseBool(raw.sparge_alert) ??
					parseBool(raw.flags?.sparge_alert),
				errorCode: parseNum(raw.errorCode) ?? parseNum(raw.error_code),
				connected: parseBool(raw.connected)
			};
		} catch {
			return {};
		}
	}

	let overrideMetrics = $state<Partial<ParsedMetrics> | null>(null);
	let baseMetrics = $derived.by(() => parseMetrics(equipment.latestMetricsJson));
	let localMetrics = $derived.by<ParsedMetrics>(() => {
		if (overrideMetrics) {
			return { ...baseMetrics, ...overrideMetrics };
		}
		return baseMetrics;
	});
	let isAutoMode = $derived(localMetrics.autoMode === true);
	let isDeviceConnected = $derived.by(() => {
		if (localMetrics.connected === false) return false;
		if (localMetrics.connected === true) return true;
		return equipment.currentTemperatureC != null || !!equipment.latestMetricsJson;
	});
	let interactionPlan = $derived(getInteractionActions(localMetrics.interactionCode));

	let isOperating = $state(false);
	let isSyncing = $state(false);
	let operationError = $state<string | null>(null);
	let operationSuccess = $state<string | null>(null);
	let showConfirmCancelModal = $state(false);
	let showConfirmCancelSessionModal = $state(false);

	// Target temp input
	let customInputTemp = $state<number | null>(null);
	let inputTemp = $derived.by<number | null>(() => {
		if (customInputTemp != null) return customInputTemp;
		if (localMetrics.targetTempC != null) {
			return settings.temperatureUnit === 'Fahrenheit'
				? celsiusToFahrenheit(localMetrics.targetTempC)
				: localMetrics.targetTempC;
		}
		return null;
	});

	// Timer duration input (minutes) & delayed heat inputs
	let inputTimerMin = $state<number>(60);
	let delayedHeatMinutes = $state<number>(0);
	let delayedHeatSeconds = $state<number>(0);

	async function executeControl(req: SendBrewLinkControlRequest) {
		isOperating = true;
		operationError = null;
		operationSuccess = null;
		try {
			const res = await api.equipment.sendBrewLinkControl(equipment.id, req);
			overrideMetrics = null;
			if (res.latestMetricsJson) {
				equipment = {
					...equipment,
					latestMetricsJson: res.latestMetricsJson
				};
			}
			operationSuccess = t('equipment.brewlink_cmd_success');
			onUpdate?.(equipment);
			setTimeout(() => {
				operationSuccess = null;
			}, 3000);
		} catch (err: unknown) {
			const isOfflineError =
				err instanceof ApiClientError &&
				(err.status === 409 ||
					err.status === 502 ||
					err.code === 'EQUIPMENT_DISCONNECTED' ||
					err.code === 'GATEWAY_ERROR');
			overrideMetrics = isOfflineError ? { connected: false } : null;
			operationError =
				(err as Error).message || t('equipment.brewlink_cmd_failed', { error: 'Unknown error' });
		} finally {
			isOperating = false;
		}
	}

	async function toggleHeater() {
		const nextState = !localMetrics.heatActive;
		overrideMetrics = {
			heatActive: nextState
		};
		await executeControl({
			action: 'set_heat',
			state: nextState,
			params: {
				state: nextState
			}
		});
	}

	async function scheduleDelayedHeat() {
		const minutes = Math.max(0, Math.min(999, Math.round(delayedHeatMinutes || 0)));
		const seconds = Math.max(0, Math.min(59, Math.round(delayedHeatSeconds || 0)));
		overrideMetrics = {
			delayedHeat: minutes > 0 || seconds > 0
		};
		await executeControl({
			action: 'delayed_heat',
			minutes,
			seconds,
			params: {
				minutes,
				seconds
			}
		});
	}

	async function togglePump() {
		const nextState = !localMetrics.pumpActive;
		overrideMetrics = {
			pumpActive: nextState
		};
		await executeControl({
			action: 'set_pump',
			state: nextState,
			params: {
				state: nextState
			}
		});
	}

	async function applyTargetTemp() {
		if (isAutoMode || inputTemp == null || isNaN(inputTemp)) return;
		const tempC =
			settings.temperatureUnit === 'Fahrenheit' ? fahrenheitToCelsius(inputTemp) : inputTemp;
		const rounded = Math.round(tempC * 10) / 10;
		customInputTemp = null;
		overrideMetrics = {
			targetTempC: rounded
		};
		await executeControl({
			action: 'set_target_temp',
			tempC: rounded,
			params: {
				temp_c: rounded
			}
		});
	}

	async function stepTemp(direction: -1 | 1) {
		if (isAutoMode) return;
		if (settings.temperatureUnit === 'Fahrenheit') {
			const current = inputTemp ?? 149;
			customInputTemp = Math.round((current + direction) * 10) / 10;
			await applyTargetTemp();
			return;
		}
		customInputTemp = null;
		const action = direction > 0 ? 'temp_up' : 'temp_down';
		if (localMetrics.targetTempC != null) {
			overrideMetrics = {
				targetTempC: Math.max(
					0,
					Math.min(105, Math.round((localMetrics.targetTempC + direction) * 10) / 10)
				)
			};
		}
		await executeControl({ action });
	}

	async function startTimer() {
		if (isAutoMode || inputTimerMin <= 0) return;
		await executeControl({
			action: 'set_timer',
			durationMin: inputTimerMin,
			params: {
				duration_min: inputTimerMin
			}
		});
	}

	async function pauseTimer() {
		await executeControl({ action: 'pause_timer' });
	}

	async function resumeTimer() {
		await executeControl({ action: 'resume_timer' });
	}

	function cancelTimer() {
		if (isAutoMode) return;
		showConfirmCancelModal = true;
	}

	async function executeCancelTimer() {
		showConfirmCancelModal = false;
		await executeControl({ action: 'cancel_timer' });
	}

	async function pressSet() {
		await executeControl({ action: 'press_set' });
	}

	async function confirmInteraction() {
		await executeControl({ action: interactionPlan.confirmAction });
	}

	async function dismissInteraction() {
		await executeControl({ action: interactionPlan.dismissAction });
	}

	async function dismissSpargeAlert() {
		await executeControl({ action: 'dismiss_sparge_alert' });
	}

	async function disconnectMode() {
		await executeControl({ action: isAutoMode ? 'disconnect_auto' : 'disconnect_manual' });
	}

	async function executeCancelSession() {
		showConfirmCancelSessionModal = false;
		await executeControl({ action: 'cancel_session' });
	}

	async function syncState() {
		isSyncing = true;
		operationError = null;
		try {
			const res = await api.equipment.syncBrewLinkState(equipment.id);
			overrideMetrics = { connected: true };
			if (res.temperatureC != null || res.metricsJson) {
				equipment = {
					...equipment,
					currentTemperatureC: res.temperatureC ?? equipment.currentTemperatureC,
					latestMetricsJson: res.metricsJson ?? equipment.latestMetricsJson
				};
			}
			onUpdate?.(equipment);
		} catch (err: unknown) {
			overrideMetrics = { connected: false };
			operationError =
				(err as Error).message || t('equipment.brewlink_cmd_failed', { error: 'Sync failed' });
		} finally {
			isSyncing = false;
		}
	}

	function formatRemainingTime(sec?: number | null): string {
		if (sec == null || sec <= 0) return '00:00';
		const m = Math.floor(sec / 60);
		const s = sec % 60;
		return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
	}
</script>

<div
	class="rounded-2xl border border-sky-500/20 bg-sky-500/5 p-4 text-zinc-900 dark:border-sky-500/20 dark:bg-sky-500/10 dark:text-zinc-100 {compact
		? 'space-y-3'
		: 'space-y-4'}"
	data-testid="brewlink-control-panel"
>
	<!-- Header / Status Bar -->
	<div class="flex flex-wrap items-center justify-between gap-2 border-b border-sky-500/20 pb-3">
		<div class="flex flex-wrap items-center gap-2">
			<Radio class="h-4 w-4 text-sky-500" />
			<span class="text-xs font-bold tracking-wider text-sky-600 uppercase dark:text-sky-400">
				{t('equipment.brewlink_controls_title')}
			</span>
			<span
				class="rounded-full px-2 py-0.5 text-[10px] font-semibold {isDeviceConnected
					? 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-300'
					: 'bg-rose-500/15 text-rose-700 dark:text-rose-300'}"
			>
				{isDeviceConnected
					? t('equipment.brewlink_device_connected')
					: t('equipment.brewlink_device_offline')}
			</span>
			{#if localMetrics.autoMode != null}
				<span
					class="rounded-full px-2 py-0.5 text-[10px] font-semibold {isAutoMode
						? 'bg-indigo-500/15 text-indigo-700 dark:text-indigo-300'
						: 'bg-zinc-500/15 text-zinc-700 dark:text-zinc-300'}"
					data-testid="brewlink-mode-badge"
				>
					{isAutoMode ? t('equipment.brewlink_auto_mode') : t('equipment.brewlink_manual_mode')}
				</span>
			{/if}
			{#if localMetrics.rampActive}
				<span
					class="rounded-full bg-amber-500/15 px-2 py-0.5 text-[10px] font-semibold text-amber-700 dark:text-amber-300"
				>
					{t('equipment.brewlink_ramp_active')}
				</span>
			{/if}
			{#if localMetrics.delayedHeat}
				<span
					class="rounded-full bg-sky-500/15 px-2 py-0.5 text-[10px] font-semibold text-sky-700 dark:text-sky-300"
				>
					{t('equipment.brewlink_delayed_heat')}
				</span>
			{/if}
		</div>

		<div class="flex flex-wrap items-center gap-1.5">
			{#if isAutoMode}
				<button
					type="button"
					disabled={isOperating}
					onclick={() => (showConfirmCancelSessionModal = true)}
					class="inline-flex items-center gap-1 rounded-xl border border-rose-500/30 bg-rose-50 px-2.5 py-1 text-xs font-semibold text-rose-700 shadow-sm transition hover:bg-rose-100 disabled:opacity-50 dark:bg-rose-950/60 dark:text-rose-300 dark:hover:bg-rose-950"
					data-testid="brewlink-cancel-session-btn"
				>
					{t('equipment.brewlink_cancel_session')}
				</button>
			{/if}
			{#if localMetrics.autoMode != null}
				<button
					type="button"
					disabled={isOperating}
					onclick={disconnectMode}
					class="inline-flex items-center gap-1 rounded-xl border border-zinc-300 bg-white px-2.5 py-1 text-xs font-semibold text-zinc-700 shadow-sm transition hover:bg-zinc-100 disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800"
					data-testid="brewlink-disconnect-mode-btn"
				>
					{isAutoMode
						? t('equipment.brewlink_disconnect_auto')
						: t('equipment.brewlink_disconnect_manual')}
				</button>
			{/if}
			<button
				type="button"
				disabled={isSyncing || isOperating}
				onclick={syncState}
				class="inline-flex items-center gap-1.5 rounded-xl border border-sky-500/30 bg-white px-2.5 py-1 text-xs font-semibold text-sky-700 shadow-sm transition hover:bg-sky-50 disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-900 dark:text-sky-300 dark:hover:bg-zinc-800"
				data-testid="brewlink-sync-btn"
			>
				<RefreshCw class="h-3.5 w-3.5 {isSyncing ? 'animate-spin' : ''}" />
				<span>{isSyncing ? t('equipment.brewlink_syncing') : t('equipment.brewlink_sync_now')}</span
				>
			</button>
		</div>
	</div>

	<!-- Controller Error Code Banner -->
	{#if localMetrics.errorCode != null && localMetrics.errorCode !== 0}
		<div
			class="flex items-center gap-2 rounded-xl border border-rose-500/40 bg-rose-500/15 p-2.5 text-xs font-semibold text-rose-800 dark:text-rose-200"
			data-testid="brewlink-controller-error-banner"
		>
			<AlertTriangle class="h-4 w-4 shrink-0 text-rose-600 dark:text-rose-400" />
			<span>{t('equipment.brewlink_error_code', { code: localMetrics.errorCode })}</span>
		</div>
	{/if}

	<!-- Sparge Alert Banner (when active separately from interaction prompt) -->
	{#if localMetrics.spargeAlert && !localMetrics.interactionRequired}
		<div
			class="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-amber-500/40 bg-amber-500/15 p-2.5 text-xs text-amber-900 dark:text-amber-100"
			data-testid="brewlink-sparge-alert-banner"
		>
			<span class="font-semibold">{t('equipment.brewlink_sparge_alert')}</span>
			<button
				type="button"
				disabled={isOperating}
				onclick={dismissSpargeAlert}
				class="rounded-lg border border-amber-600/30 bg-white/60 px-2.5 py-1 text-xs font-semibold text-amber-900 hover:bg-white disabled:opacity-50 dark:bg-zinc-900/60 dark:text-amber-200"
				data-testid="brewlink-dismiss-sparge-btn"
			>
				{t('equipment.brewlink_dismiss_sparge')}
			</button>
		</div>
	{/if}

	<!-- Alert / Attention Banner -->
	{#if localMetrics.interactionRequired}
		<div
			class="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-amber-500/40 bg-amber-500/15 p-3 text-amber-900 dark:bg-amber-500/20 dark:text-amber-100"
			data-testid="brewlink-interaction-banner"
		>
			<div class="flex items-center gap-2.5">
				<AlertTriangle class="h-5 w-5 shrink-0 text-amber-600 dark:text-amber-400" />
				<div>
					<p class="text-xs font-bold">{t('equipment.brewlink_interaction_alert')}</p>
					<p class="text-xs">
						{localMetrics.interactionMessage || t('equipment.brewlink_interaction_alert')}
					</p>
				</div>
			</div>
			<div class="flex items-center gap-2">
				<button
					type="button"
					disabled={isOperating}
					onclick={confirmInteraction}
					class="rounded-lg bg-amber-600 px-3 py-1 text-xs font-bold text-white shadow hover:bg-amber-700 disabled:opacity-50 dark:bg-amber-500 dark:hover:bg-amber-600"
					data-testid="brewlink-ack-btn"
				>
					{t('common.yes')} — {t(interactionPlan.confirmLabelKey)}
				</button>
				<button
					type="button"
					disabled={isOperating}
					onclick={dismissInteraction}
					class="rounded-lg border border-amber-600/30 bg-white/50 px-2.5 py-1 text-xs font-semibold text-amber-900 hover:bg-white/80 disabled:opacity-50 dark:bg-zinc-900/50 dark:text-amber-200"
					data-testid="brewlink-dismiss-btn"
				>
					{t('common.no')} — {t(interactionPlan.dismissLabelKey)}
				</button>
			</div>
		</div>
	{/if}

	<!-- Operational Message Alerts -->
	{#if operationError}
		<div
			class="rounded-xl border border-rose-500/30 bg-rose-500/10 p-2.5 text-xs text-rose-700 dark:text-rose-300"
			data-testid="brewlink-error-msg"
		>
			{operationError}
		</div>
	{/if}
	{#if operationSuccess}
		<div
			class="rounded-xl border border-emerald-500/30 bg-emerald-500/10 p-2.5 text-xs text-emerald-700 dark:text-emerald-300"
			data-testid="brewlink-success-msg"
		>
			{operationSuccess}
		</div>
	{/if}

	<!-- Grid of Controls -->
	<div class="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
		<!-- Temperature & Target Setpoint -->
		<div
			class="rounded-xl border border-zinc-200 bg-white p-3 shadow-sm dark:border-zinc-800 dark:bg-zinc-900"
		>
			<div class="flex items-center justify-between text-xs text-zinc-500 dark:text-zinc-400">
				<span class="flex items-center gap-1.5 font-semibold">
					<Thermometer class="h-4 w-4 text-sky-500" />
					{t('equipment.brewlink_current_temp')}
				</span>
				<span class="font-mono text-sm font-bold text-zinc-900 dark:text-zinc-100">
					{settings.formatTemperature(equipment.currentTemperatureC ?? localMetrics.currentTempC)}
				</span>
			</div>

			<div class="mt-3">
				<label
					for="brewlink-target-temp"
					class="text-[11px] font-semibold text-zinc-600 dark:text-zinc-300"
				>
					{t('equipment.brewlink_target_temp')} ({settings.tempShort})
				</label>
				<div class="mt-1 flex items-center gap-1">
					<button
						type="button"
						disabled={isOperating || isAutoMode}
						onclick={() => void stepTemp(-1)}
						class="flex h-8 w-8 items-center justify-center rounded-lg border border-zinc-300 bg-zinc-50 text-xs font-bold text-zinc-700 hover:bg-zinc-100 disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
						data-testid="brewlink-temp-down-btn"
					>
						-
					</button>
					<input
						id="brewlink-target-temp"
						data-testid="brewlink-target-temp-input"
						type="number"
						step="0.5"
						disabled={isAutoMode}
						value={inputTemp}
						oninput={(e) => {
							const val = e.currentTarget.valueAsNumber;
							customInputTemp = isNaN(val) ? null : val;
						}}
						class="h-8 w-full rounded-lg border border-zinc-300 bg-white px-2 text-center font-mono text-xs font-bold text-zinc-900 focus:border-sky-500 focus:outline-none disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100"
					/>
					<button
						type="button"
						disabled={isOperating || isAutoMode}
						onclick={() => void stepTemp(1)}
						class="flex h-8 w-8 items-center justify-center rounded-lg border border-zinc-300 bg-zinc-50 text-xs font-bold text-zinc-700 hover:bg-zinc-100 disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
						data-testid="brewlink-temp-up-btn"
					>
						+
					</button>
					<button
						type="button"
						disabled={isOperating || isAutoMode}
						onclick={applyTargetTemp}
						class="rounded-lg bg-sky-600 px-2.5 py-1 text-xs font-bold text-white shadow hover:bg-sky-700 disabled:opacity-50 dark:bg-sky-500 dark:hover:bg-sky-600"
						data-testid="brewlink-set-temp-btn"
					>
						<Check class="h-3.5 w-3.5" />
					</button>
				</div>
				{#if isAutoMode}
					<p class="mt-1.5 text-[10px] text-amber-600 dark:text-amber-400">
						{t('equipment.brewlink_auto_mode_locked')}
					</p>
				{/if}
			</div>
		</div>

		<!-- Heater & Read-Only Output / Delayed Heat -->
		<div
			class="rounded-xl border border-zinc-200 bg-white p-3 shadow-sm dark:border-zinc-800 dark:bg-zinc-900"
		>
			<div class="flex items-center justify-between">
				<span
					class="flex items-center gap-1.5 text-xs font-semibold text-zinc-700 dark:text-zinc-300"
				>
					<Flame
						class="h-4 w-4 {localMetrics.heatActive
							? 'animate-pulse text-rose-500'
							: 'text-zinc-400'}"
					/>
					{t('equipment.brewlink_heater')}
				</span>
				<button
					type="button"
					disabled={isOperating}
					onclick={toggleHeater}
					class="rounded-lg px-2.5 py-1 text-xs font-bold shadow-sm transition {localMetrics.heatActive
						? 'bg-rose-600 text-white hover:bg-rose-700 dark:bg-rose-500 dark:hover:bg-rose-600'
						: 'border border-zinc-300 bg-zinc-100 text-zinc-700 hover:bg-zinc-200 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300'}"
					data-testid="brewlink-toggle-heat-btn"
				>
					{localMetrics.heatActive
						? t('equipment.brewlink_heat_on')
						: t('equipment.brewlink_heat_off')}
				</button>
			</div>

			<div class="mt-3">
				<div
					class="flex items-center justify-between text-[11px] font-semibold text-zinc-500 dark:text-zinc-400"
				>
					<span>{t('equipment.brewlink_power')}</span>
					<span class="font-mono">{localMetrics.heatPowerPct ?? 0}%</span>
				</div>
				<div
					class="mt-1 h-2 w-full overflow-hidden rounded-full bg-zinc-100 dark:bg-zinc-800"
					data-testid="brewlink-power-readonly"
				>
					<div
						class="h-full rounded-full transition-all duration-300 {localMetrics.heatActive
							? 'bg-rose-500'
							: 'bg-zinc-400'}"
						style="width: {Math.max(0, Math.min(100, localMetrics.heatPowerPct ?? 0))}%"
					></div>
				</div>
				<p class="mt-1 text-[10px] text-zinc-400 dark:text-zinc-500">
					{t('equipment.brewlink_power_readonly')}
				</p>
			</div>

			{#if !isAutoMode}
				<div class="mt-2.5 border-t border-zinc-100 pt-2 dark:border-zinc-800">
					<div class="flex items-center gap-1">
						<input
							type="number"
							min="0"
							max="999"
							bind:value={delayedHeatMinutes}
							aria-label={t('equipment.brewlink_delayed_heat_min')}
							class="h-7 w-14 rounded-lg border border-zinc-300 bg-white px-1.5 text-center font-mono text-[11px] font-bold text-zinc-900 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100"
							data-testid="brewlink-delayed-min-input"
						/>
						<span class="text-[10px] text-zinc-500">{t('equipment.brewlink_delayed_heat_min')}</span
						>
						<input
							type="number"
							min="0"
							max="59"
							bind:value={delayedHeatSeconds}
							aria-label={t('equipment.brewlink_delayed_heat_sec')}
							class="h-7 w-12 rounded-lg border border-zinc-300 bg-white px-1.5 text-center font-mono text-[11px] font-bold text-zinc-900 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100"
							data-testid="brewlink-delayed-sec-input"
						/>
						<span class="text-[10px] text-zinc-500">{t('equipment.brewlink_delayed_heat_sec')}</span
						>
						<button
							type="button"
							disabled={isOperating}
							onclick={scheduleDelayedHeat}
							class="ml-auto rounded-lg border border-sky-500/30 bg-sky-500/10 px-2 py-1 text-[10px] font-bold text-sky-700 hover:bg-sky-500/20 disabled:opacity-50 dark:text-sky-300"
							data-testid="brewlink-delayed-heat-btn"
						>
							{t('equipment.brewlink_schedule_delayed_heat')}
						</button>
					</div>
				</div>
			{/if}
		</div>

		<!-- Pump Control & SET Button -->
		<div
			class="rounded-xl border border-zinc-200 bg-white p-3 shadow-sm dark:border-zinc-800 dark:bg-zinc-900"
		>
			<div class="flex items-center justify-between">
				<span
					class="flex items-center gap-1.5 text-xs font-semibold text-zinc-700 dark:text-zinc-300"
				>
					<Waves
						class="h-4 w-4 {localMetrics.pumpActive
							? 'animate-spin text-cyan-500'
							: 'text-zinc-400'}"
					/>
					{t('equipment.brewlink_pump')}
				</span>
				<button
					type="button"
					disabled={isOperating}
					onclick={togglePump}
					class="rounded-lg px-2.5 py-1 text-xs font-bold shadow-sm transition {localMetrics.pumpActive
						? 'bg-cyan-600 text-white hover:bg-cyan-700 dark:bg-cyan-500 dark:hover:bg-cyan-600'
						: 'border border-zinc-300 bg-zinc-100 text-zinc-700 hover:bg-zinc-200 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300'}"
					data-testid="brewlink-toggle-pump-btn"
				>
					{localMetrics.pumpActive
						? t('equipment.brewlink_pump_on')
						: t('equipment.brewlink_pump_off')}
				</button>
			</div>

			<div
				class="mt-3 flex items-center justify-between border-t border-zinc-100 pt-2 dark:border-zinc-800"
			>
				<span class="text-[11px] font-semibold text-zinc-500 dark:text-zinc-400">
					{localMetrics.stepName ||
						(localMetrics.stepIndex != null
							? `${t('equipment.brewlink_stage')} ${localMetrics.stepIndex}`
							: t('equipment.brewlink_controls'))}
				</span>
				<button
					type="button"
					disabled={isOperating}
					onclick={pressSet}
					class="rounded-lg border border-amber-500/40 bg-amber-500/10 px-2 py-0.5 text-xs font-bold text-amber-700 hover:bg-amber-500/20 disabled:opacity-50 dark:text-amber-300"
					data-testid="brewlink-press-set-btn"
				>
					{t('equipment.brewlink_press_set')}
				</button>
			</div>
		</div>

		<!-- Hardware Timer -->
		<div
			class="rounded-xl border border-zinc-200 bg-white p-3 shadow-sm sm:col-span-2 lg:col-span-3 dark:border-zinc-800 dark:bg-zinc-900"
		>
			<div class="flex flex-wrap items-center justify-between gap-2">
				<div class="flex items-center gap-2">
					<Timer class="h-4 w-4 text-amber-500" />
					<span class="text-xs font-semibold text-zinc-700 dark:text-zinc-300">
						{t('equipment.brewlink_timer')}:
					</span>
					<span class="font-mono text-sm font-bold text-zinc-900 dark:text-zinc-100">
						{formatRemainingTime(localMetrics.timerRemainingSec)}
					</span>
					{#if localMetrics.timerPaused}
						<span
							class="rounded bg-amber-500/20 px-1.5 py-0.5 text-[10px] font-semibold text-amber-700 dark:text-amber-300"
						>
							{t('equipment.brewlink_pause_timer')}
						</span>
					{/if}
				</div>

				<div class="flex flex-wrap items-center gap-1.5">
					{#if localMetrics.timerActive}
						{#if localMetrics.timerPaused}
							<button
								type="button"
								disabled={isOperating}
								onclick={resumeTimer}
								class="inline-flex items-center gap-1 rounded-lg border border-emerald-500/30 bg-emerald-50 px-2 py-1 text-xs font-semibold text-emerald-700 hover:bg-emerald-100 disabled:opacity-50 dark:bg-emerald-950 dark:text-emerald-300"
								data-testid="brewlink-resume-timer-btn"
							>
								<Play class="h-3 w-3" />
								<span>{t('equipment.brewlink_resume_timer')}</span>
							</button>
						{:else}
							<button
								type="button"
								disabled={isOperating}
								onclick={pauseTimer}
								class="inline-flex items-center gap-1 rounded-lg border border-amber-500/30 bg-amber-50 px-2 py-1 text-xs font-semibold text-amber-700 hover:bg-amber-100 disabled:opacity-50 dark:bg-amber-950 dark:text-amber-300"
								data-testid="brewlink-pause-timer-btn"
							>
								<Pause class="h-3 w-3" />
								<span>{t('equipment.brewlink_pause_timer')}</span>
							</button>
						{/if}
						<button
							type="button"
							disabled={isOperating || isAutoMode}
							onclick={cancelTimer}
							class="inline-flex items-center gap-1 rounded-lg border border-rose-500/30 bg-rose-50 px-2 py-1 text-xs font-semibold text-rose-700 hover:bg-rose-100 disabled:opacity-50 dark:bg-rose-950 dark:text-rose-300"
							data-testid="brewlink-cancel-timer-btn"
						>
							<Square class="h-3 w-3" />
							<span>{t('equipment.brewlink_cancel_timer')}</span>
						</button>
					{:else}
						<div class="flex items-center gap-1">
							<input
								type="number"
								min="1"
								max="600"
								disabled={isAutoMode}
								bind:value={inputTimerMin}
								class="h-7 w-16 rounded-lg border border-zinc-300 bg-white px-2 text-center font-mono text-xs font-bold text-zinc-900 focus:border-amber-500 focus:outline-none disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100"
							/>
							<span class="text-[11px] text-zinc-500 dark:text-zinc-400">
								{t('equipment.brewlink_timer_min')}
							</span>
							<button
								type="button"
								disabled={isOperating || isAutoMode}
								onclick={startTimer}
								class="rounded-lg bg-amber-600 px-2.5 py-1 text-xs font-bold text-white shadow hover:bg-amber-700 disabled:opacity-50 dark:bg-amber-500 dark:hover:bg-amber-600"
								data-testid="brewlink-set-timer-btn"
							>
								{t('equipment.brewlink_set_timer')}
							</button>
						</div>
					{/if}
				</div>
			</div>
		</div>
	</div>

	<ConfirmModal
		open={showConfirmCancelModal}
		title={t('batches.workspace.confirm_reset_timer_title')}
		message={t('batches.workspace.confirm_reset_timer_message', { equipment: equipment.name })}
		confirmText={t('common.yes')}
		cancelText={t('common.no')}
		variant="danger"
		confirmTestId="brewlink-confirm-cancel-timer-btn"
		cancelTestId="brewlink-dismiss-cancel-timer-btn"
		onConfirm={executeCancelTimer}
		onClose={() => (showConfirmCancelModal = false)}
	/>

	<ConfirmModal
		open={showConfirmCancelSessionModal}
		title={t('equipment.brewlink_confirm_cancel_session_title')}
		message={t('equipment.brewlink_confirm_cancel_session_message', { equipment: equipment.name })}
		confirmText={t('common.yes')}
		cancelText={t('common.no')}
		variant="danger"
		confirmTestId="brewlink-confirm-cancel-session-btn"
		cancelTestId="brewlink-dismiss-cancel-session-btn"
		onConfirm={executeCancelSession}
		onClose={() => (showConfirmCancelSessionModal = false)}
	/>
</div>
