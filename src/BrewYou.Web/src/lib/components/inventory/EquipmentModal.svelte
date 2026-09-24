<script lang="ts">
	import { api, ApiClientError } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import { formatNumber } from '$lib/utils/formatNumber';
	import { generateEquipmentTopic } from '$lib/utils/mqtt';
	import {
		settings,
		litersToGallons,
		gallonsToLiters,
		celsiusToFahrenheit,
		fahrenheitToCelsius
	} from '$lib/stores/settings.svelte';
	import {
		type EquipmentDto,
		type EquipmentType,
		type EquipmentSubtype,
		type VolumeUnit,
		type EquipmentConnectionType,
		EQUIPMENT_SUBTYPES_BY_TYPE,
		DEFAULT_SUBTYPE_BY_TYPE
	} from '$lib/types/api';
	import { brewery, DEFAULT_BREWERY_ID } from '$lib/stores/brewery.svelte';
	import ConfirmModal from '$lib/components/ui/ConfirmModal.svelte';
	import VesselIcon from './VesselIcon.svelte';
	import { isVesselFillable } from './vessel-geometry';
	import {
		X,
		Loader2,
		Check,
		Building2,
		Wifi,
		Thermometer,
		Copy,
		CheckCheck,
		RefreshCw,
		Globe,
		Radio,
		Eye,
		EyeOff
	} from '@lucide/svelte';

	interface Props {
		open: boolean;
		equipment?: EquipmentDto | null;
		existingEquipment?: EquipmentDto[];
		defaultCategory?: EquipmentType;
		onClose: () => void;
		onSaved: (item: EquipmentDto) => void;
	}

	let {
		open,
		equipment = null,
		existingEquipment = [],
		defaultCategory = 'Boiler',
		onClose,
		onSaved
	}: Props = $props();

	let name = $state('');
	let type = $state<EquipmentType>('Boiler');
	let subtype = $state<EquipmentSubtype>('AllInOne');
	let capacity = $state<number | ''>('');
	let currentVolume = $state<number | ''>('');
	let unit = $state<VolumeUnit>('Liters');
	let description = $state('');
	let notes = $state('');

	let boilOffRatePerHour = $state<number | ''>('');
	let trubLossLiters = $state<number | ''>('');
	let mashTunDeadSpaceLiters = $state<number | ''>('');
	let packagingLossLiters = $state<number | ''>('');

	// Telemetry & External Connectivity
	let connectionType = $state<EquipmentConnectionType>('None');
	let connectionToken = $state<string | null>(null);
	let currentTemperature = $state<number | ''>('');
	let pollUrl = $state('');
	let pollIntervalSeconds = $state(60);
	let pollJsonPath = $state('temperature');
	let mqttSource = $state<'userSettings' | 'custom'>('userSettings');
	let mqttBrokerHost = $state('');
	let mqttBrokerPort = $state<number | ''>(1883);
	let mqttTopic = $state('');
	let isTopicCustomized = $state(false);
	let mqttUsername = $state('');
	let mqttPassword = $state('');
	let mqttCertificate = $state('');
	let showMqttCustomPassword = $state(false);

	let isRegeneratingToken = $state(false);
	let showRegenerateTokenModal = $state(false);
	let isTestingPoll = $state(false);
	let testPollMessage = $state<{ type: 'success' | 'error'; text: string } | null>(null);
	let copiedWebhook = $state(false);

	let isSubmitting = $state(false);
	let errorMessage = $state<string | null>(null);

	let isDuplicateName = $derived.by(() => {
		const trimmed = name.trim().toLowerCase();
		if (!trimmed) return false;
		return existingEquipment.some((item) => {
			if (equipment && item.id === equipment.id) return false;
			if (
				item.brewerySetupId &&
				brewery.activeSetupId &&
				item.brewerySetupId !== brewery.activeSetupId
			) {
				return false;
			}
			return item.name.trim().toLowerCase() === trimmed;
		});
	});

	function getCategoryName(cat: EquipmentType): string {
		switch (cat) {
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

	function getSubtypeName(sub: EquipmentSubtype): string {
		return t(`equipment.subtypes.${sub}`) || sub;
	}

	function getSubtypeDescription(sub: EquipmentSubtype): string {
		return t(`equipment.subtype_descriptions.${sub}`) || sub;
	}

	const DEFAULT_CAPACITIES_LITERS: Record<EquipmentSubtype, number> = {
		AllInOne: 35,
		Pan: 50,
		Hlt: 30,
		HermsRims: 15,
		Bucket: 30,
		ConicalFermenter: 27,
		Carboy: 20,
		PressureFermenter: 30,
		StainlessBucket: 26,
		Cornelius: 19,
		Minikeg: 5,
		MiniBarrel: 10,
		PetKeg: 8,
		ISpindel: 0,
		Tilt: 0,
		GenericSensor: 0,
		Other: 20
	};

	const DEFAULT_LOSSES_BY_TYPE: Record<
		EquipmentType,
		{
			boilOffRatePerHour: number | '';
			trubLossLiters: number | '';
			mashTunDeadSpaceLiters: number | '';
			packagingLossLiters: number | '';
		}
	> = {
		Boiler: {
			boilOffRatePerHour: 3.0,
			trubLossLiters: 1.5,
			mashTunDeadSpaceLiters: 0.0,
			packagingLossLiters: ''
		},
		Fermenter: {
			boilOffRatePerHour: '',
			trubLossLiters: 1.5,
			mashTunDeadSpaceLiters: '',
			packagingLossLiters: ''
		},
		Keg: {
			boilOffRatePerHour: '',
			trubLossLiters: '',
			mashTunDeadSpaceLiters: '',
			packagingLossLiters: 0.5
		},
		Sensor: {
			boilOffRatePerHour: '',
			trubLossLiters: '',
			mashTunDeadSpaceLiters: '',
			packagingLossLiters: ''
		},
		Other: {
			boilOffRatePerHour: '',
			trubLossLiters: '',
			mashTunDeadSpaceLiters: '',
			packagingLossLiters: 0.5
		}
	};

	$effect(() => {
		if (open) {
			if (equipment) {
				name = equipment.name;
				type = equipment.type;
				subtype = equipment.subtype ?? DEFAULT_SUBTYPE_BY_TYPE[equipment.type];
				unit = settings.volumeUnit;
				if (settings.volumeUnit === 'Gallons') {
					capacity = parseFloat(litersToGallons(equipment.capacityLiters).toFixed(1));
					currentVolume = parseFloat(litersToGallons(equipment.currentVolumeLiters).toFixed(1));
				} else {
					capacity = parseFloat(equipment.capacityLiters.toFixed(1));
					currentVolume = parseFloat(equipment.currentVolumeLiters.toFixed(1));
				}
				description = equipment.description ?? '';
				notes = equipment.notes ?? '';
				boilOffRatePerHour = equipment.boilOffRatePerHour ?? '';
				trubLossLiters = equipment.trubLossLiters ?? '';
				mashTunDeadSpaceLiters = equipment.mashTunDeadSpaceLiters ?? '';
				packagingLossLiters = equipment.packagingLossLiters ?? '';

				// Initialize telemetry
				connectionType = equipment.connectionType ?? 'None';
				connectionToken = equipment.connectionToken ?? null;
				if (equipment.currentTemperatureC != null) {
					currentTemperature =
						settings.temperatureUnit === 'Fahrenheit'
							? parseFloat(celsiusToFahrenheit(equipment.currentTemperatureC).toFixed(1))
							: parseFloat(equipment.currentTemperatureC.toFixed(1));
				} else {
					currentTemperature = '';
				}

				if (equipment.connectionConfigJson) {
					try {
						const cfg = JSON.parse(equipment.connectionConfigJson);
						pollUrl = cfg.url ?? '';
						pollIntervalSeconds = cfg.intervalSeconds ?? 60;
						pollJsonPath = cfg.jsonPath ?? 'temperature';
						mqttTopic = cfg.topic ?? '';
						isTopicCustomized = !!cfg.topic;
						mqttBrokerHost = cfg.brokerHost ?? '';
						mqttBrokerPort = cfg.brokerPort ?? 1883;
						mqttUsername = cfg.username ?? '';
						mqttPassword = cfg.password ?? '';
						mqttCertificate = cfg.certificate ?? '';
						if (cfg.useUserSettings !== undefined) {
							mqttSource = cfg.useUserSettings ? 'userSettings' : 'custom';
						} else if (
							settings.hasMqttConfigured &&
							(!cfg.brokerHost || cfg.brokerHost === settings.mqttHost)
						) {
							mqttSource = 'userSettings';
						} else {
							mqttSource = cfg.brokerHost
								? 'custom'
								: settings.hasMqttConfigured
									? 'userSettings'
									: 'custom';
						}
					} catch {
						pollUrl = '';
						pollIntervalSeconds = 60;
						pollJsonPath = 'temperature';
						mqttBrokerHost = '';
						mqttBrokerPort = 1883;
						mqttTopic = generateEquipmentTopic(settings.mqttTopicPrefix, equipment.name);
						isTopicCustomized = false;
						mqttUsername = '';
						mqttPassword = '';
						mqttCertificate = '';
						mqttSource = settings.hasMqttConfigured ? 'userSettings' : 'custom';
					}
				} else {
					pollUrl = '';
					pollIntervalSeconds = 60;
					pollJsonPath = 'temperature';
					mqttBrokerHost = '';
					mqttBrokerPort = 1883;
					mqttTopic = generateEquipmentTopic(settings.mqttTopicPrefix, equipment.name);
					isTopicCustomized = false;
					mqttUsername = '';
					mqttPassword = '';
					mqttCertificate = '';
					mqttSource = settings.hasMqttConfigured ? 'userSettings' : 'custom';
				}
			} else {
				name = '';
				const initialCategory = defaultCategory ?? 'Boiler';
				type = initialCategory;
				const initialSubtype = DEFAULT_SUBTYPE_BY_TYPE[initialCategory] ?? 'AllInOne';
				subtype = initialSubtype;
				unit = settings.volumeUnit;
				const defLiters = DEFAULT_CAPACITIES_LITERS[initialSubtype] ?? 20;
				capacity =
					settings.volumeUnit === 'Gallons'
						? parseFloat(litersToGallons(defLiters).toFixed(1))
						: defLiters;
				currentVolume = 0;
				description = '';
				notes = '';
				const defLosses = DEFAULT_LOSSES_BY_TYPE[initialCategory];
				boilOffRatePerHour = defLosses.boilOffRatePerHour;
				trubLossLiters = defLosses.trubLossLiters;
				mashTunDeadSpaceLiters = defLosses.mashTunDeadSpaceLiters;
				packagingLossLiters = defLosses.packagingLossLiters;

				// Reset telemetry
				connectionType = 'None';
				connectionToken = null;
				currentTemperature = '';
				pollUrl = '';
				pollIntervalSeconds = 60;
				pollJsonPath = 'temperature';
				mqttBrokerHost = '';
				mqttBrokerPort = 1883;
				mqttTopic = '';
				isTopicCustomized = false;
				mqttUsername = '';
				mqttPassword = '';
				mqttCertificate = '';
				mqttSource = settings.hasMqttConfigured ? 'userSettings' : 'custom';
			}
			testPollMessage = null;
			errorMessage = null;
		}
	});

	$effect(() => {
		if (!open) return;
		const currentName = name;
		const prefix = settings.mqttTopicPrefix;
		if (!isTopicCustomized) {
			mqttTopic = generateEquipmentTopic(prefix, currentName);
		}
	});

	function handleCategoryChange(newType: EquipmentType) {
		type = newType;
		const defaultSub = DEFAULT_SUBTYPE_BY_TYPE[newType];
		subtype = defaultSub;

		// Suggest default capacity and losses if creating new equipment
		if (!equipment) {
			const defLiters = DEFAULT_CAPACITIES_LITERS[defaultSub] ?? 20;
			capacity = unit === 'Gallons' ? parseFloat(litersToGallons(defLiters).toFixed(1)) : defLiters;
			currentVolume = 0;
			const defLosses = DEFAULT_LOSSES_BY_TYPE[newType];
			boilOffRatePerHour = defLosses.boilOffRatePerHour;
			trubLossLiters = defLosses.trubLossLiters;
			mashTunDeadSpaceLiters = defLosses.mashTunDeadSpaceLiters;
			packagingLossLiters = defLosses.packagingLossLiters;
		}
	}

	let isFillable = $derived(isVesselFillable(subtype));

	function handleSubtypeSelect(selectedSub: EquipmentSubtype) {
		subtype = selectedSub;
		if (!isVesselFillable(selectedSub)) {
			capacity = 0;
			currentVolume = 0;
		} else if (!equipment && (!capacity || capacity === 0)) {
			const defLiters = DEFAULT_CAPACITIES_LITERS[selectedSub] ?? 20;
			capacity = unit === 'Gallons' ? parseFloat(litersToGallons(defLiters).toFixed(1)) : defLiters;
		}
	}

	let fillPercent = $derived.by(() => {
		if (!isFillable) return 0;
		const numCap = typeof capacity === 'number' ? capacity : parseFloat(String(capacity));
		const numVol =
			typeof currentVolume === 'number' ? currentVolume : parseFloat(String(currentVolume));
		if (isNaN(numCap) || numCap <= 0 || isNaN(numVol) || numVol <= 0) return 0;
		return Math.min(100, Math.max(0, Math.round((numVol / numCap) * 100)));
	});

	function setPreset(percent: number) {
		const numCap = typeof capacity === 'number' ? capacity : parseFloat(String(capacity));
		if (isNaN(numCap) || numCap <= 0) {
			currentVolume = 0;
			return;
		}
		const calculated = (percent / 100) * numCap;
		currentVolume = parseFloat(calculated.toFixed(1));
	}

	let convertedPreview = $derived.by(() => {
		const num = typeof capacity === 'number' ? capacity : parseFloat(String(capacity));
		if (isNaN(num) || num <= 0) return null;

		if (unit === 'Liters') {
			const gal = litersToGallons(num);
			return `≈ ${formatNumber(gal, 1)} US gal`;
		} else {
			const lit = gallonsToLiters(num);
			return `≈ ${formatNumber(lit, 1)} L`;
		}
	});

	let webhookUrl = $derived.by(() => {
		if (!connectionToken) return '';
		if (typeof window !== 'undefined') {
			const baseUrl = window.location.origin.includes('3000')
				? window.location.origin.replace(':3000', ':5000')
				: window.location.origin;
			return `${baseUrl}/api/v1/telemetry/equipment/${connectionToken}`;
		}
		return `/api/v1/telemetry/equipment/${connectionToken}`;
	});

	async function copyWebhookUrl() {
		if (!webhookUrl) return;
		try {
			await navigator.clipboard.writeText(webhookUrl);
			copiedWebhook = true;
			setTimeout(() => {
				copiedWebhook = false;
			}, 2500);
		} catch (err) {
			console.error('Failed to copy webhook URL:', err);
		}
	}

	async function handleConfirmRegenerateToken() {
		if (!equipment || isRegeneratingToken) return;

		isRegeneratingToken = true;
		try {
			const updated = await api.equipment.regenerateToken(equipment.id);
			connectionToken = updated.connectionToken ?? null;
			showRegenerateTokenModal = false;
		} catch (err: unknown) {
			errorMessage = (err as Error).message || t('equipment.err_regenerate_failed');
			showRegenerateTokenModal = false;
		} finally {
			isRegeneratingToken = false;
		}
	}

	async function handleTestPoll() {
		if (!equipment) {
			testPollMessage = { type: 'error', text: t('equipment.poll_save_first') };
			return;
		}
		if (!pollUrl.trim()) {
			testPollMessage = { type: 'error', text: t('equipment.poll_url_required') };
			return;
		}

		isTestingPoll = true;
		testPollMessage = null;
		try {
			await api.equipment.update(equipment.id, {
				name: name.trim(),
				type,
				capacity: typeof capacity === 'number' ? capacity : Number(capacity),
				connectionType: 'HttpPoll',
				connectionConfigJson: JSON.stringify({
					url: pollUrl.trim(),
					intervalSeconds: Number(pollIntervalSeconds) || 60,
					jsonPath: pollJsonPath.trim() || 'temperature'
				})
			});

			const result = await api.equipment.testPoll(equipment.id);
			if (result.success && result.temperatureC != null) {
				const displayTemp =
					settings.temperatureUnit === 'Fahrenheit'
						? `${celsiusToFahrenheit(result.temperatureC)} °F`
						: `${result.temperatureC} °C`;
				testPollMessage = {
					type: 'success',
					text: t('equipment.poll_success', { temp: displayTemp })
				};
				currentTemperature =
					settings.temperatureUnit === 'Fahrenheit'
						? celsiusToFahrenheit(result.temperatureC)
						: result.temperatureC;
			} else {
				testPollMessage = {
					type: 'error',
					text: result.errorMessage || t('equipment.poll_failed')
				};
			}
		} catch (err: unknown) {
			testPollMessage = {
				type: 'error',
				text: (err as Error).message || t('equipment.poll_failed')
			};
		} finally {
			isTestingPoll = false;
		}
	}

	async function handleSubmit(e: SubmitEvent) {
		e.preventDefault();
		errorMessage = null;

		let numCapacity = typeof capacity === 'number' ? capacity : parseFloat(String(capacity));
		let numCurrent =
			currentVolume === '' || currentVolume === null || isNaN(Number(currentVolume))
				? 0
				: Number(currentVolume);

		if (!name.trim()) {
			errorMessage = t('equipment.err_name_required');
			return;
		}

		if (isDuplicateName) {
			errorMessage = t('equipment.err_duplicate_name');
			return;
		}

		if (!isFillable) {
			numCapacity = 0;
			numCurrent = 0;
		} else {
			if (isNaN(numCapacity) || numCapacity <= 0) {
				errorMessage = t('equipment.err_capacity_positive');
				return;
			}

			if (numCurrent < 0) {
				errorMessage = t('equipment.err_volume_negative');
				return;
			}

			if (numCurrent > numCapacity) {
				const unitLabel = unit === 'Liters' ? 'L' : 'gal';
				errorMessage = t('equipment.err_volume_exceeds', {
					current: numCurrent,
					capacity: numCapacity,
					unit: unitLabel
				});
				return;
			}
		}

		isSubmitting = true;

		try {
			let configJson: string | null = null;
			if (connectionType === 'HttpPoll') {
				configJson = JSON.stringify({
					url: pollUrl.trim(),
					intervalSeconds: Number(pollIntervalSeconds) || 60,
					jsonPath: pollJsonPath.trim() || 'temperature'
				});
			} else if (connectionType === 'Mqtt') {
				if (mqttSource === 'userSettings') {
					configJson = JSON.stringify({
						useUserSettings: true,
						brokerHost: settings.mqttHost.trim(),
						brokerPort: Number(settings.mqttPort) || 1883,
						topic: mqttTopic.trim(),
						username: settings.mqttUsername.trim() || null,
						hasCertificate: !!settings.mqttCertificate.trim()
					});
				} else {
					configJson = JSON.stringify({
						useUserSettings: false,
						brokerHost: mqttBrokerHost.trim(),
						brokerPort: Number(mqttBrokerPort) || 1883,
						topic: mqttTopic.trim(),
						username: mqttUsername.trim() || null,
						password: mqttPassword || null,
						certificate: mqttCertificate.trim() || null,
						hasCertificate: !!mqttCertificate.trim()
					});
				}
			}

			let numTemperatureC: number | null = null;
			if (
				currentTemperature !== '' &&
				currentTemperature !== null &&
				!isNaN(Number(currentTemperature))
			) {
				numTemperatureC =
					settings.temperatureUnit === 'Fahrenheit'
						? fahrenheitToCelsius(Number(currentTemperature))
						: Number(currentTemperature);
			}

			let savedItem: EquipmentDto;
			if (equipment) {
				savedItem = await api.equipment.update(equipment.id, {
					name: name.trim(),
					type,
					subtype,
					capacity: numCapacity,
					currentVolume: numCurrent,
					unit,
					description: description.trim() || null,
					notes: notes.trim() || null,
					boilOffRatePerHour:
						type === 'Sensor' || boilOffRatePerHour === '' ? null : Number(boilOffRatePerHour),
					trubLossLiters:
						type === 'Sensor' || trubLossLiters === '' ? null : Number(trubLossLiters),
					mashTunDeadSpaceLiters:
						type === 'Sensor' || mashTunDeadSpaceLiters === ''
							? null
							: Number(mashTunDeadSpaceLiters),
					packagingLossLiters:
						type === 'Sensor' || packagingLossLiters === '' ? null : Number(packagingLossLiters),
					currentTemperatureC: numTemperatureC,
					connectionType,
					connectionConfigJson: configJson
				});
			} else {
				if (brewery.activeSetupId === DEFAULT_BREWERY_ID) {
					await brewery.syncFromBackend();
				}

				const targetSetupId =
					brewery.activeSetupId !== DEFAULT_BREWERY_ID
						? brewery.activeSetupId
						: (brewery.setups.find((s) => s.id !== DEFAULT_BREWERY_ID)?.id ??
							brewery.activeSetupId);

				savedItem = await api.equipment.create({
					brewerySetupId: targetSetupId,
					name: name.trim(),
					type,
					subtype,
					capacity: numCapacity,
					currentVolume: numCurrent,
					unit,
					description: description.trim() || null,
					notes: notes.trim() || null,
					boilOffRatePerHour:
						type === 'Sensor' || boilOffRatePerHour === '' ? null : Number(boilOffRatePerHour),
					trubLossLiters:
						type === 'Sensor' || trubLossLiters === '' ? null : Number(trubLossLiters),
					mashTunDeadSpaceLiters:
						type === 'Sensor' || mashTunDeadSpaceLiters === ''
							? null
							: Number(mashTunDeadSpaceLiters),
					packagingLossLiters:
						type === 'Sensor' || packagingLossLiters === '' ? null : Number(packagingLossLiters),
					currentTemperatureC: numTemperatureC,
					connectionType,
					connectionConfigJson: configJson
				});
			}

			onSaved(savedItem);
			onClose();
		} catch (err: unknown) {
			if (
				(err instanceof ApiClientError && (err.code === 'DUPLICATE_NAME' || err.status === 409)) ||
				(err instanceof Error &&
					(err.message.includes('DUPLICATE_NAME') ||
						err.message.toLowerCase().includes('already exists')))
			) {
				errorMessage = t('equipment.err_duplicate_name');
			} else {
				errorMessage = (err as Error).message || t('equipment.err_save_failed');
			}
		} finally {
			isSubmitting = false;
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && open) {
			onClose();
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if open}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center p-4"
		role="dialog"
		aria-modal="true"
		aria-labelledby="equipment-modal-title"
	>
		<!-- Backdrop -->
		<div
			class="fixed inset-0 bg-black/70 backdrop-blur-sm transition-opacity"
			onclick={onClose}
			aria-hidden="true"
		></div>

		<!-- Dialog Panel -->
		<div
			class="relative z-10 max-h-[92vh] w-full max-w-lg overflow-y-auto rounded-2xl border border-zinc-200 bg-white p-6 shadow-2xl transition-all dark:border-zinc-800 dark:bg-zinc-950"
		>
			<div
				class="flex items-start justify-between border-b border-zinc-200 pb-4 dark:border-zinc-800"
			>
				<div>
					<h2 id="equipment-modal-title" class="text-lg font-bold text-zinc-900 dark:text-zinc-100">
						{equipment ? t('equipment.edit_title') : t('equipment.add_title')}
					</h2>
					<div class="mt-1 flex items-center gap-1.5 text-xs text-amber-600 dark:text-amber-400">
						<Building2 class="h-3.5 w-3.5 shrink-0" />
						<span
							>{t('brewery.setup_label')}:
							<strong class="font-semibold text-zinc-800 dark:text-zinc-200"
								>{brewery.activeSetup.name}</strong
							></span
						>
					</div>
				</div>
				<button
					type="button"
					onclick={onClose}
					class="rounded-lg p-1 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 dark:hover:bg-zinc-900 dark:hover:text-zinc-200"
					aria-label={t('common.cancel')}
				>
					<X class="h-5 w-5" />
				</button>
			</div>

			<form onsubmit={handleSubmit} class="mt-4 space-y-4">
				{#if errorMessage}
					<div
						data-testid="equipment-error-banner"
						class="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
					>
						{errorMessage}
					</div>
				{/if}

				<!-- Name -->
				<div>
					<label
						for="equipment-name"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('equipment.name')} <span class="text-amber-500">*</span>
					</label>
					<input
						id="equipment-name"
						type="text"
						bind:value={name}
						placeholder={t('equipment.name_placeholder')}
						maxlength="100"
						required
						aria-invalid={isDuplicateName}
						aria-errormessage={isDuplicateName ? 'equipment-name-error' : undefined}
						class="mt-1 h-10 w-full rounded-xl border bg-white px-3 text-sm text-zinc-900 placeholder-zinc-400 transition-colors focus:outline-none dark:bg-zinc-900 dark:text-zinc-100 dark:placeholder-zinc-600 {isDuplicateName
							? 'border-red-500 focus:border-red-500 focus:ring-1 focus:ring-red-500/30 dark:border-red-500'
							: 'border-zinc-300 focus:border-amber-500 dark:border-zinc-800'}"
					/>
					{#if isDuplicateName}
						<p
							id="equipment-name-error"
							class="mt-1 text-xs text-red-600 dark:text-red-400"
							role="alert"
							aria-live="polite"
						>
							{t('equipment.err_duplicate_name')}
						</p>
					{/if}
				</div>

				<!-- Category Tabs -->
				<div>
					<span class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300">
						{t('equipment.category')} <span class="text-amber-500">*</span>
					</span>
					<div class="mt-1 grid grid-cols-5 gap-1.5 sm:gap-2">
						{#each ['Boiler', 'Fermenter', 'Keg', 'Sensor', 'Other'] as const as opt}
							<button
								type="button"
								data-testid={`category-tab-${opt.toLowerCase()}`}
								onclick={() => handleCategoryChange(opt)}
								class="rounded-xl border py-2 text-xs font-medium transition-colors {type === opt
									? 'border-amber-500 bg-amber-500/10 font-bold text-amber-600 dark:text-amber-400'
									: 'border-zinc-200 bg-zinc-50 text-zinc-700 hover:bg-zinc-100 dark:border-zinc-800 dark:bg-zinc-900/60 dark:text-zinc-300 dark:hover:bg-zinc-800'}"
							>
								{getCategoryName(opt)}
							</button>
						{/each}
					</div>
				</div>

				<!-- Selectable Thematic Vessel Styles -->
				<div>
					<span
						id="vessel-selector-label"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('equipment.vessel_type_label')} <span class="text-amber-500">*</span>
					</span>
					<div
						role="radiogroup"
						aria-labelledby="vessel-selector-label"
						class="mt-1.5 grid grid-cols-2 gap-2 sm:grid-cols-3"
					>
						{#each EQUIPMENT_SUBTYPES_BY_TYPE[type] || ['Other'] as subOpt}
							{@const isSelected = subtype === subOpt}
							<button
								type="button"
								role="radio"
								aria-checked={isSelected}
								onclick={() => handleSubtypeSelect(subOpt)}
								class="group relative flex flex-col items-center justify-between rounded-xl border p-2.5 text-center transition-all duration-200 {isSelected
									? 'border-amber-500 bg-amber-500/10 shadow-[0_0_14px_rgba(245,158,11,0.18)] ring-1 ring-amber-500/40'
									: 'border-zinc-200 bg-zinc-50/50 hover:border-zinc-300 hover:bg-zinc-100/60 dark:border-zinc-800 dark:bg-zinc-900/40 dark:hover:border-zinc-700 dark:hover:bg-zinc-900/80'}"
							>
								{#if isSelected}
									<span
										class="absolute top-1.5 right-1.5 flex h-4 w-4 items-center justify-center rounded-full bg-amber-500 text-zinc-950 shadow-sm"
									>
										<Check class="h-2.5 w-2.5 stroke-[3]" />
									</span>
								{/if}

								<div class="my-1 flex h-11 w-11 items-center justify-center">
									<VesselIcon
										subtype={subOpt}
										size={40}
										fillPercent={isSelected ? fillPercent : 0}
										active={isSelected}
										showMeter={false}
										class={isSelected
											? 'text-amber-500'
											: 'text-zinc-500 group-hover:text-zinc-700 dark:text-zinc-400 dark:group-hover:text-zinc-200'}
									/>
								</div>

								<div class="mt-1 flex flex-col items-center">
									<span
										class="text-xs font-bold {isSelected
											? 'text-amber-600 dark:text-amber-400'
											: 'text-zinc-800 dark:text-zinc-200'}"
									>
										{getSubtypeName(subOpt)}
									</span>
									<span
										class="mt-0.5 line-clamp-1 text-[10px] text-zinc-500 dark:text-zinc-400"
										title={getSubtypeDescription(subOpt)}
									>
										{getSubtypeDescription(subOpt)}
									</span>
								</div>
							</button>
						{/each}
					</div>
				</div>

				{#if isFillable}
					<!-- Capacity & Unit -->
					<div>
						<label
							for="equipment-capacity"
							class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
						>
							{t('equipment.capacity')} <span class="text-amber-500">*</span>
						</label>
						<div class="mt-1 flex items-center gap-2">
							<input
								id="equipment-capacity"
								type="number"
								step="0.1"
								min="0.1"
								max="100000"
								bind:value={capacity}
								placeholder={t('equipment.capacity_placeholder')}
								required
								class="h-10 flex-1 rounded-xl border border-zinc-300 bg-white px-3 font-mono text-sm text-zinc-900 placeholder-zinc-400 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100 dark:placeholder-zinc-600"
							/>
							<span
								class="inline-flex h-10 items-center rounded-xl border border-zinc-300 bg-zinc-100 px-3.5 text-xs font-bold text-zinc-700 dark:border-zinc-800 dark:bg-zinc-800 dark:text-zinc-300"
								title={t('equipment.unit')}
							>
								{unit === 'Liters' ? t('equipment.liters') : t('equipment.gallons')}
							</span>
						</div>
						{#if convertedPreview}
							<p class="mt-1 text-xs text-zinc-500 dark:text-zinc-400">
								{convertedPreview}
							</p>
						{/if}
					</div>

					<!-- Fill Level Section (Visual Slider + Live Preview) -->
					<div
						class="rounded-2xl border border-zinc-200/80 bg-zinc-50/70 p-3.5 dark:border-zinc-800/80 dark:bg-zinc-900/40"
					>
						<div class="flex items-center justify-between">
							<label for="fill-slider" class="text-xs font-bold text-zinc-700 dark:text-zinc-300">
								{t('equipment.fill_level')}
							</label>
							<span
								class="inline-flex items-center rounded-lg border border-amber-500/30 bg-amber-500/10 px-2 py-0.5 font-mono text-xs font-bold text-amber-600 dark:text-amber-400"
							>
								{t('equipment.filled_pct', { pct: fillPercent })}
							</span>
						</div>

						<div class="mt-2.5 flex items-center gap-3">
							<!-- Real-time Animated Vessel Graphic Feedback -->
							<div
								class="flex h-14 w-14 flex-shrink-0 items-center justify-center rounded-xl border border-zinc-200 bg-white p-1 shadow-inner dark:border-zinc-800 dark:bg-zinc-950"
							>
								<VesselIcon
									{subtype}
									size={42}
									{fillPercent}
									active={true}
									class="text-zinc-800 dark:text-zinc-200"
								/>
							</div>

							<!-- Slider & Numeric Input Controls -->
							<div class="flex flex-1 flex-col gap-2">
								<div class="flex items-center gap-2">
									<input
										id="fill-slider"
										type="range"
										min="0"
										max={Number(capacity) || 100}
										step="0.5"
										bind:value={currentVolume}
										aria-label={t('equipment.fill_level')}
										aria-valuemin="0"
										aria-valuemax={Number(capacity) || 100}
										aria-valuenow={Number(currentVolume) || 0}
										aria-valuetext="{currentVolume || 0} {unit === 'Liters'
											? 'L'
											: 'gal'}, {fillPercent}% full"
										class="h-2 w-full cursor-pointer appearance-none rounded-lg bg-zinc-200 accent-amber-500 dark:bg-zinc-800"
									/>
									<div class="flex items-center gap-1">
										<input
											type="number"
											min="0"
											max={Number(capacity) || 100000}
											step="0.1"
											bind:value={currentVolume}
											placeholder="0"
											class="h-8 w-16 rounded-lg border border-zinc-300 bg-white px-2 text-right font-mono text-xs font-bold text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100"
										/>
										<span class="text-xs font-semibold text-zinc-500">
											{unit === 'Liters' ? 'L' : 'gal'}
										</span>
									</div>
								</div>

								<!-- Quick Preset Buttons -->
								<div class="flex items-center justify-between gap-1 pt-0.5">
									{#each [{ label: t('equipment.preset_empty'), pct: 0 }, { label: '25%', pct: 25 }, { label: '50%', pct: 50 }, { label: '75%', pct: 75 }, { label: t('equipment.preset_full'), pct: 100 }] as preset}
										<button
											type="button"
											onclick={() => setPreset(preset.pct)}
											class="rounded-lg border px-2 py-0.5 text-[10px] font-bold transition-colors {fillPercent ===
											preset.pct
												? 'border-amber-500 bg-amber-500/20 text-amber-600 dark:text-amber-400'
												: 'border-zinc-200 bg-white text-zinc-600 hover:bg-zinc-100 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800'}"
										>
											{preset.label}
										</button>
									{/each}
								</div>
							</div>
						</div>
					</div>
				{:else}
					<!-- Sensor / Probe Device Graphic Preview -->
					<div
						class="flex items-center gap-3.5 rounded-2xl border border-amber-500/20 bg-amber-500/5 p-3.5 dark:border-amber-500/20 dark:bg-amber-500/10"
					>
						<div
							class="flex h-14 w-14 flex-shrink-0 items-center justify-center rounded-xl border border-zinc-200 bg-white p-1 shadow-inner dark:border-zinc-800 dark:bg-zinc-950"
						>
							<VesselIcon
								{subtype}
								size={42}
								active={true}
								class="text-zinc-800 dark:text-zinc-200"
							/>
						</div>
						<div>
							<div class="font-bold text-zinc-900 dark:text-zinc-100">
								{getSubtypeName(subtype)}
							</div>
							<p class="mt-0.5 text-xs text-zinc-500 dark:text-zinc-400">
								{getSubtypeDescription(subtype)}
							</p>
						</div>
					</div>
				{/if}

				<!-- Loss & Calibration Parameters -->
				{#if type !== 'Sensor'}
					<div
						class="rounded-2xl border border-zinc-200/80 bg-zinc-50/70 p-3.5 dark:border-zinc-800/80 dark:bg-zinc-900/40"
					>
						<div class="mb-2">
							<span class="text-xs font-bold text-zinc-800 dark:text-zinc-200">
								{t('equipment.loss_parameters')}
							</span>
							<p class="text-[11px] text-zinc-500 dark:text-zinc-400">
								{t('equipment.loss_parameters_desc')}
							</p>
						</div>

						<div class="grid grid-cols-1 gap-3 pt-1 sm:grid-cols-2">
							{#if type === 'Boiler'}
								<div>
									<label
										for="boil-off-rate"
										class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
									>
										{t('equipment.boil_off_rate')} ({t('equipment.boil_off_rate_unit')})
									</label>
									<input
										id="boil-off-rate"
										type="number"
										step="0.1"
										min="0"
										max="50"
										bind:value={boilOffRatePerHour}
										placeholder={t('equipment.boil_off_rate_placeholder')}
										class="mt-1 h-9 w-full rounded-xl border border-zinc-300 bg-white px-3 font-mono text-sm text-zinc-900 placeholder-zinc-400 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100 dark:placeholder-zinc-600"
									/>
								</div>
								<div>
									<label
										for="boiler-trub-loss"
										class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
									>
										{t('equipment.trub_loss')} (L)
									</label>
									<input
										id="boiler-trub-loss"
										type="number"
										step="0.1"
										min="0"
										max="100"
										bind:value={trubLossLiters}
										placeholder={t('equipment.trub_loss_placeholder')}
										class="mt-1 h-9 w-full rounded-xl border border-zinc-300 bg-white px-3 font-mono text-sm text-zinc-900 placeholder-zinc-400 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100 dark:placeholder-zinc-600"
									/>
								</div>
								<div>
									<label
										for="mash-dead-space"
										class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
									>
										{t('equipment.mash_tun_dead_space')} (L)
									</label>
									<input
										id="mash-dead-space"
										type="number"
										step="0.1"
										min="0"
										max="50"
										bind:value={mashTunDeadSpaceLiters}
										placeholder={t('equipment.mash_tun_dead_space_placeholder')}
										class="mt-1 h-9 w-full rounded-xl border border-zinc-300 bg-white px-3 font-mono text-sm text-zinc-900 placeholder-zinc-400 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100 dark:placeholder-zinc-600"
									/>
								</div>
							{:else if type === 'Fermenter'}
								<div class="sm:col-span-2">
									<label
										for="fermenter-trub-loss"
										class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
									>
										{t('equipment.trub_loss_fermenter')} (L)
									</label>
									<input
										id="fermenter-trub-loss"
										type="number"
										step="0.1"
										min="0"
										max="100"
										bind:value={trubLossLiters}
										placeholder={t('equipment.trub_loss_placeholder')}
										class="mt-1 h-9 w-full rounded-xl border border-zinc-300 bg-white px-3 font-mono text-sm text-zinc-900 placeholder-zinc-400 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100 dark:placeholder-zinc-600"
									/>
								</div>
							{:else}
								<div class="sm:col-span-2">
									<label
										for="packaging-loss"
										class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
									>
										{t('equipment.packaging_loss')} (L)
									</label>
									<input
										id="packaging-loss"
										type="number"
										step="0.1"
										min="0"
										max="50"
										bind:value={packagingLossLiters}
										placeholder={t('equipment.packaging_loss_placeholder')}
										class="mt-1 h-9 w-full rounded-xl border border-zinc-300 bg-white px-3 font-mono text-sm text-zinc-900 placeholder-zinc-400 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100 dark:placeholder-zinc-600"
									/>
								</div>
							{/if}
						</div>
					</div>
				{/if}

				<!-- External Connectivity & Temperature Section -->
				<div
					class="rounded-2xl border border-zinc-200/80 bg-zinc-50/70 p-3.5 dark:border-zinc-800/80 dark:bg-zinc-900/40"
				>
					<div class="flex items-center justify-between">
						<div class="flex items-center gap-1.5">
							<Thermometer class="h-4 w-4 text-amber-500" />
							<span class="text-xs font-bold text-zinc-800 dark:text-zinc-200">
								{t('equipment.telemetry_title')}
							</span>
						</div>
						<span
							class="rounded-md border border-amber-500/20 bg-amber-500/10 px-1.5 py-0.5 text-[10px] font-semibold text-amber-600 dark:text-amber-400"
						>
							{t('equipment.telemetry_badge')}
						</span>
					</div>
					<p class="mt-0.5 text-[11px] text-zinc-500 dark:text-zinc-400">
						{t('equipment.telemetry_desc')}
					</p>

					<!-- Current Temperature & Connection Protocol -->
					<div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2">
						<div>
							<label
								for="equipment-current-temp"
								class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('equipment.temperature_label')} ({settings.tempShort})
							</label>
							<div class="mt-1 flex items-center gap-2">
								<input
									id="equipment-current-temp"
									type="number"
									step="0.1"
									min="-20"
									max="120"
									bind:value={currentTemperature}
									placeholder={t('equipment.temperature_placeholder')}
									class="h-9 w-full rounded-xl border border-zinc-300 bg-white px-3 font-mono text-sm text-zinc-900 placeholder-zinc-400 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100 dark:placeholder-zinc-600"
								/>
								{#if currentTemperature !== ''}
									<button
										type="button"
										onclick={() => (currentTemperature = '')}
										class="rounded-lg p-1.5 text-xs text-zinc-400 hover:text-zinc-600 dark:hover:text-zinc-200"
										title={t('equipment.clear_temperature')}
									>
										<X class="h-4 w-4" />
									</button>
								{/if}
							</div>
						</div>

						<!-- Connection Protocol Selector -->
						<div>
							<label
								for="equipment-connection-type"
								class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
							>
								{t('equipment.connection_type_label')}
							</label>
							<select
								id="equipment-connection-type"
								bind:value={connectionType}
								class="mt-1 h-9 w-full rounded-xl border border-zinc-300 bg-white px-3 text-xs font-semibold text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"
							>
								<option value="None">{t('equipment.conn_none')}</option>
								<option value="HttpPush">{t('equipment.conn_http_push')}</option>
								<option value="HttpPoll">{t('equipment.conn_http_poll')}</option>
								<option value="Mqtt">{t('equipment.conn_mqtt')}</option>
							</select>
						</div>
					</div>

					<!-- HttpPush (Inbound Webhook) View -->
					{#if connectionType === 'HttpPush'}
						<div
							class="mt-3 rounded-xl border border-amber-500/20 bg-amber-500/5 p-3 dark:border-amber-500/20 dark:bg-amber-500/10"
						>
							<div class="flex items-center justify-between">
								<div
									class="flex items-center gap-1.5 text-xs font-bold text-amber-700 dark:text-amber-400"
								>
									<Wifi class="h-3.5 w-3.5" />
									<span>{t('equipment.webhook_instructions_title')}</span>
								</div>
								{#if equipment}
									<button
										type="button"
										onclick={() => (showRegenerateTokenModal = true)}
										disabled={isRegeneratingToken}
										class="flex items-center gap-1 text-[11px] font-semibold text-zinc-500 hover:text-amber-600 dark:text-zinc-400 dark:hover:text-amber-400"
									>
										<RefreshCw class="h-3 w-3 {isRegeneratingToken ? 'animate-spin' : ''}" />
										<span>{t('equipment.regenerate_token')}</span>
									</button>
								{/if}
							</div>
							<p class="mt-1 text-[11px] text-zinc-600 dark:text-zinc-400">
								{t('equipment.webhook_instructions_desc')}
							</p>

							{#if connectionToken}
								<div class="mt-2 flex items-center gap-2">
									<input
										type="text"
										readonly
										value={webhookUrl}
										class="h-8 flex-1 rounded-lg border border-zinc-200 bg-white px-2.5 font-mono text-xs text-zinc-700 select-all dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-300"
									/>
									<button
										type="button"
										onclick={copyWebhookUrl}
										class="flex h-8 items-center gap-1 rounded-lg border border-zinc-300 bg-white px-2.5 text-xs font-bold text-zinc-800 hover:bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-200"
									>
										{#if copiedWebhook}
											<CheckCheck class="h-3.5 w-3.5 text-emerald-500" />
											<span class="text-emerald-600 dark:text-emerald-400"
												>{t('equipment.copied')}</span
											>
										{:else}
											<Copy class="h-3.5 w-3.5" />
											<span>{t('equipment.copy_url')}</span>
										{/if}
									</button>
								</div>
							{:else}
								<p class="mt-2 text-xs text-amber-600 italic dark:text-amber-400">
									{t('equipment.webhook_token_on_save')}
								</p>
							{/if}

							<div class="mt-2 text-[10px] text-zinc-500 dark:text-zinc-400">
								<span>
									{t('equipment.supported_formats')}: <code>Tilt</code>, <code>iSpindel</code>,
									<code>Generic JSON</code>
								</span>
							</div>
						</div>
					{/if}

					<!-- HttpPoll View -->
					{#if connectionType === 'HttpPoll'}
						<div
							class="mt-3 space-y-2 rounded-xl border border-zinc-200 bg-white/60 p-3 dark:border-zinc-800 dark:bg-zinc-900/60"
						>
							<div
								class="flex items-center gap-1.5 text-xs font-bold text-zinc-800 dark:text-zinc-200"
							>
								<Globe class="h-3.5 w-3.5 text-amber-500" />
								<span>{t('equipment.poll_config_title')}</span>
							</div>

							<div>
								<label
									for="poll-url"
									class="block text-[11px] font-semibold text-zinc-600 dark:text-zinc-400"
								>
									{t('equipment.poll_url_label')}
								</label>
								<input
									id="poll-url"
									type="url"
									bind:value={pollUrl}
									placeholder="http://192.168.1.120/status"
									class="mt-1 h-8 w-full rounded-lg border border-zinc-300 bg-white px-2.5 font-mono text-xs text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100"
								/>
							</div>

							<div class="grid grid-cols-2 gap-2">
								<div>
									<label
										for="poll-interval"
										class="block text-[11px] font-semibold text-zinc-600 dark:text-zinc-400"
									>
										{t('equipment.poll_interval_label')}
									</label>
									<select
										id="poll-interval"
										bind:value={pollIntervalSeconds}
										class="mt-1 h-8 w-full rounded-lg border border-zinc-300 bg-white px-2 text-xs font-medium text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100"
									>
										<option value={30}>30 {t('equipment.seconds')}</option>
										<option value={60}>60 {t('equipment.seconds')}</option>
										<option value={120}>2 {t('equipment.minutes')}</option>
										<option value={300}>5 {t('equipment.minutes')}</option>
										<option value={900}>15 {t('equipment.minutes')}</option>
									</select>
								</div>
								<div>
									<label
										for="poll-json-path"
										class="block text-[11px] font-semibold text-zinc-600 dark:text-zinc-400"
									>
										{t('equipment.poll_json_path_label')}
									</label>
									<input
										id="poll-json-path"
										type="text"
										bind:value={pollJsonPath}
										placeholder="temperature"
										class="mt-1 h-8 w-full rounded-lg border border-zinc-300 bg-white px-2.5 font-mono text-xs text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100"
									/>
								</div>
							</div>

							<div class="flex items-center justify-between pt-1">
								<button
									type="button"
									onclick={handleTestPoll}
									disabled={isTestingPoll || !pollUrl.trim()}
									class="flex items-center gap-1.5 rounded-lg border border-zinc-300 bg-zinc-100 px-3 py-1 text-xs font-semibold text-zinc-800 hover:bg-zinc-200 disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-200"
								>
									{#if isTestingPoll}
										<Loader2 class="h-3 w-3 animate-spin text-amber-500" />
									{/if}
									<span>{t('equipment.poll_test_btn')}</span>
								</button>
								{#if testPollMessage}
									<span
										class="text-[11px] {testPollMessage.type === 'success'
											? 'text-emerald-600 dark:text-emerald-400'
											: 'text-red-600 dark:text-red-400'}"
									>
										{testPollMessage.text}
									</span>
								{/if}
							</div>
						</div>
					{/if}

					<!-- Mqtt View -->
					{#if connectionType === 'Mqtt'}
						<div
							class="mt-3 space-y-3 rounded-xl border border-zinc-200 bg-white/60 p-3.5 dark:border-zinc-800 dark:bg-zinc-900/60"
						>
							<div class="flex flex-wrap items-center justify-between gap-2">
								<div
									class="flex items-center gap-1.5 text-xs font-bold text-zinc-800 dark:text-zinc-200"
								>
									<Radio class="h-3.5 w-3.5 text-amber-500" />
									<span>{t('equipment.mqtt_config_title')}</span>
								</div>

								<!-- Configuration Source Selector (User Settings vs Custom) -->
								<div
									class="flex rounded-lg border border-zinc-200 bg-zinc-100 p-0.5 text-[11px] dark:border-zinc-700 dark:bg-zinc-800"
								>
									<button
										type="button"
										data-testid="mqtt-source-settings-btn"
										onclick={() => (mqttSource = 'userSettings')}
										class="rounded-md px-2.5 py-0.5 font-semibold transition-all {mqttSource ===
										'userSettings'
											? 'bg-white text-zinc-900 shadow-xs dark:bg-zinc-900 dark:text-white'
											: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-white'}"
									>
										{t('equipment.mqtt_source_settings')}
									</button>
									<button
										type="button"
										data-testid="mqtt-source-custom-btn"
										onclick={() => (mqttSource = 'custom')}
										class="rounded-md px-2.5 py-0.5 font-semibold transition-all {mqttSource ===
										'custom'
											? 'bg-white text-zinc-900 shadow-xs dark:bg-zinc-900 dark:text-white'
											: 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-white'}"
									>
										{t('equipment.mqtt_source_custom')}
									</button>
								</div>
							</div>

							{#if mqttSource === 'userSettings'}
								{#if settings.hasMqttConfigured}
									<!-- Display configured user settings broker summary -->
									<div
										data-testid="mqtt-user-settings-summary"
										class="rounded-lg border p-3 {settings.mqttConnected === false
											? 'border-red-500/20 bg-red-500/5 dark:border-red-500/20 dark:bg-red-500/10'
											: 'border-amber-500/20 bg-amber-500/5 dark:border-amber-500/20 dark:bg-amber-500/10'}"
									>
										<div class="flex flex-wrap items-center justify-between gap-2">
											<div class="flex items-center gap-2">
												<span
													class="h-2 w-2 rounded-full {settings.mqttConnected === false
														? 'bg-red-500'
														: 'animate-pulse bg-emerald-500'}"
												></span>
												<span class="font-mono text-xs font-bold text-zinc-900 dark:text-white">
													{settings.mqttHost}:{settings.mqttPort}
												</span>
												<span
													class="rounded px-1.5 py-0.5 text-[10px] font-semibold {settings.mqttConnected ===
													false
														? 'bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300'
														: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300'}"
												>
													{settings.mqttConnected === false
														? t('equipment.mqtt_disconnected')
														: t('equipment.mqtt_connected')}
												</span>
											</div>
											<div class="flex items-center gap-2">
												{#if settings.mqttUsername}
													<span
														class="rounded bg-zinc-200/70 px-1.5 py-0.5 text-[10px] font-semibold text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
													>
														{t('equipment.mqtt_auth_configured')}
													</span>
												{/if}
												{#if settings.mqttCertificate}
													<span
														class="rounded bg-zinc-200/70 px-1.5 py-0.5 text-[10px] font-semibold text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
													>
														{t('equipment.mqtt_cert_configured')}
													</span>
												{/if}
												<a
													href="/settings#connectivity"
													target="_blank"
													class="text-[11px] font-semibold text-amber-600 hover:underline dark:text-amber-400"
												>
													{t('equipment.mqtt_configure_in_settings')}
												</a>
											</div>
										</div>
										<p class="mt-1 text-[11px] text-zinc-500 dark:text-zinc-400">
											{t('equipment.mqtt_using_settings')}
										</p>
									</div>
								{:else}
									<!-- Alert: No settings configured yet -->
									<div
										data-testid="mqtt-no-settings-alert"
										class="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-amber-500/30 bg-amber-500/10 p-3"
									>
										<div class="text-[11px] text-amber-900 dark:text-amber-200">
											{t('equipment.mqtt_settings_not_configured')}
										</div>
										<a
											href="/settings#connectivity"
											target="_blank"
											class="rounded-lg bg-amber-500 px-2.5 py-1 text-[11px] font-bold text-zinc-950 hover:bg-amber-400"
										>
											{t('equipment.mqtt_configure_in_settings')}
										</a>
									</div>
								{/if}
							{:else}
								<!-- Custom Broker Fields -->
								<div class="grid grid-cols-3 gap-2">
									<div class="col-span-2">
										<label
											for="mqtt-host"
											class="block text-[11px] font-semibold text-zinc-600 dark:text-zinc-400"
										>
											{t('equipment.mqtt_host')}
										</label>
										<input
											id="mqtt-host"
											data-testid="mqtt-custom-host-input"
											type="text"
											bind:value={mqttBrokerHost}
											placeholder="192.168.1.50"
											class="mt-1 h-8 w-full rounded-lg border border-zinc-300 bg-white px-2.5 font-mono text-xs text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100"
										/>
									</div>
									<div>
										<label
											for="mqtt-port"
											class="block text-[11px] font-semibold text-zinc-600 dark:text-zinc-400"
										>
											{t('equipment.mqtt_port')}
										</label>
										<input
											id="mqtt-port"
											data-testid="mqtt-custom-port-input"
											type="number"
											bind:value={mqttBrokerPort}
											placeholder="1883"
											class="mt-1 h-8 w-full rounded-lg border border-zinc-300 bg-white px-2.5 font-mono text-xs text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100"
										/>
									</div>
								</div>

								<div class="grid grid-cols-2 gap-2">
									<div>
										<label
											for="mqtt-custom-username"
											class="block text-[11px] font-semibold text-zinc-600 dark:text-zinc-400"
										>
											{t('equipment.mqtt_username')}
										</label>
										<input
											id="mqtt-custom-username"
											data-testid="mqtt-custom-username-input"
											type="text"
											bind:value={mqttUsername}
											placeholder="sensor_user"
											class="mt-1 h-8 w-full rounded-lg border border-zinc-300 bg-white px-2.5 text-xs text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100"
										/>
									</div>
									<div>
										<label
											for="mqtt-custom-password"
											class="block text-[11px] font-semibold text-zinc-600 dark:text-zinc-400"
										>
											{t('equipment.mqtt_password')}
										</label>
										<div class="relative mt-1">
											<input
												id="mqtt-custom-password"
												data-testid="mqtt-custom-password-input"
												type={showMqttCustomPassword ? 'text' : 'password'}
												bind:value={mqttPassword}
												placeholder="••••••••"
												class="h-8 w-full rounded-lg border border-zinc-300 bg-white px-2.5 pr-8 text-xs text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100"
											/>
											<button
												type="button"
												onclick={() => (showMqttCustomPassword = !showMqttCustomPassword)}
												class="absolute inset-y-0 right-0 flex items-center pr-2 text-zinc-400 hover:text-zinc-600"
											>
												{#if showMqttCustomPassword}
													<EyeOff class="h-3.5 w-3.5" />
												{:else}
													<Eye class="h-3.5 w-3.5" />
												{/if}
											</button>
										</div>
									</div>
								</div>

								<div>
									<label
										for="mqtt-custom-cert"
										class="block text-[11px] font-semibold text-zinc-600 dark:text-zinc-400"
									>
										{t('equipment.mqtt_certificate')}
									</label>
									<textarea
										id="mqtt-custom-cert"
										data-testid="mqtt-custom-cert-input"
										rows="2"
										bind:value={mqttCertificate}
										placeholder="-----BEGIN CERTIFICATE-----..."
										class="mt-1 w-full rounded-lg border border-zinc-300 bg-white p-2 font-mono text-[11px] text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100"
									></textarea>
								</div>
							{/if}

							<!-- Sensor Specific Topic (Always Required) -->
							<div>
								<div class="flex items-center justify-between">
									<label
										for="mqtt-topic"
										class="block text-[11px] font-semibold text-zinc-600 dark:text-zinc-400"
									>
										{t('equipment.mqtt_topic')}
									</label>
									<button
										type="button"
										data-testid="auto-generate-topic-btn"
										class="text-[11px] font-medium text-amber-600 hover:text-amber-700 dark:text-amber-400 dark:hover:text-amber-300"
										onclick={() => {
											isTopicCustomized = false;
											mqttTopic = generateEquipmentTopic(settings.mqttTopicPrefix, name);
										}}
									>
										{t('equipment.mqtt_topic_auto_slug')}
									</button>
								</div>
								<input
									id="mqtt-topic"
									data-testid="mqtt-topic-input"
									type="text"
									bind:value={mqttTopic}
									oninput={() => {
										isTopicCustomized = true;
									}}
									placeholder={generateEquipmentTopic(settings.mqttTopicPrefix, 'kettle') ||
										'brewyou/equipment/kettle/telemetry'}
									class="mt-1 h-8 w-full rounded-lg border border-zinc-300 bg-white px-2.5 font-mono text-xs text-zinc-900 focus:border-amber-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100"
								/>
							</div>
						</div>
					{/if}
				</div>

				<!-- Description -->
				<div>
					<label
						for="equipment-description"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('equipment.desc_label')}
					</label>
					<input
						id="equipment-description"
						type="text"
						bind:value={description}
						placeholder={t('equipment.desc_placeholder')}
						maxlength="500"
						class="mt-1 h-10 w-full rounded-xl border border-zinc-300 bg-white px-3 text-sm text-zinc-900 placeholder-zinc-400 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100 dark:placeholder-zinc-600"
					/>
				</div>

				<!-- Notes -->
				<div>
					<label
						for="equipment-notes"
						class="block text-xs font-semibold text-zinc-700 dark:text-zinc-300"
					>
						{t('equipment.notes_label')}
					</label>
					<textarea
						id="equipment-notes"
						rows="2"
						bind:value={notes}
						placeholder={t('equipment.notes_placeholder')}
						maxlength="2000"
						class="mt-1 w-full rounded-xl border border-zinc-300 bg-white p-3 text-sm text-zinc-900 placeholder-zinc-400 focus:border-amber-500 focus:outline-none dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100 dark:placeholder-zinc-600"
					></textarea>
				</div>

				<!-- Actions -->
				<div class="flex items-center justify-end gap-3 pt-3">
					<button
						type="button"
						onclick={onClose}
						disabled={isSubmitting}
						class="rounded-xl px-4 py-2 text-sm font-medium text-zinc-700 hover:bg-zinc-100 dark:text-zinc-300 dark:hover:bg-zinc-900"
					>
						{t('common.cancel')}
					</button>
					<button
						type="submit"
						data-testid="save-equipment-btn"
						disabled={isSubmitting || isDuplicateName}
						class="flex items-center gap-2 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-5 py-2 text-sm font-bold text-zinc-950 shadow-md transition-all hover:from-amber-400 hover:to-amber-500 disabled:opacity-50"
					>
						{#if isSubmitting}
							<Loader2 class="h-4 w-4 animate-spin" />
						{/if}
						{equipment ? t('equipment.save_changes') : t('equipment.create_btn')}
					</button>
				</div>
			</form>
		</div>
	</div>
{/if}

<ConfirmModal
	open={showRegenerateTokenModal}
	title={t('equipment.regenerate_token')}
	message={t('equipment.confirm_regenerate_token')}
	confirmText={t('equipment.regenerate_token')}
	variant="warning"
	loading={isRegeneratingToken}
	confirmTestId="confirm-regenerate-token-btn"
	cancelTestId="cancel-regenerate-token-btn"
	onClose={() => (showRegenerateTokenModal = false)}
	onConfirm={handleConfirmRegenerateToken}
/>
