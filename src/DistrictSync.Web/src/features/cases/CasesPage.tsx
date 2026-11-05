import { useMemo } from 'react';
import { Link, useNavigate } from 'react-router';
import { CASE_PRIORITIES, CASE_STATES, type CaseDto } from '@/api/types';
import { useAuth } from '@/auth/useAuth';
import { Icons } from '@/components/layout/icons';
import { ErrorAlert } from '@/components/ui/Alert';
import { Badge, StatusBadge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { DataTable, type Column } from '@/components/ui/DataTable';
import { Checkbox, Input, Select } from '@/components/ui/Field';
import { FilterBar, FilterField } from '@/components/ui/FilterBar';
import { IdChip } from '@/components/ui/Mono';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { cn } from '@/lib/cn';
import { fmtDateTime, fmtRelative, humanize } from '@/lib/format';
import { useNow } from '@/lib/useNow';
import { useSearchFilters } from '@/lib/useSearchFilters';
import { useCases, type CaseFilters } from './hooks';

const DEFAULTS = { state: '', priority: '', assigneeId: '', overdue: false, page: 1, pageSize: 25 };

export default function CasesPage() {
  const [filters, setFilters] = useSearchFilters(DEFAULTS);
  const query = useCases(filters as CaseFilters);
  const auth = useAuth();
  const navigate = useNavigate();
  const now = useNow();
  const mine = Boolean(auth.userId) && filters.assigneeId === auth.userId;

  const columns = useMemo<Array<Column<CaseDto>>>(
    () => [
      { key: 'priority', header: 'Priority', cell: (c) => <StatusBadge value={c.priority} /> },
      { key: 'state', header: 'State', cell: (c) => <StatusBadge value={c.state} /> },
      {
        key: 'title',
        header: 'Case',
        cell: (c) => (
          <div className="min-w-0 max-w-md">
            <Link to={`/cases/${c.id}`} className="font-medium text-ink hover:text-accent hover:underline line-clamp-1" onClick={(e) => e.stopPropagation()}>{c.title}</Link>
            <p className="text-xs text-muted"><IdChip id={c.id} /> · discrepancy <IdChip id={c.discrepancyId} /></p>
          </div>
        ),
      },
      {
        key: 'assignee',
        header: 'Assignee',
        cell: (c) => (
          <div className="min-w-0">
            <p className={cn('truncate max-w-36', c.assigneeId ? 'text-ink' : 'text-muted')} title={c.assigneeId ?? undefined}>{c.assigneeId ?? 'Unassigned'}</p>
            {c.assignedRole && <p className="text-[11px] text-muted">role: {c.assignedRole}</p>}
          </div>
        ),
      },
      {
        key: 'due',
        header: 'Due',
        cell: (c) => {
          const closed = c.state === 'Resolved' || c.state === 'Cancelled';
          return (
            <div className={cn('whitespace-nowrap', c.isOverdue && !closed && 'text-crit-ink font-medium')} title={fmtDateTime(c.dueAt)}>
              {fmtRelative(c.dueAt, now)}
              {c.isOverdue && !closed && <Badge tone="crit" className="ml-2">overdue</Badge>}
            </div>
          );
        },
      },
      { key: 'escalation', header: 'Escalation', align: 'center', cell: (c) => (c.escalationLevel > 0 ? <Badge tone="crit">L{c.escalationLevel}</Badge> : <span className="text-muted">—</span>) },
      { key: 'opened', header: 'Opened', cell: (c) => <span className="whitespace-nowrap" title={fmtDateTime(c.openedAt)}>{fmtRelative(c.openedAt, now)}</span> },
    ],
    [now],
  );

  const data = query.data;
  const anyFilter = filters.state || filters.priority || filters.assigneeId || filters.overdue;

  return (
    <div className="space-y-4">
      <PageHeader
        title="Cases"
        description="Discrepancies that need a human decision. Work the overdue and urgent ones first."
        actions={<Button variant="ghost" onClick={() => void query.refetch()} loading={query.isFetching && !query.isLoading} icon={<Icons.refresh className="size-3.5" />}>Refresh</Button>}
      />
      <Card bodyClassName="p-0">
        <FilterBar>
          <FilterField label="State">
            <Select value={filters.state} onChange={(e) => setFilters({ state: e.target.value })} className="w-40 h-8">
              <option value="">All states</option>
              {CASE_STATES.map((s) => (<option key={s} value={s}>{humanize(s)}</option>))}
            </Select>
          </FilterField>
          <FilterField label="Priority">
            <Select value={filters.priority} onChange={(e) => setFilters({ priority: e.target.value })} className="w-32 h-8">
              <option value="">All</option>
              {CASE_PRIORITIES.map((p) => (<option key={p} value={p}>{p}</option>))}
            </Select>
          </FilterField>
          <FilterField label="Assignee">
            <Input value={filters.assigneeId} onChange={(e) => setFilters({ assigneeId: e.target.value })} placeholder="user id" className="w-44 h-8" />
          </FilterField>
          <div className="flex items-center gap-4 h-8 self-end">
            <Checkbox label="My cases" checked={mine} onChange={(e) => setFilters({ assigneeId: e.target.checked ? (auth.userId ?? '') : '' })} disabled={!auth.userId} />
            <Checkbox label="Overdue only" checked={filters.overdue} onChange={(e) => setFilters({ overdue: e.target.checked })} />
          </div>
          {anyFilter && <Button size="sm" variant="ghost" onClick={() => setFilters({ state: '', priority: '', assigneeId: '', overdue: false })}>Clear</Button>}
        </FilterBar>
        {query.isError && <ErrorAlert error={query.error} className="m-4" />}
        <DataTable
          columns={columns}
          rows={data?.items}
          rowKey={(c) => c.id}
          isLoading={query.isLoading || query.isPlaceholderData}
          onRowClick={(c) => void navigate(`/cases/${c.id}`)}
          rowClassName={(c) => (c.isOverdue && c.state !== 'Resolved' && c.state !== 'Cancelled' ? 'bg-crit/5' : undefined)}
          emptyTitle={anyFilter ? 'No cases match these filters' : 'No cases'}
          emptyDescription={anyFilter ? 'Try clearing a filter.' : 'Cases are opened by rules with an OpenCase action after a reconciliation run.'}
          caption="Case worklist"
        />
        {data && <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount} onPageChange={(page) => setFilters({ page })} onPageSizeChange={(pageSize) => setFilters({ pageSize })} />}
      </Card>
    </div>
  );
}
