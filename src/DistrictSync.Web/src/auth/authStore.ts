import { createStore } from '@/lib/store';
import { decodeJwt, type DecodedIdentity } from './jwt';

const TOKEN_KEY = 'ds.token';

export interface AuthState {
  token: string | null;
  identity: DecodedIdentity | null;
}

function read(): AuthState {
  try {
    const token = localStorage.getItem(TOKEN_KEY);
    if (!token) return { token: null, identity: null };
    const identity = decodeJwt(token);
    if (identity?.expiresAt && identity.expiresAt <= Date.now()) {
      localStorage.removeItem(TOKEN_KEY);
      return { token: null, identity: null };
    }
    return { token, identity };
  } catch {
    return { token: null, identity: null };
  }
}

export const authStore = createStore<AuthState>(read());

export function setToken(token: string): void {
  try {
    localStorage.setItem(TOKEN_KEY, token);
  } catch {
    /* storage unavailable: session-only auth */
  }
  authStore.set({ token, identity: decodeJwt(token) });
}

export function clearToken(): void {
  try {
    localStorage.removeItem(TOKEN_KEY);
  } catch {
    /* ignore */
  }
  authStore.set((prev) => (prev.token === null ? prev : { token: null, identity: null }));
}

export function getToken(): string | null {
  return authStore.get().token;
}

// Keep tabs in sync: logging out in one tab logs out the others.
if (typeof window !== 'undefined') {
  window.addEventListener('storage', (e) => {
    if (e.key === TOKEN_KEY) authStore.set(read());
  });
}
