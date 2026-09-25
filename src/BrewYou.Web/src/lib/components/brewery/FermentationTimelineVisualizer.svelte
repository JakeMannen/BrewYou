<script module lang="ts">
	import type { FermentationStepType } from '$lib/types/api';

	export interface FermentationStepVisualItem {
		id?: string;
		name: string;
		type: FermentationStepType;
		targetTemperatureC: number;
		durationDays: number;
		rampTimeHours?: number | null;
		triggerGravity?: number | null;
	}

	export function getStageStyle(type: FermentationStepType): {
		barColor: string;
		badgeClass: string;
	} {
		switch (type) {
			case 'Primary':
				return {
					barColor: '#10b981',
					badgeClass:
						'bg-emerald-500/15 text-emerald-700 dark:text-emerald-300 border-emerald-500/30'
				};
			case 'Secondary':
				return {
					barColor: '#059669',
					badgeClass: 'bg-teal-500/15 text-teal-700 dark:text-teal-300 border-teal-500/30'
				};
			case 'Ramp':
				return {
					barColor: '#f59e0b',
					badgeClass: 'bg-amber-500/15 text-amber-700 dark:text-amber-300 border-amber-500/30'
				};
			case 'FreeRise':
				return {
					barColor: '#f97316',
					badgeClass: 'bg-orange-500/15 text-orange-700 dark:text-orange-300 border-orange-500/30'
				};
			case 'ColdCrash':
				return {
					barColor: '#38bdf8',
					badgeClass: 'bg-sky-500/15 text-sky-700 dark:text-sky-300 border-sky-500/30'
				};
			case 'Conditioning':
			default:
				return {
					barColor: '#8b5cf6',
					badgeClass: 'bg-purple-500/15 text-purple-700 dark:text-purple-300 border-purple-500/30'
				};
		}
	}
</script>

<script lang="ts">
	import { settings } from '$lib/stores/settings.svelte';
	import { t } from '$lib/i18n/index.svelte';
	import { Activity, Clock, Thermometer } from '@lucide/svelte';

	interface Props {
		steps: FermentationStepVisualItem[];
		class?: string;
	}

	let { steps, class: className = '' }: Props = $props();

	function getDisplayTemp(celsius: number): number {
		if (settings.temperatureUnit === 'Fahrenheit') {
			return Math.round(((celsius * 9) / 5 + 32) * 10) / 10;
		}
		return Math.round(celsius * 10) / 10;
	}

	const totalDays = $derived(steps.reduce((sum, s) => sum + (s.durationDays || 0), 0));
</script>

{#if steps && steps.length > 0}
	<div
		data-testid="fermentation-timeline-visualizer"
		class="flex flex-col gap-3 rounded-2xl border border-zinc-200/80 bg-zinc-50/60 p-4 transition-all dark:border-zinc-800/80 dark:bg-zinc-900/40 {className}"
	>
		<!-- Header Metric Row -->
		<div class="flex items-center justify-between gap-2">
			<div class="flex items-center gap-2">
				<Activity class="h-4 w-4 text-hops-500 dark:text-hops-400" />
				<span class="font-editorial text-sm font-bold text-zinc-900 dark:text-white">
					{t('fermentation_profile.timeline_title')}
				</span>
			</div>
			<div
				class="flex items-center gap-1.5 rounded-full border border-zinc-200/80 bg-white/80 px-2.5 py-0.5 font-mono text-[11px] font-semibold text-zinc-600 shadow-2xs dark:border-zinc-800 dark:bg-zinc-800/80 dark:text-zinc-300"
			>
				<Clock class="h-3 w-3 text-hops-500" />
				<span>{t('fermentation_profile.total_fermentation_time', { days: totalDays })}</span>
			</div>
		</div>

		<!-- Continuous Stepped Stage Ribbon -->
		<div
			class="flex h-12 w-full overflow-hidden rounded-xl border border-zinc-200/80 bg-zinc-100 dark:border-zinc-800 dark:bg-zinc-800/60"
			role="group"
			aria-label={t('fermentation_profile.timeline_title')}
		>
			{#each steps as step, index (step.id || index)}
				{@const style = getStageStyle(step.type)}
				{@const widthPercent =
					totalDays > 0
						? Math.max(14, ((step.durationDays || 1) / totalDays) * 100)
						: 100 / steps.length}
				<div
					class="relative flex flex-col justify-between border-r border-white/20 p-1.5 transition-all last:border-r-0 hover:brightness-105"
					style="width: {widthPercent}%; background-color: {style.barColor}22;"
					title="{step.name ||
						t(`fermentation_profile.type_${step.type.toLowerCase()}`)}: {getDisplayTemp(
						step.targetTemperatureC
					)}{settings.tempShort} ({step.durationDays} d)"
				>
					<!-- Top Temperature Marker -->
					<div class="flex items-center justify-between gap-1">
						<span class="truncate font-mono text-[11px] font-bold text-zinc-800 dark:text-zinc-100">
							{getDisplayTemp(step.targetTemperatureC)}{settings.tempShort}
						</span>
						<span class="truncate font-mono text-[9px] text-zinc-500 dark:text-zinc-400">
							{step.durationDays}d
						</span>
					</div>

					<!-- Bottom Stage Pill -->
					<div class="flex items-center gap-1">
						<span
							class="h-1.5 w-1.5 shrink-0 rounded-full"
							style="background-color: {style.barColor};"
						></span>
						<span class="truncate text-[9px] font-medium text-zinc-700 dark:text-zinc-300">
							{step.name || t(`fermentation_profile.type_${step.type.toLowerCase()}`)}
						</span>
					</div>
				</div>
			{/each}
		</div>

		<!-- Step Detail Badges -->
		<div class="flex flex-wrap gap-2 pt-1">
			{#each steps as step (step.id || step.name)}
				{@const style = getStageStyle(step.type)}
				<div
					class="inline-flex items-center gap-1.5 rounded-lg border px-2 py-1 text-[11px] font-medium transition-all {style.badgeClass}"
				>
					<Thermometer class="h-3 w-3 shrink-0" />
					<span class="font-semibold"
						>{step.name || t(`fermentation_profile.type_${step.type.toLowerCase()}`)}:</span
					>
					<span class="font-mono"
						>{getDisplayTemp(step.targetTemperatureC)}{settings.tempShort}</span
					>
					<span class="text-[10px] opacity-80"
						>({step.durationDays} {t('fermentation_profile.days')})</span
					>
				</div>
			{/each}
		</div>
	</div>
{/if}
