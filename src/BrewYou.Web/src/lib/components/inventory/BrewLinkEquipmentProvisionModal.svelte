<script lang="ts">
	import { api, ApiClientError } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import { settings } from '$lib/stores/settings.svelte';
	import type { BrewLinkEquipmentDeviceDto, EquipmentDto, EquipmentType } from '$lib/types/api';
	import {
		X,
		Loader2,
		RefreshCw,
		Cpu,
		Check,
		AlertCircle,
		Radio,
		Thermometer,
		Plus
	} from '@lucide/svelte';

	interface Props {
		open: boolean;
		gatewayId: string | null;
		gatewayName?: string | null;
		gatewayHost?: string | null;
		hardwareGatewayId?: string | null;
		brewerySetupId: string;
		onClose: () => void;
		onProvisioned: (createdEquipment: EquipmentDto[]) => void;
	}

	let {
		open,
		gatewayId,
		gatewayName = null,
		gatewayHost = null,
		hardwareGatewayId = null,
		brewerySetupId,
		onClose,
		onProvisioned
	}: Props = $props();

	let devices = $state<BrewLinkEquipmentDeviceDto[]>([]);
	let selectedMap = $state<Record<string, boolean>>({});
	let isLoading = $state(false);
	let isProvisioning = $state(false);
	let errorMessage = $state<string | null>(null);
	let resolvedGatewayName = $state<string | null>(null);
	let resolvedGatewayHost = $state<string | null>(null);
	let resolvedHardwareId = $state<string | null>(null);

	function deviceKey(dev: BrewLinkEquipmentDeviceDto): string {
		return `${dev.slot}-${dev.mac || ''}`;
	}

	let selectedDevices = $derived.by(() =>
		devices.filter((dev) => !dev.existingEquipmentId && selectedMap[deviceKey(dev)])
	);

	function formatEquipmentType(type?: EquipmentType): string {
		switch (type) {
			case 'Boiler':
				return t('equipment.boilers');
			case 'Fermenter':
				return t('equipment.fermenters');
			case 'Keg':
				return t('equipment.kegs');
			case 'Tap':
				return t('equipment.taps');
			case 'Sensor':
				return t('equipment.sensors');
			default:
				return t('equipment.other');
		}
	}

	function formatTemp(tempC: number | null | undefined): string {
		if (tempC == null) return '';
		return settings.formatTemperature(tempC);
	}

	$effect(() => {
		if (open && gatewayId) {
			resolvedGatewayName = gatewayName;
			resolvedGatewayHost = gatewayHost;
			resolvedHardwareId = hardwareGatewayId;
			void loadDevices();
		} else if (!open) {
			devices = [];
			selectedMap = {};
			errorMessage = null;
			isLoading = false;
			isProvisioning = false;
		}
	});

	async function loadDevices() {
		if (!gatewayId) return;
		isLoading = true;
		errorMessage = null;
		try {
			const [devicesRes, gatewaysRes] = await Promise.allSettled([
				api.brewlink.getGatewayEquipment(gatewayId, brewerySetupId),
				!resolvedGatewayHost || !resolvedHardwareId
					? api.brewlink.listGateways()
					: Promise.resolve([])
			]);

			if (gatewaysRes.status === 'fulfilled' && Array.isArray(gatewaysRes.value)) {
				const gw = gatewaysRes.value.find((g) => g.id === gatewayId);
				if (gw) {
					resolvedGatewayName = resolvedGatewayName || gw.name;
					resolvedGatewayHost = resolvedGatewayHost || gw.host;
					resolvedHardwareId = resolvedHardwareId || gw.gatewayId;
				}
			}

			if (devicesRes.status === 'rejected') {
				const err = devicesRes.reason;
				errorMessage =
					err instanceof ApiClientError || err instanceof Error
						? err.message
						: t('settings.brewlink.provision_modal.load_error');
				devices = [];
				selectedMap = {};
				return;
			}

			const list = devicesRes.value ?? [];
			devices = list;
			const nextSelection: Record<string, boolean> = {};
			for (const dev of list) {
				if (!dev.existingEquipmentId) {
					nextSelection[deviceKey(dev)] = true;
				}
			}
			selectedMap = nextSelection;
		} finally {
			isLoading = false;
		}
	}

	function toggleDevice(dev: BrewLinkEquipmentDeviceDto) {
		if (dev.existingEquipmentId || isProvisioning) return;
		const key = deviceKey(dev);
		selectedMap = {
			...selectedMap,
			[key]: !selectedMap[key]
		};
	}

	async function handleProvision() {
		if (!gatewayId || selectedDevices.length === 0 || isProvisioning) return;
		isProvisioning = true;
		errorMessage = null;
		try {
			const result = await api.brewlink.provisionGatewayEquipment(gatewayId, {
				brewerySetupId,
				devices: selectedDevices.map((dev) => ({
					slot: dev.slot,
					mac: dev.mac || null,
					name: dev.name,
					model: dev.model ?? null,
					equipmentType: dev.equipmentType
				}))
			});
			onProvisioned(result.createdEquipment ?? []);
			onClose();
		} catch (err: unknown) {
			errorMessage =
				err instanceof ApiClientError || err instanceof Error
					? err.message
					: t('settings.brewlink.provision_modal.provision_error');
		} finally {
			isProvisioning = false;
		}
	}
</script>

{#if open}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm"
		role="dialog"
		aria-modal="true"
		data-testid="brewlink-provision-modal"
	>
		<div
			class="w-full max-w-xl rounded-2xl border border-zinc-200 bg-white shadow-2xl dark:border-zinc-800 dark:bg-zinc-900"
		>
			<!-- Header -->
			<div
				class="flex items-start justify-between gap-3 border-b border-zinc-200/80 px-5 py-4 dark:border-white/10"
			>
				<div class="flex items-start gap-3">
					<div
						class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-sky-500/30 bg-sky-500/10 text-sky-600 dark:text-sky-400"
					>
						<Radio class="h-5 w-5" />
					</div>
					<div>
						<h2 class="text-base font-bold text-zinc-900 dark:text-white">
							{t('settings.brewlink.provision_modal.title')}
						</h2>
						<p class="mt-0.5 text-xs text-zinc-500 dark:text-zinc-400">
							{t('settings.brewlink.provision_modal.subtitle', {
								name: resolvedGatewayName || 'BrewLink Gateway'
							})}
						</p>
						{#if resolvedGatewayHost || resolvedHardwareId}
							<div
								class="mt-1 flex flex-wrap items-center gap-2 font-mono text-[10px] text-zinc-500 dark:text-zinc-400"
							>
								{#if resolvedGatewayHost}
									<span>{resolvedGatewayHost}</span>
								{/if}
								{#if resolvedGatewayHost && resolvedHardwareId}
									<span>•</span>
								{/if}
								{#if resolvedHardwareId}
									<code>{resolvedHardwareId}</code>
								{/if}
							</div>
						{/if}
					</div>
				</div>

				<div class="flex items-center gap-2">
					<button
						type="button"
						onclick={loadDevices}
						disabled={isLoading || isProvisioning}
						data-testid="brewlink-recheck-btn"
						class="inline-flex items-center gap-1.5 rounded-xl border border-zinc-200 bg-white px-2.5 py-1.5 text-xs font-semibold text-zinc-700 transition hover:bg-zinc-50 disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-200 dark:hover:bg-zinc-700"
					>
						<RefreshCw class="h-3.5 w-3.5 {isLoading ? 'animate-spin' : ''}" />
						<span
							>{isLoading
								? t('settings.brewlink.provision_modal.rechecking')
								: t('settings.brewlink.provision_modal.recheck')}</span
						>
					</button>
					<button
						type="button"
						onclick={onClose}
						aria-label={t('common.close')}
						class="rounded-lg p-1.5 text-zinc-400 transition hover:bg-zinc-100 hover:text-zinc-600 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
					>
						<X class="h-4 w-4" />
					</button>
				</div>
			</div>

			<!-- Body -->
			<div class="max-h-[60vh] space-y-3 overflow-y-auto px-5 py-4">
				{#if isLoading}
					<div class="flex flex-col items-center justify-center gap-2.5 py-10 text-center">
						<Loader2 class="h-6 w-6 animate-spin text-sky-500" />
						<p class="text-xs font-medium text-zinc-500 dark:text-zinc-400">
							{t('settings.brewlink.provision_modal.loading')}
						</p>
					</div>
				{:else if errorMessage}
					<div
						class="flex items-start gap-2.5 rounded-xl border border-red-500/30 bg-red-500/10 p-3.5 text-xs text-red-700 dark:text-red-300"
						role="alert"
					>
						<AlertCircle class="mt-0.5 h-4 w-4 shrink-0" />
						<span>{errorMessage}</span>
					</div>
				{:else if devices.length === 0}
					<div
						class="flex flex-col items-center justify-center rounded-xl border border-dashed border-zinc-300 p-6 text-center dark:border-zinc-700"
						data-testid="brewlink-provision-empty"
					>
						<Cpu class="h-7 w-7 text-zinc-400" />
						<h3 class="mt-2.5 text-sm font-bold text-zinc-900 dark:text-white">
							{t('settings.brewlink.provision_modal.empty_title')}
						</h3>
						<p class="mt-1 max-w-sm text-xs text-zinc-500 dark:text-zinc-400">
							{t('settings.brewlink.provision_modal.empty_desc')}
						</p>
					</div>
				{:else}
					<div class="space-y-2.5">
						{#each devices as dev (`${dev.slot}-${dev.mac}`)}
							{@const alreadyAdded = !!dev.existingEquipmentId}
							{@const isChecked = alreadyAdded || !!selectedMap[deviceKey(dev)]}
							<label
								data-testid="brewlink-provision-device-{dev.slot}"
								class="flex cursor-pointer items-start gap-3 rounded-xl border p-3.5 transition {alreadyAdded
									? 'cursor-default border-zinc-200/70 bg-zinc-50/70 opacity-85 dark:border-zinc-800 dark:bg-zinc-900/40'
									: isChecked
										? 'border-sky-500/50 bg-sky-500/5 dark:border-sky-500/40 dark:bg-sky-500/10'
										: 'border-zinc-200 bg-white hover:border-zinc-300 dark:border-zinc-800 dark:bg-zinc-900 dark:hover:border-zinc-700'}"
							>
								<input
									type="checkbox"
									checked={isChecked}
									disabled={alreadyAdded || isProvisioning}
									onchange={() => toggleDevice(dev)}
									class="mt-1 h-4 w-4 rounded border-zinc-300 text-sky-600 focus:ring-sky-500 disabled:opacity-60"
								/>

								<div class="min-w-0 flex-1 space-y-1">
									<div class="flex flex-wrap items-center justify-between gap-2">
										<div class="flex flex-wrap items-center gap-2">
											<span class="text-sm font-bold text-zinc-900 dark:text-white">
												{dev.name}
											</span>
											<span
												class="rounded-md bg-zinc-200/70 px-1.5 py-0.5 text-[10px] font-semibold text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
											>
												{t('settings.brewlink.provision_modal.slot_label', { slot: dev.slot })}
											</span>
											{#if dev.connected}
												<span
													class="inline-flex items-center gap-1 rounded-full bg-emerald-500/15 px-2 py-0.5 text-[10px] font-bold text-emerald-700 dark:text-emerald-400"
												>
													<span class="h-1.5 w-1.5 rounded-full bg-emerald-500"></span>
													{t('settings.brewlink.provision_modal.status_connected')}
												</span>
											{:else}
												<span
													class="inline-flex items-center gap-1 rounded-full bg-zinc-500/15 px-2 py-0.5 text-[10px] font-semibold text-zinc-600 dark:text-zinc-400"
												>
													{t('settings.brewlink.provision_modal.status_standby')}
												</span>
											{/if}
										</div>

										{#if dev.connected && dev.state?.tempC != null}
											<span
												class="inline-flex items-center gap-1 rounded-lg bg-amber-500/10 px-2 py-0.5 font-mono text-xs font-bold text-amber-700 dark:text-amber-300"
											>
												<Thermometer class="h-3 w-3" />
												{formatTemp(dev.state.tempC)}
											</span>
										{/if}
									</div>

									<div
										class="flex flex-wrap items-center gap-2 text-[11px] text-zinc-500 dark:text-zinc-400"
									>
										{#if dev.model && dev.model !== dev.name}
											<span class="font-medium text-zinc-700 dark:text-zinc-300">{dev.model}</span>
											<span>•</span>
										{/if}
										{#if dev.mac}
											<code class="font-mono text-[10px]">{dev.mac}</code>
										{/if}
									</div>

									{#if alreadyAdded}
										<div
											class="inline-flex items-center gap-1.5 rounded-lg border border-emerald-500/25 bg-emerald-500/10 px-2 py-1 text-[11px] font-semibold text-emerald-700 dark:text-emerald-300"
										>
											<Check class="h-3.5 w-3.5" />
											<span>
												{t('settings.brewlink.provision_modal.already_added', {
													name: dev.existingEquipmentName || dev.name
												})}
											</span>
										</div>
									{:else}
										<p class="text-[11px] font-medium text-sky-700 dark:text-sky-300">
											{t('settings.brewlink.provision_modal.will_create_summary', {
												type: formatEquipmentType(dev.mappedEquipmentType),
												subtype: t(`equipment.subtypes.${dev.mappedSubtype || 'AllInOne'}`),
												capacity: dev.recommendedCapacityLiters ?? 30
											})}
										</p>
									{/if}
								</div>
							</label>
						{/each}
					</div>
				{/if}
			</div>

			<!-- Footer -->
			<div
				class="flex flex-wrap items-center justify-end gap-2.5 border-t border-zinc-200/80 px-5 py-3.5 dark:border-white/10"
			>
				<button
					type="button"
					onclick={onClose}
					disabled={isProvisioning}
					class="rounded-xl border border-zinc-300 px-4 py-2 text-xs font-semibold text-zinc-700 transition hover:bg-zinc-50 disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-700"
				>
					{t('settings.brewlink.provision_modal.skip_close')}
				</button>

				{#if selectedDevices.length > 0}
					<button
						type="button"
						onclick={handleProvision}
						disabled={isProvisioning || isLoading}
						data-testid="brewlink-provision-confirm-btn"
						class="inline-flex items-center gap-1.5 rounded-xl bg-amber-500 px-4 py-2 text-xs font-bold text-zinc-950 shadow-sm transition hover:bg-amber-400 disabled:opacity-50"
					>
						{#if isProvisioning}
							<Loader2 class="h-3.5 w-3.5 animate-spin" />
							<span>{t('settings.brewlink.provision_modal.adding')}</span>
						{:else}
							<Plus class="h-3.5 w-3.5" />
							<span>
								{t('settings.brewlink.provision_modal.add_selected', {
									count: selectedDevices.length
								})}
							</span>
						{/if}
					</button>
				{/if}
			</div>
		</div>
	</div>
{/if}
