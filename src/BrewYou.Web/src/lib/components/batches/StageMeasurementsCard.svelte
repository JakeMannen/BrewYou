<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { formatNumber } from '$lib/utils/formatNumber';
	import {
		calculateBoilTimeAdjustment,
		recalculatePostStepMilestones
	} from '$lib/calculators/volume';
	import type {
		BatchDetailDto,
		BatchVolumeProfileDto,
		BrewStage,
		EquipmentDto,
		UpdateBatchRequest
	} from '$lib/types/api';
	import { Edit3, Save, Loader2, CheckCircle2, AlertTriangle, Droplets } from '@lucide/svelte';

	interface Props {
		stage: BrewStage;
		batch: BatchDetailDto;
		equipmentList?: EquipmentDto[];
		onSave: (req: UpdateBatchRequest) => Promise<void>;
		class?: string;
	}

	let { stage, batch, equipmentList = [], onSave, class: className = '' }: Props = $props();

	let isEditing = $state(false);
	let saving = $state(false);
	let error = $state<string | null>(null);
	let success = $state<string | null>(null);

	// Draft inputs
	let draftPreBoilVol = $state<number | null>(null);
	let draftPreBoilGrav = $state<number | null>(null);
	let draftPostBoilVol = $state<number | null>(null);
	let draftMeasuredOg = $state<number | null>(null);
	let draftPitchTemp = $state<number | null>(null);
	let draftMeasuredBatchSize = $state<number | null>(null);
	let draftMeasuredFg = $state<number | null>(null);
	let draftPackagedVol = $state<number | null>(null);
	let draftPackagingVesselId = $state<string>('');
	let draftNotes = $state<string>('');

	let displayVolumeProfile = $derived.by(() => {
		if (!batch?.volumeProfile) return null;
		const vp: BatchVolumeProfileDto = { ...batch.volumeProfile };
		if (isEditing) {
			if (draftPreBoilVol !== null && draftPreBoilVol !== undefined && !isNaN(draftPreBoilVol)) {
				vp.measuredPreBoilVolumeLiters = draftPreBoilVol;
			}
			if (draftPreBoilGrav !== null && draftPreBoilGrav !== undefined && !isNaN(draftPreBoilGrav)) {
				vp.measuredPreBoilGravity = draftPreBoilGrav;
			}
			if (draftPostBoilVol !== null && draftPostBoilVol !== undefined && !isNaN(draftPostBoilVol)) {
				vp.measuredPostBoilVolumeLiters = draftPostBoilVol;
			}
			if (
				draftMeasuredBatchSize !== null &&
				draftMeasuredBatchSize !== undefined &&
				!isNaN(draftMeasuredBatchSize)
			) {
				vp.measuredFermenterVolumeLiters = draftMeasuredBatchSize;
			}
			if (draftPackagedVol !== null && draftPackagedVol !== undefined && !isNaN(draftPackagedVol)) {
				vp.measuredPackagedVolumeLiters = draftPackagedVol;
			}
		}
		return recalculatePostStepMilestones(vp, batch.boilTimeMinutes ?? 60);
	});

	let plannedGrainAbsorption = $derived.by(() => {
		const vp = displayVolumeProfile ?? batch?.volumeProfile;
		if (!vp || !vp.totalWaterLiters || !vp.targetPreBoilVolumeLiters) return null;
		const loss =
			vp.totalWaterLiters - vp.targetPreBoilVolumeLiters - (vp.mashTunDeadSpaceLiters || 0);
		return loss > 0 ? loss : null;
	});

	let boilExtensionMinutes = $derived.by(() => {
		if (stage !== 'Boil' || !displayVolumeProfile) return 0;
		const preBoilVol = displayVolumeProfile.measuredPreBoilVolumeLiters;
		if (!preBoilVol) return 0;
		return calculateBoilTimeAdjustment(
			preBoilVol,
			batch.volumeProfile?.targetPreBoilVolumeLiters ??
				displayVolumeProfile.targetPreBoilVolumeLiters,
			displayVolumeProfile.boilOffRatePerHour
		);
	});

	function startEditing() {
		draftPreBoilVol = batch.volumeProfile?.measuredPreBoilVolumeLiters ?? null;
		draftPreBoilGrav = batch.volumeProfile?.measuredPreBoilGravity ?? null;
		draftPostBoilVol = batch.volumeProfile?.measuredPostBoilVolumeLiters ?? null;
		draftMeasuredOg = batch.measuredOg ?? null;
		draftPitchTemp = batch.pitchTemperatureC ?? null;
		draftMeasuredBatchSize = batch.measuredBatchSizeLiters ?? null;
		draftMeasuredFg = batch.measuredFg ?? null;
		draftPackagedVol = batch.volumeProfile?.measuredPackagedVolumeLiters ?? null;
		draftPackagingVesselId = batch.packagingVesselId ?? '';
		draftNotes = batch.notes ?? '';
		error = null;
		success = null;
		isEditing = true;
	}

	function cancelEditing() {
		isEditing = false;
		error = null;
	}

	async function handleSubmit(event: SubmitEvent) {
		event.preventDefault();
		saving = true;
		error = null;
		success = null;

		try {
			const req: UpdateBatchRequest = {};

			if (stage === 'Mash') {
				req.measuredPreBoilVolumeLiters = draftPreBoilVol;
				req.measuredPreBoilGravity = draftPreBoilGrav;
			} else if (stage === 'Boil') {
				req.measuredPreBoilVolumeLiters = draftPreBoilVol;
				req.measuredPreBoilGravity = draftPreBoilGrav;
				req.measuredPostBoilVolumeLiters = draftPostBoilVol;
				req.measuredOg = draftMeasuredOg;
			} else if (stage === 'Ferment') {
				req.measuredOg = draftMeasuredOg;
				req.pitchTemperatureC = draftPitchTemp;
				req.measuredBatchSizeLiters = draftMeasuredBatchSize;
			} else if (stage === 'Condition') {
				// Condition stage inputs
			} else if (stage === 'Package') {
				req.measuredFg = draftMeasuredFg;
				req.measuredPackagedVolumeLiters = draftPackagedVol;
				req.packagingVesselId = draftPackagingVesselId || null;
			}

			if (draftNotes !== (batch.notes ?? '')) {
				req.notes = draftNotes.trim() || null;
			}

			await onSave(req);
			isEditing = false;
			success = t('batches.workspace.stage_inputs_saved');
			setTimeout(() => {
				success = null;
			}, 4000);
		} catch (err: unknown) {
			error = (err as Error).message || t('batches.workspace.stage_inputs_error');
		} finally {
			saving = false;
		}
	}
</script>

<div
	class="glass-panel rounded-3xl border border-zinc-200/80 p-5 sm:p-6 dark:border-white/10 {className}"
>
	<div
		class="flex flex-wrap items-center justify-between gap-3 border-b border-zinc-200/60 pb-4 dark:border-white/5"
	>
		<div>
			<h2 class="text-base font-bold text-zinc-900 dark:text-white">
				{t('batches.workspace.stage_measurements_title')}
			</h2>
			<p class="text-xs text-zinc-500 dark:text-zinc-400">
				{t('batches.workspace.stage_measurements_desc')}
			</p>
		</div>

		{#if !isEditing}
			<button
				type="button"
				onclick={startEditing}
				class="flex min-h-[40px] items-center gap-2 rounded-xl border border-zinc-200/80 bg-white px-3.5 py-2 text-xs font-semibold text-zinc-700 shadow-xs transition-all hover:border-amber-500/40 hover:bg-amber-500/5 hover:text-amber-700 dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-200 dark:hover:bg-zinc-800"
			>
				<Edit3 class="h-3.5 w-3.5 text-amber-500" />
				<span>{t('batches.workspace.edit_stage_inputs')}</span>
			</button>
		{/if}
	</div>

	{#if success}
		<div
			role="status"
			class="mt-4 flex items-center gap-2 rounded-xl border border-emerald-500/30 bg-emerald-500/10 p-3 text-xs font-medium text-emerald-800 dark:text-emerald-300"
		>
			<CheckCircle2 class="h-4 w-4 shrink-0 text-emerald-500" />
			<span>{success}</span>
		</div>
	{/if}

	{#if error}
		<div
			role="alert"
			class="mt-4 flex items-center gap-2 rounded-xl border border-red-500/30 bg-red-500/10 p-3 text-xs font-medium text-red-800 dark:text-red-300"
		>
			<AlertTriangle class="h-4 w-4 shrink-0 text-red-500" />
			<span>{error}</span>
		</div>
	{/if}

	{#if !isEditing}
		<!-- READ-ONLY SUMMARY GRID -->
		<div class="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
			{#if stage === 'Mash'}
				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.workspace.pre_boil_vol_label')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-zinc-900 dark:text-white">
						{#if batch.volumeProfile?.measuredPreBoilVolumeLiters}
							<span class="text-emerald-600 dark:text-emerald-400">
								{formatNumber(batch.volumeProfile.measuredPreBoilVolumeLiters, 1)}L
							</span>
							{#if batch.volumeProfile.targetPreBoilVolumeLiters}
								<span class="text-xs font-normal text-zinc-400">
									/ {formatNumber(batch.volumeProfile.targetPreBoilVolumeLiters, 1)}L
								</span>
							{/if}
						{:else}
							<span class="text-zinc-400">—</span>
						{/if}
					</div>
				</div>

				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.workspace.pre_boil_grav_label')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-zinc-900 dark:text-white">
						{batch.volumeProfile?.measuredPreBoilGravity
							? `${formatNumber(batch.volumeProfile.measuredPreBoilGravity, 3)} SG`
							: '—'}
					</div>
				</div>

				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.workspace.grain_absorption_label')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-amber-700 dark:text-amber-300">
						{plannedGrainAbsorption !== null ? `~${formatNumber(plannedGrainAbsorption, 1)}L` : '—'}
					</div>
				</div>
			{:else if stage === 'Boil'}
				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.workspace.pre_boil_vol_label')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-zinc-900 dark:text-white">
						{batch.volumeProfile?.measuredPreBoilVolumeLiters
							? `${formatNumber(batch.volumeProfile.measuredPreBoilVolumeLiters, 1)}L`
							: '—'}
					</div>
				</div>

				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.workspace.pre_boil_grav_label')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-zinc-900 dark:text-white">
						{batch.volumeProfile?.measuredPreBoilGravity
							? `${formatNumber(batch.volumeProfile.measuredPreBoilGravity, 3)} SG`
							: '—'}
					</div>
				</div>

				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.workspace.post_boil_vol_label')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-zinc-900 dark:text-white">
						{batch.volumeProfile?.measuredPostBoilVolumeLiters
							? `${formatNumber(batch.volumeProfile.measuredPostBoilVolumeLiters, 1)}L`
							: '—'}
					</div>
				</div>

				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.measured_og')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-zinc-900 dark:text-white">
						{batch.measuredOg ? `${formatNumber(batch.measuredOg, 3)} SG` : '—'}
					</div>
				</div>
			{:else if stage === 'Ferment'}
				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.measured_og')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-zinc-900 dark:text-white">
						{batch.measuredOg ? `${formatNumber(batch.measuredOg, 3)} SG` : '—'}
					</div>
				</div>

				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.workspace.pitch_temp_label')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-zinc-900 dark:text-white">
						{batch.pitchTemperatureC !== null && batch.pitchTemperatureC !== undefined
							? `${formatNumber(batch.pitchTemperatureC, 1)}°C`
							: '—'}
					</div>
				</div>

				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.workspace.measured_volume_label')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-zinc-900 dark:text-white">
						{batch.measuredBatchSizeLiters
							? `${formatNumber(batch.measuredBatchSizeLiters, 1)}L`
							: '—'}
					</div>
				</div>
			{:else if stage === 'Condition'}
				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.current_gravity')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-emerald-600 dark:text-emerald-400">
						{batch.currentGravity ? `${formatNumber(batch.currentGravity, 3)} SG` : '—'}
					</div>
				</div>
			{:else if stage === 'Package'}
				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.workspace.measured_fg_label')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-zinc-900 dark:text-white">
						{batch.measuredFg ? `${formatNumber(batch.measuredFg, 3)} SG` : '—'}
					</div>
				</div>

				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.workspace.packaged_vol_label')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-zinc-900 dark:text-white">
						{batch.volumeProfile?.measuredPackagedVolumeLiters
							? `${formatNumber(batch.volumeProfile.measuredPackagedVolumeLiters, 1)}L`
							: '—'}
					</div>
				</div>

				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.equipment.packaging_vessel')}
					</span>
					<div class="mt-1 text-xs font-medium text-zinc-900 dark:text-white">
						{batch.packagingVesselName || t('batches.workspace.bottled_cellared_option')}
					</div>
				</div>
			{/if}
		</div>

		{#if batch.notes}
			<div
				class="mt-3 rounded-xl border border-zinc-200/50 bg-zinc-50/50 p-3 dark:border-white/5 dark:bg-zinc-900/30"
			>
				<span class="block text-[10px] font-semibold text-zinc-400 uppercase">
					{t('batches.workspace.reading_notes_label')}
				</span>
				<p class="mt-0.5 text-xs text-zinc-700 dark:text-zinc-300">
					{batch.notes}
				</p>
			</div>
		{/if}
	{:else}
		<!-- INLINE EDIT FORM -->
		<form onsubmit={handleSubmit} class="mt-4 space-y-4">
			<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
				{#if stage === 'Mash'}
					<div>
						<label
							for="edit-preboil-vol"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.workspace.pre_boil_vol_label')}
						</label>
						<input
							id="edit-preboil-vol"
							type="number"
							step="0.1"
							bind:value={draftPreBoilVol}
							placeholder="e.g. 28.5"
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div>
						<label
							for="edit-preboil-grav"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.workspace.pre_boil_grav_label')}
						</label>
						<input
							id="edit-preboil-grav"
							type="number"
							step="0.001"
							bind:value={draftPreBoilGrav}
							placeholder="e.g. 1.045"
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>
				{:else if stage === 'Boil'}
					<div>
						<label
							for="edit-preboil-vol"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.workspace.pre_boil_vol_label')}
						</label>
						<input
							id="edit-preboil-vol"
							type="number"
							step="0.1"
							bind:value={draftPreBoilVol}
							placeholder="e.g. 28.5"
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div>
						<label
							for="edit-preboil-grav"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.workspace.pre_boil_grav_label')}
						</label>
						<input
							id="edit-preboil-grav"
							type="number"
							step="0.001"
							bind:value={draftPreBoilGrav}
							placeholder="e.g. 1.045"
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div>
						<label
							for="edit-postboil-vol"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.workspace.post_boil_vol_label')}
						</label>
						<input
							id="edit-postboil-vol"
							type="number"
							step="0.1"
							bind:value={draftPostBoilVol}
							placeholder="e.g. 23.0"
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div>
						<label
							for="edit-measured-og"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.measured_og')}
						</label>
						<input
							id="edit-measured-og"
							type="number"
							step="0.001"
							bind:value={draftMeasuredOg}
							placeholder="e.g. 1.055"
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>
				{:else if stage === 'Ferment'}
					<div>
						<label
							for="edit-measured-og"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.measured_og')}
						</label>
						<input
							id="edit-measured-og"
							type="number"
							step="0.001"
							bind:value={draftMeasuredOg}
							placeholder="e.g. 1.055"
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div>
						<label
							for="edit-pitch-temp"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.workspace.pitch_temp_label')}
						</label>
						<input
							id="edit-pitch-temp"
							type="number"
							step="0.5"
							bind:value={draftPitchTemp}
							placeholder="e.g. 19.0"
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div class="sm:col-span-2">
						<label
							for="edit-fermenter-vol"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.workspace.measured_volume_label')}
						</label>
						<input
							id="edit-fermenter-vol"
							type="number"
							step="0.1"
							bind:value={draftMeasuredBatchSize}
							placeholder="e.g. 20.0"
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>
				{:else if stage === 'Package'}
					<div>
						<label
							for="edit-measured-fg"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.workspace.measured_fg_label')}
						</label>
						<input
							id="edit-measured-fg"
							type="number"
							step="0.001"
							bind:value={draftMeasuredFg}
							placeholder="e.g. 1.010"
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div>
						<label
							for="edit-packaged-vol"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.workspace.packaged_vol_label')}
						</label>
						<input
							id="edit-packaged-vol"
							type="number"
							step="0.1"
							bind:value={draftPackagedVol}
							placeholder="e.g. 18.5"
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>

					<div class="sm:col-span-2">
						<label
							for="edit-packaging-vessel"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('batches.equipment.select_packaging_vessel')}
						</label>
						<select
							id="edit-packaging-vessel"
							bind:value={draftPackagingVesselId}
							class="mt-1 h-10 w-full rounded-xl border border-zinc-200/80 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
						>
							<option value="">{t('batches.workspace.bottled_cellared_option')}</option>
							{#each equipmentList.filter((e) => e.type === 'Keg') as keg}
								<option value={keg.id}>{keg.name} ({keg.capacity}L)</option>
							{/each}
						</select>
					</div>
				{/if}

				<div class="sm:col-span-2">
					<label
						for="edit-stage-notes"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('batches.workspace.reading_notes_label')}
					</label>
					<textarea
						id="edit-stage-notes"
						rows="2"
						bind:value={draftNotes}
						placeholder={t('batches.workspace.stage_notes_placeholder')}
						class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white p-2.5 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900 dark:text-zinc-100"
					></textarea>
				</div>
			</div>

			<div
				class="flex items-center justify-end gap-2.5 border-t border-zinc-200/60 pt-3 dark:border-white/5"
			>
				<button
					type="button"
					onclick={cancelEditing}
					disabled={saving}
					class="min-h-[38px] rounded-xl border border-zinc-200 px-4 py-2 text-xs font-semibold text-zinc-600 transition-colors hover:bg-zinc-100 dark:border-zinc-800 dark:text-zinc-400 dark:hover:bg-zinc-800"
				>
					{t('batches.workspace.cancel_edit_stage_inputs')}
				</button>

				<button
					type="submit"
					disabled={saving}
					class="flex min-h-[38px] items-center gap-2 rounded-xl bg-amber-500 px-5 py-2 text-xs font-bold text-zinc-950 shadow-md transition-all hover:bg-amber-400 disabled:opacity-50"
				>
					{#if saving}
						<Loader2 class="h-3.5 w-3.5 animate-spin" />
						<span>{t('common.saving')}</span>
					{:else}
						<Save class="h-3.5 w-3.5 stroke-[2.5]" />
						<span>{t('batches.workspace.save_stage_inputs')}</span>
					{/if}
				</button>
			</div>
		</form>
	{/if}

	<!-- Water Schedule & Volume Milestones -->
	{#if displayVolumeProfile}
		<div class="mt-6 border-t border-zinc-200/60 pt-5 dark:border-white/5">
			<div class="flex flex-wrap items-center justify-between gap-3">
				<div class="flex items-center gap-2">
					<Droplets class="h-4 w-4 text-amber-500" />
					<h3 class="text-xs font-bold tracking-wider text-zinc-700 uppercase dark:text-zinc-300">
						{t('batches.workspace.volume_milestones_and_water')}
					</h3>
				</div>
				<div class="flex flex-wrap items-center gap-2 text-xs text-zinc-600 dark:text-zinc-400">
					<span
						>{t('batches.wizard.strike_water')}:
						<strong class="font-mono text-amber-600 dark:text-amber-400"
							>{formatNumber(displayVolumeProfile.strikeWaterLiters, 1)}L</strong
						></span
					>
					<span>•</span>
					<span
						>{t('batches.wizard.sparge_water')}:
						<strong class="font-mono text-amber-600 dark:text-amber-400"
							>{formatNumber(displayVolumeProfile.spargeWaterLiters, 1)}L</strong
						></span
					>
					<span>•</span>
					<span
						>{t('batches.wizard.total_water')}:
						<strong class="font-mono text-zinc-900 dark:text-white"
							>{formatNumber(displayVolumeProfile.totalWaterLiters, 1)}L</strong
						></span
					>
				</div>
			</div>

			<!-- Checkpoints progression -->
			<div class="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.wizard.pre_boil_target')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-zinc-900 dark:text-white">
						{#if displayVolumeProfile.measuredPreBoilVolumeLiters}
							<span class="text-emerald-600 dark:text-emerald-400"
								>{formatNumber(displayVolumeProfile.measuredPreBoilVolumeLiters, 1)}L</span
							>
							<span class="text-xs font-normal text-zinc-400"
								>/ {formatNumber(displayVolumeProfile.targetPreBoilVolumeLiters, 1)}L</span
							>
						{:else}
							<span>{formatNumber(displayVolumeProfile.targetPreBoilVolumeLiters, 1)}L</span>
						{/if}
					</div>
					{#if displayVolumeProfile.measuredPreBoilGravity}
						<span class="mt-0.5 block font-mono text-[10px] text-zinc-500">
							{formatNumber(displayVolumeProfile.measuredPreBoilGravity, 3)} SG
						</span>
					{/if}
				</div>

				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.wizard.post_boil_target')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-zinc-900 dark:text-white">
						{#if displayVolumeProfile.measuredPostBoilVolumeLiters}
							<span class="text-emerald-600 dark:text-emerald-400"
								>{formatNumber(displayVolumeProfile.measuredPostBoilVolumeLiters, 1)}L</span
							>
							<span class="text-xs font-normal text-zinc-400"
								>/ {formatNumber(displayVolumeProfile.targetPostBoilVolumeLiters, 1)}L</span
							>
						{:else}
							<span>{formatNumber(displayVolumeProfile.targetPostBoilVolumeLiters, 1)}L</span>
						{/if}
					</div>
				</div>

				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.wizard.into_fermenter_target')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-zinc-900 dark:text-white">
						{#if displayVolumeProfile.measuredFermenterVolumeLiters}
							<span class="text-emerald-600 dark:text-emerald-400"
								>{formatNumber(displayVolumeProfile.measuredFermenterVolumeLiters, 1)}L</span
							>
							<span class="text-xs font-normal text-zinc-400"
								>/ {formatNumber(displayVolumeProfile.targetFermenterVolumeLiters, 1)}L</span
							>
						{:else}
							<span>{formatNumber(displayVolumeProfile.targetFermenterVolumeLiters, 1)}L</span>
						{/if}
					</div>
				</div>

				<div
					class="rounded-xl border border-zinc-200/60 bg-white/70 p-3 text-center dark:border-white/5 dark:bg-zinc-900/60"
				>
					<span class="block text-[10px] font-semibold text-zinc-500 uppercase dark:text-zinc-400">
						{t('batches.wizard.packaged_target')}
					</span>
					<div class="mt-1 font-mono text-sm font-bold text-zinc-900 dark:text-white">
						{#if displayVolumeProfile.measuredPackagedVolumeLiters}
							<span class="text-emerald-600 dark:text-emerald-400"
								>{formatNumber(displayVolumeProfile.measuredPackagedVolumeLiters, 1)}L</span
							>
							<span class="text-xs font-normal text-zinc-400"
								>/ {formatNumber(displayVolumeProfile.targetPackagedVolumeLiters, 1)}L</span
							>
						{:else}
							<span>{formatNumber(displayVolumeProfile.targetPackagedVolumeLiters, 1)}L</span>
						{/if}
					</div>
				</div>
			</div>

			<!-- Smart Boil Extension Suggestion -->
			{#if stage === 'Boil' && boilExtensionMinutes > 0}
				<div
					role="alert"
					class="mt-3 flex items-center gap-2 rounded-xl border border-amber-300 bg-amber-50/80 p-3 text-xs text-amber-800 dark:border-amber-900/40 dark:bg-amber-950/40 dark:text-amber-200"
				>
					<AlertTriangle class="h-4 w-4 shrink-0 text-amber-600" />
					<span>
						{t('batches.workspace.boil_extension_suggestion', { mins: boilExtensionMinutes })}
					</span>
				</div>
			{/if}
		</div>
	{/if}
</div>
