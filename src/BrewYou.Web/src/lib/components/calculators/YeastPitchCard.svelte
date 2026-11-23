<script lang="ts">
	import { onMount } from 'svelte';
	import { FlaskConical, Sparkles, AlertTriangle, CheckCircle2 } from '@lucide/svelte';
	import { api } from '$lib/api/client';
	import { auth } from '$lib/stores/auth.svelte';
	import { settings, formatVolume } from '$lib/stores/settings.svelte';
	import { t } from '$lib/i18n/index.svelte';
	import {
		calculateYeastPitch,
		type YeastStyleCategory,
		type YeastFormFactor,
		type YeastStarterMethod
	} from '$lib/calculators/yeast';
	import type { YeastBankEntryDto } from '$lib/types/api';

	let {
		class: className = '',
		initialVolumeLiters = 20,
		initialGravity = 1.052
	}: {
		class?: string;
		initialVolumeLiters?: number;
		initialGravity?: number;
	} = $props();

	let batchVolumeLiters = $state(20);
	let originalGravity = $state(1.052);
	let styleCategory = $state<YeastStyleCategory>('Ale');
	let customRate = $state(0.75);
	let yeastForm = $state<YeastFormFactor>('Liquid');
	let packageAgeDays = $state(14);
	let baseViabilityPercent = $state(98);
	let packsAvailable = $state(1);
	let slurryDensityBillionPerMl = $state(1.0);
	let slurryVolumeAvailableMl = $state(250);
	let starterMethod = $state<YeastStarterMethod>('StirPlate');
	let customStarterVolumeLiters = $state<number | null>(null);

	let yeastBankEntries = $state<YeastBankEntryDto[]>([]);
	let selectedBankId = $state('');

	$effect(() => {
		batchVolumeLiters = initialVolumeLiters;
		originalGravity = initialGravity;
	});

	onMount(async () => {
		if (!auth.isAuthenticated) return;
		try {
			yeastBankEntries = await api.yeastBank.list('Available');
		} catch {
			// Ignore optional bank load failure when offline/unauthenticated
		}
	});

	function selectBankEntry(id: string) {
		selectedBankId = id;
		const found = yeastBankEntries.find((e) => e.id === id);
		if (!found) return;
		yeastForm = found.form as YeastFormFactor;
		packageAgeDays = found.ageDays;
		baseViabilityPercent = found.baseViabilityPercent;
		slurryDensityBillionPerMl = found.estimatedDensityBillionPerMl;
		slurryVolumeAvailableMl = found.volumeMl;
		starterMethod = found.form === 'Slurry' ? 'None' : 'StirPlate';
	}

	const result = $derived(
		calculateYeastPitch({
			batchVolumeLiters: Math.max(0.5, Number(batchVolumeLiters) || 20),
			originalGravity: Math.min(1.2, Math.max(1.005, Number(originalGravity) || 1.05)),
			styleCategory,
			customPitchRateMillionPerMlPlato: styleCategory === 'Custom' ? customRate : null,
			yeastForm,
			packageAgeDays: Math.max(0, Number(packageAgeDays) || 0),
			baseViabilityPercent: Math.min(100, Math.max(1, Number(baseViabilityPercent) || 98)),
			packsAvailable: Math.max(1, Number(packsAvailable) || 1),
			slurryDensityBillionPerMl: Math.max(0.1, Number(slurryDensityBillionPerMl) || 1.0),
			slurryVolumeAvailableMl: Math.max(0, Number(slurryVolumeAvailableMl) || 0),
			starterMethod,
			starterVolumeLiters:
				customStarterVolumeLiters && customStarterVolumeLiters > 0
					? customStarterVolumeLiters
					: null
		})
	);
</script>

<section
	class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm dark:border-slate-800 dark:bg-slate-900 {className}"
>
	<div class="mb-4 flex items-center justify-between gap-3">
		<div class="flex items-center gap-2.5">
			<div
				class="flex h-9 w-9 items-center justify-center rounded-xl bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300"
			>
				<FlaskConical class="h-5 w-5" />
			</div>
			<div>
				<h3 class="text-base font-bold text-slate-900 dark:text-white">
					{t('calculations.yeast_pitch.title')}
				</h3>
				<p class="text-xs text-slate-500 dark:text-slate-400">
					{t('calculations.yeast_pitch.description')}
				</p>
			</div>
		</div>
	</div>

	{#if yeastBankEntries.length > 0}
		<div
			class="mb-4 rounded-xl border border-amber-200/80 bg-amber-50/70 p-3 dark:border-amber-800/60 dark:bg-amber-950/20"
		>
			<label
				for="calc-yeast-bank"
				class="mb-1 block text-xs font-semibold text-amber-900 dark:text-amber-200"
			>
				{t('yeast.load_from_bank')}
			</label>
			<select
				id="calc-yeast-bank"
				value={selectedBankId}
				onchange={(e) => selectBankEntry(e.currentTarget.value)}
				class="w-full rounded-lg border border-amber-300 bg-white px-3 py-1.5 text-sm text-slate-900 dark:border-amber-700 dark:bg-slate-900 dark:text-slate-100"
			>
				<option value="">{t('yeast.manual_entry')}</option>
				{#each yeastBankEntries as entry (entry.id)}
					<option value={entry.id}>
						{entry.strainName} (Gen {entry.generation}, {entry.volumeMl} mL, {entry.currentEstimatedViabilityPercent}%)
					</option>
				{/each}
			</select>
		</div>
	{/if}

	<div class="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
		<div>
			<label for="yp-vol" class="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">
				{t('yeast.batch_volume_l')}
			</label>
			<input
				id="yp-vol"
				type="number"
				step="0.5"
				min="1"
				bind:value={batchVolumeLiters}
				class="w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
			/>
		</div>

		<div>
			<label for="yp-og" class="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">
				{t('yeast.original_gravity')} ({result.plato} °P)
			</label>
			<input
				id="yp-og"
				type="number"
				step="0.001"
				min="1.01"
				max="1.18"
				bind:value={originalGravity}
				class="w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
			/>
		</div>

		<div>
			<label
				for="yp-style"
				class="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400"
			>
				{t('yeast.style_target')}
			</label>
			<select
				id="yp-style"
				bind:value={styleCategory}
				class="w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
			>
				<option value="Ale">{t('yeast.style_ale')}</option>
				<option value="Hybrid">{t('yeast.style_hybrid')}</option>
				<option value="Lager">{t('yeast.style_lager')}</option>
				<option value="Custom">{t('yeast.style_custom')}</option>
			</select>
		</div>

		{#if styleCategory === 'Custom'}
			<div>
				<label
					for="yp-custom-rate"
					class="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400"
				>
					{t('yeast.custom_pitch_rate')}
				</label>
				<input
					id="yp-custom-rate"
					type="number"
					step="0.05"
					min="0.2"
					max="3.5"
					bind:value={customRate}
					class="w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
				/>
			</div>
		{/if}

		<div>
			<label
				for="yp-form"
				class="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400"
			>
				{t('yeast.form')}
			</label>
			<select
				id="yp-form"
				bind:value={yeastForm}
				class="w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
			>
				<option value="Liquid">{t('yeast.form_liquid')}</option>
				<option value="Dry">{t('yeast.form_dry')}</option>
				<option value="Slurry">{t('yeast.form_slurry')}</option>
			</select>
		</div>

		<div>
			<label for="yp-age" class="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">
				{t('yeast.package_age_days')}
			</label>
			<input
				id="yp-age"
				type="number"
				step="1"
				min="0"
				bind:value={packageAgeDays}
				class="w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
			/>
		</div>

		{#if yeastForm === 'Slurry'}
			<div>
				<label
					for="yp-density"
					class="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400"
				>
					{t('yeast.slurry_density')}
				</label>
				<input
					id="yp-density"
					type="number"
					step="0.1"
					min="0.2"
					max="5"
					bind:value={slurryDensityBillionPerMl}
					class="w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
				/>
			</div>
			<div>
				<label
					for="yp-slurry-vol"
					class="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400"
				>
					{t('yeast.slurry_available_ml')}
				</label>
				<input
					id="yp-slurry-vol"
					type="number"
					step="10"
					min="0"
					bind:value={slurryVolumeAvailableMl}
					class="w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
				/>
			</div>
		{:else}
			<div>
				<label
					for="yp-packs"
					class="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400"
				>
					{t('yeast.packs_available')}
				</label>
				<input
					id="yp-packs"
					type="number"
					step="1"
					min="1"
					bind:value={packsAvailable}
					class="w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
				/>
			</div>
		{/if}

		<div>
			<label
				for="yp-starter"
				class="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400"
			>
				{t('yeast.starter_method')}
			</label>
			<select
				id="yp-starter"
				bind:value={starterMethod}
				class="w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
			>
				<option value="None">{t('yeast.starter_none')}</option>
				<option value="Simple">{t('yeast.starter_simple')}</option>
				<option value="Shaken">{t('yeast.starter_shaken')}</option>
				<option value="StirPlate">{t('yeast.starter_stirplate')}</option>
			</select>
		</div>

		{#if starterMethod !== 'None'}
			<div>
				<label
					for="yp-starter-vol"
					class="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400"
				>
					{t('yeast.starter_volume_override')}
				</label>
				<input
					id="yp-starter-vol"
					type="number"
					step="0.25"
					min="0"
					max="10"
					placeholder="Auto"
					bind:value={customStarterVolumeLiters}
					class="w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
				/>
			</div>
		{/if}
	</div>

	<div
		class="mt-4 grid grid-cols-2 gap-3 rounded-xl bg-slate-50 p-3.5 sm:grid-cols-4 dark:bg-slate-800/70"
	>
		<div>
			<div class="text-xs text-slate-500 dark:text-slate-400">{t('yeast.target_cells')}</div>
			<div class="text-lg font-bold text-slate-900 dark:text-white">
				{result.requiredCellsBillions} B
			</div>
			<div class="text-[11px] text-slate-500">
				{result.targetPitchRateMillionPerMlPlato} M/mL/°P
			</div>
		</div>

		<div>
			<div class="text-xs text-slate-500 dark:text-slate-400">{t('yeast.viability')}</div>
			<div class="text-lg font-bold text-amber-600 dark:text-amber-400">
				{result.estimatedViabilityPercent}%
			</div>
			<div class="text-[11px] text-slate-500">
				{t('yeast.initial_viable', { cells: result.initialViableCellsBillions })}
			</div>
		</div>

		<div>
			<div class="text-xs text-slate-500 dark:text-slate-400">
				{yeastForm === 'Dry' ? t('yeast.dry_packs_needed') : t('yeast.slurry_needed')}
			</div>
			<div class="text-lg font-bold text-slate-900 dark:text-white">
				{#if yeastForm === 'Dry'}
					{result.dryPacksRecommended} ({result.dryPacksExact})
				{:else}
					{result.requiredSlurryMl} mL
				{/if}
			</div>
			<div class="text-[11px] text-slate-500">
				{t('yeast.batch_size_short', { vol: formatVolume(batchVolumeLiters, settings.volumeUnit) })}
			</div>
		</div>

		<div>
			<div class="text-xs text-slate-500 dark:text-slate-400">{t('yeast.starter_plan')}</div>
			{#if starterMethod === 'None' || result.recommendedStarterVolumeLiters <= 0}
				<div class="text-sm font-semibold text-emerald-600 dark:text-emerald-400">
					{result.deficitCellsBillions <= 0
						? t('yeast.direct_pitch_ok')
						: t('yeast.add_packs_needed')}
				</div>
			{:else}
				<div class="text-lg font-bold text-indigo-600 dark:text-indigo-400">
					{result.recommendedStarterVolumeLiters} L
				</div>
				<div class="text-[11px] text-slate-500">
					{result.starterDmeGrams} g DME → {result.projectedStarterCellsBillions} B
				</div>
			{/if}
		</div>
	</div>

	<div
		class="mt-3 flex items-center gap-2 rounded-xl px-3 py-2 text-xs font-medium {result.needsMultiStepStarter
			? 'bg-amber-50 text-amber-800 dark:bg-amber-950/30 dark:text-amber-200'
			: result.deficitCellsBillions <= 0 ||
				  result.projectedStarterCellsBillions >= result.requiredCellsBillions
				? 'bg-emerald-50 text-emerald-800 dark:bg-emerald-950/30 dark:text-emerald-200'
				: 'bg-sky-50 text-sky-800 dark:bg-sky-950/30 dark:text-sky-200'}"
	>
		{#if result.needsMultiStepStarter}
			<AlertTriangle class="h-4 w-4 shrink-0" />
			<span>{t('yeast.step_up_warning')}</span>
		{:else if result.deficitCellsBillions <= 0 || result.projectedStarterCellsBillions >= result.requiredCellsBillions}
			<CheckCircle2 class="h-4 w-4 shrink-0" />
			<span>{t(`yeast.summary_${result.recommendationSummaryKey}`)}</span>
		{:else}
			<Sparkles class="h-4 w-4 shrink-0" />
			<span>{t(`yeast.summary_${result.recommendationSummaryKey}`)}</span>
		{/if}
	</div>
</section>
