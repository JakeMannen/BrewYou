<script lang="ts">
	import { onMount } from 'svelte';
	import { Droplets, Plus, Trash2, Lock, Edit3, Check, X } from '@lucide/svelte';
	import { api } from '$lib/api/client';
	import { auth } from '$lib/stores/auth.svelte';
	import { t } from '$lib/i18n/index.svelte';
	import type {
		CreateWaterProfileRequest,
		WaterProfileCategory,
		WaterProfileDto
	} from '$lib/types/api';

	let { class: className = '' }: { class?: string } = $props();

	let profiles = $state<WaterProfileDto[]>([]);
	let loading = $state(false);
	let error = $state<string | null>(null);
	let categoryFilter = $state<'All' | WaterProfileCategory>('All');

	let showForm = $state(false);
	let editingId = $state<string | null>(null);
	let form = $state<CreateWaterProfileRequest>({
		name: '',
		category: 'Source',
		description: '',
		calciumPpm: 25,
		magnesiumPpm: 5,
		sodiumPpm: 12,
		sulfatePpm: 20,
		chloridePpm: 22,
		bicarbonatePpm: 60,
		ph: 7.2
	});

	async function loadProfiles() {
		if (!auth.isAuthenticated) return;
		loading = true;
		error = null;
		try {
			profiles = await api.waterProfiles.list();
		} catch (err) {
			error = err instanceof Error ? err.message : t('common.error');
		} finally {
			loading = false;
		}
	}

	onMount(() => {
		void loadProfiles();
	});

	function openCreate() {
		editingId = null;
		form = {
			name: '',
			category: 'Source',
			description: '',
			calciumPpm: 25,
			magnesiumPpm: 5,
			sodiumPpm: 12,
			sulfatePpm: 20,
			chloridePpm: 22,
			bicarbonatePpm: 60,
			ph: 7.2
		};
		showForm = true;
	}

	function openEdit(profile: WaterProfileDto) {
		editingId = profile.id;
		form = {
			name: profile.name,
			category: profile.category,
			description: profile.description ?? '',
			calciumPpm: profile.calciumPpm,
			magnesiumPpm: profile.magnesiumPpm,
			sodiumPpm: profile.sodiumPpm,
			sulfatePpm: profile.sulfatePpm,
			chloridePpm: profile.chloridePpm,
			bicarbonatePpm: profile.bicarbonatePpm,
			ph: profile.ph ?? 7.0
		};
		showForm = true;
	}

	async function saveProfile(e: Event) {
		e.preventDefault();
		if (!form.name.trim()) return;
		loading = true;
		error = null;
		try {
			if (editingId) {
				await api.waterProfiles.update(editingId, form);
			} else {
				await api.waterProfiles.create(form);
			}
			showForm = false;
			await loadProfiles();
		} catch (err) {
			error = err instanceof Error ? err.message : t('common.error');
		} finally {
			loading = false;
		}
	}

	async function deleteProfile(id: string) {
		loading = true;
		error = null;
		try {
			await api.waterProfiles.delete(id);
			await loadProfiles();
		} catch (err) {
			error = err instanceof Error ? err.message : t('common.error');
		} finally {
			loading = false;
		}
	}

	const filteredProfiles = $derived(
		categoryFilter === 'All' ? profiles : profiles.filter((p) => p.category === categoryFilter)
	);
</script>

<section
	class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm dark:border-slate-800 dark:bg-slate-900 {className}"
>
	<div class="mb-4 flex flex-wrap items-center justify-between gap-3">
		<div class="flex items-center gap-2.5">
			<div
				class="flex h-9 w-9 items-center justify-center rounded-xl bg-sky-100 text-sky-700 dark:bg-sky-900/40 dark:text-sky-300"
			>
				<Droplets class="h-5 w-5" />
			</div>
			<div>
				<h3 class="text-base font-bold text-slate-900 dark:text-white">
					{t('water.library_title')}
				</h3>
				<p class="text-xs text-slate-500 dark:text-slate-400">
					{t('water.library_subtitle')}
				</p>
			</div>
		</div>

		{#if auth.isAuthenticated}
			<div class="flex items-center gap-2">
				<div class="flex rounded-lg border border-slate-200 p-0.5 text-xs dark:border-slate-700">
					{#each ['All', 'Source', 'Target'] as cat}
						<button
							type="button"
							onclick={() => (categoryFilter = cat as 'All' | WaterProfileCategory)}
							class="rounded-md px-2.5 py-1 font-medium transition {categoryFilter === cat
								? 'bg-sky-600 text-white'
								: 'text-slate-600 hover:text-slate-900 dark:text-slate-300'}"
						>
							{cat === 'All'
								? t('common.all')
								: cat === 'Source'
									? t('water.category_source')
									: t('water.category_target')}
						</button>
					{/each}
				</div>

				<button
					type="button"
					onclick={openCreate}
					class="inline-flex items-center gap-1.5 rounded-lg bg-sky-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-sky-500"
				>
					<Plus class="h-3.5 w-3.5" />
					{t('water.add_profile')}
				</button>
			</div>
		{/if}
	</div>

	{#if !auth.isAuthenticated}
		<p class="text-xs text-slate-500 dark:text-slate-400">{t('water.sign_in_for_library')}</p>
	{:else}
		{#if error}
			<div
				class="mb-3 rounded-lg bg-rose-50 px-3 py-2 text-xs text-rose-700 dark:bg-rose-950/40 dark:text-rose-300"
			>
				{error}
			</div>
		{/if}

		{#if showForm}
			<form
				onsubmit={saveProfile}
				class="mb-4 rounded-xl border border-sky-200 bg-sky-50/50 p-4 dark:border-sky-800/60 dark:bg-sky-950/20"
			>
				<div class="mb-3 flex items-center justify-between">
					<h4 class="text-sm font-semibold text-slate-900 dark:text-white">
						{editingId ? t('water.edit_profile') : t('water.new_profile')}
					</h4>
					<button
						type="button"
						onclick={() => (showForm = false)}
						class="text-slate-400 hover:text-slate-600"
					>
						<X class="h-4 w-4" />
					</button>
				</div>

				<div class="grid grid-cols-1 gap-3 sm:grid-cols-3">
					<div class="sm:col-span-2">
						<label class="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-300">
							{t('common.name')}
							<input
								type="text"
								required
								bind:value={form.name}
								class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							/>
						</label>
					</div>
					<div>
						<label class="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-300">
							{t('water.category')}
							<select
								bind:value={form.category}
								class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
							>
								<option value="Source">{t('water.category_source')}</option>
								<option value="Target">{t('water.category_target')}</option>
							</select>
						</label>
					</div>
				</div>

				<div class="mt-2 grid grid-cols-2 gap-2.5 sm:grid-cols-7">
					<label class="text-xs text-slate-600 dark:text-slate-300"
						>Ca²⁺ (ppm)
						<input
							type="number"
							step="0.1"
							min="0"
							bind:value={form.calciumPpm}
							class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-2 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
						/>
					</label>
					<label class="text-xs text-slate-600 dark:text-slate-300"
						>Mg²⁺ (ppm)
						<input
							type="number"
							step="0.1"
							min="0"
							bind:value={form.magnesiumPpm}
							class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-2 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
						/>
					</label>
					<label class="text-xs text-slate-600 dark:text-slate-300"
						>Na⁺ (ppm)
						<input
							type="number"
							step="0.1"
							min="0"
							bind:value={form.sodiumPpm}
							class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-2 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
						/>
					</label>
					<label class="text-xs text-slate-600 dark:text-slate-300"
						>SO₄²⁻ (ppm)
						<input
							type="number"
							step="0.1"
							min="0"
							bind:value={form.sulfatePpm}
							class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-2 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
						/>
					</label>
					<label class="text-xs text-slate-600 dark:text-slate-300"
						>Cl⁻ (ppm)
						<input
							type="number"
							step="0.1"
							min="0"
							bind:value={form.chloridePpm}
							class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-2 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
						/>
					</label>
					<label class="text-xs text-slate-600 dark:text-slate-300"
						>HCO₃⁻ (ppm)
						<input
							type="number"
							step="0.1"
							min="0"
							bind:value={form.bicarbonatePpm}
							class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-2 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
						/>
					</label>
					<label class="text-xs text-slate-600 dark:text-slate-300"
						>pH
						<input
							type="number"
							step="0.1"
							min="3"
							max="11"
							bind:value={form.ph}
							class="mt-1 w-full rounded-lg border border-slate-300 bg-white px-2 py-1 text-sm dark:border-slate-700 dark:bg-slate-800 dark:text-white"
						/>
					</label>
				</div>

				<div class="mt-3 flex justify-end gap-2">
					<button
						type="button"
						onclick={() => (showForm = false)}
						class="rounded-lg border border-slate-300 px-3 py-1.5 text-xs font-medium text-slate-700 dark:border-slate-700 dark:text-slate-300"
					>
						{t('common.cancel')}
					</button>
					<button
						type="submit"
						disabled={loading}
						class="inline-flex items-center gap-1 rounded-lg bg-sky-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-sky-500 disabled:opacity-50"
					>
						<Check class="h-3.5 w-3.5" />
						{t('common.save')}
					</button>
				</div>
			</form>
		{/if}

		<div class="overflow-x-auto">
			<table class="w-full text-left text-xs">
				<thead
					class="border-b border-slate-200 text-slate-500 dark:border-slate-800 dark:text-slate-400"
				>
					<tr>
						<th class="py-2 pr-3 font-semibold">{t('common.name')}</th>
						<th class="px-2 py-2 font-semibold">{t('water.category')}</th>
						<th class="px-2 py-2 font-semibold">Ca</th>
						<th class="px-2 py-2 font-semibold">Mg</th>
						<th class="px-2 py-2 font-semibold">Na</th>
						<th class="px-2 py-2 font-semibold">SO₄</th>
						<th class="px-2 py-2 font-semibold">Cl</th>
						<th class="px-2 py-2 font-semibold">HCO₃</th>
						<th class="px-2 py-2 font-semibold">SO₄:Cl</th>
						<th class="px-2 py-2 font-semibold">RA</th>
						<th class="py-2 pl-2 text-right font-semibold">{t('common.actions')}</th>
					</tr>
				</thead>
				<tbody class="divide-y divide-slate-100 dark:divide-slate-800/80">
					{#each filteredProfiles as profile (profile.id)}
						<tr class="hover:bg-slate-50/70 dark:hover:bg-slate-800/40">
							<td class="py-2 pr-3 font-medium text-slate-900 dark:text-white">
								<div class="flex items-center gap-1.5">
									<span>{profile.name}</span>
									{#if profile.isPreset}
										<span
											class="inline-flex items-center gap-0.5 rounded bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium text-slate-600 dark:bg-slate-800 dark:text-slate-400"
										>
											<Lock class="h-2.5 w-2.5" />
											{t('water.preset')}
										</span>
									{/if}
								</div>
							</td>
							<td class="px-2 py-2">
								<span
									class="rounded-full px-2 py-0.5 text-[10px] font-semibold {profile.category ===
									'Source'
										? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-300'
										: 'bg-sky-100 text-sky-700 dark:bg-sky-950/60 dark:text-sky-300'}"
								>
									{profile.category === 'Source'
										? t('water.category_source')
										: t('water.category_target')}
								</span>
							</td>
							<td class="px-2 py-2 tabular-nums">{profile.calciumPpm}</td>
							<td class="px-2 py-2 tabular-nums">{profile.magnesiumPpm}</td>
							<td class="px-2 py-2 tabular-nums">{profile.sodiumPpm}</td>
							<td class="px-2 py-2 tabular-nums">{profile.sulfatePpm}</td>
							<td class="px-2 py-2 tabular-nums">{profile.chloridePpm}</td>
							<td class="px-2 py-2 tabular-nums">{profile.bicarbonatePpm}</td>
							<td class="px-2 py-2 font-medium tabular-nums">
								{profile.sulfateToChlorideRatio != null
									? `${profile.sulfateToChlorideRatio}:1`
									: '—'}
							</td>
							<td class="px-2 py-2 tabular-nums">{profile.residualAlkalinityPpm}</td>
							<td class="py-2 pl-2 text-right">
								{#if !profile.isPreset}
									<button
										type="button"
										onclick={() => openEdit(profile)}
										class="mr-1 rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-slate-800"
										title={t('common.edit')}
									>
										<Edit3 class="h-3.5 w-3.5" />
									</button>
									<button
										type="button"
										onclick={() => deleteProfile(profile.id)}
										class="rounded p-1 text-slate-400 hover:bg-rose-50 hover:text-rose-600 dark:hover:bg-rose-950/40"
										title={t('common.delete')}
									>
										<Trash2 class="h-3.5 w-3.5" />
									</button>
								{/if}
							</td>
						</tr>
					{/each}
				</tbody>
			</table>
		</div>
	{/if}
</section>
