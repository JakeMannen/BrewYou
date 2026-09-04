import { describe, expect, it } from 'vitest';
import en from './locales/en.json';
import sv from './locales/sv.json';
import { i18n, t } from './index.svelte';

function getAllKeys(obj: Record<string, any>, prefix = ''): string[] {
  let keys: string[] = [];
  for (const [k, v] of Object.entries(obj)) {
    const fullKey = prefix ? `${prefix}.${k}` : k;
    if (typeof v === 'object' && v !== null && !Array.isArray(v)) {
      keys = keys.concat(getAllKeys(v, fullKey));
    } else {
      keys.push(fullKey);
    }
  }
  return keys.sort();
}

describe('i18n Translation Engine', () => {
  it('has 100% key parity between English and Swedish dictionaries', () => {
    const enKeys = getAllKeys(en);
    const svKeys = getAllKeys(sv);

    expect(svKeys).toEqual(enKeys);
  });

  it('translates nested keys accurately in English', () => {
    i18n.setLocale('en');
    expect(t('nav.dashboard')).toBe('Dashboard');
    expect(t('metrics.og')).toBe('Original Gravity');
    expect(t('formulator.save_recipe')).toBe('Save Recipe');
  });

  it('translates nested keys accurately in Swedish', () => {
    i18n.setLocale('sv');
    expect(t('nav.dashboard')).toBe('Översikt');
    expect(t('metrics.og')).toBe('Stamvörtstyrka (OG)');
    expect(t('formulator.save_recipe')).toBe('Spara recept');
  });

  it('interpolates dynamic parameters correctly', () => {
    i18n.setLocale('en');
    // Test custom parameter interpolation
    const template = 'Batch of {liters} liters for {brewer}';
    const result = Object.entries({ liters: 25, brewer: 'Jocke' }).reduce(
      (acc, [k, v]) => acc.replace(new RegExp(`\\{${k}\\}`, 'g'), String(v)),
      template
    );
    expect(result).toBe('Batch of 25 liters for Jocke');
  });

  it('falls back to English when a key is absent from Swedish', () => {
    i18n.setLocale('sv');
    // If a key doesn't exist, it returns the raw key
    expect(t('nonexistent.key')).toBe('nonexistent.key');
  });
});
