import { Injectable, signal } from '@angular/core';
import { RO } from './translations-ro';

export type Lang = 'en' | 'ro';

const KEY = 'lynx-lang';
const LOCALES: Record<Lang, string> = { en: 'en-US', ro: 'ro-RO' };

/**
 * Runtime translations. English text is the key; a missing Romanian entry falls back to the English text.
 * Templates call t() so the signal read re-renders OnPush components when the language changes.
 */
@Injectable({ providedIn: 'root' })
export class I18n {
  readonly lang = signal<Lang>(this.initial());

  constructor() {
    document.documentElement.lang = this.lang();
  }

  toggle() {
    const next: Lang = this.lang() === 'en' ? 'ro' : 'en';
    this.lang.set(next);
    document.documentElement.lang = next;
    try { localStorage.setItem(KEY, next); } catch { /* storage unavailable */ }
  }

  /** Translates `text`; `{name}` placeholders are filled from `params`. `{de}` is Romanian's "de " before numbers >= 20. */
  t(text: string, params?: Record<string, string | number>): string {
    let out = this.lang() === 'ro' ? (RO[text] ?? text) : text;
    if (params) {
      const n = params['n'];
      const de = typeof n === 'number' && this.lang() === 'ro' && (n % 100 === 0 || n % 100 >= 20) ? 'de ' : '';
      for (const [k, v] of Object.entries({ de, ...params })) out = out.replaceAll(`{${k}}`, String(v));
    }
    return out;
  }

  money(value: number, currency = 'EUR'): string {
    return new Intl.NumberFormat(LOCALES[this.lang()], { style: 'currency', currency }).format(value);
  }

  /** ISO date (yyyy-mm-dd) in the active locale. */
  date(iso: string): string {
    return new Intl.DateTimeFormat(LOCALES[this.lang()], { dateStyle: 'medium' }).format(new Date(`${iso}T00:00:00`));
  }

  monthYear(year: number, monthIndex: number): string {
    return new Intl.DateTimeFormat(LOCALES[this.lang()], { month: 'long', year: 'numeric' }).format(new Date(year, monthIndex, 1));
  }

  private initial(): Lang {
    try { return localStorage.getItem(KEY) === 'ro' ? 'ro' : 'en'; } catch { return 'en'; }
  }
}
