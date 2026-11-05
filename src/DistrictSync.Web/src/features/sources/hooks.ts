import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/api/client';
import type { ApiKeyResponse, CreateSourceSystemRequest, SourceSystemCreatedResponse, SourceSystemDto } from '@/api/types';
import { ROOT_KEYS } from '@/realtime/handlers';

export const sourceKeys = {
  all: ROOT_KEYS.sources,
  list: () => [...ROOT_KEYS.sources, 'list'] as const,
};

export function useSourceSystems() {
  return useQuery({ queryKey: sourceKeys.list(), queryFn: () => api.get<SourceSystemDto[]>('/source-systems'), staleTime: 60_000 });
}

export function useCreateSourceSystem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateSourceSystemRequest) => api.post<SourceSystemCreatedResponse>('/source-systems', body),
    onSuccess: () => void qc.invalidateQueries({ queryKey: sourceKeys.all }),
  });
}

export function useRotateApiKey() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.post<ApiKeyResponse>(`/source-systems/${id}/rotate-key`),
    onSuccess: () => void qc.invalidateQueries({ queryKey: sourceKeys.all }),
  });
}
