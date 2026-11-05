import type { ReconciliationStatsDto } from '@/api/types';
import { useChartTheme } from '@/lib/charts';
import { fmtNumber, pct } from '@/lib/format';

/**
 * One whole split into parts: a segmented bar of the comparison outcome with a 2px surface gap between
 * segments and a legend that doubles as the table view.
 */
export function StatsBar({ stats }: { stats: ReconciliationStatsDto }) {
  const t = useChartTheme();
  const segments = [
    { key: 'matched', label: 'Matched', value: stats.matched, color: t.series[0] },
    { key: 'missingInLeft', label: 'Missing in left', value: stats.missingInLeft, color: t.series[1] },
    { key: 'missingInRight', label: 'Missing in right', value: stats.missingInRight, color: t.series[2] },
    { key: 'fieldConflicts', label: 'Field conflicts', value: stats.fieldConflicts, color: t.series[3] },
    { key: 'duplicates', label: 'Duplicates', value: stats.duplicates, color: t.series[4] },
  ];
  const total = segments.reduce((s, x) => s + x.value, 0);

  return (
    <div className="space-y-3">
      <div className="flex h-5 w-full overflow-hidden rounded bg-surface-3 gap-0.5" role="img" aria-label={`Comparison outcome: ${segments.map((s) => `${s.label} ${fmtNumber(s.value)}`).join(', ')}`}>
        {total === 0 ? (
          <div className="w-full grid place-items-center text-[11px] text-muted">no records compared yet</div>
        ) : (
          segments
            .filter((s) => s.value > 0)
            .map((s) => (
              <div key={s.key} style={{ width: `${pct(s.value, total)}%`, background: s.color }} title={`${s.label}: ${fmtNumber(s.value)} (${pct(s.value, total).toFixed(1)}%)`} className="min-w-[3px] transition-[width] duration-500" />
            ))
        )}
      </div>
      <ul className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-2 text-sm">
        {segments.map((s) => (
          <li key={s.key} className="flex items-center gap-2 rounded-md border border-line px-2.5 py-1.5">
            <span className="size-2.5 rounded-full shrink-0" style={{ background: s.color }} aria-hidden="true" />
            <div className="min-w-0">
              <p className="text-[11px] text-muted leading-4 truncate">{s.label}</p>
              <p className="tabular-nums font-semibold text-ink leading-5">{fmtNumber(s.value)} <span className="text-muted font-normal text-xs">{total ? `${pct(s.value, total).toFixed(0)}%` : ''}</span></p>
            </div>
          </li>
        ))}
      </ul>
      <dl className="grid grid-cols-2 sm:grid-cols-5 gap-2 text-xs">
        {[
          ['Left records', stats.leftRecords],
          ['Right records', stats.rightRecords],
          ['Auto-resolved', stats.autoResolved],
          ['Cases opened', stats.casesOpened],
          ['Ignored', stats.ignored],
        ].map(([label, value]) => (
          <div key={String(label)} className="rounded-md bg-surface-2/60 px-2.5 py-1.5">
            <dt className="text-muted">{label}</dt>
            <dd className="tabular-nums font-medium text-ink">{fmtNumber(Number(value))}</dd>
          </div>
        ))}
      </dl>
    </div>
  );
}
