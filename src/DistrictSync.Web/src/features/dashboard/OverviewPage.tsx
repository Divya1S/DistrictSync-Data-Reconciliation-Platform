import { Link } from 'react-router';
import { Area, AreaChart, Bar, BarChart, CartesianGrid, Cell, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { CASE_PRIORITIES, type CountByKeyDto, type DailyCountDto } from '@/api/types';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorAlert } from '@/components/ui/Alert';
import { KpiTile } from '@/components/ui/KpiTile';
import { PageHeader } from '@/components/ui/PageHeader';
import { Skeleton } from '@/components/ui/Skeleton';
import { Badge } from '@/components/ui/Badge';
import { toneFor } from '@/components/ui/statusTones';
import { Icons } from '@/components/layout/icons';
import { tooltipStyle, useChartTheme } from '@/lib/charts';
import { fmtDay, fmtNumber, fmtTime, humanize } from '@/lib/format';
import { describeUpdate } from '@/realtime/handlers';
import { clearActivity, liveStore } from '@/realtime/liveStore';
import { useDashboardSummary } from './hooks';

export default function OverviewPage() {
  const summary = useDashboardSummary();
  const d = summary.data;
  const loading = summary.isLoading;

  return (
    <div className="space-y-4">
      <PageHeader
        title="Overview"
        description="Tenant-wide health of ingestion, reconciliation, case work and the messaging backbone."
        actions={
          <Button size="sm" onClick={() => void summary.refetch()} loading={summary.isFetching && !loading} icon={<Icons.refresh className="size-3.5" />}>
            Refresh
          </Button>
        }
      />

      {summary.isError && <ErrorAlert error={summary.error} title="Could not load the dashboard summary" />}

      <section aria-label="Key metrics" className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <KpiTile label="Ingestion in progress" value={d?.ingestionInProgress} loading={loading} tone="info" to="/ingestion?status=Processing" hint={d ? `${fmtNumber(d.ingestionCompletedToday)} completed · ${fmtNumber(d.ingestionFailedToday)} failed today` : undefined} />
        <KpiTile label="Open discrepancies" value={d?.openDiscrepancies} loading={loading} tone="warn" to="/reconciliation" hint={d ? `${fmtNumber(d.reconciliationRunning)} runs active · ${fmtNumber(d.reconciliationCompletedToday)} completed today` : undefined} />
        <KpiTile label="Open cases" value={d?.casesOpen} loading={loading} tone="info" to="/cases" hint={d ? `${fmtNumber(d.casesResolvedToday)} resolved today` : undefined} />
        <KpiTile label="Overdue cases" value={d?.casesOverdue} loading={loading} tone="crit" to="/cases?overdue=true" hint="Past SLA due date" />
        <KpiTile label="Dead letters" value={d?.deadLetters} loading={loading} tone="crit" to="/operations" hint="Messages awaiting replay" />
        <KpiTile label="Outbox pending" value={d?.outboxPending} loading={loading} tone="warn" to="/operations" hint="Events not yet published" />
        <KpiTile label="Auto-resolved today" value={d?.autoResolvedToday} loading={loading} tone="good" to="/rules" hint="Discrepancies closed by rules" />
        <KpiTile label="Records ingested today" value={d?.recordsIngestedToday} loading={loading} to="/ingestion" hint="Accepted across all sources" />
      </section>

      <div className="grid grid-cols-1 xl:grid-cols-3 gap-4">
        <Card title="Discrepancies, last 14 days" description="Detected vs resolved per day" className="xl:col-span-2">
          {loading ? <Skeleton className="h-56" /> : d && d.discrepanciesLast14Days.length > 0 ? <TrendChart data={d.discrepanciesLast14Days} /> : <EmptyState compact title="No history yet" description="Run a reconciliation to start the series." />}
        </Card>
        <Card title="Live activity" description="Streamed from the operations hub" bodyClassName="p-0" actions={<Button size="xs" variant="ghost" onClick={clearActivity}>Clear</Button>}>
          <ActivityFeed />
        </Card>
        <Card title="Open discrepancies by type">
          {loading ? <Skeleton className="h-48" /> : d && d.openDiscrepanciesByType.length > 0 ? <CategoryBars data={d.openDiscrepanciesByType} /> : <EmptyState compact title="No open discrepancies" />}
        </Card>
        <Card title="Open cases by priority">
          {loading ? <Skeleton className="h-48" /> : d && d.openCasesByPriority.length > 0 ? <PriorityBars data={d.openCasesByPriority} /> : <EmptyState compact title="No open cases" />}
        </Card>
        <Card title="Today at a glance">
          {loading ? (
            <Skeleton className="h-48" />
          ) : d ? (
            <dl className="grid grid-cols-2 gap-3 text-sm">
              {[
                ['Batches completed', d.ingestionCompletedToday],
                ['Batches failed', d.ingestionFailedToday],
                ['Runs completed', d.reconciliationCompletedToday],
                ['Runs active', d.reconciliationRunning],
                ['Cases resolved', d.casesResolvedToday],
                ['Auto-resolved', d.autoResolvedToday],
              ].map(([label, value]) => (
                <div key={String(label)} className="rounded-md border border-line bg-surface-2/60 px-3 py-2">
                  <dt className="text-[11px] uppercase tracking-wide text-muted">{label}</dt>
                  <dd className="text-lg font-semibold tabular-nums text-ink">{fmtNumber(Number(value))}</dd>
                </div>
              ))}
            </dl>
          ) : null}
        </Card>
      </div>
    </div>
  );
}

function TrendChart({ data }: { data: DailyCountDto[] }) {
  const t = useChartTheme();
  const tip = tooltipStyle(t);
  return (
    <div className="h-56" role="img" aria-label="Line chart of discrepancies detected and resolved per day for the last 14 days">
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={data} margin={{ top: 8, right: 8, left: -16, bottom: 0 }}>
          <CartesianGrid vertical={false} stroke={t.grid} />
          <XAxis dataKey="day" tickFormatter={(v: string) => fmtDay(v)} tickLine={false} axisLine={{ stroke: t.axis }} minTickGap={24} />
          <YAxis tickLine={false} axisLine={false} allowDecimals={false} width={56} />
          <Tooltip {...tip} labelFormatter={(label: unknown) => fmtDay(String(label))} formatter={(value: unknown, name: unknown) => [fmtNumber(Number(value)), humanize(String(name))]} cursor={{ stroke: t.axis }} />
          <Legend iconType="circle" iconSize={8} formatter={(value: unknown) => humanize(String(value))} />
          <Area type="monotone" dataKey="detected" stroke={t.series[0]} fill={t.series[0]} fillOpacity={0.1} strokeWidth={2} dot={false} activeDot={{ r: 4, strokeWidth: 2, stroke: t.surface }} isAnimationActive={false} />
          <Area type="monotone" dataKey="resolved" stroke={t.series[1]} fill={t.series[1]} fillOpacity={0.1} strokeWidth={2} dot={false} activeDot={{ r: 4, strokeWidth: 2, stroke: t.surface }} isAnimationActive={false} />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}

function CategoryBars({ data }: { data: CountByKeyDto[] }) {
  const t = useChartTheme();
  const tip = tooltipStyle(t);
  const rows = data.map((x) => ({ key: x.key, label: humanize(x.key), count: x.count }));
  return (
    <div className="h-48" role="img" aria-label="Bar chart of open discrepancies by type">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={rows} margin={{ top: 8, right: 8, left: -16, bottom: 0 }} barCategoryGap="30%">
          <CartesianGrid vertical={false} stroke={t.grid} />
          <XAxis dataKey="label" tickLine={false} axisLine={{ stroke: t.axis }} interval={0} />
          <YAxis tickLine={false} axisLine={false} allowDecimals={false} width={56} />
          <Tooltip {...tip} cursor={{ fill: t.grid, opacity: 0.5 }} formatter={(value: unknown) => [fmtNumber(Number(value)), 'Open']} />
          <Bar dataKey="count" fill={t.series[0]} radius={[4, 4, 0, 0]} maxBarSize={24} isAnimationActive={false} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}

function PriorityBars({ data }: { data: CountByKeyDto[] }) {
  const t = useChartTheme();
  const tip = tooltipStyle(t);
  // Priority is ordinal (Low → Urgent): a single-hue ramp, darker = more urgent.
  const byKey = new Map(data.map((x) => [x.key, x.count]));
  const rows = CASE_PRIORITIES.map((p, i) => ({ key: p, count: byKey.get(p) ?? 0, color: t.ordinal[i] }));
  return (
    <div className="h-48" role="img" aria-label="Bar chart of open cases by priority">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={rows} margin={{ top: 8, right: 8, left: -16, bottom: 0 }} barCategoryGap="30%">
          <CartesianGrid vertical={false} stroke={t.grid} />
          <XAxis dataKey="key" tickLine={false} axisLine={{ stroke: t.axis }} interval={0} />
          <YAxis tickLine={false} axisLine={false} allowDecimals={false} width={56} />
          <Tooltip {...tip} cursor={{ fill: t.grid, opacity: 0.5 }} formatter={(value: unknown) => [fmtNumber(Number(value)), 'Open cases']} />
          <Bar dataKey="count" radius={[4, 4, 0, 0]} maxBarSize={24} isAnimationActive={false}>
            {rows.map((r) => (
              <Cell key={r.key} fill={r.color} />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}

function ActivityFeed() {
  const activity = liveStore.use((s) => s.activity);
  const connection = liveStore.use((s) => s.connection);
  if (activity.length === 0) {
    return (
      <EmptyState
        compact
        title={connection === 'connected' ? 'Waiting for events' : 'Not connected'}
        description={connection === 'connected' ? 'Batch, run and case events will appear here as they happen.' : 'The live feed fills in once the hub connection is up.'}
      />
    );
  }
  return (
    <ol className="max-h-72 overflow-auto scroll-thin divide-y divide-line" aria-live="polite" aria-label="Recent events">
      {activity.map(({ id, update }) => {
        const info = describeUpdate(update);
        const [area] = update.kind.split('.');
        const tone = update.kind.endsWith('failed') || update.kind.endsWith('dead-lettered') || update.kind.endsWith('escalated') ? 'crit' : update.kind.endsWith('completed') || update.kind.endsWith('approved') ? 'good' : toneFor('Processing');
        return (
          <li key={id} className="flex items-start gap-3 px-4 py-2">
            <time className="mono text-[11px] text-muted pt-0.5 shrink-0" dateTime={update.at}>{fmtTime(update.at)}</time>
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-2">
                <Badge tone={tone}>{area}</Badge>
                {info.href ? <Link to={info.href} className="text-sm font-medium text-ink hover:underline truncate">{info.title}</Link> : <span className="text-sm font-medium text-ink truncate">{info.title}</span>}
              </div>
              {info.detail && <p className="text-xs text-ink-2 truncate" title={info.detail}>{info.detail}</p>}
            </div>
          </li>
        );
      })}
    </ol>
  );
}
