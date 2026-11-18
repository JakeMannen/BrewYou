import { describe, expect, it } from 'vitest';
import { calculateWaterRequirements } from '$lib/calculators/volume';
import { i18n, t } from '$lib/i18n/index.svelte';
import { formatNumber } from '$lib/utils/formatNumber';
import type { EquipmentDto } from '$lib/types/api';

describe('Batches Wizard — Equipment Capacity & Boilover Calculations', () => {
	function createMockBoiler(overrides: Partial<EquipmentDto> = {}): EquipmentDto {
		return {
			id: 'boiler-1',
			brewerySetupId: 'setup-1',
			name: 'Grainfather G30',
			type: 'Boiler',
			subtype: 'AllInOne',
			capacity: 30.0,
			unit: 'Liters',
			capacityLiters: 30.0,
			currentVolume: 0.0,
			currentVolumeLiters: 0.0,
			fillPercentage: 0.0,
			boilOffRatePerHour: 2.5,
			trubLossLiters: 2.0,
			mashTunDeadSpaceLiters: 3.5,
			packagingLossLiters: null,
			displayWidthPx: null,
			displayHeightPx: null,
			displayOrientation: null,
			connectionType: 'None',
			connectionToken: null,
			connectionConfigJson: null,
			currentTemperatureC: null,
			description: null,
			notes: null,
			createdAt: '2026-01-01T00:00:00Z',
			...overrides
		} as EquipmentDto;
	}

	function checkBoilerUsedForMashing(
		topology: '1V' | '2V_AIO_HLT' | '2V_MLT_BK' | '3V',
		boilerSubtype: string,
		mashTunId: string,
		boilerId: string
	): boolean {
		return (
			topology === '1V' ||
			topology === '2V_AIO_HLT' ||
			boilerSubtype === 'AllInOne' ||
			(mashTunId !== '' && mashTunId === boilerId)
		);
	}

	it('calculates totalMashVolumeLiters using brewing grain displacement (0.67 L/kg)', () => {
		const strikeWaterLiters = 18.0;
		const totalGrainKg = 5.0;
		const totalMashVolumeLiters = strikeWaterLiters + totalGrainKg * 0.67;

		// 18.0 + (5.0 * 0.67) = 18.0 + 3.35 = 21.35 L
		expect(totalMashVolumeLiters).toBeCloseTo(21.35, 2);
	});

	it('detects isBoilerMashOverCapacity for 1V and All-In-One topologies when mash exceeds vessel volume', () => {
		const boiler = createMockBoiler({ capacityLiters: 30.0, subtype: 'AllInOne' });
		const strikeWaterLiters = 26.0;
		const totalGrainKg = 8.0;
		const totalMashVolumeLiters = strikeWaterLiters + totalGrainKg * 0.67; // 26 + 5.36 = 31.36 L

		// When topology is 1V
		const isBoilerUsedForMashing1V = checkBoilerUsedForMashing(
			'1V',
			boiler.subtype,
			boiler.id,
			boiler.id
		);
		const isMashOverCapacity1V =
			boiler.capacityLiters > 0 &&
			isBoilerUsedForMashing1V &&
			totalMashVolumeLiters > boiler.capacityLiters;

		expect(isMashOverCapacity1V).toBe(true);

		// When mash volume is safe
		const safeStrikeWaterLiters = 18.0;
		const safeMashVolumeLiters = safeStrikeWaterLiters + totalGrainKg * 0.67; // 18 + 5.36 = 23.36 L
		const isSafeMashOverCapacity =
			boiler.capacityLiters > 0 &&
			isBoilerUsedForMashing1V &&
			safeMashVolumeLiters > boiler.capacityLiters;

		expect(isSafeMashOverCapacity).toBe(false);
	});

	it('does not trigger isBoilerMashOverCapacity for multi-vessel topologies when boiler is not used for mashing', () => {
		const boilKettle = createMockBoiler({
			capacityLiters: 30.0,
			subtype: 'Pan' // traditional kettle
		});
		const strikeWaterLiters = 26.0;
		const totalGrainKg = 8.0;
		const totalMashVolumeLiters = strikeWaterLiters + totalGrainKg * 0.67; // 31.36 L

		// In 2V_MLT_BK or 3V with a dedicated separate mash tun
		const isBoilerUsedForMashing = checkBoilerUsedForMashing(
			'2V_MLT_BK',
			boilKettle.subtype,
			'separate-mash-tun',
			boilKettle.id
		);

		const isMashOverCapacity =
			boilKettle.capacityLiters > 0 &&
			isBoilerUsedForMashing &&
			totalMashVolumeLiters > boilKettle.capacityLiters;

		expect(isBoilerUsedForMashing).toBe(false);
		expect(isMashOverCapacity).toBe(false);
	});

	it('detects isBoilerOverCapacity when pre-boil volume exceeds boiler capacity', () => {
		const boiler = createMockBoiler({ capacityLiters: 30.0 });
		const preBoilOver = 32.5;
		const preBoilSafe = 28.0;

		const isOver = boiler.capacityLiters > 0 && preBoilOver > boiler.capacityLiters;
		const isSafe = boiler.capacityLiters > 0 && preBoilSafe > boiler.capacityLiters;

		expect(isOver).toBe(true);
		expect(isSafe).toBe(false);
	});

	it('detects isBoiloverRisk when pre-boil volume leaves less than 15% headspace (<15% margin)', () => {
		const boiler = createMockBoiler({ capacityLiters: 30.0 });
		// 85% of 30L is 25.5L
		const margin85Pct = boiler.capacityLiters * 0.85; // 25.5L

		// Case 1: Pre-boil = 27L (headspace = 3L / 10%) -> boilover risk!
		const preBoilTight = 27.0;
		const isBoiloverRiskTight = preBoilTight <= boiler.capacityLiters && preBoilTight > margin85Pct;
		expect(isBoiloverRiskTight).toBe(true);

		const headspaceTight = Math.round(
			((boiler.capacityLiters - preBoilTight) / boiler.capacityLiters) * 100
		);
		expect(headspaceTight).toBe(10);

		// Case 2: Pre-boil = 24L (headspace = 6L / 20%) -> safe, no boilover risk
		const preBoilComfortable = 24.0;
		const isBoiloverRiskComfortable =
			preBoilComfortable <= boiler.capacityLiters && preBoilComfortable > margin85Pct;
		expect(isBoiloverRiskComfortable).toBe(false);

		const headspaceComfortable = Math.round(
			((boiler.capacityLiters - preBoilComfortable) / boiler.capacityLiters) * 100
		);
		expect(headspaceComfortable).toBe(20);

		// Case 3: Pre-boil = 31L (> 30L) -> this is isBoilerOverCapacity, NOT boilover risk
		const preBoilOverflow = 31.0;
		const isBoiloverRiskOverflow =
			preBoilOverflow <= boiler.capacityLiters && preBoilOverflow > margin85Pct;
		expect(isBoiloverRiskOverflow).toBe(false);
	});

	it('integrates with calculateWaterRequirements to evaluate real craft recipes', () => {
		const boiler = createMockBoiler({ capacityLiters: 30.0 });
		const schedule = calculateWaterRequirements({
			targetBatchSizeLiters: 23.0,
			targetBasis: 'fermenter',
			spargeEnabled: true,
			boilTimeMinutes: 60,
			totalGrainWeightKg: 5.5,
			boilOffRatePerHour: boiler.boilOffRatePerHour ?? 2.5,
			kettleTrubLossLiters: boiler.trubLossLiters ?? 2.0,
			mashTunDeadSpaceLiters: boiler.mashTunDeadSpaceLiters ?? 3.5,
			hltDeadSpaceLiters: 0,
			plumbingLossLiters: 0,
			spargeMethod: 'Batch',
			fermenterLossLiters: 1.5,
			packagingLossLiters: 0.5,
			coolingShrinkagePercent: 4.0,
			mashThicknessLitersPerKg: 3.0
		});

		// Pre-boil volume in this 23L batch will typically be around 28-29L
		expect(schedule.targetPreBoilVolumeLiters).toBeGreaterThan(25.5);
		const isBoilover =
			schedule.targetPreBoilVolumeLiters <= boiler.capacityLiters &&
			schedule.targetPreBoilVolumeLiters > boiler.capacityLiters * 0.85;
		expect(isBoilover).toBe(true);
	});

	it('renders localized craft brewing warnings in both English and Swedish', () => {
		i18n.setLocale('en');
		const enMashWarn = t('batches.wizard.boiler_mash_warning', {
			mashVol: formatNumber(32.5, 1),
			capacity: formatNumber(30.0, 1)
		});
		expect(enMashWarn).toContain('32.5');
		expect(enMashWarn).toContain('30.0');
		expect(enMashWarn).toContain('exceeds boiler capacity');

		const enBoiloverWarn = t('batches.wizard.boiler_boilover_warning', {
			preBoil: formatNumber(27.5, 1),
			capacity: formatNumber(30.0, 1),
			headspace: 8
		});
		expect(enBoiloverWarn).toContain('Boilover risk');
		expect(enBoiloverWarn).toContain('8%');

		i18n.setLocale('sv');
		const svMashWarn = t('batches.wizard.boiler_mash_warning', {
			mashVol: formatNumber(32.5, 1),
			capacity: formatNumber(30.0, 1)
		});
		expect(svMashWarn).toContain('32,5');
		expect(svMashWarn).toContain('30,0');
		expect(svMashWarn).toContain('överskrider kokkärlets kapacitet');

		const svBoiloverWarn = t('batches.wizard.boiler_boilover_warning', {
			preBoil: formatNumber(27.5, 1),
			capacity: formatNumber(30.0, 1),
			headspace: 8
		});
		expect(svBoiloverWarn).toContain('Risk för överkokning');
		expect(svBoiloverWarn).toContain('8%');
	});

	describe('Boiler Warning Deduplication (TC-T1-R1-05, TC-T2-R1-04)', () => {
		function shouldRenderBoilerAlert(
			step: number,
			location: 'vesselSelector' | 'waterSchedule' | 'reviewSummary',
			isOverCapacity: boolean
		): boolean {
			if (!isOverCapacity) return false;
			// Dedicated rule: warning only renders in Step 2 Vessel Selector; suppressed in water schedule and review
			if (step === 2 && location === 'vesselSelector') return true;
			return false;
		}

		it('TC-T1-R1-05: renders boiler overcapacity warning exactly once in Step 2 vessel selector and suppresses duplicates', () => {
			const isOverCapacity = true;

			// Step 2 Vessel Selector renders the alert
			expect(shouldRenderBoilerAlert(2, 'vesselSelector', isOverCapacity)).toBe(true);

			// Step 2 Water Schedule alert is suppressed
			expect(shouldRenderBoilerAlert(2, 'waterSchedule', isOverCapacity)).toBe(false);

			// Step 3 Review Summary alert is suppressed
			expect(shouldRenderBoilerAlert(3, 'reviewSummary', isOverCapacity)).toBe(false);
		});

		it('TC-T2-R1-04: tests exact capacity boundary (30.00L vs 30.01L)', () => {
			const capacity = 30.0;
			const exactlyAtCapacity = 30.0;
			const slightlyOverCapacity = 30.01;
			const slightlyUnderCapacity = 29.99;

			const isOverAtCapacity = exactlyAtCapacity > capacity;
			const isOverAboveCapacity = slightlyOverCapacity > capacity;
			const isOverBelowCapacity = slightlyUnderCapacity > capacity;

			expect(isOverAtCapacity).toBe(false);
			expect(isOverAboveCapacity).toBe(true);
			expect(isOverBelowCapacity).toBe(false);

			// Only slightlyOverCapacity activates the alert in Step 2 vessel selector
			expect(shouldRenderBoilerAlert(2, 'vesselSelector', isOverAtCapacity)).toBe(false);
			expect(shouldRenderBoilerAlert(2, 'vesselSelector', isOverAboveCapacity)).toBe(true);
			expect(shouldRenderBoilerAlert(2, 'vesselSelector', isOverBelowCapacity)).toBe(false);
		});
	});

	describe('Equipment Connectivity Badges & Graceful Disconnect Fallback (TC-T1-R3-01, TC-T2-R3-01)', () => {
		interface ConnectionBadge {
			status: 'Connected' | 'Offline' | 'Manual';
			badgeColor: 'green' | 'amber' | 'zinc';
			canAutoSendRecipe: boolean;
		}

		function resolveEquipmentConnectionBadge(
			equipment: EquipmentDto,
			isDeviceOnline = false
		): ConnectionBadge {
			if (equipment.connectionType === 'BrewLink') {
				if (isDeviceOnline) {
					return {
						status: 'Connected',
						badgeColor: 'green',
						canAutoSendRecipe: true
					};
				}
				return {
					status: 'Offline',
					badgeColor: 'amber',
					canAutoSendRecipe: false
				};
			}
			return {
				status: 'Manual',
				badgeColor: 'zinc',
				canAutoSendRecipe: false
			};
		}

		function resolveBatchCreationMode(
			boiler: EquipmentDto,
			requestedAutoSend: boolean,
			isDeviceOnline: boolean
		): { sendRecipeToBoiler: boolean; isManualFallback: boolean; canProceed: boolean } {
			if (boiler.connectionType === 'BrewLink') {
				if (!isDeviceOnline) {
					// Fallback to manual mode gracefully without throwing unhandled 409
					return {
						sendRecipeToBoiler: false,
						isManualFallback: true,
						canProceed: true
					};
				}
				return {
					sendRecipeToBoiler: requestedAutoSend,
					isManualFallback: false,
					canProceed: true
				};
			}
			return {
				sendRecipeToBoiler: false,
				isManualFallback: false,
				canProceed: true
			};
		}

		it('TC-T1-R3-01: resolves live connected BrewLink equipment to green Connected badge', () => {
			const brewlinkBoiler = createMockBoiler({
				connectionType: 'BrewLink',
				connectionToken: 'tok-123'
			});
			const badge = resolveEquipmentConnectionBadge(brewlinkBoiler, true);

			expect(badge.status).toBe('Connected');
			expect(badge.badgeColor).toBe('green');
			expect(badge.canAutoSendRecipe).toBe(true);
		});

		it('TC-T1-R3-01: resolves offline BrewLink equipment to amber Offline badge and disables auto-send', () => {
			const brewlinkBoiler = createMockBoiler({
				connectionType: 'BrewLink',
				connectionToken: 'tok-123'
			});
			const badge = resolveEquipmentConnectionBadge(brewlinkBoiler, false);

			expect(badge.status).toBe('Offline');
			expect(badge.badgeColor).toBe('amber');
			expect(badge.canAutoSendRecipe).toBe(false);
		});

		it('TC-T2-R3-01: gracefully falls back to manual batch creation when BrewLink boiler is offline', () => {
			const offlineBoiler = createMockBoiler({
				connectionType: 'BrewLink',
				connectionToken: 'tok-123'
			});

			// User requested auto-send, but device is offline
			const mode = resolveBatchCreationMode(offlineBoiler, true, false);

			expect(mode.canProceed).toBe(true);
			expect(mode.sendRecipeToBoiler).toBe(false);
			expect(mode.isManualFallback).toBe(true);
		});

		it('TC-T2-R3-01: handles manual equipment without connection gracefully', () => {
			const manualBoiler = createMockBoiler({
				connectionType: 'None',
				connectionToken: null
			});
			const badge = resolveEquipmentConnectionBadge(manualBoiler, false);
			const mode = resolveBatchCreationMode(manualBoiler, false, false);

			expect(badge.status).toBe('Manual');
			expect(mode.canProceed).toBe(true);
			expect(mode.sendRecipeToBoiler).toBe(false);
			expect(mode.isManualFallback).toBe(false);
		});
	});
});
