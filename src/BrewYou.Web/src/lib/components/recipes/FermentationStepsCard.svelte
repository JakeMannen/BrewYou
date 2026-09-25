<script lang="ts">
	import type { FermentationStepType } from '$lib/types/api';
	import { settings } from '$lib/stores/settings.svelte';
	import { t } from '$lib/i18n/index.svelte';
	import { Plus, Trash2, ArrowUp, ArrowDown, Thermometer, Clock, Activity } from '@lucide/svelte';
	import FermentationTimelineVisualizer from '$lib/components/brewery/FermentationTimelineVisualizer.svelte';

	export interface FormFermentationStepItem {
		id: string;
		stepOrder: number;
		name: string;
		type: FermentationStepType;
		targetTemperatureC: number;
		durationDays: number;
		rampTimeHours?: number | null;
		triggerGravity?: number | null;
		notes?: string | null;
	}

	interface Props {
		steps: FormFermentationStepItem[];
		onAddStep: () => void;
		onRemoveStep: (id: string) => void;
		onMoveStep: (index: number, direction: 'up' | 'down') => void;
		onApplyPreset: (preset: 'ale' | 'lager' | 'saison' | 'neipa') => void;
	}

	let { steps = $bindable(), onAddStep, onRemoveStep, onMoveStep, onApplyPreset }: Props = $props();

	const stepTypes: FermentationStepType[] = [
		'Primary',
		'Secondary',
		'Ramp',
		'FreeRise',
		'ColdCrash',
		'Conditioning'
	];

	function handleTempChange(step: FormFermentationStepItem, displayValue: number) {
		if (settings.temperatureUnit === 'Fahrenheit') {
			step.targetTemperatureC = Math.round((((displayValue - 32) * 5) / 9) * 10) / 10;
		} else {
			step.targetTemperatureC = Math.round(displayValue * 10) / 10;
		}
	}

	function getDisplayTemp(celsius: number): number {
		if (settings.temperatureUnit === 'Fahrenheit') {
			return Math.round(((celsius * 9) / 5 + 32) * 10) / 10;
		}
		return Math.round(celsius * 10) / 10;
	}
</script>

<div
	class="overflow-hidden rounded-2xl border border-zinc-200/80 bg-white/70 shadow-sm backdrop-blur-md transition-all dark:border-zinc-800/80 dark:bg-zinc-900/60"
>
	<!-- Header -->
	<div
		class="flex flex-wrap items-center justify-between gap-3 border-b border-zinc-200/80 px-6 py-4 dark:border-zinc-800/80"
	>
		<div class="flex items-center gap-3">
			<div
				class="flex h-10 w-10 items-center justify-center rounded-xl bg-gradient-to-br from-hops-500/20 to-emerald-600/20 text-hops-600 dark:text-hops-400"
			>
				<Activity class="h-5 w-5" />
			</div>
			<div>
				<h3 class="font-editorial text-lg font-bold text-zinc-900 dark:text-white">
					{t('fermentation_profile.title')}
				</h3>
				<p class="text-xs text-zinc-500 dark:text-zinc-400">
					{t('fermentation_profile.subtitle')}
				</p>
			</div>
		</div>

		<!-- Presets & Add Step Buttons -->
		<div class="flex flex-wrap items-center gap-2">
			<div class="flex items-center gap-1 rounded-lg bg-zinc-100 p-1 dark:bg-zinc-800">
				<button
					type="button"
					onclick={() => onApplyPreset('ale')}
					class="cursor-pointer rounded-md px-2.5 py-1 text-xs font-medium text-zinc-600 transition-colors hover:bg-white hover:text-zinc-900 hover:shadow-xs dark:text-zinc-300 dark:hover:bg-zinc-700 dark:hover:text-white"
				>
					{t('fermentation_profile.preset_ale')}
				</button>
				<button
					type="button"
					onclick={() => onApplyPreset('lager')}
					class="cursor-pointer rounded-md px-2.5 py-1 text-xs font-medium text-zinc-600 transition-colors hover:bg-white hover:text-zinc-900 hover:shadow-xs dark:text-zinc-300 dark:hover:bg-zinc-700 dark:hover:text-white"
				>
					{t('fermentation_profile.preset_lager')}
				</button>
				<button
					type="button"
					onclick={() => onApplyPreset('saison')}
					class="cursor-pointer rounded-md px-2.5 py-1 text-xs font-medium text-zinc-600 transition-colors hover:bg-white hover:text-zinc-900 hover:shadow-xs dark:text-zinc-300 dark:hover:bg-zinc-700 dark:hover:text-white"
				>
					{t('fermentation_profile.preset_saison')}
				</button>
				<button
					type="button"
					onclick={() => onApplyPreset('neipa')}
					class="cursor-pointer rounded-md px-2.5 py-1 text-xs font-medium text-zinc-600 transition-colors hover:bg-white hover:text-zinc-900 hover:shadow-xs dark:text-zinc-300 dark:hover:bg-zinc-700 dark:hover:text-white"
				>
					{t('fermentation_profile.preset_neipa')}
				</button>
			</div>

			<button
				type="button"
				onclick={onAddStep}
				class="flex cursor-pointer items-center gap-1.5 rounded-lg bg-hops-600 px-3 py-1.5 text-xs font-bold text-white shadow-sm transition-all hover:bg-hops-500 active:scale-95"
			>
				<Plus class="h-3.5 w-3.5" />
				<span>{t('fermentation_profile.add_step')}</span>
			</button>
		</div>
	</div>

	<!-- Interactive Fermentation Stage Timeline Visualizer -->
	{#if steps.length > 0}
		<div class="px-4 pt-4 sm:px-6 sm:pt-6">
			<FermentationTimelineVisualizer {steps} />
		</div>
	{/if}

	<!-- Step List -->
	<div class="divide-y divide-zinc-200/60 p-4 sm:p-6 dark:divide-zinc-800/60">
		{#if steps.length === 0}
			<div class="py-8 text-center text-sm text-zinc-500 dark:text-zinc-400">
				{t('fermentation_profile.no_steps')}
			</div>
		{:else}
			<div class="space-y-3">
				{#each steps as step, index (step.id)}
					<div
						class="flex flex-col gap-3 rounded-xl border border-zinc-200/60 bg-zinc-50/50 p-3.5 sm:flex-row sm:items-center sm:gap-4 dark:border-zinc-800/60 dark:bg-zinc-900/40"
					>
						<!-- Order & Reorder arrows -->
						<div class="flex items-center gap-1.5">
							<span
								class="flex h-7 w-7 items-center justify-center rounded-lg bg-cyan-500/10 font-mono text-xs font-bold text-cyan-600 dark:text-cyan-400"
							>
								{index + 1}
							</span>
							<div class="flex flex-col">
								<button
									type="button"
									disabled={index === 0}
									onclick={() => onMoveStep(index, 'up')}
									class="cursor-pointer p-0.5 text-zinc-400 hover:text-zinc-700 disabled:cursor-not-allowed disabled:opacity-30 dark:hover:text-zinc-200"
									aria-label={t('fermentation_profile.move_up')}
								>
									<ArrowUp class="h-3 w-3" />
								</button>
								<button
									type="button"
									disabled={index === steps.length - 1}
									onclick={() => onMoveStep(index, 'down')}
									class="cursor-pointer p-0.5 text-zinc-400 hover:text-zinc-700 disabled:cursor-not-allowed disabled:opacity-30 dark:hover:text-zinc-200"
									aria-label={t('fermentation_profile.move_down')}
								>
									<ArrowDown class="h-3 w-3" />
								</button>
							</div>
						</div>

						<!-- Step Name -->
						<div class="min-w-0 flex-1">
							<input
								type="text"
								bind:value={step.name}
								placeholder={t('fermentation_profile.step_name_placeholder')}
								class="w-full rounded-lg border border-zinc-200 bg-white px-3 py-1.5 text-sm font-medium text-zinc-900 placeholder:text-zinc-400 focus:border-cyan-500 focus:ring-1 focus:ring-cyan-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-800 dark:text-white"
							/>
						</div>

						<!-- Step Type -->
						<div class="w-full sm:w-36">
							<select
								bind:value={step.type}
								class="w-full rounded-lg border border-zinc-200 bg-white px-3 py-1.5 text-xs font-medium text-zinc-800 focus:border-cyan-500 focus:ring-1 focus:ring-cyan-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-200"
							>
								{#each stepTypes as st}
									<option value={st}>{t(`fermentation_profile.type_${st.toLowerCase()}`)}</option>
								{/each}
							</select>
						</div>

						<!-- Target Temperature -->
						<div class="flex items-center gap-1.5 sm:w-32">
							<Thermometer class="h-4 w-4 shrink-0 text-cyan-500" />
							<input
								type="number"
								min="-2"
								max="45"
								step="0.5"
								value={getDisplayTemp(step.targetTemperatureC)}
								oninput={(e) => handleTempChange(step, parseFloat(e.currentTarget.value) || 19.0)}
								class="w-full rounded-lg border border-zinc-200 bg-white px-2.5 py-1.5 text-sm font-semibold text-zinc-900 focus:border-cyan-500 focus:ring-1 focus:ring-cyan-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-800 dark:text-white"
							/>
							<span class="text-xs text-zinc-500 dark:text-zinc-400">{settings.tempShort}</span>
						</div>

						<!-- Duration (Days) -->
						<div class="flex items-center gap-1.5 sm:w-28">
							<Clock class="h-4 w-4 shrink-0 text-blue-500" />
							<input
								type="number"
								min="1"
								max="365"
								step="1"
								bind:value={step.durationDays}
								class="w-full rounded-lg border border-zinc-200 bg-white px-2.5 py-1.5 text-sm font-semibold text-zinc-900 focus:border-cyan-500 focus:ring-1 focus:ring-cyan-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-800 dark:text-white"
							/>
							<span class="text-xs text-zinc-500 dark:text-zinc-400"
								>{t('fermentation_profile.days')}</span
							>
						</div>

						<!-- Remove Action -->
						<button
							type="button"
							disabled={steps.length <= 1}
							onclick={() => onRemoveStep(step.id)}
							class="cursor-pointer self-end rounded-lg p-2 text-zinc-400 transition-colors hover:bg-rose-50 hover:text-rose-600 disabled:cursor-not-allowed disabled:opacity-30 sm:self-center dark:hover:bg-rose-950/30 dark:hover:text-rose-400"
							aria-label={t('fermentation_profile.remove_step')}
						>
							<Trash2 class="h-4 w-4" />
						</button>
					</div>
				{/each}
			</div>
		{/if}
	</div>
</div>
