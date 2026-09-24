import type { BrewerySetup } from '$lib/types/brewery';
import { api } from '$lib/api/client';

export const DEFAULT_BREWERY_ID = 'default-1';
export const DEFAULT_BREWERY_NAME = 'My brewery';

const STORAGE_SETUPS_KEY = 'brewyou_brewery_setups';
const STORAGE_ACTIVE_ID_KEY = 'brewyou_active_setup_id';

class BreweryState {
	setups = $state<BrewerySetup[]>([
		{
			id: DEFAULT_BREWERY_ID,
			name: DEFAULT_BREWERY_NAME,
			isDefault: true,
			createdAt: new Date().toISOString()
		}
	]);

	activeSetupId = $state<string>(DEFAULT_BREWERY_ID);
	isLoading = $state<boolean>(false);

	get activeSetup(): BrewerySetup {
		return (
			this.setups.find((s) => s.id === this.activeSetupId) ??
			this.setups[0] ?? {
				id: DEFAULT_BREWERY_ID,
				name: DEFAULT_BREWERY_NAME,
				isDefault: true,
				createdAt: new Date().toISOString()
			}
		);
	}

	get canDelete(): boolean {
		return this.setups.length > 1;
	}

	async init() {
		if (typeof localStorage === 'undefined') {
			return;
		}

		try {
			const savedSetups = localStorage.getItem(STORAGE_SETUPS_KEY);
			if (savedSetups) {
				const parsed = JSON.parse(savedSetups);
				if (Array.isArray(parsed) && parsed.length > 0) {
					const valid = parsed.filter(
						(s) => s && typeof s.id === 'string' && typeof s.name === 'string'
					);
					if (valid.length > 0) {
						this.setups = valid;
					} else {
						this.resetToDefault();
					}
				} else {
					this.resetToDefault();
				}
			} else {
				this.resetToDefault();
			}

			const savedActiveId = localStorage.getItem(STORAGE_ACTIVE_ID_KEY);
			if (savedActiveId && this.setups.some((s) => s.id === savedActiveId)) {
				this.activeSetupId = savedActiveId;
			} else {
				this.activeSetupId = this.setups[0]?.id ?? DEFAULT_BREWERY_ID;
			}
		} catch (err) {
			console.warn('Could not hydrate brewery setups from localStorage, using default:', err);
			this.resetToDefault();
		}

		if (typeof window !== 'undefined' && localStorage.getItem('brewyou_token')) {
			await this.syncFromBackend();
		}
	}

	private syncPromise: Promise<void> | null = null;

	async syncFromBackend(): Promise<void> {
		if (this.syncPromise) {
			return this.syncPromise;
		}

		this.syncPromise = (async () => {
			try {
				this.isLoading = true;
				const remote = await api.brewerySetups.list();
				if (remote && remote.length > 0) {
					this.setups = remote.map((s) => ({
						id: s.id,
						name: s.name,
						description: s.description ?? undefined,
						isDefault: s.isDefault,
						equipmentCount: s.equipmentCount,
						createdAt: s.createdAt
					}));

					if (!this.setups.some((s) => s.id === this.activeSetupId)) {
						const defaultSetup = this.setups.find((s) => s.isDefault) ?? this.setups[0];
						this.activeSetupId = defaultSetup.id;
					}
					this.persistToLocalStorage();
				}
			} catch (err) {
				console.warn('Could not sync brewery setups from backend:', err);
			} finally {
				this.isLoading = false;
				this.syncPromise = null;
			}
		})();

		return this.syncPromise;
	}

	resetToDefault() {
		this.setups = [
			{
				id: DEFAULT_BREWERY_ID,
				name: DEFAULT_BREWERY_NAME,
				isDefault: true,
				createdAt: new Date().toISOString()
			}
		];
		this.activeSetupId = DEFAULT_BREWERY_ID;
		if (typeof localStorage !== 'undefined') {
			try {
				localStorage.removeItem(STORAGE_SETUPS_KEY);
				localStorage.removeItem(STORAGE_ACTIVE_ID_KEY);
			} catch (err) {
				console.warn('Could not clear brewery setups from localStorage:', err);
			}
		}
	}

	selectSetup(id: string): void {
		if (this.setups.some((s) => s.id === id)) {
			this.activeSetupId = id;
			this.persistToLocalStorage();
		}
	}

	async addSetup(name?: string): Promise<BrewerySetup> {
		let finalName = name?.trim();
		if (!finalName) {
			let counter = this.setups.length + 1;
			while (this.setups.some((s) => s.name.toLowerCase() === `brewery ${counter}`.toLowerCase())) {
				counter++;
			}
			finalName = `Brewery ${counter}`;
		}

		// Security: sanitize invalid angle brackets
		finalName = finalName.replace(/[<>]/g, '').trim();
		if (finalName.length > 50) {
			finalName = finalName.slice(0, 50);
		}

		if (typeof window !== 'undefined' && localStorage.getItem('brewyou_token')) {
			try {
				const remote = await api.brewerySetups.create({ name: finalName });
				if (remote && typeof remote.id === 'string' && typeof remote.name === 'string') {
					const newSetup: BrewerySetup = {
						id: remote.id,
						name: remote.name,
						description: remote.description ?? undefined,
						isDefault: remote.isDefault,
						equipmentCount: remote.equipmentCount,
						createdAt: remote.createdAt
					};
					this.setups = [...this.setups, newSetup];
					this.activeSetupId = newSetup.id;
					this.persistToLocalStorage();
					return newSetup;
				}
			} catch (err) {
				console.warn('Backend setup creation failed, falling back to local:', err);
			}
		}

		const newSetup: BrewerySetup = {
			id:
				typeof crypto !== 'undefined' && crypto.randomUUID
					? crypto.randomUUID()
					: `brewery-${Date.now()}`,
			name: finalName,
			createdAt: new Date().toISOString()
		};

		this.setups = [...this.setups, newSetup];
		this.activeSetupId = newSetup.id;
		this.persistToLocalStorage();
		return newSetup;
	}

	async renameSetup(id: string, newName: string): Promise<boolean> {
		const trimmed = newName.trim();
		if (!trimmed || trimmed.length > 50 || trimmed.includes('<') || trimmed.includes('>')) {
			return false;
		}

		const index = this.setups.findIndex((s) => s.id === id);
		if (index === -1) return false;

		if (typeof window !== 'undefined' && localStorage.getItem('brewyou_token')) {
			try {
				await api.brewerySetups.update(id, { name: trimmed });
			} catch (err) {
				console.warn('Backend setup update failed:', err);
			}
		}

		const updated = [...this.setups];
		updated[index] = {
			...updated[index],
			name: trimmed
		};
		this.setups = updated;
		this.persistToLocalStorage();
		return true;
	}

	async deleteSetup(id: string): Promise<boolean> {
		if (this.setups.length <= 1) {
			return false;
		}

		const targetExists = this.setups.some((s) => s.id === id);
		if (!targetExists) return false;

		if (typeof window !== 'undefined' && localStorage.getItem('brewyou_token')) {
			try {
				await api.brewerySetups.delete(id);
			} catch (err) {
				console.warn('Backend setup delete failed:', err);
			}
		}

		const remaining = this.setups.filter((s) => s.id !== id);
		this.setups = remaining;

		if (this.activeSetupId === id) {
			this.activeSetupId = remaining[0].id;
		}

		this.persistToLocalStorage();
		return true;
	}

	private persistToLocalStorage(): void {
		if (typeof localStorage !== 'undefined') {
			try {
				localStorage.setItem(STORAGE_SETUPS_KEY, JSON.stringify(this.setups));
				localStorage.setItem(STORAGE_ACTIVE_ID_KEY, this.activeSetupId);
			} catch (err) {
				console.warn('Could not persist brewery setups to localStorage:', err);
			}
		}
	}
}

export const brewery = new BreweryState();
