import { expect, test } from '@playwright/test';
import { setupAuthenticatedSession } from '../fixtures/auth';

test.beforeEach(async ({ page }) => {
	await setupAuthenticatedSession(page);
});

test.describe('Carbonation Cheat Sheet & Calculator Sync', () => {
	test('filters finishing calculators, applies BJCP style target, and syncs force-carb matrix cells', async ({
		page
	}) => {
		await page.goto('/calculations');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		// Filter to Finishing & Packaging category
		await page.getByRole('button', { name: /Finishing & Packaging/i }).click();

		const calcCard = page.locator('#carbonation-priming');
		const cheatSheet = page.locator('#carbonation-cheat-sheet');
		await expect(calcCard).toBeVisible();
		await expect(cheatSheet).toBeVisible();

		// Search for Saison in BJCP styles and apply its 3.2 vol target
		const styleSearch = cheatSheet.getByPlaceholder(/Filter BJCP styles/i);
		await styleSearch.fill('Saison');
		await expect(cheatSheet).toContainText('25B Saison');

		await cheatSheet.getByRole('button', { name: /Use 3\.2/i }).click();
		await expect(calcCard.locator('#calc-target-co2')).toHaveValue('3.2');
		await expect(calcCard.getByRole('status')).toContainText(/3\.0–3\.2 vol/i);

		// Switch to Force Carb Matrix tab and click 6°C / 2.6 vol cell (14.9 PSI)
		await cheatSheet.getByRole('tab', { name: /Force Carb/i }).click();
		await expect(cheatSheet).toContainText('14.9');
		await cheatSheet.getByRole('button', { name: /14\.9/ }).click();

		await expect(calcCard.locator('#calc-target-co2')).toHaveValue('2.6');
		await expect(calcCard.locator('#calc-keg-temp')).toHaveValue('6');
		await expect(calcCard).toContainText('14.9 PSI');

		// Switch to Priming & Bottle Safety tab
		await cheatSheet.getByRole('tab', { name: /Priming & Bottle Safety/i }).click();
		await expect(cheatSheet).toContainText(/Peak Post-Fermentation Temp/i);
		await expect(cheatSheet).toContainText('1.47×');

		// Switch to Draft Lines & Troubleshooting tab and verify interactive line balancer
		await cheatSheet.getByRole('tab', { name: /Draft Lines/i }).click();
		await expect(cheatSheet.locator('#draft-keg-psi')).toBeVisible();
		await expect(cheatSheet).toContainText(/4 mm ID/i);

		// Verify zero orphaned slate- classes
		const slateElements = page.locator('[class*="bg-slate-"], [class*="border-slate-"]');
		await expect(slateElements).toHaveCount(0);
	});
});
