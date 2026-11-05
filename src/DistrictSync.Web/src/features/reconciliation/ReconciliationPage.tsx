import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { RECONCILIATION_RUN_STATUSES, type ReconciliationRunDto } from '@/api/types';
import { Icons } from '@/components/layout/icons';
import { ErrorAlert } from '@/components/ui/Alert';
import { StatusBadge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { DataTable, type Column } from '@/components/ui/DataTable';
import { Select } from '@/components/ui/Field';
import { FilterBar, FilterField } from '@/components/ui/FilterBar';
import { IdChip } from '@/components/ui/Mono';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { toast } from '@/components/ui/toastStore';
import { fmtDateTime, fmtElapsed, fmtNumber, fmtRelative, humanize } from '@/lib/format';
import { useNow } from '@/lib/useNow';
import { useSearchFilters } from '@/lib/useSearchFilters';
import { liveStore } from '@/realtime/liveStore';
import { NewRunDialog } from './NewRunDialog';
import { useCancelRun, useRuns, type RunFilters } from './hooks';

const DEFAULTS = { status: '', page: 1, pageSize: 25 };

export default function ReconciliationPage() {
  const [filters, setFilters] = useSearchFilters(DEFAULTS);
  const query = useRuns(filters as RunFilters);
  const cancel = useCancelRun();
  const navigate = useNavigate();
  const [newOpen, setNewOpen] = useState(false);
  const now = useNow();

  const columns = useMemo<Array<Column<ReconciliationRunDto>>>(
    () => [
      {
        key: 'id',
        header: 'Run',
        cell: (r) => (
          <div>
            <Link to={`/reconciliation/runs/${r.id}`} className="font-medium text-accent hover:underline" onClick={(e) => e.stopPropagation()}>
              <IdChip id={r.id} className="text-accent" />
            </Link>
            <p className="text-xs text-muted">{r.entityType}</p>
          </div>
        ),
      },
      {
        key: 'pair',
        header: 'Left vs right',
        cell: (r) => (
          <span className="mono whitespace-nowrap">
            {r.leftSourceSystemCode} <span className="text-muted">⇄</span> {r.rightSourceSystemCode}
          </span>
        ),
      },
      {
        key: 'status',
        header: 'Status',
        cell: (r) => (
          <div className="space-y-1 min-w-36">
            <StatusBadge value={r.status} />
            {r.status === 'Running' && <RunLiveLine runId={r.id} />}
            {r.failureReason && <p className="text-xs text-crit-ink truncate max-w-56" title={r.failureReason}>{r.failureReason}</p>}
          </div>
        ),
      },
      { key: 'left', header: 'L / R records', align: 'right', cell: (r) => <span className="tabular-nums whitespace-nowrap">{fmtNumber(r.stats.leftRecords)} / {fmtNumber(r.stats.rightRecords)}</span> },
      { key: 'matched', header: 'Matched', align: 'right', cell: (r) => <span className="tabular-nums">{fmtNumber(r.stats.matched)}</span> },
      { key: 'missing', header: 'Missing L / R', align: 'right', cell: (r) => <span className="tabular-nums whitespace-nowrap">{fmtNumber(r.stats.missingInLeft)} / {fmtNumber(r.stats.missingInRight)}</span> },
      { key: 'conflicts', header: 'Conflicts', align: 'right', cell: (r) => <span className="tabular-nums">{fmtNumber(r.stats.fieldConflicts)}</span> },
      { key: 'dups', header: 'Dups', align: 'right', cell: (r) => <span className="tabular-nums">{fmtNumber(r.stats.duplicates)}</span> },
      { key: 'auto', header: 'Auto / cases', align: 'right', cell: (r) => <span className="tabular-nums whitespace-nowrap"><span className="text-good-ink">{fmtNumber(r.stats.autoResolved)}</span> / <span className="text-violet-ink">{fmtNumber(r.stats.casesOpened)}</span></span> },
      { key: 'rules', header: 'Rules', cell: (r) => (r.ruleSetVersion !== null ? <Link to={`/rules/${r.ruleSetId ?? ''}`} className="text-accent hover:underline" onClick={(e) => e.stopPropagation()}>v{r.ruleSetVersion}</Link> : <span className="text-muted">—</span>) },
      { key: 'requested', header: 'Requested', cell: (r) => <span title={`${fmtDateTime(r.requestedAt)} by ${r.requestedBy}`} className="whitespace-nowrap">{fmtRelative(r.requestedAt, now)}</span> },
      { key: 'duration', header: 'Duration', cell: (r) => <span className="tabular-nums whitespace-nowrap">{fmtElapsed(r.startedAt, r.completedAt, now)}</span> },
      {
        key: 'actions',
        header: <span className="sr-only">Actions</span>,
        align: 'right',
        cell: (r) =>
          r.status === 'Queued' || r.status === 'Running' ? (
            <Button
              size="xs"
              variant="danger"
              loading={cancel.isPending && cancel.variables === r.id}
              onClick={(e) => {
                e.stopPropagation();
                cancel.mutate(r.id, { onSuccess: () => toast.info('Cancellation requested'), onError: (err) => toast.error('Could not cancel', { description: err.message }) });
              }}
            >
              Cancel
            </Button>
          ) : null,
      },
    ],
    [now, cancel],
  );

  const data = query.data;
  return (
    <div className="space-y-4">
      <PageHeader
        title="Reconciliation"
        description="Compare two source systems for an entity type; discrepancies feed the rules engine and case queue."
        actions={
          <>
            <Button variant="primary" onClick={() => setNewOpen(true)} icon={<Icons.plus className="size-4" />}>New run</Button>
            <Button variant="ghost" onClick={() => void query.refetch()} loading={query.isFetching && !query.isLoading} icon={<Icons.refresh className="size-3.5" />}>Refresh</Button>
          </>
        }
      />
      <Card bodyClassName="p-0">
        <FilterBar>
          <FilterField label="Status">
            <Select value={filters.status} onChange={(e) => setFilters({ status: e.target.value })} className="w-40 h-8">
              <option value="">All statuses</option>
              {RECONCILIATION_RUN_STATUSES.map((s) => (
                <option key={s} value={s}>{humanize(s)}</option>
              ))}
            </Select>
          </FilterField>
        </FilterBar>
        {query.isError && <ErrorAlert error={query.error} className="m-4" />}
        <DataTable
          columns={columns}
          rows={data?.items}
          rowKey={(r) => r.id}
          isLoading={query.isLoading || query.isPlaceholderData}
          onRowClick={(r) => void navigate(`/reconciliation/runs/${r.id}`)}
          emptyTitle="No runs yet"
          emptyDescription="Queue a run to compare two sources."
          emptyAction={<Button variant="primary" size="sm" onClick={() => setNewOpen(true)}>New run</Button>}
          caption="Reconciliation runs"
        />
        {data && <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount} onPageChange={(page) => setFilters({ page })} onPageSizeChange={(pageSize) => setFilters({ pageSize })} />}
      </Card>
      <NewRunDialog open={newOpen} onClose={() => setNewOpen(false)} />
    </div>
  );
}

function RunLiveLine({ runId }: { runId: string }) {
  const p = liveStore.use((s) => s.reconciliationProgress[runId]);
  if (!p) return <p className="text-[11px] text-muted">waiting for progress…</p>;
  return (
    <p className="text-[11px] text-muted tabular-nums whitespace-nowrap">
      L {fmtNumber(p.leftRead)} · R {fmtNumber(p.rightRead)} · {fmtNumber(p.discrepancies)} found
    </p>
  );
}
