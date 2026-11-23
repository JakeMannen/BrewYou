<script lang="ts">
	import { onMount } from 'svelte';
	import { FlaskConical, Plus, Trash2, Edit3, PlayCircle, Archive, Check, X } from '@lucide/svelte';
	import { api } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import type {
		CreateYeastBankEntryRequest,
		YeastBankEntryDto,
		YeastBankStatus
	} from '$lib/types/api';

	let entries = $state<YeastBankEntryDto[]>([]);
	let loading = $state(false);
	let error = $state<string | null>(null);
	let statusFilter = $state<'All' | YeastBankStatus>('Available');

	let showForm = $state(false);
	let editingId = $state<string | null>(null);
	let form = $state<CreateYeastBankEntryRequest & { status?: YeastBankStatus }>({
		strainName: '',
		labOrBrand: '',
		productCode: '',
		form: 'Slurry',
		generation: 1,
		volumeMl: 300,
		estimatedDensityBillionPerMl: 1.1,
		baseViabilityPercent: 95,
		storageLocation: '',
		notes: ''
	});

	async function loadEntries() {
		loading = true;
		error = null;
		try {
			entries = await api.yeastBank.list();
		} catch (err) {
			error = err instanceof Error ? err.message : t('common.error');
		} finally {
			loading = false;
		}
	}

	onMount(() => {
		void loadEntries();
	});

	function openCreate() {
		editingId = null;
		form = {
			strainName: '',
			labOrBrand: '',
			productCode: '',
			form: 'Slurry',
			generation: 1,
			volumeMl: 300,
			estimatedDensityBillionPerMl: 1.1,
			baseViabilityPercent: 95,
			storageLocation: '',
			notes: ''
		};
		showForm = true;
	}

	function openEdit(entry: YeastBankEntryDto) {
		editingId = entry.id;
		form = {
			strainName: entry.strainName,
			labOrBrand: entry.labOrBrand ?? '',
			productCode: entry.productCode ?? '',
			form: entry.form,
			generation: entry.generation,
			volumeMl: entry.volumeMl,
			estimatedDensityBillionPerMl: entry.estimatedDensityBillionPerMl,
			baseViabilityPercent: entry.baseViabilityPercent,
			storageLocation: entry.storageLocation ?? '',
			notes: entry.notes ?? '',
			status: entry.status
		};
		showForm = true;
	}

	async function saveEntry(e: Event) {
		e.preventDefault();
		if (!form.strainName.trim()) return;
		loading = true;
		error = null;
		try {
			if (editingId) {
				await api.yeastBank.update(editingId, form);
			} else {
				await api.yeastBank.create(form);
			}
			showForm = false;
			await loadEntries();
		} catch (err) {
			error = err instanceof Error ? err.message : t('common.error');
		} finally {
			loading = false;
		}
	}

	async function markDiscarded(entry: YeastBankEntryDto) {
		loading = true;
		try {
			await api.yeastBank.update(entry.id, { status: 'Discarded' });
			await loadEntries();
		} finally {
			loading = false;
		}
	}

	async function removeEntry(id: string) {
		loading = true;
		try {
			await api.yeastBank.delete(id);
			await loadEntries();
		} finally {
			loading = false;
		}
	}

	const filtered = $derived(
		statusFilter === 'All' ? entries : entries.filter((e) => e.status === statusFilter)
	);
</script>

<section class="space-y-4">
	<div class="flex flex-wrap items-center justify-between gap-3">
		<div class="flex items-center gap-2.5">
			<div
				class="flex h-9 w-9 items-center justify-center rounded-xl bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300"
			>
				<FlaskConical class="h-5 w-5" />
			</div>
			<div>
				<h2 class="text-lg font-bold text-slate-900 dark:text-white">{t('yeast.bank_title')}</h2>
				<p class="text-xs text-slate-500 dark:text-slate-400">{t('yeast.bank_subtitle')}</p>
			</div>
		</div>

		<div class="flex flex-wrap items-center gap-2">
			<div class="flex rounded-lg border border-slate-200 p-0.5 text-xs dark:border-slate-700">
				{#each ['Available', 'Depleted', 'Discarded', 'All'] as st}
					<button
						type="button"
						onclick={() => (statusFilter = st as 'All' | YeastBankStatus)}
						class="rounded-md px-2.5 py-1 font-medium transition {statusFilter === st
							? 'bg-amber-600 text-white'
							: 'text-slate-600 hover:text-slate-900 dark:text-slate-300'}"
					>
						{st === 'All' ? t('common.all') : t(`yeast.status_${st.toLowerCase()}`)}
					</button>
				{/each}
			</div>

			<button
				type="button"
				onclick={openCreate}
				class="inline-flex items-center gap-1.5 rounded-lg bg-amber-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-amber-500"
			>
				<Plus class="h-3.5 w-3.5" />
				{t('yeast.add_entry')}
			</button>
		</div>
	</div>

	{#if error}
		<div
			class="rounded-xl bg-rose-50 px-3 py-2 text-xs text-rose-700 dark:bg-rose-950/40 dark:text-rose-300"
		>
			{error}
		</div>
	{/if}

	{#if showForm}
		<form
			onsubmit={saveEntry}
			class="rounded-2xl border border-amber-200 bg-amber-50/40 p-4 dark:border-amber-800/60 dark:bg-amber-950/20"
		>
			<div class="mb-3 flex items-center justify-between">
				<h3 class="text-sm font-semibold text-slate-900 dark:text-white">
					{editingId ? t('yeast.edit_entry') : t('yeast.new_entry')}
				</h3>
				<button
					type="button"
					onclick={() => (showForm = false)}
					class="text-slate-400 hover:text-slate-600"
				>
					<X class="h-4 w-4" />
				</button>
			</div>

			<div class="grid grid-cols-1 gap-3 sm:grid-cols-3">
				<label class="text-xs font-medium text-slate-700 sm:col-span-2 dark:text-slate-300">
					{t('yeast.strain_name')}
					<input
						type="text"
						required
						bind:value={form.strainName}
						class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
					/>
				</label>
				<label class="text-xs font-medium text-slate-700 dark:text-slate-300">
					{t('yeast.form')}
					<select
						bind:value={form.form}
						class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
					>
						<option value="Slurry">{t('yeast.form_slurry')}</option>
						<option value="Liquid">{t('yeast.form_liquid')}</option>
						<option value="Dry">{t('yeast.form_dry')}</option>
					</select>
				</label>
				<label class="text-xs font-medium text-slate-700 dark:text-slate-300">
					{t('yeast.lab_brand')}
					<input
						type="text"
						bind:value={form.labOrBrand}
						class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
					/>
				</label>
				<label class="text-xs font-medium text-slate-700 dark:text-slate-300">
					{t('yeast.generation')}
					<input
						type="number"
						min="0"
						max="50"
						bind:value={form.generation}
						class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
					/>
				</label>
				<label class="text-xs font-medium text-slate-700 dark:text-slate-300">
					{t('yeast.volume_ml')}
					<input
						type="number"
						step="10"
						min="0"
						bind:value={form.volumeMl}
						class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
					/>
				</label>
				<label class="text-xs font-medium text-slate-700 dark:text-slate-300">
					{t('yeast.slurry_density')}
					<input
						type="number"
						step="0.1"
						min="0.1"
						bind:value={form.estimatedDensityBillionPerMl}
						class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
					/>
				</label>
				<label class="text-xs font-medium text-slate-700 dark:text-slate-300">
					{t('yeast.storage_location')}
					<input
						type="text"
						bind:value={form.storageLocation}
						class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
					/>
				</label>
				<label class="text-xs font-medium text-slate-700 dark:text-slate-300">
					{t('common.notes')}
					<input
						type="text"
						bind:value={form.notes}
						class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
					/>
				</label>
			</div>

			<div class="mt-3 flex justify-end gap-2">
				<button
					type="button"
					onclick={() => (showForm = false)}
					class="rounded-lg border border-slate-300 px-3 py-1.5 text-xs font-medium dark:border-slate-700"
				>
					{t('common.cancel')}
				</button>
				<button
					type="submit"
					disabled={loading}
					class="inline-flex items-center gap-1 rounded-lg bg-amber-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-amber-500"
				>
					<Check class="h-3.5 w-3.5" />
					{t('common.save')}
				</button>
			</div>
		</form>
	{/if}

	{#if filtered.length === 0 && !loading}
		<div
			class="rounded-2xl border border-dashed border-slate-300 p-8 text-center text-sm text-slate-500 dark:border-slate-700 dark:text-slate-400"
		>
			{t('yeast.bank_empty')}
		</div>
	{:else}
		<div class="grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">
			{#each filtered as entry (entry.id)}
				<div
					class="flex flex-col justify-between rounded-2xl border border-slate-200 bg-white p-4 shadow-sm dark:border-slate-800 dark:bg-slate-900"
				>
					<div>
						<div class="flex items-start justify-between gap-2">
							<div>
								<h3 class="font-bold text-slate-900 dark:text-white">{entry.strainName}</h3>
								{#if entry.labOrBrand || entry.productCode}
									<p class="text-xs text-slate-500">
										{[entry.labOrBrand, entry.productCode].filter(Boolean).join(' · ')}
									</p>
								{/if}
							</div>
							<span
								class="rounded-full bg-amber-100 px-2 py-0.5 text-[11px] font-semibold text-amber-800 dark:bg-amber-950/60 dark:text-amber-300"
							>
								Gen {entry.generation}
							</span>
						</div>

						<div
							class="mt-3 grid grid-cols-2 gap-2 rounded-xl bg-slate-50 p-2.5 text-xs dark:bg-slate-800/60"
						>
							<div>
								<span class="text-slate-500">{t('yeast.volume_ml')}:</span>
								<span class="ml-1 font-semibold text-slate-900 dark:text-white"
									>{entry.volumeMl} mL</span
								>
							</div>
							<div>
								<span class="text-slate-500">{t('yeast.viability')}:</span>
								<span
									class="ml-1 font-semibold {entry.currentEstimatedViabilityPercent >= 60
										? 'text-emerald-600'
										: 'text-amber-600'}"
								>
									{entry.currentEstimatedViabilityPercent}%
								</span>
							</div>
							<div>
								<span class="text-slate-500">{t('yeast.viable_cells')}:</span>
								<span class="ml-1 font-semibold text-slate-900 dark:text-white"
									>{entry.estimatedTotalViableCellsBillions} B</span
								>
							</div>
							<div>
								<span class="text-slate-500">{t('yeast.age')}:</span>
								<span class="ml-1 font-semibold text-slate-900 dark:text-white"
									>{entry.ageDays} d</span
								>
							</div>
						</div>

						{#if entry.sourceBatchName || entry.sourceBatchCode}
							<p class="mt-2 text-xs text-slate-600 dark:text-slate-400">
								{t('yeast.harvested_from')}:
								{#if entry.sourceBatchId}
									<a
										href={`/batches/${entry.sourceBatchId}`}
										class="font-medium text-amber-600 hover:underline"
									>
										{entry.sourceBatchCode} — {entry.sourceBatchName}
									</a>
								{:else}
									<span>{entry.sourceBatchCode} — {entry.sourceBatchName}</span>
								{/if}
							</p>
						{/if}
						{#if entry.storageLocation}
							<p class="mt-1 text-xs text-slate-500">
								{t('yeast.storage_location')}: {entry.storageLocation}
							</p>
						{/if}
					</div>

					<div
						class="mt-3 flex items-center justify-between border-t border-slate-100 pt-2.5 dark:border-slate-800"
					>
						{#if entry.status === 'Available' && entry.volumeMl > 0}
							<a
								href={`/batches/new?yeastBankId=${entry.id}`}
								class="inline-flex items-center gap-1 rounded-lg bg-emerald-600 px-2.5 py-1 text-xs font-semibold text-white hover:bg-emerald-500"
							>
								<PlayCircle class="h-3.5 w-3.5" />
								{t('yeast.repitch_in_batch')}
							</a>
						{:else}
							<span
								class="rounded bg-slate-100 px-2 py-0.5 text-[11px] font-medium text-slate-600 dark:bg-slate-800 dark:text-slate-400"
							>
								{t(`yeast.status_${entry.status.toLowerCase()}`)}
							</span>
						{/if}

						<div class="flex items-center gap-1">
							<button
								type="button"
								onclick={() => openEdit(entry)}
								class="rounded p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-slate-800"
								title={t('common.edit')}
							>
								<Edit3 class="h-3.5 w-3.5" />
							</button>
							{#if entry.status === 'Available'}
								<button
									type="button"
									onclick={() => markDiscarded(entry)}
									class="rounded p-1.5 text-slate-400 hover:bg-amber-50 hover:text-amber-600 dark:hover:bg-amber-950/40"
									title={t('yeast.mark_discarded')}
								>
									<Archive class="h-3.5 w-3.5" />
								</button>
							{/if}
							<button
								type="button"
								onclick={() => removeEntry(entry.id)}
								class="rounded p-1.5 text-slate-400 hover:bg-rose-50 hover:text-rose-600 dark:hover:bg-rose-950/40"
								title={t('common.delete')}
							>
								<Trash2 class="h-3.5 w-3.5" />
							</button>
						</div>
					</div>
				</div>
			{/each}
		</div>
	{/if}
</section>
