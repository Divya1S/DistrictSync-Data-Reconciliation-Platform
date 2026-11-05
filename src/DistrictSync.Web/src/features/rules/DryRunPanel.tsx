import { useState, type FormEvent } from 'react';
import { ErrorAlert } from '@/components/ui/Alert';
import { Badge, StatusBadge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Field, Input } from '@/components/ui/Field';
import { IdChip } from '@/components/ui/Mono';
import { cn } from '@/lib/cn';
import { humanize } from '@/lib/format';
import { useDryRun } from './hooks';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function DryRunPanel({ setId }: { setId: string }) {
  const dryRun = useDryRun(setId);
  const [discrepancyId, setDiscrepancyId] = useState('');
  const valid = GUID.test(discrepancyId.trim());

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (valid) dryRun.mutate(discrepancyId.trim());
  };

  const result = dryRun.data;
  return (
    <Card title="Dry run" description="Evaluate this version against an existing discrepancy without changing anything.">
      <form onSubmit={submit} className="flex flex-col sm:flex-row gap-2 sm:items-end">
        <Field label="Discrepancy id" className="flex-1" error={discrepancyId && !valid ? 'Enter a full GUID (copy it from a run’s discrepancy table).' : undefined}>
          {(p) => <Input {...p} value={discrepancyId} onChange={(e) => setDiscrepancyId(e.target.value)} mono placeholder="00000000-0000-0000-0000-000000000000" invalid={Boolean(discrepancyId) && !valid} />}
        </Field>
        <Button type="submit" variant="primary" loading={dryRun.isPending} disabled={!valid}>Evaluate</Button>
      </form>
      <ErrorAlert error={dryRun.error} title="Dry run failed" className="mt-3" />
      {result && (
        <div className="mt-4 space-y-3">
          <div className={cn('rounded-md border px-3 py-2 text-sm', result.matchedRuleId ? 'border-good/40 bg-good/8' : 'border-line bg-surface-2/60')}>
            {result.matchedRuleId ? (
              <div className="flex flex-wrap items-center gap-2">
                <span className="text-ink">Matched <strong>{result.matchedRuleName}</strong></span>
                <IdChip id={result.matchedRuleId} />
                {result.action && (
                  <span className="flex items-center gap-1.5 ml-auto">
                    <StatusBadge value={result.action.kind} />
                    {result.action.strategy && <Badge tone="info">{humanize(result.action.strategy)}</Badge>}
                    {result.action.priority && <StatusBadge value={result.action.priority} />}
                    {result.action.assignToRole && <Badge tone="neutral">role: {result.action.assignToRole}</Badge>}
                  </span>
                )}
              </div>
            ) : (
              <span className="text-ink-2">No rule matched; the discrepancy would stay open for manual triage.</span>
            )}
          </div>
          <ol className="divide-y divide-line rounded-md border border-line text-sm">
            {result.trace.map((t, i) => (
              <li key={t.ruleId} className={cn('flex items-center gap-3 px-3 py-1.5', t.matched && 'bg-good/8')}>
                <span className="mono text-xs text-muted w-5 text-right">{i + 1}</span>
                <span className={cn('size-2 rounded-full shrink-0', t.error ? 'bg-crit' : t.matched ? 'bg-good' : 'bg-line-2')} aria-hidden="true" />
                <span className="flex-1 truncate text-ink">{t.ruleName}</span>
                {t.error ? <span className="text-xs text-crit-ink truncate max-w-xs" title={t.error}>{t.error}</span> : <span className="text-xs text-muted">{t.matched ? 'matched' : 'no match'}</span>}
              </li>
            ))}
            {result.trace.length === 0 && <li className="px-3 py-2 text-muted text-xs">No rules were evaluated.</li>}
          </ol>
        </div>
      )}
    </Card>
  );
}
