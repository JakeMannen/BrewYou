<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { auth } from '$lib/stores/auth.svelte';
	import { i18n, supportedLocales, t, type LocaleCode } from '$lib/i18n/index.svelte';
	import {
		settings,
		toDisplayVolume,
		fromDisplayVolume,
		formatVolume,
		formatGrainWeight,
		formatHopWeight,
		formatTemperature,
		formatGravity,
		formatColor,
		formatCarbonation,
		type ColorUnit,
		type HopWeightUnit,
		type GrainWeightUnit,
		type CarbonationUnit,
		type BitternessFormula,
		type ColorFormula,
		type AbvFormula,
		type UnitPreset
	} from '$lib/stores/settings.svelte';
	import type { GravityUnit, TemperatureUnit, ThemePreference, VolumeUnit } from '$lib/types/api';
	import {
		User,
		Palette,
		Globe,
		Scale,
		Sliders,
		Sun,
		Moon,
		Monitor,
		Leaf,
		Check,
		Save,
		LogIn,
		LogOut,
		Sparkles,
		Flame,
		CheckCircle2,
		AlertCircle,
		ChevronDown,
		Radio,
		Wifi,
		Eye,
		EyeOff,
		Info
	} from '@lucide/svelte';

	import { SvelteURL } from 'svelte/reactivity';

	type SettingsTab = 'units' | 'defaults' | 'connectivity' | 'account';

	let activeTab = $state<SettingsTab>('units');

	// Profile form state
	let displayName = $state(auth.user?.displayName || '');
	let isSavingProfile = $state(false);
	let profileSuccess = $state(false);
	let profileError = $state<string | null>(null);

	// Defaults form state (in display units)
	let defaultBatchSizeDisplay = $state(
		toDisplayVolume(settings.defaultBatchSize, settings.volumeUnit)
	);
	let defaultEfficiency = $state(settings.defaultEfficiency);
	let defaultBoilTime = $state(settings.defaultBoilTime);
	let isSavingDefaults = $state(false);
	let defaultsSuccess = $state(false);
	let defaultsError = $state<string | null>(null);

	// Derived dirty-state for defaults form
	let isDefaultsDirty = $derived.by(() => {
		const currentDisplay = toDisplayVolume(settings.defaultBatchSize, settings.volumeUnit);
		return (
			Number(defaultBatchSizeDisplay) !== Number(currentDisplay) ||
			Number(defaultEfficiency) !== Number(settings.defaultEfficiency) ||
			Number(defaultBoilTime) !== Number(settings.defaultBoilTime)
		);
	});

	// Connectivity form state
	let mqttHost = $state(settings.mqttHost);
	let mqttPort = $state<number | ''>(settings.mqttPort || 1883);
	let mqttUsername = $state(settings.mqttUsername);
	let mqttPassword = $state(settings.mqttPassword);
	let mqttCertificate = $state(settings.mqttCertificate);
	let mqttTopicPrefix = $state(settings.mqttTopicPrefix || 'brewyou/equipment');
	let showMqttPassword = $state(false);
	let isSavingConnectivity = $state(false);
	let isTestingMqtt = $state(false);
	let testMqttMessage = $state<{ text: string; ok: boolean } | null>(null);
	let connectivitySuccess = $state(false);
	let connectivityError = $state<string | null>(null);

	function setTab(tab: SettingsTab) {
		activeTab = tab;
		if (typeof window !== 'undefined') {
			const url = new SvelteURL(window.location.href);
			if (tab === 'units') {
				url.searchParams.delete('tab');
			} else {
				url.searchParams.set('tab', tab);
			}
			url.hash = '';
			window.history.replaceState({}, '', url);
		}
	}

	onMount(() => {
		if (auth.user?.displayName && !displayName) {
			displayName = auth.user.displayName;
		}
		defaultBatchSizeDisplay = toDisplayVolume(settings.defaultBatchSize, settings.volumeUnit);
		mqttHost = settings.mqttHost;
		mqttPort = settings.mqttPort || 1883;
		mqttUsername = settings.mqttUsername;
		mqttPassword = settings.mqttPassword;
		mqttCertificate = settings.mqttCertificate;
		mqttTopicPrefix = settings.mqttTopicPrefix || 'brewyou/equipment';

		// Resolve initial tab from query param or URL hash
		const initialHash = typeof window !== 'undefined' ? window.location.hash : page.url.hash;
		const urlTab = page.url.searchParams.get('tab') as SettingsTab | null;
		if (urlTab && ['units', 'defaults', 'connectivity', 'account'].includes(urlTab)) {
			activeTab = urlTab;
		} else if (initialHash === '#connectivity') {
			activeTab = 'connectivity';
		} else if (initialHash === '#defaults') {
			activeTab = 'defaults';
		} else if (initialHash === '#account' || initialHash === '#profile') {
			activeTab = 'account';
		}

		const handleHashChange = () => {
			const hash = window.location.hash;
			if (hash === '#connectivity') activeTab = 'connectivity';
			else if (hash === '#defaults') activeTab = 'defaults';
			else if (hash === '#account' || hash === '#profile') activeTab = 'account';
			else if (hash === '#units') activeTab = 'units';
		};
		if (typeof window !== 'undefined') {
			window.addEventListener('hashchange', handleHashChange);
		}

		if (settings.hasMqttConfigured) {
			void settings.checkMqttStatus();
		}
		settings.startMqttHeartbeat();

		return () => {
			if (typeof window !== 'undefined') {
				window.removeEventListener('hashchange', handleHashChange);
			}
		};
	});

	async function handleSaveProfile(e: SubmitEvent) {
		e.preventDefault();
		if (!auth.isAuthenticated) return;

		isSavingProfile = true;
		profileSuccess = false;
		profileError = null;

		const ok = await auth.updateProfile(displayName);
		isSavingProfile = false;

		if (ok) {
			profileSuccess = true;
			setTimeout(() => {
				profileSuccess = false;
			}, 3500);
		} else {
			profileError = auth.error || 'Failed to update profile.';
		}
	}

	async function handleSaveDefaults(e: SubmitEvent) {
		e.preventDefault();
		isSavingDefaults = true;
		defaultsSuccess = false;
		defaultsError = null;

		try {
			const canonicalBatchLiters = fromDisplayVolume(
				Number(defaultBatchSizeDisplay),
				settings.volumeUnit
			);
			settings.defaultBatchSize = canonicalBatchLiters;
			settings.defaultEfficiency = Number(defaultEfficiency);
			settings.defaultBoilTime = Number(defaultBoilTime);

			await settings.syncAllToBackend();
			defaultsSuccess = true;
			setTimeout(() => {
				defaultsSuccess = false;
			}, 3500);
		} catch (err: unknown) {
			defaultsError = (err as Error).message || t('settings.defaults.error_toast');
		} finally {
			isSavingDefaults = false;
		}
	}

	async function handleSaveConnectivity(e: SubmitEvent) {
		e.preventDefault();
		isSavingConnectivity = true;
		connectivitySuccess = false;
		connectivityError = null;

		try {
			const portNum = Number(mqttPort);
			if (mqttHost.trim() && (isNaN(portNum) || portNum < 1 || portNum > 65535)) {
				throw new Error('MQTT port must be between 1 and 65,535.');
			}

			await settings.setMqttSettings(
				mqttHost.trim(),
				portNum || 1883,
				mqttUsername.trim(),
				mqttPassword,
				mqttCertificate.trim(),
				mqttTopicPrefix.trim() || 'brewyou/equipment'
			);
			connectivitySuccess = true;
			setTimeout(() => {
				connectivitySuccess = false;
			}, 3500);
		} catch (err: unknown) {
			connectivityError = (err as Error).message || t('settings.connectivity.error_toast');
		} finally {
			isSavingConnectivity = false;
		}
	}

	async function handleTestConnectivity() {
		isTestingMqtt = true;
		testMqttMessage = null;
		connectivityError = null;

		try {
			const portNum = Number(mqttPort) || 1883;
			const isConnected = await settings.checkMqttStatus(mqttHost.trim(), portNum);
			if (isConnected) {
				testMqttMessage = { text: t('settings.connectivity.test_success'), ok: true };
			} else {
				testMqttMessage = { text: t('settings.connectivity.test_failed'), ok: false };
			}
		} catch {
			testMqttMessage = { text: t('settings.connectivity.test_failed'), ok: false };
		} finally {
			isTestingMqtt = false;
			setTimeout(() => {
				testMqttMessage = null;
			}, 5000);
		}
	}

	async function updateLanguage(code: LocaleCode) {
		i18n.setLocale(code);
		if (auth.isAuthenticated) {
			await auth.updatePreferences({ language: code });
		}
	}

	async function updateTheme(pref: ThemePreference) {
		settings.setThemePreference(pref);
		if (auth.isAuthenticated) {
			await auth.updatePreferences({ theme: pref });
		}
	}

	async function updateVolumeUnit(unit: VolumeUnit) {
		await settings.setVolumeUnit(unit);
		defaultBatchSizeDisplay = toDisplayVolume(settings.defaultBatchSize, settings.volumeUnit);
	}

	async function updateGrainWeightUnit(unit: GrainWeightUnit) {
		settings.setGrainWeightUnit(unit);
		if (auth.isAuthenticated) {
			await auth.updatePreferences({ weightUnit: settings.weightUnit });
		}
	}

	async function updateHopWeightUnit(unit: HopWeightUnit) {
		settings.setHopWeightUnit(unit);
		if (auth.isAuthenticated) {
			await auth.updatePreferences({ weightUnit: settings.weightUnit });
		}
	}

	function updateColorUnit(unit: ColorUnit) {
		settings.setColorUnit(unit);
	}

	function updateCarbonationUnit(unit: CarbonationUnit) {
		settings.setCarbonationUnit(unit);
	}

	function updateBitternessFormula(formula: BitternessFormula) {
		settings.setBitternessFormula(formula);
	}

	function updateColorFormula(formula: ColorFormula) {
		settings.setColorFormula(formula);
	}

	function updateAbvFormula(formula: AbvFormula) {
		settings.setAbvFormula(formula);
	}

	async function updateTemperatureUnit(unit: TemperatureUnit) {
		settings.setTemperatureUnit(unit);
		if (auth.isAuthenticated) {
			await auth.updatePreferences({ temperatureUnit: unit });
		}
	}

	async function updateGravityUnit(unit: GravityUnit) {
		settings.setGravityUnit(unit);
		if (auth.isAuthenticated) {
			await auth.updatePreferences({ gravityUnit: unit });
		}
	}

	const PRESET_CONFIGS: Record<
		Exclude<UnitPreset, 'Custom'>,
		{
			volumeUnit: VolumeUnit;
			grainWeightUnit: GrainWeightUnit;
			hopWeightUnit: HopWeightUnit;
			colorUnit: ColorUnit;
			temperatureUnit: TemperatureUnit;
			gravityUnit: GravityUnit;
			carbonationUnit: CarbonationUnit;
		}
	> = {
		'European Metric': {
			volumeUnit: 'Liters',
			grainWeightUnit: 'kg',
			hopWeightUnit: 'g',
			colorUnit: 'EBC',
			temperatureUnit: 'Celsius',
			gravityUnit: 'SpecificGravity',
			carbonationUnit: 'Volumes'
		},
		'US Craft': {
			volumeUnit: 'Gallons',
			grainWeightUnit: 'lb',
			hopWeightUnit: 'oz',
			colorUnit: 'SRM',
			temperatureUnit: 'Fahrenheit',
			gravityUnit: 'SpecificGravity',
			carbonationUnit: 'Volumes'
		},
		'UK Traditional': {
			volumeUnit: 'Liters',
			grainWeightUnit: 'kg',
			hopWeightUnit: 'oz',
			colorUnit: 'EBC',
			temperatureUnit: 'Celsius',
			gravityUnit: 'SpecificGravity',
			carbonationUnit: 'Volumes'
		}
	};

	let activePreset = $derived.by<UnitPreset>(() => {
		for (const [name, cfg] of Object.entries(PRESET_CONFIGS) as [
			UnitPreset,
			(typeof PRESET_CONFIGS)['European Metric']
		][]) {
			const matches =
				settings.volumeUnit === cfg.volumeUnit &&
				settings.grainWeightUnit === cfg.grainWeightUnit &&
				settings.hopWeightUnit === cfg.hopWeightUnit &&
				settings.colorUnit === cfg.colorUnit &&
				settings.temperatureUnit === cfg.temperatureUnit &&
				settings.gravityUnit === cfg.gravityUnit &&
				settings.carbonationUnit === cfg.carbonationUnit;
			if (matches) return name;
		}
		return 'Custom';
	});

	function handleApplyPreset(preset: Exclude<UnitPreset, 'Custom'>) {
		settings.applyPreset(preset);
		defaultBatchSizeDisplay = toDisplayVolume(settings.defaultBatchSize, settings.volumeUnit);
	}

	// Dynamic live preview values
	const sampleBatchLiters = 20.0;
	const sampleMaltKg = 5.5;
	const sampleHopGrams = 85.0;
	const sampleMashTempC = 67.0;
	const sampleOgSg = 1.058;
	const sampleColorSrm = 6.5;
	const sampleCarbVols = 2.45;

	let previewBatch = $derived(formatVolume(sampleBatchLiters, settings.volumeUnit));
	let previewMalt = $derived(formatGrainWeight(sampleMaltKg, settings.grainWeightUnit));
	let previewHop = $derived(formatHopWeight(sampleHopGrams, settings.hopWeightUnit));
	let previewTemp = $derived(formatTemperature(sampleMashTempC, settings.temperatureUnit));
	let previewGravity = $derived(formatGravity(sampleOgSg, settings.gravityUnit));
	let previewColor = $derived(formatColor(sampleColorSrm, settings.colorUnit));
	let previewCarb = $derived(formatCarbonation(sampleCarbVols, settings.carbonationUnit));
</script>

<svelte:head>
	<title>{t('settings.title')} — BrewYou</title>
</svelte:head>

<div class="mx-auto max-w-5xl space-y-6 pb-16">
	<!-- Page Header -->
	<div class="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
		<div>
			<h1 class="text-2xl font-black tracking-tight text-zinc-900 sm:text-3xl dark:text-white">
				{t('settings.title')}
			</h1>
			<p class="mt-1 text-sm text-zinc-600 dark:text-zinc-400">
				{t('settings.subtitle')}
			</p>
		</div>

		{#if auth.isAuthenticated}
			<div class="flex items-center gap-2">
				<span
					class="inline-flex items-center gap-1.5 rounded-full border border-emerald-500/30 bg-emerald-500/10 px-3 py-1 text-xs font-semibold text-emerald-600 dark:text-emerald-400"
				>
					<span class="h-1.5 w-1.5 animate-pulse rounded-full bg-emerald-500"></span>
					{t('settings.profile.verified')}
				</span>
			</div>
		{/if}
	</div>

	<!-- Guest Preview Notice (if unauthenticated) -->
	{#if !auth.isAuthenticated}
		<div
			class="flex flex-col gap-4 rounded-2xl border border-amber-500/30 bg-amber-500/10 p-4 sm:flex-row sm:items-center sm:justify-between sm:p-5"
			role="alert"
		>
			<div class="flex items-start gap-3">
				<div class="mt-0.5 rounded-xl bg-amber-500/20 p-2 text-amber-600 dark:text-amber-400">
					<Sparkles class="h-5 w-5" />
				</div>
				<div>
					<h2 class="text-sm font-bold text-amber-900 dark:text-amber-200">
						{t('settings.guest_notice_title')}
					</h2>
					<p class="mt-0.5 text-xs text-amber-800/90 dark:text-amber-300/80">
						{t('settings.guest_notice_desc')}
					</p>
				</div>
			</div>
			<a
				href="/login"
				class="inline-flex items-center justify-center gap-2 rounded-xl bg-amber-500 px-4 py-2 text-xs font-bold text-zinc-950 shadow-sm transition hover:bg-amber-400 active:scale-95"
			>
				<LogIn class="h-4 w-4" />
				<span>{t('settings.guest_signin')}</span>
			</a>
		</div>
	{/if}

	<!-- Navigation Tabs -->
	<div
		role="tablist"
		aria-label={t('settings.tabs.aria_label')}
		class="glass-panel flex items-center gap-1.5 overflow-x-auto rounded-2xl border border-zinc-200/80 p-1.5 text-xs dark:border-white/[0.08]"
	>
		<!-- Tab 1: Display & Units -->
		<button
			type="button"
			role="tab"
			id="tab-units"
			aria-selected={activeTab === 'units'}
			aria-controls="panel-units"
			data-testid="tab-btn-units"
			onclick={() => setTab('units')}
			class="flex min-h-[40px] cursor-pointer items-center gap-2 rounded-xl px-4 py-2 font-bold whitespace-nowrap transition-all {activeTab ===
			'units'
				? 'bg-amber-500 text-zinc-950 shadow-sm'
				: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-200'}"
		>
			<Scale class="h-4 w-4" />
			<span>{t('settings.tabs.units')}</span>
		</button>

		<!-- Tab 2: Equipment Defaults -->
		<button
			type="button"
			role="tab"
			id="tab-defaults"
			aria-selected={activeTab === 'defaults'}
			aria-controls="panel-defaults"
			data-testid="tab-btn-defaults"
			onclick={() => setTab('defaults')}
			class="flex min-h-[40px] cursor-pointer items-center gap-2 rounded-xl px-4 py-2 font-bold whitespace-nowrap transition-all {activeTab ===
			'defaults'
				? 'bg-amber-500 text-zinc-950 shadow-sm'
				: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-200'}"
		>
			<Sliders class="h-4 w-4" />
			<span>{t('settings.tabs.defaults')}</span>
			{#if isDefaultsDirty}
				<span class="h-2 w-2 animate-pulse rounded-full bg-amber-400"></span>
			{/if}
		</button>

		<!-- Tab 3: Hardware & IoT -->
		<button
			type="button"
			role="tab"
			id="tab-connectivity"
			aria-selected={activeTab === 'connectivity'}
			aria-controls="panel-connectivity"
			data-testid="tab-btn-connectivity"
			onclick={() => setTab('connectivity')}
			class="flex min-h-[40px] cursor-pointer items-center gap-2 rounded-xl px-4 py-2 font-bold whitespace-nowrap transition-all {activeTab ===
			'connectivity'
				? 'bg-amber-500 text-zinc-950 shadow-sm'
				: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-200'}"
		>
			<Radio class="h-4 w-4" />
			<span>{t('settings.tabs.connectivity')}</span>
			{#if settings.hasMqttConfigured}
				<span
					class="h-2 w-2 rounded-full {settings.mqttConnected === false
						? 'bg-red-400'
						: 'bg-emerald-400'}"
				></span>
			{/if}
		</button>

		<!-- Tab 4: Account & Profile -->
		<button
			type="button"
			role="tab"
			id="tab-account"
			aria-selected={activeTab === 'account'}
			aria-controls="panel-account"
			data-testid="tab-btn-account"
			onclick={() => setTab('account')}
			class="flex min-h-[40px] cursor-pointer items-center gap-2 rounded-xl px-4 py-2 font-bold whitespace-nowrap transition-all {activeTab ===
			'account'
				? 'bg-amber-500 text-zinc-950 shadow-sm'
				: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-200'}"
		>
			<User class="h-4 w-4" />
			<span>{t('settings.tabs.account')}</span>
		</button>
	</div>

	<!-- TAB PANEL 1: DISPLAY & UNITS -->
	{#if activeTab === 'units'}
		<div
			id="panel-units"
			role="tabpanel"
			aria-labelledby="tab-units"
			class="grid grid-cols-1 gap-8 lg:grid-cols-3"
		>
			<!-- Main Settings (2 cols) -->
			<div class="space-y-8 lg:col-span-2">
				<!-- Section: Appearance & UI Mode -->
				<section
					class="glass-panel rounded-2xl border border-zinc-200/80 p-5 sm:p-6 dark:border-white/[0.08]"
					aria-labelledby="appearance-heading"
				>
					<div
						class="flex items-center justify-between border-b border-zinc-200/80 pb-4 dark:border-white/[0.08]"
					>
						<div class="flex items-center gap-3">
							<div
								class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
							>
								<Palette class="h-5 w-5" />
							</div>
							<div>
								<div class="flex items-center gap-2">
									<h2
										id="appearance-heading"
										class="text-base font-bold text-zinc-900 dark:text-white"
									>
										{t('settings.appearance.title')}
									</h2>
								</div>
								<p class="text-xs text-zinc-500 dark:text-zinc-400">
									{t('settings.appearance.desc')}
								</p>
							</div>
						</div>
						<span
							class="rounded-full bg-zinc-100 px-2.5 py-0.5 text-[10px] font-medium text-zinc-500 dark:bg-zinc-800/80 dark:text-zinc-400"
						>
							{t('settings.appearance.auto_save_hint')}
						</span>
					</div>

					<div
						class="mt-5 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4"
						role="radiogroup"
						aria-label="Theme preference"
					>
						<!-- Dark Mode -->
						<button
							type="button"
							role="radio"
							aria-checked={settings.themePreference === 'Dark'}
							data-testid="theme-card-dark"
							onclick={() => updateTheme('Dark')}
							class="group relative flex flex-col items-start rounded-2xl border p-4 text-left transition-all {settings.themePreference ===
							'Dark'
								? 'border-amber-500/60 bg-amber-500/10 shadow-[0_0_15px_rgba(245,158,11,0.15)] ring-1 ring-amber-500'
								: 'border-zinc-200/80 bg-zinc-100/50 hover:border-zinc-300 dark:border-white/5 dark:bg-zinc-900/40 dark:hover:border-white/10'}"
						>
							<div class="flex w-full items-center justify-between">
								<div
									class="flex h-8 w-8 items-center justify-center rounded-lg bg-zinc-900 text-amber-400 shadow"
								>
									<Moon class="h-4 w-4" />
								</div>
								{#if settings.themePreference === 'Dark'}
									<Check class="h-4 w-4 text-amber-500 dark:text-amber-400" />
								{/if}
							</div>
							<div class="mt-3 text-xs font-semibold text-zinc-900 dark:text-white">
								{t('settings.appearance.theme_dark')}
							</div>
							<div class="mt-1 text-[11px] text-zinc-500 dark:text-zinc-400">
								{t('settings.appearance.theme_dark_desc')}
							</div>
						</button>

						<!-- Light Mode -->
						<button
							type="button"
							role="radio"
							aria-checked={settings.themePreference === 'Light'}
							data-testid="theme-card-light"
							onclick={() => updateTheme('Light')}
							class="group relative flex flex-col items-start rounded-2xl border p-4 text-left transition-all {settings.themePreference ===
							'Light'
								? 'border-amber-500/60 bg-amber-500/10 shadow-[0_0_15px_rgba(245,158,11,0.15)] ring-1 ring-amber-500'
								: 'border-zinc-200/80 bg-zinc-100/50 hover:border-zinc-300 dark:border-white/5 dark:bg-zinc-900/40 dark:hover:border-white/10'}"
						>
							<div class="flex w-full items-center justify-between">
								<div
									class="flex h-8 w-8 items-center justify-center rounded-lg bg-amber-100 text-amber-600 shadow"
								>
									<Sun class="h-4 w-4" />
								</div>
								{#if settings.themePreference === 'Light'}
									<Check class="h-4 w-4 text-amber-500 dark:text-amber-400" />
								{/if}
							</div>
							<div class="mt-3 text-xs font-semibold text-zinc-900 dark:text-white">
								{t('settings.appearance.theme_light')}
							</div>
							<div class="mt-1 text-[11px] text-zinc-500 dark:text-zinc-400">
								{t('settings.appearance.theme_light_desc')}
							</div>
						</button>

						<!-- Botanical Moss Mode -->
						<button
							type="button"
							role="radio"
							aria-checked={settings.themePreference === 'Botanical'}
							data-testid="theme-card-botanical"
							onclick={() => updateTheme('Botanical')}
							class="group relative flex flex-col items-start rounded-2xl border p-4 text-left transition-all {settings.themePreference ===
							'Botanical'
								? 'border-[#8eb63b]/70 bg-[#8eb63b]/15 shadow-[0_0_15px_rgba(142,182,59,0.2)] ring-1 ring-[#8eb63b]'
								: 'border-zinc-200/80 bg-zinc-100/50 hover:border-zinc-300 dark:border-white/5 dark:bg-zinc-900/40 dark:hover:border-white/10'}"
						>
							<div class="flex w-full items-center justify-between">
								<div
									class="flex h-8 w-8 items-center justify-center rounded-lg border border-[#8eb63b]/30 bg-[#131b15] text-[#8eb63b] shadow"
								>
									<Leaf class="h-4 w-4" />
								</div>
								{#if settings.themePreference === 'Botanical'}
									<Check class="h-4 w-4 text-[#8eb63b]" />
								{/if}
							</div>
							<div class="mt-3 text-xs font-semibold text-zinc-900 dark:text-white">
								{t('settings.appearance.theme_botanical')}
							</div>
							<div class="mt-1 text-[11px] text-zinc-500 dark:text-zinc-400">
								{t('settings.appearance.theme_botanical_desc')}
							</div>
						</button>

						<!-- System Match -->
						<button
							type="button"
							role="radio"
							aria-checked={settings.themePreference === 'System'}
							data-testid="theme-card-system"
							onclick={() => updateTheme('System')}
							class="group relative flex flex-col items-start rounded-2xl border p-4 text-left transition-all {settings.themePreference ===
							'System'
								? 'border-amber-500/60 bg-amber-500/10 shadow-[0_0_15px_rgba(245,158,11,0.15)] ring-1 ring-amber-500'
								: 'border-zinc-200/80 bg-zinc-100/50 hover:border-zinc-300 dark:border-white/5 dark:bg-zinc-900/40 dark:hover:border-white/10'}"
						>
							<div class="flex w-full items-center justify-between">
								<div
									class="flex h-8 w-8 items-center justify-center rounded-lg bg-zinc-200 text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
								>
									<Monitor class="h-4 w-4" />
								</div>
								{#if settings.themePreference === 'System'}
									<Check class="h-4 w-4 text-amber-500 dark:text-amber-400" />
								{/if}
							</div>
							<div class="mt-3 text-xs font-semibold text-zinc-900 dark:text-white">
								{t('settings.appearance.theme_system')}
							</div>
							<div class="mt-1 text-[11px] text-zinc-500 dark:text-zinc-400">
								{t('settings.appearance.theme_system_desc')}
							</div>
						</button>
					</div>
				</section>

				<!-- Section: Language & Regional -->
				<section
					class="glass-panel rounded-2xl border border-zinc-200/80 p-5 sm:p-6 dark:border-white/[0.08]"
					aria-labelledby="language-heading"
				>
					<div
						class="flex items-center justify-between border-b border-zinc-200/80 pb-4 dark:border-white/[0.08]"
					>
						<div class="flex items-center gap-3">
							<div
								class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
							>
								<Globe class="h-5 w-5" />
							</div>
							<div>
								<h2 id="language-heading" class="text-base font-bold text-zinc-900 dark:text-white">
									{t('settings.language.title')}
								</h2>
								<p class="text-xs text-zinc-500 dark:text-zinc-400">
									{t('settings.language.desc')}
								</p>
							</div>
						</div>
						<span
							class="rounded-full bg-zinc-100 px-2.5 py-0.5 text-[10px] font-medium text-zinc-500 dark:bg-zinc-800/80 dark:text-zinc-400"
						>
							{t('settings.language.auto_save_hint')}
						</span>
					</div>

					<div class="mt-5 max-w-sm">
						<label
							for="language-select"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('settings.language.title')}
						</label>
						<div class="relative mt-2">
							<select
								id="language-select"
								data-testid="language-select"
								value={i18n.locale}
								onchange={(e) =>
									updateLanguage((e.currentTarget as HTMLSelectElement).value as LocaleCode)}
								class="h-11 w-full appearance-none rounded-xl border border-zinc-300 bg-zinc-50/50 px-4 pr-10 text-sm font-medium text-zinc-900 transition focus:border-amber-500 focus:bg-white focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900/50 dark:text-white dark:focus:border-amber-400 dark:focus:bg-zinc-900"
							>
								{#each supportedLocales as loc}
									<option value={loc.code}>
										{loc.flag}
										{loc.label} ({loc.code === 'en' ? 'English' : 'Svenska'})
									</option>
								{/each}
							</select>
							<div
								class="pointer-events-none absolute inset-y-0 right-0 flex items-center pr-3.5 text-zinc-400"
							>
								<ChevronDown class="h-4 w-4" />
							</div>
						</div>
					</div>
				</section>

				<!-- Section: Brewery Units & Measurements -->
				<section
					class="glass-panel rounded-2xl border border-zinc-200/80 p-5 sm:p-6 dark:border-white/[0.08]"
					aria-labelledby="units-heading"
				>
					<div
						class="flex items-center justify-between border-b border-zinc-200/80 pb-4 dark:border-white/[0.08]"
					>
						<div class="flex items-center gap-3">
							<div
								class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
							>
								<Scale class="h-5 w-5" />
							</div>
							<div>
								<h2 id="units-heading" class="text-base font-bold text-zinc-900 dark:text-white">
									{t('settings.units.title')}
								</h2>
								<p class="text-xs text-zinc-500 dark:text-zinc-400">
									{t('settings.units.desc')}
								</p>
							</div>
						</div>
						<span
							class="rounded-full bg-zinc-100 px-2.5 py-0.5 text-[10px] font-medium text-zinc-500 dark:bg-zinc-800/80 dark:text-zinc-400"
						>
							{t('settings.units.auto_save_hint')}
						</span>
					</div>

					<div class="mt-6 space-y-6">
						<!-- Presets Toolbar -->
						<div
							class="rounded-xl border border-zinc-200/80 bg-zinc-50/50 p-4 dark:border-white/5 dark:bg-zinc-900/30"
						>
							<div class="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
								<div>
									<span
										class="block text-xs font-bold tracking-wider text-zinc-700 uppercase dark:text-zinc-200"
									>
										{t('settings.presets.title')}
									</span>
									<p class="text-[11px] text-zinc-500 dark:text-zinc-400">
										{t('settings.presets.desc')}
									</p>
								</div>
								<div class="flex items-center gap-2">
									<span
										data-testid="active-preset-badge"
										class="rounded-full px-2.5 py-0.5 text-[11px] font-semibold {activePreset ===
										'Custom'
											? 'border border-amber-500/20 bg-amber-500/10 text-amber-600 dark:text-amber-400'
											: 'border border-emerald-500/20 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400'}"
									>
										{activePreset === 'Custom'
											? t('settings.presets.custom_badge')
											: t('settings.presets.active_preset', { name: activePreset })}
									</span>
								</div>
							</div>
							<div class="mt-3 grid grid-cols-1 gap-2.5 sm:grid-cols-3">
								<button
									type="button"
									data-testid="preset-european-metric"
									onclick={() => handleApplyPreset('European Metric')}
									class="flex flex-col items-start rounded-lg border p-2.5 text-left transition {activePreset ===
									'European Metric'
										? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
										: 'border-zinc-200 bg-white/70 hover:bg-zinc-100 dark:border-zinc-800 dark:bg-zinc-800/40 dark:hover:bg-zinc-800/70'}"
								>
									<div class="flex w-full items-center justify-between">
										<span class="text-xs font-bold text-zinc-900 dark:text-white"
											>{t('settings.presets.european_metric')}</span
										>
										{#if activePreset === 'European Metric'}
											<Check class="h-3.5 w-3.5 text-amber-500" />
										{/if}
									</div>
									<span class="mt-0.5 text-[10px] text-zinc-500 dark:text-zinc-400"
										>{t('settings.presets.european_metric_desc')}</span
									>
								</button>

								<button
									type="button"
									data-testid="preset-us-craft"
									onclick={() => handleApplyPreset('US Craft')}
									class="flex flex-col items-start rounded-lg border p-2.5 text-left transition {activePreset ===
									'US Craft'
										? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
										: 'border-zinc-200 bg-white/70 hover:bg-zinc-100 dark:border-zinc-800 dark:bg-zinc-800/40 dark:hover:bg-zinc-800/70'}"
								>
									<div class="flex w-full items-center justify-between">
										<span class="text-xs font-bold text-zinc-900 dark:text-white"
											>{t('settings.presets.us_craft')}</span
										>
										{#if activePreset === 'US Craft'}
											<Check class="h-3.5 w-3.5 text-amber-500" />
										{/if}
									</div>
									<span class="mt-0.5 text-[10px] text-zinc-500 dark:text-zinc-400"
										>{t('settings.presets.us_craft_desc')}</span
									>
								</button>

								<button
									type="button"
									data-testid="preset-uk-traditional"
									onclick={() => handleApplyPreset('UK Traditional')}
									class="flex flex-col items-start rounded-lg border p-2.5 text-left transition {activePreset ===
									'UK Traditional'
										? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
										: 'border-zinc-200 bg-white/70 hover:bg-zinc-100 dark:border-zinc-800 dark:bg-zinc-800/40 dark:hover:bg-zinc-800/70'}"
								>
									<div class="flex w-full items-center justify-between">
										<span class="text-xs font-bold text-zinc-900 dark:text-white"
											>{t('settings.presets.uk_traditional')}</span
										>
										{#if activePreset === 'UK Traditional'}
											<Check class="h-3.5 w-3.5 text-amber-500" />
										{/if}
									</div>
									<span class="mt-0.5 text-[10px] text-zinc-500 dark:text-zinc-400"
										>{t('settings.presets.uk_traditional_desc')}</span
									>
								</button>
							</div>
						</div>

						<!-- Volume -->
						<div>
							<span
								class="block text-xs font-bold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
							>
								{t('settings.units.volume')}
							</span>
							<div class="mt-2 grid grid-cols-1 gap-3 sm:grid-cols-2" role="radiogroup">
								<button
									type="button"
									role="radio"
									aria-checked={settings.volumeUnit === 'Liters'}
									data-testid="unit-volume-liters"
									onclick={() => updateVolumeUnit('Liters')}
									class="flex items-start justify-between rounded-xl border p-3.5 text-left transition {settings.volumeUnit ===
									'Liters'
										? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
										: 'border-zinc-200 bg-zinc-100/60 dark:border-zinc-800 dark:bg-zinc-900/40'}"
								>
									<div>
										<div class="text-xs font-bold text-zinc-900 dark:text-white">
											{t('settings.units.volume_liters')}
										</div>
										<div class="mt-0.5 text-[11px] text-zinc-500 dark:text-zinc-400">
											{t('settings.units.volume_liters_desc')}
										</div>
									</div>
									{#if settings.volumeUnit === 'Liters'}
										<Check class="h-4 w-4 text-amber-500" />
									{/if}
								</button>

								<button
									type="button"
									role="radio"
									aria-checked={settings.volumeUnit === 'Gallons'}
									data-testid="unit-volume-gallons"
									onclick={() => updateVolumeUnit('Gallons')}
									class="flex items-start justify-between rounded-xl border p-3.5 text-left transition {settings.volumeUnit ===
									'Gallons'
										? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
										: 'border-zinc-200 bg-zinc-100/60 dark:border-zinc-800 dark:bg-zinc-900/40'}"
								>
									<div>
										<div class="text-xs font-bold text-zinc-900 dark:text-white">
											{t('settings.units.volume_gallons')}
										</div>
										<div class="mt-0.5 text-[11px] text-zinc-500 dark:text-zinc-400">
											{t('settings.units.volume_gallons_desc')}
										</div>
									</div>
									{#if settings.volumeUnit === 'Gallons'}
										<Check class="h-4 w-4 text-amber-500" />
									{/if}
								</button>
							</div>
						</div>

						<!-- Weight Units (Composite) -->
						<div>
							<div class="flex items-center justify-between">
								<span
									class="block text-xs font-bold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
								>
									{t('settings.units.weight')}
								</span>
							</div>

							<div class="mt-2 grid grid-cols-1 gap-3 sm:grid-cols-2" role="radiogroup">
								<button
									type="button"
									role="radio"
									aria-checked={settings.weightUnit === 'Metric'}
									data-testid="unit-weight-metric"
									onclick={() => {
										updateGrainWeightUnit('kg');
										updateHopWeightUnit('g');
									}}
									class="flex items-start justify-between rounded-xl border p-3.5 text-left transition {settings.weightUnit ===
									'Metric'
										? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
										: 'border-zinc-200 bg-zinc-100/60 dark:border-zinc-800 dark:bg-zinc-900/40'}"
								>
									<div>
										<div class="text-xs font-bold text-zinc-900 dark:text-white">
											{t('settings.units.weight_metric')}
										</div>
										<div class="mt-0.5 text-[11px] text-zinc-500 dark:text-zinc-400">
											{t('settings.units.weight_metric_desc')}
										</div>
									</div>
									{#if settings.weightUnit === 'Metric'}
										<Check class="h-4 w-4 text-amber-500" />
									{/if}
								</button>

								<button
									type="button"
									role="radio"
									aria-checked={settings.weightUnit === 'Imperial'}
									data-testid="unit-weight-imperial"
									onclick={() => {
										updateGrainWeightUnit('lb');
										updateHopWeightUnit('oz');
									}}
									class="flex items-start justify-between rounded-xl border p-3.5 text-left transition {settings.weightUnit ===
									'Imperial'
										? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
										: 'border-zinc-200 bg-zinc-100/60 dark:border-zinc-800 dark:bg-zinc-900/40'}"
								>
									<div>
										<div class="text-xs font-bold text-zinc-900 dark:text-white">
											{t('settings.units.weight_imperial')}
										</div>
										<div class="mt-0.5 text-[11px] text-zinc-500 dark:text-zinc-400">
											{t('settings.units.weight_imperial_desc')}
										</div>
									</div>
									{#if settings.weightUnit === 'Imperial'}
										<Check class="h-4 w-4 text-amber-500" />
									{/if}
								</button>
							</div>

							<!-- Fine-Grained Weight Breakdown (Grain vs Hop) -->
							<div
								class="mt-4 grid grid-cols-1 gap-4 rounded-xl border border-zinc-200/60 bg-zinc-50/40 p-3.5 sm:grid-cols-2 dark:border-white/5 dark:bg-zinc-900/20"
							>
								<div>
									<span class="block text-[11px] font-semibold text-zinc-600 dark:text-zinc-400">
										{t('settings.units.grain_weight')}
									</span>
									<div class="mt-1.5 flex gap-2">
										<button
											type="button"
											onclick={() => updateGrainWeightUnit('kg')}
											class="flex-1 rounded-lg border py-1.5 text-center text-xs font-bold transition {settings.grainWeightUnit ===
											'kg'
												? 'border-amber-500 bg-amber-500/20 text-amber-600 dark:text-amber-400'
												: 'border-zinc-200 bg-white text-zinc-700 dark:border-zinc-800 dark:bg-zinc-800/60 dark:text-zinc-300'}"
										>
											{t('settings.units.grain_kg')}
										</button>
										<button
											type="button"
											onclick={() => updateGrainWeightUnit('lb')}
											class="flex-1 rounded-lg border py-1.5 text-center text-xs font-bold transition {settings.grainWeightUnit ===
											'lb'
												? 'border-amber-500 bg-amber-500/20 text-amber-600 dark:text-amber-400'
												: 'border-zinc-200 bg-white text-zinc-700 dark:border-zinc-800 dark:bg-zinc-800/60 dark:text-zinc-300'}"
										>
											{t('settings.units.grain_lb')}
										</button>
									</div>
								</div>

								<div>
									<span class="block text-[11px] font-semibold text-zinc-600 dark:text-zinc-400">
										{t('settings.units.hop_weight')}
									</span>
									<div class="mt-1.5 flex gap-2">
										<button
											type="button"
											onclick={() => updateHopWeightUnit('g')}
											class="flex-1 rounded-lg border py-1.5 text-center text-xs font-bold transition {settings.hopWeightUnit ===
											'g'
												? 'border-amber-500 bg-amber-500/20 text-amber-600 dark:text-amber-400'
												: 'border-zinc-200 bg-white text-zinc-700 dark:border-zinc-800 dark:bg-zinc-800/60 dark:text-zinc-300'}"
										>
											{t('settings.units.hop_g')}
										</button>
										<button
											type="button"
											onclick={() => updateHopWeightUnit('oz')}
											class="flex-1 rounded-lg border py-1.5 text-center text-xs font-bold transition {settings.hopWeightUnit ===
											'oz'
												? 'border-amber-500 bg-amber-500/20 text-amber-600 dark:text-amber-400'
												: 'border-zinc-200 bg-white text-zinc-700 dark:border-zinc-800 dark:bg-zinc-800/60 dark:text-zinc-300'}"
										>
											{t('settings.units.hop_oz')}
										</button>
									</div>
								</div>
							</div>
						</div>

						<!-- Color Scale -->
						<div>
							<span
								class="block text-xs font-bold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
							>
								{t('settings.units.color')}
							</span>
							<div class="mt-2 grid grid-cols-1 gap-3 sm:grid-cols-2" role="radiogroup">
								<button
									type="button"
									role="radio"
									aria-checked={settings.colorUnit === 'EBC'}
									data-testid="unit-color-ebc"
									onclick={() => updateColorUnit('EBC')}
									class="flex items-start justify-between rounded-xl border p-3.5 text-left transition {settings.colorUnit ===
									'EBC'
										? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
										: 'border-zinc-200 bg-zinc-100/60 dark:border-zinc-800 dark:bg-zinc-900/40'}"
								>
									<div>
										<div class="text-xs font-bold text-zinc-900 dark:text-white">
											{t('settings.units.color_ebc')}
										</div>
										<div class="mt-0.5 text-[11px] text-zinc-500 dark:text-zinc-400">
											{t('settings.units.color_ebc_desc')}
										</div>
									</div>
									{#if settings.colorUnit === 'EBC'}
										<Check class="h-4 w-4 text-amber-500" />
									{/if}
								</button>

								<button
									type="button"
									role="radio"
									aria-checked={settings.colorUnit === 'SRM'}
									data-testid="unit-color-srm"
									onclick={() => updateColorUnit('SRM')}
									class="flex items-start justify-between rounded-xl border p-3.5 text-left transition {settings.colorUnit ===
									'SRM'
										? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
										: 'border-zinc-200 bg-zinc-100/60 dark:border-zinc-800 dark:bg-zinc-900/40'}"
								>
									<div>
										<div class="text-xs font-bold text-zinc-900 dark:text-white">
											{t('settings.units.color_srm')}
										</div>
										<div class="mt-0.5 text-[11px] text-zinc-500 dark:text-zinc-400">
											{t('settings.units.color_srm_desc')}
										</div>
									</div>
									{#if settings.colorUnit === 'SRM'}
										<Check class="h-4 w-4 text-amber-500" />
									{/if}
								</button>
							</div>
						</div>

						<!-- Carbonation Unit -->
						<div>
							<span
								class="block text-xs font-bold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
							>
								{t('settings.units.carbonation')}
							</span>
							<div class="mt-2 grid grid-cols-1 gap-3 sm:grid-cols-2" role="radiogroup">
								<button
									type="button"
									role="radio"
									aria-checked={settings.carbonationUnit === 'Volumes'}
									data-testid="unit-carb-volumes"
									onclick={() => updateCarbonationUnit('Volumes')}
									class="flex items-start justify-between rounded-xl border p-3.5 text-left transition {settings.carbonationUnit ===
									'Volumes'
										? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
										: 'border-zinc-200 bg-zinc-100/60 dark:border-zinc-800 dark:bg-zinc-900/40'}"
								>
									<div>
										<div class="text-xs font-bold text-zinc-900 dark:text-white">
											{t('settings.units.carbonation_volumes')}
										</div>
										<div class="mt-0.5 text-[11px] text-zinc-500 dark:text-zinc-400">
											{t('settings.units.carbonation_volumes_desc')}
										</div>
									</div>
									{#if settings.carbonationUnit === 'Volumes'}
										<Check class="h-4 w-4 text-amber-500" />
									{/if}
								</button>

								<button
									type="button"
									role="radio"
									aria-checked={settings.carbonationUnit === 'GramsPerLiter'}
									data-testid="unit-carb-gl"
									onclick={() => updateCarbonationUnit('GramsPerLiter')}
									class="flex items-start justify-between rounded-xl border p-3.5 text-left transition {settings.carbonationUnit ===
									'GramsPerLiter'
										? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
										: 'border-zinc-200 bg-zinc-100/60 dark:border-zinc-800 dark:bg-zinc-900/40'}"
								>
									<div>
										<div class="text-xs font-bold text-zinc-900 dark:text-white">
											{t('settings.units.carbonation_g_l')}
										</div>
										<div class="mt-0.5 text-[11px] text-zinc-500 dark:text-zinc-400">
											{t('settings.units.carbonation_g_l_desc')}
										</div>
									</div>
									{#if settings.carbonationUnit === 'GramsPerLiter'}
										<Check class="h-4 w-4 text-amber-500" />
									{/if}
								</button>
							</div>
						</div>

						<!-- Temperature -->
						<div>
							<span
								class="block text-xs font-bold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
							>
								{t('settings.units.temperature')}
							</span>
							<div class="mt-2 grid grid-cols-1 gap-3 sm:grid-cols-2" role="radiogroup">
								<button
									type="button"
									role="radio"
									aria-checked={settings.temperatureUnit === 'Celsius'}
									data-testid="unit-temp-celsius"
									onclick={() => updateTemperatureUnit('Celsius')}
									class="flex items-start justify-between rounded-xl border p-3.5 text-left transition {settings.temperatureUnit ===
									'Celsius'
										? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
										: 'border-zinc-200 bg-zinc-100/60 dark:border-zinc-800 dark:bg-zinc-900/40'}"
								>
									<div>
										<div class="text-xs font-bold text-zinc-900 dark:text-white">
											{t('settings.units.temperature_celsius')}
										</div>
										<div class="mt-0.5 text-[11px] text-zinc-500 dark:text-zinc-400">
											{t('settings.units.temperature_celsius_desc')}
										</div>
									</div>
									{#if settings.temperatureUnit === 'Celsius'}
										<Check class="h-4 w-4 text-amber-500" />
									{/if}
								</button>

								<button
									type="button"
									role="radio"
									aria-checked={settings.temperatureUnit === 'Fahrenheit'}
									data-testid="unit-temp-fahrenheit"
									onclick={() => updateTemperatureUnit('Fahrenheit')}
									class="flex items-start justify-between rounded-xl border p-3.5 text-left transition {settings.temperatureUnit ===
									'Fahrenheit'
										? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
										: 'border-zinc-200 bg-zinc-100/60 dark:border-zinc-800 dark:bg-zinc-900/40'}"
								>
									<div>
										<div class="text-xs font-bold text-zinc-900 dark:text-white">
											{t('settings.units.temperature_fahrenheit')}
										</div>
										<div class="mt-0.5 text-[11px] text-zinc-500 dark:text-zinc-400">
											{t('settings.units.temperature_fahrenheit_desc')}
										</div>
									</div>
									{#if settings.temperatureUnit === 'Fahrenheit'}
										<Check class="h-4 w-4 text-amber-500" />
									{/if}
								</button>
							</div>
						</div>

						<!-- Gravity -->
						<div>
							<span
								class="block text-xs font-bold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
							>
								{t('settings.units.gravity')}
							</span>
							<div class="mt-2 grid grid-cols-1 gap-3 sm:grid-cols-2" role="radiogroup">
								<button
									type="button"
									role="radio"
									aria-checked={settings.gravityUnit === 'SpecificGravity'}
									data-testid="unit-gravity-sg"
									onclick={() => updateGravityUnit('SpecificGravity')}
									class="flex items-start justify-between rounded-xl border p-3.5 text-left transition {settings.gravityUnit ===
									'SpecificGravity'
										? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
										: 'border-zinc-200 bg-zinc-100/60 dark:border-zinc-800 dark:bg-zinc-900/40'}"
								>
									<div>
										<div class="text-xs font-bold text-zinc-900 dark:text-white">
											{t('settings.units.gravity_sg')}
										</div>
										<div class="mt-0.5 text-[11px] text-zinc-500 dark:text-zinc-400">
											{t('settings.units.gravity_sg_desc')}
										</div>
									</div>
									{#if settings.gravityUnit === 'SpecificGravity'}
										<Check class="h-4 w-4 text-amber-500" />
									{/if}
								</button>

								<button
									type="button"
									role="radio"
									aria-checked={settings.gravityUnit === 'Plato'}
									data-testid="unit-gravity-plato"
									onclick={() => updateGravityUnit('Plato')}
									class="flex items-start justify-between rounded-xl border p-3.5 text-left transition {settings.gravityUnit ===
									'Plato'
										? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
										: 'border-zinc-200 bg-zinc-100/60 dark:border-zinc-800 dark:bg-zinc-900/40'}"
								>
									<div>
										<div class="text-xs font-bold text-zinc-900 dark:text-white">
											{t('settings.units.gravity_plato')}
										</div>
										<div class="mt-0.5 text-[11px] text-zinc-500 dark:text-zinc-400">
											{t('settings.units.gravity_plato_desc')}
										</div>
									</div>
									{#if settings.gravityUnit === 'Plato'}
										<Check class="h-4 w-4 text-amber-500" />
									{/if}
								</button>
							</div>
						</div>
					</div>
				</section>

				<!-- Section: Brewing Calculation Models (Formulas) -->
				<section
					class="glass-panel rounded-2xl border border-zinc-200/80 p-5 sm:p-6 dark:border-white/[0.08]"
					aria-labelledby="formulas-heading"
				>
					<div
						class="flex items-center justify-between border-b border-zinc-200/80 pb-4 dark:border-white/[0.08]"
					>
						<div class="flex items-center gap-3">
							<div
								class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
							>
								<Sparkles class="h-5 w-5" />
							</div>
							<div>
								<h2 id="formulas-heading" class="text-base font-bold text-zinc-900 dark:text-white">
									{t('settings.formulas.title')}
								</h2>
								<p class="text-xs text-zinc-500 dark:text-zinc-400">
									{t('settings.formulas.desc')}
								</p>
							</div>
						</div>
						<span
							class="rounded-full bg-zinc-100 px-2.5 py-0.5 text-[10px] font-medium text-zinc-500 dark:bg-zinc-800/80 dark:text-zinc-400"
						>
							{t('settings.formulas.auto_save_hint')}
						</span>
					</div>

					<div class="mt-6 space-y-6">
						<!-- Bitterness Formula -->
						<div>
							<span
								class="block text-xs font-bold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
							>
								{t('settings.formulas.bitterness')}
							</span>
							<div class="mt-2 grid grid-cols-1 gap-3 sm:grid-cols-3" role="radiogroup">
								{#each ['Tinseth', 'Rager', 'Daniels'] as const as form}
									<button
										type="button"
										role="radio"
										aria-checked={settings.bitternessFormula === form}
										data-testid="formula-bitterness-{form.toLowerCase()}"
										onclick={() => updateBitternessFormula(form)}
										class="flex items-start justify-between rounded-xl border p-3 text-left transition {settings.bitternessFormula ===
										form
											? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
											: 'border-zinc-200 bg-zinc-100/60 dark:border-zinc-800 dark:bg-zinc-900/40'}"
									>
										<div>
											<div class="text-xs font-bold text-zinc-900 dark:text-white">
												{t(`settings.formulas.bitterness_${form.toLowerCase()}`)}
											</div>
										</div>
										{#if settings.bitternessFormula === form}
											<Check class="h-4 w-4 text-amber-500" />
										{/if}
									</button>
								{/each}
							</div>
						</div>

						<!-- Color Formula -->
						<div>
							<span
								class="block text-xs font-bold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
							>
								{t('settings.formulas.color')}
							</span>
							<div class="mt-2 grid grid-cols-1 gap-3 sm:grid-cols-3" role="radiogroup">
								{#each ['Morey', 'Mosher', 'Daniels'] as const as form}
									<button
										type="button"
										role="radio"
										aria-checked={settings.colorFormula === form}
										data-testid="formula-color-{form.toLowerCase()}"
										onclick={() => updateColorFormula(form)}
										class="flex items-start justify-between rounded-xl border p-3 text-left transition {settings.colorFormula ===
										form
											? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
											: 'border-zinc-200 bg-zinc-100/60 dark:border-zinc-800 dark:bg-zinc-900/40'}"
									>
										<div>
											<div class="text-xs font-bold text-zinc-900 dark:text-white">
												{t(`settings.formulas.color_${form.toLowerCase()}`)}
											</div>
										</div>
										{#if settings.colorFormula === form}
											<Check class="h-4 w-4 text-amber-500" />
										{/if}
									</button>
								{/each}
							</div>
						</div>

						<!-- ABV Formula -->
						<div>
							<span
								class="block text-xs font-bold tracking-wider text-zinc-500 uppercase dark:text-zinc-400"
							>
								{t('settings.formulas.abv')}
							</span>
							<div class="mt-2 grid grid-cols-1 gap-3 sm:grid-cols-2" role="radiogroup">
								{#each ['Linear', 'Advanced'] as const as form}
									<button
										type="button"
										role="radio"
										aria-checked={settings.abvFormula === form}
										data-testid="formula-abv-{form.toLowerCase()}"
										onclick={() => updateAbvFormula(form)}
										class="flex items-start justify-between rounded-xl border p-3.5 text-left transition {settings.abvFormula ===
										form
											? 'border-amber-500/60 bg-amber-500/10 ring-1 ring-amber-500'
											: 'border-zinc-200 bg-zinc-100/60 dark:border-zinc-800 dark:bg-zinc-900/40'}"
									>
										<div>
											<div class="text-xs font-bold text-zinc-900 dark:text-white">
												{t(`settings.formulas.abv_${form.toLowerCase()}`)}
											</div>
										</div>
										{#if settings.abvFormula === form}
											<Check class="h-4 w-4 text-amber-500" />
										{/if}
									</button>
								{/each}
							</div>
						</div>
					</div>
				</section>
			</div>

			<!-- Right Column: Interactive Live Preview (1 col) -->
			<div class="space-y-6">
				<!-- Live Interactive Preview Card -->
				<div
					class="glass-panel sticky top-20 rounded-2xl border border-amber-500/30 bg-gradient-to-br from-amber-500/5 via-transparent to-amber-500/10 p-5 shadow-lg dark:border-amber-500/20"
					data-testid="settings-live-preview"
				>
					<div class="flex items-center gap-2 text-amber-600 dark:text-amber-400">
						<Flame class="h-5 w-5 animate-pulse" />
						<h3 class="text-xs font-bold tracking-wider uppercase">
							{t('settings.units.preview_title')}
						</h3>
					</div>
					<p class="mt-1 text-xs text-zinc-500 dark:text-zinc-400">
						{t('settings.units.preview_desc')}
					</p>

					<!-- Recipe Simulation Preview Box -->
					<div
						class="mt-4 rounded-xl border border-zinc-200/80 bg-zinc-100/80 p-4 dark:border-white/10 dark:bg-zinc-900/80"
					>
						<div
							class="flex items-center justify-between border-b border-zinc-200/60 pb-2 dark:border-white/5"
						>
							<span class="text-xs font-bold text-zinc-900 dark:text-white"
								>Citra Sunshine Hazy IPA</span
							>
							<span
								class="rounded bg-amber-500/20 px-1.5 py-0.5 font-mono text-[10px] font-bold text-amber-600 dark:text-amber-400"
							>
								BJCP 21A
							</span>
						</div>

						<div class="mt-3 space-y-2.5 text-xs">
							<div class="flex items-center justify-between">
								<span class="text-zinc-500 dark:text-zinc-400"
									>{t('settings.units.preview_batch')}</span
								>
								<span
									class="font-mono font-bold text-zinc-900 dark:text-zinc-100"
									data-testid="preview-batch"
								>
									{previewBatch}
								</span>
							</div>

							<div class="flex items-center justify-between">
								<span class="text-zinc-500 dark:text-zinc-400"
									>{t('settings.units.preview_malt')}</span
								>
								<span
									class="font-mono font-bold text-zinc-900 dark:text-zinc-100"
									data-testid="preview-malt"
								>
									{previewMalt}
								</span>
							</div>

							<div class="flex items-center justify-between">
								<span class="text-zinc-500 dark:text-zinc-400"
									>{t('settings.units.preview_hop')}</span
								>
								<span
									class="font-mono font-bold text-zinc-900 dark:text-zinc-100"
									data-testid="preview-hop"
								>
									{previewHop}
								</span>
							</div>

							<div class="flex items-center justify-between">
								<span class="text-zinc-500 dark:text-zinc-400"
									>{t('settings.units.preview_mash')}</span
								>
								<span
									class="font-mono font-bold text-zinc-900 dark:text-zinc-100"
									data-testid="preview-temp"
								>
									{previewTemp}
								</span>
							</div>

							<div class="flex items-center justify-between">
								<span class="text-zinc-500 dark:text-zinc-400"
									>{t('settings.units.preview_og')}</span
								>
								<span
									class="font-mono font-bold text-zinc-900 dark:text-zinc-100"
									data-testid="preview-gravity"
								>
									{previewGravity}
								</span>
							</div>

							<div class="flex items-center justify-between">
								<span class="text-zinc-500 dark:text-zinc-400"
									>{t('settings.units.preview_color')}</span
								>
								<span
									class="font-mono font-bold text-zinc-900 dark:text-zinc-100"
									data-testid="preview-color"
								>
									{previewColor}
								</span>
							</div>

							<div class="flex items-center justify-between">
								<span class="text-zinc-500 dark:text-zinc-400"
									>{t('settings.units.preview_carb')}</span
								>
								<span
									class="font-mono font-bold text-zinc-900 dark:text-zinc-100"
									data-testid="preview-carbonation"
								>
									{previewCarb}
								</span>
							</div>
						</div>
					</div>
				</div>
			</div>
		</div>
	{/if}

	<!-- TAB PANEL 2: EQUIPMENT DEFAULTS -->
	{#if activeTab === 'defaults'}
		<div
			id="panel-defaults"
			role="tabpanel"
			aria-labelledby="tab-defaults"
			class="max-w-3xl space-y-6"
		>
			<section
				class="glass-panel rounded-2xl border border-zinc-200/80 p-5 sm:p-6 dark:border-white/[0.08]"
				aria-labelledby="defaults-heading"
			>
				<div
					class="flex items-center gap-3 border-b border-zinc-200/80 pb-4 dark:border-white/[0.08]"
				>
					<div
						class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
					>
						<Sliders class="h-5 w-5" />
					</div>
					<div>
						<h2 id="defaults-heading" class="text-base font-bold text-zinc-900 dark:text-white">
							{t('settings.defaults.title')}
						</h2>
						<p class="text-xs text-zinc-500 dark:text-zinc-400">
							{t('settings.defaults.desc')}
						</p>
					</div>
				</div>

				<!-- Explanatory Scope Callout -->
				<div
					class="mt-4 rounded-xl border border-amber-500/20 bg-amber-500/5 p-3.5 text-xs text-amber-800 dark:text-amber-300"
				>
					<div class="flex items-start gap-2.5">
						<Info class="mt-0.5 h-4 w-4 flex-shrink-0 text-amber-600 dark:text-amber-400" />
						<p class="leading-relaxed">
							{t('settings.defaults.baseline_note')}
						</p>
					</div>
				</div>

				<form onsubmit={handleSaveDefaults} class="mt-5 space-y-4">
					<div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
						<!-- Batch Size -->
						<div>
							<label
								for="default-batch-size-input"
								class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('settings.defaults.batch_size')} ({settings.unitShort})
							</label>
							<input
								id="default-batch-size-input"
								data-testid="default-batch-size-input"
								type="number"
								step="0.1"
								min="1"
								max="10000"
								bind:value={defaultBatchSizeDisplay}
								required
								class="mt-1.5 w-full rounded-xl border border-zinc-300 bg-zinc-50/50 px-3.5 py-2 text-sm text-zinc-900 transition focus:border-amber-500 focus:bg-white focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900/50 dark:text-white dark:focus:border-amber-400 dark:focus:bg-zinc-900"
							/>
						</div>

						<!-- Efficiency -->
						<div>
							<label
								for="default-efficiency-input"
								class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('settings.defaults.efficiency')} (%)
							</label>
							<input
								id="default-efficiency-input"
								data-testid="default-efficiency-input"
								type="number"
								step="0.5"
								min="10"
								max="100"
								bind:value={defaultEfficiency}
								required
								class="mt-1.5 w-full rounded-xl border border-zinc-300 bg-zinc-50/50 px-3.5 py-2 text-sm text-zinc-900 transition focus:border-amber-500 focus:bg-white focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900/50 dark:text-white dark:focus:border-amber-400 dark:focus:bg-zinc-900"
							/>
							<span class="mt-1 block text-[11px] text-zinc-400 dark:text-zinc-500">
								{t('settings.defaults.efficiency_help')}
							</span>
						</div>

						<!-- Boil Time -->
						<div>
							<label
								for="default-boil-time-input"
								class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('settings.defaults.boil_time')} (min)
							</label>
							<input
								id="default-boil-time-input"
								data-testid="default-boil-time-input"
								type="number"
								step="5"
								min="0"
								max="360"
								bind:value={defaultBoilTime}
								required
								class="mt-1.5 w-full rounded-xl border border-zinc-300 bg-zinc-50/50 px-3.5 py-2 text-sm text-zinc-900 transition focus:border-amber-500 focus:bg-white focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900/50 dark:text-white dark:focus:border-amber-400 dark:focus:bg-zinc-900"
							/>
							<span class="mt-1 block text-[11px] text-zinc-400 dark:text-zinc-500">
								{t('settings.defaults.boil_time_help')}
							</span>
						</div>
					</div>

					{#if defaultsSuccess}
						<div
							class="flex items-center gap-2 rounded-xl border border-emerald-500/30 bg-emerald-500/10 px-3 py-2 text-xs font-medium text-emerald-600 dark:text-emerald-400"
							role="status"
						>
							<CheckCircle2 class="h-4 w-4 flex-shrink-0" />
							<span>{t('settings.defaults.saved_toast')}</span>
						</div>
					{/if}

					{#if defaultsError}
						<div
							class="flex items-center gap-2 rounded-xl border border-red-500/30 bg-red-500/10 px-3 py-2 text-xs font-medium text-red-600 dark:text-red-400"
							role="alert"
						>
							<AlertCircle class="h-4 w-4 flex-shrink-0" />
							<span>{defaultsError}</span>
						</div>
					{/if}

					<div
						class="flex items-center justify-between border-t border-zinc-200/80 pt-4 dark:border-white/[0.08]"
					>
						<div>
							{#if isDefaultsDirty}
								<span
									class="inline-flex items-center gap-1.5 text-xs font-semibold text-amber-600 dark:text-amber-400"
								>
									<span class="h-1.5 w-1.5 animate-pulse rounded-full bg-amber-500"></span>
									{t('settings.defaults.unsaved_changes')}
								</span>
							{/if}
						</div>

						<button
							type="submit"
							data-testid="save-defaults-btn"
							disabled={isSavingDefaults || !isDefaultsDirty}
							class="inline-flex items-center gap-2 rounded-xl bg-amber-500 px-4 py-2 text-xs font-bold text-zinc-950 shadow-sm transition hover:bg-amber-400 active:scale-95 disabled:cursor-not-allowed disabled:opacity-50"
						>
							{#if isSavingDefaults}
								<span>{t('settings.defaults.saving')}</span>
							{:else}
								<Save class="h-4 w-4" />
								<span>{t('settings.defaults.save_all')}</span>
							{/if}
						</button>
					</div>
				</form>
			</section>
		</div>
	{/if}

	<!-- TAB PANEL 3: HARDWARE & IOT -->
	{#if activeTab === 'connectivity'}
		<div
			id="panel-connectivity"
			role="tabpanel"
			aria-labelledby="tab-connectivity"
			class="max-w-3xl space-y-6"
		>
			<section
				id="connectivity"
				class="glass-panel rounded-2xl border border-zinc-200/80 p-5 sm:p-6 dark:border-white/[0.08]"
				aria-labelledby="connectivity-heading"
			>
				<div
					class="flex items-center justify-between border-b border-zinc-200/80 pb-4 dark:border-white/[0.08]"
				>
					<div class="flex items-center gap-3">
						<div
							class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
						>
							<Radio class="h-5 w-5" />
						</div>
						<div>
							<h2
								id="connectivity-heading"
								class="text-base font-bold text-zinc-900 dark:text-white"
							>
								{t('settings.connectivity.title')}
							</h2>
							<p class="text-xs text-zinc-500 dark:text-zinc-400">
								{t('settings.connectivity.desc')}
							</p>
						</div>
					</div>

					<span
						data-testid="connectivity-status-badge"
						class="inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-semibold {!settings.hasMqttConfigured
							? 'border-zinc-300 bg-zinc-100 text-zinc-600 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-400'
							: settings.mqttConnected === false
								? 'border-red-500/30 bg-red-500/10 text-red-600 dark:text-red-400'
								: 'border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400'}"
					>
						<span
							class="h-1.5 w-1.5 rounded-full {!settings.hasMqttConfigured
								? 'bg-zinc-400'
								: settings.mqttConnected === false
									? 'bg-red-500'
									: 'animate-pulse bg-emerald-500'}"
						></span>
						{!settings.hasMqttConfigured
							? t('settings.connectivity.status_not_configured')
							: settings.mqttConnected === false
								? t('settings.connectivity.status_configured_disconnected', {
										host: settings.mqttHost,
										port: settings.mqttPort
									})
								: t('settings.connectivity.status_configured', {
										host: settings.mqttHost,
										port: settings.mqttPort
									})}
					</span>
				</div>

				<form onsubmit={handleSaveConnectivity} class="mt-5 space-y-5">
					<div
						class="rounded-xl border border-zinc-200/60 bg-zinc-50/50 p-4 dark:border-white/5 dark:bg-white/[0.02]"
					>
						<div class="flex items-center gap-2">
							<Wifi class="h-4 w-4 text-amber-500" />
							<h3 class="text-xs font-bold tracking-wider text-zinc-900 uppercase dark:text-white">
								{t('settings.connectivity.mqtt_title')}
							</h3>
						</div>
						<p class="mt-1 text-xs text-zinc-500 dark:text-zinc-400">
							{t('settings.connectivity.mqtt_desc')}
						</p>

						<div class="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-3">
							<!-- Host -->
							<div class="sm:col-span-2">
								<label
									for="mqtt-host-input"
									class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
								>
									{t('settings.connectivity.host')}
								</label>
								<input
									id="mqtt-host-input"
									data-testid="mqtt-host-input"
									type="text"
									bind:value={mqttHost}
									placeholder={t('settings.connectivity.host_placeholder')}
									class="mt-1.5 w-full rounded-xl border border-zinc-300 bg-zinc-50/50 px-3.5 py-2 font-mono text-sm text-zinc-900 transition focus:border-amber-500 focus:bg-white focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900/50 dark:text-white dark:focus:border-amber-400 dark:focus:bg-zinc-900"
								/>
							</div>

							<!-- Port -->
							<div>
								<label
									for="mqtt-port-input"
									class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
								>
									{t('settings.connectivity.port')}
								</label>
								<input
									id="mqtt-port-input"
									data-testid="mqtt-port-input"
									type="number"
									min="1"
									max="65535"
									bind:value={mqttPort}
									placeholder={t('settings.connectivity.port_placeholder')}
									class="mt-1.5 w-full rounded-xl border border-zinc-300 bg-zinc-50/50 px-3.5 py-2 font-mono text-sm text-zinc-900 transition focus:border-amber-500 focus:bg-white focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900/50 dark:text-white dark:focus:border-amber-400 dark:focus:bg-zinc-900"
								/>
							</div>
						</div>

						<div class="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2">
							<!-- Username -->
							<div>
								<label
									for="mqtt-username-input"
									class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
								>
									{t('settings.connectivity.username')}
								</label>
								<input
									id="mqtt-username-input"
									data-testid="mqtt-username-input"
									type="text"
									bind:value={mqttUsername}
									placeholder={t('settings.connectivity.username_placeholder')}
									class="mt-1.5 w-full rounded-xl border border-zinc-300 bg-zinc-50/50 px-3.5 py-2 text-sm text-zinc-900 transition focus:border-amber-500 focus:bg-white focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900/50 dark:text-white dark:focus:border-amber-400 dark:focus:bg-zinc-900"
								/>
							</div>

							<!-- Password -->
							<div>
								<label
									for="mqtt-password-input"
									class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
								>
									{t('settings.connectivity.password')}
								</label>
								<div class="relative mt-1.5">
									<input
										id="mqtt-password-input"
										data-testid="mqtt-password-input"
										type={showMqttPassword ? 'text' : 'password'}
										bind:value={mqttPassword}
										placeholder={settings.hasMqttPassword
											? t('settings.connectivity.password_saved')
											: t('settings.connectivity.password_placeholder')}
										class="w-full rounded-xl border border-zinc-300 bg-zinc-50/50 px-3.5 py-2 pr-10 text-sm text-zinc-900 transition focus:border-amber-500 focus:bg-white focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900/50 dark:text-white dark:focus:border-amber-400 dark:focus:bg-zinc-900"
									/>
									<button
										type="button"
										onclick={() => (showMqttPassword = !showMqttPassword)}
										class="absolute inset-y-0 right-0 flex items-center pr-3 text-zinc-400 hover:text-zinc-600 dark:hover:text-zinc-200"
										title={showMqttPassword
											? t('settings.connectivity.hide_password')
											: t('settings.connectivity.show_password')}
									>
										{#if showMqttPassword}
											<EyeOff class="h-4 w-4" />
										{:else}
											<Eye class="h-4 w-4" />
										{/if}
									</button>
								</div>
								{#if settings.hasMqttPassword && !mqttPassword}
									<span class="mt-1 block text-[11px] text-emerald-600 dark:text-emerald-400">
										✓ {t('settings.connectivity.password_saved')}
									</span>
								{/if}
							</div>
						</div>

						<!-- Certificate -->
						<div class="mt-4">
							<label
								for="mqtt-certificate-input"
								class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('settings.connectivity.certificate')}
							</label>
							<textarea
								id="mqtt-certificate-input"
								data-testid="mqtt-certificate-input"
								rows="3"
								bind:value={mqttCertificate}
								placeholder={t('settings.connectivity.certificate_placeholder')}
								class="mt-1.5 w-full rounded-xl border border-zinc-300 bg-zinc-50/50 p-3 font-mono text-xs text-zinc-900 placeholder-zinc-400 transition focus:border-amber-500 focus:bg-white focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900/50 dark:text-white dark:placeholder-zinc-600 dark:focus:border-amber-400 dark:focus:bg-zinc-900"
							></textarea>
							<span class="mt-1 block text-[11px] text-zinc-400 dark:text-zinc-500">
								{t('settings.connectivity.certificate_help')}
							</span>
						</div>

						<!-- Base Topic Prefix -->
						<div class="mt-4">
							<label
								for="mqtt-topic-prefix-input"
								class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('settings.connectivity.topic_prefix')}
							</label>
							<input
								id="mqtt-topic-prefix-input"
								data-testid="mqtt-topic-prefix-input"
								type="text"
								bind:value={mqttTopicPrefix}
								placeholder={t('settings.connectivity.topic_prefix_placeholder')}
								class="mt-1.5 w-full rounded-xl border border-zinc-300 bg-zinc-50/50 px-3.5 py-2 font-mono text-sm text-zinc-900 transition focus:border-amber-500 focus:bg-white focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900/50 dark:text-white dark:focus:border-amber-400 dark:focus:bg-zinc-900"
							/>
							<span class="mt-1 block text-[11px] text-zinc-400 dark:text-zinc-500">
								{t('settings.connectivity.topic_prefix_help')}
							</span>
						</div>
					</div>

					{#if connectivitySuccess}
						<div
							class="flex items-center gap-2 rounded-xl border border-emerald-500/30 bg-emerald-500/10 px-3 py-2 text-xs font-medium text-emerald-600 dark:text-emerald-400"
							role="status"
						>
							<CheckCircle2 class="h-4 w-4 flex-shrink-0" />
							<span>{t('settings.connectivity.saved_toast')}</span>
						</div>
					{/if}

					{#if connectivityError}
						<div
							class="flex items-center gap-2 rounded-xl border border-red-500/30 bg-red-500/10 px-3 py-2 text-xs font-medium text-red-600 dark:text-red-400"
							role="alert"
						>
							<AlertCircle class="h-4 w-4 flex-shrink-0" />
							<span>{connectivityError}</span>
						</div>
					{/if}

					{#if testMqttMessage}
						<div
							class="flex items-center gap-2 rounded-xl border px-3 py-2 text-xs font-medium {testMqttMessage.ok
								? 'border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400'
								: 'border-red-500/30 bg-red-500/10 text-red-600 dark:text-red-400'}"
							role={testMqttMessage.ok ? 'status' : 'alert'}
						>
							{#if testMqttMessage.ok}
								<CheckCircle2 class="h-4 w-4 flex-shrink-0 text-emerald-500" />
							{:else}
								<AlertCircle class="h-4 w-4 flex-shrink-0 text-red-500" />
							{/if}
							<span>{testMqttMessage.text}</span>
						</div>
					{/if}

					<div class="flex items-center justify-between gap-3 pt-2">
						<button
							type="button"
							data-testid="test-connectivity-btn"
							disabled={isTestingMqtt || !mqttHost.trim()}
							onclick={handleTestConnectivity}
							class="inline-flex items-center gap-2 rounded-xl border border-zinc-300 bg-white px-3.5 py-2 text-xs font-semibold text-zinc-700 shadow-xs transition hover:bg-zinc-50 active:scale-95 disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
						>
							{#if isTestingMqtt}
								<span
									class="h-3 w-3 animate-spin rounded-full border-2 border-amber-500 border-t-transparent"
								></span>
								<span>{t('settings.connectivity.testing')}</span>
							{:else}
								<Wifi class="h-3.5 w-3.5 text-amber-500" />
								<span>{t('settings.connectivity.test_connection')}</span>
							{/if}
						</button>

						<button
							type="submit"
							data-testid="save-connectivity-btn"
							disabled={isSavingConnectivity}
							class="inline-flex items-center gap-2 rounded-xl bg-amber-500 px-4 py-2 text-xs font-bold text-zinc-950 shadow-sm transition hover:bg-amber-400 active:scale-95 disabled:opacity-50"
						>
							{#if isSavingConnectivity}
								<span>{t('settings.connectivity.saving')}</span>
							{:else}
								<Save class="h-4 w-4" />
								<span>{t('settings.connectivity.save')}</span>
							{/if}
						</button>
					</div>
				</form>
			</section>
		</div>
	{/if}

	<!-- TAB PANEL 4: ACCOUNT & PROFILE -->
	{#if activeTab === 'account'}
		<div
			id="panel-account"
			role="tabpanel"
			aria-labelledby="tab-account"
			class="max-w-3xl space-y-6"
		>
			{#if auth.isAuthenticated && auth.user}
				<!-- Brewer Profile Form -->
				<section
					class="glass-panel rounded-2xl border border-zinc-200/80 p-5 sm:p-6 dark:border-white/[0.08]"
					aria-labelledby="profile-heading"
				>
					<div
						class="flex items-center gap-3 border-b border-zinc-200/80 pb-4 dark:border-white/[0.08]"
					>
						<div
							class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400"
						>
							<User class="h-5 w-5" />
						</div>
						<div>
							<h2 id="profile-heading" class="text-base font-bold text-zinc-900 dark:text-white">
								{t('settings.profile.title')}
							</h2>
							<p class="text-xs text-zinc-500 dark:text-zinc-400">
								{t('settings.profile.desc')}
							</p>
						</div>
					</div>

					<form onsubmit={handleSaveProfile} class="mt-5 space-y-4">
						<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
							<!-- Display Name -->
							<div>
								<label
									for="display-name-input"
									class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
								>
									{t('settings.profile.display_name')}
								</label>
								<input
									id="display-name-input"
									type="text"
									bind:value={displayName}
									maxlength={100}
									required
									placeholder="e.g. Master Brewer"
									class="mt-1.5 w-full rounded-xl border border-zinc-300 bg-zinc-50/50 px-3.5 py-2 text-sm text-zinc-900 transition focus:border-amber-500 focus:bg-white focus:ring-1 focus:ring-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900/50 dark:text-white dark:focus:border-amber-400 dark:focus:bg-zinc-900"
								/>
								<span class="mt-1 block text-[11px] text-zinc-400 dark:text-zinc-500">
									{t('settings.profile.display_name_help')}
								</span>
							</div>

							<!-- Email (Read-only) -->
							<div>
								<label
									for="email-display"
									class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
								>
									{t('settings.profile.email')}
								</label>
								<input
									id="email-display"
									type="email"
									value={auth.user.email}
									disabled
									class="mt-1.5 w-full cursor-not-allowed rounded-xl border border-zinc-200 bg-zinc-100/70 px-3.5 py-2 text-sm text-zinc-500 dark:border-zinc-800 dark:bg-zinc-900/30 dark:text-zinc-400"
								/>
								<span class="mt-1 block text-[11px] text-zinc-400 dark:text-zinc-500">
									{t('settings.profile.member_since')}: {new Date(
										auth.user.createdAt || Date.now()
									).toLocaleDateString()}
								</span>
							</div>
						</div>

						<!-- Alerts & Save Button -->
						{#if profileSuccess}
							<div
								class="flex items-center gap-2 rounded-xl border border-emerald-500/30 bg-emerald-500/10 px-3 py-2 text-xs font-medium text-emerald-600 dark:text-emerald-400"
								role="status"
							>
								<CheckCircle2 class="h-4 w-4 flex-shrink-0" />
								<span>{t('settings.profile.profile_saved')}</span>
							</div>
						{/if}

						{#if profileError}
							<div
								class="flex items-center gap-2 rounded-xl border border-red-500/30 bg-red-500/10 px-3 py-2 text-xs font-medium text-red-600 dark:text-red-400"
								role="alert"
							>
								<AlertCircle class="h-4 w-4 flex-shrink-0" />
								<span>{profileError}</span>
							</div>
						{/if}

						<div class="flex justify-end pt-2">
							<button
								type="submit"
								disabled={isSavingProfile}
								class="inline-flex items-center gap-2 rounded-xl bg-amber-500 px-4 py-2 text-xs font-bold text-zinc-950 shadow-sm transition hover:bg-amber-400 active:scale-95 disabled:opacity-50"
							>
								{#if isSavingProfile}
									<span>{t('settings.profile.saving_profile')}</span>
								{:else}
									<Save class="h-4 w-4" />
									<span>{t('settings.profile.save_profile')}</span>
								{/if}
							</button>
						</div>
					</form>
				</section>

				<!-- Active Session & Logout Card -->
				<section
					class="glass-panel rounded-2xl border border-zinc-200/80 p-5 sm:p-6 dark:border-white/[0.08]"
					aria-labelledby="session-heading"
				>
					<div class="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
						<div>
							<h2 id="session-heading" class="text-base font-bold text-zinc-900 dark:text-white">
								{t('settings.profile.session_title')}
							</h2>
							<p class="text-xs text-zinc-500 dark:text-zinc-400">
								{t('settings.profile.session_desc')}
							</p>
							<p class="mt-2 text-xs text-zinc-700 dark:text-zinc-300">
								{t('settings.profile.signed_in_as')}
								<span class="font-bold text-zinc-900 dark:text-white">{auth.user.email}</span>
							</p>
						</div>

						<button
							type="button"
							data-testid="session-logout-btn"
							onclick={() => auth.logout()}
							class="inline-flex items-center justify-center gap-2 rounded-xl border border-red-500/30 bg-red-500/10 px-4 py-2.5 text-xs font-semibold text-red-500 transition hover:bg-red-500/20 active:scale-95"
						>
							<LogOut class="h-4 w-4" />
							<span>{t('nav.logout')}</span>
						</button>
					</div>
				</section>
			{:else}
				<!-- Guest Sign-in Invite in Account Tab -->
				<section
					class="glass-panel rounded-2xl border border-amber-500/30 bg-amber-500/10 p-5 text-center sm:p-6"
				>
					<User class="mx-auto h-10 w-10 text-amber-600 dark:text-amber-400" />
					<h2 class="mt-3 text-base font-bold text-zinc-900 dark:text-white">
						{t('settings.guest_notice_title')}
					</h2>
					<p class="mx-auto mt-1 max-w-md text-xs text-zinc-600 dark:text-zinc-400">
						{t('settings.guest_notice_desc')}
					</p>
					<div class="mt-4">
						<a
							href="/login"
							class="inline-flex items-center gap-2 rounded-xl bg-amber-500 px-5 py-2.5 text-xs font-bold text-zinc-950 shadow-sm transition hover:bg-amber-400 active:scale-95"
						>
							<LogIn class="h-4 w-4" />
							<span>{t('settings.guest_signin')}</span>
						</a>
					</div>
				</section>
			{/if}
		</div>
	{/if}
</div>
