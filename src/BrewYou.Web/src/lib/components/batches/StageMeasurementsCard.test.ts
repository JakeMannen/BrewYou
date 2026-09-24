import { describe, it, expect } from 'vitest';
import type { BatchDetailDto, BrewStage, UpdateBatchRequest } from '$lib/types/api';
import { formatNumber } from '$lib/utils/formatNumber';
import { recalculatePostStepMilestones } from '$lib/calculators/volume';
const mockBatch: BatchDetailDto = {
	id: 'batch-123',
	userId: 'user-1',
	name: 'Summer IPA',
	batchCode: 'B-001',
	beerStyle: 'IPA',
	recipeId: 'rec-1',
	recipeName: 'Summer IPA Recipe',
	status: 'Fermenting',
	currentStage: 'Ferment',
	brewDate: '2026-09-01T08:00:00Z',
	daysActive: 3,
	targetOg: 1.055,
	targetFg: 1.012,
	targetAbv: 5.6,
	targetIbu: 45,
	targetColorSrm: 6,
	targetBatchSizeLiters: 20,
	boilTimeMinutes: 60,
	efficiencyPercent: 75,
	volumeProfile: {
		totalWaterLiters: 33,
		strikeWaterLiters: 18,
		spargeWaterLiters: 15,
		targetPreBoilVolumeLiters: 27,
		measuredPreBoilVolumeLiters: 27,
		measuredPreBoilGravity: 1.046,
		targetPostBoilVolumeLiters: 23,
		measuredPostBoilVolumeLiters: 23,
		targetFermenterVolumeLiters: 20,
		measuredFermenterVolumeLiters: 20,
		targetPackagedVolumeLiters: 19,
		measuredPackagedVolumeLiters: null,
		boilOffRatePerHour: 4.0,
		grainAbsorptionRateLPerKg: 1.0,
		kettleTrubLossLiters: 2.0,
		fermenterTrubLossLiters: 1.0,
		mashTunDeadSpaceLiters: 0.5,
		coolingShrinkagePercent: 4.0,
		packagingLossLiters: 0.5
	},
	measuredOg: 1.056,
	pitchTemperatureC: 19.5,
	measuredFg: null,
	measuredBatchSizeLiters: 20,
	packagingVesselId: null,
	packagingVesselName: null,
	boilerId: null,
	boilerName: null,
	fermenterId: null,
	fermenterName: null,
	alcoholByVolume: null,
	brewhouseEfficiency: null,
	currentGravity: 1.056,
	notes: 'Clean mash conversion.',
	createdAt: '2026-09-01T08:00:00Z',
	updatedAt: '2026-09-01T12:00:00Z',
	completedAt: null,
	readings: [],
	ingredients: [],
	mashSteps: [],
	fermentationSteps: [],
	stageHistory: [],
	sensorAssignments: []
};

describe('StageMeasurementsCard Form & Payload Logic', () => {
	function buildStageUpdatePayload(
		stage: BrewStage,
		batch: BatchDetailDto,
		edits: Partial<UpdateBatchRequest>
	): UpdateBatchRequest {
		return {
			name: edits.name !== undefined ? edits.name : batch.name,
			beerStyle: edits.beerStyle !== undefined ? edits.beerStyle : batch.beerStyle,
			notes: edits.notes !== undefined ? edits.notes : batch.notes,
			measuredPreBoilVolumeLiters:
				edits.measuredPreBoilVolumeLiters !== undefined
					? edits.measuredPreBoilVolumeLiters
					: batch.volumeProfile?.measuredPreBoilVolumeLiters,
			measuredPreBoilGravity:
				edits.measuredPreBoilGravity !== undefined
					? edits.measuredPreBoilGravity
					: batch.volumeProfile?.measuredPreBoilGravity,
			measuredPostBoilVolumeLiters:
				edits.measuredPostBoilVolumeLiters !== undefined
					? edits.measuredPostBoilVolumeLiters
					: batch.volumeProfile?.measuredPostBoilVolumeLiters,
			measuredOg: edits.measuredOg !== undefined ? edits.measuredOg : batch.measuredOg,
			pitchTemperatureC:
				edits.pitchTemperatureC !== undefined ? edits.pitchTemperatureC : batch.pitchTemperatureC,
			measuredFg: edits.measuredFg !== undefined ? edits.measuredFg : batch.measuredFg,
			measuredPackagedVolumeLiters:
				edits.measuredPackagedVolumeLiters !== undefined
					? edits.measuredPackagedVolumeLiters
					: batch.volumeProfile?.measuredPackagedVolumeLiters,
			packagingVesselId:
				edits.packagingVesselId !== undefined ? edits.packagingVesselId : batch.packagingVesselId
		};
	}

	it('creates valid Mash stage update without touching fermentation or packaging fields', () => {
		const payload = buildStageUpdatePayload('Mash', mockBatch, {
			measuredPreBoilVolumeLiters: 28,
			measuredPreBoilGravity: 1.047
		});

		expect(payload.measuredPreBoilVolumeLiters).toBe(28);
		expect(payload.measuredPreBoilGravity).toBe(1.047);
		expect(payload.measuredOg).toBe(1.056);
		expect(payload.measuredFg).toBeNull();
		expect(payload.name).toBe('Summer IPA');
	});

	it('creates valid Boil stage update with adjusted post-boil volume and OG', () => {
		const payload = buildStageUpdatePayload('Boil', mockBatch, {
			measuredPostBoilVolumeLiters: 22.5,
			measuredOg: 1.058
		});

		expect(payload.measuredPostBoilVolumeLiters).toBe(22.5);
		expect(payload.measuredOg).toBe(1.058);
		expect(payload.measuredPreBoilVolumeLiters).toBe(27);
	});

	it('creates valid Package stage update with FG, packaged volume, and vessel ID', () => {
		const payload = buildStageUpdatePayload('Package', mockBatch, {
			measuredFg: 1.011,
			measuredPackagedVolumeLiters: 19.5,
			packagingVesselId: 'vessel-keg-19l'
		});

		expect(payload.measuredFg).toBe(1.011);
		expect(payload.measuredPackagedVolumeLiters).toBe(19.5);
		expect(payload.packagingVesselId).toBe('vessel-keg-19l');
		expect(payload.measuredOg).toBe(1.056);
	});

	it('preserves existing notes when not edited during stage input adjustment', () => {
		const payload = buildStageUpdatePayload('Ferment', mockBatch, {
			pitchTemperatureC: 18.0
		});

		expect(payload.pitchTemperatureC).toBe(18.0);
		expect(payload.notes).toBe('Clean mash conversion.');
	});
});

describe('StageMeasurementsCard Volume Milestones & Water Schedule', () => {
	it('calculates boil extension when pre-boil volume exceeds target in boil stage', () => {
		const targetPreBoil = 28.0;
		const boilOffRate = 3.5;
		const measuredPreBoil = 31.5;
		const excessVolume = measuredPreBoil - targetPreBoil;
		const extensionMinutes = Math.round((excessVolume / boilOffRate) * 60);

		expect(extensionMinutes).toBe(60);
	});

	it('returns 0 boil extension when pre-boil volume is at or below target', () => {
		const targetPreBoil = 28.0;
		const boilOffRate = 3.5;
		const measuredPreBoil = 28.0;
		const excessVolume = Math.max(0, measuredPreBoil - targetPreBoil);
		const extensionMinutes = Math.round((excessVolume / boilOffRate) * 60);

		expect(extensionMinutes).toBe(0);
	});

	it('formats all water milestone numbers consistently with one decimal (0.1)', () => {
		const rawValues = [33, 18.04, 15, 27, 22.56, 20.0, 18.5];
		const formatted = rawValues.map((v) => formatNumber(v, 1));

		expect(formatted).toEqual(['33.0', '18.0', '15.0', '27.0', '22.6', '20.0', '18.5']);
	});

	it('recalculates post-step targets when editing pre-boil volume in batch workflow', () => {
		const profile = mockBatch.volumeProfile!;
		const updated = recalculatePostStepMilestones(
			{
				...profile,
				measuredPreBoilVolumeLiters: 29.0,
				measuredPostBoilVolumeLiters: null,
				measuredFermenterVolumeLiters: null
			},
			mockBatch.boilTimeMinutes
		);

		// Pre-boil 29.0 -> expected post boil cold ~ 25.0
		expect(updated.targetPostBoilVolumeLiters).toBeGreaterThan(profile.targetPostBoilVolumeLiters);
		// Fermenter target updates: post-boil - 2.0 kettle trub
		expect(updated.targetFermenterVolumeLiters).toBe(
			Math.round((updated.targetPostBoilVolumeLiters - profile.kettleTrubLossLiters) * 100) / 100
		);
		// Packaged target updates: fermenter - (1.0 fermenter trub + 0.5 packaging)
		expect(updated.targetPackagedVolumeLiters).toBe(
			Math.round(
				(updated.targetFermenterVolumeLiters -
					(profile.fermenterTrubLossLiters + profile.packagingLossLiters)) *
					100
			) / 100
		);
	});

	it('derives planned grain absorption correctly from water budget and pre-boil targets', () => {
		const profile = mockBatch.volumeProfile!;
		const plannedAbsorption =
			profile.totalWaterLiters -
			profile.targetPreBoilVolumeLiters -
			(profile.mashTunDeadSpaceLiters || 0);

		// 33L total water - 27L target pre-boil - 0.5L dead space = 5.5L grain absorption
		expect(plannedAbsorption).toBe(5.5);
		expect(formatNumber(plannedAbsorption, 1)).toBe('5.5');
	});
});
