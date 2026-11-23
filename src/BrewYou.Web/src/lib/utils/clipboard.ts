/**
 * Copies text to the system clipboard using the async Clipboard API when available
 * (HTTPS / localhost) and falls back to `document.execCommand('copy')` on plain
 * HTTP origins (such as LAN IP self-hosted deployments).
 */
export async function copyTextToClipboard(
	text: string,
	container?: HTMLElement | null
): Promise<boolean> {
	if (!text) return false;

	if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
		try {
			await navigator.clipboard.writeText(text);
			return true;
		} catch {
			// Fall back to legacy textarea selection copy below
		}
	}

	if (typeof document === 'undefined') return false;

	const textarea = document.createElement('textarea');
	textarea.value = text;
	textarea.setAttribute('readonly', '');
	textarea.style.position = 'fixed';
	textarea.style.top = '0';
	textarea.style.left = '-9999px';
	textarea.style.opacity = '0';

	const parent = container ?? document.body;
	const previousFocus =
		document.activeElement instanceof HTMLElement ? document.activeElement : null;
	parent.appendChild(textarea);

	try {
		textarea.focus();
		textarea.select();
		textarea.setSelectionRange(0, text.length);
		return document.execCommand('copy');
	} catch {
		return false;
	} finally {
		if (textarea.parentNode === parent) {
			parent.removeChild(textarea);
		}
		previousFocus?.focus?.();
	}
}
