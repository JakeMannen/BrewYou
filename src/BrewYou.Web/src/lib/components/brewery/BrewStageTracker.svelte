<script module lang="ts">
	import type { EquipmentSubtype } from '$lib/types/api';

	export type StageStatus = 'pending' | 'in_progress' | 'completed' | 'skipped';

	export interface BrewStage {
		id: string;
		label: string;
		status: StageStatus;
		timestamp?: string;
		estimatedMinutes?: number;
		equipmentSubtype?: EquipmentSubtype | 'Bottle' | string;
		fillPercent?: number;
		equipmentName?: string;
	}

	export function getDefaultEquipmentSubtype(
		stageId: string
	): EquipmentSubtype | 'Bottle' | string {
		switch (stageId.toLowerCase()) {
			case 'mash':
				return 'AllInOne';
			case 'boil':
				return 'Pan';
			case 'ferment':
				return 'ConicalFermenter';
			case 'condition':
				return 'Carboy';
			case 'package':
				return 'Bottle';
			default:
				return 'Other';
		}
	}

	export function getDefaultFillPercent(stageId: string): number {
		switch (stageId.toLowerCase()) {
			case 'mash':
				return 75;
			case 'boil':
				return 85;
			case 'ferment':
				return 80;
			case 'condition':
				return 80;
			case 'package':
				return 90;
			default:
				return 50;
		}
	}
</script>

<script lang="ts">
	import {
		Check,
		Loader2,
		Circle,
		FastForward,
		Flame,
		Droplets,
		Thermometer,
		Clock,
		PackageCheck
	} from '@lucide/svelte';
	import VesselIcon from '$lib/components/inventory/VesselIcon.svelte';

	interface Props {
		stages?: BrewStage[];
		activeStageId?: string;
		currentStageId?: string;
		selectedStageId?: string;
		interactive?: boolean;
		onStageSelect?: (stage: BrewStage) => void;
		class?: string;
	}

	let {
		stages = [
			{ id: 'mash', label: 'Mash', status: 'completed' },
			{ id: 'boil', label: 'Boil', status: 'in_progress' },
			{ id: 'ferment', label: 'Ferment', status: 'pending' },
			{ id: 'condition', label: 'Condition', status: 'pending' },
			{ id: 'package', label: 'Package', status: 'pending' }
		],
		activeStageId,
		currentStageId,
		selectedStageId,
		interactive = false,
		onStageSelect,
		class: className = ''
	}: Props = $props();

	const effectiveActiveId = $derived(activeStageId ?? currentStageId);

	function handleStageClick(stage: BrewStage) {
		if (interactive && onStageSelect) {
			onStageSelect(stage);
		}
	}

	function handleKeyDown(event: KeyboardEvent, index: number) {
		if (!interactive || !onStageSelect) return;
		let targetIndex: number | null = null;
		if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
			event.preventDefault();
			targetIndex = (index + 1) % stages.length;
		} else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
			event.preventDefault();
			targetIndex = (index - 1 + stages.length) % stages.length;
		} else if (event.key === 'Home') {
			event.preventDefault();
			targetIndex = 0;
		} else if (event.key === 'End') {
			event.preventDefault();
			targetIndex = stages.length - 1;
		}

		if (targetIndex !== null) {
			onStageSelect(stages[targetIndex]);
			const container = (event.currentTarget as HTMLElement).closest('ol');
			const buttons = container?.querySelectorAll<HTMLButtonElement>('button');
			buttons?.[targetIndex]?.focus();
		}
	}
</script>

<nav
	aria-label="Brewing Progress"
	data-testid="brew-stage-tracker"
	class="w-full px-2 py-4 {className}"
>
	<ol class="relative flex w-full items-start justify-between">
		{#each stages as stage, index (stage.id)}
			{@const isActive =
				stage.id === effectiveActiveId || (stage.status === 'in_progress' && !effectiveActiveId)}
			{@const isSelected = stage.id === selectedStageId}
			{@const isCompleted = stage.status === 'completed'}
			{@const isSkipped = stage.status === 'skipped'}
			{@const showEquipment = isActive || isCompleted}
			{@const eqSubtype = stage.equipmentSubtype || getDefaultEquipmentSubtype(stage.id)}
			{@const fill = stage.fillPercent ?? getDefaultFillPercent(stage.id)}

			<li class="group relative flex flex-1 flex-col items-center">
				<!-- Equipment Icon Container: Displayed only for active or completed/finished steps -->
				<div class="mb-1.5 flex h-8 w-8 items-center justify-center sm:h-9 sm:w-9">
					{#if showEquipment}
						<div
							class="transition-all duration-300 {isActive
								? 'scale-105'
								: 'opacity-85 hover:opacity-100'}"
							title="{stage.equipmentName ?? stage.label}: {Math.round(fill)}%"
						>
							<VesselIcon
								subtype={eqSubtype}
								fillPercent={fill}
								size={32}
								active={isActive}
								animationStage={stage.id.toLowerCase()}
								showMeter={false}
							/>
						</div>
					{/if}
				</div>

				<!-- Node Circle Container with Connecting Line -->
				<div class="relative flex h-8 w-full items-center justify-center sm:h-9">
					<!-- Connecting progress line between nodes -->
					{#if index < stages.length - 1}
						<div
							class="absolute top-1/2 left-1/2 z-0 h-0.5 w-full -translate-y-1/2 transition-colors duration-500
							{isCompleted ? 'bg-hops-500' : 'bg-zinc-200 dark:bg-zinc-800'}"
							aria-hidden="true"
						></div>
					{/if}

					<!-- Node Icon Button -->
					<button
						type="button"
						data-testid="stage-node-{stage.id.toLowerCase()}"
						disabled={!interactive}
						onclick={() => handleStageClick(stage)}
						onkeydown={(e) => handleKeyDown(e, index)}
						aria-current={isActive ? 'step' : undefined}
						aria-pressed={isSelected}
						class="relative z-10 flex h-8 w-8 items-center justify-center rounded-full border-2 transition-all duration-300 sm:h-9 sm:w-9
						{interactive
							? 'cursor-pointer hover:scale-105 focus-visible:ring-2 focus-visible:ring-amber-500 focus-visible:ring-offset-2 focus-visible:outline-none'
							: 'cursor-default'}
						{isSelected
							? 'scale-105 shadow-md ring-2 ring-amber-500 ring-offset-2 ring-offset-white dark:ring-offset-zinc-950'
							: ''}
						{isCompleted
							? 'border-hops-500 bg-emerald-100 text-emerald-700 shadow-hops-glow dark:bg-emerald-950/80 dark:text-hops-400'
							: isActive
								? 'border-amber-500 bg-amber-50 text-amber-600 shadow-amber-glow ring-4 ring-amber-400/20 dark:border-amber-400 dark:bg-zinc-900 dark:text-amber-400'
								: isSkipped
									? 'border-zinc-400 bg-zinc-200 text-zinc-600 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-400'
									: 'border-zinc-300 bg-zinc-100 text-zinc-400 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-600'}"
						aria-label="{stage.label}: {stage.status}{isActive ? ' (Active)' : ''}{isSelected
							? ' (Selected)'
							: ''}"
					>
						{#if isCompleted}
							<Check class="h-4 w-4 stroke-[3]" />
						{:else if isActive}
							{#if stage.id.toLowerCase() === 'boil'}
								<Flame class="h-4 w-4 animate-bounce text-amber-600 dark:text-amber-400" />
							{:else if stage.id.toLowerCase() === 'ferment'}
								<Droplets class="h-4 w-4 animate-bounce text-hops-600 dark:text-hops-400" />
							{:else if stage.id.toLowerCase() === 'mash'}
								<Thermometer class="h-4 w-4 animate-pulse text-amber-600 dark:text-amber-400" />
							{:else if stage.id.toLowerCase() === 'condition'}
								<Clock
									class="h-4 w-4 animate-spin text-sky-600 [animation-duration:8s] dark:text-sky-400"
								/>
							{:else if stage.id.toLowerCase() === 'package'}
								<PackageCheck class="h-4 w-4 animate-pulse text-purple-600 dark:text-purple-400" />
							{:else}
								<Loader2 class="h-4 w-4 animate-spin" />
							{/if}
						{:else if isSkipped}
							<FastForward class="h-3.5 w-3.5" />
						{:else}
							<Circle class="h-2.5 w-2.5 fill-current" />
						{/if}
					</button>
				</div>

				<!-- Stage Label -->
				<div class="mt-2 flex flex-col items-center text-center">
					<span
						class="text-xs font-semibold tracking-wide transition-colors
						{isActive
							? 'font-bold text-amber-600 dark:text-amber-400'
							: isSelected
								? 'font-bold text-zinc-950 underline decoration-amber-500 decoration-2 underline-offset-4 dark:text-white'
								: isCompleted
									? 'text-zinc-800 dark:text-zinc-200'
									: 'text-zinc-400 dark:text-zinc-500'}"
					>
						{stage.label}
					</span>
					{#if stage.timestamp}
						<span class="font-mono text-[10px] text-zinc-500">{stage.timestamp}</span>
					{/if}
				</div>
			</li>
		{/each}
	</ol>
</nav>
