<script lang="ts">
	import type { EquipmentSubtype } from '$lib/types/api';
	import {
		CHAMBER_BOUNDS,
		isVesselFillable,
		resolveVesselAnimationType
	} from '$lib/components/inventory/vessel-geometry';

	interface Props {
		subtype?: EquipmentSubtype | 'Bottle' | string;
		fillPercent?: number;
		size?: number | string;
		class?: string;
		active?: boolean;
		title?: string;
		showMeter?: boolean;
		animationStage?: 'mash' | 'boil' | 'ferment' | 'condition' | 'package' | string;
		animated?: boolean;
	}

	let {
		subtype = 'AllInOne',
		fillPercent = 0,
		size = 48,
		class: className = '',
		active = false,
		title = '',
		showMeter = true,
		animationStage,
		animated = false
	}: Props = $props();

	// Canonical subtype normalization
	const validSubtypes: (EquipmentSubtype | 'Bottle')[] = [
		'AllInOne',
		'Pan',
		'Hlt',
		'HermsRims',
		'Bucket',
		'ConicalFermenter',
		'Carboy',
		'PressureFermenter',
		'StainlessBucket',
		'Cornelius',
		'Minikeg',
		'MiniBarrel',
		'PetKeg',
		'ISpindel',
		'Tilt',
		'GenericSensor',
		'Bottle',
		'Other'
	];

	const normalizedSubtype = $derived<EquipmentSubtype | 'Bottle'>(
		validSubtypes.find((s) => s.toLowerCase() === (subtype ?? '').toLowerCase()) ?? 'Other'
	);

	const isFillable = $derived(isVesselFillable(normalizedSubtype));

	const instanceId = $derived(`vessel-${Math.random().toString(36).slice(2, 8)}`);
	const clipId = $derived(`clip-${instanceId}`);
	const gradId = $derived(`grad-${instanceId}`);
	const titleId = $derived(`title-${instanceId}`);

	const clampedFill = $derived.by(() => {
		if (!isFillable) return 0;
		if (typeof fillPercent !== 'number' || isNaN(fillPercent)) return 0;
		return Math.max(0, Math.min(100, fillPercent));
	});

	const bounds = $derived(CHAMBER_BOUNDS[normalizedSubtype] ?? CHAMBER_BOUNDS.Other);
	const usableHeight = $derived(bounds.bottomY - bounds.topY);
	const liquidHeight = $derived((clampedFill / 100) * usableHeight);
	const liquidY = $derived(bounds.bottomY - liquidHeight);

	const accessibilityLabel = $derived.by(() => {
		if (title) return title;
		if (!isFillable) return normalizedSubtype;
		if (clampedFill === 0) return `${normalizedSubtype} (Empty)`;
		if (clampedFill === 100) return `${normalizedSubtype} (Full, 100%)`;
		return `${normalizedSubtype} (${Math.round(clampedFill)}% Full)`;
	});

	const effectiveAnimationType = $derived(
		resolveVesselAnimationType(normalizedSubtype, { active, animated, animationStage })
	);
</script>

<svg
	xmlns="http://www.w3.org/2000/svg"
	viewBox="0 0 48 48"
	width={size}
	height={size}
	fill="none"
	stroke="currentColor"
	stroke-width="2"
	stroke-linecap="round"
	stroke-linejoin="round"
	data-testid="illustration-{normalizedSubtype}"
	role={showMeter && isFillable ? 'meter' : 'img'}
	aria-labelledby={title ? titleId : undefined}
	aria-label={!title ? accessibilityLabel : undefined}
	aria-valuenow={showMeter && isFillable ? Math.round(clampedFill) : undefined}
	aria-valuemin={showMeter && isFillable ? 0 : undefined}
	aria-valuemax={showMeter && isFillable ? 100 : undefined}
	aria-valuetext={showMeter && isFillable ? accessibilityLabel : undefined}
	class="transition-all duration-200 {className} {active
		? 'text-amber-500 drop-shadow-[0_0_10px_rgba(245,158,11,0.35)]'
		: ''}"
>
	{#if title}
		<title id={titleId}>{title}</title>
	{/if}

	<defs>
		<!-- Craft Beer Amber Wort Gradient -->
		<linearGradient id={gradId} x1="0" y1="0" x2="0" y2="1">
			<stop offset="0%" stop-color="#f59e0b" stop-opacity="0.85" />
			<stop offset="60%" stop-color="#d97706" stop-opacity="0.92" />
			<stop offset="100%" stop-color="#92400e" stop-opacity="0.98" />
		</linearGradient>

		<!-- Internal Chamber Clip Paths for Each Vessel Type -->
		<clipPath id={clipId}>
			{#if normalizedSubtype === 'AllInOne'}
				<rect x="15" y="13" width="18" height="26" rx="1.5" />
			{:else if normalizedSubtype === 'Pan'}
				<path d="M11 16 H37 V35 Q37 38 34 38 H14 Q11 38 11 35 Z" />
			{:else if normalizedSubtype === 'Hlt'}
				<rect x="14" y="13" width="20" height="26" rx="2" />
			{:else if normalizedSubtype === 'HermsRims'}
				<rect x="15" y="14" width="18" height="24" rx="2" />
			{:else if normalizedSubtype === 'Bucket'}
				<path d="M14 13.5 L16.5 39.5 H31.5 L34 13.5 Z" />
			{:else if normalizedSubtype === 'ConicalFermenter'}
				<path d="M15 12 H33 V26 L24 37 L15 26 Z" />
			{:else if normalizedSubtype === 'Carboy'}
				<path d="M21 14 H27 L33 24 Q35 34 31 40 H17 Q13 34 15 24 Z" />
			{:else if normalizedSubtype === 'PressureFermenter'}
				<path d="M15 11 H33 V25 L24 37 L15 25 Z" />
			{:else if normalizedSubtype === 'StainlessBucket'}
				<path d="M14 13.5 L16.5 35.5 L24 39.5 L31.5 35.5 L34 13.5 Z" />
			{:else if normalizedSubtype === 'Cornelius'}
				<rect x="16" y="13.5" width="16" height="23" rx="1.5" />
			{:else if normalizedSubtype === 'Minikeg'}
				<rect x="14" y="12" width="20" height="28" rx="2" />
			{:else if normalizedSubtype === 'MiniBarrel'}
				<path d="M17.5 9 Q12.5 24 17.5 39 H30.5 Q35.5 24 30.5 9 Z" />
			{:else if normalizedSubtype === 'PetKeg'}
				<path d="M15 13 H33 Q35 13 35 17 V36 Q35 39 32 39 H16 Q13 39 13 36 V17 Q13 13 15 13 Z" />
			{:else if normalizedSubtype === 'Bottle'}
				<path
					d="M22 13 H26 V16 C26 21 31 23 31 27 V40 Q31 41.5 29 41.5 H19 Q17 41.5 17 40 V27 C17 23 22 21 22 16 Z"
				/>
			{:else}
				<rect x="16" y="13" width="16" height="24" rx="1" />
			{/if}
		</clipPath>
	</defs>

	{#if isFillable}
		<!-- Liquid Fill Layer (clipped inside the chamber contour) -->
		<g clip-path="url(#{clipId})">
			<rect
				data-testid="liquid-fill"
				x="0"
				y={liquidY}
				width="48"
				height={liquidHeight}
				fill="url(#{gradId})"
				stroke="none"
				class="transition-all duration-300 ease-out"
			/>
			{#if clampedFill > 0 && clampedFill < 100}
				<!-- Krausen Foam / Liquid Meniscus Line -->
				<line
					x1="0"
					y1={liquidY}
					x2="48"
					y2={liquidY}
					stroke="#fef3c7"
					stroke-width="1.2"
					stroke-linecap="round"
					class="transition-all duration-300 ease-out"
				/>
			{/if}

			{#if effectiveAnimationType === 'boil' && clampedFill > 5}
				<!-- Animated boiling bubbles inside wort -->
				<g class="boil-bubbles">
					<circle cx="20" cy={bounds.bottomY - 3} r="1.5" class="bubble b1" />
					<circle cx="27" cy={bounds.bottomY - 1} r="2" class="bubble b2" />
					<circle cx="23" cy={bounds.bottomY - 5} r="1.2" class="bubble b3" />
					<circle cx="17" cy={bounds.bottomY - 2} r="1.8" class="bubble b4" />
					<circle cx="31" cy={bounds.bottomY - 4} r="1.4" class="bubble b5" />
				</g>
			{:else if effectiveAnimationType === 'ferment' && clampedFill > 5}
				<!-- Animated fermentation CO2 bubbles -->
				<g class="ferment-bubbles">
					<circle cx="21" cy={bounds.bottomY - 3} r="1.2" class="bubble fb1" />
					<circle cx="25" cy={bounds.bottomY - 6} r="1.5" class="bubble fb2" />
					<circle cx="28" cy={bounds.bottomY - 2} r="1" class="bubble fb3" />
					<circle cx="19" cy={bounds.bottomY - 7} r="1.3" class="bubble fb4" />
				</g>
			{:else if effectiveAnimationType === 'mash' && clampedFill > 5}
				<!-- Animated mash convection bubbles -->
				<g class="mash-bubbles">
					<circle cx="22" cy={bounds.bottomY - 3} r="1.4" class="bubble mb1" />
					<circle cx="26" cy={bounds.bottomY - 4} r="1.6" class="bubble mb2" />
				</g>
			{:else if (effectiveAnimationType === 'condition' || effectiveAnimationType === 'package') && clampedFill > 5}
				<!-- Subtle carbonation bubbles -->
				<g class="condition-bubbles">
					<circle cx="22" cy={bounds.bottomY - 4} r="0.9" class="bubble cb1" />
					<circle cx="26" cy={bounds.bottomY - 7} r="0.8" class="bubble cb2" />
				</g>
			{/if}
		</g>
	{/if}

	<!-- Atmospheric Brewing Effects (Steam & Airlock bubbles) -->
	{#if effectiveAnimationType === 'boil' || effectiveAnimationType === 'mash'}
		<g class="steam-wisps" stroke="#fbbf24" stroke-width="1.2" stroke-linecap="round" fill="none">
			<path d="M20 10 Q18 7 20 4" class="steam s1" />
			<path d="M24 9 Q26 6 24 3" class="steam s2" />
			<path d="M28 10 Q26 7 28 4" class="steam s3" />
		</g>
	{:else if effectiveAnimationType === 'ferment'}
		<g class="airlock-bubbles" fill="#34d399">
			{#if normalizedSubtype === 'Bucket'}
				<circle cx="19" cy="5.5" r="1.2" class="airlock-bubble" />
			{:else if normalizedSubtype === 'ConicalFermenter' || normalizedSubtype === 'PressureFermenter' || normalizedSubtype === 'StainlessBucket'}
				<circle cx="24" cy="4.5" r="1.2" class="airlock-bubble" />
			{:else if normalizedSubtype === 'Carboy'}
				<circle cx="24" cy="6.5" r="1.3" class="airlock-bubble" />
			{/if}
		</g>
	{:else if effectiveAnimationType === 'sensor'}
		<!-- Digital Telemetry Emission (0 & 1 bits emitting upward) -->
		<g
			class="digital-bits"
			fill="#06b6d4"
			font-family="ui-monospace, monospace"
			font-size="4"
			font-weight="900"
			text-anchor="middle"
			aria-hidden="true"
		>
			{#if normalizedSubtype === 'Tilt'}
				<text x="18" y="9" class="bit bit-1">1</text>
				<text x="24" y="5" class="bit bit-2">0</text>
				<text x="28" y="7" class="bit bit-3">1</text>
				<text x="22" y="3" class="bit bit-4">0</text>
			{:else if normalizedSubtype === 'ISpindel'}
				<text x="20" y="8" class="bit bit-1">0</text>
				<text x="26" y="5" class="bit bit-2">1</text>
				<text x="22" y="2" class="bit bit-3">0</text>
				<text x="28" y="7" class="bit bit-4">1</text>
			{:else}
				<!-- GenericSensor -->
				<text x="31" y="6" class="bit bit-1">1</text>
				<text x="36" y="4" class="bit bit-2">0</text>
				<text x="33" y="1" class="bit bit-3">1</text>
				<text x="38" y="7" class="bit bit-4">0</text>
			{/if}
		</g>
	{/if}

	<!-- Vessel Foreground Outlined Vector Art -->
	<g class="fill-none stroke-current stroke-2">
		{#if normalizedSubtype === 'AllInOne'}
			<!-- All in One Brewing System (Grainfather, BrewZilla) -->
			<!-- Outer Cylindrical Body -->
			<rect x="14" y="12" width="20" height="28" rx="2" />
			<!-- Recirculation Pipe Arm -->
			<path d="M10 24 V14 Q10 10 14 10 H20" />
			<circle cx="10" cy="24" r="1.5" class="fill-current" />
			<!-- Lid Rim & Handle -->
			<line x1="12" y1="12" x2="36" y2="12" />
			<path d="M21 8 H27 V12 H21 Z" />
			<!-- Digital PID Controller Base -->
			<rect x="18" y="32" width="12" height="6" rx="1" stroke-width="1.5" />
			<line x1="20" y1="35" x2="28" y2="35" stroke-width="1" stroke-dasharray="1.5 1" />
			<!-- Side Handles -->
			<path d="M14 18 H10 V22 H14" />
			<path d="M34 18 H38 V22 H34" />
		{:else if normalizedSubtype === 'Pan'}
			<!-- Pan / Traditional Brew Kettle Stock Pot -->
			<!-- Pot Body -->
			<path d="M10 15 H38 V35 Q38 39 34 39 H14 Q10 39 10 35 Z" />
			<!-- Top Rim -->
			<line x1="8" y1="15" x2="40" y2="15" />
			<!-- Domed Lid & Arch Handle -->
			<path d="M11 15 Q24 9 37 15" />
			<path d="M21 9 Q24 6 27 9" />
			<!-- Dual Loop Handles -->
			<path d="M10 20 H6 Q4 20 4 24 Q4 28 6 28 H10" />
			<path d="M38 20 H42 Q44 20 44 24 Q44 28 42 28 H38" />
			<!-- Ball Valve Drain Port -->
			<path d="M38 33 H43 V37" stroke-width="1.6" />
		{:else if normalizedSubtype === 'Hlt'}
			<!-- Hot Liquor Tank (HLT) -->
			<rect x="13" y="12" width="22" height="27" rx="2" />
			<!-- Sight Glass Scale -->
			<line x1="31" y1="16" x2="31" y2="33" stroke-width="1.5" />
			<circle cx="31" cy="16" r="1" class="fill-current" />
			<circle cx="31" cy="33" r="1" class="fill-current" />
			<!-- Top Lid & Sparge Outlet -->
			<line x1="11" y1="12" x2="37" y2="12" />
			<path d="M21 8 H27 V12 H21 Z" />
			<!-- Temp Probe & Drain Valve -->
			<path d="M13 28 H9" stroke-width="1.8" />
			<path d="M35 33 H40 V37" stroke-width="1.8" />
		{:else if normalizedSubtype === 'HermsRims'}
			<!-- HERMS / RIMS Recirculation Module -->
			<rect x="14" y="13" width="20" height="25" rx="2" />
			<!-- Internal Heat Exchange Coil -->
			<path d="M18 19 Q24 22 30 19" stroke-width="1.5" />
			<path d="M18 25 Q24 28 30 25" stroke-width="1.5" />
			<path d="M18 31 Q24 34 30 31" stroke-width="1.5" />
			<!-- Inlet & Outlet Triclamps -->
			<path d="M14 17 H9 V21 H14" />
			<path d="M34 31 H39 V35 H34" />
			<!-- Top Sensor Probe -->
			<line x1="24" y1="13" x2="24" y2="7" stroke-width="1.8" />
			<circle cx="24" cy="7" r="1.5" class="fill-current" />
		{:else if normalizedSubtype === 'Bucket'}
			<!-- Fermentation Bucket ("Jäshink") -->
			<!-- 1. Tapered Bucket Silhouette Body -->
			<path d="M13 13 L16 40 H32 L35 13 Z" />

			<!-- 2. Reinforced Snap-on Lid Rim with Gasket Seam -->
			<rect x="11" y="9.5" width="26" height="3.5" rx="1.2" />
			<line x1="11" y1="12" x2="37" y2="12" stroke-width="1.2" />

			<!-- 3. Off-Center S-Airlock (Twin-Bubble Bubbler on Left Grommet) -->
			<!-- Lid Grommet Collar -->
			<line x1="17.5" y1="9.5" x2="20.5" y2="9.5" stroke-width="1.8" />
			<!-- Twin-Bubble S-Tube -->
			<path d="M19 9.5 V7 C16.5 7 16.5 4.5 19 4.5 C21.5 4.5 21.5 2 19 2" stroke-width="1.5" />
			<!-- Vented Dust Cap -->
			<line x1="17.5" y1="2" x2="20.5" y2="2" stroke-width="1.4" />

			<!-- 4. Wire Bail Handle with Center Plastic Grip Sleeve -->
			<!-- Side Molded Pivot Lugs -->
			<circle cx="13" cy="16" r="0.8" class="fill-current" stroke="none" />
			<circle cx="35" cy="16" r="0.8" class="fill-current" stroke="none" />
			<!-- Left & Right Wire Span -->
			<path d="M13 16 C13 20 18 22.5 20.5 22.5" stroke-width="1.3" />
			<path d="M35 16 C35 20 30 22.5 27.5 22.5" stroke-width="1.3" />
			<!-- Cylindrical Center Grip Sleeve -->
			<rect x="20.5" y="21.2" width="7" height="2.6" rx="1.3" stroke-width="1.4" />

			<!-- 5. Vertical Center Volume Graduation Markings (25L Scale) -->
			<line x1="24" y1="16.5" x2="24" y2="37" stroke-width="1" stroke-dasharray="1 1.5" />
			<line x1="22" y1="18" x2="26" y2="18" stroke-width="1.3" />
			<line x1="22" y1="26" x2="26" y2="26" stroke-width="1.3" />
			<line x1="22.5" y1="29.5" x2="25.5" y2="29.5" stroke-width="1.2" />
			<line x1="22" y1="33" x2="26" y2="33" stroke-width="1.3" />
			<line x1="22.5" y1="36.5" x2="25.5" y2="36.5" stroke-width="1.2" />

			<!-- 6. Bottom Dispensing Spigot Valve (Tappkran) -->
			<!-- Threaded Shank -->
			<path d="M32.5 35.5 H37" stroke-width="1.8" />
			<!-- Downward Discharge Spout -->
			<path d="M37 35.5 V39.5" stroke-width="1.8" />
			<!-- Wing Valve Handle -->
			<line x1="35.5" y1="33.5" x2="38.5" y2="33.5" stroke-width="1.5" />
		{:else if normalizedSubtype === 'ConicalFermenter'}
			<!-- Conical Fermenter / Unitank -->
			<!-- Cylindrical Chamber -->
			<rect x="14" y="11" width="20" height="15" rx="1" />
			<!-- 60° Cone Bottom -->
			<path d="M14 26 L22 36 V39 H26 V36 L34 26 Z" />
			<!-- Sanitary Butterfly Dump Valve -->
			<line x1="20" y1="41" x2="28" y2="41" stroke-width="2" />
			<line x1="24" y1="39" x2="24" y2="43" stroke-width="2" />
			<!-- Dome Top & Pressure Airlock -->
			<path d="M14 11 Q24 7 34 11" />
			<line x1="24" y1="9" x2="24" y2="4" />
			<circle cx="24" cy="4" r="1.5" class="fill-current" />
			<!-- Tripod Stand Legs & Feet -->
			<path d="M14 23 L9 43 H11" stroke-width="1.8" />
			<path d="M34 23 L39 43 H37" stroke-width="1.8" />
			<line x1="9.5" y1="33" x2="16" y2="30" stroke-width="1.3" />
			<line x1="38.5" y1="33" x2="32" y2="30" stroke-width="1.3" />
			<!-- Sample Valve -->
			<path d="M18 31 H13 V33" stroke-width="1.6" />
		{:else if normalizedSubtype === 'Carboy'}
			<!-- Glass / PET Carboy (Damejeanne) -->
			<!-- Teardrop Jug Body -->
			<path d="M20 13 H28 L34 23 Q36 33 32 40 H16 Q12 33 14 23 Z" />
			<!-- Neck Rim Ring -->
			<line x1="19" y1="13" x2="29" y2="13" stroke-width="1.5" />
			<!-- Bubbler Airlock -->
			<line x1="24" y1="13" x2="24" y2="9" stroke-width="1.5" />
			<circle cx="24" cy="6.5" r="2.5" stroke-width="1.4" />
			<line x1="24" y1="4" x2="24" y2="2" stroke-width="1.4" />
			<!-- Glass Curved Highlight Line -->
			<path d="M18 24 Q16 30 18 35" stroke-width="1.2" stroke-linecap="round" />
		{:else if normalizedSubtype === 'PressureFermenter'}
			<!-- Pressure Fermenter / Spunding Conical -->
			<rect x="14" y="11" width="20" height="14" rx="1" />
			<path d="M14 25 L21 35 H27 L34 25 Z" />
			<!-- Top Spunding Pressure Valve & Posts -->
			<line x1="24" y1="11" x2="24" y2="5" stroke-width="1.8" />
			<circle cx="24" cy="5" r="1.5" class="fill-current" />
			<path d="M18 11 V7 H20" stroke-width="1.5" />
			<path d="M30 11 V7 H28" stroke-width="1.5" />
			<!-- Bottom Yeast Collection Vessel -->
			<rect x="21" y="35" width="6" height="5" rx="1" stroke-width="1.5" />
			<!-- Metal Support Frame -->
			<path d="M14 21 L9 42 H11" stroke-width="1.8" />
			<path d="M34 21 L39 42 H37" stroke-width="1.8" />
		{:else if normalizedSubtype === 'StainlessBucket'}
			<!-- Stainless Steel Flat/Conical Bottom Fermenter -->
			<path d="M13 13 L15.5 34 L24 38.5 L32.5 34 L35 13 Z" />
			<!-- Stainless Clamp Lid -->
			<rect x="11" y="9.5" width="26" height="3.5" rx="1.2" />
			<rect x="11" y="11" width="2" height="3.5" rx="0.5" class="fill-current" />
			<rect x="35" y="11" width="2" height="3.5" rx="0.5" class="fill-current" />
			<!-- Rotating Racking Arm Valve -->
			<path d="M24 35 H29 V37" stroke-width="1.6" />
			<!-- 3 Welded Stand Legs -->
			<path d="M15.5 34 V41" stroke-width="1.8" />
			<path d="M32.5 34 V41" stroke-width="1.8" />
			<!-- Central Airlock -->
			<line x1="24" y1="9.5" x2="24" y2="4" stroke-width="1.5" />
			<circle cx="24" cy="4" r="1.5" class="fill-current" />
		{:else if normalizedSubtype === 'Cornelius'}
			<!-- Cornelius (Corny) Keg -->
			<!-- High-pressure Cylinder -->
			<rect x="15" y="12" width="18" height="25" rx="1.5" />
			<!-- Rubber Molded Top Collar with Handles -->
			<path d="M14 13 V8 Q14 6 18 6 H30 Q34 6 34 8 V13 Z" />
			<rect x="18" y="8" width="12" height="2.5" rx="1" stroke-width="1.2" />
			<!-- Gas & Liquid Ball-Lock Posts -->
			<line x1="17" y1="12" x2="17" y2="9" stroke-width="1.8" />
			<line x1="31" y1="12" x2="31" y2="9" stroke-width="1.8" />
			<!-- Oval Center Access Hatch -->
			<ellipse cx="24" cy="13" rx="3.5" ry="1.2" stroke-width="1.4" />
			<!-- Molded Rubber Bottom Boot -->
			<path d="M14 36 H34 V41 Q34 42 32 42 H16 Q14 42 14 41 Z" />
		{:else if normalizedSubtype === 'Minikeg'}
			<!-- 5L Pressurized Mini Keg -->
			<!-- Ribbed Tank Body -->
			<path d="M15 11 H33 Q35 11 35 15 V37 Q35 41 33 41 H15 Q13 41 13 37 V15 Q13 11 15 11 Z" />
			<!-- Horizontal Rigidity Ribs -->
			<line x1="13" y1="19" x2="35" y2="19" stroke-width="1.5" />
			<line x1="13" y1="26" x2="35" y2="26" stroke-width="1.5" />
			<line x1="13" y1="33" x2="35" y2="33" stroke-width="1.5" />
			<!-- Metal Carry Loop Handle -->
			<path d="M19 11 V7 Q19 5 24 5 Q29 5 29 7 V11" stroke-width="1.6" />
			<!-- Top Bung / Regulator Cap -->
			<rect x="22" y="9" width="4" height="2" rx="0.5" class="fill-current" />
			<!-- Dispense Tap Spigot -->
			<path d="M22 36 H26 V38 H22 Z" stroke-width="1.2" />
		{:else if normalizedSubtype === 'MiniBarrel'}
			<!-- Traditional Wood Cask / Aging Mini Barrel -->
			<!-- Bulging Wooden Staves Silhouette -->
			<path d="M17 8 Q11 24 17 40 H31 Q37 24 31 8 Z" />
			<!-- Top & Bottom Chimes -->
			<line x1="17" y1="8" x2="31" y2="8" stroke-width="2.2" />
			<line x1="17" y1="40" x2="31" y2="40" stroke-width="2.2" />
			<!-- Steel Hoops / Rings -->
			<path d="M15 15 Q24 18 33 15" stroke-width="1.8" />
			<path d="M13 24 Q24 27 35 24" stroke-width="1.8" />
			<path d="M15 33 Q24 36 33 33" stroke-width="1.8" />
			<!-- Center Bunghole -->
			<circle cx="24" cy="24" r="1.8" class="fill-current" />
			<!-- Front Wooden Spigot / Tap -->
			<path d="M24 33 V36 H26" stroke-width="1.5" />
		{:else if normalizedSubtype === 'PetKeg'}
			<!-- PET Pressure Keg (Oxebar / Snub Nose) -->
			<!-- Ribbed Bottle Body -->
			<path d="M15 12 H33 Q35 12 35 16 V35 Q35 39 31 39 H17 Q13 39 13 35 V16 Q13 12 15 12 Z" />
			<!-- Reinforcement Ribs -->
			<line x1="13" y1="20" x2="35" y2="20" stroke-width="1.4" />
			<line x1="13" y1="28" x2="35" y2="28" stroke-width="1.4" />
			<!-- PCO Cap with Posts -->
			<rect x="20" y="8" width="8" height="4" rx="1" stroke-width="1.4" />
			<line x1="22" y1="8" x2="22" y2="5" stroke-width="1.8" />
			<line x1="26" y1="8" x2="26" y2="5" stroke-width="1.8" />
			<!-- Petalloid Base Feet -->
			<path d="M15 39 L14 42 H18 L19 39" stroke-width="1.3" />
			<path d="M33 39 L34 42 H30 L29 39" stroke-width="1.3" />
		{:else if normalizedSubtype === 'ISpindel'}
			<!-- iSpindel Wireless Digital Hydrometer (PET Preform Capsule) -->
			<g transform="rotate(20 24 24)">
				<!-- Threaded Soda Bottle Screw Cap -->
				<rect x="18" y="6" width="12" height="5" rx="1" stroke-width="1.8" class="fill-current" />
				<!-- Cap Gripping Ribs -->
				<line
					x1="21"
					y1="7"
					x2="21"
					y2="10"
					stroke-width="0.8"
					stroke="white"
					stroke-linecap="butt"
				/>
				<line
					x1="24"
					y1="7"
					x2="24"
					y2="10"
					stroke-width="0.8"
					stroke="white"
					stroke-linecap="butt"
				/>
				<line
					x1="27"
					y1="7"
					x2="27"
					y2="10"
					stroke-width="0.8"
					stroke="white"
					stroke-linecap="butt"
				/>
				<!-- Preform Bottle Neck Ring / Support Flange -->
				<line x1="16.5" y1="12" x2="31.5" y2="12" stroke-width="1.8" />
				<!-- Transparent PET Preform Tube Body with Hemispherical Bottom -->
				<path d="M18.5 12 V32 C18.5 37 29.5 37 29.5 32 V12" stroke-width="1.8" />
				<!-- Internal 18650 Li-Ion Battery Cylinder -->
				<rect x="21" y="15" width="6" height="12" rx="1" stroke-width="1.2" />
				<rect x="23" y="14" width="2" height="1" rx="0.5" stroke-width="0.8" class="fill-current" />
				<!-- Microcontroller & Gyroscope Sensor PCB Sled -->
				<rect x="21.5" y="28.5" width="5" height="4" rx="0.5" stroke-width="1" />
				<!-- Lead/Ballast Flotation Weight -->
				<path d="M20.5 33.5 Q24 35.5 27.5 33.5" stroke-width="1.4" class="fill-current" />
			</g>
		{:else if normalizedSubtype === 'Tilt'}
			<!-- Tilt Hydrometer (Sleek Seamless Floating Hydrometer) -->
			<!-- Ambient Floating Surface Waves -->
			<path d="M7 32 Q11 30 15 32 T23 32" stroke-width="1.2" stroke-dasharray="2 2" opacity="0.5" />
			<path
				d="M32 32 Q36 34 40 32 T45 32"
				stroke-width="1.2"
				stroke-dasharray="2 2"
				opacity="0.5"
			/>
			<!-- Tilted Floating Hydrometer Cylinder -->
			<g transform="rotate(-22 24 24)">
				<!-- Sleek Seamless Tube Body with Beveled Ends -->
				<rect x="19" y="8" width="10" height="32" rx="4.5" stroke-width="2" />
				<!-- Top Hex / Beveled Cap Accent -->
				<line x1="20.5" y1="12" x2="27.5" y2="12" stroke-width="1" opacity="0.6" />
				<!-- Iconic Tilt BLE Beacon LED (blinks during reading) -->
				<circle cx="24" cy="15.5" r="1.5" stroke-width="1" class="fill-current" />
				<!-- Internal Circuit Board Strip -->
				<line x1="24" y1="19" x2="24" y2="28" stroke-width="1.5" stroke-dasharray="2 1.5" />
				<!-- Precision Calibration / Scale Reference Markings -->
				<line x1="21.5" y1="22" x2="23" y2="22" stroke-width="0.9" />
				<line x1="21.5" y1="25" x2="23" y2="25" stroke-width="0.9" />
				<!-- Solid Ballast Weight at Bottom -->
				<path
					d="M20.5 31 H27.5 V35.5 Q27.5 38.5 24 38.5 Q20.5 38.5 20.5 35.5 Z"
					stroke-width="1.2"
					class="fill-current"
				/>
			</g>
		{:else if normalizedSubtype === 'GenericSensor'}
			<!-- Generic Digital Temperature Probe & Telemetry Sensor -->
			<!-- Wireless Telemetry Signal Waves (top right) -->
			<path d="M33 7 Q36 10 33 13" stroke-width="1.4" stroke-linecap="round" class="opacity-75" />
			<path d="M36 4 Q41 10 36 16" stroke-width="1.4" stroke-linecap="round" class="opacity-50" />
			<!-- Probe Cable / Wire with Strain Relief Boot -->
			<path d="M24 8 V4 Q24 2 28 2" stroke-width="1.8" fill="none" stroke-linecap="round" />
			<!-- Sensor Transmitter Head / Enclosure Body -->
			<rect x="16" y="8" width="16" height="13" rx="2.5" stroke-width="2" />
			<!-- Digital Display Screen / Temperature Pulse -->
			<rect x="19" y="11" width="10" height="6.5" rx="1" stroke-width="1.2" />
			<path d="M21 14.5 H22.5 L24 12.5 L25.5 16 L27 14.5 H28" stroke-width="1" fill="none" />
			<!-- Threaded Fitting / Hex Collar / Thermowell Nut -->
			<path d="M19.5 21 H28.5 V24 H19.5 Z" stroke-width="1.5" class="fill-current" />
			<!-- Stainless Steel Immersion Probe Stem -->
			<line x1="24" y1="24" x2="24" y2="43" stroke-width="2.5" stroke-linecap="round" />
			<!-- Immersion Depth Ring Markers on Probe -->
			<line x1="22.5" y1="31" x2="25.5" y2="31" stroke-width="1" />
			<line x1="22.5" y1="36" x2="25.5" y2="36" stroke-width="1" />
			<!-- Sensitive Measurement Probe Tip -->
			<circle cx="24" cy="43" r="1.3" stroke-width="1" class="fill-current" />
		{:else if normalizedSubtype === 'Bottle'}
			<!-- Craft Beer Bottle with Crown Cap -->
			<!-- Bottle Outer Silhouette -->
			<path
				d="M22 10 V16 C22 21 17 23 17 27 V40 Q17 42 19 42 H29 Q31 42 31 40 V27 C31 23 26 21 26 16 V10"
				stroke-width="2"
			/>
			<!-- Crown Cap Lip / Flange -->
			<line x1="21" y1="10" x2="27" y2="10" stroke-width="1.8" />
			<!-- Crown Cap -->
			<path
				d="M21.5 10 L21 7.5 Q21 6.5 22.5 6.5 H25.5 Q27 6.5 27 7.5 L26.5 10"
				stroke-width="1.6"
				class="fill-current"
			/>
			<!-- Cap Crimp Details -->
			<line
				x1="23"
				y1="10"
				x2="22.5"
				y2="8.5"
				stroke-width="0.8"
				stroke="white"
				stroke-linecap="butt"
			/>
			<line
				x1="25"
				y1="10"
				x2="25.5"
				y2="8.5"
				stroke-width="0.8"
				stroke="white"
				stroke-linecap="butt"
			/>
			<!-- Glass Reflection Highlight -->
			<path d="M19.5 28 V38" stroke-width="1.2" stroke-linecap="round" opacity="0.6" />
			<!-- Bottle Label -->
			<rect x="19.5" y="27" width="9" height="9" rx="1" stroke-width="1.2" opacity="0.8" />
			<line x1="22" y1="31.5" x2="26" y2="31.5" stroke-width="1" opacity="0.6" />
		{:else}
			<!-- Fallback Generic Container -->
			<rect x="15" y="12" width="18" height="26" rx="2" />
			<line x1="13" y1="12" x2="35" y2="12" />
			<path d="M20 9 H28 V12 H20 Z" />
		{/if}
	</g>
</svg>

<style>
	/* Boiling bubbles */
	.boil-bubbles .bubble {
		fill: #fef3c7;
		animation: boil-rise 1.2s infinite ease-in;
	}
	.boil-bubbles .b1 {
		animation-delay: 0s;
		animation-duration: 0.9s;
	}
	.boil-bubbles .b2 {
		animation-delay: 0.25s;
		animation-duration: 1.1s;
	}
	.boil-bubbles .b3 {
		animation-delay: 0.5s;
		animation-duration: 0.8s;
	}
	.boil-bubbles .b4 {
		animation-delay: 0.7s;
		animation-duration: 1s;
	}
	.boil-bubbles .b5 {
		animation-delay: 0.35s;
		animation-duration: 1.2s;
	}

	@keyframes boil-rise {
		0% {
			transform: translateY(0) scale(0.6);
			opacity: 0.3;
		}
		50% {
			opacity: 0.9;
		}
		100% {
			transform: translateY(-22px) scale(1.3);
			opacity: 0;
		}
	}

	/* Fermentation CO2 bubbles */
	.ferment-bubbles .bubble {
		fill: #ecfdf5;
		animation: ferment-rise 2s infinite ease-in-out;
	}
	.ferment-bubbles .fb1 {
		animation-delay: 0s;
		animation-duration: 2.2s;
	}
	.ferment-bubbles .fb2 {
		animation-delay: 0.6s;
		animation-duration: 1.8s;
	}
	.ferment-bubbles .fb3 {
		animation-delay: 1.2s;
		animation-duration: 2.4s;
	}
	.ferment-bubbles .fb4 {
		animation-delay: 0.3s;
		animation-duration: 2s;
	}

	@keyframes ferment-rise {
		0% {
			transform: translateY(0) scale(0.7);
			opacity: 0.2;
		}
		40% {
			opacity: 0.85;
			transform: translateY(-10px) scale(1);
		}
		100% {
			transform: translateY(-22px) scale(1.2);
			opacity: 0;
		}
	}

	/* Airlock bubble */
	.airlock-bubble {
		animation: airlock-pulse 1.4s infinite cubic-bezier(0.4, 0, 0.2, 1);
		transform-origin: center;
	}

	@keyframes airlock-pulse {
		0% {
			transform: scale(0.5);
			opacity: 0.2;
		}
		50% {
			transform: scale(1.3);
			opacity: 1;
		}
		100% {
			transform: scale(0.5);
			opacity: 0.2;
		}
	}

	/* Steam wisps */
	.steam-wisps .steam {
		animation: steam-drift 1.8s infinite ease-out;
	}
	.steam-wisps .s1 {
		animation-delay: 0s;
	}
	.steam-wisps .s2 {
		animation-delay: 0.6s;
	}
	.steam-wisps .s3 {
		animation-delay: 1.2s;
	}

	@keyframes steam-drift {
		0% {
			transform: translateY(0) scaleX(0.8);
			opacity: 0;
		}
		40% {
			opacity: 0.75;
		}
		100% {
			transform: translateY(-7px) scaleX(1.3);
			opacity: 0;
		}
	}

	/* Mash & Condition bubbles */
	.mash-bubbles .bubble {
		fill: #fef3c7;
		animation: boil-rise 1.8s infinite ease-in;
	}
	.condition-bubbles .bubble {
		fill: #e0f2fe;
		animation: ferment-rise 3s infinite ease-in-out;
	}

	/* Digital 0 & 1 Telemetry Emission */
	.digital-bits .bit {
		animation: bit-drift 2.2s infinite ease-out;
		opacity: 0;
	}
	.digital-bits .bit-1 {
		animation-delay: 0s;
		animation-duration: 2s;
	}
	.digital-bits .bit-2 {
		animation-delay: 0.55s;
		animation-duration: 2.3s;
	}
	.digital-bits .bit-3 {
		animation-delay: 1.1s;
		animation-duration: 1.9s;
	}
	.digital-bits .bit-4 {
		animation-delay: 1.65s;
		animation-duration: 2.4s;
	}

	@keyframes bit-drift {
		0% {
			transform: translateY(3px) scale(0.6);
			opacity: 0;
		}
		25% {
			opacity: 0.95;
		}
		70% {
			opacity: 0.7;
		}
		100% {
			transform: translateY(-9px) scale(1.1);
			opacity: 0;
		}
	}

	@media (prefers-reduced-motion: reduce) {
		.bubble,
		.steam,
		.airlock-bubble,
		.digital-bits .bit {
			animation: none !important;
		}
	}
</style>
