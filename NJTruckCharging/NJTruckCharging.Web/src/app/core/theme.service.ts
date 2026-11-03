import { Injectable, effect, signal } from '@angular/core';

export type Theme = 'light' | 'dark';

/**
 * Light/Dark theme state. The active theme is written to <html data-theme> (CSS tokens key off it)
 * and persisted in localStorage. Light is the default for first-time visitors. A small inline script
 * in index.html applies the saved theme before Angular boots to avoid a flash.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private static readonly KEY = 'njtc-theme';

  readonly theme = signal<Theme>(ThemeService.initial());
  readonly isDark = () => this.theme() === 'dark';

  constructor() {
    effect(() => {
      const t = this.theme();
      document.documentElement.setAttribute('data-theme', t);
      try { localStorage.setItem(ThemeService.KEY, t); } catch { /* storage unavailable */ }
    });
  }

  toggle() { this.theme.update(t => (t === 'light' ? 'dark' : 'light')); }
  set(theme: Theme) { this.theme.set(theme); }

  private static initial(): Theme {
    try {
      const saved = localStorage.getItem(ThemeService.KEY);
      if (saved === 'light' || saved === 'dark') return saved;
    } catch { /* storage unavailable */ }
    return 'light'; // default for first-time visitors
  }
}
