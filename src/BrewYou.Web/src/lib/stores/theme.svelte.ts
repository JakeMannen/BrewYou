export type ThemePreference = 'dark' | 'light' | 'system' | 'botanical';
export type ResolvedTheme = 'dark' | 'light' | 'botanical';

const STORAGE_KEY = 'brewyou-theme';

class ThemeStore {
	#preference = $state<ThemePreference>('dark');
	#systemTheme = $state<ResolvedTheme>('dark');

	constructor() {
		// Default to dark theme
	}

	init(): void {
		if (typeof window === 'undefined') return;

		const saved = localStorage.getItem(STORAGE_KEY) as ThemePreference | null;
		if (saved === 'dark' || saved === 'light' || saved === 'system' || saved === 'botanical') {
			this.#preference = saved;
		} else {
			this.#preference = 'dark';
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
					e.newValue === 'botanical'
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
		const next: ThemePreference = this.current === 'dark' ? 'light' : 'dark';
		this.setTheme(next);
	}

	private applyDocumentClass(): void {
		if (typeof document === 'undefined') return;
		const resolved = this.current;
		const isDarkOrBotanical = resolved === 'dark' || resolved === 'botanical';
		document.documentElement.classList.toggle('dark', isDarkOrBotanical);
		document.documentElement.setAttribute('data-theme', resolved);
		if (document.documentElement.style) {
			document.documentElement.style.colorScheme = isDarkOrBotanical ? 'dark' : 'light';
		}
	}
}

export const theme = new ThemeStore();
