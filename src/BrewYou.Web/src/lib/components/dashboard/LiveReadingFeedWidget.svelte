<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { dashboard } from '$lib/stores/dashboard.svelte';
	import { Activity, Calculator, Clock, ExternalLink } from '@lucide/svelte';

	function formatTimeAgo(isoString: string): string {
		try {
			const diffMs = Date.now() - new Date(isoString).getTime();
			const diffMins = Math.floor(diffMs / 60000);
			if (diffMins < 1) return 'just now';
			if (diffMins < 60) return `${diffMins}m ago`;
			const diffHours = Math.floor(diffMins / 60);
			if (diffHours < 24) return `${diffHours}h ago`;
			return `${Math.floor(diffHours / 24)}d ago`;
		} catch {
			return '';
		}
	}
</script>

<div class="glass-panel flex h-full flex-col justify-between space-y-4 rounded-3xl p-6">
	<div class="space-y-4">
		<div class="flex items-center justify-between">
			<div class="flex items-center gap-2">
				<Activity class="h-4 w-4 text-hops-500" />
				<h3 class="text-sm font-bold tracking-tight text-zinc-900 sm:text-base dark:text-white">
					{t('dashboard.live_activity_stream')}
				</h3>
			</div>
		</div>

		<!-- Activity Feed Items -->
		{#if dashboard.recentActivity.length === 0}
			<div
				class="flex flex-col items-center justify-center rounded-2xl border border-dashed border-zinc-200 p-4 text-center dark:border-white/10"
			>
				<Clock class="mx-auto h-6 w-6 text-zinc-400 dark:text-zinc-500" />
				<p class="mt-1.5 text-xs text-zinc-600 dark:text-zinc-400">
					{t('dashboard.no_recent_readings')}
				</p>
			</div>
		{:else}
			<div class="space-y-2">
				{#each dashboard.recentActivity.slice(0, 5) as item (item.id)}
					<div
						class="flex items-start justify-between gap-2 rounded-xl border border-zinc-200/60 bg-zinc-100/50 p-2.5 text-xs dark:border-white/5 dark:bg-zinc-900/30"
					>
						<div class="min-w-0 space-y-0.5">
							<div class="flex items-center gap-1.5">
								{#if item.batchCode}
									<span class="font-mono font-bold text-amber-600 dark:text-amber-400">
										{item.batchCode}
									</span>
								{/if}
								<span class="truncate font-medium text-zinc-800 dark:text-zinc-200">
									{item.batchName}
								</span>
							</div>
							<p class="text-[11px] text-zinc-600 dark:text-zinc-400">
								{item.message}
							</p>
						</div>

						<span class="shrink-0 font-mono text-[10px] text-zinc-600 dark:text-zinc-400">
							{formatTimeAgo(item.timestamp)}
						</span>
					</div>
				{/each}
			</div>
		{/if}
	</div>

	<!-- Quick Calculator Jump Shortcuts -->
	<div class="border-t border-zinc-200/80 pt-3 dark:border-white/10">
		<div
			class="mb-2 flex items-center justify-between text-xs font-semibold text-zinc-600 dark:text-zinc-400"
		>
			<div class="flex items-center gap-1.5">
				<Calculator class="h-3.5 w-3.5" />
				<span>{t('dashboard.quick_calculators')}</span>
			</div>
			<a href="/calculations" class="text-amber-600 transition-colors hover:text-amber-500">
				<ExternalLink class="h-3 w-3" />
			</a>
		</div>

		<div class="grid grid-cols-3 gap-1.5">
			<a
				href="/calculations"
				class="flex items-center justify-center rounded-lg border border-zinc-200 bg-white/70 px-1.5 py-2 text-center text-[10px] font-medium text-zinc-700 transition hover:bg-zinc-100 dark:border-white/10 dark:bg-zinc-900/40 dark:text-zinc-300 dark:hover:bg-zinc-800"
			>
				<span class="truncate">{t('dashboard.calc_hydrometer')}</span>
			</a>
			<a
				href="/calculations"
				class="flex items-center justify-center rounded-lg border border-zinc-200 bg-white/70 px-1.5 py-2 text-center text-[10px] font-medium text-zinc-700 transition hover:bg-zinc-100 dark:border-white/10 dark:bg-zinc-900/40 dark:text-zinc-300 dark:hover:bg-zinc-800"
			>
				<span class="truncate">{t('dashboard.calc_dilution')}</span>
			</a>
			<a
				href="/calculations"
				class="flex items-center justify-center rounded-lg border border-zinc-200 bg-white/70 px-1.5 py-2 text-center text-[10px] font-medium text-zinc-700 transition hover:bg-zinc-100 dark:border-white/10 dark:bg-zinc-900/40 dark:text-zinc-300 dark:hover:bg-zinc-800"
			>
				<span class="truncate">{t('dashboard.calc_strike')}</span>
			</a>
		</div>
	</div>
</div>
