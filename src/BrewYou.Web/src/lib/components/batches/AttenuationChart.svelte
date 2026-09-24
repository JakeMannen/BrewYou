<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import { formatNumber } from '$lib/utils/formatNumber';
	import type {
		BatchReadingDto,
		BatchEquipmentReadingDto,
		EquipmentSubtype,
		BrewStage
	} from '$lib/types/api';
	import VesselIcon from '$lib/components/inventory/VesselIcon.svelte';
	import { Activity, Plus, Droplets } from '@lucide/svelte';

	interface Props {
		readings?: BatchReadingDto[];
		equipmentReadings?: BatchEquipmentReadingDto[];
		targetOg?: number;
		targetFg?: number;
		targetTemperatureC?: number | null;
		stage?: BrewStage;
		equipmentName?: string | null;
		sensorSubtype?: EquipmentSubtype | null;
		isLive?: boolean;
		daysActive?: number;
		canLog?: boolean;
		onOpenLogReading?: () => void;
		onOpenLogTemperature?: () => void;
		class?: string;
	}

	let {
		readings = [],
		equipmentReadings = [],
		targetOg = 1.05,
		targetFg = 1.01,
		targetTemperatureC = 20.0,
		stage = 'Ferment',
		equipmentName = null,
		sensorSubtype = null,
		isLive = false,
		daysActive,
		canLog = false,
		onOpenLogReading,
		onOpenLogTemperature,
		class: className = ''
	}: Props = $props();

	// SVG Dimensions
	const width = 720;
	const height = 260;
	const padding = { top: 25, right: 65, bottom: 40, left: 65 };

	const innerWidth = width - padding.left - padding.right;
	const innerHeight = height - padding.top - padding.bottom;

	// Helper to display temperature in user preference
	function displayTemp(tempC: number): number {
		return settings.temperatureUnit === 'Fahrenheit' ? (tempC * 9) / 5 + 32 : tempC;
	}

	let unitLabel = $derived(settings.temperatureUnit === 'Fahrenheit' ? '°F' : '°C');

	// Unified data point structures
	interface GravityPoint {
		id: string;
		time: number;
		sg: number;
		tempC?: number | null;
		source: string;
		isSensor: boolean;
	}

	interface TemperaturePoint {
		id: string;
		time: number;
		tempC: number;
		sg?: number | null;
		source: string;
		isSensor: boolean;
	}

	// Extract and merge SG points
	let sgPoints = $derived.by<GravityPoint[]>(() => {
		const points: GravityPoint[] = [];

		// Manual batch readings
		for (const r of readings) {
			const time = new Date(r.timestamp).getTime();
			if (!isNaN(time) && r.specificGravity) {
				points.push({
					id: r.id,
					time,
					sg: r.specificGravity,
					tempC: r.temperatureC,
					source: r.notes || 'Manual',
					isSensor: false
				});
			}
		}

		// Equipment readings with SG (e.g. from Tilt, iSpindel)
		for (const er of equipmentReadings) {
			if (er.specificGravity !== null && er.specificGravity !== undefined) {
				const time = new Date(er.timestamp).getTime();
				if (!isNaN(time)) {
					// Avoid exact duplicates with manual readings
					if (!points.some((p) => p.id === er.id || Math.abs(p.time - time) < 2000)) {
						points.push({
							id: er.id,
							time,
							sg: er.specificGravity,
							tempC: er.temperatureC,
							source: er.equipmentName || er.source || 'Sensor',
							isSensor: true
						});
					}
				}
			}
		}

		return points.sort((a, b) => a.time - b.time);
	});

	// Extract and merge Temperature points
	let tempPoints = $derived.by<TemperaturePoint[]>(() => {
		const points: TemperaturePoint[] = [];

		// Equipment readings
		for (const er of equipmentReadings) {
			const time = new Date(er.timestamp).getTime();
			if (!isNaN(time) && er.temperatureC !== null && er.temperatureC !== undefined) {
				points.push({
					id: er.id,
					time,
					tempC: er.temperatureC,
					sg: er.specificGravity,
					source: er.equipmentName || er.source || 'Sensor',
					isSensor: true
				});
			}
		}

		// Manual readings with temp
		for (const r of readings) {
			if (r.temperatureC !== null && r.temperatureC !== undefined) {
				const time = new Date(r.timestamp).getTime();
				if (!isNaN(time)) {
					if (!points.some((p) => p.id === r.id || Math.abs(p.time - time) < 2000)) {
						points.push({
							id: r.id,
							time,
							tempC: r.temperatureC,
							sg: r.specificGravity,
							source: r.notes || 'Manual',
							isSensor: false
						});
					}
				}
			}
		}

		return points.sort((a, b) => a.time - b.time);
	});

	// Time Domain
	let allTimestamps = $derived.by<number[]>(() => {
		const times: number[] = [];
		for (const p of sgPoints) times.push(p.time);
		for (const p of tempPoints) times.push(p.time);
		return times.sort((a, b) => a - b);
	});

	let minTime = $derived(allTimestamps.length > 0 ? allTimestamps[0] : 0);
	let maxTime = $derived(allTimestamps.length > 0 ? allTimestamps[allTimestamps.length - 1] : 0);
	let timeSpan = $derived(maxTime - minTime);

	function getX(time: number): number {
		if (timeSpan <= 0) return padding.left + innerWidth / 2;
		const norm = (time - minTime) / timeSpan;
		return padding.left + norm * innerWidth;
	}

	// Specific Gravity Domain (Left Y-Axis)
	let allSgValues = $derived.by<number[]>(() => {
		const list = [targetOg, targetFg];
		for (const p of sgPoints) list.push(p.sg);
		return list;
	});

	let minSg = $derived(Math.min(targetFg - 0.003, ...allSgValues));
	let maxSg = $derived(Math.max(targetOg + 0.003, ...allSgValues));

	function getSgY(val: number): number {
		const range = maxSg - minSg || 0.01;
		const norm = (val - minSg) / range;
		return padding.top + innerHeight - norm * innerHeight;
	}

	// Temperature Domain (Right Y-Axis)
	let effectiveTargetC = $derived(targetTemperatureC ?? (stage === 'Condition' ? 2.0 : 20.0));
	let convertedTargetTemp = $derived(
		effectiveTargetC !== null ? displayTemp(effectiveTargetC) : null
	);

	let allTempValues = $derived.by<number[]>(() => {
		const list: number[] = [];
		if (convertedTargetTemp !== null) list.push(convertedTargetTemp);
		for (const p of tempPoints) list.push(displayTemp(p.tempC));
		return list;
	});

	let minTemp = $derived.by(() => {
		if (allTempValues.length === 0) return settings.temperatureUnit === 'Fahrenheit' ? 50 : 10;
		const min = Math.min(...allTempValues);
		return Math.floor(min - (settings.temperatureUnit === 'Fahrenheit' ? 4 : 2));
	});

	let maxTemp = $derived.by(() => {
		if (allTempValues.length === 0) return settings.temperatureUnit === 'Fahrenheit' ? 85 : 30;
		const max = Math.max(...allTempValues);
		return Math.ceil(max + (settings.temperatureUnit === 'Fahrenheit' ? 4 : 2));
	});

	function getTempY(val: number): number {
		const range = maxTemp - minTemp || 5;
		const norm = (val - minTemp) / range;
		return padding.top + innerHeight - norm * innerHeight;
	}

	// SVG Paths
	let gravityPath = $derived.by(() => {
		if (sgPoints.length === 0) return '';
		return sgPoints
			.map((p, i) => {
				const x = getX(p.time).toFixed(1);
				const y = getSgY(p.sg).toFixed(1);
				return `${i === 0 ? 'M' : 'L'} ${x} ${y}`;
			})
			.join(' ');
	});

	let tempPath = $derived.by(() => {
		if (tempPoints.length === 0) return '';
		return tempPoints
			.map((p, i) => {
				const x = getX(p.time).toFixed(1);
				const y = getTempY(displayTemp(p.tempC)).toFixed(1);
				return `${i === 0 ? 'M' : 'L'} ${x} ${y}`;
			})
			.join(' ');
	});

	let tempAreaPath = $derived.by(() => {
		if (tempPoints.length === 0) return '';
		const firstX = getX(tempPoints[0].time).toFixed(1);
		const lastX = getX(tempPoints[tempPoints.length - 1].time).toFixed(1);
		const bottomY = (padding.top + innerHeight).toFixed(1);
		return `${tempPath} L ${lastX} ${bottomY} L ${firstX} ${bottomY} Z`;
	});

	// Grid ticks
	let sgTicks = $derived.by(() => {
		const count = 5;
		const step = (maxSg - minSg) / (count - 1);
		const ticks: number[] = [];
		for (let i = 0; i < count; i++) {
			ticks.push(minSg + i * step);
		}
		return ticks;
	});

	let tempTicks = $derived.by(() => {
		const count = 5;
		const step = (maxTemp - minTemp) / (count - 1);
		const ticks: number[] = [];
		for (let i = 0; i < count; i++) {
			ticks.push(Math.round(minTemp + i * step));
		}
		return ticks;
	});

	// X-axis date milestones
	let timeTicks = $derived.by(() => {
		if (allTimestamps.length < 2) return [];
		const count = Math.min(5, Math.max(2, allTimestamps.length));
		const step = timeSpan / (count - 1);
		const ticks: number[] = [];
		for (let i = 0; i < count; i++) {
			ticks.push(minTime + i * step);
		}
		return ticks;
	});

	// Interactive hover state
	let hoveredSgIndex = $state<number | null>(null);
	let hoveredTempIndex = $state<number | null>(null);

	let activeSgPoint = $derived(hoveredSgIndex !== null ? sgPoints[hoveredSgIndex] : null);
	let activeTempPoint = $derived(hoveredTempIndex !== null ? tempPoints[hoveredTempIndex] : null);

	// Latest KPIs
	let latestSgPoint = $derived(sgPoints.length > 0 ? sgPoints[sgPoints.length - 1] : null);
	let latestTempPoint = $derived(tempPoints.length > 0 ? tempPoints[tempPoints.length - 1] : null);

	let currentSg = $derived(latestSgPoint ? latestSgPoint.sg : null);
	let currentTempC = $derived(
		latestTempPoint
			? latestTempPoint.tempC
			: latestSgPoint?.tempC !== null && latestSgPoint?.tempC !== undefined
				? latestSgPoint.tempC
				: null
	);

	let apparentAttenuation = $derived.by(() => {
		if (!currentSg || targetOg <= 1.0) return null;
		const totalDrop = targetOg - 1.0;
		const currentDrop = targetOg - currentSg;
		return Math.max(0, Math.min(100, (currentDrop / totalDrop) * 100));
	});

	let progressToFg = $derived.by(() => {
		if (!currentSg || targetOg <= targetFg) return null;
		const targetDrop = targetOg - targetFg;
		const currentDrop = targetOg - currentSg;
		return Math.max(0, Math.min(100, Math.round((currentDrop / targetDrop) * 100)));
	});

	let deltaFromTargetTemp = $derived.by(() => {
		if (currentTempC === null || effectiveTargetC === null) return null;
		return currentTempC - effectiveTargetC;
	});

	let pointsToFg = $derived.by(() => {
		if (!currentSg || !targetFg) return null;
		const diff = currentSg - targetFg;
		return Math.max(0, Math.round(diff * 1000));
	});

	let totalDropPoints = $derived.by(() => {
		if (!currentSg || !targetOg) return null;
		const drop = targetOg - currentSg;
		return Math.max(0, Math.round(drop * 1000));
	});

	let totalReadingsCount = $derived(sgPoints.length + tempPoints.length);

	let hasAnyData = $derived(sgPoints.length > 0 || tempPoints.length > 0);
</script>

<div
	class="glass-panel relative flex flex-col rounded-3xl border border-zinc-200/80 p-5 shadow-xs dark:border-white/10 {className}"
>
	<!-- Header & Controls -->
	<div
		class="flex flex-wrap items-center justify-between gap-3 border-b border-zinc-200/60 pb-4 dark:border-white/5"
	>
		<div class="flex items-center gap-3">
			{#if sensorSubtype}
				<div class="flex h-9 w-9 shrink-0 items-center justify-center">
					<VesselIcon subtype={sensorSubtype} active={isLive} class="h-9 w-9" />
				</div>
			{:else}
				<div
					class="flex h-9 w-9 shrink-0 items-center justify-center rounded-2xl bg-amber-500/10 text-amber-600 dark:bg-amber-500/20 dark:text-amber-400"
				>
					<Activity class="h-5 w-5" />
				</div>
			{/if}

			<div>
				<div class="flex items-center gap-2">
					<h3 class="text-sm font-bold text-zinc-900 dark:text-white">
						{t('batches.telemetry.fermentation_chart_title')}
					</h3>

					{#if isLive}
						<span
							class="inline-flex items-center gap-1.5 rounded-full border border-emerald-500/30 bg-emerald-500/10 px-2 py-0.5 text-[10px] font-bold tracking-wider text-emerald-600 uppercase dark:border-emerald-400/30 dark:bg-emerald-400/10 dark:text-emerald-400"
							title={t('batches.telemetry.live_streaming')}
						>
							<span class="relative flex h-1.5 w-1.5">
								<span
									class="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75"
								></span>
								<span class="relative inline-flex h-1.5 w-1.5 rounded-full bg-emerald-500"></span>
							</span>
							<span>{t('batches.telemetry.live_badge')}</span>
						</span>
					{/if}
				</div>

				<div class="flex items-center gap-2 text-xs text-zinc-500 dark:text-zinc-400">
					{#if equipmentName}
						<span>{equipmentName}</span>
					{/if}
					{#if daysActive !== undefined}
						<span>•</span>
						<span class="font-semibold"
							>{t('batches.telemetry.days_in_fermenter_badge', { days: daysActive })}</span
						>
					{/if}
				</div>
			</div>
		</div>

		<!-- Action Buttons -->
		<div class="flex flex-wrap items-center gap-2">
			{#if canLog && onOpenLogReading}
				<button
					type="button"
					data-testid="log-sg-reading-btn"
					onclick={onOpenLogReading}
					class="flex cursor-pointer items-center gap-1.5 rounded-xl border border-zinc-200 bg-white/80 px-3 py-1.5 text-xs font-semibold text-zinc-700 shadow-xs backdrop-blur-xs transition-colors hover:bg-zinc-100 hover:text-zinc-900 dark:border-white/10 dark:bg-zinc-800/80 dark:text-zinc-200 dark:hover:bg-zinc-700"
				>
					<Plus class="h-3.5 w-3.5" />
					<span>{t('batches.telemetry.log_sg_button')}</span>
				</button>
			{/if}

			{#if canLog && onOpenLogTemperature}
				<button
					type="button"
					onclick={onOpenLogTemperature}
					class="flex cursor-pointer items-center gap-1.5 rounded-xl border border-zinc-200 bg-white/80 px-3 py-1.5 text-xs font-semibold text-zinc-700 shadow-xs backdrop-blur-xs transition-colors hover:bg-zinc-100 hover:text-zinc-900 dark:border-white/10 dark:bg-zinc-800/80 dark:text-zinc-200 dark:hover:bg-zinc-700"
				>
					<Droplets class="h-3.5 w-3.5" />
					<span>{t('batches.telemetry.log_temp_button')}</span>
				</button>
			{/if}
		</div>
	</div>

	<!-- KPIs Ribbon -->
	<div class="mt-4 grid grid-cols-2 gap-2 sm:grid-cols-4">
		<!-- Progress to FG -->
		<div
			class="rounded-2xl border border-zinc-200/60 bg-zinc-50/50 p-3 dark:border-white/5 dark:bg-white/[0.02]"
		>
			<div class="text-[11px] font-medium text-zinc-500 dark:text-zinc-400">
				{t('batches.telemetry.progress_to_fg_title')}
			</div>
			<div class="mt-1 flex items-baseline gap-2">
				<span class="font-mono text-lg font-bold text-amber-600 dark:text-amber-400">
					{progressToFg !== null ? `${progressToFg}%` : '—'}
				</span>
				{#if progressToFg !== null && progressToFg >= 100}
					<span class="text-[11px] font-semibold text-emerald-500">
						{t('batches.telemetry.target_reached')}
					</span>
				{/if}
			</div>
			<div class="mt-0.5 text-[10px] text-zinc-400">
				{#if pointsToFg !== null}
					{pointsToFg > 0
						? t('batches.telemetry.points_remaining', { points: pointsToFg })
						: t('batches.telemetry.target_reached')}
				{:else}
					{t('batches.telemetry.target_fg')}: {formatNumber(targetFg, 3)}
				{/if}
			</div>
		</div>

		<!-- Current Temp & Delta -->
		<div
			class="rounded-2xl border border-zinc-200/60 bg-zinc-50/50 p-3 dark:border-white/5 dark:bg-white/[0.02]"
		>
			<div class="text-[11px] font-medium text-zinc-500 dark:text-zinc-400">
				{t('batches.telemetry.current_temp')}
			</div>
			<div class="mt-1 flex items-baseline gap-2">
				<span class="font-mono text-lg font-bold text-cyan-600 dark:text-cyan-400">
					{currentTempC !== null ? `${displayTemp(currentTempC).toFixed(1)}${unitLabel}` : '—'}
				</span>
				{#if deltaFromTargetTemp !== null}
					<span
						class="text-[11px] font-semibold {Math.abs(deltaFromTargetTemp) <= 0.5
							? 'text-emerald-500'
							: deltaFromTargetTemp > 0
								? 'text-amber-500'
								: 'text-blue-500'}"
					>
						{deltaFromTargetTemp > 0 ? '+' : ''}{displayTemp(deltaFromTargetTemp).toFixed(1)}°
					</span>
				{/if}
			</div>
			<div class="mt-0.5 text-[10px] text-zinc-400">
				{t('batches.telemetry.target_temp')}: {convertedTargetTemp !== null
					? `${convertedTargetTemp.toFixed(1)}${unitLabel}`
					: '—'}
			</div>
		</div>

		<!-- Apparent Attenuation -->
		<div
			class="rounded-2xl border border-zinc-200/60 bg-zinc-50/50 p-3 dark:border-white/5 dark:bg-white/[0.02]"
		>
			<div class="text-[11px] font-medium text-zinc-500 dark:text-zinc-400">
				{t('batches.telemetry.attenuation_label')}
			</div>
			<div class="mt-1 flex items-baseline gap-2">
				<span class="font-mono text-lg font-bold text-zinc-900 dark:text-white">
					{apparentAttenuation !== null ? `${apparentAttenuation.toFixed(1)}%` : '—'}
				</span>
			</div>
			<div class="mt-0.5 text-[10px] text-zinc-400">
				{#if totalDropPoints !== null}
					{t('batches.telemetry.points_dropped', { points: totalDropPoints })}
				{:else}
					{t('batches.telemetry.target_og')}: {formatNumber(targetOg, 3)}
				{/if}
			</div>
		</div>

		<!-- Time in Fermenter -->
		<div
			class="rounded-2xl border border-zinc-200/60 bg-zinc-50/50 p-3 dark:border-white/5 dark:bg-white/[0.02]"
		>
			<div class="text-[11px] font-medium text-zinc-500 dark:text-zinc-400">
				{t('batches.telemetry.fermenter_time_label')}
			</div>
			<div class="mt-1 flex items-baseline gap-2">
				<span class="font-mono text-lg font-bold text-emerald-600 dark:text-emerald-400">
					{#if daysActive !== undefined}
						{t('batches.telemetry.days_in_fermenter_badge', { days: daysActive })}
					{:else if allTimestamps.length >= 2}
						{t('batches.telemetry.days_in_fermenter_badge', {
							days: Math.max(
								1,
								Math.ceil(
									(allTimestamps[allTimestamps.length - 1] - allTimestamps[0]) /
										(1000 * 60 * 60 * 24)
								)
							)
						})}
					{:else}
						—
					{/if}
				</span>
			</div>
			<div class="mt-0.5 text-[10px] text-zinc-400">
				{t('batches.telemetry.readings_count')}: {totalReadingsCount}
			</div>
		</div>
	</div>

	<!-- Chart Legend -->
	<div class="mt-4 flex flex-wrap items-center justify-between gap-3 text-xs font-semibold">
		<div class="flex flex-wrap items-center gap-4">
			<span class="flex items-center gap-1.5 text-amber-600 dark:text-amber-400">
				<span class="h-0.5 w-3.5 rounded-full bg-amber-500"></span>
				{t('batches.telemetry.gravity_sg')}
			</span>
			<span class="flex items-center gap-1.5 text-cyan-600 dark:text-cyan-400">
				<span class="h-0.5 w-3.5 rounded-full bg-cyan-500"></span>
				{t('batches.telemetry.temperature')}
			</span>
			<span class="flex items-center gap-1.5 text-zinc-500 dark:text-zinc-400">
				<span class="h-0.5 w-3 border-t border-dashed border-emerald-500"></span>
				{t('batches.telemetry.target_fg')}: {formatNumber(targetFg, 3)}
			</span>
			{#if effectiveTargetC !== null}
				<span class="flex items-center gap-1.5 text-zinc-500 dark:text-zinc-400">
					<span class="h-0.5 w-3 border-t border-dotted border-cyan-500"></span>
					{t('batches.telemetry.target_temp')}: {settings.formatTemp(effectiveTargetC)}
				</span>
			{/if}
		</div>

		{#if activeSgPoint || activeTempPoint}
			<div class="font-mono text-xs font-medium text-zinc-700 dark:text-zinc-300">
				{#if activeSgPoint}
					<span
						>SG: <strong class="text-amber-500">{formatNumber(activeSgPoint.sg, 3)}</strong></span
					>
				{/if}
				{#if activeTempPoint}
					<span class="ml-2"
						>Temp: <strong class="text-cyan-500"
							>{settings.formatTemp(activeTempPoint.tempC)}</strong
						></span
					>
				{/if}
			</div>
		{/if}
	</div>

	<!-- Chart Canvas or Empty State -->
	{#if !hasAnyData}
		<div
			class="flex flex-col items-center justify-center rounded-2xl border border-dashed border-zinc-200/70 py-12 text-center dark:border-white/10"
		>
			<Activity class="h-8 w-8 text-zinc-300 dark:text-zinc-600" />
			<p class="mt-2 text-xs font-semibold text-zinc-600 dark:text-zinc-300">
				{t('batches.telemetry.no_fermentation_data')}
			</p>
			<p class="mt-1 max-w-sm text-[11px] text-zinc-400">
				{t('batches.telemetry.waiting_for_telemetry')}
			</p>
			{#if canLog && onOpenLogReading}
				<button
					type="button"
					onclick={onOpenLogReading}
					class="mt-3 cursor-pointer rounded-xl bg-amber-500 px-3.5 py-1.5 text-xs font-semibold text-white shadow-xs transition-colors hover:bg-amber-600"
				>
					{t('batches.telemetry.log_sg_button')}
				</button>
			{/if}
		</div>
	{:else}
		<div class="relative mt-3 w-full overflow-hidden">
			<svg
				viewBox="0 0 {width} {height}"
				class="h-auto w-full overflow-visible select-none"
				aria-hidden="true"
			>
				<defs>
					<linearGradient id="fermentTempGradient" x1="0" y1="0" x2="0" y2="1">
						<stop offset="0%" stop-color="#06b6d4" stop-opacity="0.25" />
						<stop offset="100%" stop-color="#06b6d4" stop-opacity="0.0" />
					</linearGradient>
				</defs>

				<!-- Horizontal Grid Lines for SG & Left Axis Labels -->
				{#each sgTicks as tickVal}
					{@const yPos = getSgY(tickVal)}
					<line
						x1={padding.left}
						x2={width - padding.right}
						y1={yPos}
						y2={yPos}
						stroke="currentColor"
						stroke-opacity="0.08"
						stroke-width="1"
					/>
					<text
						x={padding.left - 8}
						y={yPos + 3.5}
						text-anchor="end"
						font-size="10"
						class="fill-amber-600/80 font-mono dark:fill-amber-400/80"
					>
						{tickVal.toFixed(3)}
					</text>
				{/each}

				<!-- Right Axis Labels for Temperature -->
				{#each tempTicks as tickVal}
					{@const yPos = getTempY(tickVal)}
					<text
						x={width - padding.right + 8}
						y={yPos + 3.5}
						text-anchor="start"
						font-size="10"
						class="fill-cyan-600/80 font-mono dark:fill-cyan-400/80"
					>
						{tickVal}{unitLabel}
					</text>
				{/each}

				<!-- Target FG Horizontal Line -->
				<line
					x1={padding.left}
					x2={width - padding.right}
					y1={getSgY(targetFg)}
					y2={getSgY(targetFg)}
					stroke="#10b981"
					stroke-dasharray="4 4"
					stroke-width="1"
				/>
				<text
					x={width - padding.right - 6}
					y={getSgY(targetFg) - 5}
					text-anchor="end"
					font-size="10"
					class="fill-emerald-600 font-mono font-bold dark:fill-emerald-400"
				>
					FG {formatNumber(targetFg, 3)}
				</text>

				<!-- Target OG Horizontal Line -->
				<line
					x1={padding.left}
					x2={width - padding.right}
					y1={getSgY(targetOg)}
					y2={getSgY(targetOg)}
					stroke="#f59e0b"
					stroke-dasharray="3 3"
					stroke-width="1"
					stroke-opacity="0.4"
				/>
				<text
					x={padding.left + 6}
					y={getSgY(targetOg) - 5}
					text-anchor="start"
					font-size="9"
					class="fill-amber-600/70 font-mono dark:fill-amber-400/70"
				>
					OG {formatNumber(targetOg, 3)}
				</text>

				<!-- Target Temperature Line -->
				{#if convertedTargetTemp !== null}
					{@const tY = getTempY(convertedTargetTemp)}
					<line
						x1={padding.left}
						x2={width - padding.right}
						y1={tY}
						y2={tY}
						stroke="#06b6d4"
						stroke-dasharray="3 3"
						stroke-width="1"
						stroke-opacity="0.6"
					/>
				{/if}

				<!-- Temperature Gradient Area -->
				{#if tempAreaPath}
					<path d={tempAreaPath} fill="url(#fermentTempGradient)" />
				{/if}

				<!-- Temperature Line (Cyan) -->
				{#if tempPath}
					<path
						d={tempPath}
						fill="none"
						stroke="#06b6d4"
						stroke-width="1.5"
						stroke-linecap="round"
						stroke-linejoin="round"
					/>
				{/if}

				<!-- Gravity Line (Amber) -->
				{#if gravityPath}
					<path
						d={gravityPath}
						fill="none"
						stroke="#f59e0b"
						stroke-width="1.5"
						stroke-linecap="round"
						stroke-linejoin="round"
					/>
				{/if}

				<!-- Single Point Fallbacks (when only 1 reading exists) -->
				{#if tempPoints.length === 1}
					<circle
						cx={getX(tempPoints[0].time)}
						cy={getTempY(displayTemp(tempPoints[0].tempC))}
						r="3"
						class="fill-cyan-500 stroke-white dark:stroke-zinc-900"
						stroke-width="1.5"
					/>
				{/if}
				{#if sgPoints.length === 1}
					<circle
						cx={getX(sgPoints[0].time)}
						cy={getSgY(sgPoints[0].sg)}
						r="3"
						class="fill-amber-500 stroke-white dark:stroke-zinc-900"
						stroke-width="1.5"
					/>
				{/if}

				<!-- Temperature Reading Hit Targets (no dots on data points) -->
				{#each tempPoints as tp, i}
					{@const cx = getX(tp.time)}
					{@const cy = getTempY(displayTemp(tp.tempC))}
					<circle
						{cx}
						{cy}
						r="8"
						role="button"
						tabindex="-1"
						aria-label="{tp.source}: {settings.formatTemp(tp.tempC)}"
						class="cursor-pointer fill-transparent stroke-transparent"
						onpointerenter={() => (hoveredTempIndex = i)}
						onpointerleave={() => (hoveredTempIndex = null)}
					/>
					{#if hoveredTempIndex === i}
						<circle
							{cx}
							{cy}
							r="3.5"
							class="pointer-events-none fill-cyan-500 stroke-white dark:stroke-zinc-900"
							stroke-width="1.5"
						/>
					{/if}
				{/each}

				<!-- Gravity Reading Hit Targets (no dots on data points) -->
				{#each sgPoints as gp, i}
					{@const cx = getX(gp.time)}
					{@const cy = getSgY(gp.sg)}
					<circle
						{cx}
						{cy}
						r="8"
						role="button"
						tabindex="-1"
						aria-label="{gp.source}: {formatNumber(gp.sg, 3)} SG"
						class="cursor-pointer fill-transparent stroke-transparent"
						onpointerenter={() => (hoveredSgIndex = i)}
						onpointerleave={() => (hoveredSgIndex = null)}
					/>
					{#if hoveredSgIndex === i}
						<circle
							{cx}
							{cy}
							r="3.5"
							class="pointer-events-none fill-amber-500 stroke-white dark:stroke-zinc-900"
							stroke-width="1.5"
						/>
					{/if}
				{/each}

				<!-- X-Axis Timeline Labels -->
				{#each timeTicks as tVal}
					{@const xPos = getX(tVal)}
					<line
						x1={xPos}
						x2={xPos}
						y1={padding.top + innerHeight}
						y2={padding.top + innerHeight + 5}
						stroke="currentColor"
						stroke-opacity="0.2"
						stroke-width="1"
					/>
					<text
						x={xPos}
						y={padding.top + innerHeight + 18}
						text-anchor="middle"
						font-size="10"
						class="fill-zinc-400 font-mono"
					>
						{new Date(tVal).toLocaleDateString([], { month: 'short', day: 'numeric' })}
					</text>
				{/each}
			</svg>

			<!-- Hover Tooltip -->
			{#if activeSgPoint || activeTempPoint}
				{@const point = activeSgPoint || activeTempPoint}
				<div
					class="pointer-events-none absolute top-2 right-4 rounded-xl border border-zinc-200/80 bg-white/95 px-3 py-2.5 text-xs shadow-md backdrop-blur-md dark:border-zinc-800 dark:bg-zinc-900/95"
				>
					<div class="flex items-center gap-3 font-mono font-bold text-zinc-900 dark:text-white">
						{#if activeSgPoint}
							<span class="text-amber-600 dark:text-amber-400">
								{formatNumber(activeSgPoint.sg, 3)} SG
							</span>
						{/if}
						{#if activeTempPoint}
							<span class="text-cyan-600 dark:text-cyan-400">
								{settings.formatTemp(activeTempPoint.tempC)}
							</span>
						{/if}
					</div>

					<div class="mt-1 text-[11px] text-zinc-500 dark:text-zinc-400">
						{new Date(point!.time).toLocaleString([], {
							month: 'short',
							day: 'numeric',
							hour: '2-digit',
							minute: '2-digit'
						})}
						{#if point?.source}
							<span> • {point.source}</span>
						{/if}
					</div>

					{#if activeSgPoint && targetOg > 1.0}
						{@const att = Math.max(
							0,
							Math.min(100, ((targetOg - activeSgPoint.sg) / (targetOg - 1.0)) * 100)
						)}
						{@const abv = (targetOg - activeSgPoint.sg) * 131.25}
						<div class="mt-1 flex items-center gap-2 text-[10px] text-zinc-400">
							<span>{formatNumber(att, 1)}% Atten</span>
							<span>•</span>
							<span>{formatNumber(abv, 1)}% ABV</span>
						</div>
					{/if}
				</div>
			{/if}

			<!-- Screen Reader Accessible Fallback Table -->
			<table class="sr-only">
				<caption>{t('batches.telemetry.table_caption')}</caption>
				<thead>
					<tr>
						<th scope="col">{t('batches.telemetry.time')}</th>
						<th scope="col">{t('batches.telemetry.gravity_sg')}</th>
						<th scope="col">{t('batches.telemetry.temperature_col')}</th>
						<th scope="col">{t('batches.telemetry.source_col')}</th>
					</tr>
				</thead>
				<tbody>
					{#each sgPoints as p}
						<tr>
							<td>{new Date(p.time).toLocaleString()}</td>
							<td>{formatNumber(p.sg, 3)}</td>
							<td>{p.tempC !== null && p.tempC !== undefined ? `${p.tempC}°C` : 'N/A'}</td>
							<td>{p.source}</td>
						</tr>
					{/each}
				</tbody>
			</table>
		</div>
	{/if}
</div>
