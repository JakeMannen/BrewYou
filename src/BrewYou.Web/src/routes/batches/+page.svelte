<script lang="ts">
	import { onMount } from 'svelte';
	import { Activity, Plus, Search, ArrowUpRight, Loader2 } from '@lucide/svelte';
	import { api } from '$lib/api/client';
	import { auth } from '$lib/stores/auth.svelte';
	import { t } from '$lib/i18n/index.svelte';
	import { formatNumber } from '$lib/utils/formatNumber';
	import { settings } from '$lib/stores/settings.svelte';
	import { calculateActualAbv } from '$lib/calculators/brewing';
	import type { BatchSummaryDto, BatchStatus, EquipmentTelemetryUpdateDto } from '$lib/types/api';
	import SrmSwatch from '$lib/components/brewery/SrmSwatch.svelte';
	import BrewStageTracker, {
		type BrewStage as UiBrewStage
	} from '$lib/components/brewery/BrewStageTracker.svelte';

	let statusFilter = $state<'all' | BatchStatus>('all');
	let searchQuery = $state('');
	let batches = $state<BatchSummaryDto[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);

	let isInitialized = false;
	let telemetryAbortController: AbortController | null = null;
	let telemetryReconnectTimeout: ReturnType<typeof setTimeout> | null = null;
	let isUnmounted = false;

	async function runTelemetryLoop(signal: AbortSignal) {
		while (!signal.aborted && !isUnmounted && auth.isAuthenticated) {
			try {
				await api.equipment.streamTelemetry({
					signal,
					onReading: (reading: EquipmentTelemetryUpdateDto) => {
						const bIdx = batches.findIndex(
							(b) =>
								b.id === reading.batchId || (b.fermenterId && b.fermenterId === reading.equipmentId)
						);
						if (bIdx >= 0) {
							const nextGravity = reading.specificGravity ?? batches[bIdx].currentGravity;
							let nextAbv = batches[bIdx].alcoholByVolume;
							const og = batches[bIdx].measuredOg ?? batches[bIdx].targetOg;
							if (nextGravity && og && og > nextGravity) {
								nextAbv = Number(
									calculateActualAbv(og, nextGravity, settings.abvFormula).toFixed(2)
								);
							}
							batches[bIdx] = {
								...batches[bIdx],
								vesselTempC: reading.temperatureC ?? batches[bIdx].vesselTempC,
								currentGravity: nextGravity,
								alcoholByVolume: nextAbv
							};
						}
					},
					onError: () => {
						// Stream disconnected or errored; retry
					}
				});
			} catch {
				// Network error
			}

			if (signal.aborted || isUnmounted || !auth.isAuthenticated) break;

			await new Promise((resolve) => {
				telemetryReconnectTimeout = setTimeout(resolve, 3000);
			});
		}
	}

	function startTelemetryStream() {
		if (!auth.isAuthenticated || isUnmounted) return;
		stopTelemetryStream();

		telemetryAbortController = new AbortController();
		void runTelemetryLoop(telemetryAbortController.signal);
	}

	function stopTelemetryStream() {
		if (telemetryReconnectTimeout) {
			clearTimeout(telemetryReconnectTimeout);
			telemetryReconnectTimeout = null;
		}
		if (telemetryAbortController) {
			telemetryAbortController.abort();
			telemetryAbortController = null;
		}
	}

	async function loadInitialBatches() {
		const isAuth = await auth.ready();
		if (!isAuth) {
			loading = false;
			return;
		}

		if (isInitialized) return;
		isInitialized = true;

		loading = true;
		error = null;
		try {
			batches = await api.batches.list();
		} catch (err: unknown) {
			error = (err as Error).message || t('batches.err_load_failed');
		} finally {
			loading = false;
		}
	}

	onMount(() => {
		void loadInitialBatches();
		startTelemetryStream();

		return () => {
			isUnmounted = true;
			stopTelemetryStream();
		};
	});

	$effect(() => {
		if (auth.isAuthenticated) {
			if (!isInitialized && !auth.isLoading) {
				void loadInitialBatches();
			}
			startTelemetryStream();
		} else {
			stopTelemetryStream();
		}
	});

	let filteredBatches = $derived(
		batches.filter((b) => {
			const matchesStatus = statusFilter === 'all' || b.status === statusFilter;
			const q = searchQuery.toLowerCase().trim();
			const matchesQuery =
				!q ||
				b.name.toLowerCase().includes(q) ||
				b.beerStyle.toLowerCase().includes(q) ||
				b.batchCode.toLowerCase().includes(q);
			return matchesStatus && matchesQuery;
		})
	);

	function getStatusBadge(status: BatchStatus) {
		switch (status) {
			case 'Brewing':
				return {
					class: 'border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400',
					dot: 'bg-amber-500 animate-pulse',
					label: t('batches.status_brewing')
				};
			case 'Fermenting':
				return {
					class: 'border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400',
					dot: 'bg-emerald-500 animate-ping',
					label: t('batches.status_fermenting')
				};
			case 'Conditioning':
				return {
					class: 'border-sky-500/30 bg-sky-500/10 text-sky-600 dark:text-sky-400',
					dot: 'bg-sky-500',
					label: t('batches.status_conditioning')
				};
			case 'Completed':
				return {
					class: 'border-zinc-500/30 bg-zinc-500/10 text-zinc-600 dark:text-zinc-400',
					dot: 'bg-zinc-400',
					label: t('batches.status_completed')
				};
			default:
				return {
					class: 'border-zinc-500/30 bg-zinc-500/10 text-zinc-600 dark:text-zinc-400',
					dot: 'bg-zinc-400',
					label: status
				};
		}
	}

	function getUiStages(
		currentStage: string,
		status: BatchStatus,
		b?: BatchSummaryDto
	): UiBrewStage[] {
		const stages: UiBrewStage[] = [
			{
				id: 'mash',
				label: t('batches.stages.mash'),
				status: 'pending',
				equipmentSubtype: 'AllInOne',
				fillPercent: 75
			},
			{
				id: 'boil',
				label: t('batches.stages.boil'),
				status: 'pending',
				equipmentSubtype: 'Pan',
				fillPercent: 85
			},
			{
				id: 'ferment',
				label: t('batches.stages.ferment'),
				status: 'pending',
				equipmentSubtype: 'ConicalFermenter',
				fillPercent: 80,
				equipmentName: b?.fermenterName ?? undefined
			},
			{
				id: 'condition',
				label: t('batches.stages.condition'),
				status: 'pending',
				equipmentSubtype: 'Carboy',
				fillPercent: 80
			},
			{
				id: 'package',
				label: t('batches.stages.package'),
				status: 'pending',
				equipmentSubtype: 'Bottle',
				fillPercent: 90
			}
		];
		const stageNames = ['Mash', 'Boil', 'Ferment', 'Condition', 'Package'];
		const currentIdx = stageNames.findIndex((s) => s.toLowerCase() === currentStage.toLowerCase());

		return stages.map((stg, idx) => {
			if (status === 'Completed') return { ...stg, status: 'completed' };
			if (idx < currentIdx) return { ...stg, status: 'completed' };
			if (idx === currentIdx) return { ...stg, status: 'in_progress' };
			return stg;
		});
	}
</script>

<svelte:head>
	<title>BrewYou — {t('batches.title')}</title>
</svelte:head>

<div class="space-y-6 sm:space-y-8">
	<!-- Page Header -->
	<div class="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
		<div>
			<div class="flex items-center gap-2.5">
				<div
					class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-500"
				>
					<Activity class="h-5 w-5" />
				</div>
				<h1
					class="font-editorial text-3xl font-bold tracking-tight text-zinc-900 sm:text-4xl dark:text-white"
				>
					{t('batches.title')}
				</h1>
			</div>
			<p class="mt-1 text-sm text-zinc-600 dark:text-zinc-400">
				{t('batches.subtitle')}
			</p>
		</div>

		<a
			href="/batches/new"
			class="flex min-h-[44px] items-center gap-2 self-start rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-4 py-2.5 text-sm font-bold text-zinc-950 shadow-md transition-all hover:from-amber-400 hover:to-amber-500 active:scale-[0.98] sm:self-auto"
		>
			<Plus class="h-4 w-4 stroke-[2.5]" />
			<span>{t('batches.new_batch')}</span>
		</a>
	</div>

	<!-- Filter Tabs & Search Bar -->
	<div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
		<!-- Accessible Status Filter Tabs -->
		<div
			role="tablist"
			aria-label="Batch status filter"
			class="flex gap-1.5 overflow-x-auto rounded-xl border border-zinc-200/80 bg-zinc-100/70 p-1 dark:border-white/5 dark:bg-zinc-900/60"
		>
			{#each ['all', 'brewing', 'fermenting', 'conditioning', 'completed'] as filterKey}
				{@const statusVal =
					filterKey === 'all'
						? 'all'
						: ((filterKey.charAt(0).toUpperCase() + filterKey.slice(1)) as BatchStatus)}
				<button
					type="button"
					role="tab"
					aria-selected={statusFilter === (statusVal as typeof statusFilter)}
					onclick={() => (statusFilter = statusVal as typeof statusFilter)}
					class="flex min-h-[38px] items-center gap-2 rounded-lg px-3 py-1.5 text-xs font-semibold whitespace-nowrap transition-colors {statusFilter ===
					(statusVal as typeof statusFilter)
						? 'bg-white text-zinc-950 shadow-xs dark:bg-zinc-800 dark:text-white'
						: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-200'}"
				>
					<span>{t(`batches.filter_${filterKey}`)}</span>
					<span
						class="rounded-full bg-zinc-200/60 px-1.5 py-0.5 font-mono text-[10px] dark:bg-zinc-700/60"
					>
						{filterKey === 'all'
							? batches.length
							: batches.filter((b) => b.status.toLowerCase() === filterKey).length}
					</span>
				</button>
			{/each}
		</div>

		<!-- Search Bar -->
		<div class="relative w-full sm:w-72">
			<Search
				class="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-zinc-400"
			/>
			<input
				type="text"
				placeholder={t('batches.search_placeholder')}
				bind:value={searchQuery}
				aria-label={t('batches.search_placeholder')}
				class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white pr-3 pl-9 text-sm text-zinc-900 placeholder-zinc-400 transition-colors focus:border-amber-500/60 focus:ring-2 focus:ring-amber-500/20 focus:outline-none dark:border-white/10 dark:bg-zinc-900/80 dark:text-zinc-100 dark:placeholder-zinc-500"
			/>
		</div>
	</div>

	<!-- Batches Grid, Loading, or Empty State -->
	{#if loading}
		<div class="flex flex-col items-center justify-center gap-3 py-16 text-zinc-400">
			<Loader2 class="h-8 w-8 animate-spin text-amber-500" />
			<span class="text-sm font-medium">{t('common.loading')}</span>
		</div>
	{:else if error}
		<div
			class="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
		>
			{error}
		</div>
	{:else if filteredBatches.length === 0}
		<div
			class="glass-panel flex flex-col items-center justify-center rounded-3xl border border-zinc-200/80 py-16 text-center dark:border-white/[0.08]"
		>
			<div
				class="flex h-12 w-12 items-center justify-center rounded-2xl border border-zinc-200 bg-zinc-100 text-zinc-400 dark:border-zinc-800 dark:bg-zinc-900"
			>
				<Activity class="h-6 w-6" />
			</div>
			<h3 class="mt-4 text-base font-bold text-zinc-900 dark:text-white">
				{searchQuery ? t('batches.empty_search') : t('batches.empty_title')}
			</h3>
			<p class="mt-1 max-w-sm text-xs text-zinc-500 dark:text-zinc-400">
				{t('batches.empty_desc')}
			</p>
			{#if !searchQuery}
				<a
					href="/batches/new"
					class="mt-5 flex items-center gap-2 rounded-xl bg-amber-500 px-4 py-2 text-xs font-bold text-zinc-950 shadow-md transition-all hover:bg-amber-400"
				>
					<Plus class="h-4 w-4 stroke-[2.5]" />
					<span>{t('batches.start_first')}</span>
				</a>
			{/if}
		</div>
	{:else}
		<div class="grid grid-cols-1 gap-5 lg:grid-cols-2">
			{#each filteredBatches as batch (batch.id)}
				{@const badge = getStatusBadge(batch.status)}
				{@const stages = getUiStages(batch.currentStage, batch.status, batch)}
				<article
					class="glass-panel group relative flex flex-col justify-between rounded-3xl border border-zinc-200/80 p-5 transition-all duration-200 hover:border-amber-500/40 hover:shadow-lg dark:border-white/[0.08]"
				>
					<!-- Card Header -->
					<div class="space-y-4">
						<div
							class="flex items-start justify-between gap-3 border-b border-zinc-200/60 pb-3 dark:border-white/5"
						>
							<div class="space-y-1">
								<div class="flex items-center gap-2">
									<span class="font-mono text-xs font-bold text-amber-600 dark:text-amber-400">
										{batch.batchCode}
									</span>
									<span class="text-zinc-300 dark:text-zinc-700">•</span>
									<span class="text-xs text-zinc-500">{batch.beerStyle}</span>
								</div>
								<h2 class="text-lg font-bold tracking-tight text-zinc-900 dark:text-white">
									{batch.name}
								</h2>
							</div>

							<div class="flex items-center gap-2">
								<SrmSwatch srm={batch.colorSrm} size="sm" shape="pill" />
								<div
									class="flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[11px] font-bold tracking-wide uppercase {badge.class}"
								>
									<span class="relative flex h-2 w-2">
										{#if batch.status === 'Fermenting'}
											<span
												class="absolute inline-flex h-full w-full rounded-full opacity-75 {badge.dot}"
											></span>
										{/if}
										<span
											class="relative inline-flex h-2 w-2 rounded-full {badge.dot.split(' ')[0]}"
										></span>
									</span>
									<span>{badge.label}</span>
								</div>
							</div>
						</div>

						<!-- Stage Tracker Component -->
						<div
							class="rounded-2xl border border-zinc-200/50 bg-zinc-50/50 px-2 py-1 dark:border-white/5 dark:bg-zinc-950/40"
						>
							<BrewStageTracker
								{stages}
								currentStageId={batch.currentStage.toLowerCase()}
								interactive={false}
							/>
						</div>

						<!-- Vital Metrics HUD Grid -->
						<div class="grid grid-cols-2 gap-2 sm:grid-cols-4 sm:gap-2.5">
							<div
								class="rounded-xl border border-zinc-200/60 bg-zinc-100/50 p-2.5 text-center dark:border-white/5 dark:bg-zinc-900/60"
							>
								<span class="block text-[10px] font-semibold tracking-wider text-zinc-400 uppercase"
									>{t('batches.target_abv')}</span
								>
								<span class="font-mono text-sm font-bold text-amber-600 dark:text-amber-400"
									>{batch.alcoholByVolume
										? `${formatNumber(batch.alcoholByVolume, 2)}%`
										: '—'}</span
								>
							</div>

							<div
								class="rounded-xl border border-zinc-200/60 bg-zinc-100/50 p-2.5 text-center dark:border-white/5 dark:bg-zinc-900/60"
							>
								<span class="block text-[10px] font-semibold tracking-wider text-zinc-400 uppercase"
									>{batch.measuredOg
										? t('batches.measured_og')
										: t('batches.telemetry.target_og')}</span
								>
								<span class="font-mono text-sm font-bold text-zinc-800 dark:text-zinc-200"
									>{formatNumber(batch.measuredOg ?? batch.targetOg, 3)}</span
								>
							</div>

							<div
								class="rounded-xl border border-zinc-200/60 bg-zinc-100/50 p-2.5 text-center dark:border-white/5 dark:bg-zinc-900/60"
							>
								<span class="block text-[10px] font-semibold tracking-wider text-zinc-400 uppercase"
									>{t('batches.current_gravity')}</span
								>
								<span class="font-mono text-sm font-bold text-emerald-600 dark:text-emerald-400">
									{#if batch.currentGravity}
										{formatNumber(batch.currentGravity, 3)}
									{:else if batch.currentStage === 'Mash' || batch.currentStage === 'Boil'}
										—
									{:else}
										{formatNumber(batch.measuredOg ?? batch.targetOg, 3)}
									{/if}
								</span>
							</div>

							<div
								class="rounded-xl border border-zinc-200/60 bg-zinc-100/50 p-2.5 text-center dark:border-white/5 dark:bg-zinc-900/60"
							>
								<span class="block text-[10px] font-semibold tracking-wider text-zinc-400 uppercase"
									>{t('batches.vessel_temp')}</span
								>
								<span class="font-mono text-sm font-bold text-cyan-600 dark:text-cyan-400"
									>{batch.vesselTempC !== null && batch.vesselTempC !== undefined
										? `${batch.vesselTempC}°C`
										: '—'}</span
								>
							</div>
						</div>
					</div>

					<!-- Card Footer: Fermenter Info & Telemetry Link -->
					<div
						class="mt-4 flex items-center justify-between border-t border-zinc-200/60 pt-3 text-xs text-zinc-500 dark:border-white/5 dark:text-zinc-400"
					>
						<div class="flex items-center gap-2 truncate">
							<span class="truncate font-medium text-zinc-700 dark:text-zinc-300"
								>{batch.fermenterName || t('batches.workspace.no_fermenter_assigned')}</span
							>
							<span>•</span>
							<span>{t('batches.days_active', { days: batch.daysActive })}</span>
						</div>

						<a
							href="/batches/{batch.id}"
							class="flex items-center gap-1 font-semibold text-amber-600 transition-colors hover:text-amber-500 dark:text-amber-400"
							aria-label={t('batches.view_workspace_aria', {
								code: batch.batchCode,
								name: batch.name
							})}
						>
							<span>{t('batches.view_batch')}</span>
							<ArrowUpRight class="h-3.5 w-3.5" />
						</a>
					</div>
				</article>
			{/each}
		</div>
	{/if}
</div>
