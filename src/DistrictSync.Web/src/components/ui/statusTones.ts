export type Tone = 'neutral' | 'info' | 'good' | 'warn' | 'serious' | 'crit' | 'violet';

/** One consistent colour per enum value, across the whole console. */
const STATUS_TONES: Record<string, Tone> = {
  // IngestionBatchStatus / ReconciliationRunStatus / WorkflowStatus
  Received: 'neutral',
  Queued: 'neutral',
  Scheduled: 'neutral',
  Processing: 'info',
  Running: 'info',
  Completed: 'good',
  Failed: 'crit',
  Duplicate: 'warn',
  Cancelled: 'neutral',
  // DiscrepancyStatus
  Open: 'warn',
  AutoResolved: 'good',
  CaseOpened: 'violet',
  Resolved: 'good',
  Ignored: 'neutral',
  // DiscrepancyType
  MissingInLeft: 'serious',
  MissingInRight: 'serious',
  FieldConflict: 'warn',
  // Severity / CasePriority
  Low: 'neutral',
  Medium: 'info',
  Normal: 'info',
  High: 'serious',
  Urgent: 'crit',
  Critical: 'crit',
  // CaseState
  Assigned: 'info',
  PendingApproval: 'violet',
  Approved: 'good',
  Escalated: 'crit',
  // RuleSetStatus / RuleActionKind
  Draft: 'warn',
  Published: 'good',
  Retired: 'neutral',
  AutoResolve: 'good',
  OpenCase: 'violet',
  Ignore: 'neutral',
};

/** Values that are "in flight" and get a pulsing dot. */
export const LIVE_STATUSES = new Set(['Processing', 'Running']);

export function toneFor(value: string | null | undefined): Tone {
  return (value && STATUS_TONES[value]) || 'neutral';
}
