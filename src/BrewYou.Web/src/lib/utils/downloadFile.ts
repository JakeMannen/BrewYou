/**
 * Utility for cross-browser safe file downloading and filename sanitization.
 */

const RESERVED_WINDOWS_NAMES = /^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\..*)?$/i;

/**
 * Sanitizes recipe name into a safe filename across Windows, macOS, and Linux filesystems.
 */
export function sanitizeFilename(name: string | null | undefined, extension: string): string {
	const ext = extension.replace(/^\./, '').toLowerCase();
	if (!name || typeof name !== 'string') {
		return `recipe.${ext}`;
	}

	let safe = name
		.trim()
		.replace(/[\r\n\0]/g, '') // remove control characters
		.replace(/[/\\?%*:|"<>]/g, '_') // replace invalid path/filesystem chars
		.replace(/\.{2,}/g, '.') // prevent path traversal dots
		.replace(/\s+/g, '_'); // replace whitespace with underscores

	if (RESERVED_WINDOWS_NAMES.test(safe)) {
		safe = `recipe_${safe}`;
	}

	// Trim leading/trailing underscores and dots
	safe = safe.replace(/^[._]+|[._]+$/g, '');

	if (!safe) {
		safe = 'recipe';
	}

	// Truncate to maximum 80 characters to stay well within filesystem limits
	if (safe.length > 80) {
		safe = safe.slice(0, 80).replace(/[._]+$/, '');
	}

	return `${safe}.${ext}`;
}

/**
 * Triggers a client-side file download via a UTF-8 Blob and anchor tag dispatch.
 * Cleans up Object URL to prevent browser memory leaks.
 */
export function triggerFileDownload(content: string, filename: string, mimeType: string): void {
	if (typeof window === 'undefined' || typeof document === 'undefined') {
		return;
	}

	const blob = new Blob([content], { type: mimeType });
	const objectUrl = URL.createObjectURL(blob);

	const anchor = document.createElement('a');
	anchor.href = objectUrl;
	anchor.download = filename;
	anchor.style.display = 'none';

	document.body.appendChild(anchor);
	anchor.click();

	setTimeout(() => {
		if (anchor.parentNode) {
			anchor.parentNode.removeChild(anchor);
		}
		URL.revokeObjectURL(objectUrl);
	}, 200);
}
