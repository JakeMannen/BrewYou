<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { dashboard } from '$lib/stores/dashboard.svelte';
	import { brewery } from '$lib/stores/brewery.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import {
		Beer,
		PlusCircle,
		BookOpen,
		RefreshCw,
		Activity,
		Gauge,
		CheckSquare,
		Layers
	} from '@lucide/svelte';

	interface Props {
		onOpenReadingModal: () => void;
	}

	let { onOpenReadingModal }: Props = $props();

	function handleRefresh() {
		dashboard.refreshData(false);
	}
</script>

<header class="glass-panel relative overflow-hidden rounded-3xl p-6 sm:p-8">
	<!-- Background ambient glow -->
	<div
		class="pointer-events-none absolute -top-16 -right-16 h-64 w-64 rounded-full bg-amber-500/10 blur-3xl dark:bg-amber-500/15"
		aria-hidden="true"
	></div>
	<div
		class="pointer-events-none absolute -bottom-16 -left-16 h-64 w-64 rounded-full bg-hops-500/10 blur-3xl"
		aria-hidden="true"
	></div>

	<div class="relative z-10 flex flex-col gap-6">
		<!-- Top Bar: Brewery context + Action buttons -->
		<div class="flex flex-col justify-between gap-4 lg:flex-row lg:items-center">
			<div class="space-y-1.5">
				<div class="flex items-center gap-2">
					<div
						class="inline-flex items-center gap-1.5 rounded-full border border-amber-500/30 bg-amber-500/10 px-2.5 py-0.5 text-xs font-semibold text-amber-600 dark:text-amber-400"
					>
						<Beer class="h-3.5 w-3.5" />
						<span>{brewery.activeSetup?.name || 'My Brewery'}</span>
					</div>
					{#if dashboard.lastUpdated}
						<span class="text-xs text-zinc-600 dark:text-zinc-400">
							• {t('dashboard.last_updated')}
							{dashboard.lastUpdated.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
						</span>
					{/if}
				</div>
				<h1
					class="font-editorial text-3xl font-bold tracking-tight text-zinc-900 sm:text-4xl dark:text-white"
				>
					{t('dashboard.title')}
				</h1>
			</div>

			<!-- Quick Actions Toolbar -->
			<div class="flex flex-wrap items-center gap-2.5 sm:gap-3">
				<button
					type="button"
					onclick={handleRefresh}
					disabled={dashboard.refreshing}
					class="inline-flex h-10 items-center justify-center rounded-xl border border-zinc-200 bg-white/70 px-3 text-xs font-medium text-zinc-700 transition hover:bg-zinc-100 disabled:opacity-50 dark:border-white/10 dark:bg-zinc-900/60 dark:text-zinc-300 dark:hover:bg-zinc-800"
					title={t('dashboard.refresh')}
				>
					<RefreshCw class="h-4 w-4 {dashboard.refreshing ? 'animate-spin text-amber-500' : ''}" />
					<span class="ml-1.5 hidden sm:inline">{t('dashboard.refresh')}</span>
				</button>

				<button
					type="button"
					onclick={onOpenReadingModal}
					class="text-hops-700 dark:text-hops-300 inline-flex h-10 items-center gap-1.5 rounded-xl border border-hops-500/40 bg-hops-500/10 px-3.5 text-xs font-semibold transition hover:bg-hops-500/20 sm:text-sm"
				>
					<Activity class="h-4 w-4" />
					<span>{t('dashboard.log_reading')}</span>
				</button>

				<a
					href="/recipes/new"
					class="inline-flex h-10 items-center gap-1.5 rounded-xl border border-zinc-200 bg-white/80 px-3.5 text-xs font-semibold text-zinc-800 transition hover:bg-zinc-100 sm:text-sm dark:border-white/10 dark:bg-zinc-900/60 dark:text-zinc-200 dark:hover:bg-zinc-800"
				>
					<BookOpen class="h-4 w-4" />
					<span>{t('dashboard.new_recipe')}</span>
				</a>

				<a
					href="/batches/new"
					class="inline-flex h-10 items-center gap-1.5 rounded-xl border border-amber-500/50 bg-gradient-to-r from-amber-500 to-amber-600 px-4 text-xs font-bold text-zinc-950 shadow-[0_0_15px_rgba(245,158,11,0.25)] transition hover:scale-[1.02] hover:from-amber-400 hover:to-amber-500 sm:text-sm"
				>
					<PlusCircle class="h-4 w-4 stroke-[2.5]" />
					<span>{t('dashboard.new_batch')}</span>
				</a>
			</div>
		</div>

		<!-- Operational KPI Badges Bar -->
		<div class="grid grid-cols-2 gap-3 sm:grid-cols-4 sm:gap-4">
			<!-- Active Batches -->
			<div class="glass-card flex items-center gap-3.5 rounded-2xl p-4">
				<div
					class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-amber-500/15 text-amber-500 dark:text-amber-400"
				>
					<Beer class="h-5 w-5" />
				</div>
				<div>
					<p class="text-xs font-medium text-zinc-600 dark:text-zinc-400">
						{t('dashboard.active_batches')}
					</p>
					<p class="text-xl font-bold tracking-tight text-zinc-900 dark:text-white">
						{dashboard.activeBatchCount}
					</p>
				</div>
			</div>

			<!-- Cellar Utilization -->
			<div class="glass-card flex items-center gap-3.5 rounded-2xl p-4">
				<div
					class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-sky-500/15 text-sky-500 dark:text-sky-400"
				>
					<Gauge class="h-5 w-5" />
				</div>
				<div>
					<p class="text-xs font-medium text-zinc-600 dark:text-zinc-400">
						{t('dashboard.cellar_utilization')}
					</p>
					<p class="text-xl font-bold tracking-tight text-zinc-900 dark:text-white">
						{dashboard.cellarUtilizationPercent}%
					</p>
				</div>
			</div>

			<!-- Volume Under Yeast -->
			<div class="glass-card flex items-center gap-3.5 rounded-2xl p-4">
				<div
					class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-hops-500/15 text-hops-600 dark:text-hops-400"
				>
					<Layers class="h-5 w-5" />
				</div>
				<div>
					<p class="text-xs font-medium text-zinc-600 dark:text-zinc-400">
						{t('dashboard.volume_under_yeast')}
					</p>
					<p class="text-xl font-bold tracking-tight text-zinc-900 dark:text-white">
						{settings.formatVolume(dashboard.totalVolumeUnderYeast)}
					</p>
				</div>
			</div>

			<!-- Tasks Due Today -->
			<div class="glass-card flex items-center gap-3.5 rounded-2xl p-4">
				<div
					class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-rose-500/15 text-rose-500 dark:text-rose-400"
				>
					<CheckSquare class="h-5 w-5" />
				</div>
				<div>
					<p class="text-xs font-medium text-zinc-600 dark:text-zinc-400">
						{t('dashboard.tasks_due')}
					</p>
					<p class="text-xl font-bold tracking-tight text-zinc-900 dark:text-white">
						{dashboard.tasksDueCount}
					</p>
				</div>
			</div>
		</div>
	</div>
</header>
