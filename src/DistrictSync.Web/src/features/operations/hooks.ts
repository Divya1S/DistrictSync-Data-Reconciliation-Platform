import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/api/client';
import type { DeadLetterDto, OutboxHealthDto, PagedResult, WorkflowInstanceDto, WorkflowStatus } from '@/api/types';
import { ROOT_KEYS } from '@/realtime/handlers';

export interface DeadLetterFilters {
  includeReplayed: boolean;
  page: number;
  pageSize: number;
}

export interface WorkflowFilters {
  status?: WorkflowStatus | '';
  page: number;
  pageSize: number;
}

export const opsKeys = {
  all: ROOT_KEYS.operations,
  deadLetters: (f: DeadLetterFilters) => [...ROOT_KEYS.operations, 'dead-letters', f] as const,
  outbox: () => [...ROOT_KEYS.operations, 'outbox'] as const,
  workflows: (f: WorkflowFilters) => [...ROOT_KEYS.operations, 'workflows', f] as const,
};

export function useDeadLetters(filters: DeadLetterFilters) {
  return useQuery({
    queryKey: opsKeys.deadLetters(filters),
    queryFn: () => api.get<PagedResult<DeadLetterDto>>('/operations/dead-letters', { params: { includeReplayed: filters.includeReplayed || undefined, page: filters.page, pageSize: filters.pageSize } }),
    placeholderData: keepPreviousData,
  });
}

export function useReplayDeadLetter() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.post<void>(`/operations/dead-letters/${id}/replay`),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: opsKeys.all });
      void qc.invalidateQueries({ queryKey: ROOT_KEYS.dashboard });
    },
  });
}

export function useOutboxHealth() {
  return useQuery({ queryKey: opsKeys.outbox(), queryFn: () => api.get<OutboxHealthDto>('/operations/outbox'), refetchInterval: 15_000 });
}

export function useWorkflows(filters: WorkflowFilters) {
  return useQuery({
    queryKey: opsKeys.workflows(filters),
    queryFn: () => api.get<PagedResult<WorkflowInstanceDto>>('/operations/workflows', { params: { status: filters.status || undefined, page: filters.page, pageSize: filters.pageSize } }),
    placeholderData: keepPreviousData,
    refetchInterval: 20_000,
  });
}
