export interface ModalA11yOptions {
	onClose?: () => void;
	disabled?: boolean;
}

const FOCUSABLE_SELECTOR = [
	'a[href]',
	'button:not([disabled]):not([tabindex="-1"])',
	'input:not([disabled]):not([type="hidden"]):not([tabindex="-1"])',
	'select:not([disabled]):not([tabindex="-1"])',
	'textarea:not([disabled]):not([tabindex="-1"])',
	'[tabindex]:not([tabindex="-1"])'
].join(', ');

function getFocusableElements(container: HTMLElement): HTMLElement[] {
	return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)).filter(
		(el) => !el.hasAttribute('disabled') && el.getAttribute('aria-hidden') !== 'true'
	);
}

/**
 * Accessible modal action: traps Tab/Shift+Tab focus inside the modal container,
 * moves initial focus into the dialog, handles Escape to close, and restores focus on unmount.
 */
export function modalA11y(node: HTMLElement, options: ModalA11yOptions = {}) {
	let currentOptions = options;
	const previousFocus =
		typeof document !== 'undefined' && document.activeElement instanceof HTMLElement
			? document.activeElement
			: null;

	const focusInitial = () => {
		if (typeof document === 'undefined') return;
		if (!node.contains(document.activeElement)) {
			const focusables = getFocusableElements(node);
			if (focusables.length > 0) {
				focusables[0].focus();
			} else {
				if (!node.hasAttribute('tabindex')) {
					node.setAttribute('tabindex', '-1');
				}
				node.focus();
			}
		}
	};

	if (typeof queueMicrotask === 'function') {
		queueMicrotask(focusInitial);
	} else {
		focusInitial();
	}

	function handleKeydown(event: KeyboardEvent) {
		if (event.key === 'Escape') {
			if (!currentOptions.disabled && currentOptions.onClose) {
				event.preventDefault();
				currentOptions.onClose();
			}
			return;
		}

		if (event.key === 'Tab') {
			const focusables = getFocusableElements(node);
			if (focusables.length === 0) {
				event.preventDefault();
				return;
			}

			const first = focusables[0];
			const last = focusables[focusables.length - 1];
			const active = document.activeElement;

			if (event.shiftKey) {
				if (active === first || !node.contains(active)) {
					event.preventDefault();
					last.focus();
				}
			} else if (active === last || !node.contains(active)) {
				event.preventDefault();
				first.focus();
			}
		}
	}

	if (typeof document !== 'undefined') {
		document.addEventListener('keydown', handleKeydown);
	}

	return {
		update(nextOptions: ModalA11yOptions = {}) {
			currentOptions = nextOptions;
		},
		destroy() {
			if (typeof document !== 'undefined') {
				document.removeEventListener('keydown', handleKeydown);
			}
			if (previousFocus && previousFocus.isConnected && typeof previousFocus.focus === 'function') {
				previousFocus.focus();
			}
		}
	};
}
