<script module lang="ts">
	import type { BatchIngredientDto } from '$lib/types/api';

	export function calculateProgress(totalSeconds: number, remainingSeconds: number): number {
		const effectiveTotal = Math.max(totalSeconds, remainingSeconds, 1);
		const elapsed = Math.max(0, effectiveTotal - Math.max(0, remainingSeconds));
		return Math.min(100, Math.max(0, (elapsed / effectiveTotal) * 100));
	}

	export function calculateMilestonePosition(
		effectiveTotalSeconds: number,
		additionMinutes: number
	): number {
		if (effectiveTotalSeconds <= 0) return 100;
		const targetElapsedSeconds = Math.max(
			0,
			effectiveTotalSeconds - Math.max(0, additionMinutes * 60)
		);
		return Math.min(100, Math.max(0, (targetElapsedSeconds / effectiveTotalSeconds) * 100));
	}

	export interface MilestoneGroup {
		additionTimeMinutes: number;
		positionPercentage: number;
		ingredients: BatchIngredientDto[];
		allChecked: boolean;
		isDue: boolean;
	}

	export function groupMilestones(
		ingredients: BatchIngredientDto[],
		effectiveTotalSeconds: number,
		remainingSeconds: number
	): MilestoneGroup[] {
		if (!ingredients || ingredients.length === 0) return [];

		const map: Record<number, BatchIngredientDto[]> = {};
		for (const ing of ingredients) {
			const time = ing.additionTimeMinutes ?? 0;
			if (!map[time]) {
				map[time] = [];
			}
			map[time].push(ing);
		}

		const groups: MilestoneGroup[] = [];
		for (const [key, items] of Object.entries(map)) {
			const additionTimeMinutes = Number(key);
			const positionPercentage = calculateMilestonePosition(
				effectiveTotalSeconds,
				additionTimeMinutes
			);
			const allChecked = items.every((i) => i.isChecked);
			const isDue = remainingSeconds <= additionTimeMinutes * 60;
			groups.push({
				additionTimeMinutes,
				positionPercentage,
				ingredients: items,
				allChecked,
				isDue
			});
		}

		groups.sort((a, b) => a.positionPercentage - b.positionPercentage);
		return groups;
	}

	export function formatProgressTime(totalSeconds: number): string {
		const safeSec = Math.max(0, Math.floor(totalSeconds));
		const hours = Math.floor(safeSec / 3600);
		const minutes = Math.floor((safeSec % 3600) / 60);
		const seconds = safeSec % 60;

		if (hours > 0) {
			return `${hours}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
		}
		return `${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
	}
</script>

<script lang="ts">
	import type { BrewStage } from '$lib/types/api';
	import { t } from '$lib/i18n/index.svelte';
	import { Check, Flame, X } from '@lucide/svelte';

	interface Props {
		totalSeconds: number;
		remainingSeconds: number;
		running: boolean;
		stage: BrewStage;
		milestones?: BatchIngredientDto[];
		onToggleMilestone?: (ingredientId: string, currentChecked: boolean) => void;
		variant?: 'default' | 'hud';
	}

	let {
		totalSeconds,
		remainingSeconds,
		running,
		stage,
		milestones = [],
		onToggleMilestone,
		variant = 'default'
	}: Props = $props();

	let selectedGroupMinutes = $state<number | null>(null);

	const effectiveTotal = $derived(Math.max(totalSeconds, remainingSeconds, 1));
	const elapsedSeconds = $derived(Math.max(0, effectiveTotal - Math.max(0, remainingSeconds)));
	const progress = $derived(calculateProgress(totalSeconds, remainingSeconds));
	const isComplete = $derived(remainingSeconds === 0);

	const milestoneGroups = $derived(
		stage === 'Boil' && milestones.length > 0
			? groupMilestones(milestones, effectiveTotal, remainingSeconds)
			: []
	);

	const selectedGroup = $derived(
		selectedGroupMinutes !== null
			? (milestoneGroups.find((g) => g.additionTimeMinutes === selectedGroupMinutes) ?? null)
			: null
	);

	function handleTogglePin(minutes: number) {
		selectedGroupMinutes = selectedGroupMinutes === minutes ? null : minutes;
	}
</script>

<div
	data-testid="stage-progress-bar-container"
	class="w-full select-none {variant === 'hud' ? 'my-3 max-w-4xl' : 'my-2'}"
>
	<!-- Progress Header: Labels & Numeric Telemetry -->
	<div
		class="mb-1.5 flex items-center justify-between font-mono {variant === 'hud'
			? 'text-xs text-zinc-300 sm:text-sm'
			: 'text-xs text-zinc-600 dark:text-zinc-400'}"
	>
		<span class="flex items-center gap-1.5 font-semibold">
			<span
				>{t('batches.workspace.progress_title', {
					stage: t(`batches.stages.${stage.toLowerCase()}`)
				})}</span
			>
			{#if isComplete}
				<span
					class="py-0.2 rounded-md bg-amber-500/20 px-1.5 text-[10px] font-bold text-amber-400 uppercase"
				>
					{t('batches.workspace.milestone_completed')}
				</span>
			{/if}
		</span>
		<div class="flex items-center gap-2">
			<span>{formatProgressTime(elapsedSeconds)} / {formatProgressTime(effectiveTotal)}</span>
			<span class="font-bold text-amber-500 dark:text-amber-400">
				{Math.round(progress)}%
			</span>
		</div>
	</div>

	<!-- Track Container with Milestone Markers -->
	<div class="relative w-full pt-2 pb-3">
		<!-- Progress Track -->
		<div
			role="progressbar"
			aria-valuenow={Math.round(progress)}
			aria-valuemin="0"
			aria-valuemax="100"
			aria-label={t('batches.workspace.progress_bar_label')}
			data-testid="stage-progress-track"
			class="relative w-full overflow-hidden rounded-full transition-all {variant === 'hud'
				? 'h-3.5 border border-white/10 bg-zinc-800 shadow-inner sm:h-4'
				: 'h-2.5 bg-zinc-200 shadow-inner dark:bg-zinc-800'}"
		>
			<div
				data-testid="stage-progress-fill"
				style="width: {progress}%;"
				class="h-full rounded-full transition-all duration-300 ease-out {isComplete
					? 'animate-pulse bg-amber-400 shadow-[0_0_20px_rgba(251,191,36,0.6)]'
					: running
						? 'bg-gradient-to-r from-amber-500 via-amber-400 to-emerald-400 shadow-[0_0_12px_rgba(16,185,129,0.3)]'
						: 'bg-amber-500'}"
			></div>
		</div>

		<!-- Milestone Pins (Boil Hop / Kettle Additions) -->
		{#if milestoneGroups.length > 0}
			<div class="pointer-events-none absolute inset-x-0 top-0 bottom-0">
				{#each milestoneGroups as group (group.additionTimeMinutes)}
					<div
						class="pointer-events-auto absolute -top-1 z-10 -translate-x-1/2"
						style="left: {group.positionPercentage}%;"
					>
						<button
							type="button"
							data-testid="milestone-pin-{group.additionTimeMinutes}"
							onclick={() => handleTogglePin(group.additionTimeMinutes)}
							title="{group.additionTimeMinutes}m ({t('batches.workspace.milestone_items_count', {
								count: group.ingredients.length
							})})"
							aria-label={t('batches.workspace.milestone_pin_aria', {
								time: group.additionTimeMinutes
							})}
							class="flex cursor-pointer items-center gap-1 rounded-full border px-2 py-0.5 text-[10px] font-bold shadow-md transition-all active:scale-95 {group.allChecked
								? 'border-emerald-500/50 bg-emerald-950/90 text-emerald-300 hover:border-emerald-400 hover:bg-emerald-900'
								: group.isDue
									? 'animate-pulse border-amber-300 bg-amber-500 text-zinc-950 shadow-[0_0_15px_rgba(245,158,11,0.7)] hover:bg-amber-400'
									: 'border-zinc-500/40 bg-zinc-800 text-zinc-300 hover:border-amber-400 hover:text-white'}"
						>
							{#if group.allChecked}
								<Check class="h-2.5 w-2.5 stroke-[3] text-emerald-400" />
							{:else if group.isDue}
								<Flame class="h-2.5 w-2.5 fill-current" />
							{/if}
							<span>{group.additionTimeMinutes}m</span>
							{#if group.ingredients.length > 1}
								<span class="opacity-75">({group.ingredients.length})</span>
							{/if}
						</button>
					</div>
				{/each}
			</div>
		{/if}
	</div>

	<!-- Interactive Milestone Popover Card -->
	{#if selectedGroup}
		<div
			data-testid="milestone-popover-card"
			class="relative z-20 mt-2 rounded-2xl border p-4 shadow-2xl backdrop-blur-md {variant ===
			'hud'
				? 'border-amber-500/30 bg-zinc-900/95 text-white'
				: 'border-zinc-200 bg-white text-zinc-900 shadow-xl dark:border-white/10 dark:bg-zinc-900 dark:text-white'}"
		>
			<div class="flex items-center justify-between border-b pb-2 dark:border-white/10">
				<div class="flex items-center gap-2">
					<Flame class="h-4 w-4 text-amber-500" />
					<h3 class="text-xs font-bold sm:text-sm">
						{t('batches.workspace.milestone_popover_title', {
							time: selectedGroup.additionTimeMinutes
						})}
					</h3>
					{#if selectedGroup.isDue && !selectedGroup.allChecked}
						<span
							class="rounded-md bg-amber-500/20 px-2 py-0.5 text-[10px] font-bold text-amber-400 uppercase"
						>
							{t('batches.workspace.milestone_due')}
						</span>
					{/if}
				</div>
				<button
					type="button"
					onclick={() => (selectedGroupMinutes = null)}
					class="rounded-lg p-1 text-zinc-400 transition-colors hover:bg-zinc-800 hover:text-white"
					aria-label={t('batches.workspace.close_popover')}
				>
					<X class="h-4 w-4" />
				</button>
			</div>

			<ul class="mt-3 divide-y divide-zinc-200/50 dark:divide-white/5">
				{#each selectedGroup.ingredients as ing (ing.id)}
					<li class="flex items-center justify-between py-2 text-xs">
						<label class="flex cursor-pointer items-center gap-2.5">
							<input
								type="checkbox"
								data-testid="milestone-ingredient-checkbox-{ing.id}"
								checked={ing.isChecked}
								onchange={() => onToggleMilestone?.(ing.id, ing.isChecked)}
								class="h-4 w-4 rounded-md border-zinc-300 text-amber-500 focus:ring-amber-500/30 dark:border-zinc-700 dark:bg-zinc-900"
							/>
							<div class="space-y-0.5">
								<span
									class="font-semibold {ing.isChecked
										? 'text-zinc-400 line-through dark:text-zinc-500'
										: 'text-zinc-900 dark:text-zinc-100'}"
								>
									{ing.name}
								</span>
								<span class="block text-[11px] text-zinc-500">
									{ing.type}
									{#if ing.form}• {ing.form}{/if}
								</span>
							</div>
						</label>
						<span class="font-mono text-xs font-bold text-zinc-700 dark:text-zinc-300">
							{ing.amount}
							{ing.unit}
						</span>
					</li>
				{/each}
			</ul>
		</div>
	{/if}
</div>
