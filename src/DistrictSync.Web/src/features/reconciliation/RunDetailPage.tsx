import { useMemo } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
import { DISCREPANCY_STATUSES, DISCREPANCY_TYPES, SEVERITIES, type DiscrepancyDto } from '@/api/types';
import { Icons } from '@/components/layout/icons';
import { ErrorAlert } from '@/components/ui/Alert';
import { Badge, StatusBadge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { DataTable, type Column } from '@/components/ui/DataTable';
import { Input, Select } from '@/components/ui/Field';
import { FilterBar, FilterField } from '@/components/ui/FilterBar';
import { KeyValueList } from '@/components/ui/KeyValue';
import { IdChip } from '@/components/ui/Mono';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { ProgressBar } from '@/components/ui/ProgressBar';
import { SkeletonLines } from '@/components/ui/Skeleton';
import { toast } from '@/components/ui/toastStore';
import { fmtDateTime, fmtElapsed, fmtNumber, humanize } from '@/lib/format';
import { useNow } from '@/lib/useNow';
import { useSearchFilters } from '@/lib/useSearchFilters';
import { liveStore } from '@/realtime/liveStore';
import { DiscrepancyDrawer } from './DiscrepancyDrawer';
import { StatsBar } from './StatsBar';
import { useCancelRun, useDiscrepancies, useRun, type DiscrepancyFilters } from './hooks';

const DEFAULTS = { type: '', status: '', severity: '', matchKey: '', page: 1, pageSize: 25 };

export default function RunDetailPage() {
  const { id = '' } = useParams();
  const run = useRun(id);
  const cancel = useCancelRun();
  const [filters, setFilters] = useSearchFilters(DEFAULTS);
  const discrepancies = useDiscrepancies(id, filters as DiscrepancyFilters);
  const [params, setParams] = useSearchParams();
  const selected = params.get('d');
  const now = useNow();
  const live = liveStore.use((s) => s.reconciliationProgress[id]);

  const openDiscrepancy = (did: string | null) => {
    void setParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (did) next.set('d', did);
        else next.delete('d');
        return next;
      },
      { replace: true },
    );
  };

  const columns = useMemo<Array<Column<DiscrepancyDto>>>(
    () => [
      { key: 'id', header: 'Id', cell: (d) => <IdChip id={d.id} /> },
      { key: 'matchKey', header: 'Match key', cell: (d) => <code className="mono break-all">{d.matchKey}</code> },
      { key: 'type', header: 'Type', cell: (d) => <StatusBadge value={d.type} /> },
      { key: 'severity', header: 'Severity', cell: (d) => <StatusBadge value={d.severity} /> },
      { key: 'status', header: 'Status', cell: (d) => <StatusBadge value={d.status} /> },
      {
        key: 'fields',
        header: 'Differing fields',
        cell: (d) =>
          d.differences.length === 0 ? (
            <span className="text-muted">—</span>
          ) : (
            <span className="flex flex-wrap gap-1 max-w-72">
              {d.differences.slice(0, 4).map((f) => (
                <Badge key={f.field} tone={f.isCritical ? 'crit' : 'neutral'} className="mono font-normal">{f.field}</Badge>
              ))}
              {d.differences.length > 4 && <Badge tone="neutral">+{d.differences.length - 4}</Badge>}
            </span>
          ),
      },
      { key: 'detected', header: 'Detected', cell: (d) => <span className="whitespace-nowrap">{fmtDateTime(d.detectedAt)}</span> },
      { key: 'resolved', header: 'Resolved', cell: (d) => <span className="whitespace-nowrap">{fmtDateTime(d.resolvedAt)}</span> },
    ],
    [],
  );

  const r = run.data;
  const isActive = r?.status === 'Queued' || r?.status === 'Running';
  const data = discrepancies.data;

  return (
    <div className="space-y-4">
      <PageHeader
        crumbs={[{ label: 'Reconciliation', to: '/reconciliation' }, { label: `Run ${id.slice(0, 8)}` }]}
        title={r ? <span className="flex items-center gap-2 flex-wrap">{r.entityType} <span className="mono text-base text-ink-2">{r.leftSourceSystemCode} ⇄ {r.rightSourceSystemCode}</span></span> : 'Run'}
        meta={r && <StatusBadge value={r.status} />}
        description={r ? `Requested ${fmtDateTime(r.requestedAt)} by ${r.requestedBy}` : undefined}
        actions={
          <>
            {isActive && (
              <Button variant="danger" loading={cancel.isPending} onClick={() => cancel.mutate(id, { onSuccess: () => toast.info('Cancellation requested') })}>
                Cancel run
              </Button>
            )}
            <Button variant="ghost" onClick={() => { void run.refetch(); void discrepancies.refetch(); }} loading={run.isFetching && !run.isLoading} icon={<Icons.refresh className="size-3.5" />}>Refresh</Button>
          </>
        }
      />

      {run.isError && <ErrorAlert error={run.error} title="Could not load the run" />}

      {r?.status === 'Running' && (
        <Card title="Live progress" description="Streamed from the worker via SignalR">
          <div className="space-y-2">
            <ProgressBar indeterminate label="Reconciliation running" />
            <dl className="grid grid-cols-2 sm:grid-cols-4 gap-3 text-sm">
              <div><dt className="text-[11px] uppercase tracking-wide text-muted">Left read</dt><dd className="tabular-nums font-semibold">{fmtNumber(live?.leftRead ?? 0)}</dd></div>
              <div><dt className="text-[11px] uppercase tracking-wide text-muted">Right read</dt><dd className="tabular-nums font-semibold">{fmtNumber(live?.rightRead ?? 0)}</dd></div>
              <div><dt className="text-[11px] uppercase tracking-wide text-muted">Discrepancies</dt><dd className="tabular-nums font-semibold">{fmtNumber(live?.discrepancies ?? 0)}</dd></div>
              <div><dt className="text-[11px] uppercase tracking-wide text-muted">Last key</dt><dd className="mono truncate" title={live?.lastKey ?? undefined}>{live?.lastKey ?? r.checkpoint ?? '—'}</dd></div>
            </dl>
          </div>
        </Card>
      )}

      <div className="grid grid-cols-1 xl:grid-cols-3 gap-4">
        <Card title="Outcome" description="Matched records and discrepancies by kind" className="xl:col-span-2">
          {run.isLoading ? <SkeletonLines lines={4} /> : r ? <StatsBar stats={r.stats} /> : null}
        </Card>
        <Card title="Details">
          {run.isLoading ? (
            <SkeletonLines lines={6} />
          ) : r ? (
            <KeyValueList
              items={[
                { label: 'Run id', value: <IdChip id={r.id} length={36} /> },
                { label: 'Rule set', value: r.ruleSetVersion !== null ? <Link to={`/rules/${r.ruleSetId ?? ''}`} className="text-accent hover:underline">version {r.ruleSetVersion}</Link> : 'none applied' },
                { label: 'Started', value: fmtDateTime(r.startedAt) },
                { label: 'Completed', value: fmtDateTime(r.completedAt) },
                { label: 'Duration', value: fmtElapsed(r.startedAt, r.completedAt, now) },
                { label: 'Checkpoint', value: r.checkpoint ?? '—', mono: true },
                ...(r.failureReason ? [{ label: 'Failure reason', value: <span className="text-crit-ink">{r.failureReason}</span> }] : []),
              ]}
            />
          ) : null}
        </Card>
      </div>

      <Card title="Discrepancies" description={data ? `${fmtNumber(data.totalCount)} matching` : undefined} bodyClassName="p-0">
        <FilterBar>
          <FilterField label="Type">
            <Select value={filters.type} onChange={(e) => setFilters({ type: e.target.value })} className="w-40 h-8">
              <option value="">All</option>
              {DISCREPANCY_TYPES.map((t) => (<option key={t} value={t}>{humanize(t)}</option>))}
            </Select>
          </FilterField>
          <FilterField label="Status">
            <Select value={filters.status} onChange={(e) => setFilters({ status: e.target.value })} className="w-36 h-8">
              <option value="">All</option>
              {DISCREPANCY_STATUSES.map((s) => (<option key={s} value={s}>{humanize(s)}</option>))}
            </Select>
          </FilterField>
          <FilterField label="Severity">
            <Select value={filters.severity} onChange={(e) => setFilters({ severity: e.target.value })} className="w-32 h-8">
              <option value="">All</option>
              {SEVERITIES.map((s) => (<option key={s} value={s}>{s}</option>))}
            </Select>
          </FilterField>
          <FilterField label="Match key">
            <Input value={filters.matchKey} onChange={(e) => setFilters({ matchKey: e.target.value })} placeholder="exact match key" mono className="w-56 h-8" />
          </FilterField>
          {(filters.type || filters.status || filters.severity || filters.matchKey) && (
            <Button size="sm" variant="ghost" onClick={() => setFilters({ type: '', status: '', severity: '', matchKey: '' })}>Clear</Button>
          )}
        </FilterBar>
        {discrepancies.isError && <ErrorAlert error={discrepancies.error} className="m-4" />}
        <DataTable
          columns={columns}
          rows={data?.items}
          rowKey={(d) => d.id}
          isLoading={discrepancies.isLoading || discrepancies.isPlaceholderData}
          onRowClick={(d) => openDiscrepancy(d.id)}
          emptyTitle={isActive ? 'No discrepancies yet' : 'No discrepancies match'}
          emptyDescription={isActive ? 'They appear as the worker streams through both sources.' : 'Adjust the filters or pick another run.'}
          caption="Discrepancies for this run"
          maxHeight="max-h-[60vh]"
        />
        {data && <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount} onPageChange={(page) => setFilters({ page })} onPageSizeChange={(pageSize) => setFilters({ pageSize })} />}
      </Card>

      <DiscrepancyDrawer id={selected} onClose={() => openDiscrepancy(null)} />
    </div>
  );
}
