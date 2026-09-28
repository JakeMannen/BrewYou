<script module lang="ts">
	export interface MashStepVisualItem {
		id?: string;
		name: string;
		temperatureC: number;
		durationMinutes: number;
		rampTimeMinutes?: number | null;
	}

	export function getEnzymeZone(tempC: number): {
		key: string;
		badgeClass: string;
		barColor: string;
	} {
		if (tempC < 55) {
			return {
				key: 'mash_profile.zone_protein',
				badgeClass: 'bg-sky-500/15 text-sky-700 dark:text-sky-300 border-sky-500/30',
				barColor: '#38bdf8'
			};
		}
		if (tempC < 62) {
			return {
				key: 'mash_profile.zone_beta_glucan',
				badgeClass:
					'bg-emerald-500/15 text-emerald-700 dark:text-emerald-300 border-emerald-500/30',
				barColor: '#34d399'
			};
		}
		if (tempC <= 65) {
			return {
				key: 'mash_profile.zone_beta_amylase',
				badgeClass: 'bg-amber-500/15 text-amber-700 dark:text-amber-300 border-amber-500/30',
				barColor: '#f59e0b'
			};
		}
		if (tempC <= 68) {
			return {
				key: 'mash_profile.zone_balanced',
				badgeClass: 'bg-orange-500/15 text-orange-700 dark:text-orange-300 border-orange-500/30',
				barColor: '#f97316'
			};
		}
		if (tempC <= 74) {
			return {
				key: 'mash_profile.zone_alpha_amylase',
				badgeClass: 'bg-amber-600/15 text-amber-800 dark:text-amber-200 border-amber-600/30',
				barColor: '#d97706'
			};
		}
		return {
			key: 'mash_profile.zone_mash_out',
			badgeClass: 'bg-rose-500/15 text-rose-700 dark:text-rose-300 border-rose-500/30',
			barColor: '#ef4444'
		};
	}
</script>

<script lang="ts">
	import { settings } from '$lib/stores/settings.svelte';
	import { t } from '$lib/i18n/index.svelte';
	import { Flame, Clock, Thermometer } from '@lucide/svelte';

	interface Props {
		steps: MashStepVisualItem[];
		class?: string;
	}

	let { steps, class: className = '' }: Props = $props();

	function getDisplayTemp(celsius: number): number {
		if (settings.temperatureUnit === 'Fahrenheit') {
			return Math.round(((celsius * 9) / 5 + 32) * 10) / 10;
		}
		return Math.round(celsius * 10) / 10;
	}

	const totalDuration = $derived(
		steps.reduce((sum, s) => sum + (s.durationMinutes || 0) + (s.rampTimeMinutes || 0), 0)
	);
</script>

{#if steps && steps.length > 0}
	<div
		data-testid="mash-enzyme-visualizer"
		class="flex flex-col gap-3 rounded-2xl border border-zinc-200/80 bg-zinc-50/60 p-4 transition-all dark:border-zinc-800/80 dark:bg-zinc-900/40 {className}"
	>
		<!-- Header Metric Row -->
		<div class="flex items-center justify-between gap-2">
			<div class="flex items-center gap-2">
				<Flame class="h-4 w-4 text-copper-500 dark:text-copper-400" />
				<span class="font-editorial text-sm font-bold text-zinc-900 dark:text-white">
					{t('mash_profile.enzymatic_profile')}
				</span>
			</div>
			<div
				class="flex items-center gap-1.5 rounded-full border border-zinc-200/80 bg-white/80 px-2.5 py-0.5 font-mono text-[11px] font-semibold text-zinc-600 shadow-2xs dark:border-zinc-800 dark:bg-zinc-800/80 dark:text-zinc-300"
			>
				<Clock class="h-3 w-3 text-amber-500" />
				<span>{t('mash_profile.total_mash_time', { minutes: totalDuration })}</span>
			</div>
		</div>

		<!-- Continuous Stepped Temperature Ribbon -->
		<div
			class="flex h-12 w-full overflow-hidden rounded-xl border border-zinc-200/80 bg-zinc-100 dark:border-zinc-800 dark:bg-zinc-800/60"
			role="group"
			aria-label={t('mash_profile.enzymatic_profile')}
		>
			{#each steps as step, index (step.id || index)}
				{@const zone = getEnzymeZone(step.temperatureC)}
				{@const widthPercent =
					totalDuration > 0
						? Math.max(12, ((step.durationMinutes || 1) / totalDuration) * 100)
						: 100 / steps.length}
				<div
					class="relative flex flex-col justify-between border-r border-white/20 p-1.5 transition-all last:border-r-0 hover:brightness-105"
					style="width: {widthPercent}%; background-color: {zone.barColor}22;"
					title="{step.name ||
						t('mash_profile.step_order_badge', { order: index + 1 })}: {getDisplayTemp(
						step.temperatureC
					)}{settings.tempShort} ({step.durationMinutes} min)"
				>
					<!-- Top Temperature Marker -->
					<div class="flex items-center justify-between gap-1">
						<span class="truncate font-mono text-[11px] font-bold text-zinc-800 dark:text-zinc-100">
							{getDisplayTemp(step.temperatureC)}{settings.tempShort}
						</span>
						<span class="truncate font-mono text-[9px] text-zinc-500 dark:text-zinc-400">
							{step.durationMinutes}m
						</span>
					</div>

					<!-- Bottom Stage Pill -->
					<div class="flex items-center gap-1">
						<span
							class="h-1.5 w-1.5 shrink-0 rounded-full"
							style="background-color: {zone.barColor};"
						></span>
						<span class="truncate text-[9px] font-medium text-zinc-700 dark:text-zinc-300">
							{step.name || t('mash_profile.step_order_badge', { order: index + 1 })}
						</span>
					</div>
				</div>
			{/each}
		</div>

		<!-- Step Enzymatic Callout Badges -->
		<div class="flex flex-wrap gap-2 pt-1">
			{#each steps as step, index (step.id || index)}
				{@const zone = getEnzymeZone(step.temperatureC)}
				<div
					class="inline-flex items-center gap-1.5 rounded-lg border px-2 py-1 text-[11px] font-medium transition-all {zone.badgeClass}"
				>
					<Thermometer class="h-3 w-3 shrink-0" />
					<span class="font-semibold"
						>{step.name || t('mash_profile.step_order_badge', { order: index + 1 })}:</span
					>
					<span class="font-mono">{getDisplayTemp(step.temperatureC)}{settings.tempShort}</span>
					<span class="text-[10px] opacity-80">• {t(zone.key)}</span>
				</div>
			{/each}
		</div>
	</div>
{/if}
