import { i18n } from '$lib/i18n/index.svelte';

/**
 * Format a number with locale-aware decimal separator.
 *
 * Uses `Intl.NumberFormat` with the current i18n locale so that
 * Swedish (`sv`) users see `,` while English (`en`) users see `.`.
 *
 * **Do NOT use this for SVG path coordinates** — SVG requires `.`
 * as the decimal separator per spec. Use `.toFixed()` for SVG paths.
 */
export function formatNumber(
	value: number | null | undefined,
	decimals = 1,
	fallback = '-'
): string {
	if (value === null || value === undefined || isNaN(value)) {
		return fallback;
	}

	return new Intl.NumberFormat(i18n.locale, {
		minimumFractionDigits: decimals,
		maximumFractionDigits: decimals,
		useGrouping: false
	}).format(value);
}

/**
 * Format a numeric value for pre-filling editable decimal inputs using the active locale,
 * returning an empty string when unset so placeholder hints remain visible.
 */
export function formatDecimalInput(value: number | null | undefined, decimals = 1): string {
	if (value === null || value === undefined || isNaN(value)) {
		return '';
	}
	return formatNumber(value, decimals);
}

/**
 * Parse a decimal number entered with either dot (`.`) or comma (`,`) separator.
 * Returns `null` for empty, whitespace-only, or malformed inputs.
 */
export function parseDecimal(raw: string | number | null | undefined): number | null {
	if (raw === null || raw === undefined) return null;
	if (typeof raw === 'number') {
		return Number.isFinite(raw) ? raw : null;
	}
	const normalized = raw.trim().replace(/\s+/g, '').replace(',', '.');
	if (!normalized || !/^-?(?:\d+(?:\.\d*)?|\.\d+)$/.test(normalized)) {
		return null;
	}
	const parsed = Number(normalized);
	return Number.isFinite(parsed) ? parsed : null;
}
