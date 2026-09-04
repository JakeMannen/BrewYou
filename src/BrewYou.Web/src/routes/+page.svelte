<script lang="ts">
  import { Beer, PlusCircle, BookOpen, Layers, ArrowRight, Sparkles, Gauge } from '@lucide/svelte';
  import { t } from '$lib/i18n/index.svelte';
</script>

<div class="space-y-12">
  <!-- Hero Section -->
  <section class="relative rounded-2xl bg-gradient-to-br from-amber-500/10 via-slate-900 to-slate-900 border border-amber-500/20 p-8 sm:p-12 overflow-hidden shadow-2xl">
    <div class="relative z-10 max-w-2xl space-y-4">
      <div class="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-amber-500/10 border border-amber-500/30 text-amber-400 text-xs font-semibold uppercase tracking-wider">
        <Sparkles class="w-3.5 h-3.5" />
        {t('home.tagline')}
      </div>
      <h1 class="text-3xl sm:text-5xl font-extrabold tracking-tight text-white leading-tight">
        {t('home.hero_title')}
      </h1>
      <p class="text-slate-400 text-base sm:text-lg">
        {t('home.hero_desc')}
      </p>

      <div class="pt-4 flex flex-wrap items-center gap-4">
        <a
          href="/recipes/new"
          class="px-6 py-3 rounded-xl bg-amber-500 hover:bg-amber-400 text-slate-950 font-bold text-sm sm:text-base flex items-center gap-2 shadow-lg shadow-amber-500/20 transition-all hover:scale-[1.02]"
        >
          <PlusCircle class="w-5 h-5" />
          <span>{t('home.cta_formulator')}</span>
          <ArrowRight class="w-4 h-4 ml-1" />
        </a>
        <a
          href="/recipes"
          class="px-5 py-3 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 font-medium text-sm sm:text-base flex items-center gap-2 border border-slate-700 transition-colors"
        >
          <BookOpen class="w-5 h-5" />
          <span>{t('home.cta_library')}</span>
        </a>
      </div>
    </div>
  </section>

  <!-- Feature Pillars -->
  <section class="grid grid-cols-1 md:grid-cols-3 gap-6">
    <div class="p-6 rounded-xl bg-slate-900/60 border border-slate-800 hover:border-amber-500/30 transition-colors space-y-3">
      <div class="w-10 h-10 rounded-lg bg-amber-500/10 border border-amber-500/20 flex items-center justify-center text-amber-400">
        <Gauge class="w-5 h-5" />
      </div>
      <h3 class="text-lg font-bold text-white">{t('home.feature_calc_title')}</h3>
      <p class="text-sm text-slate-400 leading-relaxed">
        {t('home.feature_calc_desc')}
      </p>
    </div>

    <div class="p-6 rounded-xl bg-slate-900/60 border border-slate-800 hover:border-amber-500/30 transition-colors space-y-3">
      <div class="w-10 h-10 rounded-lg bg-emerald-500/10 border border-emerald-500/20 flex items-center justify-center text-emerald-400">
        <Layers class="w-5 h-5" />
      </div>
      <h3 class="text-lg font-bold text-white">{t('home.feature_cat_title')}</h3>
      <p class="text-sm text-slate-400 leading-relaxed">
        {t('home.feature_cat_desc')}
      </p>
    </div>

    <div class="p-6 rounded-xl bg-slate-900/60 border border-slate-800 hover:border-amber-500/30 transition-colors space-y-3">
      <div class="w-10 h-10 rounded-lg bg-sky-500/10 border border-sky-500/20 flex items-center justify-center text-sky-400">
        <Beer class="w-5 h-5" />
      </div>
      <h3 class="text-lg font-bold text-white">{t('home.feature_style_title')}</h3>
      <p class="text-sm text-slate-400 leading-relaxed">
        {t('home.feature_style_desc')}
      </p>
    </div>
  </section>
</div>
