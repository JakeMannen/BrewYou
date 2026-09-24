import { beforeEach, describe, expect, it, vi } from 'vitest';
import { brewery, DEFAULT_BREWERY_ID, DEFAULT_BREWERY_NAME } from './brewery.svelte';

class LocalStorageMock {
	private store: Record<string, string> = {};

	clear() {
		this.store = {};
	}

	getItem(key: string): string | null {
		return this.store[key] ?? null;
	}

	setItem(key: string, value: string) {
		this.store[key] = String(value);
	}

	removeItem(key: string) {
		delete this.store[key];
	}
}

const mockStorage = new LocalStorageMock();

describe('Brewery State Store (brewery.svelte.ts)', () => {
	beforeEach(async () => {
		vi.restoreAllMocks();
		mockStorage.clear();
		vi.stubGlobal('localStorage', mockStorage);

		// Reset brewery store to clean baseline
		await brewery.init();
	});

	it('initializes with default "Brewery 1" setup when storage is empty', () => {
		expect(brewery.setups.length).toBe(1);
		expect(brewery.setups[0].id).toBe(DEFAULT_BREWERY_ID);
		expect(brewery.setups[0].name).toBe(DEFAULT_BREWERY_NAME);
		expect(brewery.activeSetupId).toBe(DEFAULT_BREWERY_ID);
		expect(brewery.activeSetup.name).toBe(DEFAULT_BREWERY_NAME);
		expect(brewery.canDelete).toBe(false);
	});

	it('adds another setup with auto-incremented name', async () => {
		const setup2 = await brewery.addSetup();
		expect(setup2.name).toBe('Brewery 2');
		expect(brewery.setups.length).toBe(2);
		expect(brewery.activeSetupId).toBe(setup2.id);
		expect(brewery.activeSetup.name).toBe('Brewery 2');
		expect(brewery.canDelete).toBe(true);

		const setup3 = await brewery.addSetup();
		expect(setup3.name).toBe('Brewery 3');
		expect(brewery.setups.length).toBe(3);
		expect(brewery.activeSetupId).toBe(setup3.id);
	});

	it('adds a setup with custom trimmed name and sanitizes dangerous characters', async () => {
		const custom = await brewery.addSetup('  50L Pilot Rig <script>  ');
		expect(custom.name).toBe('50L Pilot Rig script');
		expect(brewery.activeSetup.name).toBe('50L Pilot Rig script');
	});

	it('switches between setups smoothly', async () => {
		const newSetup = await brewery.addSetup('Nano Brewhouse');
		expect(brewery.activeSetupId).toBe(newSetup.id);

		brewery.selectSetup(DEFAULT_BREWERY_ID);
		expect(brewery.activeSetupId).toBe(DEFAULT_BREWERY_ID);
		expect(brewery.activeSetup.name).toBe(DEFAULT_BREWERY_NAME);

		brewery.selectSetup(newSetup.id);
		expect(brewery.activeSetupId).toBe(newSetup.id);
		expect(brewery.activeSetup.name).toBe('Nano Brewhouse');

		// Selecting an invalid ID should be a safe no-op
		brewery.selectSetup('non-existent-id');
		expect(brewery.activeSetupId).toBe(newSetup.id);
	});

	it('renames an existing setup and updates activeSetup reactively', async () => {
		const ok = await brewery.renameSetup(DEFAULT_BREWERY_ID, 'Main Brewhouse');
		expect(ok).toBe(true);
		expect(brewery.activeSetup.name).toBe('Main Brewhouse');

		// Rejects empty or whitespace-only name
		expect(await brewery.renameSetup(DEFAULT_BREWERY_ID, '   ')).toBe(false);
		expect(brewery.activeSetup.name).toBe('Main Brewhouse');

		// Rejects names containing angle brackets
		expect(await brewery.renameSetup(DEFAULT_BREWERY_ID, '<Defaced>')).toBe(false);
		expect(brewery.activeSetup.name).toBe('Main Brewhouse');

		// Rejects renaming a non-existent ID
		expect(await brewery.renameSetup('missing-id', 'Test')).toBe(false);
	});

	it('enforces Sole Brewery Protection Invariant (cannot delete last brewery)', async () => {
		expect(brewery.setups.length).toBe(1);
		expect(brewery.canDelete).toBe(false);

		const result = await brewery.deleteSetup(DEFAULT_BREWERY_ID);
		expect(result).toBe(false);
		expect(brewery.setups.length).toBe(1);
		expect(brewery.activeSetupId).toBe(DEFAULT_BREWERY_ID);
	});

	it('deletes non-active setup safely and preserves active setup', async () => {
		const setup2 = await brewery.addSetup('Second Rig');
		brewery.selectSetup(DEFAULT_BREWERY_ID);

		expect(brewery.setups.length).toBe(2);
		expect(brewery.activeSetupId).toBe(DEFAULT_BREWERY_ID);

		const deleted = await brewery.deleteSetup(setup2.id);
		expect(deleted).toBe(true);
		expect(brewery.setups.length).toBe(1);
		expect(brewery.activeSetupId).toBe(DEFAULT_BREWERY_ID);
	});

	it('falls back to remaining setup when active setup is deleted', async () => {
		const setup2 = await brewery.addSetup('Second Rig');
		expect(brewery.activeSetupId).toBe(setup2.id);

		// Delete active setup
		const deleted = await brewery.deleteSetup(setup2.id);
		expect(deleted).toBe(true);
		expect(brewery.setups.length).toBe(1);
		expect(brewery.activeSetupId).toBe(DEFAULT_BREWERY_ID);
		expect(brewery.activeSetup.name).toBe(DEFAULT_BREWERY_NAME);
	});

	it('persists changes to localStorage and rehydrates accurately', async () => {
		const added = await brewery.addSetup('Basement Brewery');
		await brewery.renameSetup(DEFAULT_BREWERY_ID, 'Primary Shed');
		brewery.selectSetup(DEFAULT_BREWERY_ID);

		// Re-initialize to test hydration
		await brewery.init();

		expect(brewery.setups.length).toBe(2);
		expect(brewery.setups.find((s) => s.id === DEFAULT_BREWERY_ID)?.name).toBe('Primary Shed');
		expect(brewery.setups.find((s) => s.id === added.id)?.name).toBe('Basement Brewery');
		expect(brewery.activeSetupId).toBe(DEFAULT_BREWERY_ID);
	});

	it('recovers gracefully to default if localStorage data is corrupted', async () => {
		mockStorage.setItem('brewyou_brewery_setups', '{invalid-json');
		mockStorage.setItem('brewyou_active_setup_id', 'corrupt-id');

		await brewery.init();

		expect(brewery.setups.length).toBe(1);
		expect(brewery.setups[0].name).toBe(DEFAULT_BREWERY_NAME);
		expect(brewery.activeSetupId).toBe(DEFAULT_BREWERY_ID);
	});

	it('resetToDefault wipes setups from localStorage and resets activeSetupId', async () => {
		await brewery.addSetup('Basement Brewery');
		expect(mockStorage.getItem('brewyou_brewery_setups')).not.toBeNull();
		expect(mockStorage.getItem('brewyou_active_setup_id')).not.toBeNull();

		brewery.resetToDefault();

		expect(brewery.setups.length).toBe(1);
		expect(brewery.activeSetupId).toBe(DEFAULT_BREWERY_ID);
		expect(mockStorage.getItem('brewyou_brewery_setups')).toBeNull();
		expect(mockStorage.getItem('brewyou_active_setup_id')).toBeNull();
	});

	it('syncFromBackend deduplicates concurrent calls and updates active setup', async () => {
		const mockRemoteSetups = [
			{
				id: 'backend-setup-123',
				name: 'Backend Brewery',
				isDefault: true,
				equipmentCount: 2,
				defaultGrainAbsorptionRate: 0.96,
				defaultBoilOffRatePerHour: 3.0,
				defaultKettleTrubLossLiters: 1.5,
				defaultFermenterLossLiters: 1.5,
				defaultMashTunDeadSpaceLiters: 0.0,
				coolingShrinkagePercent: 4.0,
				defaultPackagingLossLiters: 0.5,
				createdAt: '2026-09-07T12:00:00Z',
				updatedAt: '2026-09-07T12:00:00Z'
			}
		];
		const { api } = await import('$lib/api/client');
		const listSpy = vi.spyOn(api.brewerySetups, 'list').mockResolvedValue(mockRemoteSetups);

		// Call twice concurrently
		await Promise.all([brewery.syncFromBackend(), brewery.syncFromBackend()]);

		expect(listSpy).toHaveBeenCalledTimes(1);
		expect(brewery.setups.length).toBe(1);
		expect(brewery.setups[0].id).toBe('backend-setup-123');
		expect(brewery.setups[0].name).toBe('Backend Brewery');
		expect(brewery.activeSetupId).toBe('backend-setup-123');
	});
});
