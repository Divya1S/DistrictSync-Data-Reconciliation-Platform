import { useCallback, useEffect } from 'react';
import { createStore } from './store';

export type ThemePreference = 'light' | 'dark' | 'system';
const KEY = 'ds.theme';

function readPreference(): ThemePreference {
  try {
    const v = localStorage.getItem(KEY);
    return v === 'light' || v === 'dark' ? v : 'system';
  } catch {
    return 'system';
  }
}

const media = typeof window !== 'undefined' ? window.matchMedia('(prefers-color-scheme: dark)') : null;

export const themeStore = createStore<{ preference: ThemePreference; systemDark: boolean }>({
  preference: readPreference(),
  systemDark: media?.matches ?? false,
});

media?.addEventListener('change', (e) => themeStore.set((s) => ({ ...s, systemDark: e.matches })));

function apply(preference: ThemePreference, systemDark: boolean) {
  const dark = preference === 'dark' || (preference === 'system' && systemDark);
  document.documentElement.classList.toggle('dark', dark);
}

export function useTheme() {
  const { preference, systemDark } = themeStore.use();
  const resolved: 'light' | 'dark' = preference === 'dark' || (preference === 'system' && systemDark) ? 'dark' : 'light';

  useEffect(() => {
    apply(preference, systemDark);
  }, [preference, systemDark]);

  const setPreference = useCallback((next: ThemePreference) => {
    try {
      if (next === 'system') localStorage.removeItem(KEY);
      else localStorage.setItem(KEY, next);
    } catch {
      /* ignore */
    }
    themeStore.set((s) => ({ ...s, preference: next }));
  }, []);

  const toggle = useCallback(() => setPreference(resolved === 'dark' ? 'light' : 'dark'), [resolved, setPreference]);

  return { preference, resolved, setPreference, toggle };
}
