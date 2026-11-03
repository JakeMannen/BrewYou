import { describe, it, expect } from 'vitest';
import fs from 'fs';
import path from 'path';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';
import de from '$lib/i18n/locales/de.json';
import fr from '$lib/i18n/locales/fr.json';
import es from '$lib/i18n/locales/es.json';
import type { BatchSummaryDto, RecipeDetailDto } from '$lib/types/api';

describe('Bottle Label Print & Crop Feature Invariants', () => {
	it('ensures label crop editor template contains no ABV mentions or compliance notices', () => {
		const cropModalPath = path.resolve(__dirname, 'LabelCropEditorModal.svelte');
		const content = fs.readFileSync(cropModalPath, 'utf8');

		// The crop feature is strictly for framing and aspect ratios, not ABV
		expect(content).not.toContain('bjcp_accuracy_notice');
		expect(content).not.toContain('show_abv_pill');
		expect(content).not.toContain('alcoholByVolume');
		expect(content).not.toContain('ABV');
	});

	it('ensures all 5 locales have complete print label and crop adjustment keys with 100% parity', () => {
		const locales = [en, sv, de, fr, es];
		const requiredKeys = [
			'print_labels_btn',
			'print_btn_label',
			'print_batch_label_aria',
			'include_abv',
			'select_batch_source',
			'no_batch_theoretical',
			'theoretical_abv_notice',
			'actual_batch_abv_notice',
			'theoretical_indicator',
			'adjust_crops_btn',
			'settings_saved',
			'adjust_crops_title',
			'adjust_crops_desc'
		];

		for (const loc of locales) {
			for (const key of requiredKeys) {
				const val = (loc.labels as Record<string, string>)[key];
				expect(val).toBeDefined();
				expect(typeof val).toBe('string');
				expect(val.length).toBeGreaterThan(0);
			}
		}

		// Verify translation quality of key adjustments
		expect(en.labels.adjust_crops_btn).toBe('Adjust Crops');
		expect(sv.labels.adjust_crops_btn).toBe('Justera beskärning');
		expect(en.labels.include_abv).toBe('Include ABV on label');
		expect(sv.labels.include_abv).toBe('Inkludera alkoholhalt (ABV) på etikett');
	});

	it('computes theoretical ABV when no batch is selected for a recipe', () => {
		const mockRecipe: RecipeDetailDto = {
			id: 'recipe-001',
			name: 'Citra Hazy IPA',
			beerStyle: 'New England IPA',
			batchSizeLiters: 20,
			boilTimeMinutes: 60,
			efficiencyPercent: 75,
			originalGravity: 1.065,
			finalGravity: 1.012,
			alcoholByVolume: 6.96,
			bitternessIbu: 45,
			colorSrm: 5.5,
			isPublic: false,
			createdAt: '2026-11-01',
			updatedAt: '2026-11-01',
			ingredients: [],
			mashSteps: [],
			fermentationSteps: []
		};

		const batches: BatchSummaryDto[] = [
			{
				id: 'batch-001',
				batchCode: 'B-001',
				name: 'First Run',
				recipeId: 'recipe-001',
				beerStyle: 'New England IPA',
				status: 'Completed',
				currentStage: 'Package',
				brewDate: '2026-10-15',
				daysActive: 19,
				targetOg: 1.065,
				targetFg: 1.012,
				measuredOg: 1.068,
				measuredFg: 1.014,
				alcoholByVolume: 7.09,
				colorSrm: 5.5,
				targetBatchSizeLiters: 20,
				createdAt: '2026-10-15',
				updatedAt: '2026-10-15'
			}
		];

		// Case 1: No batch selected (empty string)
		let selectedBatchId = '';
		let selectedBatch = batches.find((b) => b.id === selectedBatchId) ?? null;
		let isTheoretical = !selectedBatch;
		let displayAbv = selectedBatch
			? (selectedBatch.alcoholByVolume ?? null)
			: mockRecipe.alcoholByVolume;

		expect(isTheoretical).toBe(true);
		expect(displayAbv).toBe(6.96);

		// Case 2: Batch selected
		selectedBatchId = 'batch-001';
		selectedBatch = batches.find((b) => b.id === selectedBatchId) ?? null;
		isTheoretical = !selectedBatch;
		displayAbv = selectedBatch
			? (selectedBatch.alcoholByVolume ?? null)
			: mockRecipe.alcoholByVolume;

		expect(isTheoretical).toBe(false);
		expect(displayAbv).toBe(7.09);
	});

	it('calculates measured ABV accurately from batch readings when recorded ABV is null', () => {
		function computeBatchAbv(b: {
			alcoholByVolume?: number | null;
			measuredOg?: number | null;
			measuredFg?: number | null;
			currentGravity?: number | null;
			targetOg: number;
			targetFg?: number;
			targetAbv?: number;
		}): number | null {
			if (b.alcoholByVolume != null) return b.alcoholByVolume;
			const og = b.measuredOg ?? (b.targetOg > 1.0 ? b.targetOg : null);
			const fg = b.measuredFg ?? b.currentGravity;
			if (og != null && fg != null && og > fg) {
				return Math.round((og - fg) * 131.25 * 10) / 10;
			}
			return (
				b.targetAbv ??
				(b.targetFg ? Math.round((b.targetOg - b.targetFg) * 131.25 * 10) / 10 : null)
			);
		}

		// Measured readings present
		const batchWithReadings = {
			alcoholByVolume: null,
			measuredOg: 1.052,
			measuredFg: 1.01,
			currentGravity: 1.01,
			targetOg: 1.05,
			targetAbv: 5.2
		};
		expect(computeBatchAbv(batchWithReadings)).toBe(5.5);

		// Target fallback
		const batchWithoutReadings = {
			alcoholByVolume: null,
			measuredOg: null,
			measuredFg: null,
			currentGravity: null,
			targetOg: 1.05,
			targetAbv: 5.2
		};
		expect(computeBatchAbv(batchWithoutReadings)).toBe(5.2);
	});

	it('verifies batch card in batches list contains print label button to the left of the QR button', () => {
		const batchesPagePath = path.resolve(__dirname, '../../../routes/batches/+page.svelte');
		const content = fs.readFileSync(batchesPagePath, 'utf8');

		const printBtnIndex = content.indexOf('data-testid="batch-card-print-label-btn"');
		const qrBtnIndex = content.indexOf('data-testid="open-batch-qr-btn"');

		expect(printBtnIndex).toBeGreaterThan(-1);
		expect(qrBtnIndex).toBeGreaterThan(-1);
		// Print button must appear BEFORE (to the left of) the QR button
		expect(printBtnIndex).toBeLessThan(qrBtnIndex);

		expect(content).toContain('href="/batches/{batch.id}/labels/print"');
	});

	it('verifies batch detail page header contains print label button to the left of the QR button', () => {
		const batchDetailPagePath = path.resolve(
			__dirname,
			'../../../routes/batches/[id]/+page.svelte'
		);
		const content = fs.readFileSync(batchDetailPagePath, 'utf8');

		const printBtnIndex = content.indexOf('href="/batches/{batch.id}/labels/print"');
		const qrBtnIndex = content.indexOf('data-testid="batch-detail-qr-btn"');

		expect(printBtnIndex).toBeGreaterThan(-1);
		expect(qrBtnIndex).toBeGreaterThan(-1);
		// Print button must appear BEFORE (to the left of) the QR button
		expect(printBtnIndex).toBeLessThan(qrBtnIndex);
	});

	it('verifies that the batch information and ABV strip on printed labels use white background with high-contrast text', () => {
		const batchPrintPath = path.resolve(
			__dirname,
			'../../../routes/batches/[id]/labels/print/+page.svelte'
		);
		const recipePrintPath = path.resolve(
			__dirname,
			'../../../routes/recipes/[id]/labels/print/+page.svelte'
		);

		const batchContent = fs.readFileSync(batchPrintPath, 'utf8');
		const recipeContent = fs.readFileSync(recipePrintPath, 'utf8');

		// Both must use white background for the label bottom strip
		expect(batchContent).toContain('bg-white px-2 py-1 text-zinc-900');
		expect(recipeContent).toContain(
			'bg-white px-2 py-1 text-center font-mono text-[9px] font-bold text-zinc-900'
		);

		// High-contrast text colors
		expect(batchContent).toContain('text-amber-800');
		expect(batchContent).toContain('text-emerald-800');
		expect(batchContent).toContain('text-zinc-600');

		// No dark background in the label bottom strip
		expect(batchContent).not.toContain('bg-zinc-950 px-2 py-1 text-white');
		expect(recipeContent).not.toContain(
			'bg-zinc-950 px-2 py-1 text-center font-mono text-[9px] font-bold text-white'
		);
	});

	it('drops the ABV unit ending from labels so 5.2% ABV is displayed as 5.2%', () => {
		const batchPrintPath = path.resolve(
			__dirname,
			'../../../routes/batches/[id]/labels/print/+page.svelte'
		);
		const recipePrintPath = path.resolve(
			__dirname,
			'../../../routes/recipes/[id]/labels/print/+page.svelte'
		);

		const batchContent = fs.readFileSync(batchPrintPath, 'utf8');
		const recipeContent = fs.readFileSync(recipePrintPath, 'utf8');

		// Both must render {displayAbv}% without the trailing ABV unit
		expect(batchContent).toContain('{displayAbv}%</span>');
		expect(recipeContent).toContain('<span>{displayAbv}%</span>');
		expect(batchContent).not.toContain('{displayAbv}% ABV');
		expect(recipeContent).not.toContain('{displayAbv}% ABV');
	});
});
