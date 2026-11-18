<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import {
		settings,
		celsiusToFahrenheit,
		fahrenheitToCelsius,
		volumesToGramsPerLiter
	} from '$lib/stores/settings.svelte';
	import {
		CARBONATION_STYLE_GUIDE,
		DRAFT_TROUBLESHOOTING_GUIDE,
		DRAFT_TUBING_SPECS,
		FORCE_CARB_METHODS,
		FORCE_CARB_TEMPS_C,
		FORCE_CARB_TEMPS_F,
		FORCE_CARB_VOLUMES,
		PRIMING_SUGAR_GUIDE,
		VOLUMES_TO_GRAMS_PER_LITER,
		calculateCo2VolumesFromPsi,
		calculateDraftLineLength,
		calculateKegEquilibriumPsi,
		psiToBar,
		type CarbonationZone,
		type DraftTubingSpec
	} from '$lib/calculators/carbonation-cheat-sheet';
	import { formatNumber } from '$lib/utils/formatNumber';
	import {
		BookOpen,
		Search,
		AlertTriangle,
		Check,
		Info,
		Gauge,
		Sparkles,
		Wrench
	} from '@lucide/svelte';

	type CheatSheetTab = 'styles' | 'matrix' | 'priming' | 'draft';
	type ZoneFilter = 'all' | CarbonationZone;

	interface Props {
		selectedCo2Volumes?: number;
		selectedKegTempC?: number;
		onSelectTargetCo2?: (volumes: number, kegTempC?: number) => void;
		class?: string;
	}

	let {
		selectedCo2Volumes = 2.4,
		selectedKegTempC = 4.0,
		onSelectTargetCo2,
		class: className = ''
	}: Props = $props();

	let activeTab = $state<CheatSheetTab>('styles');
	let styleSearch = $state('');
	let activeZone = $state<ZoneFilter>('all');

	// Quick Pressure -> CO2 inverse checker state
	let checkPsi = $state<number>(12.0);
	let checkTempC = $state<number>(4.0);

	// Draft line balancer state
	let draftKegPsi = $state<number>(12.0);
	let draftTubingId = $state<DraftTubingSpec['id']>('evabarrier_4mm');
	let draftVerticalRiseFeet = $state<number>(2.0);

	let tempUnit = $derived(settings.temperatureUnit);
	let carbUnit = $derived(settings.carbonationUnit);

	let displayCheckTemp = $derived(
		tempUnit === 'Fahrenheit' ? celsiusToFahrenheit(checkTempC) : Number(checkTempC.toFixed(1))
	);
	let resolvedInverseCo2 = $derived(calculateCo2VolumesFromPsi(checkTempC, checkPsi));

	let selectedTubing = $derived(
		DRAFT_TUBING_SPECS.find((s) => s.id === draftTubingId) ?? DRAFT_TUBING_SPECS[1]
	);
	let draftLineResult = $derived(
		calculateDraftLineLength({
			kegPsi: draftKegPsi,
			verticalRiseFeet: draftVerticalRiseFeet,
			residualFaucetPsi: 1.5,
			resistancePsiPerFoot: selectedTubing.resistancePsiPerFoot
		})
	);

	// Matrix rows adapt cleanly to Celsius or Fahrenheit preference
	let matrixRows = $derived(
		tempUnit === 'Fahrenheit'
			? FORCE_CARB_TEMPS_F.map((tempF) => ({
					tempC: ((tempF - 32) * 5) / 9,
					primaryLabel: `${tempF} °F`,
					secondaryLabel: `${formatNumber(((tempF - 32) * 5) / 9, 1)} °C`
				}))
			: FORCE_CARB_TEMPS_C.map((tempC) => ({
					tempC,
					primaryLabel: `${tempC} °C`,
					secondaryLabel: `${formatNumber(celsiusToFahrenheit(tempC), 1)} °F`
				}))
	);

	let filteredStyles = $derived(
		CARBONATION_STYLE_GUIDE.filter((style) => {
			if (activeZone !== 'all' && style.zone !== activeZone) return false;
			const q = styleSearch.toLowerCase().trim();
			if (!q) return true;
			return (
				t(style.nameKey).toLowerCase().includes(q) ||
				t(style.examplesKey).toLowerCase().includes(q) ||
				t(style.notesKey).toLowerCase().includes(q)
			);
		})
	);

	function zoneBadgeClasses(zone: CarbonationZone): string {
		switch (zone) {
			case 'low':
				return 'border-amber-600/30 bg-amber-600/10 text-amber-700 dark:text-amber-300';
			case 'standard':
				return 'border-emerald-500/30 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300';
			case 'high':
				return 'border-sky-500/30 bg-sky-500/10 text-sky-700 dark:text-sky-300';
			case 'effervescent':
				return 'border-rose-500/30 bg-rose-500/10 text-rose-700 dark:text-rose-300';
		}
	}

	function zoneHeaderTint(vol: number): string {
		if (vol < 2.15) return 'text-amber-600 dark:text-amber-400';
		if (vol <= 2.65) return 'text-emerald-600 dark:text-emerald-400';
		if (vol <= 3.05) return 'text-sky-600 dark:text-sky-400';
		return 'text-rose-600 dark:text-rose-400';
	}

	function formatTargetRange(
		minVol: number,
		maxVol: number
	): { primary: string; secondary: string } {
		const minGl = formatNumber(volumesToGramsPerLiter(minVol), 1);
		const maxGl = formatNumber(volumesToGramsPerLiter(maxVol), 1);
		const minV = formatNumber(minVol, 1);
		const maxV = formatNumber(maxVol, 1);
		if (carbUnit === 'GramsPerLiter') {
			return {
				primary: `${minGl}–${maxGl} g/L`,
				secondary: `${minV}–${maxV} vol`
			};
		}
		return {
			primary: `${minV}–${maxV} vol`,
			secondary: `${minGl}–${maxGl} g/L`
		};
	}

	function handleCheckTempInput(e: Event) {
		const raw = Number((e.currentTarget as HTMLInputElement).value);
		if (!Number.isFinite(raw)) return;
		checkTempC = tempUnit === 'Fahrenheit' ? fahrenheitToCelsius(raw) : raw;
	}

	function applyTarget(vol: number, tempC?: number) {
		if (onSelectTargetCo2) {
			onSelectTargetCo2(vol, tempC);
		}
		if (tempC !== undefined) {
			draftKegPsi = calculateKegEquilibriumPsi(tempC, vol);
		}
	}
</script>

<div
	id="carbonation-cheat-sheet"
	class="glass-panel relative flex flex-col overflow-hidden rounded-2xl border border-zinc-200/80 p-5 shadow-sm transition-all duration-200 hover:border-amber-500/30 hover:shadow-md dark:border-white/[0.08] {className}"
>
	<!-- Header -->
	<div
		class="flex flex-col justify-between gap-4 border-b border-zinc-200/70 pb-4 sm:flex-row sm:items-center dark:border-white/[0.08]"
	>
		<div class="flex items-center gap-3">
			<div
				class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
			>
				<BookOpen class="h-5 w-5" />
			</div>
			<div>
				<h3 class="font-editorial text-base font-bold text-zinc-900 dark:text-white">
					{t('calculations.carbonation_cheat_sheet.title')}
				</h3>
				<p class="text-xs text-zinc-500 dark:text-zinc-400">
					{t('calculations.carbonation_cheat_sheet.description')}
				</p>
			</div>
		</div>
	</div>

	<!-- Sub-navigation Tabs -->
	<div
		role="tablist"
		aria-label={t('calculations.carbonation_cheat_sheet.title')}
		class="mt-4 flex flex-wrap gap-1.5 rounded-xl border border-zinc-200/80 bg-zinc-100/60 p-1 dark:border-white/5 dark:bg-zinc-900/60"
	>
		<button
			type="button"
			role="tab"
			aria-selected={activeTab === 'styles'}
			onclick={() => (activeTab = 'styles')}
			class="flex-1 rounded-lg px-3 py-1.5 text-xs font-semibold whitespace-nowrap transition-all {activeTab ===
			'styles'
				? 'bg-white text-amber-600 shadow-sm dark:bg-zinc-800 dark:text-amber-400'
				: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-white'}"
		>
			{t('calculations.carbonation_cheat_sheet.tabs.styles')}
		</button>
		<button
			type="button"
			role="tab"
			aria-selected={activeTab === 'matrix'}
			onclick={() => (activeTab = 'matrix')}
			class="flex-1 rounded-lg px-3 py-1.5 text-xs font-semibold whitespace-nowrap transition-all {activeTab ===
			'matrix'
				? 'bg-white text-amber-600 shadow-sm dark:bg-zinc-800 dark:text-amber-400'
				: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-white'}"
		>
			{t('calculations.carbonation_cheat_sheet.tabs.matrix')}
		</button>
		<button
			type="button"
			role="tab"
			aria-selected={activeTab === 'priming'}
			onclick={() => (activeTab = 'priming')}
			class="flex-1 rounded-lg px-3 py-1.5 text-xs font-semibold whitespace-nowrap transition-all {activeTab ===
			'priming'
				? 'bg-white text-amber-600 shadow-sm dark:bg-zinc-800 dark:text-amber-400'
				: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-white'}"
		>
			{t('calculations.carbonation_cheat_sheet.tabs.priming')}
		</button>
		<button
			type="button"
			role="tab"
			aria-selected={activeTab === 'draft'}
			onclick={() => (activeTab = 'draft')}
			class="flex-1 rounded-lg px-3 py-1.5 text-xs font-semibold whitespace-nowrap transition-all {activeTab ===
			'draft'
				? 'bg-white text-amber-600 shadow-sm dark:bg-zinc-800 dark:text-amber-400'
				: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-white'}"
		>
			{t('calculations.carbonation_cheat_sheet.tabs.draft')}
		</button>
	</div>

	<!-- TAB 1: BY BEER STYLE (BJCP 2021) -->
	{#if activeTab === 'styles'}
		<div class="mt-4 space-y-4">
			<!-- Filter & Zone Toolbar -->
			<div class="flex flex-col justify-between gap-3 md:flex-row md:items-center">
				<div class="flex flex-wrap gap-1.5">
					{#each ['all', 'low', 'standard', 'high', 'effervescent'] as z (z)}
						<button
							type="button"
							onclick={() => (activeZone = z as ZoneFilter)}
							class="rounded-xl px-3 py-1 text-[11px] font-semibold transition-all {activeZone === z
								? 'border border-amber-500/40 bg-amber-500/15 text-amber-700 dark:text-amber-300'
								: 'border border-zinc-200/80 bg-white/60 text-zinc-600 hover:bg-zinc-100 dark:border-white/10 dark:bg-zinc-900/40 dark:text-zinc-400 dark:hover:bg-zinc-800'}"
						>
							{t(`calculations.carbonation_cheat_sheet.zones.${z}`)}
						</button>
					{/each}
				</div>

				<div class="relative w-full md:w-64">
					<Search class="absolute top-1/2 left-3 h-3.5 w-3.5 -translate-y-1/2 text-zinc-400" />
					<input
						type="text"
						bind:value={styleSearch}
						placeholder={t('calculations.carbonation_cheat_sheet.styles_filter_placeholder')}
						class="w-full rounded-xl border border-zinc-200/80 bg-white/80 py-1.5 pr-3 pl-8 text-xs text-zinc-900 placeholder-zinc-400 focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white dark:placeholder-zinc-500"
					/>
				</div>
			</div>

			<!-- BJCP Note -->
			<div
				class="flex items-start gap-2 rounded-xl border border-zinc-200/70 bg-zinc-50/70 p-3 text-xs text-zinc-600 dark:border-white/5 dark:bg-zinc-900/40 dark:text-zinc-400"
			>
				<Info class="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />
				<span>{t('calculations.carbonation_cheat_sheet.styles_bjcp_note')}</span>
			</div>

			<!-- Styles Table -->
			{#if filteredStyles.length === 0}
				<div
					class="rounded-xl border border-dashed border-zinc-200 p-8 text-center text-xs text-zinc-500 dark:border-white/10 dark:text-zinc-400"
				>
					{t('calculations.carbonation_cheat_sheet.no_matching_styles')}
				</div>
			{:else}
				<div class="overflow-x-auto rounded-xl border border-zinc-200/80 dark:border-white/[0.08]">
					<table class="w-full text-left text-xs">
						<thead
							class="border-b border-zinc-200/80 bg-zinc-50/90 text-[10px] font-bold tracking-wider text-zinc-500 uppercase dark:border-white/[0.08] dark:bg-zinc-900/70 dark:text-zinc-400"
						>
							<tr>
								<th class="px-4 py-2.5">{t('calculations.carbonation_cheat_sheet.col_style')}</th>
								<th class="px-4 py-2.5 whitespace-nowrap">
									{t('calculations.carbonation_cheat_sheet.col_target_co2')}
								</th>
								<th class="hidden px-4 py-2.5 md:table-cell">
									{t('calculations.carbonation_cheat_sheet.col_notes')}
								</th>
								<th class="px-4 py-2.5 text-right"></th>
							</tr>
						</thead>
						<tbody class="divide-y divide-zinc-200/60 dark:divide-white/[0.06]">
							{#each filteredStyles as style (style.id)}
								{@const range = formatTargetRange(style.minVol, style.maxVol)}
								{@const isActive = Math.abs(selectedCo2Volumes - style.defaultVol) < 0.05}
								<tr
									class="transition-colors {isActive
										? 'bg-amber-500/10'
										: 'hover:bg-zinc-50/80 dark:hover:bg-zinc-900/40'}"
								>
									<td class="px-4 py-3 align-top">
										<div class="flex flex-wrap items-center gap-1.5">
											<span class="font-bold text-zinc-900 dark:text-white">
												{t(style.nameKey)}
											</span>
											<span
												class="rounded-full border px-2 py-0.5 text-[10px] font-semibold {zoneBadgeClasses(
													style.zone
												)}"
											>
												{t(`calculations.carbonation_cheat_sheet.zones.${style.zone}`)}
											</span>
											{#if style.isNitro}
												<span
													class="rounded-full border border-zinc-300 bg-zinc-200/70 px-2 py-0.5 text-[10px] font-semibold text-zinc-700 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
												>
													{t('calculations.carbonation_cheat_sheet.nitro_badge')}
												</span>
											{/if}
											{#if style.requiresHeavyGlass}
												<span
													class="rounded-full border border-rose-500/30 bg-rose-500/10 px-2 py-0.5 text-[10px] font-semibold text-rose-700 dark:text-rose-300"
												>
													{t('calculations.carbonation_cheat_sheet.heavy_glass_badge')}
												</span>
											{/if}
										</div>
										<p class="mt-0.5 text-[11px] text-zinc-500 dark:text-zinc-400">
											{t(style.examplesKey)}
										</p>
										<p class="mt-1 text-[11px] text-zinc-600 md:hidden dark:text-zinc-300">
											{t(style.notesKey)}
										</p>
									</td>
									<td class="px-4 py-3 align-top whitespace-nowrap">
										<span class="block font-mono text-xs font-bold text-zinc-900 dark:text-white">
											{range.primary}
										</span>
										<span class="block font-mono text-[10px] text-zinc-500 dark:text-zinc-400">
											{range.secondary}
										</span>
									</td>
									<td
										class="hidden px-4 py-3 align-top text-zinc-600 md:table-cell dark:text-zinc-300"
									>
										{t(style.notesKey)}
									</td>
									<td class="px-4 py-3 text-right align-top whitespace-nowrap">
										{#if onSelectTargetCo2}
											<button
												type="button"
												onclick={() => applyTarget(style.defaultVol)}
												class="inline-flex items-center gap-1 rounded-lg border px-2.5 py-1 text-[11px] font-semibold transition-all {isActive
													? 'border-amber-500 bg-amber-500 text-zinc-950'
													: 'border-amber-500/30 bg-amber-500/10 text-amber-700 hover:bg-amber-500/20 dark:text-amber-300'}"
											>
												{#if isActive}
													<Check class="h-3 w-3" />
													<span>{t('calculations.carbonation_cheat_sheet.applied_badge')}</span>
												{:else}
													<span>
														{t('calculations.carbonation_cheat_sheet.apply_target', {
															value: settings.formatCarbonation(style.defaultVol, 1)
														})}
													</span>
												{/if}
											</button>
										{/if}
									</td>
								</tr>
							{/each}
						</tbody>
					</table>
				</div>
			{/if}
		</div>

		<!-- TAB 2: FORCE CARBONATION MATRIX (PSI / bar) -->
	{:else if activeTab === 'matrix'}
		<div class="mt-4 space-y-5">
			<div class="flex flex-col justify-between gap-2 sm:flex-row sm:items-center">
				<p class="text-xs text-zinc-600 dark:text-zinc-400">
					{t('calculations.carbonation_cheat_sheet.matrix.subtitle')}
				</p>
				<div class="flex flex-wrap gap-1.5 text-[10px] font-semibold">
					<span class="rounded-full border px-2 py-0.5 {zoneBadgeClasses('low')}">
						{t('calculations.carbonation_cheat_sheet.zones.low')}
					</span>
					<span class="rounded-full border px-2 py-0.5 {zoneBadgeClasses('standard')}">
						{t('calculations.carbonation_cheat_sheet.zones.standard')}
					</span>
					<span class="rounded-full border px-2 py-0.5 {zoneBadgeClasses('high')}">
						{t('calculations.carbonation_cheat_sheet.zones.high')}
					</span>
					<span class="rounded-full border px-2 py-0.5 {zoneBadgeClasses('effervescent')}">
						{t('calculations.carbonation_cheat_sheet.zones.effervescent')}
					</span>
				</div>
			</div>

			<!-- Force Carb Equilibrium Table -->
			<div class="overflow-x-auto rounded-xl border border-zinc-200/80 dark:border-white/[0.08]">
				<table class="w-full text-center text-xs">
					<thead
						class="border-b border-zinc-200/80 bg-zinc-50/90 dark:border-white/[0.08] dark:bg-zinc-900/70"
					>
						<tr>
							<th
								class="px-3 py-2.5 text-left text-[10px] font-bold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
							>
								{t('calculations.carbonation_cheat_sheet.matrix.temp_header')}
							</th>
							{#each FORCE_CARB_VOLUMES as vol (vol)}
								<th class="px-2 py-2 whitespace-nowrap">
									<span class="block font-mono text-xs font-extrabold {zoneHeaderTint(vol)}">
										{carbUnit === 'GramsPerLiter'
											? `${formatNumber(vol * VOLUMES_TO_GRAMS_PER_LITER, 1)}`
											: formatNumber(vol, 1)}
									</span>
									<span class="block font-mono text-[10px] font-normal text-zinc-400">
										{carbUnit === 'GramsPerLiter'
											? `g/L (${formatNumber(vol, 1)}v)`
											: `vol (${formatNumber(vol * VOLUMES_TO_GRAMS_PER_LITER, 1)} g/L)`}
									</span>
								</th>
							{/each}
						</tr>
					</thead>
					<tbody class="divide-y divide-zinc-200/60 dark:divide-white/[0.06]">
						{#each matrixRows as row (row.primaryLabel)}
							<tr class="hover:bg-zinc-50/60 dark:hover:bg-zinc-900/30">
								<td class="px-3 py-2 text-left whitespace-nowrap">
									<span class="block font-mono text-xs font-bold text-zinc-900 dark:text-white">
										{row.primaryLabel}
									</span>
									<span class="block font-mono text-[10px] text-zinc-400">
										{row.secondaryLabel}
									</span>
								</td>
								{#each FORCE_CARB_VOLUMES as vol (vol)}
									{@const psi = calculateKegEquilibriumPsi(row.tempC, vol)}
									{@const bar = psiToBar(psi)}
									{@const isMatch =
										Math.abs(selectedCo2Volumes - vol) < 0.08 &&
										Math.abs(selectedKegTempC - row.tempC) < 0.8}
									<td class="p-1">
										<button
											type="button"
											onclick={() => applyTarget(vol, Number(row.tempC.toFixed(1)))}
											class="w-full rounded-lg border px-1.5 py-1.5 transition-all {isMatch
												? 'border-amber-500 bg-amber-500/20 ring-1 ring-amber-500'
												: 'border-transparent hover:border-amber-500/30 hover:bg-amber-500/10'}"
										>
											<span class="block font-mono text-xs font-bold text-zinc-900 dark:text-white">
												{formatNumber(psi, 1)}
											</span>
											<span class="block font-mono text-[9px] text-zinc-500 dark:text-zinc-400">
												{formatNumber(bar, 2)} bar
											</span>
										</button>
									</td>
								{/each}
							</tr>
						{/each}
					</tbody>
				</table>
			</div>

			<!-- Inverse Checker + Altitude Note -->
			<div class="grid grid-cols-1 gap-4 lg:grid-cols-12">
				<div
					class="rounded-xl border border-amber-500/20 bg-amber-500/5 p-4 lg:col-span-7 dark:border-amber-400/20 dark:bg-amber-400/5"
				>
					<div class="flex items-center gap-2">
						<Gauge class="h-4 w-4 text-amber-600 dark:text-amber-400" />
						<h4 class="text-xs font-bold text-zinc-900 dark:text-white">
							{t('calculations.carbonation_cheat_sheet.matrix.inverse_title')}
						</h4>
					</div>
					<div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-3 sm:items-end">
						<div>
							<label
								for="cheat-inv-psi"
								class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
							>
								{t('calculations.carbonation_cheat_sheet.matrix.inverse_psi')}
							</label>
							<input
								id="cheat-inv-psi"
								type="number"
								step="0.5"
								min="0"
								max="60"
								bind:value={checkPsi}
								class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 font-mono text-xs font-semibold text-zinc-900 focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
							/>
						</div>
						<div>
							<label
								for="cheat-inv-temp"
								class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
							>
								{t('calculations.carbonation_cheat_sheet.matrix.inverse_temp')} ({settings.tempShort})
							</label>
							<input
								id="cheat-inv-temp"
								type="number"
								step="0.5"
								value={displayCheckTemp}
								oninput={handleCheckTempInput}
								class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 font-mono text-xs font-semibold text-zinc-900 focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
							/>
						</div>
						<div
							class="rounded-xl border border-amber-500/30 bg-white/70 px-3 py-2 text-center dark:bg-zinc-900/60"
						>
							<span class="block text-[10px] font-semibold text-zinc-500 dark:text-zinc-400">
								{t('calculations.carbonation_cheat_sheet.matrix.inverse_result')}
							</span>
							<span class="font-mono text-sm font-extrabold text-amber-600 dark:text-amber-400">
								{settings.formatCarbonation(resolvedInverseCo2, 2)}
							</span>
						</div>
					</div>
				</div>

				<div
					class="flex items-start gap-2.5 rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-4 text-xs text-zinc-600 lg:col-span-5 dark:border-white/5 dark:bg-zinc-900/40 dark:text-zinc-300"
				>
					<Info class="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />
					<div class="space-y-1.5">
						<p>{t('calculations.carbonation_cheat_sheet.matrix.altitude_note')}</p>
						<p class="text-[11px] text-zinc-500 dark:text-zinc-400">
							{t('calculations.carbonation_cheat_sheet.matrix.selected_cell_hint')}
						</p>
					</div>
				</div>
			</div>

			<!-- Force Carbonation & Spunding Methods -->
			<div>
				<h4
					class="mb-2.5 text-xs font-bold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
				>
					{t('calculations.carbonation_cheat_sheet.methods_title')}
				</h4>
				<div class="grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">
					{#each FORCE_CARB_METHODS as method (method.id)}
						<div
							class="flex flex-col justify-between rounded-xl border border-zinc-200/80 bg-white/60 p-3.5 dark:border-white/[0.08] dark:bg-zinc-900/40"
						>
							<div>
								<div class="flex items-center justify-between gap-2">
									<span class="text-xs font-bold text-zinc-900 dark:text-white">
										{t(method.titleKey)}
									</span>
									<span
										class="rounded-full border border-amber-500/30 bg-amber-500/10 px-2 py-0.5 font-mono text-[10px] font-semibold text-amber-700 dark:text-amber-300"
									>
										{t(method.timingKey)}
									</span>
								</div>
								<p class="mt-1.5 text-[11px] leading-relaxed text-zinc-600 dark:text-zinc-300">
									{t(method.summaryKey)}
								</p>
							</div>
							{#if method.cautionKey}
								<p
									class="mt-2 flex items-start gap-1.5 rounded-lg border border-amber-500/30 bg-amber-500/10 p-2 text-[10px] text-amber-800 dark:text-amber-200"
								>
									<AlertTriangle class="mt-0.5 h-3 w-3 shrink-0 text-amber-500" />
									<span>{t(method.cautionKey)}</span>
								</p>
							{/if}
						</div>
					{/each}
				</div>
			</div>
		</div>

		<!-- TAB 3: PRIMING & BOTTLE SAFETY -->
	{:else if activeTab === 'priming'}
		<div class="mt-4 space-y-4">
			<!-- Crucial Temp Rule Callout -->
			<div
				class="flex items-start gap-3 rounded-xl border border-amber-500/40 bg-amber-500/10 p-4 text-xs text-amber-900 dark:text-amber-100"
			>
				<AlertTriangle class="mt-0.5 h-5 w-5 shrink-0 text-amber-600 dark:text-amber-400" />
				<div>
					<h4 class="font-bold">
						{t('calculations.carbonation_cheat_sheet.priming.temp_rule_title')}
					</h4>
					<p class="mt-1 leading-relaxed text-amber-800 dark:text-amber-200">
						{t('calculations.carbonation_cheat_sheet.priming.temp_rule_body')}
					</p>
				</div>
			</div>

			<!-- Priming Sugar Multipliers Table -->
			<div class="overflow-x-auto rounded-xl border border-zinc-200/80 dark:border-white/[0.08]">
				<table class="w-full text-left text-xs">
					<thead
						class="border-b border-zinc-200/80 bg-zinc-50/90 text-[10px] font-bold tracking-wider text-zinc-500 uppercase dark:border-white/[0.08] dark:bg-zinc-900/70 dark:text-zinc-400"
					>
						<tr>
							<th class="px-4 py-2.5">
								{t('calculations.carbonation_cheat_sheet.priming.col_agent')}
							</th>
							<th class="px-4 py-2.5 text-center">
								{t('calculations.carbonation_cheat_sheet.priming.col_yield')}
							</th>
							<th class="px-4 py-2.5 text-center">
								{t('calculations.carbonation_cheat_sheet.priming.col_multiplier')}
							</th>
							<th class="px-4 py-2.5 text-center">
								{t('calculations.carbonation_cheat_sheet.priming.col_rule_of_thumb')}
							</th>
							<th class="px-4 py-2.5">
								{t('calculations.carbonation_cheat_sheet.priming.col_notes')}
							</th>
						</tr>
					</thead>
					<tbody class="divide-y divide-zinc-200/60 dark:divide-white/[0.06]">
						{#each PRIMING_SUGAR_GUIDE as agent (agent.id)}
							<tr class="hover:bg-zinc-50/80 dark:hover:bg-zinc-900/40">
								<td class="px-4 py-2.5 font-bold text-zinc-900 dark:text-white">
									{t(agent.nameKey)}
								</td>
								<td class="px-4 py-2.5 text-center font-mono text-zinc-700 dark:text-zinc-300">
									{formatNumber(agent.yieldFactor, 2)}
								</td>
								<td
									class="px-4 py-2.5 text-center font-mono font-bold text-amber-600 dark:text-amber-400"
								>
									{formatNumber(agent.multiplierVsSucrose, 2)}×
								</td>
								<td
									class="px-4 py-2.5 text-center font-mono font-semibold text-zinc-900 dark:text-white"
								>
									~{formatNumber(agent.gramsPerLiterPerVol, 1)} g/L
								</td>
								<td class="px-4 py-2.5 text-zinc-600 dark:text-zinc-300">
									{t(agent.notesKey)}
								</td>
							</tr>
						{/each}
					</tbody>
				</table>
			</div>

			<!-- Conditioning & Safety Grid -->
			<div class="grid grid-cols-1 gap-4 md:grid-cols-2">
				<div
					class="rounded-xl border border-zinc-200/80 bg-white/60 p-4 dark:border-white/[0.08] dark:bg-zinc-900/40"
				>
					<div class="flex items-center gap-2">
						<Sparkles class="h-4 w-4 text-emerald-500" />
						<h4 class="text-xs font-bold text-zinc-900 dark:text-white">
							{t('calculations.carbonation_cheat_sheet.priming.conditioning_title')}
						</h4>
					</div>
					<p class="mt-2 text-xs leading-relaxed text-zinc-600 dark:text-zinc-300">
						{t('calculations.carbonation_cheat_sheet.priming.conditioning_body')}
					</p>
				</div>

				<div
					class="rounded-xl border border-rose-500/30 bg-rose-500/5 p-4 dark:border-rose-400/20 dark:bg-rose-400/5"
				>
					<div class="flex items-center gap-2">
						<AlertTriangle class="h-4 w-4 text-rose-600 dark:text-rose-400" />
						<h4 class="text-xs font-bold text-zinc-900 dark:text-white">
							{t('calculations.carbonation_cheat_sheet.priming.safety_title')}
						</h4>
					</div>
					<ul class="mt-2 space-y-1.5 text-xs text-zinc-700 dark:text-zinc-300">
						<li>• {t('calculations.carbonation_cheat_sheet.priming.safety_standard')}</li>
						<li class="font-semibold text-rose-700 dark:text-rose-300">
							• {t('calculations.carbonation_cheat_sheet.priming.safety_heavy')}
						</li>
					</ul>
				</div>
			</div>
		</div>

		<!-- TAB 4: DRAFT LINES & TROUBLESHOOTING -->
	{:else}
		<div class="mt-4 space-y-5">
			<!-- Interactive Draft Line Balancer -->
			<div
				class="rounded-xl border border-amber-500/20 bg-amber-500/5 p-4 dark:border-amber-400/20 dark:bg-amber-400/5"
			>
				<div class="flex items-center gap-2">
					<Wrench class="h-4 w-4 text-amber-600 dark:text-amber-400" />
					<h4 class="text-xs font-bold text-zinc-900 dark:text-white">
						{t('calculations.carbonation_cheat_sheet.draft.balancer_title')}
					</h4>
				</div>

				<div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-4">
					<div>
						<label
							for="draft-keg-psi"
							class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
						>
							{t('calculations.carbonation_cheat_sheet.draft.keg_psi')}
						</label>
						<input
							id="draft-keg-psi"
							type="number"
							step="0.5"
							min="0"
							max="50"
							bind:value={draftKegPsi}
							class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 font-mono text-xs font-semibold text-zinc-900 focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
						/>
					</div>

					<div>
						<label
							for="draft-tubing-type"
							class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
						>
							{t('calculations.carbonation_cheat_sheet.draft.tubing_type')}
						</label>
						<select
							id="draft-tubing-type"
							bind:value={draftTubingId}
							class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 text-xs font-semibold text-zinc-900 focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
						>
							{#each DRAFT_TUBING_SPECS as spec (spec.id)}
								<option value={spec.id}>{t(spec.nameKey)}</option>
							{/each}
						</select>
					</div>

					<div>
						<label
							for="draft-vertical-rise"
							class="block text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
						>
							{t('calculations.carbonation_cheat_sheet.draft.vertical_rise')}
						</label>
						<input
							id="draft-vertical-rise"
							type="number"
							step="0.5"
							min="-10"
							max="30"
							bind:value={draftVerticalRiseFeet}
							class="mt-1 w-full rounded-xl border border-zinc-200/80 bg-white/80 px-3 py-1.5 font-mono text-xs font-semibold text-zinc-900 focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:border-white/10 dark:bg-zinc-800/80 dark:text-white"
						/>
					</div>

					<div
						class="flex flex-col justify-center rounded-xl border border-amber-500/30 bg-white/80 px-3 py-2 text-center dark:bg-zinc-900/70"
					>
						<span class="text-[10px] font-semibold text-zinc-500 dark:text-zinc-400">
							{t('calculations.carbonation_cheat_sheet.draft.recommended_length')}
						</span>
						<span class="font-mono text-sm font-extrabold text-amber-600 dark:text-amber-400">
							{formatNumber(draftLineResult.lengthMeters, 2)} m
							<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400">
								({formatNumber(draftLineResult.lengthFeet, 1)} ft)
							</span>
						</span>
					</div>
				</div>

				<p class="mt-2.5 text-[11px] text-zinc-600 dark:text-zinc-400">
					{t('calculations.carbonation_cheat_sheet.draft.head_rule')}
					<span class="font-mono font-semibold text-zinc-800 dark:text-zinc-200">
						({t('calculations.carbonation_cheat_sheet.draft.effective_pressure')}:
						{formatNumber(draftLineResult.effectivePressurePsi, 1)} PSI)
					</span>
				</p>
			</div>

			<!-- Tubing Resistance Reference Table -->
			<div class="overflow-x-auto rounded-xl border border-zinc-200/80 dark:border-white/[0.08]">
				<table class="w-full text-left text-xs">
					<thead
						class="border-b border-zinc-200/80 bg-zinc-50/90 text-[10px] font-bold tracking-wider text-zinc-500 uppercase dark:border-white/[0.08] dark:bg-zinc-900/70 dark:text-zinc-400"
					>
						<tr>
							<th class="px-4 py-2.5">
								{t('calculations.carbonation_cheat_sheet.draft.col_tubing')}
							</th>
							<th class="px-4 py-2.5">
								{t('calculations.carbonation_cheat_sheet.draft.col_resistance')}
							</th>
							<th class="px-4 py-2.5">
								{t('calculations.carbonation_cheat_sheet.draft.col_typical')}
							</th>
						</tr>
					</thead>
					<tbody class="divide-y divide-zinc-200/60 dark:divide-white/[0.06]">
						{#each DRAFT_TUBING_SPECS as spec (spec.id)}
							<tr class="hover:bg-zinc-50/80 dark:hover:bg-zinc-900/40">
								<td class="px-4 py-2.5 font-bold text-zinc-900 dark:text-white">
									{t(spec.nameKey)}
								</td>
								<td class="px-4 py-2.5 font-mono text-zinc-700 dark:text-zinc-300">
									{formatNumber(spec.resistancePsiPerFoot, 1)} PSI/ft
									<span class="text-zinc-400">
										({formatNumber(spec.resistanceBarPerMeter, 2)} bar/m)
									</span>
								</td>
								<td class="px-4 py-2.5 font-mono text-amber-700 dark:text-amber-300">
									{t(spec.typicalLengthKey)}
								</td>
							</tr>
						{/each}
					</tbody>
				</table>
			</div>

			<!-- Draft Diagnostic Cheat Sheet -->
			<div>
				<h4
					class="mb-2.5 text-xs font-bold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
				>
					{t('calculations.carbonation_cheat_sheet.draft.troubleshooting_title')}
				</h4>
				<div class="overflow-x-auto rounded-xl border border-zinc-200/80 dark:border-white/[0.08]">
					<table class="w-full text-left text-xs">
						<thead
							class="border-b border-zinc-200/80 bg-zinc-50/90 text-[10px] font-bold tracking-wider text-zinc-500 uppercase dark:border-white/[0.08] dark:bg-zinc-900/70 dark:text-zinc-400"
						>
							<tr>
								<th class="px-4 py-2.5">
									{t('calculations.carbonation_cheat_sheet.draft.col_symptom')}
								</th>
								<th class="px-4 py-2.5">
									{t('calculations.carbonation_cheat_sheet.draft.col_cause')}
								</th>
								<th class="px-4 py-2.5">
									{t('calculations.carbonation_cheat_sheet.draft.col_fix')}
								</th>
							</tr>
						</thead>
						<tbody class="divide-y divide-zinc-200/60 dark:divide-white/[0.06]">
							{#each DRAFT_TROUBLESHOOTING_GUIDE as issue (issue.id)}
								<tr class="hover:bg-zinc-50/80 dark:hover:bg-zinc-900/40">
									<td class="px-4 py-2.5 font-semibold text-zinc-900 dark:text-white">
										{t(issue.symptomKey)}
									</td>
									<td class="px-4 py-2.5 text-zinc-600 dark:text-zinc-300">
										{t(issue.causeKey)}
									</td>
									<td class="px-4 py-2.5 text-emerald-700 dark:text-emerald-300">
										{t(issue.fixKey)}
									</td>
								</tr>
							{/each}
						</tbody>
					</table>
				</div>
			</div>
		</div>
	{/if}
</div>
