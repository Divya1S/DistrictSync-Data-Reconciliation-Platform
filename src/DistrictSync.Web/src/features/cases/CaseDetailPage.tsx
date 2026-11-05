import { useMemo, useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router';
import { RESOLUTION_STRATEGIES, type CaseDetailDto, type CaseHistoryEntryDto, type ResolutionStrategy } from '@/api/types';
import { useAuth } from '@/auth/useAuth';
import { Icons } from '@/components/layout/icons';
import { Alert, ErrorAlert } from '@/components/ui/Alert';
import { Badge, StatusBadge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Field, Input, Select, Textarea } from '@/components/ui/Field';
import { KeyValueList } from '@/components/ui/KeyValue';
import { Modal } from '@/components/ui/Modal';
import { IdChip, Mono } from '@/components/ui/Mono';
import { PageHeader } from '@/components/ui/PageHeader';
import { SkeletonLines } from '@/components/ui/Skeleton';
import { toast } from '@/components/ui/toastStore';
import { cn } from '@/lib/cn';
import { fmtDateTime, fmtRelative, humanize, tryPrettyJson } from '@/lib/format';
import { useNow } from '@/lib/useNow';
import { RecordCompare } from '@/features/reconciliation/RecordCompare';
import { useCase, useCaseAction, type CaseAction } from './hooks';

type DialogKind = 'assign' | 'propose' | 'approve' | 'reject' | 'cancel' | 'comment' | null;

const STRATEGY_HELP: Record<ResolutionStrategy, string> = {
  PreferLeft: 'Copy the left record’s values over the right.',
  PreferRight: 'Copy the right record’s values over the left.',
  PreferNewest: 'Keep whichever record was updated most recently in its source.',
  PreferHigherTrust: 'Keep the record from the source with the higher trust rank.',
};

export default function CaseDetailPage() {
  const { id = '' } = useParams();
  const query = useCase(id);
  const action = useCaseAction(id);
  const auth = useAuth();
  const now = useNow();
  const [dialog, setDialog] = useState<DialogKind>(null);

  const detail = query.data;
  const c = detail?.case;
  const terminal = c?.state === 'Resolved' || c?.state === 'Cancelled';
  const readOnly = auth.isViewerOnly;
  const can = {
    assign: !!c && ['Open', 'Assigned', 'Escalated'].includes(c.state),
    propose: !!c && ['Assigned', 'Escalated'].includes(c.state),
    decide: !!c && c.state === 'PendingApproval' && auth.canApprove,
    cancel: !!c && !terminal,
    comment: !!c,
  };
  const selfApproval = !!detail?.proposal && !!auth.userId && detail.proposal.proposedBy.toLowerCase() === auth.userId.toLowerCase();

  const run = (a: CaseAction, successMessage: string) => {
    if (!c) return;
    action.mutate(
      { action: a, eTag: c.eTag },
      {
        onSuccess: () => {
          toast.success(successMessage);
          setDialog(null);
        },
      },
    );
  };

  const close = () => {
    setDialog(null);
    action.reset();
  };

  return (
    <div className="space-y-4">
      <PageHeader
        crumbs={[{ label: 'Cases', to: '/cases' }, { label: `Case ${id.slice(0, 8)}` }]}
        title={c?.title ?? 'Case'}
        meta={
          c && (
            <span className="flex items-center gap-1.5 flex-wrap">
              <StatusBadge value={c.priority} />
              <StatusBadge value={c.state} />
              {c.isOverdue && !terminal && <Badge tone="crit" dot>overdue</Badge>}
              {c.escalationLevel > 0 && <Badge tone="crit">escalation L{c.escalationLevel}</Badge>}
            </span>
          )
        }
        description={c ? `Opened ${fmtDateTime(c.openedAt)} · due ${fmtDateTime(c.dueAt)} (${fmtRelative(c.dueAt, now)})` : undefined}
        actions={
          c && (
            <div className="flex items-center gap-2 flex-wrap" role="group" aria-label="Case actions">
              {can.assign && <Button onClick={() => setDialog('assign')} disabled={readOnly}>Assign</Button>}
              {can.propose && <Button variant="primary" onClick={() => setDialog('propose')} disabled={readOnly}>Propose resolution</Button>}
              {c.state === 'PendingApproval' && (
                <>
                  <Button variant="primary" onClick={() => setDialog('approve')} disabled={!can.decide || selfApproval} title={!auth.canApprove ? 'Requires the approver or admin role' : selfApproval ? 'A proposal cannot be approved by the person who made it' : undefined} icon={<Icons.check className="size-4" />}>
                    Approve
                  </Button>
                  <Button onClick={() => setDialog('reject')} disabled={!can.decide} title={!auth.canApprove ? 'Requires the approver or admin role' : undefined} icon={<Icons.x className="size-4" />}>
                    Reject
                  </Button>
                </>
              )}
              {can.comment && <Button variant="ghost" onClick={() => setDialog('comment')} disabled={readOnly}>Add comment</Button>}
              {can.cancel && <Button variant="ghost" className="text-crit-ink" onClick={() => setDialog('cancel')} disabled={readOnly}>Cancel case</Button>}
              <Button variant="ghost" onClick={() => void query.refetch()} loading={query.isFetching && !query.isLoading} aria-label="Refresh" title="Refresh" icon={<Icons.refresh className="size-3.5" />} />
            </div>
          )
        }
      />

      {readOnly && <Alert tone="info">Your session has the viewer role only; actions are disabled.</Alert>}
      {query.isError && <ErrorAlert error={query.error} title="Could not load the case" />}
      {c?.state === 'PendingApproval' && !auth.canApprove && <Alert tone="info">This case is awaiting approval. Approving or rejecting requires the approver or admin role.</Alert>}
      {c?.state === 'Approved' && <Alert tone="success" title="Approved">The workflow engine is applying the resolution; the case moves to Resolved once the data has been updated.</Alert>}

      <div className="grid grid-cols-1 xl:grid-cols-3 gap-4">
        <div className="xl:col-span-2 space-y-4">
          <Card
            title="Discrepancy"
            description={detail ? `${humanize(detail.discrepancy.discrepancy.type)} · ${detail.discrepancy.discrepancy.severity} severity · ${detail.discrepancy.discrepancy.entityType}` : undefined}
            actions={detail && <Link to={`/reconciliation/runs/${detail.discrepancy.discrepancy.runId}?d=${detail.discrepancy.discrepancy.id}`} className="text-xs text-accent hover:underline inline-flex items-center gap-1">View in run <Icons.external className="size-3" /></Link>}
          >
            {query.isLoading ? <SkeletonLines lines={8} /> : detail ? <RecordCompare detail={detail.discrepancy} /> : null}
          </Card>

          {detail?.proposal && (
            <Card title="Proposed resolution" description={`by ${detail.proposal.proposedBy} · ${fmtDateTime(detail.proposal.proposedAt)}`}>
              <div className="space-y-3">
                <KeyValueList
                  columns={2}
                  items={[
                    { label: 'Strategy', value: detail.proposal.strategy ? <Badge tone="info">{humanize(detail.proposal.strategy)}</Badge> : 'Manual values' },
                    { label: 'Approved by', value: detail.approvedBy ?? (c?.state === 'PendingApproval' ? 'pending' : '—') },
                  ]}
                />
                {detail.proposal.manualValuesJson && <Mono block>{tryPrettyJson(detail.proposal.manualValuesJson)}</Mono>}
                <p className="text-sm text-ink whitespace-pre-wrap border-l-2 border-line-2 pl-3">{detail.proposal.note}</p>
              </div>
            </Card>
          )}
        </div>

        <div className="space-y-4">
          <Card title="Details">
            {query.isLoading ? (
              <SkeletonLines lines={6} />
            ) : c ? (
              <KeyValueList
                items={[
                  { label: 'Case id', value: <IdChip id={c.id} length={36} /> },
                  { label: 'Assignee', value: c.assigneeId ?? <span className="text-muted">Unassigned</span> },
                  { label: 'Assigned role', value: c.assignedRole ?? '—' },
                  { label: 'Due', value: <span className={cn(c.isOverdue && !terminal && 'text-crit-ink font-medium')}>{fmtDateTime(c.dueAt)}</span> },
                  { label: 'Closed', value: fmtDateTime(c.closedAt) },
                  { label: 'Run', value: <Link to={`/reconciliation/runs/${c.runId}`} className="text-accent hover:underline mono">{c.runId.slice(0, 8)}</Link> },
                  { label: 'Concurrency token', value: c.eTag, mono: true },
                ]}
              />
            ) : null}
          </Card>
          <Card title="History" bodyClassName="p-0">
            {query.isLoading ? <SkeletonLines lines={5} className="p-4" /> : detail ? <HistoryTimeline history={detail.history} /> : null}
          </Card>
        </div>
      </div>

      {c && detail && (
        <>
          {dialog === 'assign' && <AssignDialog open onClose={close} defaultAssignee={c.assigneeId ?? auth.userId ?? ''} me={auth.userId} pending={action.isPending} error={action.error} onSubmit={(assigneeId) => run({ kind: 'assign', body: { assigneeId } }, `Assigned to ${assigneeId}`)} />}
          {dialog === 'propose' && <ProposeDialog open onClose={close} detail={detail} pending={action.isPending} error={action.error} onSubmit={(body) => run({ kind: 'propose', body }, 'Resolution proposed; awaiting approval')} />}
          {dialog === 'reject' && <ReasonDialog open onClose={close} title="Reject proposal" label="Reason" submitLabel="Reject" danger pending={action.isPending} error={action.error} onSubmit={(reason) => run({ kind: 'reject', body: { reason } }, 'Proposal rejected')} />}
          {dialog === 'cancel' && <ReasonDialog open onClose={close} title="Cancel case" label="Reason" submitLabel="Cancel case" danger pending={action.isPending} error={action.error} description="The discrepancy stays as it is; the case is closed without a resolution." onSubmit={(reason) => run({ kind: 'cancel', body: { reason } }, 'Case cancelled')} />}
          {dialog === 'comment' && <ReasonDialog open onClose={close} title="Add comment" label="Comment" submitLabel="Add comment" pending={action.isPending} error={action.error} onSubmit={(note) => run({ kind: 'comments', body: { note } }, 'Comment added')} />}
          <Modal
            open={dialog === 'approve'}
            onClose={close}
            title="Approve resolution"
            size="sm"
            footer={
              <>
                <Button onClick={close}>Back</Button>
                <Button variant="primary" loading={action.isPending} onClick={() => run({ kind: 'approve' }, 'Resolution approved')} data-autofocus>Approve</Button>
              </>
            }
          >
            <p className="text-sm text-ink-2">
              Approve the proposal by <strong className="text-ink">{detail.proposal?.proposedBy}</strong>? The workflow engine will apply {detail.proposal?.strategy ? <Badge tone="info">{humanize(detail.proposal.strategy)}</Badge> : 'the manual values'} to the data and resolve the discrepancy.
            </p>
            <ErrorAlert error={action.error} className="mt-3" />
          </Modal>
        </>
      )}
    </div>
  );
}

const ACTION_TONE: Record<string, 'info' | 'good' | 'crit' | 'violet' | 'neutral' | 'warn'> = {
  open: 'info',
  assign: 'info',
  propose: 'violet',
  approve: 'good',
  reject: 'warn',
  resolve: 'good',
  escalate: 'crit',
  cancel: 'neutral',
  comment: 'neutral',
};

function HistoryTimeline({ history }: { history: CaseHistoryEntryDto[] }) {
  const items = useMemo(() => [...history].sort((a, b) => new Date(b.at).getTime() - new Date(a.at).getTime()), [history]);
  if (items.length === 0) return <p className="p-4 text-sm text-muted">No history recorded.</p>;
  return (
    <ol className="divide-y divide-line max-h-[60vh] overflow-auto scroll-thin">
      {items.map((h, i) => (
        <li key={`${h.at}-${i}`} className="flex gap-3 px-4 py-2.5">
          <div className="pt-1.5">
            <span className={cn('block size-2 rounded-full', `bg-${ACTION_TONE[h.action] === 'info' ? 'accent' : ACTION_TONE[h.action] ?? 'muted'}`)} aria-hidden="true" />
          </div>
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2 flex-wrap">
              <Badge tone={ACTION_TONE[h.action] ?? 'neutral'}>{humanize(h.action)}</Badge>
              <span className="text-xs text-ink-2 truncate">{h.actor}</span>
              <time className="text-[11px] text-muted ml-auto whitespace-nowrap" dateTime={h.at} title={fmtDateTime(h.at)}>{fmtDateTime(h.at)}</time>
            </div>
            <p className="text-[11px] text-muted mt-0.5">{h.fromState ? `${humanize(h.fromState)} → ` : ''}{humanize(h.toState)}</p>
            {h.note && <p className="text-sm text-ink mt-1 whitespace-pre-wrap break-words">{h.note}</p>}
          </div>
        </li>
      ))}
    </ol>
  );
}

function AssignDialog({ open, onClose, defaultAssignee, me, pending, error, onSubmit }: { open: boolean; onClose: () => void; defaultAssignee: string; me: string | null; pending: boolean; error: unknown; onSubmit: (assigneeId: string) => void }) {
  const [assignee, setAssignee] = useState(defaultAssignee);
  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (assignee.trim()) onSubmit(assignee.trim());
  };
  return (
    <Modal open={open} onClose={onClose} title="Assign case" size="sm" footer={<><Button onClick={onClose}>Cancel</Button><Button variant="primary" type="submit" form="assign-form" loading={pending} disabled={!assignee.trim()}>Assign</Button></>}>
      <form id="assign-form" onSubmit={submit} className="space-y-3">
        <Field label="Assignee user id" required>
          {(p) => (
            <div className="flex gap-2">
              <Input {...p} value={assignee} onChange={(e) => setAssignee(e.target.value)} required data-autofocus />
              {me && <Button onClick={() => setAssignee(me)} disabled={assignee === me}>Me</Button>}
            </div>
          )}
        </Field>
        <ErrorAlert error={error} />
      </form>
    </Modal>
  );
}

function ProposeDialog({ open, onClose, detail, pending, error, onSubmit }: { open: boolean; onClose: () => void; detail: CaseDetailDto; pending: boolean; error: unknown; onSubmit: (body: { strategy: ResolutionStrategy | null; manualValues: Record<string, string | null> | null; note: string }) => void }) {
  const [mode, setMode] = useState<'strategy' | 'manual'>('strategy');
  const [strategy, setStrategy] = useState<ResolutionStrategy>('PreferHigherTrust');
  const [note, setNote] = useState('');
  const differences = detail.discrepancy.discrepancy.differences;
  const [manual, setManual] = useState<Record<string, string>>(() => Object.fromEntries(differences.map((d) => [d.field, d.leftValue ?? d.rightValue ?? ''])));

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!note.trim()) return;
    onSubmit(mode === 'strategy' ? { strategy, manualValues: null, note: note.trim() } : { strategy: null, manualValues: manual, note: note.trim() });
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Propose a resolution"
      description="An approver reviews the proposal before anything changes in the data."
      size="lg"
      footer={<><Button onClick={onClose}>Cancel</Button><Button variant="primary" type="submit" form="propose-form" loading={pending} disabled={!note.trim() || (mode === 'manual' && differences.length === 0)}>Submit proposal</Button></>}
    >
      <form id="propose-form" onSubmit={submit} className="space-y-4">
        <fieldset className="grid grid-cols-2 gap-2">
          <legend className="sr-only">Resolution mode</legend>
          {(['strategy', 'manual'] as const).map((m) => (
            <label key={m} className={cn('flex items-start gap-2 rounded-md border px-3 py-2 cursor-pointer', mode === m ? 'border-accent bg-accent/8' : 'border-line hover:bg-surface-2')}>
              <input type="radio" name="mode" value={m} checked={mode === m} onChange={() => setMode(m)} className="mt-1 accent-[var(--accent)]" />
              <span>
                <span className="block text-sm font-medium text-ink">{m === 'strategy' ? 'Apply a strategy' : 'Enter values manually'}</span>
                <span className="block text-xs text-muted">{m === 'strategy' ? 'Pick which side wins for every differing field.' : 'Set the final value of each differing field yourself.'}</span>
              </span>
            </label>
          ))}
        </fieldset>

        {mode === 'strategy' ? (
          <Field label="Strategy" required hint={STRATEGY_HELP[strategy]}>
            {(p) => (
              <Select {...p} value={strategy} onChange={(e) => setStrategy(e.target.value as ResolutionStrategy)} data-autofocus>
                {RESOLUTION_STRATEGIES.map((s) => (<option key={s} value={s}>{humanize(s)}</option>))}
              </Select>
            )}
          </Field>
        ) : differences.length === 0 ? (
          <Alert tone="warning">This discrepancy has no field differences (a record is missing on one side), so there is nothing to enter manually. Use a strategy instead.</Alert>
        ) : (
          <div className="rounded-md border border-line overflow-hidden">
            <table className="w-full text-sm">
              <thead className="bg-surface-2 text-xs text-ink-2">
                <tr><th className="text-left px-3 py-1.5 font-semibold">Field</th><th className="text-left px-3 py-1.5 font-semibold">Left</th><th className="text-left px-3 py-1.5 font-semibold">Right</th><th className="text-left px-3 py-1.5 font-semibold">Final value</th></tr>
              </thead>
              <tbody className="divide-y divide-line">
                {differences.map((d) => (
                  <tr key={d.field}>
                    <td className="px-3 py-1.5 mono">{d.field}{d.isCritical && <Badge tone="crit" className="ml-1">crit</Badge>}</td>
                    <td className="px-3 py-1.5 mono text-ink-2"><button type="button" className="hover:underline text-left" onClick={() => setManual((m) => ({ ...m, [d.field]: d.leftValue ?? '' }))} title="Use left value">{d.leftValue ?? <i className="text-muted">null</i>}</button></td>
                    <td className="px-3 py-1.5 mono text-ink-2"><button type="button" className="hover:underline text-left" onClick={() => setManual((m) => ({ ...m, [d.field]: d.rightValue ?? '' }))} title="Use right value">{d.rightValue ?? <i className="text-muted">null</i>}</button></td>
                    <td className="px-3 py-1.5"><Input aria-label={`Final value for ${d.field}`} value={manual[d.field] ?? ''} onChange={(e) => setManual((m) => ({ ...m, [d.field]: e.target.value }))} mono className="h-7" /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <Field label="Note" required hint="Why this resolution is right; shown to the approver and kept in the history.">
          {(p) => <Textarea {...p} value={note} onChange={(e) => setNote(e.target.value)} required rows={3} />}
        </Field>
        <ErrorAlert error={error} />
      </form>
    </Modal>
  );
}

function ReasonDialog({ open, onClose, title, description, label, submitLabel, danger, pending, error, onSubmit }: { open: boolean; onClose: () => void; title: string; description?: string; label: string; submitLabel: string; danger?: boolean; pending: boolean; error: unknown; onSubmit: (value: string) => void }) {
  const [value, setValue] = useState('');
  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (value.trim()) {
      onSubmit(value.trim());
      setValue('');
    }
  };
  return (
    <Modal open={open} onClose={onClose} title={title} description={description} size="sm" footer={<><Button onClick={onClose}>Back</Button><Button variant={danger ? 'danger' : 'primary'} type="submit" form="reason-form" loading={pending} disabled={!value.trim()}>{submitLabel}</Button></>}>
      <form id="reason-form" onSubmit={submit} className="space-y-3">
        <Field label={label} required>{(p) => <Textarea {...p} value={value} onChange={(e) => setValue(e.target.value)} required rows={3} data-autofocus />}</Field>
        <ErrorAlert error={error} />
      </form>
    </Modal>
  );
}
