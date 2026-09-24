<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import type {
		BatchMashStepDto,
		BrewStage,
		EquipmentDto,
		LogBatchTemperatureRequest
	} from '$lib/types/api';
	import { Thermometer, X, Loader2 } from '@lucide/svelte';

	interface Props {
		isOpen: boolean;
		stage: BrewStage;
		equipmentList: EquipmentDto[];
		defaultEquipmentId?: string | null;
		equipmentName?: string | null;
		mashSteps?: BatchMashStepDto[];
		defaultMashStepId?: string | null;
		targetTemperatureC?: number | null;
		onClose: () => void;
		onSubmit: (req: LogBatchTemperatureRequest) => Promise<void>;
	}

	let {
		isOpen,
		stage,
		equipmentList,
		defaultEquipmentId = null,
		equipmentName = null,
		mashSteps = [],
		defaultMashStepId = null,
		targetTemperatureC = null,
		onClose,
		onSubmit
	}: Props = $props();

	let selectedEquipmentId = $state<string>('');
	let selectedMashStepId = $state<string>('');
	let tempInputValue = $state<number>(65.0);
	let notes = $state<string>('');
	let saving = $state(false);
	let error = $state<string | null>(null);

	let resolvedEquipment = $derived.by(() => {
		if (selectedEquipmentId) {
			return equipmentList.find((e) => e.id === selectedEquipmentId) ?? null;
		}
		return null;
	});

	let resolvedEquipmentName = $derived(
		resolvedEquipment?.name ??
			equipmentName ??
			(defaultEquipmentId ? equipmentList.find((e) => e.id === defaultEquipmentId)?.name : null) ??
			(equipmentList.length > 0 ? equipmentList[0].name : '')
	);

	let resolvedStep = $derived.by(() => {
		if (stage === 'Mash' && selectedMashStepId && mashSteps.length > 0) {
			return mashSteps.find((s) => s.id === selectedMashStepId) ?? null;
		}
		return null;
	});

	// Sync defaults on open
	$effect(() => {
		if (isOpen) {
			selectedEquipmentId =
				defaultEquipmentId ?? (equipmentList.length > 0 ? equipmentList[0].id : '');
			selectedMashStepId = defaultMashStepId ?? (mashSteps.length > 0 ? mashSteps[0].id : '');
			const matchedStep =
				stage === 'Mash' && selectedMashStepId
					? mashSteps.find((s) => s.id === selectedMashStepId)
					: null;
			const target =
				targetTemperatureC ??
				matchedStep?.targetTemperatureC ??
				(stage === 'Boil' ? 100 : stage === 'Ferment' ? 20 : 65);
			tempInputValue =
				settings.temperatureUnit === 'Fahrenheit'
					? Number(((target * 9) / 5 + 32).toFixed(1))
					: target;
			notes = '';
			error = null;
		}
	});

	let unitLabel = $derived(settings.temperatureUnit === 'Fahrenheit' ? '°F' : '°C');

	async function handleSubmit(e: SubmitEvent) {
		e.preventDefault();
		if (tempInputValue === null || isNaN(tempInputValue)) {
			error = t('batches.telemetry.invalid_temp_error');
			return;
		}

		// Convert back to Celsius if Fahrenheit
		const tempC =
			settings.temperatureUnit === 'Fahrenheit' ? ((tempInputValue - 32) * 5) / 9 : tempInputValue;

		if (tempC < -20 || tempC > 120) {
			error = t('batches.telemetry.temp_range_error');
			return;
		}

		saving = true;
		error = null;

		try {
			let stepName: string | undefined = undefined;
			if (stage === 'Mash' && selectedMashStepId) {
				const s = mashSteps.find((ms) => ms.id === selectedMashStepId);
				stepName = s?.name;
			} else {
				stepName = t(`batches.stages.${stage.toLowerCase()}`);
			}

			await onSubmit({
				temperatureC: Number(tempC.toFixed(2)),
				equipmentId: selectedEquipmentId || null,
				stage: stage,
				batchMashStepId: stage === 'Mash' ? selectedMashStepId || null : null,
				stepName: stepName,
				notes: notes.trim() || null
			});
			onClose();
		} catch (err: unknown) {
			error = (err as Error).message || t('batches.telemetry.log_save_failed');
		} finally {
			saving = false;
		}
	}
</script>

{#if isOpen}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm"
		role="dialog"
		aria-modal="true"
		aria-labelledby="log-temp-modal-title"
	>
		<div
			class="glass-panel relative w-full max-w-md rounded-3xl border border-zinc-200/80 bg-white p-6 shadow-2xl dark:border-white/10 dark:bg-zinc-900"
		>
			<!-- Header -->
			<div
				class="flex items-center justify-between border-b border-zinc-200/60 pb-4 dark:border-white/5"
			>
				<div class="flex items-center gap-2.5">
					<div
						class="flex h-9 w-9 items-center justify-center rounded-xl bg-amber-500/10 text-amber-600 dark:bg-amber-500/20 dark:text-amber-400"
					>
						<Thermometer class="h-5 w-5" />
					</div>
					<div>
						<h3 id="log-temp-modal-title" class="text-base font-bold text-zinc-900 dark:text-white">
							{t('batches.telemetry.log_temp_modal_title')}
						</h3>
						<p class="text-xs text-zinc-500 dark:text-zinc-400">
							{t(`batches.stages.${stage.toLowerCase()}`)}
						</p>
					</div>
				</div>

				<button
					type="button"
					onclick={onClose}
					disabled={saving}
					class="cursor-pointer rounded-xl p-1.5 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('common.close')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<!-- Error Alert -->
			{#if error}
				<div
					class="mt-4 rounded-xl border border-red-500/20 bg-red-50/80 p-3 text-xs font-semibold text-red-700 dark:border-red-500/30 dark:bg-red-500/10 dark:text-red-300"
					role="alert"
				>
					{error}
				</div>
			{/if}

			<form onsubmit={handleSubmit} class="mt-4 space-y-4">
				<!-- Temperature Input -->
				<div>
					<label
						for="step-temp-input"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('batches.telemetry.temp_input_label')} ({unitLabel}) *
					</label>
					<div class="relative mt-1">
						<input
							id="step-temp-input"
							type="number"
							step="0.1"
							bind:value={tempInputValue}
							required
							class="w-full rounded-xl border border-zinc-300 bg-white px-3.5 py-2.5 font-mono text-sm font-semibold text-zinc-900 shadow-xs focus:border-amber-500 focus:ring-2 focus:ring-amber-500/20 focus:outline-hidden dark:border-zinc-700 dark:bg-zinc-900 dark:text-white"
						/>
						<span
							class="pointer-events-none absolute inset-y-0 right-0 flex items-center pr-3 font-mono text-xs text-zinc-400"
						>
							{unitLabel}
						</span>
					</div>
					{#if targetTemperatureC !== null && targetTemperatureC !== undefined}
						<p class="mt-1 text-[11px] text-zinc-500 dark:text-zinc-400">
							{t('batches.telemetry.target_temp')}: {settings.formatTemp(targetTemperatureC)}
						</p>
					{/if}
				</div>

				<!-- Current Step & Equipment Context -->
				<div
					class="rounded-2xl border border-zinc-200/80 bg-zinc-50/80 p-3.5 dark:border-white/10 dark:bg-zinc-900/50"
				>
					<div class="grid grid-cols-2 gap-3 text-xs">
						{#if stage === 'Mash'}
							<div>
								<span
									class="block text-[10px] font-semibold tracking-wider text-zinc-400 uppercase"
								>
									{t('batches.telemetry.mash_step')}
								</span>
								{#if resolvedStep}
									<span class="mt-0.5 block font-bold text-zinc-800 dark:text-zinc-200">
										#{resolvedStep.stepOrder}
										{resolvedStep.name}
									</span>
								{:else if mashSteps.length > 0}
									<select
										id="mash-step-select"
										bind:value={selectedMashStepId}
										class="mt-1 w-full rounded-lg border border-zinc-200 bg-white px-2 py-1 text-xs text-zinc-900 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-100"
									>
										{#each mashSteps as step (step.id)}
											<option value={step.id}>#{step.stepOrder} {step.name}</option>
										{/each}
									</select>
								{:else}
									<span class="mt-0.5 block text-zinc-500">—</span>
								{/if}
							</div>
						{/if}

						<div>
							<span class="block text-[10px] font-semibold tracking-wider text-zinc-400 uppercase">
								{t('batches.telemetry.equipment')}
							</span>
							{#if resolvedEquipmentName}
								<span class="mt-0.5 block font-bold text-zinc-800 dark:text-zinc-200">
									{resolvedEquipmentName}
								</span>
							{:else if equipmentList.length > 0}
								<select
									id="equipment-select"
									bind:value={selectedEquipmentId}
									class="mt-1 w-full rounded-lg border border-zinc-200 bg-white px-2 py-1 text-xs text-zinc-900 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-100"
								>
									{#each equipmentList as eq (eq.id)}
										<option value={eq.id}>{eq.name}</option>
									{/each}
								</select>
							{:else}
								<span class="mt-0.5 block text-zinc-500"
									>{t('batches.telemetry.no_equipment_available')}</span
								>
							{/if}
						</div>
					</div>
				</div>

				<!-- Notes Input -->
				<div>
					<label
						for="reading-notes-input"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('common.notes')} ({t('common.optional')})
					</label>
					<input
						id="reading-notes-input"
						type="text"
						bind:value={notes}
						placeholder={t('batches.telemetry.notes_placeholder')}
						maxlength="1000"
						class="mt-1 w-full rounded-xl border border-zinc-300 bg-white px-3 py-2 text-sm text-zinc-900 shadow-xs focus:border-amber-500 focus:ring-2 focus:ring-amber-500/20 focus:outline-hidden dark:border-zinc-700 dark:bg-zinc-900 dark:text-white"
					/>
				</div>

				<!-- Actions -->
				<div class="mt-6 flex items-center justify-end gap-3 pt-2">
					<button
						type="button"
						onclick={onClose}
						disabled={saving}
						class="cursor-pointer rounded-xl border border-zinc-300 px-4 py-2 text-xs font-semibold text-zinc-700 transition-colors hover:bg-zinc-50 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-800"
					>
						{t('common.cancel')}
					</button>

					<button
						type="submit"
						disabled={saving}
						class="inline-flex cursor-pointer items-center gap-2 rounded-xl bg-amber-500 px-4 py-2 text-xs font-semibold text-white shadow-xs transition-colors hover:bg-amber-600 disabled:opacity-50"
					>
						{#if saving}
							<Loader2 class="h-4 w-4 animate-spin" />
						{/if}
						<span>{t('batches.telemetry.save_reading')}</span>
					</button>
				</div>
			</form>
		</div>
	</div>
{/if}
