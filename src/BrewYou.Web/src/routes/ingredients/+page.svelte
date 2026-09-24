<script lang="ts">
	import { onMount } from 'svelte';
	import { api } from '$lib/api/client';
	import type { IngredientDto, IngredientType } from '$lib/types/api';
	import {
		Layers,
		Search,
		Loader2,
		Plus,
		PackageCheck,
		X,
		Check,
		Trash2,
		Pencil
	} from '@lucide/svelte';
	import { srmToHexColor, sgToPpg } from '$lib/calculators/brewing';
	import { t, i18n } from '$lib/i18n/index.svelte';
	import { formatNumber } from '$lib/utils/formatNumber';
	import {
		getLocalizedIngredientName,
		getLocalizedIngredientDescription,
		getLocalizedIngredientType,
		getLocalizedIngredientForm
	} from '$lib/i18n/ingredients';
	import IngredientModal from '$lib/components/ingredients/IngredientModal.svelte';
	import DeleteIngredientModal from '$lib/components/ingredients/DeleteIngredientModal.svelte';
	import type { IngredientUsageDto } from '$lib/types/api';

	let ingredients = $state<IngredientDto[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);
	let selectedType = $state<string>('All');
	let filterInStock = $state(false);
	let searchTerm = $state<string>('');
	let modalOpen = $state(false);

	// Delete ingredient state
	let deleteModalOpen = $state(false);
	let deletingIngredient = $state<IngredientDto | null>(null);
	let ingredientUsage = $state<IngredientUsageDto | null>(null);
	let isLoadingUsage = $state(false);

	// Quick stock update state
	let editingStockItem = $state<IngredientDto | null>(null);
	let newStockAmount = $state<number | ''>('');
	let newStockUnit = $state<string>('kg');
	let updatingStock = $state(false);

	function handleIngredientSaved(newItem: IngredientDto) {
		ingredients = [newItem, ...ingredients];
		if (selectedType !== 'All' && selectedType !== newItem.type) {
			selectedType = newItem.type;
		}
	}

	async function loadIngredients() {
		loading = true;
		error = null;
		try {
			const typeFilter = selectedType === 'All' ? undefined : (selectedType as IngredientType);
			ingredients = await api.ingredients.list(
				typeFilter,
				undefined,
				undefined,
				200,
				filterInStock ? true : undefined
			);
		} catch (err: unknown) {
			error = (err as Error).message || t('ingredients.load_error');
		} finally {
			loading = false;
		}
	}

	onMount(() => {
		loadIngredients();
	});

	function onTypeChange(type: string) {
		selectedType = type;
		loadIngredients();
	}

	function onToggleInStock() {
		filterInStock = !filterInStock;
		loadIngredients();
	}

	function openStockEditor(item: IngredientDto) {
		editingStockItem = item;
		newStockAmount = item.stockAmount ?? 0;
		newStockUnit =
			item.stockUnit || (item.type === 'Hop' ? 'g' : item.type === 'Yeast' ? 'pkg' : 'kg');
	}

	async function handleSaveStock() {
		if (!editingStockItem) return;
		updatingStock = true;
		try {
			const amount = typeof newStockAmount === 'number' && newStockAmount >= 0 ? newStockAmount : 0;
			const updated = await api.ingredients.updateStock(editingStockItem.id, {
				amount,
				unit: newStockUnit
			});
			ingredients = ingredients.map((i) => (i.id === updated.id ? updated : i));
			editingStockItem = null;
		} catch (err: unknown) {
			error = (err as Error).message;
		} finally {
			updatingStock = false;
		}
	}

	async function openDeleteModal(item: IngredientDto) {
		deletingIngredient = item;
		ingredientUsage = null;
		isLoadingUsage = true;
		deleteModalOpen = true;

		try {
			ingredientUsage = await api.ingredients.getUsage(item.id);
		} catch (err: unknown) {
			console.error('Failed to load ingredient usage:', err);
		} finally {
			isLoadingUsage = false;
		}
	}

	function handleIngredientDeleted(deletedId: string) {
		ingredients = ingredients.filter((i) => i.id !== deletedId);
	}

	let filteredIngredients = $derived(
		ingredients
			.filter((item) => {
				if (!searchTerm.trim()) return true;
				const q = searchTerm.toLowerCase().trim();
				const localizedName = getLocalizedIngredientName(item).toLowerCase();
				const localizedDesc = getLocalizedIngredientDescription(item).toLowerCase();
				const rawName = item.name.toLowerCase();
				const rawDesc = (item.description ?? '').toLowerCase();
				return (
					localizedName.includes(q) ||
					localizedDesc.includes(q) ||
					rawName.includes(q) ||
					rawDesc.includes(q)
				);
			})
			.slice()
			.sort((a, b) => {
				const nameA = getLocalizedIngredientName(a);
				const nameB = getLocalizedIngredientName(b);
				return nameA.localeCompare(nameB, i18n.locale, { sensitivity: 'base' });
			})
	);

	function getTypeBadgeClass(type: string): string {
		switch (type) {
			case 'Fermentable':
				return 'border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400';
			case 'Hop':
				return 'border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400';
			case 'Yeast':
				return 'border-sky-500/30 bg-sky-500/10 text-sky-600 dark:text-sky-400';
			default:
				return 'border-purple-500/30 bg-purple-500/10 text-purple-600 dark:text-purple-400';
		}
	}
</script>

<div class="space-y-6">
	<!-- Header -->
	<div class="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
		<div>
			<div class="flex items-center gap-2.5">
				<div
					class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-500"
				>
					<Layers class="h-5 w-5" />
				</div>
				<h1 class="text-2xl font-bold tracking-tight text-zinc-900 sm:text-3xl dark:text-white">
					{t('ingredients.title')}
				</h1>
			</div>
			<p class="mt-1 text-sm text-zinc-600 dark:text-zinc-400">
				{t('ingredients.subtitle')}
			</p>
		</div>

		<div>
			<button
				type="button"
				onclick={() => (modalOpen = true)}
				class="flex items-center gap-2 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-4 py-2.5 text-xs font-bold text-zinc-950 shadow-md transition-all hover:from-amber-400 hover:to-amber-500 active:scale-98 sm:text-sm"
			>
				<Plus class="h-4 w-4 stroke-[2.5]" />
				<span>{t('ingredients.add_ingredient')}</span>
			</button>
		</div>
	</div>

	<!-- Filters & Search Toolbar -->
	<div
		class="glass-panel flex flex-col items-stretch justify-between gap-4 rounded-xl border border-zinc-200/80 p-3 sm:flex-row sm:items-center dark:border-white/[0.08]"
	>
		<!-- Type Filter Tabs & In-Stock Toggle -->
		<div class="flex flex-wrap items-center gap-2">
			<div
				role="tablist"
				aria-label="Ingredient type filter"
				class="flex items-center gap-1.5 overflow-x-auto rounded-xl border border-zinc-200/80 bg-zinc-100/70 p-1 text-xs dark:border-white/5 dark:bg-zinc-900/60"
			>
				{#each ['All', 'Fermentable', 'Hop', 'Yeast', 'Other'] as const as type}
					<button
						type="button"
						role="tab"
						aria-selected={selectedType === type}
						onclick={() => onTypeChange(type)}
						class="min-h-[38px] rounded-lg px-3.5 py-1.5 font-semibold whitespace-nowrap transition-colors {selectedType ===
						type
							? 'bg-white text-zinc-950 shadow-xs dark:bg-zinc-800 dark:text-white'
							: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-200'}"
					>
						{type === 'All'
							? t('ingredients.all_ingredients')
							: type === 'Fermentable'
								? t('ingredients.fermentables')
								: type === 'Hop'
									? t('ingredients.hops')
									: type === 'Yeast'
										? t('ingredients.yeasts')
										: t('ingredients.others')}
					</button>
				{/each}
			</div>

			<!-- In-Stock Filter Toggle -->
			<button
				type="button"
				onclick={onToggleInStock}
				class="flex min-h-[38px] items-center gap-1.5 rounded-xl border px-3 py-1.5 text-xs font-semibold whitespace-nowrap transition-colors {filterInStock
					? 'border-emerald-500/50 bg-emerald-500/10 text-emerald-700 dark:text-emerald-400'
					: 'border-zinc-200/80 bg-zinc-100/70 text-zinc-600 hover:text-zinc-900 dark:border-white/5 dark:bg-zinc-900/60 dark:text-zinc-400 dark:hover:text-zinc-200'}"
			>
				<PackageCheck class="h-3.5 w-3.5" />
				<span>{t('ingredients.filter_in_stock')}</span>
			</button>
		</div>

		<!-- Search Bar -->
		<div class="relative w-full sm:w-72">
			<Search
				class="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-zinc-400"
			/>
			<input
				type="text"
				placeholder={t('ingredients.search_placeholder')}
				bind:value={searchTerm}
				aria-label={t('ingredients.search_placeholder')}
				class="h-10 w-full rounded-xl border border-zinc-200/80 bg-white pr-3 pl-9 text-sm text-zinc-900 placeholder-zinc-400 transition-colors focus:border-amber-500/60 focus:ring-2 focus:ring-amber-500/20 focus:outline-none dark:border-white/10 dark:bg-zinc-900/80 dark:text-zinc-100 dark:placeholder-zinc-500"
			/>
		</div>
	</div>

	<!-- Ingredient Grid -->
	{#if loading}
		<div class="flex flex-col items-center justify-center gap-3 py-16 text-zinc-400">
			<Loader2 class="h-8 w-8 animate-spin text-amber-500" />
			<span class="text-sm text-zinc-600 dark:text-zinc-400">{t('common.loading')}</span>
		</div>
	{:else if error}
		<div
			class="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
		>
			{error}
		</div>
	{:else if filteredIngredients.length === 0}
		<div
			class="glass-panel flex flex-col items-center justify-center rounded-3xl border border-zinc-200/80 py-16 text-center dark:border-white/[0.08]"
		>
			<div
				class="flex h-12 w-12 items-center justify-center rounded-2xl border border-zinc-200 bg-zinc-100 text-zinc-400 dark:border-zinc-800 dark:bg-zinc-900"
			>
				<Layers class="h-6 w-6" />
			</div>
			<h3 class="mt-4 text-base font-bold text-zinc-900 dark:text-white">
				{t('ingredients.empty')}
			</h3>
		</div>
	{:else}
		<div class="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
			{#each filteredIngredients as item (item.id)}
				<article
					class="glass-panel group relative flex flex-col justify-between rounded-2xl border border-zinc-200/80 p-5 transition-all duration-200 hover:border-amber-500/40 hover:shadow-lg dark:border-white/[0.08]"
					data-testid="ingredient-card-{item.id}"
				>
					<div class="space-y-3">
						<div class="space-y-2">
							<div
								class="flex items-start justify-between gap-2 border-b border-zinc-200/60 pb-3 dark:border-white/5"
							>
								<h3 class="text-base leading-snug font-bold text-zinc-900 dark:text-white">
									{getLocalizedIngredientName(item)}
								</h3>
								<div class="flex flex-wrap items-center justify-end gap-1.5">
									<span
										class="inline-block rounded-md border px-2 py-0.5 text-[10px] font-bold tracking-wider uppercase {getTypeBadgeClass(
											item.type
										)}"
									>
										{getLocalizedIngredientType(item.type)}
									</span>
									{#if item.form}
										<span
											class="inline-block rounded-md border border-zinc-200/80 bg-zinc-100/80 px-2 py-0.5 text-[10px] font-bold tracking-wider text-zinc-700 uppercase dark:border-white/10 dark:bg-zinc-800 dark:text-zinc-300"
										>
											{getLocalizedIngredientForm(item.form)}
										</span>
									{/if}
									{#if !item.isCatalogItem}
										<button
											type="button"
											onclick={() => openDeleteModal(item)}
											class="rounded-md border border-zinc-200/80 bg-zinc-100/80 p-1 text-zinc-500 transition-colors hover:border-red-500/50 hover:bg-red-500/10 hover:text-red-600 dark:border-white/10 dark:bg-zinc-800 dark:text-zinc-400 dark:hover:text-red-400"
											title={t('ingredients.delete_ingredient')}
											aria-label={t('ingredients.delete_ingredient')}
											data-testid="delete-ingredient-{item.id}"
										>
											<Trash2 class="h-3 w-3" />
										</button>
									{/if}
								</div>
							</div>
							{#if item.description}
								<p class="line-clamp-2 text-xs leading-relaxed text-zinc-600 dark:text-zinc-400">
									{getLocalizedIngredientDescription(item)}
								</p>
							{/if}
						</div>

						<!-- Specs Badge Matrix -->
						<div
							class="grid grid-cols-2 gap-2 border-t border-zinc-200/60 pt-3 text-xs dark:border-white/5"
						>
							{#if item.type === 'Fermentable'}
								<div
									class="rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-2 dark:border-zinc-800/80 dark:bg-zinc-900/60"
								>
									<span class="block text-[10px] font-medium text-zinc-500 dark:text-zinc-400"
										>{t('ingredients.potential')}</span
									>
									<div class="flex items-baseline gap-1">
										<span class="font-mono font-medium text-zinc-800 dark:text-zinc-200">
											{formatNumber(item.potentialGravity ?? 1.037, 3)}
										</span>
										<span class="font-mono text-[11px] text-zinc-500 dark:text-zinc-400">
											({sgToPpg(item.potentialGravity ?? 1.037)} PPG)
										</span>
									</div>
								</div>
								<div
									class="flex items-center justify-between rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-2 dark:border-zinc-800/80 dark:bg-zinc-900/60"
								>
									<div>
										<span class="block text-[10px] font-medium text-zinc-500 dark:text-zinc-400"
											>{t('ingredients.color')}</span
										>
										<span class="font-mono font-medium text-zinc-800 dark:text-zinc-200"
											>{item.colorSrm ?? 0} SRM</span
										>
									</div>
									{#if item.colorSrm}
										<div
											class="h-4 w-4 rounded-full border border-zinc-300 shadow-sm dark:border-zinc-600"
											style="background-color: {srmToHexColor(item.colorSrm)}"
											title="{item.colorSrm} SRM"
										></div>
									{/if}
								</div>
							{:else if item.type === 'Hop'}
								<div
									class="rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-2 dark:border-zinc-800/80 dark:bg-zinc-900/60"
								>
									<span class="block text-[10px] font-medium text-zinc-500 dark:text-zinc-400"
										>{t('ingredients.alpha_acids')}</span
									>
									<span class="font-mono font-bold text-emerald-600 dark:text-emerald-400"
										>{item.alphaAcidPercent}%</span
									>
								</div>
								<div
									class="rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-2 dark:border-zinc-800/80 dark:bg-zinc-900/60"
								>
									<span class="block text-[10px] font-medium text-zinc-500 dark:text-zinc-400"
										>{t('ingredients.purpose')}</span
									>
									<span class="font-medium text-zinc-800 dark:text-zinc-200">
										{(item.alphaAcidPercent ?? 0) > 10
											? t('ingredients.bittering_dual')
											: t('ingredients.aroma_flavor')}
									</span>
								</div>
							{:else if item.type === 'Yeast'}
								<div
									class="col-span-2 rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-2 dark:border-zinc-800/80 dark:bg-zinc-900/60"
								>
									<span class="block text-[10px] font-medium text-zinc-500 dark:text-zinc-400"
										>{t('ingredients.attenuation')}</span
									>
									<span class="font-mono font-bold text-sky-600 dark:text-sky-400"
										>{item.attenuationPercent}%</span
									>
								</div>
							{:else}
								<div
									class="col-span-2 rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-2 dark:border-zinc-800/80 dark:bg-zinc-900/60"
								>
									<span class="block text-[10px] font-medium text-zinc-500 dark:text-zinc-400"
										>{t('ingredients.purpose')}</span
									>
									<span class="font-medium text-purple-600 dark:text-purple-400">
										{t('ingredients.category_other')}
									</span>
								</div>
							{/if}
						</div>
					</div>

					<!-- Bottom Row: Stock Information & Edit Stock Button -->
					<div
						class="mt-4 flex items-center justify-between gap-2 border-t border-zinc-200/60 pt-3 text-xs dark:border-white/5"
						data-testid="stock-row-{item.id}"
					>
						<div class="flex min-w-0 items-center gap-1.5" data-testid="stock-info-{item.id}">
							<span class="text-xs font-medium text-zinc-500 dark:text-zinc-400">
								{t('ingredients.stock')}:
							</span>
							{#if (item.stockAmount ?? 0) > 0}
								<span
									class="inline-flex items-center gap-1 font-mono text-xs font-semibold text-emerald-600 dark:text-emerald-400"
								>
									<PackageCheck class="h-3.5 w-3.5 shrink-0" />
									<span>{formatNumber(item.stockAmount ?? 0, 2)} {item.stockUnit || 'kg'}</span>
								</span>
							{:else}
								<span class="text-xs font-medium text-zinc-400 italic dark:text-zinc-500">
									{t('ingredients.out_of_stock')}
								</span>
							{/if}
						</div>

						<button
							type="button"
							onclick={() => openStockEditor(item)}
							class="inline-flex shrink-0 items-center gap-1.5 rounded-lg border border-zinc-200/80 bg-zinc-100/80 px-2.5 py-1 text-xs font-semibold text-zinc-700 transition-colors hover:border-amber-500/50 hover:bg-amber-500/10 hover:text-amber-600 active:scale-95 dark:border-white/10 dark:bg-zinc-800 dark:text-zinc-300 dark:hover:text-amber-400"
							title={t('ingredients.update_stock')}
							aria-label="{t('ingredients.update_stock')} - {getLocalizedIngredientName(item)}"
							data-testid="edit-stock-{item.id}"
						>
							<Pencil class="h-3 w-3" />
							<span>{t('ingredients.edit_stock')}</span>
						</button>
					</div>
				</article>
			{/each}
		</div>
	{/if}
</div>

<IngredientModal
	open={modalOpen}
	existingIngredients={ingredients}
	onClose={() => (modalOpen = false)}
	onSaved={handleIngredientSaved}
/>

{#if editingStockItem}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4"
		role="dialog"
		aria-modal="true"
		aria-labelledby="stock-editor-title"
	>
		<!-- Backdrop -->
		<button
			type="button"
			class="fixed inset-0 bg-black/60 backdrop-blur-sm transition-opacity"
			onclick={() => (editingStockItem = null)}
			aria-label={t('common.close_dialog')}
		></button>

		<!-- Dialog Panel -->
		<div
			class="relative w-full max-w-sm rounded-2xl border border-zinc-200 bg-white p-6 shadow-2xl transition-all dark:border-zinc-800 dark:bg-zinc-900"
		>
			<div
				class="flex items-center justify-between border-b border-zinc-200/80 pb-3 dark:border-zinc-800"
			>
				<h3 id="stock-editor-title" class="text-base font-bold text-zinc-900 dark:text-white">
					{t('ingredients.update_stock')}
				</h3>
				<button
					type="button"
					onclick={() => (editingStockItem = null)}
					class="rounded-lg p-1 text-zinc-400 hover:bg-zinc-100 hover:text-zinc-600 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('common.close_dialog')}
				>
					<X class="h-4 w-4" />
				</button>
			</div>

			<div class="mt-4 space-y-4">
				<p class="text-xs font-semibold text-zinc-700 dark:text-zinc-300">
					{getLocalizedIngredientName(editingStockItem)}
				</p>

				<div class="grid grid-cols-2 gap-3">
					<div>
						<label
							for="edit-stock-amount"
							class="block text-xs font-medium text-zinc-600 dark:text-zinc-400"
						>
							{t('ingredients.stock_amount_label')}
						</label>
						<input
							id="edit-stock-amount"
							type="number"
							step="0.01"
							min="0"
							bind:value={newStockAmount}
							class="mt-1 h-10 w-full rounded-xl border border-zinc-300 bg-white px-3 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
						/>
					</div>
					<div>
						<label
							for="edit-stock-unit"
							class="block text-xs font-medium text-zinc-600 dark:text-zinc-400"
						>
							{t('ingredients.stock_unit_label')}
						</label>
						<select
							id="edit-stock-unit"
							bind:value={newStockUnit}
							class="mt-1 h-10 w-full rounded-xl border border-zinc-300 bg-white px-3 text-sm text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
						>
							<option value="kg">kg</option>
							<option value="g">g</option>
							<option value="pkg">pkg</option>
							<option value="items">items</option>
							<option value="ml">ml</option>
							<option value="oz">oz</option>
							<option value="lbs">lbs</option>
						</select>
					</div>
				</div>
			</div>

			<div
				class="mt-6 flex items-center justify-end gap-3 border-t border-zinc-200/80 pt-4 dark:border-zinc-800"
			>
				<button
					type="button"
					onclick={() => (editingStockItem = null)}
					disabled={updatingStock}
					class="rounded-xl border border-zinc-200 bg-white px-3.5 py-1.5 text-xs font-semibold text-zinc-700 transition-colors hover:bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-700"
				>
					{t('common.cancel')}
				</button>
				<button
					type="button"
					onclick={handleSaveStock}
					disabled={updatingStock}
					class="flex items-center gap-1.5 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-4 py-1.5 text-xs font-bold text-zinc-950 shadow-sm transition-all hover:from-amber-400 hover:to-amber-500 active:scale-98 disabled:opacity-50"
				>
					{#if updatingStock}
						<Loader2 class="h-3.5 w-3.5 animate-spin" />
					{:else}
						<Check class="h-3.5 w-3.5 stroke-[2.5]" />
					{/if}
					<span>{t('common.save')}</span>
				</button>
			</div>
		</div>
	</div>
{/if}

<DeleteIngredientModal
	open={deleteModalOpen}
	ingredient={deletingIngredient}
	usage={ingredientUsage}
	{isLoadingUsage}
	onClose={() => {
		deleteModalOpen = false;
		deletingIngredient = null;
		ingredientUsage = null;
	}}
	onDeleted={handleIngredientDeleted}
/>
