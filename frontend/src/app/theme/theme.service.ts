import { Injectable, effect, signal } from '@angular/core';

export type Theme = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'target30_theme';
const SUPPORTED: Theme[] = ['light', 'dark', 'system'];

function detectDefaultTheme(): Theme {
  try {
    const stored = localStorage.getItem(STORAGE_KEY) as Theme | null;
    if (stored && SUPPORTED.includes(stored)) return stored;
  } catch {
    // localStorage indisponível — segue com o padrão
  }
  return 'system';
}

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly theme = signal<Theme>(detectDefaultTheme());

  constructor() {
    effect(() => this.applyTheme(this.theme()));
  }

  setTheme(theme: Theme): void {
    this.theme.set(theme);
    try {
      localStorage.setItem(STORAGE_KEY, theme);
    } catch {
      // ignora se não conseguir persistir
    }
  }

  private applyTheme(theme: Theme): void {
    const root = document.documentElement;
    if (theme === 'system') root.removeAttribute('data-theme');
    else root.setAttribute('data-theme', theme);
  }
}
