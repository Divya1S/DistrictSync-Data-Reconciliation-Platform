import { useMemo } from 'react';
import type { DiscrepancyDetailDto, SourceRecordDto } from '@/api/types';
import { Badge } from '@/components/ui/Badge';
import { cn } from '@/lib/cn';
import { fmtDateTime } from '@/lib/format';
import { useSourceSystems } from '@/features/sources/hooks';

/**
 * Side-by-side view of the left and right source records with the differing fields highlighted.
 * Shared by the discrepancy drawer and the case detail page.
 */
export function RecordCompare({ detail, className }: { detail: DiscrepancyDetailDto; className?: string }) {
  const sources = useSourceSystems();
  const { discrepancy, left, right } = detail;

  const codeFor = (id: string) => sources.data?.find((s) => s.id === id)?.code ?? id.slice(0, 8);
  const leftLabel = codeFor(discrepancy.leftSourceSystemId);
  const rightLabel = codeFor(discrepancy.rightSourceSystemId);

  const diffByField = useMemo(() => new Map(discrepancy.differences.map((d) => [d.field, d])), [discrepancy.differences]);

  const fields = useMemo(() => {
    const names = new Set<string>();
    discrepancy.differences.forEach((d) => names.add(d.field));
    Object.keys(left?.attributes ?? {}).forEach((k) => names.add(k));
    Object.keys(right?.attributes ?? {}).forEach((k) => names.add(k));
    // Differing fields first (critical before non-critical), then everything else alphabetically.
    return [...names].sort((a, b) => {
      const da = diffByField.get(a);
      const db = diffByField.get(b);
      const ra = da ? (da.isCritical ? 0 : 1) : 2;
      const rb = db ? (db.isCritical ? 0 : 1) : 2;
      return ra - rb || a.localeCompare(b);
    });
  }, [discrepancy.differences, left, right, diffByField]);

  const valueOf = (rec: SourceRecordDto | null, field: string, side: 'left' | 'right') => {
    const diff = diffByField.get(field);
    if (diff) return side === 'left' ? diff.leftValue : diff.rightValue;
    return rec?.attributes[field] ?? null;
  };

  return (
    <div className={cn('space-y-3', className)}>
      <div className="grid grid-cols-2 gap-3">
        <RecordHeader label={leftLabel} record={left} missingText="Missing in left" />
        <RecordHeader label={rightLabel} record={right} missingText="Missing in right" />
      </div>

      <div className="table-wrap max-h-[60vh] rounded-md border border-line">
        <table className="w-full text-sm">
          <caption className="sr-only">Field-by-field comparison of the left and right records</caption>
          <thead>
            <tr>
              <th scope="col" className="w-1/4">Field</th>
              <th scope="col">{leftLabel}</th>
              <th scope="col">{rightLabel}</th>
            </tr>
          </thead>
          <tbody>
            {fields.length === 0 && (
              <tr>
                <td colSpan={3} className="text-center text-muted py-6">No attributes to compare.</td>
              </tr>
            )}
            {fields.map((f) => {
              const diff = diffByField.get(f);
              const lv = valueOf(left, f, 'left');
              const rv = valueOf(right, f, 'right');
              return (
                <tr key={f} className={cn(diff && (diff.isCritical ? 'bg-crit/8' : 'bg-warn/10'))}>
                  <th scope="row" className="font-medium! normal-case! tracking-normal! text-xs! bg-transparent! static! text-ink-2">
                    <span className="mono">{f}</span>
                    {diff?.isCritical && <Badge tone="crit" className="ml-2">critical</Badge>}
                  </th>
                  <td className={cn('mono break-all', diff && 'font-semibold text-ink')}>{renderValue(lv)}</td>
                  <td className={cn('mono break-all', diff && 'font-semibold text-ink')}>{renderValue(rv)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      <p className="text-[11px] text-muted">
        Highlighted rows differ after normalisation; <span className="text-crit-ink">critical</span> fields come from the comparison profile.
      </p>
    </div>
  );
}

function renderValue(v: string | null | undefined) {
  if (v === null || v === undefined) return <span className="text-muted italic">null</span>;
  if (v === '') return <span className="text-muted italic">empty</span>;
  return v;
}

function RecordHeader({ label, record, missingText }: { label: string; record: SourceRecordDto | null; missingText: string }) {
  return (
    <div className={cn('rounded-md border px-3 py-2 min-w-0', record ? 'border-line bg-surface-2/60' : 'border-dashed border-serious/60 bg-serious/8')}>
      <div className="flex items-center justify-between gap-2">
        <p className="text-xs font-semibold text-ink truncate"><code className="mono">{label}</code></p>
        {record?.isDeleted && <Badge tone="crit">deleted</Badge>}
      </div>
      {record ? (
        <dl className="mt-1 grid grid-cols-[auto_1fr] gap-x-2 text-[11px] text-ink-2">
          <dt className="text-muted">External id</dt>
          <dd className="mono truncate" title={record.externalId}>{record.externalId}</dd>
          <dt className="text-muted">Match key</dt>
          <dd className="mono truncate" title={record.matchKey}>{record.matchKey}</dd>
          <dt className="text-muted">Source updated</dt>
          <dd>{fmtDateTime(record.sourceUpdatedAt)}</dd>
          <dt className="text-muted">Ingested</dt>
          <dd>{fmtDateTime(record.ingestedAt)}</dd>
        </dl>
      ) : (
        <p className="mt-1 text-xs text-serious-ink">{missingText}</p>
      )}
    </div>
  );
}
