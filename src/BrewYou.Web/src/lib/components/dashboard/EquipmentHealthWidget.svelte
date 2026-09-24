<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { dashboard } from '$lib/stores/dashboard.svelte';
	import { Wrench, Wifi, ChevronRight, CheckCircle2 } from '@lucide/svelte';

	const totalEquipment = $derived(dashboard.equipment.length);
	const occupiedVesselCount = $derived(dashboard.activeBatches.length);
	const availableVesselCount = $derived(Math.max(0, totalEquipment - occupiedVesselCount));
</script>

<div class="glass-panel flex h-full flex-col justify-between space-y-4 rounded-3xl p-6">
	<div class="space-y-4">
		<div class="flex items-center justify-between">
			<div class="flex items-center gap-2">
				<Wrench class="h-4 w-4 text-amber-500" />
				<h3 class="text-sm font-bold tracking-tight text-zinc-900 sm:text-base dark:text-white">
					{t('dashboard.equipment_health')}
				</h3>
			</div>

			<a
				href="/inventory/equipment"
				class="inline-flex items-center gap-1 text-xs font-semibold text-amber-600 transition hover:text-amber-500 dark:text-amber-400"
			>
				<ChevronRight class="h-3.5 w-3.5" />
			</a>
		</div>

		<!-- Readiness Summary Badges -->
		<div class="grid grid-cols-2 gap-2.5">
			<div
				class="flex flex-col justify-between rounded-xl border border-zinc-200/80 bg-zinc-100/60 p-2.5 dark:border-white/10 dark:bg-zinc-900/40"
			>
				<div
					class="flex items-start justify-between gap-1 text-xs text-zinc-600 dark:text-zinc-400"
				>
					<span class="leading-tight">{t('dashboard.vessels_ready')}</span>
					<CheckCircle2 class="h-3.5 w-3.5 shrink-0 text-emerald-500" />
				</div>
				<p class="mt-2 font-mono text-base font-bold text-zinc-900 dark:text-white">
					{availableVesselCount}
					<span class="text-xs font-normal text-zinc-600 dark:text-zinc-400"
						>/ {totalEquipment}</span
					>
				</p>
			</div>

			<div
				class="flex flex-col justify-between rounded-xl border border-zinc-200/80 bg-zinc-100/60 p-2.5 dark:border-white/10 dark:bg-zinc-900/40"
			>
				<div
					class="flex items-start justify-between gap-1 text-xs text-zinc-600 dark:text-zinc-400"
				>
					<span class="leading-tight">{t('dashboard.probes_online')}</span>
					<Wifi
						class="h-3.5 w-3.5 shrink-0 {dashboard.probeStats.online > 0
							? 'text-hops-500'
							: 'text-zinc-600'}"
					/>
				</div>
				<p class="mt-2 font-mono text-base font-bold text-zinc-900 dark:text-white">
					{dashboard.probeStats.online}
					<span class="text-xs font-normal text-zinc-600 dark:text-zinc-400"
						>/ {dashboard.probeStats.total}</span
					>
				</p>
			</div>
		</div>
	</div>

	<!-- Equipment Quick List Preview or Empty State -->
	<div class="flex flex-1 flex-col justify-start pt-1">
		{#if dashboard.equipment.length > 0}
			<div class="space-y-1.5">
				{#each dashboard.equipment.slice(0, 4) as equip (equip.id)}
					{@const isOccupied = dashboard.activeBatches.some((b) => b.fermenterId === equip.id)}
					<div
						class="flex items-center justify-between rounded-xl border border-zinc-200/60 bg-zinc-100/50 px-2.5 py-1.5 text-xs transition hover:bg-zinc-100 dark:border-white/5 dark:bg-zinc-900/30 dark:hover:bg-zinc-800/50"
					>
						<div class="flex min-w-0 items-center gap-2">
							<span
								class="h-2 w-2 shrink-0 rounded-full {isOccupied
									? 'bg-amber-500 ring-2 ring-amber-500/20'
									: 'bg-emerald-500 ring-2 ring-emerald-500/20'}"
							></span>
							<span class="truncate font-medium text-zinc-800 dark:text-zinc-200">{equip.name}</span
							>
						</div>

						<span class="font-mono text-[11px] text-zinc-600 dark:text-zinc-400">
							{isOccupied ? t('dashboard.status_occupied') : t('dashboard.status_ready')}
						</span>
					</div>
				{/each}
			</div>
		{:else}
			<div
				class="flex flex-1 flex-col items-center justify-center rounded-2xl border border-dashed border-zinc-200 p-4 text-center dark:border-white/10"
			>
				<Wrench class="mx-auto h-5 w-5 text-zinc-400 dark:text-zinc-500" />
				<p class="mt-1.5 text-xs text-zinc-600 dark:text-zinc-400">
					{t('dashboard.no_equipment_configured')}
				</p>
			</div>
		{/if}
	</div>
</div>
