<script lang="ts">
	import { srmToHexColor } from '$lib/calculators/brewing';
	import { settings } from '$lib/stores/settings.svelte';
	import BeerGlass from '$lib/components/brewery/BeerGlass.svelte';

	interface Props {
		srm: number;
		ebc?: number;
		showLabel?: boolean;
		showEbc?: boolean;
		size?: 'sm' | 'md' | 'lg';
		shape?: 'circle' | 'pill' | 'swatch' | 'glass';
		class?: string;
	}

	let {
		srm,
		ebc,
		showLabel = true,
		showEbc = false,
		size = 'md',
		shape = 'pill',
		class: className = ''
	}: Props = $props();

	const effectiveEbc = $derived(ebc ?? Number((srm * 1.97).toFixed(1)));
	const hexColor = $derived(srmToHexColor(srm));
	const isDarkBeer = $derived(srm > 8);

	const srmName = $derived.by(() => {
		if (srm <= 2) return 'Pale Straw';
		if (srm <= 4) return 'Straw / Pilsner';
		if (srm <= 6) return 'Gold';
		if (srm <= 9) return 'Deep Gold';
		if (srm <= 12) return 'Amber';
		if (srm <= 15) return 'Deep Amber';
		if (srm <= 20) return 'Copper';
		if (srm <= 27) return 'Brown';
		if (srm <= 35) return 'Dark Brown';
		return 'Imperial Stout / Black';
	});

	const sizeClasses = {
		sm: 'h-6 text-xs px-2 gap-1.5',
		md: 'h-8 text-xs px-3 gap-2',
		lg: 'h-10 text-sm px-3.5 gap-2.5'
	};

	const shapeClasses: Record<string, string> = {
		circle: 'rounded-full aspect-square p-0 justify-center',
		pill: 'rounded-full',
		swatch: 'rounded-md'
	};
</script>

{#if shape === 'glass'}
	<BeerGlass {srm} {ebc} {size} {showLabel} {showEbc} class={className} />
{:else}
	<div
		class="relative inline-flex items-center font-medium shadow-inner transition-transform select-none {sizeClasses[
			size
		]} {shapeClasses[shape]} {className}"
		style="background-color: {hexColor}; color: {isDarkBeer ? '#FFFFFF' : '#1A1006'};"
		role="img"
		aria-label="Beer color {srm} SRM, {srmName}"
		title="{srmName} ({srm} SRM / {effectiveEbc} EBC)"
	>
		<!-- Liquid optical glass highlight -->
		<span
			class="pointer-events-none absolute inset-x-0 top-0 h-1/2 rounded-t-[inherit] bg-gradient-to-b from-white/30 to-transparent"
			aria-hidden="true"
		></span>

		{#if shape !== 'circle' && showLabel}
			<span class="font-mono font-bold tracking-tight">
				{settings.colorUnit === 'EBC' ? `${effectiveEbc} EBC` : `${srm} SRM`}
			</span>
			{#if showEbc}
				<span class="font-mono text-[0.7em] opacity-80">
					({settings.colorUnit === 'EBC' ? `${srm} SRM` : `${effectiveEbc} EBC`})
				</span>
			{/if}
		{/if}
	</div>
{/if}
