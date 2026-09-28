<script lang="ts">
	import { onMount } from 'svelte';
	import { api } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import { formatNumber } from '$lib/utils/formatNumber';
	import { auth } from '$lib/stores/auth.svelte';
	import { brewery, DEFAULT_BREWERY_ID } from '$lib/stores/brewery.svelte';
	import { settings, litersToGallons } from '$lib/stores/settings.svelte';
	import {
		type EquipmentDto,
		type EquipmentType,
		type EquipmentSubtype,
		type EquipmentTelemetryUpdateDto
	} from '$lib/types/api';
	import EquipmentModal from '$lib/components/inventory/EquipmentModal.svelte';
	import DeleteEquipmentModal from '$lib/components/inventory/DeleteEquipmentModal.svelte';
	import VesselIcon from '$lib/components/inventory/VesselIcon.svelte';
	import { isVesselFillable } from '$lib/components/inventory/vessel-geometry';
	import {
		Wrench,
		Plus,
		Search,
		Loader2,
		Trash2,
		Pencil,
		LogIn,
		Building2,
		Thermometer,
		Wifi,
		Gauge,
		Droplets,
		Battery,
		ChevronDown,
		ChevronUp
	} from '@lucide/svelte';

	let equipmentList = $state<EquipmentDto[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);
	let selectedCategory = $state<string>('All');
	let searchFilter = $state('');

	let modalOpen = $state(false);
	let editingItem = $state<EquipmentDto | null>(null);
	let deleteModalOpen = $state(false);
	let itemToDelete = $state<EquipmentDto | null>(null);
	let expandedAttributes = $state<Record<string, boolean>>({});

	let telemetryAbortController: AbortController | null = null;
	let telemetryReconnectTimeout: ReturnType<typeof setTimeout> | null = null;
	let isUnmounted = false;

	function toggleAttributes(equipmentId: string) {
		expandedAttributes[equipmentId] = !expandedAttributes[equipmentId];
	}

	function getParsedAttributes(item: EquipmentDto): Array<{ key: string; value: string }> {
		if (!item.latestMetricsJson) return [];
		try {
			const parsed = JSON.parse(item.latestMetricsJson);
			if (typeof parsed !== 'object' || parsed === null) return [];
			const result: Array<{ key: string; value: string }> = [];
			for (const [k, v] of Object.entries(parsed)) {
				let formattedVal = '';
				if (typeof v === 'object' && v !== null) {
					formattedVal = JSON.stringify(v);
				} else {
					formattedVal = String(v);
				}
				result.push({ key: k, value: formattedVal });
			}
			return result;
		} catch {
			return [];
		}
	}

	async function runTelemetryLoop(signal: AbortSignal) {
		while (!signal.aborted && !isUnmounted && auth.isAuthenticated) {
			try {
				await api.equipment.streamTelemetry({
					signal,
					onReading: (reading: EquipmentTelemetryUpdateDto) => {
						const index = equipmentList.findIndex((e) => e.id === reading.equipmentId);
						if (index !== -1) {
							const existing = equipmentList[index];
							equipmentList[index] = {
								...existing,
								currentTemperatureC:
									reading.temperatureC !== undefined
										? reading.temperatureC
										: existing.currentTemperatureC,
								temperatureUpdatedAt: reading.timestamp,
								lastTelemetryAt: reading.timestamp,
								currentSpecificGravity:
									reading.specificGravity !== undefined && reading.specificGravity !== null
										? reading.specificGravity
										: existing.currentSpecificGravity,
								currentPressureBar:
									reading.pressureBar !== undefined && reading.pressureBar !== null
										? reading.pressureBar
										: existing.currentPressureBar,
								currentBatteryPercent:
									reading.batteryPercent !== undefined && reading.batteryPercent !== null
										? reading.batteryPercent
										: existing.currentBatteryPercent,
								currentBatteryVoltage:
									reading.batteryVoltage !== undefined && reading.batteryVoltage !== null
										? reading.batteryVoltage
										: existing.currentBatteryVoltage,
								latestMetricsJson:
									reading.metricsJson !== undefined && reading.metricsJson !== null
										? reading.metricsJson
										: existing.latestMetricsJson,
								currentVolumeLiters:
									reading.currentVolumeLiters !== undefined && reading.currentVolumeLiters !== null
										? reading.currentVolumeLiters
										: existing.currentVolumeLiters,
								fillPercentage:
									reading.fillPercentage !== undefined && reading.fillPercentage !== null
										? reading.fillPercentage
										: existing.fillPercentage
							};
						}
					},
					onError: () => {
						// Stream disconnected or errored; wait and reconnect below
					}
				});
			} catch {
				// Handled or network abort
			}

			if (signal.aborted || isUnmounted || !auth.isAuthenticated) {
				break;
			}

			// Wait 3 seconds before reconnecting
			await new Promise((resolve) => {
				telemetryReconnectTimeout = setTimeout(resolve, 3000);
			});
		}
	}

	function startTelemetryStream() {
		if (!auth.isAuthenticated || isUnmounted) return;
		stopTelemetryStream();

		telemetryAbortController = new AbortController();
		void runTelemetryLoop(telemetryAbortController.signal);
	}

	function stopTelemetryStream() {
		if (telemetryReconnectTimeout) {
			clearTimeout(telemetryReconnectTimeout);
			telemetryReconnectTimeout = null;
		}
		if (telemetryAbortController) {
			telemetryAbortController.abort();
			telemetryAbortController = null;
		}
	}

	async function loadEquipment() {
		if (!auth.isAuthenticated) {
			loading = false;
			return;
		}

		loading = true;
		error = null;
		try {
			if (brewery.activeSetupId === DEFAULT_BREWERY_ID) {
				await brewery.syncFromBackend();
			}

			const typeFilter =
				selectedCategory === 'All' ? undefined : (selectedCategory as EquipmentType);
			const setupIdFilter =
				brewery.activeSetupId && brewery.activeSetupId !== DEFAULT_BREWERY_ID
					? brewery.activeSetupId
					: undefined;

			equipmentList = await api.equipment.list(
				typeFilter,
				searchFilter,
				undefined,
				undefined,
				setupIdFilter
			);
		} catch (err: unknown) {
			error = (err as Error).message || t('equipment.loading');
		} finally {
			loading = false;
		}
	}

	onMount(() => {
		loadEquipment();
		startTelemetryStream();
		if (settings.hasMqttConfigured) {
			void settings.checkMqttStatus();
		}
		settings.startMqttHeartbeat();

		return () => {
			isUnmounted = true;
			stopTelemetryStream();
		};
	});

	$effect(() => {
		// Re-fetch when auth becomes available or active brewery setup changes
		if (auth.isAuthenticated) {
			void brewery.activeSetupId;
			loadEquipment();
			startTelemetryStream();
		} else {
			stopTelemetryStream();
		}
	});

	function handleCategoryChange(cat: string) {
		selectedCategory = cat;
		loadEquipment();
	}

	function handleSearchInput() {
		loadEquipment();
	}

	function openAddModal() {
		editingItem = null;
		modalOpen = true;
	}

	function openEditModal(item: EquipmentDto) {
		editingItem = item;
		modalOpen = true;
	}

	function handleSaved(saved: EquipmentDto) {
		const index = equipmentList.findIndex((e) => e.id === saved.id);
		if (index >= 0) {
			if (selectedCategory === 'All' || selectedCategory === saved.type) {
				equipmentList[index] = saved;
			} else {
				equipmentList = equipmentList.filter((e) => e.id !== saved.id);
			}
		} else {
			if (selectedCategory === 'All' || selectedCategory === saved.type) {
				equipmentList = [saved, ...equipmentList];
			}
		}
	}

	function requestDelete(item: EquipmentDto) {
		itemToDelete = item;
		deleteModalOpen = true;
	}

	function handleDeleted(equipmentId: string) {
		equipmentList = equipmentList.filter((e) => e.id !== equipmentId);
	}

	function getCategoryName(cat: string): string {
		switch (cat) {
			case 'All':
				return t('equipment.all_equipment');
			case 'Boiler':
				return t('equipment.boilers');
			case 'Fermenter':
				return t('equipment.fermenters');
			case 'Keg':
				return t('equipment.kegs');
			case 'Sensor':
				return t('equipment.sensors');
			case 'Other':
				return t('equipment.other');
			default:
				return cat;
		}
	}

	function getSubtypeName(subtype: EquipmentSubtype): string {
		return t(`equipment.subtypes.${subtype}`) || subtype;
	}

	function getTypeName(type: EquipmentType): string {
		return t(`equipment.types.${type}`) || type;
	}

	function getCategoryColor(cat: EquipmentType): string {
		switch (cat) {
			case 'Boiler':
				return 'border-orange-500/30 bg-orange-500/10 text-orange-600 dark:text-orange-400';
			case 'Fermenter':
				return 'border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400';
			case 'Keg':
				return 'border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400';
			case 'Sensor':
				return 'border-cyan-500/30 bg-cyan-500/10 text-cyan-600 dark:text-cyan-400';
			default:
				return 'border-zinc-500/30 bg-zinc-500/10 text-zinc-600 dark:text-zinc-400';
		}
	}

	function formatDisplayMetrics(e: EquipmentDto) {
		const pref = settings.volumeUnit;
		const fillPct = typeof e.fillPercentage === 'number' ? Math.round(e.fillPercentage) : 0;
		if (pref === 'Gallons') {
			const capGal = litersToGallons(e.capacityLiters);
			const curGal = litersToGallons(e.currentVolumeLiters);
			return {
				currentFormatted: `${formatNumber(curGal, 1)} gal`,
				capacityFormatted: `${formatNumber(capGal, 1)} gal`,
				primaryVal: `${formatNumber(curGal, 1)} / ${formatNumber(capGal, 1)} gal`,
				fillPct,
				unit: 'gal'
			};
		} else {
			return {
				currentFormatted: `${formatNumber(e.currentVolumeLiters, 1)} L`,
				capacityFormatted: `${formatNumber(e.capacityLiters, 1)} L`,
				primaryVal: `${formatNumber(e.currentVolumeLiters, 1)} / ${formatNumber(e.capacityLiters, 1)} L`,
				fillPct,
				unit: 'L'
			};
		}
	}

	function getFillStatusBadge(fillPct: number): { text: string; classes: string } {
		if (fillPct === 0) {
			return {
				text: t('equipment.fill_status_empty'),
				classes: 'border-zinc-500/20 bg-zinc-500/10 text-zinc-500 dark:text-zinc-400'
			};
		}
		if (fillPct >= 100) {
			return {
				text: t('equipment.fill_status_full'),
				classes: 'border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400'
			};
		}
		return {
			text: t('equipment.fill_status_pct', { pct: fillPct }),
			classes: 'border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400'
		};
	}

	function formatTemperatureBadge(e: EquipmentDto): {
		hasTemp: boolean;
		displayTemp: string;
		status: 'live' | 'recent' | 'stale' | 'none';
		statusText: string;
		dotColor: string;
	} {
		if (e.currentTemperatureC == null) {
			if (e.connectionType && e.connectionType !== 'None') {
				return {
					hasTemp: false,
					displayTemp: '',
					status: 'none',
					statusText: t('equipment.awaiting_telemetry'),
					dotColor: 'bg-zinc-400'
				};
			}
			return {
				hasTemp: false,
				displayTemp: '',
				status: 'none',
				statusText: '',
				dotColor: ''
			};
		}

		const displayTemp = settings.formatTemp(e.currentTemperatureC, 1);

		let status: 'live' | 'recent' | 'stale' = 'stale';
		let statusText = t('equipment.temp_stale');
		let dotColor = 'bg-zinc-400';

		if (e.temperatureUpdatedAt) {
			const diffMinutes = Math.floor(
				(Date.now() - new Date(e.temperatureUpdatedAt).getTime()) / (1000 * 60)
			);
			if (diffMinutes < 10) {
				status = 'live';
				statusText = t('equipment.temp_live', { mins: diffMinutes });
				dotColor = 'bg-emerald-500 animate-pulse';
			} else if (diffMinutes < 60) {
				status = 'recent';
				statusText = t('equipment.temp_recent', { mins: diffMinutes });
				dotColor = 'bg-amber-500';
			} else {
				const hours = Math.floor(diffMinutes / 60);
				statusText = t('equipment.temp_hours_ago', { hours });
			}
		}

		return {
			hasTemp: true,
			displayTemp,
			status,
			statusText,
			dotColor
		};
	}

	function isMqttConnected(item: EquipmentDto): boolean {
		if (item.connectionType !== 'Mqtt') return false;
		if (!item.connectionConfigJson) {
			return settings.mqttConnected ?? false;
		}
		try {
			const cfg = JSON.parse(item.connectionConfigJson);
			if (cfg.useUserSettings !== false) {
				return settings.mqttConnected ?? false;
			}
			if (
				cfg.brokerHost === settings.mqttHost &&
				(Number(cfg.brokerPort) || 1883) === settings.mqttPort
			) {
				return settings.mqttConnected ?? false;
			}
			return settings.mqttConnected ?? false;
		} catch {
			return settings.mqttConnected ?? false;
		}
	}
</script>

<div class="space-y-6">
	<!-- Page Header -->
	<div class="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
		<div>
			<div class="flex items-center gap-2.5">
				<div
					class="flex h-10 w-10 items-center justify-center rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-500"
				>
					<Wrench class="h-5 w-5" />
				</div>
				<div>
					<h1
						class="font-editorial text-3xl font-bold tracking-tight text-zinc-900 sm:text-4xl dark:text-white"
					>
						{t('equipment.title')}
					</h1>
					<div class="mt-0.5 flex items-center gap-1.5 text-xs text-zinc-600 dark:text-zinc-400">
						<Building2 class="h-3.5 w-3.5 text-amber-500" />
						<span
							>{t('brewery.active_setup_prefix')}:
							<strong class="font-semibold text-zinc-900 dark:text-zinc-200"
								>{brewery.activeSetup.name}</strong
							></span
						>
					</div>
				</div>
			</div>
			<p class="mt-1 text-sm text-zinc-600 dark:text-zinc-400">
				{t('equipment.subtitle')}
			</p>
		</div>

		<!-- Action Bar: Add Button -->
		<div class="flex flex-wrap items-center gap-3">
			{#if auth.isAuthenticated}
				<button
					type="button"
					onclick={openAddModal}
					data-testid="add-equipment-btn"
					class="flex min-h-[44px] cursor-pointer items-center gap-2 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-4 py-2.5 text-sm font-bold text-zinc-950 shadow-md transition-all hover:from-amber-400 hover:to-amber-500 active:scale-[0.98]"
				>
					<Plus class="h-4 w-4 stroke-[2.5]" />
					<span>{t('equipment.add_equipment')}</span>
				</button>
			{/if}
		</div>
	</div>

	{#if !auth.isAuthenticated}
		<!-- Unauthenticated State -->
		<div
			class="glass-panel flex flex-col items-center justify-center rounded-2xl border border-zinc-200/80 p-12 text-center dark:border-white/[0.08]"
		>
			<div
				class="flex h-14 w-14 items-center justify-center rounded-2xl border border-amber-500/30 bg-amber-500/10 text-amber-500"
			>
				<Wrench class="h-7 w-7" />
			</div>
			<h2 class="mt-4 text-lg font-bold text-zinc-900 dark:text-white">
				{t('equipment.unauth_title')}
			</h2>
			<p class="mt-1 max-w-md text-sm text-zinc-500 dark:text-zinc-400">
				{t('equipment.unauth_desc')}
			</p>
			<div class="mt-6 flex items-center gap-3">
				<a
					href="/login"
					class="flex items-center gap-2 rounded-xl bg-amber-500 px-5 py-2.5 text-sm font-bold text-zinc-950 shadow-md transition-all hover:bg-amber-400"
				>
					<LogIn class="h-4 w-4" />
					<span>{t('auth.signin_btn')}</span>
				</a>
				<a
					href="/register"
					class="rounded-xl border border-zinc-300 bg-white px-5 py-2.5 text-sm font-semibold text-zinc-800 transition-colors hover:bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-200 dark:hover:bg-zinc-800"
				>
					{t('auth.signup_btn')}
				</a>
			</div>
		</div>
	{:else}
		<!-- Filters & Search Toolbar -->
		<div
			class="glass-panel flex flex-col items-stretch justify-between gap-4 rounded-xl border border-zinc-200/80 p-3 sm:flex-row sm:items-center dark:border-white/[0.08]"
		>
			<!-- Category Tabs -->
			<div class="flex items-center gap-1.5 overflow-x-auto p-0.5 text-xs">
				{#each ['All', 'Boiler', 'Fermenter', 'Keg', 'Sensor', 'Other'] as const as cat}
					<button
						type="button"
						data-testid={`filter-tab-${cat.toLowerCase()}`}
						onclick={() => handleCategoryChange(cat)}
						class="rounded-lg px-3 py-1.5 font-medium whitespace-nowrap transition-colors {selectedCategory ===
						cat
							? 'bg-amber-500 font-bold text-zinc-950 shadow-sm'
							: 'text-zinc-600 hover:bg-zinc-200/60 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800 dark:hover:text-white'}"
					>
						{getCategoryName(cat)}
					</button>
				{/each}
			</div>

			<!-- Search Bar -->
			<div class="relative w-full sm:w-64">
				<Search class="absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-zinc-400" />
				<input
					type="text"
					placeholder={t('equipment.search_placeholder')}
					bind:value={searchFilter}
					oninput={handleSearchInput}
					class="h-9 w-full rounded-xl border border-zinc-200 bg-white pr-3 pl-9 text-sm text-zinc-900 placeholder-zinc-400 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100 dark:placeholder-zinc-500"
				/>
			</div>
		</div>

		<!-- Equipment Cards Grid -->
		{#if loading}
			<div class="flex flex-col items-center justify-center gap-3 py-16 text-zinc-400">
				<Loader2 class="h-8 w-8 animate-spin text-amber-500" />
				<span class="text-sm">{t('equipment.loading')}</span>
			</div>
		{:else if error}
			<div
				class="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
			>
				{error}
			</div>
		{:else if equipmentList.length === 0}
			<!-- Empty State -->
			<div
				class="glass-panel flex flex-col items-center justify-center rounded-2xl border border-zinc-200/80 py-16 text-center dark:border-white/[0.08]"
			>
				<div
					class="flex h-12 w-12 items-center justify-center rounded-2xl border border-zinc-200 bg-zinc-100 text-zinc-400 dark:border-zinc-800 dark:bg-zinc-900"
				>
					<Wrench class="h-6 w-6" />
				</div>
				<h3 class="mt-4 text-base font-bold text-zinc-900 dark:text-white">
					{t('equipment.empty_title')}
				</h3>
				<p class="mt-1 max-w-sm text-xs text-zinc-500 dark:text-zinc-400">
					{t('equipment.empty_desc')}
				</p>
				<button
					type="button"
					onclick={openAddModal}
					class="mt-5 flex items-center gap-2 rounded-xl bg-amber-500 px-4 py-2 text-xs font-bold text-zinc-950 shadow-md transition-all hover:bg-amber-400"
				>
					<Plus class="h-4 w-4 stroke-[2.5]" />
					<span>{t('equipment.add_first')}</span>
				</button>
			</div>
		{:else}
			<div class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
				{#each equipmentList as item (item.id)}
					{@const metrics = formatDisplayMetrics(item)}
					{@const status = getFillStatusBadge(metrics.fillPct)}
					{@const tempInfo = formatTemperatureBadge(item)}
					{@const extraAttrs = getParsedAttributes(item)}
					<div
						class="glass-panel group relative flex flex-col justify-between rounded-2xl border border-zinc-200/80 p-5 transition-all duration-200 hover:border-amber-500/40 hover:shadow-lg dark:border-white/[0.08]"
						data-testid="equipment-card"
					>
						<!-- Top: Hero Vessel Icon, Title & Badges -->
						<div>
							<div class="flex items-start justify-between gap-3">
								<div class="flex items-center gap-3.5">
									<!-- Thematic Outlined Vessel Icon with Live Fill -->
									<div
										class="flex h-16 w-16 flex-shrink-0 items-center justify-center rounded-2xl border border-amber-500/25 bg-amber-500/5 p-1 transition-transform duration-200 group-hover:scale-105 dark:border-amber-500/20 dark:bg-amber-500/10"
									>
										<VesselIcon
											subtype={item.subtype}
											fillPercent={item.fillPercentage}
											size={48}
											class="text-zinc-800 dark:text-zinc-100"
										/>
									</div>
									<div>
										<h3 class="font-bold text-zinc-900 dark:text-white">
											{item.name}
										</h3>
										<div class="mt-0.5 flex items-center gap-1.5">
											<span
												class="inline-block rounded-md border px-2 py-0.5 text-[10px] font-bold tracking-wider uppercase {getCategoryColor(
													item.type
												)}"
											>
												{getTypeName(item.type)}
											</span>
											<span class="text-xs font-semibold text-zinc-500 dark:text-zinc-400">
												{getSubtypeName(item.subtype)}
											</span>
										</div>
									</div>
								</div>

								<!-- Action Menu -->
								<div class="flex items-center gap-1 opacity-80 group-hover:opacity-100">
									<button
										type="button"
										onclick={() => openEditModal(item)}
										class="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
										title={t('equipment.edit_equipment')}
									>
										<Pencil class="h-4 w-4" />
									</button>
									<button
										type="button"
										onclick={() => requestDelete(item)}
										class="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-950/50 dark:hover:text-red-400"
										title={t('equipment.delete_equipment')}
										data-testid="delete-equipment-btn"
									>
										<Trash2 class="h-4 w-4" />
									</button>
								</div>
							</div>

							{#if item.description}
								<p class="mt-3 line-clamp-2 text-xs text-zinc-600 dark:text-zinc-400">
									{item.description}
								</p>
							{/if}

							<!-- Live Temperature & Telemetry Status -->
							{#if tempInfo.hasTemp}
								<div
									class="mt-3 flex flex-col gap-2 rounded-xl border border-amber-500/20 bg-amber-500/5 p-3 dark:border-amber-500/20 dark:bg-amber-500/10"
									data-testid="equipment-temp-badge"
								>
									<div class="flex items-center justify-between">
										<div class="flex items-center gap-2">
											<span class="flex h-2 w-2 rounded-full {tempInfo.dotColor}"></span>
											<span
												class="flex items-center gap-1 font-mono text-sm font-bold text-zinc-900 dark:text-zinc-100"
											>
												<Thermometer class="h-3.5 w-3.5 text-amber-500" />
												{tempInfo.displayTemp}
											</span>
											{#if item.connectionType === 'Mqtt'}
												<span
													data-testid="equipment-mqtt-tag"
													class="inline-flex items-center gap-1 rounded-full border px-1.5 py-0.5 text-[10px] font-bold {isMqttConnected(
														item
													)
														? 'border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400'
														: 'border-red-500/30 bg-red-500/10 text-red-600 dark:text-red-400'}"
													title={isMqttConnected(item)
														? t('equipment.mqtt_connected')
														: t('equipment.mqtt_disconnected')}
												>
													<span
														class="h-1.5 w-1.5 rounded-full {isMqttConnected(item)
															? 'bg-emerald-500'
															: 'bg-red-500'}"
													></span>
													MQTT
												</span>
											{/if}
										</div>
										<span
											class="text-[11px] font-medium text-zinc-500 dark:text-zinc-400"
											title={tempInfo.statusText}
										>
											{tempInfo.statusText}
										</span>
									</div>

									<!-- Additional Live Primary Metrics (Gravity, Pressure, Battery) -->
									{#if item.currentSpecificGravity != null || item.currentPressureBar != null || item.currentBatteryPercent != null || item.currentBatteryVoltage != null}
										<div
											class="flex flex-wrap items-center gap-2 border-t border-amber-500/15 pt-1"
										>
											{#if item.currentSpecificGravity != null}
												<div
													class="inline-flex items-center gap-1 rounded-lg border border-sky-500/25 bg-sky-500/10 px-2 py-0.5 font-mono text-xs font-bold text-sky-700 dark:text-sky-300"
													title={t('equipment.telemetry_gravity')}
													data-testid="equipment-gravity-badge"
												>
													<Droplets class="h-3 w-3 text-sky-500" />
													<span>{item.currentSpecificGravity.toFixed(3)} SG</span>
												</div>
											{/if}

											{#if item.currentPressureBar != null}
												<div
													class="inline-flex items-center gap-1 rounded-lg border border-indigo-500/25 bg-indigo-500/10 px-2 py-0.5 font-mono text-xs font-bold text-indigo-700 dark:text-indigo-300"
													title={t('equipment.telemetry_pressure')}
													data-testid="equipment-pressure-badge"
												>
													<Gauge class="h-3 w-3 text-indigo-500" />
													<span>{formatNumber(item.currentPressureBar, 2)} bar</span>
												</div>
											{/if}

											{#if item.currentBatteryPercent != null || item.currentBatteryVoltage != null}
												<div
													class="inline-flex items-center gap-1 rounded-lg border px-2 py-0.5 font-mono text-xs font-bold {item.currentBatteryPercent !=
														null && item.currentBatteryPercent <= 20
														? 'border-red-500/30 bg-red-500/10 text-red-600 dark:text-red-400'
														: 'border-emerald-500/25 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300'}"
													title={t('equipment.telemetry_battery')}
													data-testid="equipment-battery-badge"
												>
													<Battery class="h-3 w-3" />
													<span
														>{item.currentBatteryPercent != null
															? `${Math.round(item.currentBatteryPercent)}%`
															: ''}{item.currentBatteryVoltage != null
															? ` (${item.currentBatteryVoltage.toFixed(2)}V)`
															: ''}</span
													>
												</div>
											{/if}
										</div>
									{/if}
								</div>
							{:else if item.connectionType && item.connectionType !== 'None'}
								<div
									class="mt-3 flex items-center justify-between rounded-xl border border-dashed border-zinc-200 bg-zinc-50/50 px-3 py-2 dark:border-zinc-800 dark:bg-zinc-900/30"
								>
									<div class="flex items-center gap-2">
										<Wifi class="h-3.5 w-3.5 text-zinc-400" />
										<span class="text-[11px] text-zinc-500 dark:text-zinc-400">
											{t('equipment.awaiting_telemetry')}
										</span>
									</div>
									{#if item.connectionType === 'Mqtt'}
										<span
											data-testid="equipment-mqtt-tag"
											class="inline-flex items-center gap-1 rounded-full border px-1.5 py-0.5 text-[10px] font-bold {isMqttConnected(
												item
											)
												? 'border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400'
												: 'border-red-500/30 bg-red-500/10 text-red-600 dark:text-red-400'}"
											title={isMqttConnected(item)
												? t('equipment.mqtt_connected')
												: t('equipment.mqtt_disconnected')}
										>
											<span
												class="h-1.5 w-1.5 rounded-full {isMqttConnected(item)
													? 'bg-emerald-500'
													: 'bg-red-500'}"
											></span>
											MQTT
										</span>
									{:else}
										<span
											class="rounded bg-zinc-200/60 px-1.5 py-0.5 text-[9px] font-bold tracking-wider text-zinc-600 uppercase dark:bg-zinc-800 dark:text-zinc-400"
										>
											{item.connectionType}
										</span>
									{/if}
								</div>
							{/if}

							<!-- Custom Telemetry Attributes & Full JSON Inspector -->
							{#if extraAttrs.length > 0 || item.latestMetricsJson}
								<div
									class="mt-2.5 rounded-xl border border-zinc-200/60 bg-zinc-50/50 p-2.5 dark:border-zinc-800/60 dark:bg-zinc-900/40"
								>
									{#if extraAttrs.length > 0}
										<div class="flex flex-wrap gap-1.5">
											{#each extraAttrs.slice(0, 4) as attr}
												<span
													class="inline-flex items-center gap-1 rounded-md border border-zinc-200/80 bg-white/80 px-2 py-0.5 text-[10px] font-medium text-zinc-700 dark:border-zinc-800 dark:bg-zinc-800/70 dark:text-zinc-300"
												>
													<span class="font-semibold text-zinc-500 dark:text-zinc-400"
														>{attr.key}:</span
													>
													<span class="font-mono font-bold text-zinc-900 dark:text-zinc-100"
														>{attr.value}</span
													>
												</span>
											{/each}
										</div>
									{/if}

									<button
										type="button"
										onclick={() => toggleAttributes(item.id)}
										class="mt-1.5 flex items-center gap-1 text-[10px] font-semibold text-amber-600 transition-colors hover:text-amber-500 dark:text-amber-400 dark:hover:text-amber-300"
										data-testid="toggle-telemetry-attributes-btn"
									>
										{#if expandedAttributes[item.id]}
											<ChevronUp class="h-3 w-3" />
											<span>{t('equipment.telemetry_hide_attributes')}</span>
										{:else}
											<ChevronDown class="h-3 w-3" />
											<span
												>{t('equipment.telemetry_view_attributes', {
													count: extraAttrs.length
												})}</span
											>
										{/if}
									</button>

									{#if expandedAttributes[item.id]}
										<div
											class="mt-2 rounded-lg border border-zinc-300/80 bg-zinc-950 p-2.5 font-mono text-[10px] text-emerald-400 shadow-inner dark:border-zinc-800"
											data-testid="telemetry-attributes-panel"
										>
											<div
												class="mb-1.5 flex items-center justify-between border-b border-zinc-800 pb-1 text-[9px] font-bold tracking-wider text-zinc-400 uppercase"
											>
												<span>{t('equipment.telemetry_attributes_title')}</span>
												<span class="text-zinc-500">
													{item.lastTelemetryAt
														? new Date(item.lastTelemetryAt).toLocaleTimeString()
														: ''}
												</span>
											</div>
											{#if extraAttrs.length > 0}
												<div class="space-y-1">
													{#each extraAttrs as attr}
														<div class="flex items-start justify-between gap-2">
															<span class="text-amber-400 select-all">{attr.key}:</span>
															<span class="text-right break-all text-zinc-200 select-all"
																>{attr.value}</span
															>
														</div>
													{/each}
												</div>
											{:else}
												<pre
													class="overflow-x-auto text-zinc-300 select-all">{item.latestMetricsJson}</pre>
											{/if}
										</div>
									{/if}
								</div>
							{/if}
						</div>

						<!-- Fill Level & Volume Box (for fillable vessels only) -->
						{#if isVesselFillable(item.subtype)}
							<div
								class="mt-4 rounded-xl border border-zinc-200/80 bg-zinc-50/70 p-3.5 dark:border-zinc-800/80 dark:bg-zinc-900/60"
							>
								<div class="flex items-center justify-between">
									<span class="text-[10px] font-bold tracking-wider text-zinc-400 uppercase">
										{t('equipment.fill_level_capacity')}
									</span>
									<span
										class="rounded-full border px-2 py-0.5 font-mono text-[10px] font-bold {status.classes}"
									>
										{status.text}
									</span>
								</div>

								<div class="mt-1 flex items-baseline justify-between">
									<div class="flex items-baseline gap-1.5">
										<span class="font-mono text-lg font-bold text-amber-600 dark:text-amber-400">
											{metrics.currentFormatted}
										</span>
										<span class="text-xs text-zinc-400">/</span>
										<span class="font-mono text-xs font-semibold text-zinc-500 dark:text-zinc-400">
											{metrics.capacityFormatted}
										</span>
									</div>
								</div>

								<!-- Liquid Fill Progress Bar -->
								<div
									class="mt-2.5 h-1.5 w-full overflow-hidden rounded-full bg-zinc-200 dark:bg-zinc-800"
								>
									<div
										class="h-full rounded-full bg-gradient-to-r from-amber-400 via-amber-500 to-amber-600 transition-all duration-500 {metrics.fillPct >
										0
											? 'shadow-[0_0_8px_rgba(245,158,11,0.35)]'
											: ''}"
										style="width: {metrics.fillPct}%"
										role="progressbar"
										aria-valuenow={metrics.fillPct}
										aria-valuemin="0"
										aria-valuemax="100"
									></div>
								</div>
							</div>
						{/if}

						{#if item.notes}
							<p class="mt-3 text-[11px] text-zinc-400 italic">
								"{item.notes}"
							</p>
						{/if}
					</div>
				{/each}
			</div>
		{/if}
	{/if}
</div>

<!-- Add / Edit Modal -->
<EquipmentModal
	open={modalOpen}
	equipment={editingItem}
	existingEquipment={equipmentList}
	defaultCategory={selectedCategory !== 'All' ? (selectedCategory as EquipmentType) : 'Boiler'}
	onClose={() => (modalOpen = false)}
	onSaved={handleSaved}
/>

<!-- Delete Confirmation Modal -->
<DeleteEquipmentModal
	open={deleteModalOpen}
	equipment={itemToDelete}
	onClose={() => {
		deleteModalOpen = false;
		itemToDelete = null;
	}}
	onDeleted={handleDeleted}
/>
