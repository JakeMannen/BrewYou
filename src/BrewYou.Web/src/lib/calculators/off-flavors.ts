import type { OffFlavorDescriptorDto } from '$lib/types/api';

export const OFF_FLAVOR_KEYS = [
	'Diacetyl',
	'Dms',
	'Acetaldehyde',
	'Oxidation',
	'Phenolic',
	'Autolysis',
	'Lightstruck'
] as const;

export const FALLBACK_OFF_FLAVOR_CATALOG: OffFlavorDescriptorDto[] = [
	{
		key: 'Diacetyl',
		displayName: 'Diacetyl',
		sensoryCue: 'Butterscotch, buttered popcorn, or slick oily mouthfeel.',
		likelyCauses: [
			'Rushed fermentation or crashing temperature before yeast reabsorbed VDK/diacetyl.',
			'Weak yeast health, low pitch rate, or premature racking off yeast cake.',
			'Pediococcus or Lactobacillus bacterial contamination.'
		],
		remediationFixes: [
			'Perform a 2–4 day diacetyl rest at 18–20°C near ~75% attenuation before cold crashing.',
			'Pitch healthy yeast at target cell count with adequate wort aeration.'
		]
	},
	{
		key: 'Dms',
		displayName: 'DMS (Dimethyl Sulfide)',
		sensoryCue: 'Cooked sweet corn, creamed corn, or canned vegetables.',
		likelyCauses: [
			'Covered boil trapping volatile SMM/DMS precursors instead of venting steam.',
			'Slow post-boil chill leaving wort hot above 60°C.'
		],
		remediationFixes: [
			'Boil uncovered with a vigorous rolling boil (60–90 min for Pilsner malts).',
			'Chill rapidly below 25°C right after flameout.'
		]
	},
	{
		key: 'Acetaldehyde',
		displayName: 'Acetaldehyde',
		sensoryCue: 'Green apple, freshly cut pumpkin, or latex paint.',
		likelyCauses: [
			'Packaging too early before yeast reduces acetaldehyde to ethanol.',
			'Under-pitching or post-fermentation oxygen pickup.'
		],
		remediationFixes: [
			'Give healthy yeast 4–7 extra days warm after FG stabilizes.',
			'Use a starter and purge transfer lines/kegs.'
		]
	},
	{
		key: 'Oxidation',
		displayName: 'Oxidation',
		sensoryCue: 'Wet cardboard, papery stale malt, sherry, or faded hops.',
		likelyCauses: [
			'Splashing fermented beer during transfers or dry hopping.',
			'Air suck-back during cold crash or unpurged headspace.'
		],
		remediationFixes: [
			'Use CO2-purged closed transfers and a CO2 trap/balloon on cold crash.',
			'Store finished hop-forward beer cold (2–5°C).'
		]
	},
	{
		key: 'Phenolic',
		displayName: 'Phenolic / Chlorophenol',
		sensoryCue: 'Medicinal band-aid, clove spice, plastic, or smoky.',
		likelyCauses: [
			'Municipal chlorine or chloramine reacting with wort polyphenols.',
			'Over-sparging above pH 6.0 / 77°C extracting astringent tannins.'
		],
		remediationFixes: [
			'Treat brewing water with Campden tablet or activated carbon filter.',
			'Acidify sparge water below pH 5.8 and keep sparge temp ≤ 76°C.'
		]
	},
	{
		key: 'Autolysis',
		displayName: 'Autolysis',
		sensoryCue: 'Brothy, soy sauce, Vegemite, meaty, or burnt rubber.',
		likelyCauses: [
			'Leaving beer on aged trub for weeks/months at warm temperatures.',
			'Stressed or over-reused high-generation yeast.'
		],
		remediationFixes: [
			'Rack off primary yeast cake or crash within 3–4 weeks once FG is stable.',
			'Limit repitching to healthy low-generation slurries with nutrient.'
		]
	},
	{
		key: 'Lightstruck',
		displayName: 'Lightstruck (Skunky)',
		sensoryCue: 'Skunky musk from UV/blue light reacting with hop iso-alpha acids.',
		likelyCauses: [
			'Exposure of hopped beer in clear/green glass or open carboys to sunlight or bright UV lights.'
		],
		remediationFixes: [
			'Store beer in dark amber bottles, cans, or stainless kegs away from direct light.'
		]
	}
];
