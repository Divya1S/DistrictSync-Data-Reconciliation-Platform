import { createStore } from './store';

// A shared clock: components that render relative times re-render together, and no component calls
// Date.now() during render.
const nowStore = createStore<number>(Date.now());
if (typeof window !== 'undefined') {
  window.setInterval(() => nowStore.set(Date.now()), 15_000);
}

export function useNow(): number {
  return nowStore.use();
}
