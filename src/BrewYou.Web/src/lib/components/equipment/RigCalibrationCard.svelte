<script lang="ts">
	import { Activity, Check, SlidersHorizontal, RefreshCw } from '@lucide/svelte';
	import { api } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import type { EquipmentDto, RigCalibrationReportDto } from '$lib/types/api';

	let {
		setupId,
		equipmentList = [],
		isOwner = true,
		onApplied
	}: {
		setupId: string;
		equipmentList?: EquipmentDto[];
		isOwner?: boolean;
		onApplied?: () => Promise<void> | void;
	} = $props();

	let sampleSize = $state(5);
	let selectedBoilerId = $state<string>('');
	let selectedFermenterId = $state<string>('');
	let loading = $state(false);
	let applying = $state(false);
	let error = $state<string | null>(null);
	let successMessage = $state<string | null>(null);
	let report = $state<RigCalibrationReportDto | null>(null);

	let applyEfficiency = $state(true);
	let applyBoilOffRate = $state(true);
	let applyKettleTrubLoss = $state(true);
	let applyFermenterLoss = $state(true);
	let applyGrainAbsorption = $state(true);
	let applyToPrimaryEquipment = $state(true);
	let applyToSetup = $state(true);

	let boilers = $derived(equipmentList.filter((e) => e.type === 'Boiler'));
	let fermenters = $derived(equipmentList.filter((e) => e.type === 'Fermenter'));
	let selectedBoiler = $derived(boilers.find((b) => b.id === selectedBoilerId) || null);
	let selectedFermenter = $derived(fermenters.find((f) => f.id === selectedFermenterId) || null);

	async function loadReport() {
		if (!setupId) return;
		loading = true;
		error = null;
		try {
			report = await api.brewerySetups.getCalibration(
				setupId,
				sampleSize,
				selectedBoilerId || null,
				selectedFermenterId || null
			);
		} catch (err) {
			error = err instanceof Error ? err.message : t('common.error');
		} finally {
			loading = false;
		}
	}

	$effect(() => {
		if (
			selectedBoilerId &&
			equipmentList.length > 0 &&
			!boilers.some((b) => b.id === selectedBoilerId)
		) {
			selectedBoilerId = '';
		}
		if (
			selectedFermenterId &&
			equipmentList.length > 0 &&
			!fermenters.some((f) => f.id === selectedFermenterId)
		) {
			selectedFermenterId = '';
		}
	});

	$effect(() => {
		if (setupId) {
			void sampleSize;
			void selectedBoilerId;
			void selectedFermenterId;
			void loadReport();
		}
	});

	async function applyCalibration() {
		if (!setupId) return;
		applying = true;
		error = null;
		successMessage = null;
		try {
			report = await api.brewerySetups.applyCalibration(setupId, {
				sampleSize,
				applyBoilOffRate,
				applyKettleTrubLoss,
				applyFermenterLoss,
				applyGrainAbsorption,
				applyToPrimaryEquipment,
				targetBoilerId: selectedBoilerId || null,
				targetFermenterId: selectedFermenterId || null,
				applyEfficiency,
				applyToSetup
			});
			successMessage = t('calibration.applied_success');
			await onApplied?.();
		} catch (err) {
			error = err instanceof Error ? err.message : t('common.error');
		} finally {
			applying = false;
		}
	}

	function formatDelta(val?: number | null, suffix = ''): string {
		if (val == null) return '—';
		const sign = val > 0 ? '+' : '';
		return `${sign}${val}${suffix}`;
	}
</script>

<section
	class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm dark:border-slate-800 dark:bg-slate-900"
>
	<div class="mb-4 flex flex-wrap items-center justify-between gap-3">
		<div class="flex items-center gap-2.5">
			<div
				class="flex h-9 w-9 items-center justify-center rounded-xl bg-indigo-100 text-indigo-700 dark:bg-indigo-900/40 dark:text-indigo-300"
			>
				<Activity class="h-5 w-5" />
			</div>
			<div>
				<h3 class="text-base font-bold text-slate-900 dark:text-white">
					{t('calibration.title')}
				</h3>
				<p class="text-xs text-slate-500 dark:text-slate-400">
					{t('calibration.subtitle')}
				</p>
			</div>
		</div>

		<div class="flex flex-wrap items-center gap-2">
			{#if boilers.length > 0}
				<select
					bind:value={selectedBoilerId}
					aria-label={t('calibration.all_boilers')}
					class="rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs font-medium dark:border-slate-700 dark:bg-slate-800 dark:text-white"
				>
					<option value="">{t('calibration.all_boilers')}</option>
					{#each boilers as boiler (boiler.id)}
						<option value={boiler.id}>{boiler.name}</option>
					{/each}
				</select>
			{/if}
			{#if fermenters.length > 0}
				<select
					bind:value={selectedFermenterId}
					aria-label={t('calibration.all_fermenters')}
					class="rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs font-medium dark:border-slate-700 dark:bg-slate-800 dark:text-white"
				>
					<option value="">{t('calibration.all_fermenters')}</option>
					{#each fermenters as fermenter (fermenter.id)}
						<option value={fermenter.id}>{fermenter.name}</option>
					{/each}
				</select>
			{/if}
			<select
				bind:value={sampleSize}
				class="rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs font-medium dark:border-slate-700 dark:bg-slate-800 dark:text-white"
			>
				<option value={3}>{t('calibration.last_n_batches', { count: 3 })}</option>
				<option value={5}>{t('calibration.last_n_batches', { count: 5 })}</option>
				<option value={10}>{t('calibration.last_n_batches', { count: 10 })}</option>
			</select>
			<button
				type="button"
				onclick={loadReport}
				disabled={loading}
				class="rounded-lg border border-slate-200 p-1.5 text-slate-600 hover:bg-slate-50 dark:border-slate-700 dark:text-slate-300"
				title={t('common.refresh')}
			>
				<RefreshCw class="h-4 w-4 {loading ? 'animate-spin' : ''}" />
			</button>
		</div>
	</div>

	{#if error}
		<div
			class="mb-3 rounded-xl bg-rose-50 px-3 py-2 text-xs text-rose-700 dark:bg-rose-950/40 dark:text-rose-300"
		>
			{error}
		</div>
	{/if}
	{#if successMessage}
		<div
			class="mb-3 rounded-xl bg-emerald-50 px-3 py-2 text-xs text-emerald-700 dark:bg-emerald-950/40 dark:text-emerald-300"
		>
			{successMessage}
		</div>
	{/if}

	{#if report && report.analyzedBatchCount === 0}
		<div
			class="rounded-xl border border-dashed border-slate-300 p-6 text-center text-xs text-slate-500 dark:border-slate-700 dark:text-slate-400"
		>
			{t('calibration.no_batches')}
		</div>
	{:else if report}
		<div class="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-5">
			<label
				class="flex cursor-pointer flex-col justify-between rounded-xl bg-slate-50 p-3 dark:bg-slate-800/70"
			>
				<div class="flex items-center justify-between">
					<span class="text-xs text-slate-500 dark:text-slate-400">
						{t('calibration.learned_efficiency')}
					</span>
					<input type="checkbox" bind:checked={applyEfficiency} class="rounded text-indigo-600" />
				</div>
				<div class="mt-1 text-lg font-bold text-indigo-600 dark:text-indigo-400">
					{report.suggestedBrewhouseEfficiencyPercent != null
						? `${report.suggestedBrewhouseEfficiencyPercent}%`
						: '—'}
				</div>
				<div class="text-[11px] text-slate-500">
					{t('calibration.current')}: {report.currentDefaultEfficiencyPercent}% · {t(
						'calibration.mash_eff_short'
					)}: {report.suggestedMashEfficiencyPercent != null
						? `${report.suggestedMashEfficiencyPercent}%`
						: '—'}
				</div>
			</label>

			<label
				class="flex cursor-pointer flex-col justify-between rounded-xl bg-slate-50 p-3 dark:bg-slate-800/70"
			>
				<div class="flex items-center justify-between">
					<span class="text-xs text-slate-500">{t('calibration.boil_off')}</span>
					<input type="checkbox" bind:checked={applyBoilOffRate} class="rounded text-indigo-600" />
				</div>
				<div class="mt-1 text-base font-bold text-slate-900 dark:text-white">
					{report.suggestedBoilOffRatePerHour != null
						? `${report.suggestedBoilOffRatePerHour} L/hr`
						: '—'}
				</div>
				<div class="text-[11px] text-slate-500">
					{t('calibration.current')}: {report.currentDefaultBoilOffRatePerHour} L/hr
				</div>
			</label>

			<label
				class="flex cursor-pointer flex-col justify-between rounded-xl bg-slate-50 p-3 dark:bg-slate-800/70"
			>
				<div class="flex items-center justify-between">
					<span class="text-xs text-slate-500">{t('calibration.kettle_trub')}</span>
					<input
						type="checkbox"
						bind:checked={applyKettleTrubLoss}
						class="rounded text-indigo-600"
					/>
				</div>
				<div class="mt-1 text-base font-bold text-slate-900 dark:text-white">
					{report.suggestedKettleTrubLossLiters != null
						? `${report.suggestedKettleTrubLossLiters} L`
						: '—'}
				</div>
				<div class="text-[11px] text-slate-500">
					{t('calibration.current')}: {report.currentDefaultKettleTrubLossLiters} L
				</div>
			</label>

			<label
				class="flex cursor-pointer flex-col justify-between rounded-xl bg-slate-50 p-3 dark:bg-slate-800/70"
			>
				<div class="flex items-center justify-between">
					<span class="text-xs text-slate-500">{t('calibration.fermenter_loss')}</span>
					<input
						type="checkbox"
						bind:checked={applyFermenterLoss}
						class="rounded text-indigo-600"
					/>
				</div>
				<div class="mt-1 text-base font-bold text-slate-900 dark:text-white">
					{report.suggestedFermenterLossLiters != null
						? `${report.suggestedFermenterLossLiters} L`
						: '—'}
				</div>
				<div class="text-[11px] text-slate-500">
					{t('calibration.current')}: {report.currentDefaultFermenterLossLiters} L
				</div>
			</label>

			<label
				class="flex cursor-pointer flex-col justify-between rounded-xl bg-slate-50 p-3 dark:bg-slate-800/70"
			>
				<div class="flex items-center justify-between">
					<span class="text-xs text-slate-500">{t('calibration.grain_absorption')}</span>
					<input
						type="checkbox"
						bind:checked={applyGrainAbsorption}
						class="rounded text-indigo-600"
					/>
				</div>
				<div class="mt-1 text-base font-bold text-slate-900 dark:text-white">
					{report.suggestedGrainAbsorptionRate != null
						? `${report.suggestedGrainAbsorptionRate} L/kg`
						: '—'}
				</div>
				<div class="text-[11px] text-slate-500">
					{t('calibration.current')}: {report.currentDefaultGrainAbsorptionRate} L/kg
				</div>
			</label>
		</div>

		{#if isOwner && report.hasSuggestions}
			<div
				class="mt-3 flex flex-wrap items-center justify-between gap-2 rounded-xl border border-indigo-200/70 bg-indigo-50/60 px-3 py-2.5 dark:border-indigo-900/50 dark:bg-indigo-950/20"
			>
				<div class="flex flex-wrap items-center gap-4">
					<label class="flex items-center gap-2 text-xs text-slate-700 dark:text-slate-300">
						<input type="checkbox" bind:checked={applyToSetup} class="rounded text-indigo-600" />
						{t('calibration.also_update_setup')}
					</label>
					{#if selectedBoiler || selectedFermenter}
						<span
							class="inline-flex items-center rounded-md border border-indigo-300 bg-white px-2 py-0.5 text-[11px] font-medium text-indigo-700 dark:border-indigo-800 dark:bg-slate-900 dark:text-indigo-300"
						>
							{t('calibration.targeting_selected_vessels', {
								vessels: [selectedBoiler?.name, selectedFermenter?.name].filter(Boolean).join(' + ')
							})}
						</span>
					{:else}
						<label class="flex items-center gap-2 text-xs text-slate-700 dark:text-slate-300">
							<input
								type="checkbox"
								bind:checked={applyToPrimaryEquipment}
								class="rounded text-indigo-600"
							/>
							{t('calibration.also_update_equipment')}
						</label>
					{/if}
				</div>
				<button
					type="button"
					onclick={applyCalibration}
					disabled={applying}
					class="inline-flex items-center gap-1.5 rounded-lg bg-indigo-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-indigo-500 disabled:opacity-50"
				>
					<Check class="h-3.5 w-3.5" />
					{t('calibration.apply_suggestions')}
				</button>
			</div>
		{/if}

		<div class="mt-4">
			<h4
				class="mb-2 flex items-center gap-1.5 text-xs font-bold tracking-wider text-slate-500 uppercase dark:text-slate-400"
			>
				<SlidersHorizontal class="h-3.5 w-3.5" />
				{t('calibration.variance_report')} ({report.analyzedBatchCount})
			</h4>
			<div class="overflow-x-auto">
				<table class="w-full text-left text-xs">
					<thead
						class="border-b border-slate-200 text-slate-500 dark:border-slate-800 dark:text-slate-400"
					>
						<tr>
							<th class="py-2 pr-3">{t('common.batch')}</th>
							<th class="px-2 py-2">OG ({t('calibration.target_vs_actual')})</th>
							<th class="px-2 py-2">FG ({t('calibration.target_vs_actual')})</th>
							<th class="px-2 py-2">{t('calibration.efficiency')}</th>
							<th class="px-2 py-2">{t('calibration.fermenter_vol')}</th>
							<th class="px-2 py-2">{t('calibration.boil_off')}</th>
						</tr>
					</thead>
					<tbody class="divide-y divide-slate-100 dark:divide-slate-800/80">
						{#each report.batches as item (item.batchId)}
							<tr>
								<td class="py-2 pr-3">
									<a
										href={`/batches/${item.batchId}`}
										class="font-medium text-indigo-600 hover:underline dark:text-indigo-400"
									>
										{item.batchCode} · {item.name}
									</a>
									{#if item.boilerName || item.fermenterName}
										<div class="text-[11px] text-slate-500 dark:text-slate-400">
											{[item.boilerName, item.fermenterName].filter(Boolean).join(' → ')}
										</div>
									{/if}
								</td>
								<td class="px-2 py-2 tabular-nums">
									{item.targetOg.toFixed(3)} / {item.measuredOg ? item.measuredOg.toFixed(3) : '—'}
									{#if item.ogVariancePoints != null}
										<span class="ml-1 text-[11px] text-slate-500"
											>({formatDelta(item.ogVariancePoints, ' pts')})</span
										>
									{/if}
								</td>
								<td class="px-2 py-2 tabular-nums">
									{item.targetFg.toFixed(3)} / {item.measuredFg ? item.measuredFg.toFixed(3) : '—'}
									{#if item.fgVariancePoints != null}
										<span class="ml-1 text-[11px] text-slate-500"
											>({formatDelta(item.fgVariancePoints, ' pts')})</span
										>
									{/if}
								</td>
								<td class="px-2 py-2 tabular-nums">
									{item.targetEfficiencyPercent}% / {item.actualBrewhouseEfficiencyPercent != null
										? `${item.actualBrewhouseEfficiencyPercent}%`
										: '—'}
								</td>
								<td class="px-2 py-2 tabular-nums">
									{item.targetFermenterVolumeLiters ?? '—'} / {item.measuredFermenterVolumeLiters ??
										'—'} L
									{#if item.volumeVarianceLiters != null}
										<span class="ml-1 text-[11px] text-slate-500"
											>({formatDelta(item.volumeVarianceLiters, ' L')})</span
										>
									{/if}
								</td>
								<td class="px-2 py-2 tabular-nums">
									{item.observedBoilOffRatePerHour != null
										? `${item.observedBoilOffRatePerHour} L/hr`
										: '—'}
								</td>
							</tr>
						{/each}
					</tbody>
				</table>
			</div>
		</div>
	{/if}
</section>
