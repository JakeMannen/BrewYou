export type ThemePreference =
	'dark' | 'light' | 'system' | 'imperial-stout' | 'chocolate-porter' | 'obsidian';
export type ResolvedTheme = 'dark' | 'light' | 'imperial-stout' | 'chocolate-porter' | 'obsidian';

const STORAGE_KEY = 'brewyou-theme';

class ThemeStore {
	#preference = $state<ThemePreference>('imperial-stout');
	#systemTheme = $state<ResolvedTheme>('imperial-stout');

	constructor() {
		// Default to imperial-stout theme
	}

	init(): void {
		if (typeof window === 'undefined') return;

		const saved = localStorage.getItem(STORAGE_KEY) as ThemePreference | null;
		if (
			saved === 'dark' ||
			saved === 'light' ||
			saved === 'system' ||
			saved === 'imperial-stout' ||
			saved === 'chocolate-porter' ||
			saved === 'obsidian'
		) {
			this.#preference = saved;
		} else {
			this.#preference = 'imperial-stout';
		}

		if (window.matchMedia) {
			const media = window.matchMedia('(prefers-color-scheme: dark)');
			this.#systemTheme = media.matches ? 'dark' : 'light';

			media.addEventListener('change', (e) => {
				this.#systemTheme = e.matches ? 'dark' : 'light';
				if (this.#preference === 'system') {
					this.applyDocumentClass();
				}
			});
		}

		window.addEventListener('storage', (e) => {
			if (e.key === STORAGE_KEY && e.newValue) {
				if (
					e.newValue === 'dark' ||
					e.newValue === 'light' ||
					e.newValue === 'system' ||
					e.newValue === 'imperial-stout' ||
					e.newValue === 'chocolate-porter' ||
					e.newValue === 'obsidian'
				) {
					this.#preference = e.newValue as ThemePreference;
					this.applyDocumentClass();
				}
			}
		});

		this.applyDocumentClass();
	}

	get current(): ResolvedTheme {
		return this.#preference === 'system' ? this.#systemTheme : this.#preference;
	}

	get preference(): ThemePreference {
		return this.#preference;
	}

	setTheme(pref: ThemePreference): void {
		this.#preference = pref;
		if (typeof window !== 'undefined') {
			localStorage.setItem(STORAGE_KEY, pref);
			this.applyDocumentClass();
		}
	}

	toggle(): void {
		const next: ThemePreference =
			this.current === 'dark' ||
			this.current === 'imperial-stout' ||
			this.current === 'chocolate-porter' ||
			this.current === 'obsidian'
				? 'light'
				: 'dark';
		this.setTheme(next);
	}

	cycle(): void {
		let next: ThemePreference;
		if (this.current === 'imperial-stout') {
			next = 'chocolate-porter';
		} else if (this.current === 'chocolate-porter') {
			next = 'obsidian';
		} else if (this.current === 'obsidian') {
			next = 'dark';
		} else if (this.current === 'dark') {
			next = 'light';
		} else {
			next = 'imperial-stout';
		}
		this.setTheme(next);
	}

	private applyDocumentClass(): void {
		if (typeof document === 'undefined') return;
		const isDark =
			this.current === 'dark' ||
			this.current === 'imperial-stout' ||
			this.current === 'chocolate-porter' ||
			this.current === 'obsidian';
		document.documentElement.classList.toggle('dark', isDark);
		document.documentElement.setAttribute('data-theme', this.current);
		if (document.documentElement.style) {
			document.documentElement.style.colorScheme = isDark ? 'dark' : 'light';
		}
	}
}

export const theme = new ThemeStore();
