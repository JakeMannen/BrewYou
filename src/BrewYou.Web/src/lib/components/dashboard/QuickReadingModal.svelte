<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { dashboard } from '$lib/stores/dashboard.svelte';
	import {
		settings,
		celsiusToFahrenheit,
		fahrenheitToCelsius,
		platoToSg,
		sgToPlato
	} from '$lib/stores/settings.svelte';
	import type { BatchSummaryDto } from '$lib/types/api';
	import { X, Activity, Loader2, Check } from '@lucide/svelte';

	interface Props {
		isOpen: boolean;
		batches: BatchSummaryDto[];
		initialBatchId?: string | null;
		onClose: () => void;
	}

	let { isOpen = $bindable(false), batches, initialBatchId = null, onClose }: Props = $props();

	let selectedBatchId = $state('');
	let gravityInput = $state('');
	let tempInput = $state('');
	let notesInput = $state('');
	let isSubmitting = $state(false);
	let errorMessage = $state<string | null>(null);

	$effect(() => {
		if (isOpen) {
			errorMessage = null;
			notesInput = '';
			if (initialBatchId && batches.some((b) => b.id === initialBatchId)) {
				selectedBatchId = initialBatchId;
			} else if (batches.length > 0) {
				selectedBatchId = batches[0].id;
			}

			// Pre-fill current temperature or gravity if available
			const batch = batches.find((b) => b.id === selectedBatchId);
			if (batch) {
				if (batch.vesselTempC !== null && batch.vesselTempC !== undefined) {
					tempInput =
						settings.temperatureUnit === 'Fahrenheit'
							? celsiusToFahrenheit(batch.vesselTempC).toFixed(1)
							: batch.vesselTempC.toFixed(1);
				} else {
					tempInput = settings.temperatureUnit === 'Fahrenheit' ? '68' : '20';
				}

				if (batch.currentGravity) {
					gravityInput =
						settings.gravityUnit === 'Plato'
							? sgToPlato(batch.currentGravity).toFixed(1)
							: batch.currentGravity.toFixed(3);
				} else {
					gravityInput = '';
				}
			}
		}
	});

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && isOpen) {
			onClose();
		}
	}

	async function handleSubmit(e: Event) {
		e.preventDefault();
		if (!selectedBatchId) return;

		errorMessage = null;
		const rawGrav = parseFloat(gravityInput);
		if (isNaN(rawGrav) || rawGrav <= 0) {
			errorMessage = 'Please enter a valid gravity reading.';
			return;
		}

		let specificGravity = rawGrav;
		if (settings.gravityUnit === 'Plato') {
			specificGravity = platoToSg(rawGrav);
		}

		if (specificGravity < 0.98 || specificGravity > 1.25) {
			errorMessage = 'Gravity reading is outside normal brewing range (0.980 - 1.250 SG).';
			return;
		}

		let tempC: number | null = null;
		if (tempInput.trim()) {
			const rawTemp = parseFloat(tempInput);
			if (!isNaN(rawTemp)) {
				tempC = settings.temperatureUnit === 'Fahrenheit' ? fahrenheitToCelsius(rawTemp) : rawTemp;
			}
		}

		isSubmitting = true;
		try {
			await dashboard.logReading(selectedBatchId, {
				specificGravity,
				temperatureC: tempC,
				notes: notesInput.trim() || undefined,
				timestamp: new Date().toISOString()
			});
			onClose();
		} catch (err: unknown) {
			errorMessage = (err as Error).message || 'Failed to record reading';
		} finally {
			isSubmitting = false;
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if isOpen}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4"
		role="dialog"
		aria-modal="true"
		aria-labelledby="reading-modal-title"
	>
		<!-- Backdrop -->
		<button
			type="button"
			class="fixed inset-0 bg-black/60 backdrop-blur-sm transition-opacity"
			onclick={onClose}
			tabindex="-1"
			aria-label={t('common.close_dialog')}
		></button>

		<!-- Modal Dialog Box -->
		<div
			class="glass-panel relative z-10 w-full max-w-md overflow-hidden rounded-3xl border border-zinc-200/80 p-6 shadow-2xl sm:p-7 dark:border-white/10"
		>
			<div
				class="flex items-center justify-between border-b border-zinc-200/80 pb-4 dark:border-white/10"
			>
				<div class="flex items-center gap-2.5">
					<div
						class="flex h-9 w-9 items-center justify-center rounded-xl bg-hops-500/15 text-hops-600 dark:text-hops-400"
					>
						<Activity class="h-5 w-5" />
					</div>
					<h3 id="reading-modal-title" class="text-lg font-bold text-zinc-900 dark:text-white">
						{t('dashboard.log_reading_modal_title')}
					</h3>
				</div>
				<button
					type="button"
					onclick={onClose}
					class="rounded-xl p-1.5 text-zinc-600 transition hover:bg-zinc-100 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800 dark:hover:text-white"
					aria-label={t('common.close_dialog')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			{#if errorMessage}
				<div
					class="mt-4 rounded-xl border border-rose-500/30 bg-rose-500/10 p-3 text-xs text-rose-600 dark:text-rose-400"
				>
					{errorMessage}
				</div>
			{/if}

			<form onsubmit={handleSubmit} class="mt-4 space-y-4">
				<!-- Batch Selector -->
				<div>
					<label
						for="reading-batch-select"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('dashboard.select_batch')}
					</label>
					<select
						id="reading-batch-select"
						bind:value={selectedBatchId}
						class="mt-1.5 w-full rounded-xl border border-zinc-300 bg-white/80 px-3.5 py-2.5 text-sm text-zinc-900 focus:border-amber-500 focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900/60 dark:text-white"
					>
						{#each batches as batch (batch.id)}
							<option value={batch.id}>
								{batch.batchCode} — {batch.name} ({batch.currentStage})
							</option>
						{/each}
					</select>
				</div>

				<!-- Gravity & Temp Inputs Row -->
				<div class="grid grid-cols-2 gap-3">
					<div>
						<label
							for="reading-gravity"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{settings.gravityUnit === 'Plato'
								? t('dashboard.plato_reading')
								: t('dashboard.gravity_reading')}
						</label>
						<input
							id="reading-gravity"
							type="number"
							step={settings.gravityUnit === 'Plato' ? '0.1' : '0.001'}
							bind:value={gravityInput}
							placeholder={settings.gravityUnit === 'Plato' ? '12.5' : '1.050'}
							required
							class="mt-1.5 w-full rounded-xl border border-zinc-300 bg-white/80 px-3.5 py-2.5 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900/60 dark:text-white"
						/>
					</div>

					<div>
						<label
							for="reading-temp"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('dashboard.temperature_reading')} ({settings.temperatureUnit === 'Fahrenheit'
								? '°F'
								: '°C'})
						</label>
						<input
							id="reading-temp"
							type="number"
							step="0.1"
							bind:value={tempInput}
							placeholder={settings.temperatureUnit === 'Fahrenheit' ? '68.0' : '20.0'}
							class="mt-1.5 w-full rounded-xl border border-zinc-300 bg-white/80 px-3.5 py-2.5 font-mono text-sm text-zinc-900 focus:border-amber-500 focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900/60 dark:text-white"
						/>
					</div>
				</div>

				<!-- Sensory Notes -->
				<div>
					<label
						for="reading-notes"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('dashboard.notes_optional')}
					</label>
					<textarea
						id="reading-notes"
						bind:value={notesInput}
						rows="2"
						placeholder="Aromas, clarity, active bubbling, dry hop aroma..."
						class="mt-1.5 w-full rounded-xl border border-zinc-300 bg-white/80 p-3 text-xs text-zinc-900 focus:border-amber-500 focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900/60 dark:text-white"
					></textarea>
				</div>

				<!-- Actions -->
				<div class="flex items-center justify-end gap-3 pt-2">
					<button
						type="button"
						onclick={onClose}
						class="rounded-xl border border-zinc-200 bg-white px-4 py-2 text-xs font-semibold text-zinc-800 transition hover:bg-zinc-100 dark:border-white/10 dark:bg-zinc-800 dark:text-zinc-200 dark:hover:bg-zinc-700"
					>
						{t('common.cancel')}
					</button>

					<button
						type="submit"
						disabled={isSubmitting || !gravityInput}
						class="inline-flex items-center gap-1.5 rounded-xl border border-amber-500/50 bg-amber-500 px-4 py-2 text-xs font-bold text-zinc-950 transition hover:bg-amber-400 disabled:opacity-40"
					>
						{#if isSubmitting}
							<Loader2 class="h-3.5 w-3.5 animate-spin" />
							<span>{t('dashboard.saving_reading')}</span>
						{:else}
							<Check class="h-3.5 w-3.5 stroke-[2.5]" />
							<span>{t('dashboard.save_reading_btn')}</span>
						{/if}
					</button>
				</div>
			</form>
		</div>
	</div>
{/if}
