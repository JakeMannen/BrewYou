import type { IngredientDto, IngredientType } from '$lib/types/api';

/**
 * Remove diacritics / accents from text (e.g., 'ü' -> 'u', 'ä' -> 'a', 'é' -> 'e').
 */
export function removeDiacritics(str: string): string {
	return str.normalize('NFD').replace(/[\u0300-\u036f]/g, '');
}

/**
 * Common Swedish and German compound brewing suffixes that should be split into words
 * for accurate token matching (e.g. 'Pilsnermalt' -> 'Pilsner malt').
 */
function splitCompoundBrewingTerms(str: string): string {
	return str
		.replace(/([a-zA-Z]{3,})(malt|malz)\b/gi, '$1 $2')
		.replace(/([a-zA-Z]{3,})(humle|hopfen)\b/gi, '$1 $2')
		.replace(/([a-zA-Z]{3,})(jäst|hefe)\b/gi, '$1 $2');
}

/**
 * Normalizes an ingredient name for comparison:
 * - Splits compound brewing terms (e.g. Pilsnermalt -> Pilsner malt)
 * - Removes accents / diacritics
 * - Lowercases and trims
 * - Normalizes German hop suffixes (e.g. Hallertauer -> Hallertau, Tettnanger -> Tettnang)
 * - Replaces punctuation (dashes, slashes, brackets, parens, quotes) with spaces
 * - Collapses multiple spaces
 */
export function normalizeIngredientName(name: string): string {
	const withSplitCompounds = splitCompoundBrewingTerms(name);
	const withoutDiacritics = removeDiacritics(withSplitCompounds).toLowerCase();

	const cleaned = withoutDiacritics
		.replace(/[()[\]{}"'’`\-–—/\\+:,._]/g, ' ')
		.replace(/\s+/g, ' ')
		.trim();

	// Normalize regional hop suffixes: hallertauer -> hallertau, tettnanger -> tettnang, spalter -> spalt
	return cleaned
		.split(' ')
		.map((word) => {
			if (['hallertauer', 'tettnanger', 'spalter', 'saazer', 'hersbrucker'].includes(word)) {
				return word.replace(/er$/, '');
			}
			return word;
		})
		.join(' ');
}

/**
 * Common brewing stopwords that don't differentiate an ingredient variety
 * (e.g. "malt", "grain", "hops", "hop", "yeast", "pellet", "pellets").
 */
const BREWING_STOPWORDS = new Set([
	'malt',
	'malts',
	'malz',
	'grain',
	'grains',
	'hop',
	'hops',
	'humle',
	'hopfen',
	'yeast',
	'yeasts',
	'jast',
	'hefe',
	'pellet',
	'pellets',
	'leaf',
	'leaves',
	'whole',
	'plug',
	'extract',
	'liquid',
	'dry'
]);

/**
 * Common brewing manufacturers/brands often prefixed with a delimiter (e.g. "Weyermann - ", "Crisp - ")
 */
const KNOWN_MANUFACTURERS = [
	'weyermann',
	'crisp',
	'simpsons',
	'muntons',
	'fawcett',
	'thomas fawcett',
	'briess',
	'dingemans',
	'castle',
	'castle malting',
	'viking',
	'viking malt',
	'bestmalz',
	'best malz',
	'bairds',
	'gladfield',
	'gambrinus',
	'rahr',
	'white labs',
	'wyeast',
	'fermentis',
	'lallemand',
	'omega',
	'imperial',
	'mangrove jacks',
	'mangrove jack',
	'yakima chief',
	'hopsteiner',
	'barthhaas',
	'ych'
];

/**
 * Strips manufacturer / maltster prefix from an ingredient name.
 * Handles patterns like:
 * - "Weyermann - Barke Pilsner Malt" -> "Barke Pilsner Malt"
 * - "Crisp: Maris Otter" -> "Maris Otter"
 * - "Fermentis SafAle US-05" -> "SafAle US-05"
 */
export function stripManufacturerPrefix(name: string): string {
	const trimmed = name.trim();

	// Explicit delimiter like "Brand - Name" or "Brand : Name"
	const delimiterMatch = trimmed.match(/^([A-Za-z0-9\s&]+?)\s*[-–—:]\s*(.+)$/);
	if (delimiterMatch) {
		const prefix = delimiterMatch[1].trim().toLowerCase();
		const remainder = delimiterMatch[2].trim();
		if (
			KNOWN_MANUFACTURERS.some((m) => prefix === m || prefix.includes(m) || m.includes(prefix)) ||
			prefix.split(/\s+/).length <= 3
		) {
			return remainder;
		}
	}

	// Known manufacturer at the beginning without delimiter
	const normalized = normalizeIngredientName(trimmed);
	for (const m of KNOWN_MANUFACTURERS) {
		if (normalized.startsWith(m + ' ')) {
			const words = trimmed.split(/\s+/);
			const mWordCount = m.split(/\s+/).length;
			if (words.length > mWordCount) {
				return words.slice(mWordCount).join(' ');
			}
		}
	}

	return trimmed;
}

/**
 * Strips parenthetical manufacturer / variety / country info at the end of a name.
 * e.g., "Pilsner Malt (Weyermann)" -> "Pilsner Malt"
 * e.g., "Centennial (US)" -> "Centennial"
 */
export function stripParentheticalSuffix(name: string): string {
	return name.replace(/\s*\([^)]*\)\s*$/, '').trim();
}

/**
 * Extract tokens from an ingredient name, excluding generic brewing stopwords.
 */
export function getSignificantTokens(name: string): string[] {
	const normalized = normalizeIngredientName(name);
	return normalized
		.split(' ')
		.map((t) => t.trim())
		.filter((t) => t.length > 0 && !BREWING_STOPWORDS.has(t));
}

/**
 * Extract yeast product code if present (e.g. "WLP001", "WLP860", "US-05", "S-04", "W-34/70", "WY1056", "M44", "K-97").
 */
export function extractYeastCode(name: string): string | null {
	const match = name.match(
		/\b(wlp\d{3,4}|us[- ]?05|s[- ]?04|s[- ]?23|s[- ]?33|s[- ]?189|w[- ]?34\/?70|be[- ]?134|be[- ]?256|t[- ]?58|wb[- ]?06|k[- ]?97|wy\d{4}|wyeast\s*\d{4}|m\d{2})\b/i
	);
	return match ? match[1].toLowerCase().replace(/[- /]/g, '') : null;
}

/**
 * Robustly matches an imported ingredient against a catalog of ingredients.
 *
 * Enforces strict matching tiers:
 * Tier 1: Exact string match (trimmed, case-insensitive)
 * Tier 2: Normalized exact match (ignoring punctuation, accents, compound endings)
 * Tier 3: Yeast product code match (for yeast items only)
 * Tier 4: Manufacturer prefix/suffix stripped exact match
 * Tier 5: Significant token exact match (equal token sets, preventing specific varieties like Barke Pilsner
 *         from matching generic base malts like Pilsner Malt)
 */
export function findCatalogIngredientMatch(
	imported: { name: string; type: IngredientType },
	catalog: IngredientDto[]
): IngredientDto | null {
	const rawName = imported.name.trim();
	if (!rawName) return null;

	// Candidates must have the exact same IngredientType
	const candidates = catalog.filter((c) => c.type === imported.type);
	if (candidates.length === 0) return null;

	const targetLower = rawName.toLowerCase();
	const targetNormalized = normalizeIngredientName(rawName);

	// Tier 1: Exact Case-Insensitive Match across ALL candidates
	const exactMatch = candidates.find((c) => c.name.trim().toLowerCase() === targetLower);
	if (exactMatch) return exactMatch;

	// Tier 2: Normalized Exact Match
	const normMatch = candidates.find((c) => normalizeIngredientName(c.name) === targetNormalized);
	if (normMatch) return normMatch;

	// Tier 3: Yeast Product Code Match (Yeast only)
	if (imported.type === 'Yeast') {
		const targetYeastCode = extractYeastCode(rawName);
		if (targetYeastCode) {
			const yeastCodeMatch = candidates.find((c) => {
				const cCode = extractYeastCode(c.name);
				return cCode !== null && cCode === targetYeastCode;
			});
			if (yeastCodeMatch) return yeastCodeMatch;
		}
	}

	// Tier 4: Manufacturer / Suffix Stripped Exact Match
	const strippedTarget = stripParentheticalSuffix(stripManufacturerPrefix(rawName));
	const strippedTargetNorm = normalizeIngredientName(strippedTarget);

	if (strippedTargetNorm && strippedTargetNorm !== targetNormalized) {
		const strippedMatch = candidates.find((c) => {
			const strippedC = stripParentheticalSuffix(stripManufacturerPrefix(c.name));
			const strippedCNorm = normalizeIngredientName(strippedC);
			return (
				normalizeIngredientName(c.name) === strippedTargetNorm ||
				strippedCNorm === strippedTargetNorm ||
				strippedCNorm === targetNormalized
			);
		});
		if (strippedMatch) return strippedMatch;
	}

	// Tier 5: Significant Token Exact Match (Word-boundary matching without generic stopwords)
	const targetSignificantTokens = getSignificantTokens(strippedTarget);
	if (targetSignificantTokens.length > 0) {
		const targetTokenSet = new Set(targetSignificantTokens);

		const tokenMatch = candidates.find((c) => {
			const strippedC = stripParentheticalSuffix(stripManufacturerPrefix(c.name));
			const cTokens = getSignificantTokens(strippedC);
			if (cTokens.length === 0 || cTokens.length !== targetSignificantTokens.length) {
				return false;
			}
			return cTokens.every((t) => targetTokenSet.has(t));
		});
		if (tokenMatch) return tokenMatch;
	}

	return null;
}

/**
 * Checks whether an imported ingredient matches an existing catalog item.
 */
export function isIngredientMatch(
	imported: { name: string; type: IngredientType },
	catalogItem: IngredientDto
): boolean {
	return findCatalogIngredientMatch(imported, [catalogItem]) !== null;
}

/**
 * Builds a canonical lookup key for resolving an unmatched imported ingredient
 * across the import modal and recipe creation/formulator handlers.
 */
export function buildMissingIngredientKey(item: { name: string; type: IngredientType }): string {
	return `${item.type}:${item.name.trim().toLowerCase()}`;
}

function getBigrams(str: string): string[] {
	const clean = str.replace(/\s+/g, '');
	if (clean.length < 2) return clean ? [clean] : [];
	const bigrams: string[] = [];
	for (let i = 0; i < clean.length - 1; i++) {
		bigrams.push(clean.slice(i, i + 2));
	}
	return bigrams;
}

function diceBigramSimilarity(a: string, b: string): number {
	if (!a || !b) return 0;
	if (a === b) return 1;
	const aBigrams = getBigrams(a);
	const bBigrams = getBigrams(b);
	if (aBigrams.length === 0 || bBigrams.length === 0) return 0;

	const bCounts = new Map<string, number>();
	for (const bg of bBigrams) {
		bCounts.set(bg, (bCounts.get(bg) ?? 0) + 1);
	}

	let intersection = 0;
	for (const bg of aBigrams) {
		const count = bCounts.get(bg) ?? 0;
		if (count > 0) {
			intersection++;
			bCounts.set(bg, count - 1);
		}
	}

	return (2 * intersection) / (aBigrams.length + bBigrams.length);
}

/**
 * Finds ranked close/fuzzy catalog matches of the same IngredientType for an unmatched
 * imported ingredient (e.g. "Weyermann - Barke Pilsner Malt" -> "Pilsner Malt",
 * "Citra Cryo" -> "Citra", "Centenial" -> "Centennial") so the user can pick an
 * existing catalog item instead of creating duplicates.
 */
export function findCloseCatalogIngredientMatches(
	imported: { name: string; type: IngredientType },
	catalog: IngredientDto[],
	limit = 5
): IngredientDto[] {
	const rawName = imported.name.trim();
	if (!rawName || limit <= 0) return [];

	const seenIds = new Set<string>();
	const candidates = catalog.filter((c) => {
		if (c.type !== imported.type || seenIds.has(c.id)) return false;
		seenIds.add(c.id);
		return true;
	});
	if (candidates.length === 0) return [];

	const strippedTarget = stripParentheticalSuffix(stripManufacturerPrefix(rawName));
	const targetTokens = getSignificantTokens(strippedTarget);
	const targetSigStr =
		targetTokens.length > 0
			? targetTokens.join(' ')
			: normalizeIngredientName(strippedTarget || rawName);

	const scored: Array<{ ingredient: IngredientDto; score: number }> = [];

	for (const candidate of candidates) {
		const strippedCand = stripParentheticalSuffix(stripManufacturerPrefix(candidate.name));
		const candTokens = getSignificantTokens(strippedCand);
		const candSigStr =
			candTokens.length > 0
				? candTokens.join(' ')
				: normalizeIngredientName(strippedCand || candidate.name);

		let tokenScore = 0;
		if (targetTokens.length > 0 && candTokens.length > 0) {
			let shared = 0;
			for (const tToken of targetTokens) {
				const matched = candTokens.some(
					(cToken) =>
						cToken === tToken ||
						(tToken.length >= 4 &&
							cToken.length >= 4 &&
							(cToken.includes(tToken) || tToken.includes(cToken)))
				);
				if (matched) shared++;
			}

			if (shared > 0) {
				const containment = shared / Math.min(targetTokens.length, candTokens.length);
				const unionSize = new Set([...targetTokens, ...candTokens]).size;
				const jaccard = unionSize > 0 ? shared / unionSize : 0;
				tokenScore = 0.65 * containment + 0.35 * jaccard;
			}
		}

		let substringScore = 0;
		if (
			targetSigStr.length >= 3 &&
			candSigStr.length >= 3 &&
			(targetSigStr.includes(candSigStr) || candSigStr.includes(targetSigStr))
		) {
			const lengthRatio =
				Math.min(targetSigStr.length, candSigStr.length) /
				Math.max(targetSigStr.length, candSigStr.length);
			substringScore = 0.65 + 0.35 * lengthRatio;
		}

		const bigramScore = diceBigramSimilarity(targetSigStr, candSigStr);
		const score = Math.max(tokenScore, substringScore, bigramScore >= 0.55 ? bigramScore : 0);

		if (score >= 0.45) {
			scored.push({ ingredient: candidate, score });
		}
	}

	return scored
		.sort((a, b) => b.score - a.score || a.ingredient.name.localeCompare(b.ingredient.name))
		.slice(0, limit)
		.map((s) => s.ingredient);
}
