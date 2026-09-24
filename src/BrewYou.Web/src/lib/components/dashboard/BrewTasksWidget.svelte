<script lang="ts">
	import { t } from '$lib/i18n/index.svelte';
	import { dashboard } from '$lib/stores/dashboard.svelte';
	import { CheckSquare, Plus, Trash2, Sparkles, CheckCircle2 } from '@lucide/svelte';

	let newTaskText = $state('');

	function handleAddTask(e: Event) {
		e.preventDefault();
		if (!newTaskText.trim()) return;
		dashboard.addAdhocTask(newTaskText);
		newTaskText = '';
	}
</script>

<section class="glass-panel flex h-full flex-col justify-between space-y-4 rounded-3xl p-6">
	<div class="space-y-4">
		<div class="flex items-center justify-between">
			<div class="flex items-center gap-2">
				<CheckSquare class="h-4 w-4 text-rose-500" />
				<h2 class="text-sm font-bold tracking-tight text-zinc-900 sm:text-base dark:text-white">
					{t('dashboard.tasks_title')}
				</h2>
				{#if dashboard.tasksDueCount > 0}
					<span
						class="rounded-full bg-rose-500/15 px-2.5 py-0.5 font-mono text-xs font-semibold text-rose-600 dark:text-rose-400"
					>
						{dashboard.tasksDueCount}
					</span>
				{/if}
			</div>
		</div>

		<!-- Inline Quick Add Input -->
		<form onsubmit={handleAddTask} class="flex items-center gap-2">
			<input
				type="text"
				bind:value={newTaskText}
				placeholder={t('dashboard.add_task_placeholder')}
				class="h-10 flex-1 rounded-xl border border-zinc-200 bg-white/80 px-3.5 text-xs text-zinc-900 placeholder:text-zinc-600 focus:border-amber-500 focus:ring-1 focus:ring-amber-500 focus:outline-none sm:text-sm dark:border-white/10 dark:bg-zinc-900/60 dark:text-white dark:placeholder:text-zinc-400"
			/>
			<button
				type="submit"
				disabled={!newTaskText.trim()}
				class="inline-flex h-10 items-center gap-1 rounded-xl border border-amber-500/50 bg-amber-500 px-3 text-xs font-bold text-zinc-950 transition hover:bg-amber-400 disabled:opacity-40 sm:px-4 sm:text-sm"
			>
				<Plus class="h-4 w-4" />
				<span>{t('dashboard.add_task_btn')}</span>
			</button>
		</form>
	</div>

	<!-- Tasks List or Empty State -->
	<div class="flex flex-1 flex-col justify-start pt-1">
		{#if dashboard.allTasks.length === 0}
			<div
				class="flex flex-1 flex-col items-center justify-center rounded-2xl border border-dashed border-zinc-200 py-8 text-center dark:border-white/10"
			>
				<CheckCircle2 class="h-8 w-8 text-emerald-500/70" />
				<p class="mt-2 text-xs font-medium text-zinc-600 dark:text-zinc-400">
					{t('dashboard.no_tasks_due')}
				</p>
			</div>
		{:else}
			<div
				class="divide-y divide-zinc-200/60 rounded-2xl border border-zinc-200/60 bg-white/40 dark:divide-white/5 dark:border-white/10 dark:bg-zinc-900/40"
			>
				{#each dashboard.allTasks as task (task.id)}
					<div
						class="flex items-center justify-between gap-3 p-3 transition hover:bg-zinc-100/50 dark:hover:bg-zinc-800/40 {task.completed
							? 'opacity-60'
							: ''}"
					>
						<label class="flex min-w-0 flex-1 cursor-pointer items-center gap-3">
							<input
								type="checkbox"
								checked={task.completed}
								onchange={() => dashboard.toggleTask(task.id)}
								class="h-4 w-4 rounded border-zinc-300 text-amber-500 focus:ring-amber-400 dark:border-zinc-700 dark:bg-zinc-800"
							/>
							<div class="min-w-0 flex-1">
								<p
									class="truncate text-xs sm:text-sm {task.completed
										? 'text-zinc-600 line-through dark:text-zinc-400'
										: 'font-medium text-zinc-900 dark:text-white'}"
								>
									{task.text}
								</p>
							</div>
						</label>

						<div class="flex items-center gap-2">
							{#if 'isMilestone' in task && task.isMilestone}
								<span
									class="inline-flex items-center gap-1 rounded-md border border-amber-500/30 bg-amber-500/10 px-2 py-0.5 text-[10px] font-semibold text-amber-600 dark:text-amber-400"
								>
									<Sparkles class="h-3 w-3" />
									<span>{t('dashboard.milestone_badge')}</span>
								</span>
							{:else}
								<button
									type="button"
									onclick={() => dashboard.deleteTask(task.id)}
									class="rounded-lg p-1 text-zinc-600 transition hover:bg-zinc-200/60 hover:text-rose-600 dark:text-zinc-400 dark:hover:bg-zinc-800 dark:hover:text-rose-400"
									title={t('common.delete')}
								>
									<Trash2 class="h-3.5 w-3.5" />
								</button>
							{/if}
						</div>
					</div>
				{/each}
			</div>
		{/if}
	</div>
</section>
