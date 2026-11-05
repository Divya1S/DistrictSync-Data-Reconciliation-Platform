import { useSyncExternalStore } from 'react';

/** Minimal external store usable from React components and from non-React code (fetch client, SignalR). */
export function createStore<T>(initial: T) {
  let state = initial;
  const listeners = new Set<() => void>();
  const get = () => state;
  const set = (next: T | ((prev: T) => T)) => {
    const value = typeof next === 'function' ? (next as (prev: T) => T)(state) : next;
    if (Object.is(value, state)) return;
    state = value;
    listeners.forEach((l) => l());
  };
  const subscribe = (listener: () => void) => {
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
    };
  };
  const use = <S = T>(selector?: (s: T) => S): S =>
    useSyncExternalStore(
      subscribe,
      () => (selector ? selector(state) : (state as unknown as S)),
      () => (selector ? selector(state) : (state as unknown as S)),
    );
  return { get, set, subscribe, use };
}
