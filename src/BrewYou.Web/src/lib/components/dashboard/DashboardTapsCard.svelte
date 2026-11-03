<script module lang="ts">
	import { resolveApiUrl } from '$lib/api/client';
	import type { EquipmentDto } from '$lib/types/api';

	export function getTapImageUrl(tap: EquipmentDto): string {
		if (tap.assignedBatch?.labelImageUrl) {
			return resolveApiUrl(tap.assignedBatch.labelImageUrl);
		}
		return resolveApiUrl('/api/v1/labels/default/image/card?size=640');
	}
</script>

<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import { sortEquipmentByName } from '$lib/utils/equipment-order';
	import { Beer, ChevronRight, Plus, Thermometer } from '@lucide/svelte';

	interface Props {
		taps: EquipmentDto[];
		onAssignTap?: (tap: EquipmentDto) => void;
	}

	let { taps, onAssignTap }: Props = $props();

	let sortedTaps = $derived(sortEquipmentByName(taps));
	let pouringTapCount = $derived(sortedTaps.filter((t) => !!t.assignedBatch).length);
</script>

<div
	class="glass-panel flex flex-col space-y-4 rounded-3xl p-6 shadow-xs transition-all"
	data-testid="dashboard-taps-card"
>
	<!-- Header Bar with Title, KPIs, and Navigation -->
	<div class="flex flex-wrap items-center justify-between gap-3">
		<div class="flex items-center gap-3">
			<div
				class="flex h-10 w-10 items-center justify-center rounded-2xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
			>
				<Beer class="h-5 w-5" />
			</div>
			<div>
				<div class="flex items-center gap-2">
					<h3 class="text-base font-bold tracking-tight text-zinc-900 sm:text-lg dark:text-white">
						{t('dashboard.taps_title')}
					</h3>
					{#if taps.length > 0}
						<span
							class="rounded-full border border-amber-500/30 bg-amber-500/10 px-2.5 py-0.5 font-mono text-xs font-bold text-amber-700 dark:text-amber-400"
						>
							{pouringTapCount} / {taps.length}
						</span>
					{/if}
				</div>
				<p class="text-xs text-zinc-500 dark:text-zinc-400">
					{t('dashboard.taps_subtitle')}
				</p>
			</div>
		</div>

		<a
			href="/inventory/equipment"
			class="flex items-center gap-1.5 rounded-xl border border-zinc-200/80 bg-white/70 px-3 py-1.5 text-xs font-semibold text-zinc-700 shadow-xs transition-colors hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900/60 dark:text-zinc-300 dark:hover:bg-zinc-800"
		>
			<span>{t('dashboard.manage_taps')}</span>
			<ChevronRight class="h-3.5 w-3.5 text-zinc-400" />
		</a>
	</div>

	{#if taps.length === 0}
		<!-- Empty State: No Taps configured yet -->
		<div
			class="flex flex-col items-center justify-center rounded-2xl border border-dashed border-zinc-300/80 bg-zinc-50/50 p-8 text-center sm:p-12 dark:border-zinc-800/80 dark:bg-zinc-900/20"
		>
			<div
				class="flex h-12 w-12 items-center justify-center rounded-full bg-zinc-200/70 text-zinc-500 dark:bg-zinc-800 dark:text-zinc-400"
			>
				<Beer class="h-6 w-6 stroke-[1.5]" />
			</div>
			<h4 class="mt-4 text-sm font-bold text-zinc-900 dark:text-white">
				{t('dashboard.taps_empty_title')}
			</h4>
			<p class="mt-1 max-w-md text-xs text-zinc-500 dark:text-zinc-400">
				{t('dashboard.taps_empty_desc')}
			</p>
			<a
				href="/inventory/equipment"
				class="mt-4 inline-flex items-center gap-1.5 rounded-xl bg-amber-500 px-4 py-2 text-xs font-bold text-zinc-950 shadow-xs transition-colors hover:bg-amber-400"
			>
				<Plus class="h-4 w-4 stroke-[2.5]" />
				<span>{t('dashboard.manage_taps')}</span>
			</a>
		</div>
	{:else}
		<!-- All Taps Displayed Side-by-Side with Batch Image Above Each -->
		<div
			class="grid grid-cols-1 gap-4 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6"
			data-testid="dashboard-taps-grid"
		>
			{#each sortedTaps as tap (tap.id)}
				<article
					class="artisan-card group flex flex-col justify-between overflow-hidden rounded-2xl border border-zinc-200/80 bg-white/70 p-0 shadow-xs transition-all hover:border-amber-500/40 dark:border-zinc-800/80 dark:bg-zinc-900/60"
					data-testid="tap-card-{tap.id}"
				>
					<div>
						<!-- Batch Image ABOVE Tap -->
						<div
							class="relative aspect-[16/10] w-full overflow-hidden border-b border-zinc-200/60 bg-zinc-900 dark:border-white/5"
							data-testid="tap-image-container"
						>
							{#if tap.assignedBatch}
								<a
									href="/batches/{tap.assignedBatch.id}"
									class="relative block h-full w-full"
									title="{tap.assignedBatch.name} ({tap.assignedBatch.batchCode})"
								>
									<img
										src={getTapImageUrl(tap)}
										alt={tap.assignedBatch.name}
										loading="lazy"
										class="h-full w-full object-cover transition-transform duration-500 group-hover:scale-105"
									/>
									<div
										class="absolute inset-0 bg-gradient-to-t from-black/80 via-black/25 to-transparent"
									></div>

									<!-- Tap Name Badge (Top Right) -->
									<div class="absolute top-2.5 right-2.5">
										<span
											class="inline-flex items-center rounded-md border border-white/20 bg-black/60 px-2 py-0.5 font-mono text-[10px] font-bold text-white shadow-xs backdrop-blur-md"
										>
											{tap.name}
										</span>
									</div>

									<!-- Beer Style & ABV Badges (Bottom) -->
									<div
										class="absolute right-2.5 bottom-2.5 left-2.5 flex items-center justify-between gap-1.5"
									>
										<span
											class="truncate rounded-md border border-amber-500/30 bg-amber-500/90 px-1.5 py-0.5 text-[10px] font-bold text-zinc-950 shadow-xs backdrop-blur-md"
										>
											{tap.assignedBatch.beerStyle}
										</span>
										{#if tap.assignedBatch.actualAbv != null}
											<span
												class="shrink-0 rounded-md border border-white/20 bg-black/75 px-1.5 py-0.5 font-mono text-[10px] font-bold text-emerald-400 shadow-xs backdrop-blur-md"
											>
												{tap.assignedBatch.actualAbv}% ABV
											</span>
										{/if}
									</div>
								</a>
							{:else}
								<!-- Empty Tap Placeholder Artwork -->
								<div
									class="relative flex h-full w-full flex-col items-center justify-center bg-gradient-to-br from-zinc-100 to-zinc-200 dark:from-zinc-900 dark:to-zinc-800"
								>
									<Beer class="h-8 w-8 text-zinc-400/60 dark:text-zinc-600/60" />
									<div class="absolute top-2.5 right-2.5">
										<span
											class="inline-flex items-center rounded-md border border-zinc-200 bg-white/80 px-2 py-0.5 font-mono text-[10px] font-bold text-zinc-600 shadow-xs backdrop-blur-md dark:border-zinc-700 dark:bg-black/60 dark:text-zinc-400"
										>
											{tap.name}
										</span>
									</div>
									<span
										class="absolute bottom-2.5 rounded-md border border-zinc-200/80 bg-white/80 px-2 py-0.5 text-[10px] font-semibold text-zinc-500 shadow-xs backdrop-blur-md dark:border-zinc-700/80 dark:bg-zinc-900/80 dark:text-zinc-400"
									>
										{t('dashboard.tap_empty')}
									</span>
								</div>
							{/if}
						</div>

						<!-- Tap Details & Batch Info Below Image -->
						<div class="space-y-3 p-4">
							{#if tap.assignedBatch}
								<!-- Batch Title & Code -->
								<div class="space-y-1">
									<a
										href="/batches/{tap.assignedBatch.id}"
										class="block truncate text-sm font-bold text-zinc-900 transition-colors hover:text-amber-600 dark:text-white dark:hover:text-amber-400"
										title={tap.assignedBatch.name}
									>
										{tap.assignedBatch.name}
									</a>
									<div class="flex items-center gap-1.5 text-xs">
										<span
											class="font-mono text-[11px] font-bold text-amber-600 dark:text-amber-400"
										>
											{tap.assignedBatch.batchCode}
										</span>
										{#if tap.assignedKeg}
											<span class="text-zinc-400">•</span>
											<span
												class="truncate text-[11px] font-medium text-zinc-500 dark:text-zinc-400"
											>
												{tap.assignedKeg.name}
											</span>
										{/if}
									</div>
								</div>

								<!-- Keg Fill Level & Progress -->
								{#if tap.assignedKeg}
									<div class="space-y-1.5" data-testid="tap-keg-level">
										<div class="flex items-baseline justify-between text-xs">
											<span
												class="font-mono text-[11px] font-bold text-zinc-700 dark:text-zinc-300"
											>
												{settings.formatVolume(tap.assignedKeg.currentVolumeLiters)}
												<span class="font-normal text-zinc-400">
													/ {settings.formatVolume(tap.assignedKeg.capacityLiters)}
												</span>
											</span>
											<span
												class="font-mono text-[11px] font-bold text-amber-600 dark:text-amber-400"
											>
												{Math.round(tap.assignedKeg.fillPercentage)}%
											</span>
										</div>

										<div
											class="h-1.5 w-full overflow-hidden rounded-full bg-zinc-200 dark:bg-zinc-800"
										>
											<div
												class="h-full rounded-full bg-gradient-to-r from-amber-400 via-amber-500 to-amber-600 transition-all duration-500 {tap
													.assignedKeg.fillPercentage > 0
													? 'shadow-[0_0_8px_rgba(245,158,11,0.35)]'
													: ''}"
												style="width: {Math.max(0, Math.min(100, tap.assignedKeg.fillPercentage))}%"
												role="progressbar"
												aria-valuenow={Math.round(tap.assignedKeg.fillPercentage)}
												aria-valuemin="0"
												aria-valuemax="100"
											></div>
										</div>
									</div>
								{:else if tap.fillLevelPercent != null}
									<div class="space-y-1.5">
										<div class="flex items-baseline justify-between text-xs">
											<span class="text-[11px] text-zinc-500 dark:text-zinc-400">
												{t('equipment.fill_level')}
											</span>
											<span
												class="font-mono text-[11px] font-bold text-amber-600 dark:text-amber-400"
											>
												{Math.round(tap.fillLevelPercent)}%
											</span>
										</div>
										<div
											class="h-1.5 w-full overflow-hidden rounded-full bg-zinc-200 dark:bg-zinc-800"
										>
											<div
												class="h-full rounded-full bg-gradient-to-r from-amber-400 via-amber-500 to-amber-600 transition-all duration-500"
												style="width: {Math.max(0, Math.min(100, tap.fillLevelPercent))}%"
												role="progressbar"
												aria-valuenow={Math.round(tap.fillLevelPercent)}
												aria-valuemin="0"
												aria-valuemax="100"
											></div>
										</div>
									</div>
								{/if}
							{:else}
								<!-- Unassigned Tap Description -->
								<div class="py-2 text-center">
									<p class="text-xs text-zinc-500 dark:text-zinc-400">
										{t('dashboard.tap_no_batch')}
									</p>
								</div>
							{/if}
						</div>
					</div>

					<!-- Card Footer: Status & Actions -->
					<div
						class="flex items-center justify-between border-t border-zinc-200/60 p-3 dark:border-white/5"
					>
						{#if tap.assignedBatch}
							<span
								class="inline-flex items-center gap-1.5 rounded-full border border-emerald-500/30 bg-emerald-500/10 px-2 py-0.5 text-[10px] font-bold text-emerald-600 dark:text-emerald-400"
							>
								<span class="h-1.5 w-1.5 animate-pulse rounded-full bg-emerald-500"></span>
								{t('dashboard.tap_pouring')}
							</span>

							{#if tap.currentTemperatureC != null}
								<span
									class="flex items-center gap-1 font-mono text-[11px] text-zinc-600 dark:text-zinc-400"
									title="Serving Temperature"
								>
									<Thermometer class="h-3 w-3 text-cyan-500" />
									{settings.formatTemp(tap.currentTemperatureC)}
								</span>
							{/if}

							{#if onAssignTap}
								<button
									type="button"
									onclick={() => onAssignTap?.(tap)}
									class="rounded-lg p-1 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-amber-600 dark:hover:bg-zinc-800 dark:hover:text-amber-400"
									title={t('taps.change_batch_btn')}
									aria-label="{t('taps.change_batch_btn')} - {tap.name}"
								>
									<Beer class="h-3.5 w-3.5" />
								</button>
							{/if}
						{:else}
							<span
								class="inline-flex items-center rounded-full border border-zinc-200 bg-zinc-100 px-2 py-0.5 text-[10px] font-medium text-zinc-500 dark:border-zinc-800 dark:bg-zinc-800 dark:text-zinc-400"
							>
								{t('dashboard.tap_empty')}
							</span>

							{#if onAssignTap}
								<button
									type="button"
									onclick={() => onAssignTap?.(tap)}
									class="inline-flex items-center gap-1 rounded-lg border border-amber-500/30 bg-amber-500/10 px-2 py-1 text-[11px] font-bold text-amber-700 transition-colors hover:bg-amber-500 hover:text-zinc-950 dark:text-amber-400 dark:hover:bg-amber-400 dark:hover:text-zinc-950"
								>
									<Plus class="h-3 w-3 stroke-[2.5]" />
									<span>{t('dashboard.tap_assign_action')}</span>
								</button>
							{:else}
								<a
									href="/inventory/equipment"
									class="inline-flex items-center gap-1 text-[11px] font-semibold text-amber-600 hover:underline dark:text-amber-400"
								>
									<span>{t('dashboard.tap_assign_action')}</span>
									<ChevronRight class="h-3 w-3" />
								</a>
							{/if}
						{/if}
					</div>
				</article>
			{/each}
		</div>
	{/if}
</div>
