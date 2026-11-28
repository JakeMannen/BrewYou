<script lang="ts">
	import { tick, untrack } from 'svelte';
	import { ApiClientError } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import { settings, celsiusToFahrenheit, fahrenheitToCelsius } from '$lib/stores/settings.svelte';
	import { modalA11y } from '$lib/actions/modalA11y';
	import { isBatchTelemetrySensor } from '$lib/components/inventory/vessel-geometry';
	import {
		buildEquipmentIdUpdate,
		buildFermentationSensorAssignmentsUpdate,
		getPrimaryFermentationSensorAssignment,
		isPackagingVesselEquipment
	} from '$lib/utils/batchEdit';
	import { isEquipmentOccupied, type BatchOccupancySummary } from '$lib/utils/batchEquipment';
	import { formatDecimalInput, formatNumber, parseDecimal } from '$lib/utils/formatNumber';
	import type {
		BatchDetailDto,
		BatchStatus,
		EquipmentDto,
		UpdateBatchRequest
	} from '$lib/types/api';
	import { AlertTriangle, Edit3, Loader2, Save, X } from '@lucide/svelte';

	interface Props {
		open: boolean;
		batch: BatchDetailDto | null;
		equipmentList?: EquipmentDto[];
		activeBatches?: BatchOccupancySummary[];
		onClose: () => void;
		onSave: (req: UpdateBatchRequest) => Promise<void>;
	}

	let { open, batch, equipmentList = [], activeBatches = [], onClose, onSave }: Props = $props();

	let draftName = $state('');
	let draftBeerStyle = $state('');
	let draftBrewDate = $state('');
	let draftStatus = $state<BatchStatus>('Brewing');
	let draftMeasuredOgStr = $state('');
	let draftMeasuredFgStr = $state('');
	let draftMeasuredBatchSizeStr = $state('');
	let draftPitchTempDisplayStr = $state('');
	let draftMeasuredOg = $derived(parseDecimal(draftMeasuredOgStr));
	let draftMeasuredFg = $derived(parseDecimal(draftMeasuredFgStr));
	let draftMeasuredBatchSize = $derived(parseDecimal(draftMeasuredBatchSizeStr));
	let draftPitchTempDisplay = $derived(parseDecimal(draftPitchTempDisplayStr));
	let draftFermenterId = $state('');
	let draftFermentationSensorId = $state('');
	let draftPackagingVesselId = $state('');
	let draftNotes = $state('');

	let saving = $state(false);
	let errorMessage = $state<string | null>(null);
	let errorDetails = $state<string[]>([]);
	let errorBannerEl = $state<HTMLDivElement | null>(null);
	let snapshottedBatchId: string | null = null;

	let fermenters = $derived(equipmentList.filter((e) => e.type === 'Fermenter'));
	let fermentationSensors = $derived(equipmentList.filter((e) => isBatchTelemetrySensor(e)));
	let packagingVessels = $derived(equipmentList.filter((e) => isPackagingVesselEquipment(e)));
	let initialFermentationSensor = $derived(
		batch ? getPrimaryFermentationSensorAssignment(batch) : null
	);

	function getSubtypeName(subtype: string): string {
		return t(`equipment.subtypes.${subtype}`) || subtype;
	}

	function formatEquipmentOptionLabel(eq: EquipmentDto, includeCapacity = true): string {
		const base = includeCapacity
			? `${eq.name} (${settings.formatVolume(eq.capacityLiters)})`
			: `${eq.name} [${getSubtypeName(eq.subtype)}]`;
		const occupied = isEquipmentOccupied(eq.id, activeBatches, eq, batch?.id ?? null);
		return occupied ? `${base} — ${t('dashboard.status_occupied')}` : base;
	}

	async function revealErrorBanner() {
		await tick();
		errorBannerEl?.scrollIntoView?.({ behavior: 'smooth', block: 'nearest' });
	}

	const editableStatuses: BatchStatus[] = [
		'Planned',
		'Brewing',
		'Fermenting',
		'Conditioning',
		'Completed',
		'Archived'
	];

	// Snapshot draft only on open transition (or when editing a different batch ID)
	// so background SSE / polling updates never wipe unsaved user edits.
	$effect(() => {
		const currentId = open && batch ? batch.id : null;
		if (currentId && snapshottedBatchId !== currentId) {
			snapshottedBatchId = currentId;
			untrack(() => {
				if (!batch) return;
				draftName = batch.name ?? '';
				draftBeerStyle = batch.beerStyle ?? '';
				draftBrewDate = batch.brewDate ? batch.brewDate.slice(0, 10) : '';
				draftStatus = batch.status;
				draftMeasuredOgStr = formatDecimalInput(batch.measuredOg, 3);
				draftMeasuredFgStr = formatDecimalInput(batch.measuredFg, 3);
				draftMeasuredBatchSizeStr = formatDecimalInput(batch.measuredBatchSizeLiters, 1);
				const initialPitchDisplay =
					batch.pitchTemperatureC !== null && batch.pitchTemperatureC !== undefined
						? settings.temperatureUnit === 'Fahrenheit'
							? Number(celsiusToFahrenheit(batch.pitchTemperatureC).toFixed(1))
							: batch.pitchTemperatureC
						: null;
				draftPitchTempDisplayStr = formatDecimalInput(initialPitchDisplay, 1);
				draftFermenterId = batch.fermenterId ?? '';
				draftFermentationSensorId =
					getPrimaryFermentationSensorAssignment(batch)?.equipmentId ?? '';
				draftPackagingVesselId = batch.packagingVesselId ?? '';
				draftNotes = batch.notes ?? '';
				saving = false;
				errorMessage = null;
				errorDetails = [];
			});
		} else if (!open) {
			snapshottedBatchId = null;
		}
	});

	function getStatusLabel(status: BatchStatus): string {
		switch (status) {
			case 'Planned':
				return t('batches.status_planned');
			case 'Brewing':
				return t('batches.status_brewing');
			case 'Fermenting':
				return t('batches.status_fermenting');
			case 'Conditioning':
				return t('batches.status_conditioning');
			case 'Completed':
				return t('batches.status_completed');
			case 'Archived':
				return t('batches.status_archived');
		}
	}

	function validateForm(): string | null {
		if (!draftName || draftName.trim().length === 0) {
			return t('batches.edit_validation.name_required');
		}
		if (draftMeasuredOgStr.trim() !== '' && draftMeasuredOg === null) {
			return t('batches.edit_validation.og_range');
		}
		if (draftMeasuredOg !== null && draftMeasuredOg !== undefined) {
			if (Number.isNaN(draftMeasuredOg) || draftMeasuredOg < 1.0 || draftMeasuredOg > 1.25) {
				return t('batches.edit_validation.og_range');
			}
		}
		if (draftMeasuredFgStr.trim() !== '' && draftMeasuredFg === null) {
			return t('batches.edit_validation.fg_range');
		}
		if (draftMeasuredFg !== null && draftMeasuredFg !== undefined) {
			if (Number.isNaN(draftMeasuredFg) || draftMeasuredFg < 0.98 || draftMeasuredFg > 1.2) {
				return t('batches.edit_validation.fg_range');
			}
		}
		if (
			draftMeasuredOg !== null &&
			draftMeasuredOg !== undefined &&
			draftMeasuredFg !== null &&
			draftMeasuredFg !== undefined &&
			draftMeasuredFg >= draftMeasuredOg
		) {
			return t('batches.edit_validation.fg_lower_than_og');
		}
		if (draftMeasuredBatchSizeStr.trim() !== '' && draftMeasuredBatchSize === null) {
			return t('batches.edit_validation.volume_non_negative');
		}
		if (draftMeasuredBatchSize !== null && draftMeasuredBatchSize !== undefined) {
			if (Number.isNaN(draftMeasuredBatchSize) || draftMeasuredBatchSize < 0) {
				return t('batches.edit_validation.volume_non_negative');
			}
		}
		if (draftPitchTempDisplayStr.trim() !== '' && draftPitchTempDisplay === null) {
			return t('batches.telemetry.invalid_temp_error');
		}
		return null;
	}

	async function handleSubmit(event: SubmitEvent) {
		event.preventDefault();
		if (!batch || saving) return;

		const validationError = validateForm();
		if (validationError) {
			errorMessage = validationError;
			errorDetails = [];
			void revealErrorBanner();
			return;
		}

		saving = true;
		errorMessage = null;
		errorDetails = [];
		try {
			const pitchTempC =
				draftPitchTempDisplay !== null &&
				draftPitchTempDisplay !== undefined &&
				!Number.isNaN(draftPitchTempDisplay)
					? Number(
							(settings.temperatureUnit === 'Fahrenheit'
								? fahrenheitToCelsius(draftPitchTempDisplay)
								: draftPitchTempDisplay
							).toFixed(2)
						)
					: null;

			const req: UpdateBatchRequest = {
				name: draftName.trim(),
				beerStyle: draftBeerStyle.trim() || null,
				brewDate: draftBrewDate || null,
				status: draftStatus,
				notes: draftNotes.trim() || null
			};

			if (
				draftMeasuredOg !== null &&
				draftMeasuredOg !== undefined &&
				!Number.isNaN(draftMeasuredOg)
			) {
				req.measuredOg = draftMeasuredOg;
			}
			if (
				draftMeasuredFg !== null &&
				draftMeasuredFg !== undefined &&
				!Number.isNaN(draftMeasuredFg)
			) {
				req.measuredFg = draftMeasuredFg;
			}
			if (
				draftMeasuredBatchSize !== null &&
				draftMeasuredBatchSize !== undefined &&
				!Number.isNaN(draftMeasuredBatchSize)
			) {
				req.measuredBatchSizeLiters = draftMeasuredBatchSize;
			}
			if (pitchTempC !== null) {
				req.pitchTemperatureC = pitchTempC;
			}
			const fermenterUpdate = buildEquipmentIdUpdate(draftFermenterId, batch.fermenterId);
			if (fermenterUpdate !== undefined) {
				req.fermenterId = fermenterUpdate;
			}

			const sensorAssignmentsUpdate = buildFermentationSensorAssignmentsUpdate(
				batch,
				draftFermentationSensorId
			);
			if (sensorAssignmentsUpdate !== undefined) {
				req.sensorAssignments = sensorAssignmentsUpdate;
			}

			const packagingUpdate = buildEquipmentIdUpdate(
				draftPackagingVesselId,
				batch.packagingVesselId
			);
			if (packagingUpdate !== undefined) {
				req.packagingVesselId = packagingUpdate;
			}

			await onSave(req);
			onClose();
		} catch (err: unknown) {
			if (err instanceof ApiClientError && err.details && err.details.length > 0) {
				errorMessage = err.message || t('common.error_generic');
				errorDetails = err.details.map((d) => (d.field ? `${d.field}: ${d.issue}` : d.issue));
			} else {
				errorMessage = (err as Error).message || t('common.error_generic');
				errorDetails = [];
			}
			void revealErrorBanner();
		} finally {
			saving = false;
		}
	}
</script>

{#if open && batch}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4"
		role="dialog"
		aria-modal="true"
		aria-labelledby="batch-edit-modal-title"
		data-testid="batch-edit-modal"
	>
		<div
			class="fixed inset-0 bg-black/70 backdrop-blur-sm transition-opacity"
			onclick={() => {
				if (!saving) onClose();
			}}
			aria-hidden="true"
		></div>

		<div
			use:modalA11y={{ onClose, disabled: saving }}
			class="relative z-10 max-h-[90vh] w-full max-w-2xl overflow-y-auto rounded-3xl border border-zinc-200 bg-white p-6 shadow-2xl dark:border-zinc-800 dark:bg-zinc-950"
		>
			<div
				class="flex items-start justify-between border-b border-zinc-200 pb-4 dark:border-zinc-800"
			>
				<div class="flex items-center gap-3">
					<div
						class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
					>
						<Edit3 class="h-5 w-5" />
					</div>
					<div>
						<h2 id="batch-edit-modal-title" class="text-lg font-bold text-zinc-900 dark:text-white">
							{t('batches.workspace.edit_batch_modal_title')}
						</h2>
						<p class="text-xs text-zinc-500 dark:text-zinc-400">
							{t('batches.workspace.edit_batch_modal_desc')}
						</p>
					</div>
				</div>

				<button
					type="button"
					onclick={onClose}
					disabled={saving}
					aria-label={t('common.close_dialog')}
					class="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 disabled:opacity-50 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			{#if errorMessage}
				<div
					bind:this={errorBannerEl}
					role="alert"
					data-testid="batch-edit-error-banner"
					class="mt-4 flex items-start gap-2 rounded-xl border border-red-200 bg-red-50 p-3 text-xs text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
				>
					<AlertTriangle class="h-4 w-4 shrink-0 text-red-500" />
					<div class="space-y-1">
						<p class="font-semibold">{errorMessage}</p>
						{#if errorDetails.length > 0}
							<ul class="list-disc space-y-0.5 pl-4">
								{#each errorDetails as detail (detail)}
									<li>{detail}</li>
								{/each}
							</ul>
						{/if}
					</div>
				</div>
			{/if}

			<form onsubmit={handleSubmit} class="mt-5 space-y-4">
				<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
					<div>
						<label
							for="edit-batch-name"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.wizard.batch_name')} *
						</label>
						<input
							id="edit-batch-name"
							name="name"
							type="text"
							required
							bind:value={draftName}
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div>
						<label
							for="edit-batch-style"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.wizard.beer_style')}
						</label>
						<input
							id="edit-batch-style"
							name="beerStyle"
							type="text"
							bind:value={draftBeerStyle}
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div>
						<label
							for="edit-batch-brew-date"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.wizard.brew_date')}
						</label>
						<input
							id="edit-batch-brew-date"
							name="brewDate"
							type="date"
							bind:value={draftBrewDate}
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div>
						<label
							for="edit-batch-status"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.workspace.batch_status_label')}
						</label>
						<select
							id="edit-batch-status"
							name="status"
							bind:value={draftStatus}
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						>
							{#each editableStatuses as st (st)}
								<option value={st}>{getStatusLabel(st)}</option>
							{/each}
						</select>
					</div>

					<div>
						<label
							for="edit-batch-og"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.workspace.measured_og_label')}
						</label>
						<input
							id="edit-batch-og"
							name="measuredOg"
							type="text"
							inputmode="decimal"
							bind:value={draftMeasuredOgStr}
							placeholder={`${t('batches.workspace.example_prefix')} ${formatNumber(batch.targetOg, 3)}`}
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div>
						<label
							for="edit-batch-fg"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.workspace.measured_fg_label')}
						</label>
						<input
							id="edit-batch-fg"
							name="measuredFg"
							type="text"
							inputmode="decimal"
							bind:value={draftMeasuredFgStr}
							placeholder={`${t('batches.workspace.example_prefix')} ${formatNumber(batch.targetFg, 3)}`}
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div>
						<label
							for="edit-batch-volume"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.workspace.measured_volume_label')}
						</label>
						<input
							id="edit-batch-volume"
							name="measuredBatchSizeLiters"
							type="text"
							inputmode="decimal"
							bind:value={draftMeasuredBatchSizeStr}
							placeholder={`${t('batches.workspace.example_prefix')} ${formatNumber(batch.targetBatchSizeLiters, 1)}`}
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div>
						<label
							for="edit-batch-pitch-temp"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.workspace.pitch_temp_label', { unit: settings.tempShort })}
						</label>
						<input
							id="edit-batch-pitch-temp"
							name="pitchTemperatureC"
							type="text"
							inputmode="decimal"
							bind:value={draftPitchTempDisplayStr}
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div>
						<label
							for="edit-batch-fermenter"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.equipment.select_fermenter')}
						</label>
						<select
							id="edit-batch-fermenter"
							name="fermenterId"
							data-testid="edit-batch-fermenter-select"
							bind:value={draftFermenterId}
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						>
							{#if batch.fermenterId && !fermenters.some((e) => e.id === batch.fermenterId)}
								<option value={batch.fermenterId} disabled>
									{batch.fermenterName
										? `${batch.fermenterName} (${t('common.deleted')})`
										: t('batches.workspace.deleted_vessel')}
								</option>
							{/if}
							<option value="">{t('batches.workspace.no_fermenter_assigned')}</option>
							{#each fermenters as fermenter (fermenter.id)}
								<option value={fermenter.id}>{formatEquipmentOptionLabel(fermenter, true)}</option>
							{/each}
						</select>
					</div>

					<div>
						<label
							for="edit-batch-fermentation-sensor"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.equipment.select_fermentation_sensor')}
						</label>
						<select
							id="edit-batch-fermentation-sensor"
							name="fermentationSensorId"
							data-testid="edit-batch-fermentation-sensor-select"
							bind:value={draftFermentationSensorId}
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						>
							{#if initialFermentationSensor && !fermentationSensors.some((s) => s.id === initialFermentationSensor.equipmentId)}
								<option value={initialFermentationSensor.equipmentId} disabled>
									{initialFermentationSensor.equipmentName
										? `${initialFermentationSensor.equipmentName} (${t('common.deleted')})`
										: t('batches.workspace.deleted_vessel')}
								</option>
							{/if}
							<option value="">{t('batches.workspace.none_assigned')}</option>
							{#each fermentationSensors as sensor (sensor.id)}
								<option value={sensor.id}>
									{formatEquipmentOptionLabel(sensor, false)}
								</option>
							{/each}
						</select>
					</div>

					<div class="sm:col-span-2">
						<label
							for="edit-batch-packaging-vessel"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.equipment.select_packaging_vessel')}
						</label>
						<select
							id="edit-batch-packaging-vessel"
							name="packagingVesselId"
							bind:value={draftPackagingVesselId}
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						>
							{#if batch.packagingVesselId && !packagingVessels.some((e) => e.id === batch.packagingVesselId)}
								<option value={batch.packagingVesselId} disabled>
									{batch.packagingVesselName
										? `${batch.packagingVesselName} (${t('common.deleted')})`
										: t('batches.workspace.deleted_vessel')}
								</option>
							{/if}
							<option value="">{t('batches.workspace.bottled_cellared_option')}</option>
							{#each packagingVessels as keg (keg.id)}
								<option value={keg.id}>{formatEquipmentOptionLabel(keg, true)}</option>
							{/each}
						</select>
					</div>

					<div class="sm:col-span-2">
						<label
							for="edit-batch-notes"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.workspace.reading_notes_label')}
						</label>
						<textarea
							id="edit-batch-notes"
							name="notes"
							rows="3"
							bind:value={draftNotes}
							placeholder={t('batches.workspace.stage_notes_placeholder')}
							class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white p-2.5 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						></textarea>
					</div>
				</div>

				<div
					class="flex items-center justify-end gap-3 border-t border-zinc-200 pt-4 dark:border-zinc-800"
				>
					<button
						type="button"
						data-testid="cancel-batch-edit-btn"
						onclick={onClose}
						disabled={saving}
						class="rounded-xl border border-zinc-200 px-4 py-2 text-xs font-semibold text-zinc-700 transition-colors hover:bg-zinc-100 disabled:opacity-50 dark:border-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-900"
					>
						{t('brewery.cancel')}
					</button>

					<button
						type="submit"
						data-testid="save-batch-edit-btn"
						disabled={saving}
						class="flex min-h-[40px] items-center gap-2 rounded-xl bg-amber-500 px-5 py-2 text-xs font-bold text-zinc-950 shadow-md transition-all hover:bg-amber-400 disabled:opacity-50"
					>
						{#if saving}
							<Loader2 class="h-4 w-4 animate-spin" />
							<span>{t('common.saving')}</span>
						{:else}
							<Save class="h-4 w-4" />
							<span>{t('common.save')}</span>
						{/if}
					</button>
				</div>
			</form>
		</div>
	</div>
{/if}
