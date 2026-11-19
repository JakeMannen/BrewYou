<script lang="ts">
	import { onMount } from 'svelte';
	import {
		Star,
		Plus,
		Trash2,
		Edit3,
		AlertCircle,
		RotateCcw,
		Award,
		X,
		Check
	} from '@lucide/svelte';
	import { api } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import { FALLBACK_OFF_FLAVOR_CATALOG } from '$lib/calculators/off-flavors';
	import type {
		BatchTastingDto,
		BatchTastingSummaryDto,
		CreateBatchTastingRequest,
		OffFlavorDescriptorDto
	} from '$lib/types/api';

	let {
		batchId,
		tastings = [],
		summary = null,
		onChanged
	}: {
		batchId: string;
		tastings?: BatchTastingDto[];
		summary?: BatchTastingSummaryDto | null;
		onChanged?: () => Promise<void> | void;
	} = $props();

	let catalog = $state<OffFlavorDescriptorDto[]>(FALLBACK_OFF_FLAVOR_CATALOG);
	let showModal = $state(false);
	let editingId = $state<string | null>(null);
	let saving = $state(false);
	let error = $state<string | null>(null);
	let expandedOffFlavor = $state<string | null>(null);

	let form = $state<CreateBatchTastingRequest>({
		sampleSource: 'Keg',
		aromaScore: 9,
		aromaNotes: '',
		appearanceScore: 2.5,
		appearanceNotes: '',
		flavorScore: 15,
		flavorNotes: '',
		mouthfeelScore: 4,
		mouthfeelNotes: '',
		overallScore: 7.5,
		overallNotes: '',
		starRating: 4,
		wouldRebrew: true,
		offFlavors: [],
		servingTemperatureC: 6
	});

	const totalScore = $derived(
		Number(
			(
				Number(form.aromaScore || 0) +
				Number(form.appearanceScore || 0) +
				Number(form.flavorScore || 0) +
				Number(form.mouthfeelScore || 0) +
				Number(form.overallScore || 0)
			).toFixed(1)
		)
	);

	onMount(async () => {
		try {
			catalog = await api.tastings.getOffFlavors();
		} catch {
			// Keep built-in fallback catalog if offline
		}
	});

	function openCreate() {
		editingId = null;
		form = {
			sampleSource: 'Keg',
			aromaScore: 9,
			aromaNotes: '',
			appearanceScore: 2.5,
			appearanceNotes: '',
			flavorScore: 15,
			flavorNotes: '',
			mouthfeelScore: 4,
			mouthfeelNotes: '',
			overallScore: 7.5,
			overallNotes: '',
			starRating: 4,
			wouldRebrew: true,
			offFlavors: [],
			servingTemperatureC: 6
		};
		showModal = true;
	}

	function openEdit(item: BatchTastingDto) {
		editingId = item.id;
		form = {
			sampleSource: item.sampleSource ?? 'Keg',
			aromaScore: item.aromaScore,
			aromaNotes: item.aromaNotes ?? '',
			appearanceScore: item.appearanceScore,
			appearanceNotes: item.appearanceNotes ?? '',
			flavorScore: item.flavorScore,
			flavorNotes: item.flavorNotes ?? '',
			mouthfeelScore: item.mouthfeelScore,
			mouthfeelNotes: item.mouthfeelNotes ?? '',
			overallScore: item.overallScore,
			overallNotes: item.overallNotes ?? '',
			starRating: item.starRating,
			wouldRebrew: item.wouldRebrew,
			offFlavors: [...item.offFlavors],
			servingTemperatureC: item.servingTemperatureC ?? 6
		};
		showModal = true;
	}

	function toggleOffFlavor(key: string) {
		const list = form.offFlavors ?? [];
		if (list.includes(key)) {
			form.offFlavors = list.filter((k) => k !== key);
		} else {
			form.offFlavors = [...list, key];
			expandedOffFlavor = key;
		}
	}

	async function saveTasting(e: Event) {
		e.preventDefault();
		saving = true;
		error = null;
		try {
			if (editingId) {
				await api.batches.tastings.update(batchId, editingId, form);
			} else {
				await api.batches.tastings.create(batchId, form);
			}
			showModal = false;
			await onChanged?.();
		} catch (err) {
			error = err instanceof Error ? err.message : t('common.error');
		} finally {
			saving = false;
		}
	}

	async function deleteTasting(id: string) {
		await api.batches.tastings.delete(batchId, id);
		await onChanged?.();
	}
</script>

<section
	class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm dark:border-slate-800 dark:bg-slate-900"
>
	<div class="mb-4 flex flex-wrap items-center justify-between gap-3">
		<div class="flex items-center gap-2.5">
			<div
				class="flex h-9 w-9 items-center justify-center rounded-xl bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300"
			>
				<Award class="h-5 w-5" />
			</div>
			<div>
				<h3 class="text-base font-bold text-slate-900 dark:text-white">{t('tasting.title')}</h3>
				<p class="text-xs text-slate-500 dark:text-slate-400">{t('tasting.subtitle')}</p>
			</div>
		</div>

		<button
			type="button"
			onclick={openCreate}
			class="inline-flex items-center gap-1.5 rounded-lg bg-amber-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-amber-500"
		>
			<Plus class="h-3.5 w-3.5" />
			{t('tasting.log_tasting')}
		</button>
	</div>

	{#if summary && summary.count > 0}
		<div
			class="mb-4 grid grid-cols-2 gap-3 rounded-xl bg-slate-50 p-3 sm:grid-cols-4 dark:bg-slate-800/70"
		>
			<div>
				<div class="text-xs text-slate-500">{t('tasting.avg_rating')}</div>
				<div class="mt-0.5 flex items-center gap-1 text-base font-bold text-amber-600">
					<Star class="h-4 w-4 fill-amber-500 text-amber-500" />
					{summary.averageStarRating ?? '—'} / 5
				</div>
			</div>
			<div>
				<div class="text-xs text-slate-500">{t('tasting.avg_bjcp')}</div>
				<div class="mt-0.5 text-base font-bold text-slate-900 dark:text-white">
					{summary.averageBjcpScore ?? '—'} / 50
				</div>
			</div>
			<div>
				<div class="text-xs text-slate-500">{t('tasting.would_rebrew')}</div>
				<div
					class="mt-0.5 flex items-center gap-1 text-sm font-semibold {summary.latestWouldRebrew
						? 'text-emerald-600'
						: 'text-slate-500'}"
				>
					<RotateCcw class="h-3.5 w-3.5" />
					{summary.latestWouldRebrew ? t('common.yes') : t('common.no')}
				</div>
			</div>
			<div>
				<div class="text-xs text-slate-500">{t('tasting.sessions')}</div>
				<div class="mt-0.5 text-base font-bold text-slate-900 dark:text-white">{summary.count}</div>
			</div>
		</div>
	{/if}

	{#if tastings.length === 0}
		<div
			class="rounded-xl border border-dashed border-slate-300 p-6 text-center text-xs text-slate-500 dark:border-slate-700 dark:text-slate-400"
		>
			{t('tasting.empty')}
		</div>
	{:else}
		<div class="space-y-3">
			{#each tastings as item (item.id)}
				<div class="rounded-xl border border-slate-200 p-4 dark:border-slate-800">
					<div class="flex flex-wrap items-start justify-between gap-2">
						<div>
							<div class="flex flex-wrap items-center gap-2">
								<span
									class="rounded-lg bg-amber-100 px-2 py-0.5 text-xs font-bold text-amber-800 dark:bg-amber-950/60 dark:text-amber-300"
								>
									{item.totalScore} / 50 ({t(`tasting.band_${item.bjcpScoreBand}`)})
								</span>
								<div class="flex items-center gap-0.5">
									{#each [1, 2, 3, 4, 5] as s}
										<Star
											class="h-3.5 w-3.5 {s <= item.starRating
												? 'fill-amber-400 text-amber-400'
												: 'text-slate-300 dark:text-slate-700'}"
										/>
									{/each}
								</div>
								<span class="text-xs text-slate-500">
									{new Date(item.tastedAt).toLocaleDateString()} · {item.sampleSource ?? 'Keg'}
								</span>
								{#if item.wouldRebrew}
									<span
										class="rounded bg-emerald-100 px-2 py-0.5 text-[10px] font-semibold text-emerald-700 dark:bg-emerald-950/50 dark:text-emerald-300"
									>
										{t('tasting.would_rebrew')}
									</span>
								{/if}
							</div>
						</div>

						<div class="flex items-center gap-1">
							<button
								type="button"
								onclick={() => openEdit(item)}
								class="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-slate-800"
								title={t('common.edit')}
							>
								<Edit3 class="h-3.5 w-3.5" />
							</button>
							<button
								type="button"
								onclick={() => deleteTasting(item.id)}
								class="rounded p-1 text-slate-400 hover:bg-rose-50 hover:text-rose-600 dark:hover:bg-rose-950/40"
								title={t('common.delete')}
							>
								<Trash2 class="h-3.5 w-3.5" />
							</button>
						</div>
					</div>

					<div
						class="mt-2 grid grid-cols-2 gap-2 text-[11px] text-slate-600 sm:grid-cols-5 dark:text-slate-400"
					>
						<div><strong>{t('tasting.aroma')}:</strong> {item.aromaScore}/12</div>
						<div><strong>{t('tasting.appearance')}:</strong> {item.appearanceScore}/3</div>
						<div><strong>{t('tasting.flavor')}:</strong> {item.flavorScore}/20</div>
						<div><strong>{t('tasting.mouthfeel')}:</strong> {item.mouthfeelScore}/5</div>
						<div><strong>{t('tasting.overall')}:</strong> {item.overallScore}/10</div>
					</div>

					{#if item.aromaNotes || item.flavorNotes || item.overallNotes}
						<p class="mt-2 text-xs text-slate-700 dark:text-slate-300">
							{[item.aromaNotes, item.flavorNotes, item.overallNotes].filter(Boolean).join(' · ')}
						</p>
					{/if}

					{#if item.offFlavors.length > 0}
						<div class="mt-2.5 space-y-1.5">
							<div class="flex flex-wrap gap-1.5">
								{#each item.offFlavors as flag}
									{@const meta = catalog.find((c) => c.key === flag)}
									<button
										type="button"
										onclick={() =>
											(expandedOffFlavor =
												expandedOffFlavor === `${item.id}:${flag}` ? null : `${item.id}:${flag}`)}
										class="inline-flex items-center gap-1 rounded-full bg-rose-100 px-2.5 py-0.5 text-[11px] font-semibold text-rose-700 dark:bg-rose-950/60 dark:text-rose-300"
									>
										<AlertCircle class="h-3 w-3" />
										{meta?.displayName ?? flag}
									</button>
								{/each}
							</div>
							{#each item.offFlavors as flag}
								{@const meta = catalog.find((c) => c.key === flag)}
								{#if meta && expandedOffFlavor === `${item.id}:${flag}`}
									<div
										class="rounded-lg bg-rose-50/70 p-2.5 text-xs text-rose-900 dark:bg-rose-950/30 dark:text-rose-200"
									>
										<p class="font-semibold">{meta.displayName}: {meta.sensoryCue}</p>
										<p class="mt-1 font-medium">{t('tasting.likely_causes')}:</p>
										<ul class="ml-4 list-disc">
											{#each meta.likelyCauses as c}<li>{c}</li>{/each}
										</ul>
										<p class="mt-1 font-medium">{t('tasting.remediation_fixes')}:</p>
										<ul class="ml-4 list-disc">
											{#each meta.remediationFixes as f}<li>{f}</li>{/each}
										</ul>
									</div>
								{/if}
							{/each}
						</div>
					{/if}
				</div>
			{/each}
		</div>
	{/if}

	{#if showModal}
		<div
			class="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 p-4 backdrop-blur-sm"
		>
			<form
				onsubmit={saveTasting}
				class="max-h-[90vh] w-full max-w-2xl overflow-y-auto rounded-2xl border border-slate-200 bg-white p-5 shadow-2xl dark:border-slate-800 dark:bg-slate-900"
			>
				<div class="mb-4 flex items-center justify-between">
					<h4 class="text-base font-bold text-slate-900 dark:text-white">
						{editingId ? t('tasting.edit_tasting') : t('tasting.log_tasting')} — {totalScore} / 50
					</h4>
					<button
						type="button"
						onclick={() => (showModal = false)}
						class="text-slate-400 hover:text-slate-600"
					>
						<X class="h-5 w-5" />
					</button>
				</div>

				{#if error}
					<div class="mb-3 rounded-lg bg-rose-50 px-3 py-2 text-xs text-rose-700">{error}</div>
				{/if}

				<div class="grid grid-cols-1 gap-3 sm:grid-cols-3">
					<label class="text-xs font-medium text-slate-600 dark:text-slate-400">
						{t('tasting.sample_source')}
						<select
							bind:value={form.sampleSource}
							class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
						>
							<option value="HydrometerSample">Hydrometer Sample</option>
							<option value="BrightTank">Bright Tank</option>
							<option value="Keg">Keg / Draft</option>
							<option value="Bottle">Bottle</option>
							<option value="Can">Can</option>
						</select>
					</label>

					<div>
						<span class="block text-xs font-medium text-slate-600 dark:text-slate-400"
							>{t('tasting.star_rating')}</span
						>
						<div class="mt-1.5 flex items-center gap-1">
							{#each [1, 2, 3, 4, 5] as star}
								<button type="button" onclick={() => (form.starRating = star)}>
									<Star
										class="h-5 w-5 {star <= form.starRating
											? 'fill-amber-400 text-amber-400'
											: 'text-slate-300 dark:text-slate-600'}"
									/>
								</button>
							{/each}
						</div>
					</div>

					<label
						class="flex items-center gap-2 pt-5 text-xs font-semibold text-slate-800 dark:text-slate-200"
					>
						<input type="checkbox" bind:checked={form.wouldRebrew} class="rounded text-amber-600" />
						{t('tasting.would_rebrew')}
					</label>
				</div>

				<div class="mt-4 space-y-3">
					<div class="grid grid-cols-1 gap-2 sm:grid-cols-4">
						<label class="text-xs font-medium"
							>{t('tasting.aroma')} (0–12)
							<input
								type="number"
								step="0.5"
								min="0"
								max="12"
								bind:value={form.aromaScore}
								class="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/>
						</label>
						<label class="text-xs font-medium sm:col-span-3"
							>{t('common.notes')}
							<input
								type="text"
								bind:value={form.aromaNotes}
								class="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/>
						</label>
					</div>

					<div class="grid grid-cols-1 gap-2 sm:grid-cols-4">
						<label class="text-xs font-medium"
							>{t('tasting.appearance')} (0–3)
							<input
								type="number"
								step="0.5"
								min="0"
								max="3"
								bind:value={form.appearanceScore}
								class="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/>
						</label>
						<label class="text-xs font-medium sm:col-span-3"
							>{t('common.notes')}
							<input
								type="text"
								bind:value={form.appearanceNotes}
								class="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/>
						</label>
					</div>

					<div class="grid grid-cols-1 gap-2 sm:grid-cols-4">
						<label class="text-xs font-medium"
							>{t('tasting.flavor')} (0–20)
							<input
								type="number"
								step="0.5"
								min="0"
								max="20"
								bind:value={form.flavorScore}
								class="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/>
						</label>
						<label class="text-xs font-medium sm:col-span-3"
							>{t('common.notes')}
							<input
								type="text"
								bind:value={form.flavorNotes}
								class="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/>
						</label>
					</div>

					<div class="grid grid-cols-1 gap-2 sm:grid-cols-4">
						<label class="text-xs font-medium"
							>{t('tasting.mouthfeel')} (0–5)
							<input
								type="number"
								step="0.5"
								min="0"
								max="5"
								bind:value={form.mouthfeelScore}
								class="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/>
						</label>
						<label class="text-xs font-medium sm:col-span-3"
							>{t('common.notes')}
							<input
								type="text"
								bind:value={form.mouthfeelNotes}
								class="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/>
						</label>
					</div>

					<div class="grid grid-cols-1 gap-2 sm:grid-cols-4">
						<label class="text-xs font-medium"
							>{t('tasting.overall')} (0–10)
							<input
								type="number"
								step="0.5"
								min="0"
								max="10"
								bind:value={form.overallScore}
								class="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/>
						</label>
						<label class="text-xs font-medium sm:col-span-3"
							>{t('common.notes')}
							<input
								type="text"
								bind:value={form.overallNotes}
								class="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/>
						</label>
					</div>
				</div>

				<div class="mt-4">
					<div class="mb-2 text-xs font-bold text-slate-700 dark:text-slate-300">
						{t('tasting.off_flavor_checklist')}
					</div>
					<div class="flex flex-wrap gap-2">
						{#each catalog as desc (desc.key)}
							{@const active = (form.offFlavors ?? []).includes(desc.key)}
							<button
								type="button"
								onclick={() => toggleOffFlavor(desc.key)}
								class="rounded-full border px-2.5 py-1 text-xs font-medium transition {active
									? 'border-rose-500 bg-rose-100 text-rose-800 dark:border-rose-700 dark:bg-rose-950/60 dark:text-rose-200'
									: 'border-slate-300 text-slate-600 hover:bg-slate-50 dark:border-slate-700 dark:text-slate-300'}"
							>
								{desc.displayName}
							</button>
						{/each}
					</div>
					{#if (form.offFlavors ?? []).length > 0}
						<div class="mt-2 space-y-2">
							{#each form.offFlavors ?? [] as key}
								{@const meta = catalog.find((c) => c.key === key)}
								{#if meta}
									<div
										class="rounded-lg bg-rose-50/70 p-2.5 text-xs text-rose-900 dark:bg-rose-950/30 dark:text-rose-200"
									>
										<p class="font-semibold">{meta.displayName} — {meta.sensoryCue}</p>
										<p class="mt-1">
											<strong>{t('tasting.likely_causes')}:</strong>
											{meta.likelyCauses.join(' ')}
										</p>
										<p class="mt-0.5">
											<strong>{t('tasting.remediation_fixes')}:</strong>
											{meta.remediationFixes.join(' ')}
										</p>
									</div>
								{/if}
							{/each}
						</div>
					{/if}
				</div>

				<div class="mt-5 flex justify-end gap-2">
					<button
						type="button"
						onclick={() => (showModal = false)}
						class="rounded-lg border border-slate-300 px-3 py-1.5 text-xs font-medium dark:border-slate-700"
					>
						{t('common.cancel')}
					</button>
					<button
						type="submit"
						disabled={saving}
						class="inline-flex items-center gap-1 rounded-lg bg-amber-600 px-4 py-1.5 text-xs font-semibold text-white hover:bg-amber-500 disabled:opacity-50"
					>
						<Check class="h-3.5 w-3.5" />
						{t('common.save')}
					</button>
				</div>
			</form>
		</div>
	{/if}
</section>
