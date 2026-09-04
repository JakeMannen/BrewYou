import en from './locales/en.json';
import sv from './locales/sv.json';

export type LocaleCode = 'en' | 'sv';

export interface LocaleOption {
  code: LocaleCode;
  label: string;
  flag: string;
}

export const supportedLocales: LocaleOption[] = [
  { code: 'en', label: 'English', flag: '🇬🇧' },
  { code: 'sv', label: 'Svenska', flag: '🇸🇪' }
];

const dictionaries: Record<string, Record<string, any>> = {
  en,
  sv
};

class I18nStore {
  locale = $state<LocaleCode>('en');

  init() {
    if (typeof window !== 'undefined') {
      const saved = localStorage.getItem('brewyou_locale') as LocaleCode | null;
      if (saved && dictionaries[saved]) {
        this.setLocale(saved);
        return;
      }

      // Check browser language
      const browserLang = navigator.language?.slice(0, 2).toLowerCase();
      if (browserLang && dictionaries[browserLang]) {
        this.setLocale(browserLang as LocaleCode);
        return;
      }
    }
    this.setLocale('en');
  }

  setLocale(newLocale: LocaleCode) {
    if (!dictionaries[newLocale]) return;
    this.locale = newLocale;

    if (typeof window !== 'undefined') {
      localStorage.setItem('brewyou_locale', newLocale);
      document.documentElement.lang = newLocale;
    }
  }

  t(key: string, params?: Record<string, string | number>): string {
    const keys = key.split('.');
    let text: any = dictionaries[this.locale];

    for (const k of keys) {
      if (text && typeof text === 'object' && k in text) {
        text = text[k];
      } else {
        text = undefined;
        break;
      }
    }

    // Fallback to English if translation is missing in current locale
    if (typeof text !== 'string') {
      let fallbackText: any = dictionaries.en;
      for (const k of keys) {
        if (fallbackText && typeof fallbackText === 'object' && k in fallbackText) {
          fallbackText = fallbackText[k];
        } else {
          fallbackText = undefined;
          break;
        }
      }
      text = typeof fallbackText === 'string' ? fallbackText : key;
    }

    if (params) {
      return Object.entries(params).reduce(
        (acc, [pKey, pVal]) => acc.replace(new RegExp(`\\{${pKey}\\}`, 'g'), String(pVal)),
        text
      );
    }

    return text;
  }
}

export const i18n = new I18nStore();
export const t = (key: string, params?: Record<string, string | number>) => i18n.t(key, params);
