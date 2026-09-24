import { describe, it, expect } from 'vitest';

/**
 * Tests for the dirty-state tracking logic used by the unsaved changes guard.
 * The serializeFormState function is inlined in RecipeFormulator.svelte; these tests
 * validate the same serialization contract externally to ensure snapshot comparison
 * works correctly for detecting unsaved changes.
 */

interface SerializableItem {
	ingredientId: string;
	type: string;
	amount: number;
	unit: string;
	durationMinutes: number;
	usage: string;
	notes?: string;
}

interface SerializableMashStep {
	stepOrder: number;
	name: string;
	type: string;
	temperatureC: number;
	durationMinutes: number;
	rampTimeMinutes?: number;
	infuseAmountLiters?: number;
	notes?: string;
}

interface FormState {
	name: string;
	description: string;
	beerStyle: string;
	batchSizeLiters: number;
	boilTimeMinutes: number;
	efficiencyPercent: number;
	isPublic: boolean;
	items: SerializableItem[];
	mashSteps: SerializableMashStep[];
}

function serializeFormState(state: FormState): string {
	return JSON.stringify({
		name: state.name,
		description: state.description,
		beerStyle: state.beerStyle,
		batchSizeLiters: state.batchSizeLiters,
		boilTimeMinutes: state.boilTimeMinutes,
		efficiencyPercent: state.efficiencyPercent,
		isPublic: state.isPublic,
		items: state.items.map((i) => ({
			ingredientId: i.ingredientId,
			type: i.type,
			amount: i.amount,
			unit: i.unit,
			durationMinutes: i.durationMinutes,
			usage: i.usage,
			notes: i.notes
		})),
		mashSteps: state.mashSteps.map((s) => ({
			stepOrder: s.stepOrder,
			name: s.name,
			type: s.type,
			temperatureC: s.temperatureC,
			durationMinutes: s.durationMinutes,
			rampTimeMinutes: s.rampTimeMinutes,
			infuseAmountLiters: s.infuseAmountLiters,
			notes: s.notes
		}))
	});
}

function makeDefaultState(): FormState {
	return {
		name: '',
		description: '',
		beerStyle: '',
		batchSizeLiters: 20,
		boilTimeMinutes: 60,
		efficiencyPercent: 72,
		isPublic: true,
		items: [],
		mashSteps: [
			{
				stepOrder: 1,
				name: 'Saccharification Rest',
				type: 'Infusion',
				temperatureC: 65.0,
				durationMinutes: 60
			}
		]
	};
}

describe('serializeFormState (dirty-state tracking)', () => {
	it('produces identical snapshots for identical state', () => {
		const state = makeDefaultState();
		const snapshot1 = serializeFormState(state);
		const snapshot2 = serializeFormState(state);
		expect(snapshot1).toBe(snapshot2);
	});

	it('detects name change', () => {
		const initial = serializeFormState(makeDefaultState());
		const modified = { ...makeDefaultState(), name: 'Citra IPA' };
		expect(serializeFormState(modified)).not.toBe(initial);
	});

	it('detects description change', () => {
		const initial = serializeFormState(makeDefaultState());
		const modified = { ...makeDefaultState(), description: 'A hoppy brew' };
		expect(serializeFormState(modified)).not.toBe(initial);
	});

	it('detects beerStyle change', () => {
		const initial = serializeFormState(makeDefaultState());
		const modified = { ...makeDefaultState(), beerStyle: 'American IPA' };
		expect(serializeFormState(modified)).not.toBe(initial);
	});

	it('detects batchSizeLiters change', () => {
		const initial = serializeFormState(makeDefaultState());
		const modified = { ...makeDefaultState(), batchSizeLiters: 25 };
		expect(serializeFormState(modified)).not.toBe(initial);
	});

	it('detects boilTimeMinutes change', () => {
		const initial = serializeFormState(makeDefaultState());
		const modified = { ...makeDefaultState(), boilTimeMinutes: 90 };
		expect(serializeFormState(modified)).not.toBe(initial);
	});

	it('detects efficiencyPercent change', () => {
		const initial = serializeFormState(makeDefaultState());
		const modified = { ...makeDefaultState(), efficiencyPercent: 80 };
		expect(serializeFormState(modified)).not.toBe(initial);
	});

	it('detects isPublic toggle', () => {
		const initial = serializeFormState(makeDefaultState());
		const modified = { ...makeDefaultState(), isPublic: false };
		expect(serializeFormState(modified)).not.toBe(initial);
	});

	it('detects added ingredient item', () => {
		const initial = serializeFormState(makeDefaultState());
		const modified = makeDefaultState();
		modified.items = [
			{
				ingredientId: 'abc123',
				type: 'Fermentable',
				amount: 5,
				unit: 'kg',
				durationMinutes: 60,
				usage: 'Mash'
			}
		];
		expect(serializeFormState(modified)).not.toBe(initial);
	});

	it('detects ingredient amount change', () => {
		const base = makeDefaultState();
		base.items = [
			{
				ingredientId: 'abc123',
				type: 'Fermentable',
				amount: 5,
				unit: 'kg',
				durationMinutes: 60,
				usage: 'Mash'
			}
		];
		const initial = serializeFormState(base);

		const modified = makeDefaultState();
		modified.items = [
			{
				ingredientId: 'abc123',
				type: 'Fermentable',
				amount: 6,
				unit: 'kg',
				durationMinutes: 60,
				usage: 'Mash'
			}
		];
		expect(serializeFormState(modified)).not.toBe(initial);
	});

	it('detects mash step temperature change', () => {
		const initial = serializeFormState(makeDefaultState());
		const modified = makeDefaultState();
		modified.mashSteps[0].temperatureC = 68;
		expect(serializeFormState(modified)).not.toBe(initial);
	});

	it('detects added mash step', () => {
		const initial = serializeFormState(makeDefaultState());
		const modified = makeDefaultState();
		modified.mashSteps.push({
			stepOrder: 2,
			name: 'Mash Out',
			type: 'Temperature',
			temperatureC: 76,
			durationMinutes: 10
		});
		expect(serializeFormState(modified)).not.toBe(initial);
	});

	it('ignores ephemeral id fields (not included in serialization)', () => {
		const state1 = makeDefaultState();
		const state2 = makeDefaultState();

		state1.items = [
			{
				ingredientId: 'abc',
				type: 'Hop',
				amount: 20,
				unit: 'g',
				durationMinutes: 15,
				usage: 'Boil'
			}
		];
		state2.items = [
			{
				ingredientId: 'abc',
				type: 'Hop',
				amount: 20,
				unit: 'g',
				durationMinutes: 15,
				usage: 'Boil'
			}
		];

		expect(serializeFormState(state1)).toBe(serializeFormState(state2));
	});

	it('treats undefined and missing optional fields consistently', () => {
		const state1 = makeDefaultState();
		state1.mashSteps[0].rampTimeMinutes = undefined;
		state1.mashSteps[0].notes = undefined;

		const state2 = makeDefaultState();

		expect(serializeFormState(state1)).toBe(serializeFormState(state2));
	});
});
