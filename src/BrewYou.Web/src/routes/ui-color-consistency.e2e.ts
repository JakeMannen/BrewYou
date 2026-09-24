import { expect, test } from '@playwright/test';
import { setupAuthenticatedSession } from './fixtures/auth';

test.beforeEach(async ({ page }) => {
	await setupAuthenticatedSession(page);
});

test.describe('UI Coloring Scheme Consistency across Brewery Views', () => {
	test('Recipes page follows the unified amber & zinc design tokens in both dark and light modes', async ({
		page
	}) => {
		await page.route('**/api/v1/recipes*', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify([
					{
						id: 'rec-1',
						name: 'Citra Horizon IPA',
						beerStyle: 'American IPA',
						description: 'A crisp, piney American IPA loaded with Citra and Centennial.',
						colorSrm: 6.5,
						alcoholByVolume: 6.2,
						bitternessIbu: 55,
						originalGravity: 1.058
					}
				])
			});
		});

		await page.goto('/recipes');
		await page.waitForLoadState('domcontentloaded');

		// Header icon container uses amber accent badge (consistent with Batches and Equipment)
		const headerIconBox = page.locator('h1').locator('..').locator('div.border-amber-500\\/30');
		await expect(headerIconBox).toBeVisible();
		await expect(headerIconBox).toHaveClass(/bg-amber-500\/10/);
		await expect(headerIconBox).toHaveClass(/text-amber-500/);

		// Header title uses zinc-900 with dark:text-white
		const pageTitle = page.locator('h1');
		await expect(pageTitle).toHaveClass(/text-zinc-900/);
		await expect(pageTitle).toHaveClass(/dark:text-white/);

		// "New Recipe" button in page header uses amber gradient and zinc-950 font-bold
		const newRecipeBtn = page.getByRole('main').locator('a[href="/recipes/new"]').first();
		await expect(newRecipeBtn).toBeVisible();
		await expect(newRecipeBtn).toHaveClass(/from-amber-500/);
		await expect(newRecipeBtn).toHaveClass(/to-amber-600/);
		await expect(newRecipeBtn).toHaveClass(/text-zinc-950/);

		// Recipe cards use glass-panel with border-zinc-200/80
		const cards = page.locator('article.glass-panel');
		await expect(cards).toHaveCount(1);

		const firstCard = cards.first();
		await expect(firstCard).toBeVisible();
		await expect(firstCard).toHaveClass(/border-zinc-200\/80/);
		await expect(firstCard).toHaveClass(/dark:border-white\/\[0\.08\]/);

		// First card should feature SRM swatch component matching Batches
		const srmSwatch = firstCard.locator('[role="img"][aria-label*="SRM"]');
		await expect(srmSwatch).toBeVisible();

		// Specs metrics matrix inside card uses themed zinc boxes
		const specBoxes = firstCard.locator('.rounded-xl.border-zinc-200\\/80');
		expect(await specBoxes.count()).toBeGreaterThanOrEqual(3);

		// Light Mode Verification: toggle theme and ensure no dark-slate hardcoded backgrounds
		await page.evaluate(() => {
			document.documentElement.classList.remove('dark');
			document.documentElement.setAttribute('data-theme', 'light');
		});

		const slateElements = page.locator('[class*="bg-slate-"], [class*="border-slate-"]');
		await expect(slateElements).toHaveCount(0);
	});

	test('Ingredients page follows the unified amber & zinc design tokens in both dark and light modes', async ({
		page
	}) => {
		await page.route('**/api/v1/ingredients*', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify([
					{
						id: 'ing-1',
						name: 'Pale Ale Malt',
						type: 'Fermentable',
						description: 'Base malt for English and American ales.',
						potentialGravity: 1.037,
						colorSrm: 3.5
					},
					{
						id: 'ing-2',
						name: 'Citra',
						type: 'Hop',
						description: 'Citrus, grapefruit, lime, tropical fruit aromas.',
						alphaAcidPercent: 12.5
					},
					{
						id: 'ing-3',
						name: 'Irish Moss',
						type: 'Other',
						description: 'Natural red seaweed kettle fining agent.'
					}
				])
			});
		});

		await page.goto('/ingredients');
		await page.locator('[data-hydrated="true"]').waitFor({ timeout: 10000 });

		// Header icon container uses amber accent badge
		const headerIconBox = page.locator('h1').locator('..').locator('div.border-amber-500\\/30');
		await expect(headerIconBox).toBeVisible();
		await expect(headerIconBox).toHaveClass(/bg-amber-500\/10/);
		await expect(headerIconBox).toHaveClass(/text-amber-500/);

		// Header title uses zinc-900 with dark:text-white
		const pageTitle = page.locator('h1');
		await expect(pageTitle).toHaveClass(/text-zinc-900/);
		await expect(pageTitle).toHaveClass(/dark:text-white/);

		// Toolbar uses glass-panel
		const toolbar = page.getByRole('main').locator('div.glass-panel').first();
		await expect(toolbar).toBeVisible();
		await expect(toolbar).toHaveClass(/border-zinc-200\/80/);

		// Filter tabs have accessible role and use zinc-900 / dark:bg-zinc-800 active state
		const tablist = page.getByRole('tablist', { name: /ingredient type filter/i });
		await expect(tablist).toBeVisible();

		// Verify all category filter tabs including Others/Övrigt are present
		const otherTab = tablist.locator('button', { hasText: /others|övrigt/i });
		await expect(otherTab).toBeVisible();

		const activeTab = tablist.locator('[aria-selected="true"]');
		await expect(activeTab).toBeVisible();
		await expect(activeTab).toHaveClass(/text-zinc-950/);

		// Ingredient cards use glass-panel with border-zinc-200/80
		const cards = page.locator('article.glass-panel');
		await expect(cards).toHaveCount(3);

		// Verify badges follow themed palette
		const fermentableBadge = page
			.locator('article.glass-panel')
			.filter({ has: page.locator('span', { hasText: /fermentable|malt/i }) })
			.locator('span', { hasText: /fermentable|malt/i })
			.first();
		await expect(fermentableBadge).toBeVisible();
		await expect(fermentableBadge).toHaveClass(/border-amber-500\/30/);
		await expect(fermentableBadge).toHaveClass(/bg-amber-500\/10/);

		const otherBadge = page
			.locator('article.glass-panel')
			.filter({ has: page.locator('span', { hasText: /other|övrigt/i }) })
			.locator('span', { hasText: /other|övrigt/i })
			.first();
		await expect(otherBadge).toBeVisible();
		await expect(otherBadge).toHaveClass(/border-purple-500\/30/);
		await expect(otherBadge).toHaveClass(/bg-purple-500\/10/);

		// Light Mode Verification: toggle theme and ensure no dark-slate hardcoded styles
		await page.evaluate(() => {
			document.documentElement.classList.remove('dark');
			document.documentElement.setAttribute('data-theme', 'light');
		});

		const slateElements = page.locator('[class*="bg-slate-"], [class*="border-slate-"]');
		await expect(slateElements).toHaveCount(0);
	});

	test('Recipe Formulator (/recipes/new) uses zinc + amber glass panels and inputs in both dark and light modes', async ({
		page
	}) => {
		await page.route('**/api/v1/ingredients*', async (route) => {
			await route.fulfill({
				status: 200,
				contentType: 'application/json',
				body: JSON.stringify([
					{
						id: 'ing-1',
						name: 'Pale Malt (2-Row)',
						type: 'Fermentable'
					},
					{
						id: 'ing-2',
						name: 'Munich Malt',
						type: 'Fermentable'
					},
					{
						id: 'ing-3',
						name: 'Citra',
						type: 'Hop'
					},
					{
						id: 'ing-4',
						name: 'SafAle US-05',
						type: 'Yeast'
					}
				])
			});
		});

		await page.goto('/recipes/new');
		await page.waitForLoadState('domcontentloaded');

		// Header icon container uses amber accent badge
		const headerIconBox = page.locator('h1').locator('..').locator('div.border-amber-500\\/30');
		await expect(headerIconBox).toBeVisible();
		await expect(headerIconBox).toHaveClass(/bg-amber-500\/10/);
		await expect(headerIconBox).toHaveClass(/text-amber-500/);

		// Form cards use glass-panel
		const formPanels = page.locator('.glass-panel');
		const panelCount = await formPanels.count();
		expect(panelCount).toBeGreaterThanOrEqual(2);

		// Verify zero orphaned slate- classes
		const slateElements = page.locator('[class*="bg-slate-"], [class*="border-slate-"]');
		await expect(slateElements).toHaveCount(0);
	});
});
