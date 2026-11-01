<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { api, ApiClientError, resolveApiUrl } from '$lib/api/client';
	import { t } from '$lib/i18n/index.svelte';
	import type { TapDisplayManifestDto } from '$lib/types/api';
	import { Beer, Loader2, AlertCircle } from '@lucide/svelte';

	let token = $derived(page.params.token);
	let manifest = $state<TapDisplayManifestDto | null>(null);
	let loading = $state(true);
	let errorMessage = $state<string | null>(null);
	let pollInterval: ReturnType<typeof setInterval> | null = null;

	async function fetchManifest() {
		const currentToken = token;
		if (!currentToken) {
			loading = false;
			return;
		}
		try {
			const data = await api.tap.getManifest(currentToken);
			manifest = data;
			errorMessage = null;
		} catch (err) {
			if (err instanceof ApiClientError) {
				errorMessage = err.message;
			} else {
				errorMessage = err instanceof Error ? err.message : t('common.error_generic');
			}
		} finally {
			loading = false;
		}
	}

	onMount(() => {
		fetchManifest();
		// Poll every 60s for live batch changes
		pollInterval = setInterval(fetchManifest, 60000);
	});

	onDestroy(() => {
		if (pollInterval) {
			clearInterval(pollInterval);
			pollInterval = null;
		}
	});

	let isRound = $derived(manifest?.shape === 'Round');
	let isPortrait = $derived((manifest?.heightPx ?? 0) > (manifest?.widthPx ?? 0));
	let isAssigned = $derived(manifest?.status === 'Active' && !!manifest.beerName);
</script>

<svelte:head>
	<title
		>{manifest?.beerName ? `${manifest.beerName} | Tap Display` : 'Digital Tap Display'} — BrewYou</title
	>
	<meta
		name="viewport"
		content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no"
	/>
</svelte:head>

<div
	class="kiosk-container flex min-h-screen w-full items-center justify-center bg-black font-sans text-white select-none"
>
	{#if loading}
		<div class="flex flex-col items-center justify-center space-y-3">
			<Loader2 class="h-10 w-10 animate-spin text-amber-500" />
			<span class="font-mono text-xs tracking-widest text-zinc-500 uppercase"
				>{t('taps.kiosk_connecting')}</span
			>
		</div>
	{:else if errorMessage}
		<div class="flex max-w-md flex-col items-center justify-center p-6 text-center">
			<AlertCircle class="h-12 w-12 text-red-500 opacity-80" />
			<h2 class="mt-4 text-lg font-bold text-white">{t('taps.kiosk_error_title')}</h2>
			<p class="mt-1 text-xs text-zinc-400">{errorMessage}</p>
			<span class="mt-4 font-mono text-[10px] text-zinc-600">TOKEN: {token}</span>
		</div>
	{:else if manifest}
		{#if isAssigned && manifest.beerName}
			<!-- Active Pouring Batch State -->
			{#if isRound}
				<!-- Round Screen Layout (e.g. Circular LCD / OLED Tap Bezel) -->
				<div
					class="relative flex aspect-square h-full max-h-[min(100vh,100vw)] w-full max-w-[min(100vh,100vw)] items-center justify-center overflow-hidden rounded-full border-4 border-zinc-900 bg-zinc-950 shadow-[0_0_80px_rgba(0,0,0,0.9)]"
				>
					<!-- High-Res Background Image -->
					{#if manifest.imageUrl}
						<img
							src={resolveApiUrl(manifest.imageUrl)}
							alt={manifest.beerName}
							class="absolute inset-0 h-full w-full object-cover"
						/>
					{/if}

					<!-- Gradient Mask to highlight typography -->
					<div
						class="absolute inset-0 bg-gradient-to-t from-black/85 via-black/20 to-black/40"
					></div>

					<!-- Circular Content Overlay -->
					<div
						class="absolute inset-x-0 bottom-6 flex flex-col items-center px-8 text-center sm:bottom-12"
					>
						<h1
							class="line-clamp-2 font-editorial text-2xl font-bold tracking-tight text-white drop-shadow-md sm:text-4xl"
						>
							{manifest.beerName}
						</h1>

						{#if manifest.actualAbv != null}
							<div
								class="mt-3 inline-flex items-center gap-1.5 rounded-full border border-amber-500/40 bg-black/80 px-4 py-1 backdrop-blur-md"
							>
								<span
									class="font-mono text-sm font-black tracking-wide text-amber-400 sm:text-base"
								>
									{manifest.actualAbv.toFixed(1)}% ABV
								</span>
							</div>
						{/if}
					</div>
				</div>
			{:else}
				<!-- Rectangular High-Res Screen Layout -->
				<div
					class="relative flex h-screen w-screen overflow-hidden bg-zinc-950 {isPortrait
						? 'flex-col justify-between p-8'
						: 'flex-row items-center p-8 lg:p-16'}"
				>
					<!-- Blurred Ambient Backdrop -->
					{#if manifest.imageUrl}
						<div
							class="absolute inset-0 bg-cover bg-center opacity-30 blur-2xl"
							style="background-image: url({resolveApiUrl(manifest.imageUrl)});"
						></div>
					{/if}

					<!-- Artwork Panel -->
					<div class="relative z-10 flex flex-1 items-center justify-center p-4">
						<div
							class="overflow-hidden rounded-3xl border border-white/10 bg-zinc-900 shadow-2xl {isPortrait
								? 'h-[50vh] w-full max-w-sm'
								: 'h-[75vh] w-full max-w-2xl'}"
						>
							{#if manifest.imageUrl}
								<img
									src={resolveApiUrl(manifest.imageUrl)}
									alt={manifest.beerName}
									class="h-full w-full object-cover"
								/>
							{/if}
						</div>
					</div>

					<!-- Details & Vitals Panel -->
					<div
						class="relative z-10 flex flex-1 flex-col justify-center space-y-5 px-6 text-left lg:px-12"
					>
						<div>
							<h1
								class="mt-3 font-editorial text-4xl font-extrabold tracking-tight text-white lg:text-6xl"
							>
								{manifest.beerName}
							</h1>
						</div>

						<!-- Big Actual ABV Metric -->
						{#if manifest.actualAbv != null}
							<div class="flex items-baseline gap-3">
								<span class="font-mono text-5xl font-black text-amber-400 lg:text-7xl">
									{manifest.actualAbv.toFixed(1)}%
								</span>
								<span class="text-xl font-bold tracking-widest text-zinc-400 uppercase"> ABV </span>
							</div>
						{/if}
					</div>
				</div>
			{/if}
		{:else}
			<!-- Standby / Idle Tap State -->
			<div class="flex flex-col items-center justify-center space-y-4 p-8 text-center">
				<div
					class="flex h-24 w-24 items-center justify-center rounded-3xl border border-zinc-800 bg-zinc-900 shadow-xl"
				>
					<Beer class="h-12 w-12 animate-pulse text-amber-500" />
				</div>
				<h1 class="font-editorial text-3xl font-bold text-white sm:text-4xl">Tap Display</h1>
				<p class="text-sm font-medium tracking-wider text-zinc-400 uppercase">
					{t('taps.kiosk_tap_available')}
				</p>
			</div>
		{/if}
	{/if}
</div>

<style>
	:global(body) {
		background: black;
		overflow: hidden;
	}
</style>
