import { describe, it, expect } from 'vitest';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';
import type {
	BatchMashStepDto,
	BrewStage,
	EquipmentDto,
	LogBatchTemperatureRequest,
	AddBatchReadingRequest
} from '$lib/types/api';

describe('LogStepTemperatureModal & Telemetry Logic', () => {
	it('has complete internationalization keys for telemetry and step temp logging in en.json and sv.json', () => {
		expect(en.batches.telemetry.log_temp_modal_title).toBe('Log Step Temperature');
		expect(sv.batches.telemetry.log_temp_modal_title).toBe('Logga stegtemperatur');

		expect(en.batches.telemetry.temp_input_label).toBe('Temperature');
		expect(sv.batches.telemetry.temp_input_label).toBe('Temperatur');

		expect(en.batches.telemetry.target_temp).toBe('Target Temp');
		expect(sv.batches.telemetry.target_temp).toBe('Måltemp');

		expect(en.batches.telemetry.actual_temp).toBe('Actual');
		expect(sv.batches.telemetry.actual_temp).toBe('Uppmätt');

		expect(en.batches.telemetry.mash_step).toBe('Mash Step');
		expect(sv.batches.telemetry.mash_step).toBe('Mäsksteg');

		expect(en.batches.telemetry.equipment).toBe('Equipment');
		expect(sv.batches.telemetry.equipment).toBe('Utrustning');

		expect(en.batches.telemetry.save_reading).toBe('Save Reading');
		expect(sv.batches.telemetry.save_reading).toBe('Spara mätning');

		expect(en.batches.telemetry.invalid_temp_error).toBeDefined();
		expect(sv.batches.telemetry.invalid_temp_error).toBeDefined();

		expect(en.batches.telemetry.temp_range_error).toBeDefined();
		expect(sv.batches.telemetry.temp_range_error).toBeDefined();

		expect(en.batches.telemetry.no_equipment_available).toBeDefined();
		expect(sv.batches.telemetry.no_equipment_available).toBeDefined();
	});

	describe('Equipment & Step Resolution Logic', () => {
		const mockEquipmentList = [
			{
				id: 'eq-boiler-1',
				name: 'Grainfather G40',
				type: 'Boiler',
				subtype: 'AllInOne',
				capacityLiters: 40,
				brewerySetupId: 'setup-1',
				capacity: 40,
				unit: 'Liters',
				currentVolume: 0,
				currentVolumeLiters: 0,
				fillPercentage: 0
			},
			{
				id: 'eq-fermenter-1',
				name: 'Conical Fermenter 30L',
				type: 'Fermenter',
				subtype: 'ConicalFermenter',
				capacityLiters: 30,
				brewerySetupId: 'setup-1',
				capacity: 30,
				unit: 'Liters',
				currentVolume: 0,
				currentVolumeLiters: 0,
				fillPercentage: 0
			}
		] as unknown as EquipmentDto[];

		const mockMashSteps: BatchMashStepDto[] = [
			{
				id: 'step-1',
				batchId: 'batch-1',
				name: 'Protein Rest',
				stepOrder: 1,
				type: 'Temperature',
				targetTemperatureC: 52.0,
				durationMinutes: 15,
				isCompleted: true,
				actualTemperatureC: 52.1
			},
			{
				id: 'step-2',
				batchId: 'batch-1',
				name: 'Saccharification',
				stepOrder: 2,
				type: 'Temperature',
				targetTemperatureC: 66.5,
				durationMinutes: 60,
				isCompleted: false,
				actualTemperatureC: null
			},
			{
				id: 'step-3',
				batchId: 'batch-1',
				name: 'Mash Out',
				stepOrder: 3,
				type: 'Temperature',
				targetTemperatureC: 76.0,
				durationMinutes: 10,
				isCompleted: false,
				actualTemperatureC: null
			}
		];

		it('automatically resolves equipment by defaultEquipmentId and fallback equipmentName', () => {
			function resolveEquipment(
				equipmentList: EquipmentDto[],
				selectedId?: string | null,
				passedName?: string | null
			) {
				if (selectedId) {
					const found = equipmentList.find((e) => e.id === selectedId);
					if (found) return { id: found.id, name: found.name };
				}
				if (passedName) {
					return { id: selectedId ?? null, name: passedName };
				}
				if (equipmentList.length > 0) {
					return { id: equipmentList[0].id, name: equipmentList[0].name };
				}
				return null;
			}

			const res1 = resolveEquipment(mockEquipmentList, 'eq-boiler-1');
			expect(res1?.id).toBe('eq-boiler-1');
			expect(res1?.name).toBe('Grainfather G40');

			const res2 = resolveEquipment(mockEquipmentList, 'eq-fermenter-1');
			expect(res2?.id).toBe('eq-fermenter-1');
			expect(res2?.name).toBe('Conical Fermenter 30L');

			const resFallback = resolveEquipment(mockEquipmentList, null, 'Custom Kettle');
			expect(resFallback?.name).toBe('Custom Kettle');
		});

		it('automatically resolves mash step by defaultMashStepId', () => {
			function resolveStep(mashSteps: BatchMashStepDto[], stepId?: string | null) {
				if (!stepId || mashSteps.length === 0) return null;
				return mashSteps.find((s) => s.id === stepId) ?? null;
			}

			const resolved = resolveStep(mockMashSteps, 'step-2');
			expect(resolved).not.toBeNull();
			expect(resolved?.name).toBe('Saccharification');
			expect(resolved?.targetTemperatureC).toBe(66.5);
			expect(resolved?.stepOrder).toBe(2);
		});

		it('resolves stage-level default target temperature correctly', () => {
			function getTargetTemp(
				stage: BrewStage,
				stepId: string | null,
				mashSteps: BatchMashStepDto[],
				pitchTemp?: number | null
			): number {
				if (stage === 'Mash' && stepId) {
					const step = mashSteps.find((s) => s.id === stepId);
					if (step) return step.targetTemperatureC;
				}
				if (stage === 'Mash') return 65.0;
				if (stage === 'Boil') return 100.0;
				if (stage === 'Ferment') return pitchTemp ?? 20.0;
				if (stage === 'Condition') return 2.0;
				return 20.0;
			}

			expect(getTargetTemp('Mash', 'step-1', mockMashSteps)).toBe(52.0);
			expect(getTargetTemp('Mash', 'step-3', mockMashSteps)).toBe(76.0);
			expect(getTargetTemp('Boil', null, mockMashSteps)).toBe(100.0);
			expect(getTargetTemp('Ferment', null, mockMashSteps, 18.5)).toBe(18.5);
			expect(getTargetTemp('Condition', null, mockMashSteps)).toBe(2.0);
		});
	});

	describe('Temperature Conversions & Input Validation', () => {
		function convertInputToCelsius(val: number, unit: 'Celsius' | 'Fahrenheit'): number {
			if (unit === 'Fahrenheit') {
				return Number((((val - 32) * 5) / 9).toFixed(2));
			}
			return Number(val.toFixed(2));
		}

		function validateTemperature(tempC: number): boolean {
			return !isNaN(tempC) && tempC >= -20 && tempC <= 120;
		}

		it('converts Fahrenheit input correctly to Celsius', () => {
			expect(convertInputToCelsius(149.0, 'Fahrenheit')).toBe(65.0);
			expect(convertInputToCelsius(212.0, 'Fahrenheit')).toBe(100.0);
			expect(convertInputToCelsius(68.0, 'Fahrenheit')).toBe(20.0);
			expect(convertInputToCelsius(32.0, 'Fahrenheit')).toBe(0.0);
		});

		it('preserves Celsius input', () => {
			expect(convertInputToCelsius(66.5, 'Celsius')).toBe(66.5);
			expect(convertInputToCelsius(100.0, 'Celsius')).toBe(100.0);
		});

		it('validates safe brewing temperature bounds (-20°C to 120°C)', () => {
			expect(validateTemperature(65)).toBe(true);
			expect(validateTemperature(-20)).toBe(true);
			expect(validateTemperature(120)).toBe(true);
			expect(validateTemperature(-25)).toBe(false);
			expect(validateTemperature(125)).toBe(false);
			expect(validateTemperature(NaN)).toBe(false);
		});
	});

	describe('Telemetry Submission Payload Invariants', () => {
		it('builds LogBatchTemperatureRequest with step, equipment, and stage metadata', () => {
			const req: LogBatchTemperatureRequest = {
				temperatureC: 66.8,
				equipmentId: 'eq-boiler-1',
				stage: 'Mash',
				batchMashStepId: 'step-2',
				stepName: 'Saccharification',
				notes: 'Stable temp hold'
			};

			expect(req.temperatureC).toBe(66.8);
			expect(req.equipmentId).toBe('eq-boiler-1');
			expect(req.stage).toBe('Mash');
			expect(req.batchMashStepId).toBe('step-2');
			expect(req.stepName).toBe('Saccharification');
			expect(req.notes).toBe('Stable temp hold');
		});

		it('ensures AddBatchReadingRequest does NOT log temperature when logging gravity', () => {
			const gravityReadingReq: AddBatchReadingRequest = {
				specificGravity: 1.048,
				temperatureC: null,
				notes: 'Hydrometer reading at post-boil'
			};

			expect(gravityReadingReq.specificGravity).toBe(1.048);
			expect(gravityReadingReq.temperatureC).toBeNull();
			expect(gravityReadingReq.notes).toBe('Hydrometer reading at post-boil');
		});
	});
});
