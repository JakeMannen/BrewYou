import type { Action } from 'svelte/action';

/**
 * Svelte action to portal an element to document.body (or a specified target).
 *
 * Browsers establish a new containing block for `position: fixed` elements
 * whenever any ancestor applies `backdrop-filter`, `transform`, `filter`, or `perspective`.
 * When a modal is embedded inside such a container (e.g. frosted glass sidebars or headers),
 * `position: fixed` is constrained to that ancestor instead of spanning the viewport.
 *
 * This action breaks the node out of that containing block by appending it directly
 * to `document.body` while preserving Svelte's reactive bindings and event listeners.
 */
export const portal: Action<HTMLElement, HTMLElement | string | undefined> = (
	node,
	target = 'body'
) => {
	function mountTo(newTarget: HTMLElement | string | undefined = 'body') {
		if (typeof document === 'undefined') return;

		const targetEl =
			!newTarget || newTarget === 'body'
				? document.body
				: typeof newTarget === 'string'
					? document.querySelector<HTMLElement>(newTarget)
					: newTarget instanceof HTMLElement
						? newTarget
						: document.body;

		if (targetEl && node.parentNode !== targetEl) {
			targetEl.appendChild(node);
		}
	}

	mountTo(target);

	return {
		update(newTarget) {
			mountTo(newTarget);
		},
		destroy() {
			if (node.parentNode) {
				node.parentNode.removeChild(node);
			}
		}
	};
};
