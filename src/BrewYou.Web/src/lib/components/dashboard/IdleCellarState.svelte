<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import type { EquipmentDto } from '$lib/types/api';
	import VesselIcon from '$lib/components/inventory/VesselIcon.svelte';
	import { PlusCircle, BookOpen, Sparkles, CheckCircle2 } from '@lucide/svelte';

	interface Props {
		equipment: EquipmentDto[];
	}

	let { equipment }: Props = $props();

	const idleFermenters = $derived.by(() => {
		return equipment.filter(
			(e) =>
				e.type === 'Fermenter' ||
				e.subtype === 'ConicalFermenter' ||
				e.subtype === 'Carboy' ||
				e.subtype === 'PressureFermenter' ||
				e.subtype === 'StainlessBucket' ||
				e.subtype === 'Bucket'
		);
	});
</script>

<div
	class="glass-panel relative flex h-full flex-col items-center justify-center overflow-hidden rounded-3xl border border-dashed border-zinc-300/80 p-6 text-center sm:p-8 dark:border-white/15"
>
	<!-- Subtle ambient glow -->
	<div
		class="pointer-events-none absolute -top-12 h-48 w-48 rounded-full bg-amber-500/10 blur-3xl dark:bg-amber-500/15"
		aria-hidden="true"
	></div>

	<!-- Idle Sanitized Tank Visual -->
	<div class="relative mb-5 flex items-center justify-center">
		<div
			class="flex h-24 w-24 items-center justify-center rounded-3xl border border-zinc-200 bg-white/80 p-3 shadow-md dark:border-white/10 dark:bg-zinc-900/60"
		>
			<VesselIcon
				subtype="ConicalFermenter"
				fillPercent={0}
				size={64}
				active={false}
				title="Sanitized & Ready Fermenter"
			/>
		</div>
		<div
			class="absolute -right-2 -bottom-2 flex h-8 w-8 items-center justify-center rounded-full border border-white bg-emerald-500 text-white shadow-md dark:border-zinc-900"
			title="Sanitized & Ready"
		>
			<CheckCircle2 class="h-5 w-5" />
		</div>
	</div>

	<h3 class="text-xl font-bold tracking-tight text-zinc-900 sm:text-2xl dark:text-white">
		{t('dashboard.cellar_idle_title')}
	</h3>
	<p class="mt-2 max-w-md text-sm text-zinc-600 sm:text-base dark:text-zinc-400">
		{t('dashboard.cellar_idle_desc')}
	</p>

	<!-- Idle vessels list if present -->
	{#if idleFermenters.length > 0}
		<div class="mt-4 flex flex-wrap items-center justify-center gap-2">
			{#each idleFermenters.slice(0, 4) as vessel (vessel.id)}
				<span
					class="inline-flex items-center gap-1.5 rounded-full border border-emerald-500/30 bg-emerald-500/10 px-3 py-1 text-xs font-semibold text-emerald-700 dark:text-emerald-400"
				>
					<span class="h-1.5 w-1.5 rounded-full bg-emerald-500"></span>
					<span>{vessel.name} ({vessel.capacityLiters} L)</span>
				</span>
			{/each}
		</div>
	{/if}

	<!-- Fast-track CTAs -->
	<div class="mt-6 flex flex-wrap items-center justify-center gap-3 sm:gap-4">
		<a
			href="/batches/new"
			class="inline-flex items-center gap-2 rounded-xl border border-amber-500/50 bg-gradient-to-r from-amber-500 to-amber-600 px-5 py-2.5 text-sm font-bold text-zinc-950 shadow-[0_0_15px_rgba(245,158,11,0.25)] transition hover:scale-[1.02] hover:from-amber-400 hover:to-amber-500"
		>
			<PlusCircle class="h-4 w-4 stroke-[2.5]" />
			<span>{t('dashboard.schedule_brew')}</span>
		</a>

		<a
			href="/recipes"
			class="inline-flex items-center gap-2 rounded-xl border border-zinc-200 bg-white px-4 py-2.5 text-sm font-semibold text-zinc-800 transition hover:bg-zinc-100 dark:border-white/10 dark:bg-zinc-900/60 dark:text-zinc-200 dark:hover:bg-zinc-800"
		>
			<BookOpen class="h-4 w-4" />
			<span>{t('dashboard.brew_recipe')}</span>
		</a>

		<a
			href="/recipes/new"
			class="inline-flex items-center gap-2 rounded-xl border border-zinc-200 bg-white px-4 py-2.5 text-sm font-semibold text-zinc-800 transition hover:bg-zinc-100 dark:border-white/10 dark:bg-zinc-900/60 dark:text-zinc-200 dark:hover:bg-zinc-800"
		>
			<Sparkles class="h-4 w-4 text-amber-500" />
			<span>{t('dashboard.formulate_recipe')}</span>
		</a>
	</div>
</div>
