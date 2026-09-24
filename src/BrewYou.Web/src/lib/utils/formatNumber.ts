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
