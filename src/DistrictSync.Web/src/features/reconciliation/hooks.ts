import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/api/client';
import type {
  ComparisonProfileDto, CreateComparisonProfileRequest, CreateReconciliationRunRequest, DiscrepancyDetailDto, DiscrepancyDto, DiscrepancyStatus, DiscrepancyType, PagedResult, ReconciliationRunDto, ReconciliationRunStatus, Severity,
} from '@/api/types';
import { ROOT_KEYS } from '@/realtime/handlers';

export interface RunFilters {
  status?: ReconciliationRunStatus | '';
  page: number;
  pageSize: number;
}

export interface DiscrepancyFilters {
  type?: DiscrepancyType | '';
  status?: DiscrepancyStatus | '';
  severity?: Severity | '';
  matchKey?: string;
  page: number;
  pageSize: number;
}

export const reconKeys = {
  all: ROOT_KEYS.reconciliation,
  runs: (f: RunFilters) => [...ROOT_KEYS.reconciliation, 'runs', f] as const,
  run: (id: string) => [...ROOT_KEYS.reconciliation, 'run', id] as const,
  discrepancies: (runId: string, f: DiscrepancyFilters) => [...ROOT_KEYS.discrepancies, 'run', runId, f] as const,
  discrepancy: (id: string) => [...ROOT_KEYS.discrepancies, 'detail', id] as const,
  profiles: () => [...ROOT_KEYS.reconciliation, 'profiles'] as const,
};

const isActive = (s: ReconciliationRunStatus) => s === 'Queued' || s === 'Running';

export function useRuns(filters: RunFilters) {
  return useQuery({
    queryKey: reconKeys.runs(filters),
    queryFn: () => api.get<PagedResult<ReconciliationRunDto>>('/reconciliation/runs', { params: { status: filters.status || undefined, page: filters.page, pageSize: filters.pageSize } }),
    placeholderData: keepPreviousData,
    refetchInterval: (q) => (q.state.data?.items.some((r) => isActive(r.status)) ? 10_000 : false),
  });
}

export function useRun(id: string | undefined) {
  return useQuery({
    queryKey: reconKeys.run(id ?? ''),
    queryFn: () => api.get<ReconciliationRunDto>(`/reconciliation/runs/${id}`),
    enabled: Boolean(id),
    refetchInterval: (q) => (q.state.data && isActive(q.state.data.status) ? 5_000 : false),
  });
}

export function useCreateRun() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateReconciliationRunRequest) => api.post<ReconciliationRunDto>('/reconciliation/runs', body),
    onSuccess: () => void qc.invalidateQueries({ queryKey: reconKeys.all }),
  });
}

export function useCancelRun() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.post<void>(`/reconciliation/runs/${id}/cancel`),
    onSuccess: () => void qc.invalidateQueries({ queryKey: reconKeys.all }),
  });
}

export function useDiscrepancies(runId: string | undefined, filters: DiscrepancyFilters) {
  return useQuery({
    queryKey: reconKeys.discrepancies(runId ?? '', filters),
    queryFn: () =>
      api.get<PagedResult<DiscrepancyDto>>(`/reconciliation/runs/${runId}/discrepancies`, {
        params: {
          type: filters.type || undefined,
          status: filters.status || undefined,
          severity: filters.severity || undefined,
          matchKey: filters.matchKey?.trim() || undefined,
          page: filters.page,
          pageSize: filters.pageSize,
        },
      }),
    enabled: Boolean(runId),
    placeholderData: keepPreviousData,
  });
}

export function useDiscrepancy(id: string | null | undefined) {
  return useQuery({ queryKey: reconKeys.discrepancy(id ?? ''), queryFn: () => api.get<DiscrepancyDetailDto>(`/discrepancies/${id}`), enabled: Boolean(id) });
}

export function useComparisonProfiles() {
  return useQuery({ queryKey: reconKeys.profiles(), queryFn: () => api.get<ComparisonProfileDto[]>('/comparison-profiles'), staleTime: 60_000 });
}

export function useCreateComparisonProfile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateComparisonProfileRequest) => api.post<ComparisonProfileDto>('/comparison-profiles', body),
    onSuccess: () => void qc.invalidateQueries({ queryKey: reconKeys.profiles() }),
  });
}
