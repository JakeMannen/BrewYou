<script lang="ts">
	import { brewery } from '$lib/stores/brewery.svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { settings, type UnitPreset } from '$lib/stores/settings.svelte';
	import { i18n, t, type LocaleCode } from '$lib/i18n/index.svelte';
	import { Beer, AlertCircle, Globe, Sparkles, Check, Scale } from '@lucide/svelte';

	interface Props {
		onComplete?: (breweryName: string, preset?: Exclude<UnitPreset, 'Custom'>) => void;
	}

	let { onComplete }: Props = $props();

	let breweryName = $state('');
	let hasCustomized = $state(false);
	let selectedPreset = $state<Exclude<UnitPreset, 'Custom'>>(
		settings.volumeUnit === 'Gallons' ? 'US Craft' : 'European Metric'
	);
	let localError = $state<string | null>(null);
	let submitting = $state(false);
	let modalElement = $state<HTMLDivElement | null>(null);

	// Initialize with default brewery name from i18n and react to language changes if user hasn't typed
	$effect(() => {
		const defaultName = t('brewery.default_brewery_name');
		if (!hasCustomized) {
			breweryName = defaultName;
		}
	});

	function handleInput(e: Event) {
		const target = e.target as HTMLInputElement;
		breweryName = target.value;
		hasCustomized = true;
	}

	function toggleLanguage() {
		const nextLocale: LocaleCode = i18n.locale === 'en' ? 'sv' : 'en';
		i18n.setLocale(nextLocale);
	}

	async function handleSubmit(e: Event) {
		e.preventDefault();
		localError = null;

		let finalName = breweryName.trim();
		if (!finalName) {
			finalName = t('brewery.default_brewery_name');
		}

		// Security: sanitize invalid angle brackets
		finalName = finalName.replace(/[<>]/g, '').trim();

		if (finalName.length > 50) {
			localError = t('brewery.validation_length');
			return;
		}

		submitting = true;
		try {
			// Rename the current active / default setup if it differs
			if (brewery.activeSetup && brewery.activeSetup.name !== finalName) {
				await brewery.renameSetup(brewery.activeSetup.id, finalName);
			}

			// Apply selected unit preset
			await settings.applyPreset(selectedPreset);

			auth.completeOnboarding();

			if (onComplete) {
				onComplete(finalName, selectedPreset);
			}
		} catch (err: unknown) {
			localError = err instanceof Error ? err.message : 'Could not set brewery name.';
		} finally {
			submitting = false;
		}
	}

	// Focus trapping & strictly blocking Escape dismissal
	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape') {
			e.preventDefault();
			e.stopPropagation();
			return;
		}

		if (e.key === 'Tab' && modalElement) {
			const focusable = modalElement.querySelectorAll<HTMLElement>(
				'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
			);
			if (focusable.length === 0) return;

			const first = focusable[0];
			const last = focusable[focusable.length - 1];

			if (e.shiftKey) {
				if (document.activeElement === first) {
					e.preventDefault();
					last.focus();
				}
			} else {
				if (document.activeElement === last) {
					e.preventDefault();
					first.focus();
				}
			}
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

<!-- Fullscreen backdrop (non-dismissible) -->
<div
	data-testid="onboarding-modal-backdrop"
	class="fixed inset-0 z-50 flex items-center justify-center bg-black/65 p-4 backdrop-blur-xl transition-all select-none"
	role="presentation"
>
	<!-- Modal Dialog Card -->
	<div
		bind:this={modalElement}
		data-testid="onboarding-modal"
		role="dialog"
		aria-modal="true"
		aria-labelledby="onboarding-modal-title"
		aria-describedby="onboarding-modal-desc"
		class="glass-panel relative w-full max-w-lg space-y-5 rounded-3xl border border-zinc-200/80 p-5 shadow-2xl sm:p-7 dark:border-white/10 dark:bg-zinc-950/90 dark:shadow-[0_0_50px_rgba(245,158,11,0.15)]"
	>
		<!-- Header Controls: Brand and Language Toggle -->
		<div class="flex items-center justify-between">
			<div class="flex items-center gap-2">
				<div
					class="flex h-8 w-8 items-center justify-center overflow-hidden rounded-lg border border-amber-500/30 bg-amber-500/10"
				>
					<img
						src="/logo-light.png"
						alt="BrewYou Logo"
						class="block h-full w-full object-contain p-0.5 dark:hidden"
					/>
					<img
						src="/logo-dark.png"
						alt="BrewYou Logo"
						class="hidden h-full w-full object-contain p-0.5 dark:block"
					/>
				</div>
				<span class="text-xs font-bold tracking-tight text-zinc-900 dark:text-white">
					Brew<span class="text-amber-500 dark:text-amber-400">You</span>
				</span>
			</div>

			<!-- Language Switcher -->
			<button
				type="button"
				onclick={toggleLanguage}
				data-testid="onboarding-lang-switcher"
				class="inline-flex items-center gap-1.5 rounded-lg border border-zinc-200/80 bg-zinc-100/80 px-2.5 py-1 text-xs font-semibold text-zinc-700 transition-colors hover:bg-zinc-200 dark:border-white/10 dark:bg-zinc-900/80 dark:text-zinc-300 dark:hover:bg-zinc-800"
				title={i18n.locale === 'en' ? 'Växla till Svenska' : 'Switch to English'}
			>
				<Globe class="h-3.5 w-3.5 text-amber-500 dark:text-amber-400" />
				<span class="uppercase">{i18n.locale}</span>
			</button>
		</div>

		<!-- Title and Subtitle -->
		<div class="space-y-2 text-center">
			<div
				class="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl border border-amber-500/30 bg-amber-500/10 text-amber-500 shadow-inner dark:text-amber-400"
			>
				<Beer class="h-6 w-6" />
			</div>
			<div class="space-y-1">
				<h2
					id="onboarding-modal-title"
					class="text-xl font-bold tracking-tight text-zinc-900 sm:text-2xl dark:text-white"
				>
					{t('brewery.onboarding_title')}
				</h2>
				<p id="onboarding-modal-desc" class="text-xs text-zinc-500 sm:text-sm dark:text-zinc-400">
					{t('brewery.onboarding_subtitle')}
				</p>
			</div>
		</div>

		<!-- Error Alert Banner -->
		{#if localError}
			<div
				data-testid="onboarding-error-alert"
				role="alert"
				class="flex items-center gap-2 rounded-xl border border-red-500/30 bg-red-500/10 p-3 text-xs text-red-600 dark:border-red-500/20 dark:text-red-400"
			>
				<AlertCircle class="h-4 w-4 flex-shrink-0" />
				<span>{localError}</span>
			</div>
		{/if}

		<!-- Brewery Naming Form -->
		<form onsubmit={handleSubmit} class="space-y-4">
			<div>
				<label
					for="onboarding-brewery-name-input"
					class="mb-1.5 block text-xs font-medium text-zinc-700 dark:text-zinc-300"
				>
					{t('brewery.onboarding_name_label')}
				</label>
				<input
					id="onboarding-brewery-name-input"
					data-testid="onboarding-brewery-name-input"
					type="text"
					required
					maxlength="50"
					autocomplete="off"
					value={breweryName}
					oninput={handleInput}
					placeholder={t('brewery.onboarding_name_placeholder')}
					class="h-11 w-full rounded-xl border border-zinc-200/90 bg-white/80 px-3.5 py-2.5 text-sm font-medium text-zinc-900 transition-colors focus:border-amber-500 focus:outline-none dark:border-white/10 dark:bg-zinc-900/90 dark:text-white"
				/>
				<p class="mt-1.5 text-[11px] text-zinc-500 dark:text-zinc-400">
					{t('brewery.onboarding_tagline')}
				</p>
			</div>

			<!-- Measurement Standard & Unit Preset Selection -->
			<div class="space-y-2">
				<div class="flex items-center justify-between">
					<label
						id="onboarding-preset-label"
						class="flex items-center gap-1.5 text-xs font-medium text-zinc-700 dark:text-zinc-300"
					>
						<Scale class="h-3.5 w-3.5 text-amber-500 dark:text-amber-400" />
						<span>{t('brewery.onboarding_preset_label')}</span>
					</label>
					<span class="text-[10px] font-semibold text-amber-600 dark:text-amber-400">
						{selectedPreset === 'European Metric'
							? t('settings.presets.european_metric')
							: selectedPreset === 'US Craft'
								? t('settings.presets.us_craft')
								: t('settings.presets.uk_traditional')}
					</span>
				</div>

				<div
					role="radiogroup"
					aria-labelledby="onboarding-preset-label"
					class="grid grid-cols-1 gap-2 sm:grid-cols-3"
				>
					<!-- European Metric -->
					<button
						type="button"
						role="radio"
						aria-checked={selectedPreset === 'European Metric'}
						data-testid="onboarding-preset-european-metric"
						onclick={() => (selectedPreset = 'European Metric')}
						class="flex flex-col items-start rounded-xl border p-2.5 text-left transition {selectedPreset ===
						'European Metric'
							? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
							: 'border-zinc-200/80 bg-zinc-50/50 hover:bg-zinc-100/80 dark:border-white/10 dark:bg-zinc-900/40 dark:hover:bg-zinc-800/60'}"
					>
						<div class="flex w-full items-center justify-between">
							<span class="text-xs font-bold text-zinc-900 dark:text-white">
								{t('settings.presets.european_metric')}
							</span>
							{#if selectedPreset === 'European Metric'}
								<Check class="h-3.5 w-3.5 text-amber-500 dark:text-amber-400" />
							{/if}
						</div>
						<span class="mt-0.5 text-[10px] text-zinc-500 dark:text-zinc-400">
							{t('settings.presets.european_metric_desc')}
						</span>
					</button>

					<!-- US Craft -->
					<button
						type="button"
						role="radio"
						aria-checked={selectedPreset === 'US Craft'}
						data-testid="onboarding-preset-us-craft"
						onclick={() => (selectedPreset = 'US Craft')}
						class="flex flex-col items-start rounded-xl border p-2.5 text-left transition {selectedPreset ===
						'US Craft'
							? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
							: 'border-zinc-200/80 bg-zinc-50/50 hover:bg-zinc-100/80 dark:border-white/10 dark:bg-zinc-900/40 dark:hover:bg-zinc-800/60'}"
					>
						<div class="flex w-full items-center justify-between">
							<span class="text-xs font-bold text-zinc-900 dark:text-white">
								{t('settings.presets.us_craft')}
							</span>
							{#if selectedPreset === 'US Craft'}
								<Check class="h-3.5 w-3.5 text-amber-500 dark:text-amber-400" />
							{/if}
						</div>
						<span class="mt-0.5 text-[10px] text-zinc-500 dark:text-zinc-400">
							{t('settings.presets.us_craft_desc')}
						</span>
					</button>

					<!-- UK Traditional -->
					<button
						type="button"
						role="radio"
						aria-checked={selectedPreset === 'UK Traditional'}
						data-testid="onboarding-preset-uk-traditional"
						onclick={() => (selectedPreset = 'UK Traditional')}
						class="flex flex-col items-start rounded-xl border p-2.5 text-left transition {selectedPreset ===
						'UK Traditional'
							? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
							: 'border-zinc-200/80 bg-zinc-50/50 hover:bg-zinc-100/80 dark:border-white/10 dark:bg-zinc-900/40 dark:hover:bg-zinc-800/60'}"
					>
						<div class="flex w-full items-center justify-between">
							<span class="text-xs font-bold text-zinc-900 dark:text-white">
								{t('settings.presets.uk_traditional')}
							</span>
							{#if selectedPreset === 'UK Traditional'}
								<Check class="h-3.5 w-3.5 text-amber-500 dark:text-amber-400" />
							{/if}
						</div>
						<span class="mt-0.5 text-[10px] text-zinc-500 dark:text-zinc-400">
							{t('settings.presets.uk_traditional_desc')}
						</span>
					</button>
				</div>
				<p class="text-[11px] text-zinc-500 dark:text-zinc-400">
					{t('brewery.onboarding_preset_hint')}
				</p>
			</div>

			<button
				type="submit"
				disabled={submitting}
				data-testid="onboarding-submit-btn"
				class="flex h-11 w-full cursor-pointer items-center justify-center gap-2 rounded-xl bg-amber-500 py-2.5 text-sm font-bold text-zinc-950 shadow-lg shadow-amber-500/20 transition-all hover:bg-amber-400 active:scale-[0.98] disabled:opacity-50"
			>
				{#if submitting}
					<span>{t('brewery.onboarding_submitting')}</span>
				{:else}
					<Sparkles class="h-4 w-4" />
					<span>{t('brewery.onboarding_submit')}</span>
				{/if}
			</button>
		</form>
	</div>
</div>
