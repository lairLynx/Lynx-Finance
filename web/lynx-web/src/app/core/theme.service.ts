import { Injectable, signal } from '@angular/core';

export type Theme = 'dark' | 'light';

const KEY = 'lynx-theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly theme = signal<Theme>(document.documentElement.dataset['theme'] === 'light' ? 'light' : 'dark');

  toggle() {
    const next: Theme = this.theme() === 'dark' ? 'light' : 'dark';
    this.theme.set(next);
    document.documentElement.dataset['theme'] = next;
    try { localStorage.setItem(KEY, next); } catch { /* storage unavailable */ }
  }
}
