<script lang="ts">
	import { onMount } from 'svelte';
	import { Droplets, Wand2, AlertTriangle, Trash2 } from '@lucide/svelte';
	import { api } from '$lib/api/client';
	import { auth } from '$lib/stores/auth.svelte';
	import { t } from '$lib/i18n/index.svelte';
	import {
		evaluateWaterPlan,
		suggestWaterSaltAdditions,
		type GristGrainSample
	} from '$lib/calculators/water-chemistry';
	import type { RecipeWaterPlanInputDto, WaterProfileDto } from '$lib/types/api';

	let {
		waterPlan = $bindable<RecipeWaterPlanInputDto | null>(null),
		batchSizeLiters = 20,
		grist = [],
		readonly = false,
		onClear
	}: {
		waterPlan: RecipeWaterPlanInputDto | null;
		batchSizeLiters?: number;
		grist?: GristGrainSample[];
		readonly?: boolean;
		onClear?: () => void;
	} = $props();

	let profiles = $state<WaterProfileDto[]>([]);

	onMount(async () => {
		if (!auth.isAuthenticated) return;
		try {
			profiles = await api.waterProfiles.list();
		} catch {
			// Optional offline tolerance
		}
	});

	function createDefaultPlan(): RecipeWaterPlanInputDto {
		return {
			sourceProfileId: null,
			sourceProfileName: '',
			sourceCalciumPpm: 0,
			sourceMagnesiumPpm: 0,
			sourceSodiumPpm: 0,
			sourceSulfatePpm: 0,
			sourceChloridePpm: 0,
			sourceBicarbonatePpm: 0,
			sourcePh: 7.0,
			targetProfileId: null,
			targetProfileName: '',
			targetCalciumPpm: 75,
			targetMagnesiumPpm: 5,
			targetSodiumPpm: 15,
			targetSulfatePpm: 80,
			targetChloridePpm: 70,
			targetBicarbonatePpm: 40,
			targetMashPh: 5.4,
			mashGypsumGrams: 0,
			mashCalciumChlorideGrams: 0,
			mashEpsomSaltGrams: 0,
			mashTableSaltGrams: 0,
			mashBakingSodaGrams: 0,
			mashChalkGrams: 0,
			mashAcidType: 'Lactic80',
			mashAcidAmountMlOrG: 0,
			spargeGypsumGrams: 0,
			spargeCalciumChlorideGrams: 0,
			spargeEpsomSaltGrams: 0,
			spargeTableSaltGrams: 0,
			spargeBakingSodaGrams: 0,
			spargeChalkGrams: 0,
			spargeAcidType: 'Lactic80',
			spargeAcidAmountMlOrG: 0,
			notes: ''
		};
	}

	const estimatedVolumes = $derived.by(() => {
		const grainKg = grist.reduce((s, g) => s + Math.max(0, g.weightKg), 0);
		const totalWater = Math.max(batchSizeLiters + 5, batchSizeLiters * 1.35 + grainKg * 0.96);
		const mashL =
			grainKg > 0 ? Math.min(totalWater * 0.85, Math.max(4, grainKg * 3)) : totalWater * 0.65;
		const spargeL = Math.max(0, totalWater - mashL);
		return { mashL: Number(mashL.toFixed(1)), spargeL: Number(spargeL.toFixed(1)) };
	});

	const liveEvaluation = $derived.by(() => {
		if (!waterPlan) return null;
		return evaluateWaterPlan(
			{
				calciumPpm: waterPlan.sourceCalciumPpm,
				magnesiumPpm: waterPlan.sourceMagnesiumPpm,
				sodiumPpm: waterPlan.sourceSodiumPpm,
				sulfatePpm: waterPlan.sourceSulfatePpm,
				chloridePpm: waterPlan.sourceChloridePpm,
				bicarbonatePpm: waterPlan.sourceBicarbonatePpm
			},
			{
				gypsumGrams: waterPlan.mashGypsumGrams,
				calciumChlorideGrams: waterPlan.mashCalciumChlorideGrams,
				epsomSaltGrams: waterPlan.mashEpsomSaltGrams,
				tableSaltGrams: waterPlan.mashTableSaltGrams,
				bakingSodaGrams: waterPlan.mashBakingSodaGrams,
				chalkGrams: waterPlan.mashChalkGrams,
				acidType: waterPlan.mashAcidType ?? 'Lactic80',
				acidAmountMlOrG: waterPlan.mashAcidAmountMlOrG
			},
			estimatedVolumes.mashL,
			{
				gypsumGrams: waterPlan.spargeGypsumGrams,
				calciumChlorideGrams: waterPlan.spargeCalciumChlorideGrams,
				epsomSaltGrams: waterPlan.spargeEpsomSaltGrams,
				tableSaltGrams: waterPlan.spargeTableSaltGrams,
				bakingSodaGrams: waterPlan.spargeBakingSodaGrams,
				chalkGrams: waterPlan.spargeChalkGrams,
				acidType: waterPlan.spargeAcidType ?? 'Lactic80',
				acidAmountMlOrG: waterPlan.spargeAcidAmountMlOrG
			},
			estimatedVolumes.spargeL,
			grist
		);
	});

	function applySourceProfile(id: string) {
		if (!waterPlan) return;
		const p = profiles.find((x) => x.id === id);
		if (!p) {
			waterPlan.sourceProfileId = null;
			return;
		}
		waterPlan.sourceProfileId = p.id;
		waterPlan.sourceProfileName = p.name;
		waterPlan.sourceCalciumPpm = p.calciumPpm;
		waterPlan.sourceMagnesiumPpm = p.magnesiumPpm;
		waterPlan.sourceSodiumPpm = p.sodiumPpm;
		waterPlan.sourceSulfatePpm = p.sulfatePpm;
		waterPlan.sourceChloridePpm = p.chloridePpm;
		waterPlan.sourceBicarbonatePpm = p.bicarbonatePpm;
		waterPlan.sourcePh = p.ph ?? 7.0;
	}

	function applyTargetProfile(id: string) {
		if (!waterPlan) return;
		const p = profiles.find((x) => x.id === id);
		if (!p) {
			waterPlan.targetProfileId = null;
			return;
		}
		waterPlan.targetProfileId = p.id;
		waterPlan.targetProfileName = p.name;
		waterPlan.targetCalciumPpm = p.calciumPpm;
		waterPlan.targetMagnesiumPpm = p.magnesiumPpm;
		waterPlan.targetSodiumPpm = p.sodiumPpm;
		waterPlan.targetSulfatePpm = p.sulfatePpm;
		waterPlan.targetChloridePpm = p.chloridePpm;
		waterPlan.targetBicarbonatePpm = p.bicarbonatePpm;
	}

	function autoSuggestSalts() {
		if (!waterPlan) return;
		const res = suggestWaterSaltAdditions(
			{
				calciumPpm: waterPlan.sourceCalciumPpm,
				magnesiumPpm: waterPlan.sourceMagnesiumPpm,
				sodiumPpm: waterPlan.sourceSodiumPpm,
				sulfatePpm: waterPlan.sourceSulfatePpm,
				chloridePpm: waterPlan.sourceChloridePpm,
				bicarbonatePpm: waterPlan.sourceBicarbonatePpm
			},
			{
				calciumPpm: waterPlan.targetCalciumPpm ?? 75,
				magnesiumPpm: waterPlan.targetMagnesiumPpm ?? 5,
				sodiumPpm: waterPlan.targetSodiumPpm ?? 15,
				sulfatePpm: waterPlan.targetSulfatePpm ?? 80,
				chloridePpm: waterPlan.targetChloridePpm ?? 70,
				bicarbonatePpm: waterPlan.targetBicarbonatePpm ?? 40
			},
			estimatedVolumes.mashL,
			estimatedVolumes.spargeL,
			grist,
			waterPlan.targetMashPh || 5.4,
			waterPlan.mashAcidType ?? 'Lactic80'
		);

		waterPlan.mashGypsumGrams = res.mashAdditions.gypsumGrams;
		waterPlan.mashCalciumChlorideGrams = res.mashAdditions.calciumChlorideGrams;
		waterPlan.mashEpsomSaltGrams = res.mashAdditions.epsomSaltGrams;
		waterPlan.mashTableSaltGrams = res.mashAdditions.tableSaltGrams;
		waterPlan.mashBakingSodaGrams = res.mashAdditions.bakingSodaGrams;
		waterPlan.mashChalkGrams = res.mashAdditions.chalkGrams;
		waterPlan.mashAcidAmountMlOrG = res.mashAdditions.acidAmountMlOrG;

		waterPlan.spargeGypsumGrams = res.spargeAdditions.gypsumGrams;
		waterPlan.spargeCalciumChlorideGrams = res.spargeAdditions.calciumChlorideGrams;
		waterPlan.spargeEpsomSaltGrams = res.spargeAdditions.epsomSaltGrams;
		waterPlan.spargeTableSaltGrams = res.spargeAdditions.tableSaltGrams;
		waterPlan.spargeBakingSodaGrams = res.spargeAdditions.bakingSodaGrams;
		waterPlan.spargeChalkGrams = res.spargeAdditions.chalkGrams;
		waterPlan.spargeAcidAmountMlOrG = res.spargeAdditions.acidAmountMlOrG;
	}
</script>

<section
	class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm dark:border-slate-800 dark:bg-slate-900"
>
	<div class="flex flex-wrap items-center justify-between gap-2">
		<div class="flex items-center gap-2.5">
			<div
				class="flex h-8 w-8 items-center justify-center rounded-lg bg-sky-100 text-sky-700 dark:bg-sky-900/40 dark:text-sky-300"
			>
				<Droplets class="h-4 w-4" />
			</div>
			<div>
				<h3 class="text-sm font-bold text-slate-900 dark:text-white">
					{t('water.recipe_plan_title')}
				</h3>
				<p class="text-xs text-slate-500 dark:text-slate-400">
					{t('water.est_liquor', {
						mash: estimatedVolumes.mashL,
						sparge: estimatedVolumes.spargeL
					})}
				</p>
			</div>
		</div>

		{#if !readonly}
			{#if !waterPlan}
				<button
					type="button"
					onclick={() => (waterPlan = createDefaultPlan())}
					class="rounded-lg bg-sky-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-sky-500"
				>
					{t('water.enable_plan')}
				</button>
			{:else}
				<div class="flex items-center gap-2">
					<button
						type="button"
						onclick={autoSuggestSalts}
						class="inline-flex items-center gap-1 rounded-lg bg-indigo-600 px-2.5 py-1.5 text-xs font-semibold text-white hover:bg-indigo-500"
					>
						<Wand2 class="h-3.5 w-3.5" />
						{t('water.auto_suggest')}
					</button>
					<button
						type="button"
						onclick={() => {
							waterPlan = null;
							onClear?.();
						}}
						class="rounded-lg border border-slate-200 p-1.5 text-slate-400 hover:text-rose-600 dark:border-slate-700"
						title={t('water.remove_plan')}
					>
						<Trash2 class="h-3.5 w-3.5" />
					</button>
				</div>
			{/if}
		{/if}
	</div>

	{#if waterPlan && liveEvaluation}
		{#if !readonly}
			<div class="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-3">
				<label class="text-xs font-medium text-slate-600 dark:text-slate-400">
					{t('water.source_profile')}
					<select
						value={waterPlan.sourceProfileId ?? ''}
						onchange={(e) => applySourceProfile(e.currentTarget.value)}
						class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
					>
						<option value="">{t('water.custom_inline')}</option>
						{#each profiles.filter((p) => p.category === 'Source' || p.isPreset) as p (p.id)}
							<option value={p.id}>{p.name}</option>
						{/each}
					</select>
				</label>

				<label class="text-xs font-medium text-slate-600 dark:text-slate-400">
					{t('water.target_profile')}
					<select
						value={waterPlan.targetProfileId ?? ''}
						onchange={(e) => applyTargetProfile(e.currentTarget.value)}
						class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
					>
						<option value="">{t('water.custom_inline')}</option>
						{#each profiles.filter((p) => p.category === 'Target' || p.isPreset) as p (p.id)}
							<option value={p.id}>{p.name}</option>
						{/each}
					</select>
				</label>

				<label class="text-xs font-medium text-slate-600 dark:text-slate-400">
					{t('water.target_mash_ph')}
					<input
						type="number"
						step="0.05"
						min="4.5"
						max="6.2"
						bind:value={waterPlan.targetMashPh}
						class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
					/>
				</label>
			</div>

			<div class="mt-3 grid grid-cols-1 gap-4 md:grid-cols-2">
				<div class="rounded-xl border border-slate-200 p-3 dark:border-slate-800">
					<div class="mb-2 text-xs font-bold text-slate-800 dark:text-slate-200">
						{t('water.mash_salts')}
					</div>
					<div class="grid grid-cols-3 gap-2 text-xs">
						<label
							>Gypsum (g)<input
								type="number"
								step="0.1"
								min="0"
								bind:value={waterPlan.mashGypsumGrams}
								class="mt-0.5 w-full rounded border border-slate-300 px-2 py-1 dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/></label
						>
						<label
							>CaCl₂ (g)<input
								type="number"
								step="0.1"
								min="0"
								bind:value={waterPlan.mashCalciumChlorideGrams}
								class="mt-0.5 w-full rounded border border-slate-300 px-2 py-1 dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/></label
						>
						<label
							>Epsom (g)<input
								type="number"
								step="0.1"
								min="0"
								bind:value={waterPlan.mashEpsomSaltGrams}
								class="mt-0.5 w-full rounded border border-slate-300 px-2 py-1 dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/></label
						>
						<label
							>NaCl (g)<input
								type="number"
								step="0.1"
								min="0"
								bind:value={waterPlan.mashTableSaltGrams}
								class="mt-0.5 w-full rounded border border-slate-300 px-2 py-1 dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/></label
						>
						<label
							>Baking Soda<input
								type="number"
								step="0.1"
								min="0"
								bind:value={waterPlan.mashBakingSodaGrams}
								class="mt-0.5 w-full rounded border border-slate-300 px-2 py-1 dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/></label
						>
						<label
							>{t('water.lactic_acid_ml')}<input
								type="number"
								step="0.1"
								min="0"
								bind:value={waterPlan.mashAcidAmountMlOrG}
								class="mt-0.5 w-full rounded border border-slate-300 px-2 py-1 dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/></label
						>
					</div>
				</div>

				<div class="rounded-xl border border-slate-200 p-3 dark:border-slate-800">
					<div class="mb-2 text-xs font-bold text-slate-800 dark:text-slate-200">
						{t('water.sparge_salts')}
					</div>
					<div class="grid grid-cols-3 gap-2 text-xs">
						<label
							>Gypsum (g)<input
								type="number"
								step="0.1"
								min="0"
								bind:value={waterPlan.spargeGypsumGrams}
								class="mt-0.5 w-full rounded border border-slate-300 px-2 py-1 dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/></label
						>
						<label
							>CaCl₂ (g)<input
								type="number"
								step="0.1"
								min="0"
								bind:value={waterPlan.spargeCalciumChlorideGrams}
								class="mt-0.5 w-full rounded border border-slate-300 px-2 py-1 dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/></label
						>
						<label
							>Epsom (g)<input
								type="number"
								step="0.1"
								min="0"
								bind:value={waterPlan.spargeEpsomSaltGrams}
								class="mt-0.5 w-full rounded border border-slate-300 px-2 py-1 dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/></label
						>
						<label
							>NaCl (g)<input
								type="number"
								step="0.1"
								min="0"
								bind:value={waterPlan.spargeTableSaltGrams}
								class="mt-0.5 w-full rounded border border-slate-300 px-2 py-1 dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/></label
						>
						<label
							>Baking Soda<input
								type="number"
								step="0.1"
								min="0"
								bind:value={waterPlan.spargeBakingSodaGrams}
								class="mt-0.5 w-full rounded border border-slate-300 px-2 py-1 dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/></label
						>
						<label
							>{t('water.lactic_acid_ml')}<input
								type="number"
								step="0.1"
								min="0"
								bind:value={waterPlan.spargeAcidAmountMlOrG}
								class="mt-0.5 w-full rounded border border-slate-300 px-2 py-1 dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/></label
						>
					</div>
				</div>
			</div>
		{/if}

		<div
			class="mt-3 grid grid-cols-2 gap-2.5 rounded-xl bg-slate-50 p-3 text-xs sm:grid-cols-4 dark:bg-slate-800/70"
		>
			<div>
				<div class="text-slate-500">{t('water.predicted_mash_ph')}</div>
				<div
					class="text-base font-bold {liveEvaluation.predictedMashPh >= 5.2 &&
					liveEvaluation.predictedMashPh <= 5.6
						? 'text-emerald-600'
						: 'text-amber-600'}"
				>
					{liveEvaluation.predictedMashPh}
				</div>
			</div>
			<div>
				<div class="text-slate-500">{t('water.so4_cl_ratio')}</div>
				<div class="text-base font-bold text-slate-900 dark:text-white">
					{liveEvaluation.sulfateToChlorideRatio != null
						? `${liveEvaluation.sulfateToChlorideRatio}:1`
						: '—'}
					<span class="ml-1 text-[11px] font-medium text-sky-600 dark:text-sky-400">
						({t(`water.balance_${liveEvaluation.balanceProfile}`)})
					</span>
				</div>
			</div>
			<div>
				<div class="text-slate-500">{t('water.residual_alkalinity')}</div>
				<div class="text-base font-bold text-slate-900 dark:text-white">
					{liveEvaluation.residualAlkalinityPpm} ppm
				</div>
			</div>
			<div>
				<div class="text-slate-500">{t('water.resulting_ions')}</div>
				<div class="font-mono text-[11px] text-slate-700 dark:text-slate-300">
					Ca {liveEvaluation.totalIons.calciumPpm} · Mg {liveEvaluation.totalIons.magnesiumPpm} · SO₄
					{liveEvaluation.totalIons.sulfatePpm} · Cl {liveEvaluation.totalIons.chloridePpm}
				</div>
			</div>
		</div>

		{#if liveEvaluation.hasChalkWarning}
			<div
				class="mt-2 flex items-center gap-2 rounded-lg bg-amber-50 px-3 py-1.5 text-xs text-amber-800 dark:bg-amber-950/30 dark:text-amber-300"
			>
				<AlertTriangle class="h-3.5 w-3.5 shrink-0" />
				<span>{t('water.chalk_warning')}</span>
			</div>
		{/if}
	{/if}
</section>
