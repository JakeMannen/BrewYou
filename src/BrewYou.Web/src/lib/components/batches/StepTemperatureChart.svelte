<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import { formatNumber } from '$lib/utils/formatNumber';
	import type { BatchEquipmentReadingDto, BatchMashStepDto, BrewStage } from '$lib/types/api';
	import { Thermometer, Plus } from '@lucide/svelte';

	interface Props {
		readings: BatchEquipmentReadingDto[];
		stage: BrewStage;
		mashSteps?: BatchMashStepDto[];
		selectedStepId?: string | null;
		targetTemperatureC?: number | null;
		equipmentName?: string | null;
		isLive?: boolean;
		onSelectStep?: (stepId: string | null) => void;
		onOpenLogModal?: () => void;
		class?: string;
	}

	let {
		readings = [],
		stage,
		mashSteps = [],
		selectedStepId = null,
		targetTemperatureC = null,
		equipmentName = null,
		isLive = false,
		onSelectStep,
		onOpenLogModal,
		class: className = ''
	}: Props = $props();

	const width = 700;
	const height = 240;
	const padding = { top: 30, right: 35, bottom: 40, left: 60 };

	const innerWidth = width - padding.left - padding.right;
	const innerHeight = height - padding.top - padding.bottom;

	// Filter readings for this stage and selectedStepId if present
	let stageReadings = $derived.by(() => {
		return readings.filter((r) => !r.stage || r.stage === stage);
	});

	let displayedReadings = $derived.by(() => {
		if (!selectedStepId) return stageReadings;
		return stageReadings.filter((r) => r.batchMashStepId === selectedStepId);
	});

	let isCurrentStepCompleted = $derived.by(() => {
		if (selectedStepId && mashSteps.length > 0) {
			const s = mashSteps.find((x) => x.id === selectedStepId);
			return s ? s.isCompleted : false;
		}
		return false;
	});

	// Helper to display temperature in user's unit preference
	function displayTemp(tempC: number): number {
		return settings.temperatureUnit === 'Fahrenheit' ? (tempC * 9) / 5 + 32 : tempC;
	}

	let unitLabel = $derived(settings.temperatureUnit === 'Fahrenheit' ? '°F' : '°C');

	// Compute temperature domain
	let effectiveTargetC = $derived.by<number | null>(() => {
		if (selectedStepId && mashSteps.length > 0) {
			const found = mashSteps.find((s) => s.id === selectedStepId);
			if (found) return found.targetTemperatureC;
		}
		if (targetTemperatureC !== null && targetTemperatureC !== undefined) return targetTemperatureC;
		if (stage === 'Mash' && mashSteps.length > 0) {
			const active = mashSteps.find((s) => !s.isCompleted) ?? mashSteps[0];
			return active.targetTemperatureC;
		}
		if (stage === 'Mash') return 65.0;
		if (stage === 'Boil') return 100.0;
		if (stage === 'Ferment') return 20.0;
		if (stage === 'Condition') return 2.0;
		return null;
	});

	let convertedTarget = $derived(effectiveTargetC !== null ? displayTemp(effectiveTargetC) : null);

	let allTemps = $derived.by<number[]>(() => {
		const list = displayedReadings.map((r) => displayTemp(r.temperatureC));
		if (stage === 'Mash' && !selectedStepId && mashSteps.length > 0) {
			for (const step of mashSteps) {
				list.push(displayTemp(step.targetTemperatureC));
			}
		} else if (convertedTarget !== null) {
			list.push(convertedTarget);
		}
		return list;
	});

	let minTemp = $derived.by(() => {
		if (allTemps.length === 0) return settings.temperatureUnit === 'Fahrenheit' ? 50 : 10;
		const min = Math.min(...allTemps);
		return Math.floor(min - (settings.temperatureUnit === 'Fahrenheit' ? 5 : 2));
	});

	let maxTemp = $derived.by(() => {
		if (allTemps.length === 0) return settings.temperatureUnit === 'Fahrenheit' ? 220 : 105;
		const max = Math.max(...allTemps);
		return Math.ceil(max + (settings.temperatureUnit === 'Fahrenheit' ? 5 : 2));
	});

	function getX(index: number, total: number) {
		if (total <= 1) return padding.left + innerWidth / 2;
		return padding.left + (index / (total - 1)) * innerWidth;
	}

	function getY(tempVal: number) {
		const range = maxTemp - minTemp || 1;
		const norm = (tempVal - minTemp) / range;
		return padding.top + innerHeight - norm * innerHeight;
	}

	// Line and Area Generators
	let tempPath = $derived.by(() => {
		if (displayedReadings.length === 0) return '';
		return displayedReadings
			.map((r, i) => {
				const x = getX(i, displayedReadings.length).toFixed(1);
				const y = getY(displayTemp(r.temperatureC)).toFixed(1);
				return `${i === 0 ? 'M' : 'L'} ${x} ${y}`;
			})
			.join(' ');
	});

	let areaPath = $derived.by(() => {
		if (displayedReadings.length === 0) return '';
		const firstX = getX(0, displayedReadings.length).toFixed(1);
		const lastX = getX(displayedReadings.length - 1, displayedReadings.length).toFixed(1);
		const bottomY = (padding.top + innerHeight).toFixed(1);
		return `${tempPath} L ${lastX} ${bottomY} L ${firstX} ${bottomY} Z`;
	});

	let hoveredIndex = $state<number | null>(null);
	let activeReading = $derived(
		hoveredIndex !== null && displayedReadings[hoveredIndex]
			? displayedReadings[hoveredIndex]
			: null
	);

	// Summary stats
	let latestReading = $derived(
		displayedReadings.length > 0 ? displayedReadings[displayedReadings.length - 1] : null
	);

	let currentTempC = $derived(latestReading ? latestReading.temperatureC : null);

	let minReadingC = $derived(
		displayedReadings.length > 0 ? Math.min(...displayedReadings.map((r) => r.temperatureC)) : null
	);

	let maxReadingC = $derived(
		displayedReadings.length > 0 ? Math.max(...displayedReadings.map((r) => r.temperatureC)) : null
	);

	let deltaFromTarget = $derived.by<number | null>(() => {
		if (currentTempC === null || effectiveTargetC === null) return null;
		return currentTempC - effectiveTargetC;
	});

	let isNearTarget = $derived.by(() => {
		if (deltaFromTarget === null) return true;
		return Math.abs(deltaFromTarget) <= 1.0;
	});
</script>

<div
	class="glass-panel relative flex flex-col rounded-3xl border border-zinc-200/80 p-5 dark:border-white/10 {className}"
>
	<!-- Header & Step Selector -->
	<div
		class="flex flex-wrap items-center justify-between gap-3 border-b border-zinc-200/60 pb-4 dark:border-white/5"
	>
		<div class="flex items-center gap-2.5">
			<div
				class="flex h-8 w-8 items-center justify-center rounded-xl bg-amber-500/10 text-amber-600 dark:bg-amber-500/20 dark:text-amber-400"
			>
				<Thermometer class="h-4 w-4" />
			</div>
			<div>
				<div class="flex items-center gap-2">
					<h3 class="text-sm font-bold text-zinc-900 dark:text-white">
						{t('batches.telemetry.step_graph_title', {
							stage: t(`batches.stages.${stage.toLowerCase()}`)
						})}
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
				{#if equipmentName}
					<p class="text-xs text-zinc-500 dark:text-zinc-400">
						{equipmentName}
					</p>
				{/if}
			</div>
		</div>

		<div class="flex flex-wrap items-center gap-2">
			<!-- Mash Step Filter (If in Mash stage with multiple steps) -->
			{#if stage === 'Mash' && mashSteps.length > 0 && onSelectStep}
				<div
					class="flex items-center rounded-xl border border-zinc-200 bg-zinc-50/80 p-0.5 dark:border-zinc-800 dark:bg-zinc-900/60"
				>
					<button
						type="button"
						onclick={() => onSelectStep(null)}
						class="cursor-pointer rounded-lg px-2.5 py-1 text-xs font-semibold transition-all {selectedStepId ===
						null
							? 'bg-white text-zinc-900 shadow-xs dark:bg-zinc-800 dark:text-white'
							: 'text-zinc-500 hover:text-zinc-800 dark:text-zinc-400 dark:hover:text-zinc-200'}"
					>
						{t('batches.telemetry.all_steps')}
					</button>
					{#each mashSteps as step (step.id)}
						<button
							type="button"
							onclick={() => onSelectStep(step.id)}
							class="cursor-pointer rounded-lg px-2.5 py-1 text-xs font-semibold transition-all {selectedStepId ===
							step.id
								? 'bg-amber-500 text-white shadow-xs'
								: 'text-zinc-500 hover:text-zinc-800 dark:text-zinc-400 dark:hover:text-zinc-200'}"
						>
							#{step.stepOrder}
							{step.name}
						</button>
					{/each}
				</div>
			{/if}

			{#if onOpenLogModal && !isCurrentStepCompleted}
				<button
					type="button"
					onclick={onOpenLogModal}
					class="inline-flex cursor-pointer items-center gap-1.5 rounded-xl border border-amber-500/30 bg-amber-500/10 px-3 py-1.5 text-xs font-semibold text-amber-700 transition-colors hover:bg-amber-500/20 dark:text-amber-300"
				>
					<Plus class="h-3.5 w-3.5" />
					<span>{t('batches.telemetry.log_temp_button')}</span>
				</button>
			{/if}
		</div>
	</div>

	<!-- Stats Strip -->
	<div class="mt-4 grid grid-cols-2 gap-2 sm:grid-cols-4 sm:gap-3">
		<!-- Current / Latest -->
		<div
			class="rounded-2xl border border-zinc-200/60 bg-white/60 p-3 dark:border-zinc-800/60 dark:bg-zinc-900/30"
		>
			<span class="text-[11px] font-semibold text-zinc-500 dark:text-zinc-400"
				>{t('batches.telemetry.latest_temp')}</span
			>
			<div class="mt-1 flex items-baseline gap-1.5">
				<span
					class="font-mono text-lg font-bold {currentTempC !== null
						? isNearTarget
							? 'text-emerald-600 dark:text-emerald-400'
							: 'text-amber-600 dark:text-amber-400'
						: 'text-zinc-400'}"
				>
					{currentTempC !== null ? settings.formatTemp(currentTempC) : '—'}
				</span>
				{#if deltaFromTarget !== null}
					<span
						class="text-[11px] font-semibold {deltaFromTarget >= 0
							? 'text-amber-600 dark:text-amber-400'
							: 'text-cyan-600 dark:text-cyan-400'}"
					>
						({deltaFromTarget >= 0 ? '+' : ''}{formatNumber(deltaFromTarget, 1)}°C)
					</span>
				{/if}
			</div>
		</div>

		<!-- Target Temp -->
		<div
			class="rounded-2xl border border-zinc-200/60 bg-white/60 p-3 dark:border-zinc-800/60 dark:bg-zinc-900/30"
		>
			<span class="text-[11px] font-semibold text-zinc-500 dark:text-zinc-400"
				>{t('batches.telemetry.target_temp')}</span
			>
			<div class="mt-1 flex items-center gap-1.5">
				<span class="font-mono text-lg font-bold text-zinc-900 dark:text-white">
					{effectiveTargetC !== null ? settings.formatTemp(effectiveTargetC) : '—'}
				</span>
			</div>
		</div>

		<!-- Min / Max Recorded -->
		<div
			class="rounded-2xl border border-zinc-200/60 bg-white/60 p-3 dark:border-zinc-800/60 dark:bg-zinc-900/30"
		>
			<span class="text-[11px] font-semibold text-zinc-500 dark:text-zinc-400"
				>{t('batches.telemetry.min_max')}</span
			>
			<div class="mt-1 font-mono text-xs font-semibold text-zinc-700 dark:text-zinc-300">
				<span>{minReadingC !== null ? settings.formatTemp(minReadingC) : '—'}</span>
				<span class="text-zinc-400"> / </span>
				<span>{maxReadingC !== null ? settings.formatTemp(maxReadingC) : '—'}</span>
			</div>
		</div>

		<!-- Readings Count -->
		<div
			class="rounded-2xl border border-zinc-200/60 bg-white/60 p-3 dark:border-zinc-800/60 dark:bg-zinc-900/30"
		>
			<span class="text-[11px] font-semibold text-zinc-500 dark:text-zinc-400"
				>{t('batches.telemetry.readings_count')}</span
			>
			<div class="mt-1 flex items-center gap-1.5">
				<span class="font-mono text-lg font-bold text-zinc-900 dark:text-white">
					{displayedReadings.length}
				</span>
				{#if displayedReadings.length > 0}
					<span class="flex h-2 w-2 animate-pulse rounded-full bg-emerald-500"></span>
				{/if}
			</div>
		</div>
	</div>

	<!-- Chart Canvas or Empty State -->
	{#if displayedReadings.length === 0}
		<div class="flex flex-col items-center justify-center py-10 text-center">
			<div
				class="flex h-12 w-12 items-center justify-center rounded-2xl bg-zinc-100 text-zinc-400 dark:bg-zinc-800"
			>
				<Thermometer class="h-6 w-6" />
			</div>
			<p class="mt-3 text-xs font-medium text-zinc-500 dark:text-zinc-400">
				{t('batches.telemetry.no_step_readings')}
			</p>
			{#if onOpenLogModal}
				<button
					type="button"
					onclick={onOpenLogModal}
					class="mt-3 cursor-pointer rounded-xl bg-amber-500 px-3.5 py-1.5 text-xs font-semibold text-white shadow-xs transition-colors hover:bg-amber-600"
				>
					{t('batches.telemetry.log_temp_first')}
				</button>
			{/if}
		</div>
	{:else}
		<div class="relative mt-3 w-full overflow-hidden">
			<!-- SVG Chart -->
			<svg
				viewBox="0 0 {width} {height}"
				class="h-auto w-full overflow-visible select-none"
				aria-hidden="true"
			>
				<defs>
					<linearGradient id="tempGradient" x1="0" y1="0" x2="0" y2="1">
						<stop offset="0%" stop-color="#f59e0b" stop-opacity="0.25" />
						<stop offset="100%" stop-color="#f59e0b" stop-opacity="0.0" />
					</linearGradient>
				</defs>

				<!-- Y-Axis Grid Lines & Labels -->
				{#each [minTemp, Math.round((minTemp + maxTemp) / 2), maxTemp] as tVal}
					{@const yPos = getY(tVal)}
					<line
						x1={padding.left}
						x2={width - padding.right}
						y1={yPos}
						y2={yPos}
						stroke="currentColor"
						stroke-opacity="0.1"
						stroke-width="1"
					/>
					<text
						x={padding.left - 8}
						y={yPos + 4}
						text-anchor="end"
						font-size="10"
						fill="currentColor"
						class="fill-zinc-400 font-mono"
					>
						{tVal}{unitLabel}
					</text>
				{/each}

				<!-- Target Temperature Line(s) -->
				{#if stage === 'Mash' && !selectedStepId && mashSteps.length > 1}
					{#each mashSteps as step (step.id)}
						{@const stepTarget = displayTemp(step.targetTemperatureC)}
						{@const targetY = getY(stepTarget)}
						<line
							x1={padding.left}
							x2={width - padding.right}
							y1={targetY}
							y2={targetY}
							stroke="#10b981"
							stroke-dasharray="4 4"
							stroke-width="1"
							stroke-opacity="0.8"
						/>
						<text
							x={width - padding.right}
							y={targetY - 6}
							text-anchor="end"
							font-size="10"
							class="fill-emerald-600 font-mono font-semibold dark:fill-emerald-400"
						>
							#{step.stepOrder}
							{step.name}
							{stepTarget.toFixed(1)}{unitLabel}
						</text>
					{/each}
				{:else if convertedTarget !== null}
					{@const targetY = getY(convertedTarget)}
					<line
						x1={padding.left}
						x2={width - padding.right}
						y1={targetY}
						y2={targetY}
						stroke="#10b981"
						stroke-dasharray="4 4"
						stroke-width="1"
					/>
					<text
						x={width - padding.right}
						y={targetY - 6}
						text-anchor="end"
						font-size="10"
						class="fill-emerald-600 font-mono font-semibold dark:fill-emerald-400"
					>
						Target {convertedTarget.toFixed(1)}{unitLabel}
					</text>
				{/if}

				<!-- Area Gradient Fill -->
				{#if areaPath}
					<path d={areaPath} fill="url(#tempGradient)" />
				{/if}

				<!-- Temperature Line -->
				{#if tempPath}
					<path
						d={tempPath}
						fill="none"
						stroke="#f59e0b"
						stroke-width="1.5"
						stroke-linecap="round"
						stroke-linejoin="round"
					/>
				{/if}

				<!-- Single Point Fallback (when only 1 reading exists) -->
				{#if displayedReadings.length === 1}
					<circle
						cx={getX(0, 1)}
						cy={getY(displayTemp(displayedReadings[0].temperatureC))}
						r="3"
						class="fill-amber-500 stroke-white dark:stroke-zinc-900"
						stroke-width="1.5"
					/>
				{/if}

				<!-- Reading Points Hit Targets (no dots on data points) -->
				{#each displayedReadings as reading, i}
					{@const cx = getX(i, displayedReadings.length)}
					{@const cy = getY(displayTemp(reading.temperatureC))}
					<circle
						{cx}
						{cy}
						r="8"
						role="button"
						tabindex="-1"
						aria-label="{reading.stepName ?? 'Step'}: {settings.formatTemp(reading.temperatureC)}"
						class="cursor-pointer fill-transparent stroke-transparent"
						onpointerenter={() => (hoveredIndex = i)}
						onpointerleave={() => (hoveredIndex = null)}
					/>
					{#if hoveredIndex === i}
						<circle
							{cx}
							{cy}
							r="3.5"
							class="pointer-events-none fill-amber-500 stroke-white dark:stroke-zinc-900"
							stroke-width="1.5"
						/>
					{/if}
				{/each}
			</svg>

			<!-- Interactive Floating Tooltip -->
			{#if activeReading}
				<div
					class="pointer-events-none absolute top-2 right-4 rounded-xl border border-zinc-200/80 bg-white/95 px-3 py-2 text-xs shadow-md backdrop-blur-md dark:border-zinc-800 dark:bg-zinc-900/95"
				>
					<div class="flex items-center gap-2 font-mono font-bold text-zinc-900 dark:text-white">
						<span>{settings.formatTemp(activeReading.temperatureC)}</span>
						{#if effectiveTargetC !== null}
							{@const d = activeReading.temperatureC - effectiveTargetC}
							<span
								class="text-[10px] {d >= 0
									? 'text-amber-600 dark:text-amber-400'
									: 'text-cyan-600 dark:text-cyan-400'}"
							>
								({d >= 0 ? '+' : ''}{formatNumber(d, 1)}°C)
							</span>
						{/if}
					</div>
					<div class="mt-0.5 text-[11px] text-zinc-500 dark:text-zinc-400">
						<span
							>{new Date(activeReading.timestamp).toLocaleTimeString([], {
								hour: '2-digit',
								minute: '2-digit',
								second: '2-digit'
							})}</span
						>
						{#if activeReading.stepName}
							<span> • {activeReading.stepName}</span>
						{/if}
					</div>
					{#if activeReading.source}
						<div class="mt-0.5 text-[10px] text-zinc-400">
							{activeReading.source}
						</div>
					{/if}
				</div>
			{/if}

			<!-- Screen-Reader Fallback Table -->
			<table class="sr-only">
				<caption>{t('batches.telemetry.table_caption')}</caption>
				<thead>
					<tr>
						<th scope="col">{t('batches.telemetry.time')}</th>
						<th scope="col">{t('batches.telemetry.step')}</th>
						<th scope="col">{t('batches.telemetry.temperature_col')}</th>
						<th scope="col">{t('batches.telemetry.equipment')}</th>
					</tr>
				</thead>
				<tbody>
					{#each displayedReadings as reading}
						<tr>
							<td>{new Date(reading.timestamp).toLocaleString()}</td>
							<td>{reading.stepName ?? 'N/A'}</td>
							<td>{settings.formatTemp(reading.temperatureC)}</td>
							<td>{reading.equipmentName}</td>
						</tr>
					{/each}
				</tbody>
			</table>
		</div>
	{/if}
</div>
