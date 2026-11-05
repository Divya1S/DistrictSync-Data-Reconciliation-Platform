import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, isApiError } from '@/api/client';
import type { AddCommentRequest, AssignCaseRequest, CancelCaseRequest, CaseDetailDto, CaseDto, CasePriority, CaseState, PagedResult, ProposeResolutionRequest, RejectProposalRequest } from '@/api/types';
import { ROOT_KEYS } from '@/realtime/handlers';
import { toast } from '@/components/ui/toastStore';

export interface CaseFilters {
  state?: CaseState | '';
  priority?: CasePriority | '';
  assigneeId?: string;
  overdue?: boolean;
  page: number;
  pageSize: number;
}

export const caseKeys = {
  all: ROOT_KEYS.cases,
  list: (f: CaseFilters) => [...ROOT_KEYS.cases, 'list', f] as const,
  detail: (id: string) => [...ROOT_KEYS.cases, 'detail', id] as const,
};

export function useCases(filters: CaseFilters) {
  return useQuery({
    queryKey: caseKeys.list(filters),
    queryFn: () =>
      api.get<PagedResult<CaseDto>>('/cases', {
        params: {
          state: filters.state || undefined,
          priority: filters.priority || undefined,
          assigneeId: filters.assigneeId?.trim() || undefined,
          overdue: filters.overdue ? true : undefined,
          page: filters.page,
          pageSize: filters.pageSize,
        },
      }),
    placeholderData: keepPreviousData,
  });
}

export function useCase(id: string | undefined) {
  return useQuery({ queryKey: caseKeys.detail(id ?? ''), queryFn: () => api.get<CaseDetailDto>(`/cases/${id}`), enabled: Boolean(id) });
}

export type CaseAction =
  | { kind: 'assign'; body: AssignCaseRequest }
  | { kind: 'propose'; body: ProposeResolutionRequest }
  | { kind: 'approve' }
  | { kind: 'reject'; body: RejectProposalRequest }
  | { kind: 'cancel'; body: CancelCaseRequest }
  | { kind: 'comments'; body: AddCommentRequest };

/**
 * All case mutations go through here so the optimistic-concurrency header (If-Match: eTag) and
 * the 412 "someone else changed it" handling live in one place.
 */
export function useCaseAction(caseId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ action, eTag }: { action: CaseAction; eTag: string }) => {
      const options = { headers: { 'If-Match': eTag } };
      const body = 'body' in action ? action.body : undefined;
      return api.post<CaseDto>(`/cases/${caseId}/${action.kind}`, body, options);
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: caseKeys.all });
      void qc.invalidateQueries({ queryKey: ROOT_KEYS.discrepancies });
      void qc.invalidateQueries({ queryKey: ROOT_KEYS.dashboard });
    },
    onError: (error) => {
      if (isApiError(error) && error.isPreconditionFailed) {
        toast.warning('This case was updated by someone else', { description: 'Reloading the latest version. Please review and try again.' });
        void qc.invalidateQueries({ queryKey: caseKeys.detail(caseId) });
      }
    },
  });
}
