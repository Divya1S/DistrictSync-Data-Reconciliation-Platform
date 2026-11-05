import { useMemo, useState } from 'react';
import { WORKFLOW_STATUSES, type DeadLetterDto, type WorkflowInstanceDto, type WorkflowStatus } from '@/api/types';
import { useAuth } from '@/auth/useAuth';
import { Icons } from '@/components/layout/icons';
import { ErrorAlert } from '@/components/ui/Alert';
import { Badge, StatusBadge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { DataTable, type Column } from '@/components/ui/DataTable';
import { Checkbox, Select } from '@/components/ui/Field';
import { FilterBar, FilterField } from '@/components/ui/FilterBar';
import { KeyValueList } from '@/components/ui/KeyValue';
import { Modal } from '@/components/ui/Modal';
import { CopyButton, IdChip, Mono } from '@/components/ui/Mono';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { Skeleton } from '@/components/ui/Skeleton';
import { toast } from '@/components/ui/toastStore';
import { cn } from '@/lib/cn';
import { fmtDateTime, fmtNumber, fmtRelative, fmtSeconds, humanize, tryPrettyJson } from '@/lib/format';
import { useNow } from '@/lib/useNow';
import { useSearchFilters } from '@/lib/useSearchFilters';
import { useDeadLetters, useOutboxHealth, useReplayDeadLetter, useWorkflows } from './hooks';

const DEFAULTS = { includeReplayed: false, dlPage: 1, dlPageSize: 25, wfStatus: '', wfPage: 1, wfPageSize: 25 };

export default function OperationsPage() {
  const [filters, setFilters] = useSearchFilters(DEFAULTS);
  const deadLetters = useDeadLetters({ includeReplayed: filters.includeReplayed, page: filters.dlPage, pageSize: filters.dlPageSize });
  const workflows = useWorkflows({ status: filters.wfStatus as WorkflowStatus | '', page: filters.wfPage, pageSize: filters.wfPageSize });
  const outbox = useOutboxHealth();
  const replay = useReplayDeadLetter();
  const auth = useAuth();
  const now = useNow();
  const [viewing, setViewing] = useState<DeadLetterDto | null>(null);
  const canReplay = auth.hasRole('admin');

  const dlColumns = useMemo<Array<Column<DeadLetterDto>>>(
    () => [
      { key: 'id', header: 'Id', cell: (d) => <IdChip id={d.id} /> },
      { key: 'type', header: 'Message type', cell: (d) => <code className="mono">{d.messageType}</code> },
      { key: 'queue', header: 'Queue / routing key', cell: (d) => (<div className="min-w-0"><p className="mono truncate max-w-56" title={d.queue}>{d.queue}</p><p className="mono text-[11px] text-muted truncate max-w-56" title={d.routingKey}>{d.routingKey}</p></div>) },
      { key: 'reason', header: 'Reason', cell: (d) => <span className="text-crit-ink line-clamp-2 max-w-md" title={d.reason}>{d.reason}</span> },
      { key: 'attempts', header: 'Attempts', align: 'right', cell: (d) => <span className="tabular-nums">{d.attempts}</span> },
      { key: 'at', header: 'Dead-lettered', cell: (d) => <span className="whitespace-nowrap" title={fmtDateTime(d.deadLetteredAt)}>{fmtRelative(d.deadLetteredAt, now)}</span> },
      { key: 'replayed', header: 'Replayed', cell: (d) => (d.replayedAt ? <Badge tone="good" title={fmtDateTime(d.replayedAt)}>replayed {fmtRelative(d.replayedAt, now)}</Badge> : <Badge tone="crit" dot>pending</Badge>) },
      {
        key: 'actions',
        header: <span className="sr-only">Actions</span>,
        align: 'right',
        cell: (d) => (
          <div className="flex items-center justify-end gap-1">
            <Button size="xs" variant="ghost" onClick={() => setViewing(d)}>Payload</Button>
            {!d.replayedAt && (
              <Button
                size="xs"
                loading={replay.isPending && replay.variables === d.id}
                disabled={!canReplay}
                title={canReplay ? 'Re-publish the original message' : 'Requires the admin role'}
                onClick={() => replay.mutate(d.id, { onSuccess: () => toast.success('Message replayed'), onError: (e) => toast.error('Replay failed', { description: e.message }) })}
                icon={<Icons.play className="size-3" />}
              >
                Replay
              </Button>
            )}
          </div>
        ),
      },
    ],
    [now, replay, canReplay],
  );

  const wfColumns = useMemo<Array<Column<WorkflowInstanceDto>>>(
    () => [
      { key: 'type', header: 'Workflow', cell: (w) => (<div><p className="font-medium text-ink">{humanize(w.workflowType)}</p><p className="text-[11px] text-muted"><IdChip id={w.id} /></p></div>) },
      { key: 'subject', header: 'Subject', cell: (w) => <IdChip id={w.subjectId} /> },
      { key: 'status', header: 'Status', cell: (w) => <StatusBadge value={w.status} /> },
      { key: 'wake', header: 'Wake up', cell: (w) => <span className={cn('whitespace-nowrap', w.status === 'Scheduled' && new Date(w.wakeUpAt).getTime() < now && 'text-warn-ink')} title={fmtDateTime(w.wakeUpAt)}>{fmtRelative(w.wakeUpAt, now)}</span> },
      { key: 'attempts', header: 'Attempts', align: 'right', cell: (w) => <span className="tabular-nums">{w.attempts}</span> },
      { key: 'lock', header: 'Lock', cell: (w) => (w.lockOwner ? <span className="mono text-xs" title={w.lockExpiresAt ? `expires ${fmtDateTime(w.lockExpiresAt)}` : undefined}>{w.lockOwner}</span> : <span className="text-muted">—</span>) },
      { key: 'error', header: 'Last error', cell: (w) => (w.lastError ? <span className="text-crit-ink line-clamp-2 max-w-sm" title={w.lastError}>{w.lastError}</span> : <span className="text-muted">—</span>) },
      { key: 'updated', header: 'Updated', cell: (w) => <span className="whitespace-nowrap" title={fmtDateTime(w.updatedAt)}>{fmtRelative(w.updatedAt, now)}</span> },
    ],
    [now],
  );

  const ob = outbox.data;
  const outboxTone = ob ? (ob.failed > 0 ? 'crit' : (ob.oldestPendingAgeSeconds ?? 0) > 60 ? 'warn' : 'good') : 'neutral';

  return (
    <div className="space-y-4">
      <PageHeader title="Operations" description="Messaging health: the transactional outbox, dead-lettered messages and long-running workflow instances." />

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <Card title="Outbox" description="Events waiting to be published to RabbitMQ" actions={<Badge tone={outboxTone} dot pulse={outboxTone === 'crit'}>{ob ? (ob.failed > 0 ? 'failures' : (ob.oldestPendingAgeSeconds ?? 0) > 60 ? 'lagging' : 'healthy') : '…'}</Badge>}>
          {outbox.isLoading ? (
            <Skeleton className="h-16" />
          ) : outbox.isError ? (
            <ErrorAlert error={outbox.error} />
          ) : ob ? (
            <KeyValueList
              columns={3}
              items={[
                { label: 'Pending', value: <span className={cn('text-xl font-semibold tabular-nums', ob.pending > 0 && 'text-warn-ink')}>{fmtNumber(ob.pending)}</span> },
                { label: 'Failed', value: <span className={cn('text-xl font-semibold tabular-nums', ob.failed > 0 && 'text-crit-ink')}>{fmtNumber(ob.failed)}</span> },
                { label: 'Oldest pending', value: <span className="text-xl font-semibold tabular-nums">{fmtSeconds(ob.oldestPendingAgeSeconds)}</span> },
              ]}
            />
          ) : null}
        </Card>
        <Card className="lg:col-span-2" title="What to look for" description="Runbook notes">
          <ul className="text-sm text-ink-2 space-y-1.5 list-disc pl-4">
            <li><strong className="text-ink">Outbox pending growing</strong> with a rising oldest age means the publisher cannot reach RabbitMQ; check the broker and the worker logs.</li>
            <li><strong className="text-ink">Dead letters</strong> are messages whose consumer failed after all retries. Inspect the payload, fix the cause, then replay. Replays are idempotent via the inbox.</li>
            <li><strong className="text-ink">Scheduled workflows</strong> whose wake-up time is in the past are stalled; a stale lock owner usually means a worker died mid-step.</li>
          </ul>
        </Card>
      </div>

      <Card title="Dead letters" description={deadLetters.data ? `${fmtNumber(deadLetters.data.totalCount)} message${deadLetters.data.totalCount === 1 ? '' : 's'}` : undefined} bodyClassName="p-0">
        <FilterBar trailing={<Button size="sm" variant="ghost" onClick={() => void deadLetters.refetch()} loading={deadLetters.isFetching && !deadLetters.isLoading} icon={<Icons.refresh className="size-3.5" />}>Refresh</Button>}>
          <Checkbox label="Include replayed" checked={filters.includeReplayed} onChange={(e) => setFilters({ includeReplayed: e.target.checked, dlPage: 1 })} />
        </FilterBar>
        {deadLetters.isError && <ErrorAlert error={deadLetters.error} className="m-4" />}
        <DataTable columns={dlColumns} rows={deadLetters.data?.items} rowKey={(d) => d.id} isLoading={deadLetters.isLoading || deadLetters.isPlaceholderData} emptyTitle="No dead letters" emptyDescription="Every consumer is keeping up." caption="Dead-lettered messages" maxHeight="max-h-[50vh]" />
        {deadLetters.data && <Pagination page={deadLetters.data.page} pageSize={deadLetters.data.pageSize} totalCount={deadLetters.data.totalCount} onPageChange={(dlPage) => setFilters({ dlPage })} onPageSizeChange={(dlPageSize) => setFilters({ dlPageSize, dlPage: 1 })} />}
      </Card>

      <Card title="Workflow instances" description="Durable, resumable processes such as case SLA escalation and resolution application" bodyClassName="p-0">
        <FilterBar trailing={<Button size="sm" variant="ghost" onClick={() => void workflows.refetch()} loading={workflows.isFetching && !workflows.isLoading} icon={<Icons.refresh className="size-3.5" />}>Refresh</Button>}>
          <FilterField label="Status">
            <Select value={filters.wfStatus} onChange={(e) => setFilters({ wfStatus: e.target.value, wfPage: 1 })} className="w-40 h-8">
              <option value="">All statuses</option>
              {WORKFLOW_STATUSES.map((s) => (<option key={s} value={s}>{s}</option>))}
            </Select>
          </FilterField>
        </FilterBar>
        {workflows.isError && <ErrorAlert error={workflows.error} className="m-4" />}
        <DataTable columns={wfColumns} rows={workflows.data?.items} rowKey={(w) => w.id} isLoading={workflows.isLoading || workflows.isPlaceholderData} emptyTitle="No workflow instances" emptyDescription="Instances appear when cases are opened or resolutions approved." caption="Workflow instances" maxHeight="max-h-[50vh]" />
        {workflows.data && <Pagination page={workflows.data.page} pageSize={workflows.data.pageSize} totalCount={workflows.data.totalCount} onPageChange={(wfPage) => setFilters({ wfPage })} onPageSizeChange={(wfPageSize) => setFilters({ wfPageSize, wfPage: 1 })} />}
      </Card>

      <Modal open={viewing !== null} onClose={() => setViewing(null)} title={viewing ? `Payload · ${viewing.messageType}` : ''} description={viewing ? `${viewing.queue} · ${viewing.routingKey} · ${viewing.attempts} attempt${viewing.attempts === 1 ? '' : 's'}` : undefined} size="lg" footer={<>{viewing && <CopyButton value={viewing.payload} label="Copy payload" />}<Button onClick={() => setViewing(null)}>Close</Button></>}>
        {viewing && (
          <div className="space-y-3">
            <div className="rounded-md border border-crit/40 bg-crit/8 px-3 py-2 text-sm"><p className="text-[11px] uppercase tracking-wide text-muted">Failure reason</p><p className="text-ink whitespace-pre-wrap break-words">{viewing.reason}</p></div>
            <Mono block className="max-h-[50vh] overflow-auto text-xs">{tryPrettyJson(viewing.payload)}</Mono>
          </div>
        )}
      </Modal>
    </div>
  );
}
