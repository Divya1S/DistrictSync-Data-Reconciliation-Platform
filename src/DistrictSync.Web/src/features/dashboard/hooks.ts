import { useQuery } from '@tanstack/react-query';
import { api } from '@/api/client';
import type { DashboardSummaryDto } from '@/api/types';
import { ROOT_KEYS } from '@/realtime/handlers';

export const dashboardKeys = {
  summary: () => [...ROOT_KEYS.dashboard, 'summary'] as const,
};

export function useDashboardSummary() {
  return useQuery({
    queryKey: dashboardKeys.summary(),
    queryFn: () => api.get<DashboardSummaryDto>('/dashboard/summary'),
    refetchInterval: 30_000,
    staleTime: 10_000,
  });
}
