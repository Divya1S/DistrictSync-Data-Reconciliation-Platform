import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/api/client';
import type { CreateRuleSetRequest, DryRunResponse, PublishRuleSetRequest, RuleDto, RuleSetDto, RuleSetSummaryDto, UpsertRuleRequest, ValidateRuleResponse } from '@/api/types';
import { ROOT_KEYS } from '@/realtime/handlers';

export const ruleKeys = {
  all: ROOT_KEYS.rules,
  sets: () => [...ROOT_KEYS.rules, 'sets'] as const,
  set: (id: string) => [...ROOT_KEYS.rules, 'set', id] as const,
};

export function useRuleSets() {
  return useQuery({ queryKey: ruleKeys.sets(), queryFn: () => api.get<RuleSetSummaryDto[]>('/rules/sets') });
}

export function useRuleSet(id: string | undefined) {
  return useQuery({ queryKey: ruleKeys.set(id ?? ''), queryFn: () => api.get<RuleSetDto>(`/rules/sets/${id}`), enabled: Boolean(id) });
}

export function useCreateRuleSet() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateRuleSetRequest) => api.post<RuleSetDto>('/rules/sets', body),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ruleKeys.all }),
  });
}

export function useAddRule(setId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: UpsertRuleRequest) => api.post<RuleDto>(`/rules/sets/${setId}/rules`, body),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ruleKeys.all }),
  });
}

export function useUpdateRule(setId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ ruleId, body }: { ruleId: string; body: UpsertRuleRequest }) => api.put<void>(`/rules/sets/${setId}/rules/${ruleId}`, body),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ruleKeys.all }),
  });
}

export function useDeleteRule(setId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (ruleId: string) => api.delete<void>(`/rules/sets/${setId}/rules/${ruleId}`),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ruleKeys.all }),
  });
}

export function usePublishRuleSet(setId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: PublishRuleSetRequest) => api.post<RuleSetDto>(`/rules/sets/${setId}/publish`, body),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ruleKeys.all }),
  });
}

export function useNextVersion(setId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => api.post<RuleSetDto>(`/rules/sets/${setId}/next-version`),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ruleKeys.all }),
  });
}

export function useValidateRule() {
  return useMutation({ mutationFn: (condition: string) => api.post<ValidateRuleResponse>('/rules/validate', { condition }) });
}

export function useDryRun(setId: string) {
  return useMutation({ mutationFn: (discrepancyId: string) => api.post<DryRunResponse>(`/rules/sets/${setId}/dry-run`, { discrepancyId }) });
}
