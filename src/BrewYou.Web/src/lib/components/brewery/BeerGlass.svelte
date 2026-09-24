<script lang="ts">
	import { srmToHexColor } from '$lib/calculators/brewing';
	import { settings } from '$lib/stores/settings.svelte';
	import { t } from '$lib/i18n/index.svelte';

	interface Props {
		srm: number;
		ebc?: number;
		size?: 'xs' | 'sm' | 'md' | 'lg' | 'xl' | 'full' | number;
		showLabel?: boolean;
		showEbc?: boolean;
		showDescriptor?: boolean;
		class?: string;
	}

	let {
		srm,
		ebc,
		size = 'md',
		showLabel = false,
		showEbc = false,
		showDescriptor = false,
		class: className = ''
	}: Props = $props();

	// Unique ID for SVG gradients to prevent clash when multiple glasses are on the same page
	const uid = Math.random().toString(36).substring(2, 9);

	const effectiveEbc = $derived(ebc ?? Number((srm * 1.97).toFixed(1)));
	const hexColor = $derived(srmToHexColor(srm));

	// Foam coloring adapting to SRM beer style
	const foam = $derived.by(() => {
		if (srm <= 8) {
			// Pale / Golden beers: crisp white to light ivory foam
			return { top: '#FFFFFF', bottom: '#F3EFE0', stroke: '#E8E2CF' };
		} else if (srm <= 20) {
			// Amber / Copper beers: warm cream head
			return { top: '#FDF7EB', bottom: '#E4D5BC', stroke: '#D8C5A7' };
		} else {
			// Dark beers / Stouts: rich mocha / tan foam
			return { top: '#ECE1CE', bottom: '#C4A987', stroke: '#B2946E' };
		}
	});

	// SRM style descriptor key
	const descriptorKey = $derived.by(() => {
		if (srm <= 2) return 'pale_straw';
		if (srm <= 4) return 'straw';
		if (srm <= 6) return 'gold';
		if (srm <= 9) return 'deep_gold';
		if (srm <= 12) return 'amber';
		if (srm <= 15) return 'deep_amber';
		if (srm <= 20) return 'copper';
		if (srm <= 27) return 'brown';
		if (srm <= 35) return 'dark_brown';
		return 'black';
	});

	// Fallback english names
	const fallbackNames: Record<string, string> = {
		pale_straw: 'Pale Straw',
		straw: 'Straw / Pilsner',
		gold: 'Gold',
		deep_gold: 'Deep Gold',
		amber: 'Amber',
		deep_amber: 'Deep Amber',
		copper: 'Copper',
		brown: 'Brown',
		dark_brown: 'Dark Brown',
		black: 'Imperial Stout / Black'
	};

	const localizedDescriptor = $derived.by(() => {
		try {
			const localized = t(`srm_descriptors.${descriptorKey}`);
			return localized && !localized.startsWith('srm_descriptors.')
				? localized
				: (fallbackNames[descriptorKey] ?? 'Beer');
		} catch {
			return fallbackNames[descriptorKey] ?? 'Beer';
		}
	});

	const ariaLabel = $derived.by(() => {
		try {
			const text = t('srm_descriptors.glass_aria', {
				srm: srm.toFixed(1),
				descriptor: localizedDescriptor
			});
			return text && !text.startsWith('srm_descriptors.')
				? text
				: `Beer glass with ${srm} SRM color (${localizedDescriptor})`;
		} catch {
			return `Beer glass with ${srm} SRM color (${localizedDescriptor})`;
		}
	});

	const titleText = $derived.by(() => {
		try {
			const text = t('srm_descriptors.glass_title', {
				descriptor: localizedDescriptor,
				srm: srm.toFixed(1),
				ebc: effectiveEbc.toFixed(1)
			});
			return text && !text.startsWith('srm_descriptors.')
				? text
				: `${localizedDescriptor} (${srm} SRM / ${effectiveEbc} EBC)`;
		} catch {
			return `${localizedDescriptor} (${srm} SRM / ${effectiveEbc} EBC)`;
		}
	});

	const dimensions = $derived.by(() => {
		if (size === 'full') {
			return { h: undefined, w: undefined, isFull: true };
		}
		if (typeof size === 'number') {
			return { h: size, w: Number(((size * 34) / 63).toFixed(1)), isFull: false };
		}
		const preset: Record<string, { h: number; w: number }> = {
			xs: { h: 28, w: 15 },
			sm: { h: 48, w: 26 },
			md: { h: 62, w: 33 },
			lg: { h: 78, w: 42 },
			xl: { h: 104, w: 56 }
		};
		return { ...(preset[size] ?? preset.md), isFull: false };
	});
</script>

<div
	class="inline-flex items-center gap-2 font-medium select-none {className}"
	role="img"
	aria-label={ariaLabel}
	title={titleText}
>
	<svg
		viewBox="7 5 34 63"
		width={dimensions.w}
		height={dimensions.h}
		class="shrink-0 drop-shadow-sm transition-transform duration-200 hover:scale-105 {dimensions.isFull
			? 'h-full max-h-full w-auto'
			: ''}"
		style={dimensions.isFull ? 'height: 100%; aspect-ratio: 34 / 63;' : ''}
		aria-hidden="true"
	>
		<defs>
			<!-- Liquid optical volume gradient -->
			<linearGradient id="beer-liquid-{uid}" x1="0%" y1="0%" x2="100%" y2="0%">
				<stop offset="0%" stop-color={hexColor} stop-opacity="0.82" />
				<stop offset="35%" stop-color={hexColor} stop-opacity="1" />
				<stop offset="85%" stop-color={hexColor} stop-opacity="0.95" />
				<stop offset="100%" stop-color={hexColor} stop-opacity="0.8" />
			</linearGradient>

			<!-- Liquid internal top-to-bottom depth -->
			<linearGradient id="beer-depth-{uid}" x1="0%" y1="0%" x2="0%" y2="100%">
				<stop offset="0%" stop-color="#FFFFFF" stop-opacity="0.18" />
				<stop offset="40%" stop-color="#FFFFFF" stop-opacity="0" />
				<stop offset="100%" stop-color="#000000" stop-opacity="0.25" />
			</linearGradient>

			<!-- Foam head gradient -->
			<linearGradient id="beer-foam-{uid}" x1="0%" y1="0%" x2="0%" y2="100%">
				<stop offset="0%" stop-color={foam.top} />
				<stop offset="70%" stop-color={foam.top} stop-opacity="0.95" />
				<stop offset="100%" stop-color={foam.bottom} />
			</linearGradient>

			<!-- Glass highlight reflection (left wall) -->
			<linearGradient id="glass-reflection-{uid}" x1="0%" y1="0%" x2="100%" y2="0%">
				<stop offset="0%" stop-color="#FFFFFF" stop-opacity="0.5" />
				<stop offset="50%" stop-color="#FFFFFF" stop-opacity="0.1" />
				<stop offset="100%" stop-color="#FFFFFF" stop-opacity="0" />
			</linearGradient>

			<!-- Glass rim shine -->
			<linearGradient id="rim-sheen-{uid}" x1="0%" y1="0%" x2="100%" y2="0%">
				<stop offset="0%" stop-color="#FFFFFF" stop-opacity="0.6" />
				<stop offset="40%" stop-color="#FFFFFF" stop-opacity="0.2" />
				<stop offset="100%" stop-color="#FFFFFF" stop-opacity="0.5" />
			</linearGradient>
		</defs>

		<!-- Glass Background Shadow / Translucent Hull -->
		<path
			d="M 10 8 L 8 26 C 8 36, 13 58, 14 60 L 12 65 C 12 66, 13 67, 15 67 L 33 67 C 35 67, 36 66, 36 65 L 34 60 C 35 58, 40 36, 40 26 L 38 8 Z"
			fill="currentColor"
			fill-opacity="0.04"
		/>

		<!-- Beer Liquid Body (filled up to y=18) -->
		<path
			d="M 13.5 59 L 8.6 26 C 8.6 22, 9.6 18, 11 18 L 37 18 C 38.4 18, 39.4 22, 39.4 26 L 34.5 59 Z"
			fill="url(#beer-liquid-{uid})"
		/>

		<!-- Beer Liquid Depth Overlay -->
		<path
			d="M 13.5 59 L 8.6 26 C 8.6 22, 9.6 18, 11 18 L 37 18 C 38.4 18, 39.4 22, 39.4 26 L 34.5 59 Z"
			fill="url(#beer-depth-{uid})"
		/>

		<!-- Carbonation Micro-Bubbles inside the liquid -->
		<g fill="#FFFFFF">
			<circle cx="20" cy="51" r="0.9" opacity="0.4" />
			<circle cx="27" cy="45" r="1.1" opacity="0.45" />
			<circle cx="17" cy="38" r="0.8" opacity="0.35" />
			<circle cx="28" cy="31" r="1.0" opacity="0.4" />
			<circle cx="22" cy="24" r="1.1" opacity="0.5" />
			<circle cx="31" cy="22" r="0.8" opacity="0.45" />
		</g>

		<!-- Pillowy Foam Head (Krausen) -->
		<path
			d="M 10.5 18 C 9 13.5, 13 11, 16.5 12 C 19.5 9.5, 28.5 9.5, 31.5 12 C 35 11, 39 13.5, 37.5 18 C 34 19.5, 14 19.5, 10.5 18 Z"
			fill="url(#beer-foam-{uid})"
			stroke={foam.stroke}
			stroke-width="0.5"
		/>

		<!-- Foam Head Highlights / Micro-foam texture -->
		<path
			d="M 14 13.5 C 16 11.5, 20 11, 23 11.5"
			stroke="#FFFFFF"
			stroke-width="0.8"
			stroke-linecap="round"
			fill="none"
			opacity="0.75"
		/>
		<path
			d="M 26 11.5 C 29 11.2, 33 12, 35 14"
			stroke="#FFFFFF"
			stroke-width="0.7"
			stroke-linecap="round"
			fill="none"
			opacity="0.6"
		/>

		<!-- Solid Glass Base / Foot (thick weighted bottom) -->
		<path
			d="M 14 59 L 12 65 C 12 66, 13 67, 15 67 L 33 67 C 35 67, 36 66, 36 65 L 34 59 Z"
			fill="currentColor"
			fill-opacity="0.08"
			stroke="currentColor"
			stroke-opacity="0.25"
			stroke-width="0.8"
		/>

		<!-- Glass Base Refraction Highlight -->
		<path
			d="M 15 63 Q 24 64.5 33 63"
			stroke="#FFFFFF"
			stroke-width="1.2"
			stroke-linecap="round"
			fill="none"
			opacity="0.45"
		/>

		<!-- Glass Wall Left Optical Specular Highlight -->
		<path
			d="M 11.2 12 C 10.2 24, 10.5 35, 14.8 57"
			stroke="url(#glass-reflection-{uid})"
			stroke-width="2"
			stroke-linecap="round"
			fill="none"
		/>

		<!-- Glass Wall Right Subtle Highlight -->
		<path
			d="M 36.8 12 C 37.8 24, 37.5 35, 33.2 57"
			stroke="#FFFFFF"
			stroke-width="0.7"
			stroke-linecap="round"
			fill="none"
			opacity="0.2"
		/>

		<!-- Glass Top Rim Oval -->
		<ellipse
			cx="24"
			cy="8"
			rx="14"
			ry="2.2"
			fill="none"
			stroke="url(#rim-sheen-{uid})"
			stroke-width="1.1"
		/>

		<!-- Outer Glass Hull Outline -->
		<path
			d="M 10 8 L 8 26 C 8 36, 13 58, 14 60 L 12 65 C 12 66, 13 67, 15 67 L 33 67 C 35 67, 36 66, 36 65 L 34 60 C 35 58, 40 36, 40 26 L 38 8"
			fill="none"
			stroke="currentColor"
			stroke-opacity="0.3"
			stroke-width="1"
			stroke-linejoin="round"
		/>
	</svg>

	{#if showLabel}
		<div class="flex flex-col select-none">
			<div class="flex items-baseline gap-1">
				<span class="font-mono text-xs font-bold text-zinc-900 dark:text-white">
					{settings.colorUnit === 'EBC' ? `${effectiveEbc} EBC` : `${srm} SRM`}
				</span>
				{#if showEbc}
					<span class="font-mono text-[10px] text-zinc-500 dark:text-zinc-400">
						({settings.colorUnit === 'EBC' ? `${srm} SRM` : `${effectiveEbc} EBC`})
					</span>
				{/if}
			</div>
			{#if showDescriptor}
				<span class="text-[10px] font-medium text-zinc-500 dark:text-zinc-400">
					{localizedDescriptor}
				</span>
			{/if}
		</div>
	{/if}
</div>
