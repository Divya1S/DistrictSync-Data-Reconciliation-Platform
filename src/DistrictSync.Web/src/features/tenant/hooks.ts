import { useQuery } from '@tanstack/react-query';
import { api } from '@/api/client';
import type { TenantDto } from '@/api/types';

export function useTenant() {
  return useQuery({ queryKey: ['tenant', 'me'], queryFn: () => api.get<TenantDto>('/tenants/me'), staleTime: 5 * 60_000 });
}
