import type { QueryClient } from '@tanstack/react-query';
import type {
  CaseEscalatedEvent, CaseOpenedEvent, CaseStateChangedEvent, IngestionBatchCompletedEvent, IngestionBatchFailedEvent, LiveUpdate, MessageDeadLetteredEvent, ReconciliationCompletedEvent, ReconciliationFailedEvent, RuleSetPublishedEvent,
} from '@/api/types';
import { toast } from '@/components/ui/toastStore';
import { fmtNumber, humanize, shortId } from '@/lib/format';
import { recordUpdate } from './liveStore';

/** Root query keys per feature; feature hook modules build on these. */
export const ROOT_KEYS = {
  dashboard: ['dashboard'] as const,
  ingestion: ['ingestion'] as const,
  reconciliation: ['reconciliation'] as const,
  discrepancies: ['discrepancies'] as const,
  cases: ['cases'] as const,
  rules: ['rules'] as const,
  operations: ['operations'] as const,
  sources: ['sources'] as const,
};

/** Invalidate the affected caches and raise non-blocking toasts for user-relevant outcomes. */
export function handleLiveUpdate(update: LiveUpdate, queryClient: QueryClient): void {
  recordUpdate(update);
  const invalidate = (...keys: ReadonlyArray<readonly unknown[]>) => keys.forEach((queryKey) => void queryClient.invalidateQueries({ queryKey }));

  switch (update.kind) {
    case 'ingestion.progress':
      // High frequency; progress is rendered from the live store, not refetched.
      return;
    case 'ingestion.batch-received':
      invalidate(ROOT_KEYS.ingestion, ROOT_KEYS.dashboard);
      return;
    case 'ingestion.batch-completed': {
      const p = update.payload as IngestionBatchCompletedEvent;
      invalidate(ROOT_KEYS.ingestion, ROOT_KEYS.dashboard);
      toast.success(`Batch ${shortId(p.batchId)} completed`, {
        description: `${p.entityType}: ${fmtNumber(p.accepted)} accepted, ${fmtNumber(p.rejected)} rejected, ${fmtNumber(p.duplicates)} duplicates`,
        href: '/ingestion',
      });
      return;
    }
    case 'ingestion.batch-failed': {
      const p = update.payload as IngestionBatchFailedEvent;
      invalidate(ROOT_KEYS.ingestion, ROOT_KEYS.dashboard);
      toast.error(`Batch ${shortId(p.batchId)} failed`, { description: p.reason, href: '/ingestion' });
      return;
    }
    case 'reconciliation.progress':
      return;
    case 'reconciliation.requested':
      invalidate(ROOT_KEYS.reconciliation, ROOT_KEYS.dashboard);
      return;
    case 'reconciliation.completed': {
      const p = update.payload as ReconciliationCompletedEvent;
      invalidate(ROOT_KEYS.reconciliation, ROOT_KEYS.discrepancies, ROOT_KEYS.cases, ROOT_KEYS.dashboard);
      const s = p.stats;
      const found = s.missingInLeft + s.missingInRight + s.fieldConflicts + s.duplicates;
      toast.success(`Reconciliation ${shortId(p.runId)} completed`, {
        description: `${p.entityType}: ${fmtNumber(s.matched)} matched, ${fmtNumber(found)} discrepancies, ${fmtNumber(s.autoResolved)} auto-resolved`,
        href: `/reconciliation/runs/${p.runId}`,
      });
      return;
    }
    case 'reconciliation.failed': {
      const p = update.payload as ReconciliationFailedEvent;
      invalidate(ROOT_KEYS.reconciliation, ROOT_KEYS.dashboard);
      toast.error(`Reconciliation ${shortId(p.runId)} failed`, { description: p.reason, href: `/reconciliation/runs/${p.runId}` });
      return;
    }
    case 'rules.published': {
      const p = update.payload as RuleSetPublishedEvent;
      invalidate(ROOT_KEYS.rules);
      toast.info(`Rule set v${p.version} published`, { description: p.entityType, href: `/rules/${p.ruleSetId}` });
      return;
    }
    case 'cases.opened': {
      const p = update.payload as CaseOpenedEvent;
      invalidate(ROOT_KEYS.cases, ROOT_KEYS.discrepancies, ROOT_KEYS.dashboard);
      if (p.priority === 'Urgent' || p.priority === 'High') {
        toast.warning(`${p.priority} case opened`, { description: p.assignedRole ? `Assigned to role ${p.assignedRole}` : undefined, href: `/cases/${p.caseId}` });
      }
      return;
    }
    case 'cases.state-changed': {
      const p = update.payload as CaseStateChangedEvent;
      invalidate(ROOT_KEYS.cases, ROOT_KEYS.dashboard);
      if (p.toState === 'Resolved') {
        toast.success(`Case ${shortId(p.caseId)} resolved`, { href: `/cases/${p.caseId}` });
      }
      return;
    }
    case 'cases.approved':
      invalidate(ROOT_KEYS.cases, ROOT_KEYS.discrepancies);
      return;
    case 'cases.escalated': {
      const p = update.payload as CaseEscalatedEvent;
      invalidate(ROOT_KEYS.cases, ROOT_KEYS.dashboard);
      toast.warning(`Case ${shortId(p.caseId)} escalated to level ${p.escalationLevel}`, { href: `/cases/${p.caseId}` });
      return;
    }
    case 'operations.dead-lettered': {
      const p = update.payload as MessageDeadLetteredEvent;
      invalidate(ROOT_KEYS.operations, ROOT_KEYS.dashboard);
      toast.error(`Message dead-lettered: ${humanize(p.messageType)}`, { description: p.reason, href: '/operations' });
      return;
    }
    default:
      // Unknown kind from a newer server: refresh everything cheap rather than ignore it.
      invalidate(ROOT_KEYS.dashboard);
  }
}

/** Short human description used by the activity feed. */
export function describeUpdate(update: LiveUpdate): { title: string; detail?: string; href?: string } {
  const p = update.payload as unknown as Record<string, unknown>;
  const str = (k: string) => {
    const v = p[k];
    return typeof v === 'string' ? v : undefined;
  };
  const num = (k: string) => {
    const v = p[k];
    return typeof v === 'number' ? v : undefined;
  };
  switch (update.kind) {
    case 'ingestion.batch-received':
      return { title: 'Batch received', detail: `${str('entityType') ?? ''} · ${shortId(str('batchId'))}`, href: '/ingestion' };
    case 'ingestion.batch-completed':
      return { title: 'Batch completed', detail: `${str('entityType') ?? ''} · ${fmtNumber(num('accepted'))} accepted · ${fmtNumber(num('rejected'))} rejected`, href: '/ingestion' };
    case 'ingestion.batch-failed':
      return { title: 'Batch failed', detail: str('reason'), href: '/ingestion' };
    case 'reconciliation.requested':
      return { title: 'Reconciliation requested', detail: str('entityType'), href: `/reconciliation/runs/${str('runId') ?? ''}` };
    case 'reconciliation.completed': {
      const stats = p.stats as { matched?: number } | undefined;
      return { title: 'Reconciliation completed', detail: `${str('entityType') ?? ''} · ${fmtNumber(stats?.matched)} matched`, href: `/reconciliation/runs/${str('runId') ?? ''}` };
    }
    case 'reconciliation.failed':
      return { title: 'Reconciliation failed', detail: str('reason'), href: `/reconciliation/runs/${str('runId') ?? ''}` };
    case 'rules.published':
      return { title: 'Rule set published', detail: `${str('entityType') ?? ''} v${num('version') ?? ''}`, href: `/rules/${str('ruleSetId') ?? ''}` };
    case 'cases.opened':
      return { title: `Case opened (${str('priority') ?? ''})`, detail: str('assignedRole') ? `Role ${str('assignedRole')}` : undefined, href: `/cases/${str('caseId') ?? ''}` };
    case 'cases.state-changed':
      return { title: 'Case state changed', detail: `${humanize(str('fromState'))} → ${humanize(str('toState'))} by ${str('actor') ?? ''}`, href: `/cases/${str('caseId') ?? ''}` };
    case 'cases.approved':
      return { title: 'Case approved', detail: `by ${str('approvedBy') ?? ''}`, href: `/cases/${str('caseId') ?? ''}` };
    case 'cases.escalated':
      return { title: `Case escalated (L${num('escalationLevel') ?? ''})`, href: `/cases/${str('caseId') ?? ''}` };
    case 'operations.dead-lettered':
      return { title: 'Message dead-lettered', detail: `${str('messageType') ?? ''} · ${str('reason') ?? ''}`, href: '/operations' };
    default:
      return { title: update.kind };
  }
}
