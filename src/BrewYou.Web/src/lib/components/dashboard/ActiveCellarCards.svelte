<script module lang="ts">
	import type { BatchSummaryDto, EquipmentDto, EquipmentSubtype } from '$lib/types/api';

	export function getBatchEquipment(
		batch: BatchSummaryDto,
		equipment: EquipmentDto[]
	): EquipmentDto | undefined {
		switch (batch.currentStage) {
			case 'Mash': {
				if (batch.mashTunId) {
					const mt = equipment.find((e) => e.id === batch.mashTunId);
					if (mt) return mt;
				}
				if (batch.boilerId) {
					const b = equipment.find((e) => e.id === batch.boilerId);
					if (b?.subtype === 'AllInOne') return b;
				}
				return equipment.find(
					(e) =>
						e.subtype === 'MashTun' ||
						e.subtype === 'AllInOne' ||
						e.subtype === 'LauterTun' ||
						e.type === 'Boiler'
				);
			}
			case 'Boil': {
				if (batch.boilerId) {
					const b = equipment.find((e) => e.id === batch.boilerId);
					if (b) return b;
				}
				return equipment.find(
					(e) =>
						e.type === 'Boiler' ||
						e.subtype === 'Pan' ||
						e.subtype === 'AllInOne' ||
						e.subtype === 'HermsRims'
				);
			}
			case 'Ferment':
			case 'Condition':
			default: {
				if (batch.fermenterId) {
					const f = equipment.find((e) => e.id === batch.fermenterId);
					if (f) return f;
				}
				return equipment.find(
					(e) =>
						e.type === 'Fermenter' ||
						e.subtype === 'ConicalFermenter' ||
						e.subtype === 'Carboy' ||
						e.subtype === 'PressureFermenter' ||
						e.subtype === 'StainlessBucket' ||
						e.subtype === 'Bucket'
				);
			}
		}
	}

	export function getBatchSubtype(
		batch: BatchSummaryDto,
		equip?: EquipmentDto
	): EquipmentSubtype | 'Bottle' | string {
		if (equip?.subtype) return equip.subtype;
		switch (batch.currentStage) {
			case 'Mash':
				return 'AllInOne';
			case 'Boil':
				return 'Pan';
			case 'Ferment':
				return 'ConicalFermenter';
			case 'Condition':
				return 'Carboy';
			case 'Package':
				return 'Bottle';
			default:
				return 'ConicalFermenter';
		}
	}
</script>

<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import VesselIcon from '$lib/components/inventory/VesselIcon.svelte';
	import BeerGlass from '$lib/components/brewery/BeerGlass.svelte';
	import {
		Activity,
		ChevronRight,
		ArrowRight,
		Thermometer,
		Droplets,
		Clock,
		Sparkles
	} from '@lucide/svelte';

	interface Props {
		batches: BatchSummaryDto[];
		equipment: EquipmentDto[];
		onLogReading: (batchId: string) => void;
		onAdvanceStage: (batch: BatchSummaryDto) => void;
	}

	let { batches, equipment, onLogReading, onAdvanceStage }: Props = $props();

	function calculateFillPercent(batch: BatchSummaryDto, equip?: EquipmentDto): number {
		const vol = batch.measuredBatchSizeLiters ?? batch.targetBatchSizeLiters ?? 20;
		if (equip?.capacityLiters && equip.capacityLiters > 0) {
			return Math.min(100, Math.max(10, Math.round((vol / equip.capacityLiters) * 100)));
		}
		return 80;
	}

	function formatGravity(val: number | null | undefined): string {
		if (val === null || val === undefined) return '--';
		if (settings.gravityUnit === 'Plato') {
			return `${settings.sgToPlato(val).toFixed(1)} °P`;
		}
		return val.toFixed(3);
	}
</script>

<section class="glass-panel flex h-full flex-col justify-between space-y-4 rounded-3xl p-6">
	<div class="flex items-center justify-between">
		<div class="flex items-center gap-2">
			<Activity class="h-4 w-4 text-hops-500" />
			<h2 class="text-sm font-bold tracking-tight text-zinc-900 sm:text-base dark:text-white">
				{t('dashboard.fermenting_cellar')}
			</h2>
			<span
				class="rounded-full bg-zinc-100 px-2.5 py-0.5 font-mono text-xs font-semibold text-zinc-600 dark:bg-zinc-800 dark:text-zinc-300"
			>
				{batches.length}
			</span>
		</div>

		<a
			href="/batches"
			class="inline-flex items-center gap-1 text-xs font-semibold text-amber-600 transition hover:text-amber-500 dark:text-amber-400"
		>
			<span>{t('dashboard.view_all_batches')}</span>
			<ChevronRight class="h-3.5 w-3.5" />
		</a>
	</div>

	<div class="grid flex-1 grid-cols-1 gap-4 lg:grid-cols-2">
		{#each batches as batch (batch.id)}
			{@const equip = getBatchEquipment(batch, equipment)}
			{@const fill = calculateFillPercent(batch, equip)}
			{@const subtype = getBatchSubtype(batch, equip)}
			{@const equipName =
				equip?.name ??
				(batch.currentStage === 'Boil'
					? batch.boilerName
					: batch.currentStage === 'Mash'
						? (batch.mashTunName ?? batch.boilerName)
						: batch.fermenterName)}

			<div
				data-testid="batch-cellar-card-{batch.id}"
				class="group relative flex flex-col justify-between overflow-hidden rounded-2xl border border-zinc-200/80 bg-zinc-50/70 p-4 transition-all duration-200 hover:border-amber-500/40 hover:shadow-md dark:border-white/10 dark:bg-zinc-900/40 dark:hover:border-amber-500/30"
			>
				<!-- Card Header: Batch Identity & Beer Color Glass -->
				<div
					class="flex items-start justify-between gap-3 border-b border-zinc-200/60 pb-3 dark:border-white/5"
				>
					<div class="min-w-0 flex-1 space-y-1">
						<div class="flex flex-wrap items-center gap-1.5">
							<span
								class="rounded-md border border-amber-500/30 bg-amber-500/10 px-2 py-0.5 font-mono text-xs font-semibold text-amber-600 dark:text-amber-400"
							>
								{batch.batchCode}
							</span>
							<span
								data-testid="batch-stage-badge-{batch.id}"
								class="rounded-md bg-amber-500/20 px-2 py-0.5 font-mono text-xs font-bold text-amber-700 dark:text-amber-300"
							>
								{t(`batches.stages.${batch.currentStage.toLowerCase()}`)}
							</span>
							<span
								class="inline-flex items-center rounded-md border border-zinc-200/80 bg-zinc-100/80 px-2 py-0.5 text-xs font-medium text-zinc-600 dark:border-white/10 dark:bg-zinc-800/80 dark:text-zinc-300"
							>
								<span class="max-w-[140px] truncate">{batch.beerStyle}</span>
							</span>
						</div>

						<h3 class="truncate text-base font-bold text-zinc-900 dark:text-white">
							<a href="/batches/{batch.id}" class="transition-colors hover:text-amber-500">
								{batch.name}
							</a>
						</h3>

						<p class="flex items-center gap-1.5 text-xs text-zinc-500 dark:text-zinc-400">
							<Clock class="h-3.5 w-3.5 shrink-0 text-zinc-400 dark:text-zinc-500" />
							<span class="truncate">
								{t('dashboard.days_in_stage', {
									day: batch.daysActive,
									stage: t(`batches.stages.${batch.currentStage.toLowerCase()}`)
								})}
							</span>
							{#if equipName}
								<span>•</span>
								<span
									data-testid="batch-equipment-name-{batch.id}"
									class="truncate font-medium text-zinc-700 dark:text-zinc-300">{equipName}</span
								>
							{/if}
						</p>
					</div>

					<BeerGlass srm={batch.colorSrm} size="sm" showLabel class="shrink-0 self-start" />
				</div>

				<!-- Vessel & Telemetry Instrumentation HUD -->
				<div class="my-3.5 flex items-center gap-3">
					<!-- Visual Tank Badge Graphic -->
					<div
						class="relative flex h-20 w-20 shrink-0 items-center justify-center rounded-2xl border border-zinc-200/80 bg-zinc-100/80 p-2 shadow-inner transition group-hover:scale-105 dark:border-white/10 dark:bg-zinc-900/60"
					>
						<VesselIcon
							{subtype}
							fillPercent={fill}
							size={50}
							active={true}
							animated={true}
							animationStage={batch.currentStage.toLowerCase()}
							title="{equipName || subtype} ({fill}% full)"
						/>
						<span
							data-testid="vessel-fill-badge-{batch.id}"
							class="absolute -bottom-2 rounded-full border border-zinc-200 bg-white px-1.5 py-0.5 font-mono text-[10px] font-bold text-zinc-600 shadow-sm dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
						>
							{fill}%
						</span>
					</div>

					<!-- Telemetry HUD Bar (Temperature & Gravity) -->
					<div
						class="grid flex-1 grid-cols-2 gap-2 rounded-xl bg-zinc-100/60 p-2.5 sm:p-3 dark:bg-zinc-900/40"
					>
						<!-- Temperature -->
						<div class="space-y-0.5">
							<div
								class="flex items-center gap-1 text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
							>
								<Thermometer class="h-3.5 w-3.5 shrink-0 text-rose-500" />
								<span class="truncate">{t('dashboard.temperature_reading')}</span>
							</div>
							<p class="font-mono text-sm font-bold text-zinc-900 dark:text-white">
								{batch.vesselTempC !== null && batch.vesselTempC !== undefined
									? settings.formatTemperature(batch.vesselTempC)
									: '--'}
							</p>
						</div>

						<!-- Gravity -->
						<div class="space-y-0.5">
							<div
								class="flex items-center gap-1 text-[11px] font-medium text-zinc-600 dark:text-zinc-400"
							>
								<Droplets class="h-3.5 w-3.5 shrink-0 text-sky-500" />
								<span class="truncate"
									>{settings.gravityUnit === 'Plato'
										? t('dashboard.plato_reading')
										: t('dashboard.gravity_reading')}</span
								>
							</div>
							<p class="truncate font-mono text-sm font-bold text-zinc-900 dark:text-white">
								{formatGravity(batch.currentGravity ?? batch.targetOg)}
								<span class="text-[10px] font-normal text-zinc-600 dark:text-zinc-400">
									({t('dashboard.target_gravity', { gravity: formatGravity(batch.targetFg) })})
								</span>
							</p>
						</div>
					</div>
				</div>

				<!-- Card Footer Actions -->
				<div
					class="flex items-center justify-between gap-2 border-t border-zinc-200/80 pt-3 dark:border-white/10"
				>
					<div class="flex items-center gap-2">
						<button
							type="button"
							onclick={() => onLogReading(batch.id)}
							class="inline-flex items-center gap-1 rounded-lg border border-zinc-200 bg-white px-2.5 py-1.5 text-xs font-semibold text-zinc-800 transition hover:bg-zinc-100 dark:border-white/10 dark:bg-zinc-800 dark:text-zinc-200 dark:hover:bg-zinc-700"
						>
							<Activity class="h-3 w-3 text-hops-500" />
							<span>{t('dashboard.log_reading')}</span>
						</button>

						<button
							type="button"
							onclick={() => onAdvanceStage(batch)}
							class="inline-flex items-center gap-1 rounded-lg border border-zinc-200 bg-white px-2.5 py-1.5 text-xs font-semibold text-zinc-800 transition hover:bg-zinc-100 dark:border-white/10 dark:bg-zinc-800 dark:text-zinc-200 dark:hover:bg-zinc-700"
						>
							<Sparkles class="h-3 w-3 text-amber-500" />
							<span>{t('dashboard.advance_stage')}</span>
						</button>
					</div>

					<a
						href="/batches/{batch.id}"
						class="inline-flex items-center gap-1 text-xs font-semibold text-zinc-600 transition hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-white"
					>
						<span>{t('dashboard.view_batch')}</span>
						<ArrowRight class="h-3 w-3" />
					</a>
				</div>
			</div>
		{/each}
	</div>
</section>
