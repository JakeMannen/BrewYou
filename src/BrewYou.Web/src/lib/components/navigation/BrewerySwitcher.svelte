<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import { brewery } from '$lib/stores/brewery.svelte';
	import { t } from '$lib/i18n/index.svelte';
	import type { BrewerySetup } from '$lib/types/brewery';
	import {
		Building2,
		ChevronDown,
		Check,
		Pencil,
		Trash2,
		Plus,
		X,
		AlertCircle
	} from '@lucide/svelte';

	let {
		collapsed = false,
		onselect
	}: {
		collapsed?: boolean;
		onselect?: () => void;
	} = $props();

	let isOpen = $state(false);
	let switcherRef = $state<HTMLDivElement | null>(null);

	// Add modal state
	let showAddModal = $state(false);
	let newName = $state('');
	let addError = $state<string | null>(null);

	// Rename modal state
	let showRenameModal = $state(false);
	let renameTargetId = $state<string | null>(null);
	let renameValue = $state('');
	let renameError = $state<string | null>(null);

	// Delete modal state
	let showDeleteModal = $state(false);
	let deleteTargetId = $state<string | null>(null);
	let deleteTarget = $derived(brewery.setups.find((s) => s.id === deleteTargetId));

	function toggleDropdown() {
		isOpen = !isOpen;
	}

	function handleSelect(id: string) {
		brewery.selectSetup(id);
		isOpen = false;
		onselect?.();
	}

	function handleKeydown(event: KeyboardEvent) {
		if (event.key === 'Escape') {
			if (showAddModal) {
				showAddModal = false;
			} else if (showRenameModal) {
				showRenameModal = false;
			} else if (showDeleteModal) {
				showDeleteModal = false;
			} else if (isOpen) {
				isOpen = false;
			}
		}
	}

	function handleOutsideClick(event: MouseEvent) {
		if (switcherRef && !switcherRef.contains(event.target as Node)) {
			isOpen = false;
		}
	}

	onMount(() => {
		if (typeof window !== 'undefined') {
			window.addEventListener('click', handleOutsideClick);
			window.addEventListener('keydown', handleKeydown);
		}
	});

	onDestroy(() => {
		if (typeof window !== 'undefined') {
			window.removeEventListener('click', handleOutsideClick);
			window.removeEventListener('keydown', handleKeydown);
		}
	});

	function openAddModal(e?: MouseEvent) {
		e?.stopPropagation();
		let counter = brewery.setups.length + 1;
		while (
			brewery.setups.some((s) => s.name.toLowerCase() === `brewery ${counter}`.toLowerCase())
		) {
			counter++;
		}
		newName = `Brewery ${counter}`;
		addError = null;
		showAddModal = true;
		isOpen = false;
	}

	async function submitAdd(e: SubmitEvent) {
		e.preventDefault();
		const trimmed = newName.trim();
		if (!trimmed) {
			addError = t('brewery.validation_required');
			return;
		}
		if (trimmed.length > 50) {
			addError = t('brewery.validation_length');
			return;
		}
		if (trimmed.includes('<') || trimmed.includes('>')) {
			addError = t('brewery.validation_invalid_chars');
			return;
		}

		await brewery.addSetup(trimmed);
		showAddModal = false;
		onselect?.();
	}

	function openRenameModal(s: BrewerySetup, e: MouseEvent) {
		e.stopPropagation();
		renameTargetId = s.id;
		renameValue = s.name;
		renameError = null;
		showRenameModal = true;
		isOpen = false;
	}

	async function submitRename(e: SubmitEvent) {
		e.preventDefault();
		if (!renameTargetId) return;

		const trimmed = renameValue.trim();
		if (!trimmed) {
			renameError = t('brewery.validation_required');
			return;
		}
		if (trimmed.length > 50) {
			renameError = t('brewery.validation_length');
			return;
		}
		if (trimmed.includes('<') || trimmed.includes('>')) {
			renameError = t('brewery.validation_invalid_chars');
			return;
		}

		const success = await brewery.renameSetup(renameTargetId, trimmed);
		if (success) {
			showRenameModal = false;
			renameTargetId = null;
		} else {
			renameError = t('brewery.validation_invalid_chars');
		}
	}

	function openDeleteModal(s: BrewerySetup, e: MouseEvent) {
		e.stopPropagation();
		if (!brewery.canDelete) return;
		deleteTargetId = s.id;
		showDeleteModal = true;
		isOpen = false;
	}

	async function submitDelete() {
		if (deleteTargetId) {
			await brewery.deleteSetup(deleteTargetId);
		}
		showDeleteModal = false;
		deleteTargetId = null;
	}
</script>

<div class="relative w-full" bind:this={switcherRef}>
	<!-- Trigger Button -->
	{#if collapsed}
		<button
			type="button"
			onclick={toggleDropdown}
			class="flex h-10 w-10 items-center justify-center rounded-xl border border-zinc-200/60 bg-zinc-100/70 text-zinc-700 transition-colors hover:border-amber-500/40 hover:bg-zinc-200/60 dark:border-white/5 dark:bg-zinc-900/60 dark:text-zinc-200 dark:hover:bg-zinc-800/60"
			title="{t('brewery.switcher_label')}: {brewery.activeSetup.name}"
			aria-label="{t('brewery.switcher_label')}: {brewery.activeSetup.name}"
			aria-expanded={isOpen}
			aria-haspopup="listbox"
			data-testid="brewery-switcher-trigger-collapsed"
		>
			<Building2 class="h-4 w-4 text-amber-500 dark:text-amber-400" />
		</button>
	{:else}
		<button
			type="button"
			onclick={toggleDropdown}
			class="group flex w-full items-center justify-between rounded-xl border border-zinc-200/60 bg-zinc-100/70 px-3 py-2 text-xs transition-colors hover:border-amber-500/40 hover:bg-zinc-200/60 dark:border-white/5 dark:bg-zinc-900/60 dark:hover:bg-zinc-800/60"
			aria-label="{t('brewery.switcher_label')}: {brewery.activeSetup.name}"
			aria-expanded={isOpen}
			aria-haspopup="listbox"
			data-testid="brewery-switcher-trigger"
		>
			<div class="flex items-center gap-2 overflow-hidden">
				<div
					class="h-2 w-2 flex-shrink-0 rounded-full bg-hops-500 shadow-[0_0_8px_rgba(16,185,129,0.5)]"
				></div>
				<div
					class="truncate font-medium text-zinc-700 dark:text-zinc-200"
					data-testid="active-brewery-name"
				>
					{brewery.activeSetup.name}
				</div>
			</div>
			<ChevronDown
				class="h-3.5 w-3.5 flex-shrink-0 text-zinc-400 transition-transform duration-200 group-hover:text-zinc-600 dark:text-zinc-500 dark:group-hover:text-zinc-300 {isOpen
					? 'rotate-180 text-amber-500 dark:text-amber-400'
					: ''}"
			/>
		</button>
	{/if}

	<!-- Dropdown Menu Popover -->
	{#if isOpen}
		<div
			class="glass-panel absolute z-50 rounded-2xl border border-zinc-200/80 bg-white/95 p-1.5 shadow-2xl backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/95 {collapsed
				? 'top-0 left-full ml-2 w-64'
				: 'top-full left-0 mt-1.5 w-full min-w-[200px]'}"
			role="listbox"
			aria-label={t('brewery.all_breweries')}
			data-testid="brewery-switcher-menu"
		>
			<!-- Header -->
			<div
				class="flex items-center justify-between border-b border-zinc-200/60 px-2 py-1.5 text-[10px] font-bold tracking-wider text-zinc-400 uppercase dark:border-white/5 dark:text-zinc-500"
			>
				<span>{t('brewery.all_breweries')}</span>
				<span class="font-mono text-[10px] text-zinc-400">{brewery.setups.length}</span>
			</div>

			<!-- Setups List -->
			<div class="mt-1 max-h-56 space-y-0.5 overflow-y-auto">
				{#each brewery.setups as setup (setup.id)}
					{@const isActive = setup.id === brewery.activeSetupId}
					<div
						class="group flex items-center justify-between gap-1 rounded-xl px-2 py-1.5 text-xs transition-colors {isActive
							? 'bg-amber-500/10 font-semibold text-amber-600 dark:text-amber-400'
							: 'text-zinc-700 hover:bg-zinc-100 dark:text-zinc-300 dark:hover:bg-zinc-800/70'}"
						role="option"
						aria-selected={isActive}
						data-testid="brewery-option-{setup.id}"
					>
						<button
							type="button"
							onclick={() => handleSelect(setup.id)}
							class="flex flex-1 items-center gap-2 overflow-hidden text-left focus:outline-none"
						>
							{#if isActive}
								<Check class="h-3.5 w-3.5 flex-shrink-0 text-amber-500 dark:text-amber-400" />
							{:else}
								<span class="h-3.5 w-3.5 flex-shrink-0"></span>
							{/if}
							<span class="truncate">{setup.name}</span>
						</button>

						<!-- Item Actions: Rename & Delete -->
						<div class="flex items-center gap-1 opacity-80 group-hover:opacity-100">
							<button
								type="button"
								onclick={(e) => openRenameModal(setup, e)}
								class="rounded-md p-1 text-zinc-400 transition-colors hover:bg-zinc-200/70 hover:text-zinc-700 dark:text-zinc-500 dark:hover:bg-zinc-700/60 dark:hover:text-zinc-200"
								title="{t('brewery.rename_brewery')}: {setup.name}"
								aria-label="{t('brewery.rename_brewery')}: {setup.name}"
								data-testid="rename-brewery-btn-{setup.id}"
							>
								<Pencil class="h-3 w-3" />
							</button>

							{#if brewery.canDelete}
								<button
									type="button"
									onclick={(e) => openDeleteModal(setup, e)}
									class="rounded-md p-1 text-zinc-400 transition-colors hover:bg-red-500/10 hover:text-red-500 dark:text-zinc-500 dark:hover:bg-red-500/20 dark:hover:text-red-400"
									title="{t('brewery.delete_brewery')}: {setup.name}"
									aria-label="{t('brewery.delete_brewery')}: {setup.name}"
									data-testid="delete-brewery-btn-{setup.id}"
								>
									<Trash2 class="h-3 w-3" />
								</button>
							{/if}
						</div>
					</div>
				{/each}
			</div>

			<!-- Divider -->
			<div class="my-1 h-px bg-zinc-200/60 dark:bg-white/5"></div>

			<!-- Add Brewery CTA -->
			<button
				type="button"
				onclick={openAddModal}
				class="flex w-full items-center gap-2 rounded-xl px-2.5 py-1.5 text-xs font-semibold text-amber-600 transition-colors hover:bg-amber-500/10 dark:text-amber-400 dark:hover:bg-amber-500/15"
				data-testid="add-brewery-btn"
			>
				<Plus class="h-3.5 w-3.5 stroke-[2.5]" />
				<span>{t('brewery.add_brewery')}</span>
			</button>
		</div>
	{/if}
</div>

<!-- Modal: Create New Brewery Setup -->
{#if showAddModal}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm"
		role="dialog"
		aria-modal="true"
		aria-labelledby="add-modal-title"
	>
		<div
			class="glass-panel w-full max-w-sm rounded-2xl border border-zinc-200/80 bg-white p-5 shadow-2xl dark:border-white/10 dark:bg-zinc-900"
		>
			<div
				class="flex items-center justify-between border-b border-zinc-200/80 pb-3 dark:border-white/10"
			>
				<h3 id="add-modal-title" class="text-sm font-bold text-zinc-900 dark:text-white">
					{t('brewery.create_title')}
				</h3>
				<button
					type="button"
					onclick={() => (showAddModal = false)}
					class="rounded-lg p-1 text-zinc-400 hover:bg-zinc-100 hover:text-zinc-600 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('brewery.cancel')}
				>
					<X class="h-4 w-4" />
				</button>
			</div>

			<form onsubmit={submitAdd} class="mt-4 space-y-4">
				<div>
					<label
						for="new-brewery-name-input"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('brewery.name_label')}
					</label>
					<input
						id="new-brewery-name-input"
						data-testid="new-brewery-name-input"
						type="text"
						bind:value={newName}
						maxlength="50"
						required
						placeholder={t('brewery.name_placeholder')}
						class="mt-1.5 w-full rounded-xl border border-zinc-300 bg-zinc-50/50 px-3.5 py-2 text-sm text-zinc-900 transition focus:border-amber-500 focus:bg-white focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-800/50 dark:text-white dark:focus:border-amber-400 dark:focus:bg-zinc-900"
					/>
					{#if addError}
						<div
							class="mt-1.5 flex items-center gap-1.5 text-xs text-red-600 dark:text-red-400"
							role="alert"
						>
							<AlertCircle class="h-3.5 w-3.5" />
							<span>{addError}</span>
						</div>
					{/if}
				</div>

				<div class="flex items-center justify-end gap-2 pt-2">
					<button
						type="button"
						onclick={() => (showAddModal = false)}
						class="rounded-xl border border-zinc-300 px-3.5 py-1.5 text-xs font-semibold text-zinc-700 transition hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-800"
					>
						{t('brewery.cancel')}
					</button>
					<button
						type="submit"
						data-testid="submit-create-brewery-btn"
						class="rounded-xl border border-amber-500/40 bg-gradient-to-r from-amber-500 to-amber-600 px-4 py-1.5 text-xs font-semibold text-zinc-950 shadow-sm transition hover:from-amber-400 hover:to-amber-500 active:scale-[0.98]"
					>
						{t('brewery.create_submit')}
					</button>
				</div>
			</form>
		</div>
	</div>
{/if}

<!-- Modal: Rename Brewery Setup -->
{#if showRenameModal}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm"
		role="dialog"
		aria-modal="true"
		aria-labelledby="rename-modal-title"
	>
		<div
			class="glass-panel w-full max-w-sm rounded-2xl border border-zinc-200/80 bg-white p-5 shadow-2xl dark:border-white/10 dark:bg-zinc-900"
		>
			<div
				class="flex items-center justify-between border-b border-zinc-200/80 pb-3 dark:border-white/10"
			>
				<h3 id="rename-modal-title" class="text-sm font-bold text-zinc-900 dark:text-white">
					{t('brewery.rename_title')}
				</h3>
				<button
					type="button"
					onclick={() => (showRenameModal = false)}
					class="rounded-lg p-1 text-zinc-400 hover:bg-zinc-100 hover:text-zinc-600 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('brewery.cancel')}
				>
					<X class="h-4 w-4" />
				</button>
			</div>

			<form onsubmit={submitRename} class="mt-4 space-y-4">
				<div>
					<label
						for="rename-brewery-input"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('brewery.name_label')}
					</label>
					<input
						id="rename-brewery-input"
						data-testid="rename-brewery-input"
						type="text"
						bind:value={renameValue}
						maxlength="50"
						required
						class="mt-1.5 w-full rounded-xl border border-zinc-300 bg-zinc-50/50 px-3.5 py-2 text-sm text-zinc-900 transition focus:border-amber-500 focus:bg-white focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-800/50 dark:text-white dark:focus:border-amber-400 dark:focus:bg-zinc-900"
					/>
					{#if renameError}
						<div
							class="mt-1.5 flex items-center gap-1.5 text-xs text-red-600 dark:text-red-400"
							role="alert"
						>
							<AlertCircle class="h-3.5 w-3.5" />
							<span>{renameError}</span>
						</div>
					{/if}
				</div>

				<div class="flex items-center justify-end gap-2 pt-2">
					<button
						type="button"
						onclick={() => (showRenameModal = false)}
						class="rounded-xl border border-zinc-300 px-3.5 py-1.5 text-xs font-semibold text-zinc-700 transition hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-800"
					>
						{t('brewery.cancel')}
					</button>
					<button
						type="submit"
						data-testid="submit-rename-brewery-btn"
						class="rounded-xl border border-amber-500/40 bg-gradient-to-r from-amber-500 to-amber-600 px-4 py-1.5 text-xs font-semibold text-zinc-950 shadow-sm transition hover:from-amber-400 hover:to-amber-500 active:scale-[0.98]"
					>
						{t('brewery.rename_submit')}
					</button>
				</div>
			</form>
		</div>
	</div>
{/if}

<!-- Modal: Delete Confirmation -->
{#if showDeleteModal && deleteTarget}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm"
		role="dialog"
		aria-modal="true"
		aria-labelledby="delete-modal-title"
	>
		<div
			class="glass-panel w-full max-w-sm rounded-2xl border border-zinc-200/80 bg-white p-5 shadow-2xl dark:border-white/10 dark:bg-zinc-900"
		>
			<div
				class="flex items-center justify-between border-b border-zinc-200/80 pb-3 dark:border-white/10"
			>
				<h3 id="delete-modal-title" class="text-sm font-bold text-red-600 dark:text-red-400">
					{t('brewery.delete_title')}
				</h3>
				<button
					type="button"
					onclick={() => (showDeleteModal = false)}
					class="rounded-lg p-1 text-zinc-400 hover:bg-zinc-100 hover:text-zinc-600 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('brewery.cancel')}
				>
					<X class="h-4 w-4" />
				</button>
			</div>

			<div class="mt-4 space-y-3">
				<p class="text-xs text-zinc-600 dark:text-zinc-300" data-testid="delete-modal-warning">
					{t('brewery.delete_warning', { name: deleteTarget.name })}
				</p>
			</div>

			<div class="flex items-center justify-end gap-2 pt-4">
				<button
					type="button"
					onclick={() => (showDeleteModal = false)}
					class="rounded-xl border border-zinc-300 px-3.5 py-1.5 text-xs font-semibold text-zinc-700 transition hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-800"
				>
					{t('brewery.cancel')}
				</button>
				<button
					type="button"
					onclick={submitDelete}
					data-testid="confirm-delete-brewery-btn"
					class="rounded-xl border border-red-500/40 bg-gradient-to-r from-red-500 to-red-600 px-4 py-1.5 text-xs font-semibold text-white shadow-sm transition hover:from-red-600 hover:to-red-700 active:scale-[0.98]"
				>
					{t('brewery.delete_submit')}
				</button>
			</div>
		</div>
	</div>
{/if}
