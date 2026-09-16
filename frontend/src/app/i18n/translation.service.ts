import { Injectable, signal } from '@angular/core';
import { Lang, SUPPORTED_LANGS, TRANSLATIONS } from './translations';

const STORAGE_KEY = 'target30_lang';

function detectDefaultLang(): Lang {
  try {
    const stored = localStorage.getItem(STORAGE_KEY) as Lang | null;
    if (stored && SUPPORTED_LANGS.includes(stored)) return stored;
  } catch {
    // localStorage indisponível (ex.: modo privado) — segue com o padrão do navegador
  }

  const browserLang = navigator.language?.slice(0, 2).toLowerCase() as Lang;
  if (SUPPORTED_LANGS.includes(browserLang)) return browserLang;
  return 'pt';
}

@Injectable({ providedIn: 'root' })
export class TranslationService {
  readonly lang = signal<Lang>(detectDefaultLang());

  setLang(lang: Lang): void {
    this.lang.set(lang);
    try {
      localStorage.setItem(STORAGE_KEY, lang);
    } catch {
      // ignora se não conseguir persistir
    }
  }

  t(key: string, params?: Record<string, string | number>): string {
    const dict = TRANSLATIONS[this.lang()];
    let text = dict[key] ?? TRANSLATIONS.pt[key] ?? key;
    if (params) {
      for (const [k, v] of Object.entries(params)) {
        text = text.replace(`{${k}}`, String(v));
      }
    }
    return text;
  }
}
