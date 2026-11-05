import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/api/client';
import type { IngestionBatchDto, IngestionBatchStatus, PagedResult, StartPullRequest } from '@/api/types';
import { ROOT_KEYS } from '@/realtime/handlers';

export interface BatchFilters {
  status?: IngestionBatchStatus | '';
  sourceSystemId?: string;
  page: number;
  pageSize: number;
}

export const ingestionKeys = {
  all: ROOT_KEYS.ingestion,
  batches: (f: BatchFilters) => [...ROOT_KEYS.ingestion, 'batches', f] as const,
  batch: (id: string) => [...ROOT_KEYS.ingestion, 'batch', id] as const,
};

export function useBatches(filters: BatchFilters) {
  return useQuery({
    queryKey: ingestionKeys.batches(filters),
    queryFn: () =>
      api.get<PagedResult<IngestionBatchDto>>('/ingestion/batches', {
        params: { status: filters.status || undefined, sourceSystemId: filters.sourceSystemId || undefined, page: filters.page, pageSize: filters.pageSize },
      }),
    placeholderData: keepPreviousData,
    refetchInterval: (q) => (q.state.data?.items.some((b) => b.status === 'Processing' || b.status === 'Received') ? 10_000 : false),
  });
}

export function useBatch(id: string | undefined) {
  return useQuery({ queryKey: ingestionKeys.batch(id ?? ''), queryFn: () => api.get<IngestionBatchDto>(`/ingestion/batches/${id}`), enabled: Boolean(id) });
}

export function useUploadCsv() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ sourceSystemId, entityType, file }: { sourceSystemId: string; entityType: string; file: File }) => {
      const form = new FormData();
      form.append('file', file, file.name);
      return api.postForm<IngestionBatchDto>('/ingestion/batches/csv', form, { params: { sourceSystemId, entityType } });
    },
    onSuccess: () => void qc.invalidateQueries({ queryKey: ingestionKeys.all }),
  });
}

export function useRetryBatch() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.post<IngestionBatchDto>(`/ingestion/batches/${id}/retry`),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ingestionKeys.all }),
  });
}

export function useStartPull() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: StartPullRequest) => api.post<IngestionBatchDto>('/ingestion/pulls', body),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ingestionKeys.all }),
  });
}
