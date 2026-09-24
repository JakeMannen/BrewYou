<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { t } from '$lib/i18n/index.svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { brewery } from '$lib/stores/brewery.svelte';
	import { dashboard } from '$lib/stores/dashboard.svelte';
	import type { BatchSummaryDto } from '$lib/types/api';
	import DashboardHeader from '$lib/components/dashboard/DashboardHeader.svelte';
	import ActiveCellarCards from '$lib/components/dashboard/ActiveCellarCards.svelte';
	import IdleCellarState from '$lib/components/dashboard/IdleCellarState.svelte';
	import BrewTasksWidget from '$lib/components/dashboard/BrewTasksWidget.svelte';
	import EquipmentHealthWidget from '$lib/components/dashboard/EquipmentHealthWidget.svelte';
	import LiveReadingFeedWidget from '$lib/components/dashboard/LiveReadingFeedWidget.svelte';
	import QuickReadingModal from '$lib/components/dashboard/QuickReadingModal.svelte';
	import AdvanceStageModal from '$lib/components/dashboard/AdvanceStageModal.svelte';
	import { Loader2 } from '@lucide/svelte';

	let readingModalOpen = $state(false);
	let selectedBatchIdForReading = $state<string | null>(null);

	let advanceModalOpen = $state(false);
	let batchToAdvance = $state<BatchSummaryDto | null>(null);

	onMount(() => {
		dashboard.init();
	});

	onDestroy(() => {
		dashboard.destroy();
	});

	// Re-load when user or active brewery setup changes
	$effect(() => {
		if (auth.isAuthenticated) {
			void brewery.activeSetupId;
			dashboard.loadDashboard();
		}
	});

	function handleOpenReadingModal(batchId?: string) {
		selectedBatchIdForReading = batchId ?? null;
		readingModalOpen = true;
	}

	function handleCloseReadingModal() {
		readingModalOpen = false;
		selectedBatchIdForReading = null;
	}

	function handleOpenAdvanceModal(batch: BatchSummaryDto) {
		batchToAdvance = batch;
		advanceModalOpen = true;
	}

	function handleCloseAdvanceModal() {
		advanceModalOpen = false;
		batchToAdvance = null;
	}
</script>

<svelte:head>
	<title>BrewYou — {t('dashboard.title')}</title>
</svelte:head>

<div class="space-y-6 sm:space-y-8">
	<!-- Top Command Header with KPIs and Quick Actions -->
	<DashboardHeader onOpenReadingModal={() => handleOpenReadingModal()} />

	{#if dashboard.loading && dashboard.batches.length === 0}
		<div class="flex items-center justify-center py-20">
			<div class="flex items-center gap-3 text-zinc-600 dark:text-zinc-400">
				<Loader2 class="h-6 w-6 animate-spin text-amber-500" />
				<span class="text-sm font-medium">{t('common.loading')}</span>
			</div>
		</div>
	{:else}
		<!-- 2x2 Command Grid: Row 1 Cellar + Equipment | Row 2 Tasks + Live Reading Feed -->
		<div class="grid grid-cols-1 items-stretch gap-6 lg:grid-cols-12">
			<!-- Row 1 Left: Active Fermentations / Cellar (8 cols) -->
			<div class="order-1 flex flex-col lg:order-1 lg:col-span-8">
				{#if dashboard.activeBatchCount > 0}
					<ActiveCellarCards
						batches={dashboard.activeBatches}
						equipment={dashboard.equipment}
						onLogReading={(id) => handleOpenReadingModal(id)}
						onAdvanceStage={handleOpenAdvanceModal}
					/>
				{:else}
					<IdleCellarState equipment={dashboard.equipment} />
				{/if}
			</div>

			<!-- Row 1 Right: Equipment Readiness & Probes (4 cols) -->
			<div class="order-3 flex flex-col lg:order-2 lg:col-span-4">
				<EquipmentHealthWidget />
			</div>

			<!-- Row 2 Left: Today's Tasks & Operations Schedule (8 cols) -->
			<div class="order-2 flex flex-col lg:order-3 lg:col-span-8">
				<BrewTasksWidget />
			</div>

			<!-- Row 2 Right: Live Telemetry Stream & Quick Calculators (4 cols) -->
			<div class="order-4 flex flex-col lg:order-4 lg:col-span-4">
				<LiveReadingFeedWidget />
			</div>
		</div>
	{/if}
</div>

<!-- In-Dashboard Quick Modals -->
<QuickReadingModal
	bind:isOpen={readingModalOpen}
	batches={dashboard.activeBatches}
	initialBatchId={selectedBatchIdForReading}
	onClose={handleCloseReadingModal}
/>

<AdvanceStageModal
	bind:isOpen={advanceModalOpen}
	batch={batchToAdvance}
	onClose={handleCloseAdvanceModal}
/>
