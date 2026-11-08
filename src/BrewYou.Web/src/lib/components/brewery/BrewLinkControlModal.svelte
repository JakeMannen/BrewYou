<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import type { EquipmentDto } from '$lib/types/api';
	import BrewLinkControlPanel from '$lib/components/brewery/BrewLinkControlPanel.svelte';
	import { X, Radio } from '@lucide/svelte';

	interface Props {
		open: boolean;
		equipment: EquipmentDto | null;
		onClose: () => void;
		onUpdate?: (updated: EquipmentDto) => void;
	}

	let { open, equipment, onClose, onUpdate }: Props = $props();

	function handleKeydown(event: KeyboardEvent) {
		if (event.key === 'Escape') {
			onClose();
		}
	}
</script>

<svelte:window onkeydown={open ? handleKeydown : undefined} />

{#if open && equipment}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4 sm:p-6"
		role="dialog"
		aria-modal="true"
		aria-labelledby="brewlink-control-modal-title"
	>
		<!-- Backdrop -->
		<button
			type="button"
			class="fixed inset-0 bg-black/70 backdrop-blur-sm transition-opacity"
			onclick={onClose}
			aria-label={t('common.cancel')}
		></button>

		<!-- Modal Container -->
		<div
			class="glass-panel relative flex max-h-[90vh] w-full max-w-3xl flex-col overflow-hidden rounded-3xl border border-sky-500/30 shadow-2xl dark:border-sky-500/20"
		>
			<!-- Header -->
			<div
				class="flex items-center justify-between border-b border-zinc-200/80 px-6 py-4 dark:border-white/[0.08]"
			>
				<div class="flex items-center gap-3">
					<div
						class="flex h-10 w-10 items-center justify-center rounded-xl border border-sky-500/30 bg-sky-500/10 text-sky-500"
					>
						<Radio class="h-5 w-5" />
					</div>
					<div>
						<h2
							id="brewlink-control-modal-title"
							class="font-editorial text-lg font-bold text-zinc-900 dark:text-white"
						>
							{t('equipment.brewlink_modal_title', { name: equipment.name })}
						</h2>
						<p class="text-xs text-zinc-500 dark:text-zinc-400">
							{t('equipment.brewlink_modal_desc')}
						</p>
					</div>
				</div>

				<button
					type="button"
					onclick={onClose}
					class="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					aria-label={t('common.cancel')}
					data-testid="close-brewlink-modal-btn"
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<!-- Body -->
			<div class="overflow-y-auto p-6">
				<BrewLinkControlPanel
					{equipment}
					onUpdate={(updated) => {
						onUpdate?.(updated);
					}}
				/>
			</div>
		</div>
	</div>
{/if}
