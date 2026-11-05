import { useMemo } from 'react';
import { authStore, clearToken, setToken } from './authStore';
import type { DevTokenResponse } from '@/api/types';

export function useAuth() {
  const state = authStore.use();
  return useMemo(() => {
    const roles = state.identity?.roles ?? [];
    const hasRole = (...wanted: string[]) => wanted.some((r) => roles.includes(r.toLowerCase()));
    return {
      isAuthenticated: state.token !== null,
      token: state.token,
      userId: state.identity?.userId ?? null,
      roles,
      tenantId: state.identity?.tenantId ?? null,
      expiresAt: state.identity?.expiresAt ?? null,
      hasRole,
      canApprove: hasRole('approver', 'admin'),
      isAdmin: hasRole('admin'),
      isViewerOnly: roles.length > 0 && roles.every((r) => r === 'viewer'),
      login: (response: DevTokenResponse) => setToken(response.accessToken),
      logout: () => clearToken(),
    };
  }, [state]);
}
