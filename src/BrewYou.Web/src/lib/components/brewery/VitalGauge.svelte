<script lang="ts">
	import { formatNumber } from '$lib/utils/formatNumber';
	interface Props {
		label: string;
		value: number;
		min: number;
		max: number;
		unit?: string;
		targetMin?: number;
		targetMax?: number;
		precision?: number;
		tone?: 'amber' | 'hops' | 'copper' | 'cyan';
		size?: number;
		strokeWidth?: number;
		class?: string;
	}

	let {
		label,
		value,
		min,
		max,
		unit = '',
		targetMin,
		targetMax,
		precision = 1,
		tone = 'amber',
		size = 130,
		strokeWidth = 10,
		class: className = ''
	}: Props = $props();

	const radius = $derived(Math.max(1, (size - strokeWidth) / 2));
	const center = $derived(size / 2);
	const startAngle = -210;
	const sweepAngle = 240;

	const normalizedPercent = $derived(
		max > min ? Math.max(0, Math.min(1, (value - min) / (max - min))) : 0
	);

	function polarToCartesian(cx: number, cy: number, r: number, angleDegrees: number) {
		const rad = ((angleDegrees - 90) * Math.PI) / 180.0;
		return {
			x: cx + r * Math.cos(rad),
			y: cy + r * Math.sin(rad)
		};
	}

	function describeArc(x: number, y: number, r: number, start: number, end: number) {
		const startCoord = polarToCartesian(x, y, r, end);
		const endCoord = polarToCartesian(x, y, r, start);
		const largeArcFlag = end - start <= 180 ? '0' : '1';
		return `M ${startCoord.x} ${startCoord.y} A ${r} ${r} 0 ${largeArcFlag} 0 ${endCoord.x} ${endCoord.y}`;
	}

	const trackPath = $derived(
		describeArc(center, center, radius, startAngle, startAngle + sweepAngle)
	);
	const currentAngle = $derived(startAngle + sweepAngle * normalizedPercent);
	const fillPath = $derived(describeArc(center, center, radius, startAngle, currentAngle));

	const isInTarget = $derived(
		targetMin !== undefined && targetMax !== undefined
			? value >= targetMin && value <= targetMax
			: true
	);

	const toneColorMap = {
		amber: '#f59e0b',
		hops: '#10b981',
		copper: '#ea580c',
		cyan: '#06b6d4'
	};
</script>

<div
	class="glass-panel relative flex flex-col items-center justify-center rounded-2xl p-3 text-slate-100 {className}"
	role="meter"
	aria-label="{label} Gauge"
	aria-valuenow={value}
	aria-valuemin={min}
	aria-valuemax={max}
	aria-valuetext="{formatNumber(value, precision)} {unit}"
>
	<svg width={size} height={size * 0.88} class="overflow-visible" aria-hidden="true">
		<!-- Track background -->
		<path
			d={trackPath}
			fill="none"
			stroke="currentColor"
			class="stroke-zinc-300 dark:stroke-zinc-800"
			stroke-width={strokeWidth}
			stroke-linecap="round"
		/>

		<!-- Target range highlight (if target bounds provided) -->
		{#if targetMin !== undefined && targetMax !== undefined && max > min}
			{@const tStart =
				startAngle + sweepAngle * Math.max(0, Math.min(1, (targetMin - min) / (max - min)))}
			{@const tEnd =
				startAngle + sweepAngle * Math.max(0, Math.min(1, (targetMax - min) / (max - min)))}
			{#if tEnd > tStart}
				<path
					d={describeArc(center, center, radius, tStart, tEnd)}
					fill="none"
					stroke="#10b981"
					stroke-opacity="0.35"
					stroke-width={strokeWidth + 4}
					stroke-linecap="round"
				/>
			{/if}
		{/if}

		<!-- Active progress arc -->
		{#if normalizedPercent > 0.005}
			<path
				d={fillPath}
				fill="none"
				stroke={toneColorMap[tone]}
				stroke-width={strokeWidth}
				stroke-linecap="round"
				class="transition-[stroke-dashoffset,d] duration-500 ease-out"
				style="filter: drop-shadow(0 0 6px {toneColorMap[tone]}66);"
			/>
		{/if}
	</svg>

	<!-- Metric Center Readout -->
	<div class="pointer-events-none absolute inset-0 flex flex-col items-center justify-center pt-2">
		<span class="text-xs font-semibold tracking-wider text-zinc-500 uppercase dark:text-zinc-400">
			{label}
		</span>
		<div class="flex items-baseline gap-0.5">
			<span
				class="font-mono text-xl font-bold tracking-tight text-zinc-900 tabular-nums dark:text-white"
			>
				{formatNumber(value, precision)}
			</span>
			{#if unit}
				<span class="font-mono text-xs text-zinc-500 dark:text-zinc-400">{unit}</span>
			{/if}
		</div>
		{#if targetMin !== undefined && targetMax !== undefined}
			<span
				class="font-mono text-[10px] {isInTarget
					? 'text-emerald-600 dark:text-hops-400'
					: 'text-amber-600 dark:text-amber-400'}"
			>
				Target: {targetMin}–{targetMax}
			</span>
		{/if}
	</div>
</div>
