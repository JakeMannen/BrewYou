<script lang="ts">
	import { untrack } from 'svelte';
	import { t } from '$lib/i18n/index.svelte';
	import { dashboard } from '$lib/stores/dashboard.svelte';
	import { settings, celsiusToFahrenheit, fahrenheitToCelsius } from '$lib/stores/settings.svelte';
	import { modalA11y } from '$lib/actions/modalA11y';
	import { isBatchTelemetrySensor } from '$lib/components/inventory/vessel-geometry';
	import { EMPTY_GUID } from '$lib/utils/batchEdit';
	import { isEquipmentOccupied } from '$lib/utils/batchEquipment';
	import { formatDecimalInput, parseDecimal } from '$lib/utils/formatNumber';
	import type {
		AdvanceBatchStageRequest,
		BatchSummaryDto,
		BrewStage,
		EquipmentDto
	} from '$lib/types/api';
	import { X, Sparkles, Loader2, ArrowRight } from '@lucide/svelte';

	interface Props {
		isOpen: boolean;
		batch: BatchSummaryDto | null;
		onClose: () => void;
	}

	let { isOpen = $bindable(false), batch, onClose }: Props = $props();

	let isSubmitting = $state(false);
	let errorMessage = $state<string | null>(null);
	let selectedFermenterId = $state('');
	let selectedFermentationSensorId = $state('');
	let pitchTempStr = $state('');
	let pitchTempDisplay = $derived(parseDecimal(pitchTempStr));
	let wasOpen = false;

	const stageOrder: BrewStage[] = ['Mash', 'Boil', 'Ferment', 'Condition', 'Package'];

	const nextStage = $derived.by<BrewStage | null>(() => {
		if (!batch) return null;
		const currentIdx = stageOrder.indexOf(batch.currentStage);
		if (currentIdx >= 0 && currentIdx < stageOrder.length - 1) {
			return stageOrder[currentIdx + 1];
		}
		return null;
	});

	const availableFermenters = $derived(dashboard.equipment.filter((e) => e.type === 'Fermenter'));
	const availableSensors = $derived(dashboard.equipment.filter((e) => isBatchTelemetrySensor(e)));

	function formatEquipmentOption(eq: EquipmentDto, includeCapacity: boolean): string {
		const base = includeCapacity
			? `${eq.name} (${settings.formatVolume(eq.capacityLiters)})`
			: `${eq.name} [${t(`equipment.subtypes.${eq.subtype}`) || eq.subtype}]`;
		const occupied = isEquipmentOccupied(eq.id, dashboard.activeBatches, eq, batch?.id ?? null);
		return occupied ? `${base} — ${t('dashboard.status_occupied')}` : base;
	}

	$effect(() => {
		if (isOpen && batch && !wasOpen) {
			wasOpen = true;
			untrack(() => {
				errorMessage = null;
				selectedFermenterId = batch.fermenterId ?? '';
				selectedFermentationSensorId = batch.fermentationSensorId ?? '';
				const defaultPitchC = 20.0;
				const initialPitch =
					settings.temperatureUnit === 'Fahrenheit'
						? celsiusToFahrenheit(defaultPitchC)
						: defaultPitchC;
				pitchTempStr = formatDecimalInput(initialPitch, 1);
			});
		} else if (!isOpen) {
			wasOpen = false;
		}
	});

	async function handleAdvance() {
		if (!batch || !nextStage) return;

		if (nextStage === 'Ferment' && pitchTempStr.trim() !== '' && pitchTempDisplay === null) {
			errorMessage = t('batches.telemetry.invalid_temp_error');
			return;
		}

		isSubmitting = true;
		errorMessage = null;

		try {
			const extra: Omit<AdvanceBatchStageRequest, 'targetStage'> = {};
			if (nextStage === 'Ferment') {
				if (selectedFermenterId !== (batch.fermenterId ?? '')) {
					extra.fermenterId = selectedFermenterId || (batch.fermenterId ? EMPTY_GUID : null);
				}
				if (selectedFermentationSensorId !== (batch.fermentationSensorId ?? '')) {
					extra.fermentationSensorId =
						selectedFermentationSensorId || (batch.fermentationSensorId ? EMPTY_GUID : null);
				}
				if (pitchTempDisplay !== null && !Number.isNaN(pitchTempDisplay)) {
					extra.pitchTemperatureC = Number(
						(settings.temperatureUnit === 'Fahrenheit'
							? fahrenheitToCelsius(pitchTempDisplay)
							: pitchTempDisplay
						).toFixed(2)
					);
				}
			}
			await dashboard.advanceStage(batch.id, nextStage, extra);
			onClose();
		} catch (err: unknown) {
			errorMessage = (err as Error).message || t('batches.errors.advance_failed');
		} finally {
			isSubmitting = false;
		}
	}
</script>

{#if isOpen && batch && nextStage}
	{@const currentStageLabel = t(`batches.stages.${batch.currentStage.toLowerCase()}`)}
	{@const nextStageLabel = t(`batches.stages.${nextStage.toLowerCase()}`)}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4"
		role="dialog"
		aria-modal="true"
		aria-labelledby="advance-modal-title"
	>
		<!-- Backdrop -->
		<button
			type="button"
			class="fixed inset-0 bg-black/60 backdrop-blur-sm transition-opacity"
			onclick={() => {
				if (!isSubmitting) onClose();
			}}
			tabindex="-1"
			aria-label={t('common.close_dialog')}
		></button>

		<!-- Modal Dialog Box -->
		<div
			use:modalA11y={{ onClose, disabled: isSubmitting }}
			class="glass-panel relative z-10 w-full max-w-md overflow-hidden rounded-3xl border border-zinc-200/80 p-6 shadow-2xl sm:p-7 dark:border-white/10"
		>
			<div
				class="flex items-center justify-between border-b border-zinc-200/80 pb-4 dark:border-white/10"
			>
				<div class="flex items-center gap-2.5">
					<div
						class="flex h-9 w-9 items-center justify-center rounded-xl bg-amber-500/15 text-amber-500 dark:text-amber-400"
					>
						<Sparkles class="h-5 w-5" />
					</div>
					<h3 id="advance-modal-title" class="text-lg font-bold text-zinc-900 dark:text-white">
						{t('dashboard.advance_stage_modal_title')}
					</h3>
				</div>
				<button
					type="button"
					onclick={onClose}
					disabled={isSubmitting}
					class="rounded-xl p-1.5 text-zinc-600 transition hover:bg-zinc-100 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800 dark:hover:text-white"
					aria-label={t('common.close_dialog')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			{#if errorMessage}
				<div
					role="alert"
					class="mt-4 rounded-xl border border-rose-500/30 bg-rose-500/10 p-3 text-xs text-rose-600 dark:text-rose-400"
				>
					{errorMessage}
				</div>
			{/if}

			<div class="my-5 space-y-4">
				<!-- Stage Transition Visual -->
				<div
					class="flex items-center justify-center gap-3 rounded-2xl bg-zinc-100/70 p-4 dark:bg-zinc-900/50"
				>
					<span
						class="rounded-xl border border-zinc-300 bg-white px-3 py-1.5 font-mono text-xs font-bold text-zinc-700 shadow-sm dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
					>
						{currentStageLabel}
					</span>
					<ArrowRight class="h-4 w-4 text-amber-500" />
					<span
						class="rounded-xl border border-amber-500/40 bg-amber-500/15 px-3 py-1.5 font-mono text-xs font-bold text-amber-700 shadow-sm dark:text-amber-300"
					>
						{nextStageLabel}
					</span>
				</div>

				<p class="text-center text-xs text-zinc-600 sm:text-sm dark:text-zinc-400">
					{t('dashboard.advance_stage_confirm', {
						batch: batch.name,
						current: currentStageLabel,
						next: nextStageLabel
					})}
				</p>

				{#if nextStage === 'Ferment'}
					<div class="space-y-3 border-t border-zinc-200/60 pt-3 dark:border-white/10">
						<div>
							<label
								for="dashboard-advance-fermenter"
								class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('batches.equipment.select_fermenter')}
							</label>
							<select
								id="dashboard-advance-fermenter"
								data-testid="dashboard-advance-fermenter-select"
								bind:value={selectedFermenterId}
								class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-xs text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
							>
								<option value="">{t('batches.workspace.no_fermenter_assigned')}</option>
								{#each availableFermenters as ferm (ferm.id)}
									<option value={ferm.id}>{formatEquipmentOption(ferm, true)}</option>
								{/each}
							</select>
						</div>

						<div>
							<label
								for="dashboard-advance-sensor"
								class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('batches.equipment.select_fermentation_sensor')}
							</label>
							<select
								id="dashboard-advance-sensor"
								data-testid="dashboard-advance-sensor-select"
								bind:value={selectedFermentationSensorId}
								class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-xs text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
							>
								<option value="">{t('batches.workspace.none_assigned')}</option>
								{#each availableSensors as sensor (sensor.id)}
									<option value={sensor.id}>{formatEquipmentOption(sensor, false)}</option>
								{/each}
							</select>
						</div>

						<div>
							<label
								for="dashboard-advance-pitch-temp"
								class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('batches.workspace.pitch_temp_label', { unit: settings.tempShort })}
							</label>
							<input
								id="dashboard-advance-pitch-temp"
								data-testid="dashboard-advance-pitch-temp-input"
								type="text"
								inputmode="decimal"
								bind:value={pitchTempStr}
								class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-xs text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
							/>
						</div>
					</div>
				{/if}
			</div>

			<!-- Modal Actions -->
			<div
				class="flex items-center justify-end gap-3 border-t border-zinc-200/80 pt-2 dark:border-white/10"
			>
				<button
					type="button"
					onclick={onClose}
					disabled={isSubmitting}
					class="rounded-xl border border-zinc-200 bg-white px-4 py-2 text-xs font-semibold text-zinc-800 transition hover:bg-zinc-100 dark:border-white/10 dark:bg-zinc-800 dark:text-zinc-200 dark:hover:bg-zinc-700"
				>
					{t('common.cancel')}
				</button>

				<button
					type="button"
					onclick={handleAdvance}
					disabled={isSubmitting}
					class="inline-flex items-center gap-1.5 rounded-xl border border-amber-500/50 bg-amber-500 px-4 py-2 text-xs font-bold text-zinc-950 transition hover:bg-amber-400 disabled:opacity-40"
				>
					{#if isSubmitting}
						<Loader2 class="h-3.5 w-3.5 animate-spin" />
						<span>{t('dashboard.advancing')}</span>
					{:else}
						<Sparkles class="h-3.5 w-3.5" />
						<span>{t('dashboard.advance_stage_btn')}</span>
					{/if}
				</button>
			</div>
		</div>
	</div>
{/if}
