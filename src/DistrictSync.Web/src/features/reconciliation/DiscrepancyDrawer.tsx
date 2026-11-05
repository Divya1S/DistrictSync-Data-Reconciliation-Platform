import { Link } from 'react-router';
import { ErrorAlert } from '@/components/ui/Alert';
import { StatusBadge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { KeyValueList } from '@/components/ui/KeyValue';
import { Modal } from '@/components/ui/Modal';
import { IdChip } from '@/components/ui/Mono';
import { SkeletonLines } from '@/components/ui/Skeleton';
import { fmtDateTime } from '@/lib/format';
import { RecordCompare } from './RecordCompare';
import { useDiscrepancy } from './hooks';

export function DiscrepancyDrawer({ id, onClose }: { id: string | null; onClose: () => void }) {
  const query = useDiscrepancy(id);
  const d = query.data?.discrepancy;

  return (
    <Modal
      open={id !== null}
      onClose={onClose}
      variant="drawer"
      title={
        <span className="flex items-center gap-2 flex-wrap">
          Discrepancy <IdChip id={id} />
          {d && <StatusBadge value={d.type} />}
          {d && <StatusBadge value={d.severity} />}
          {d && <StatusBadge value={d.status} />}
        </span>
      }
      description={d ? `${d.entityType} · match key ${d.matchKey}` : undefined}
      footer={
        <>
          {query.data?.caseId ? (
            <Link to={`/cases/${query.data.caseId}`}><Button variant="primary">Open case</Button></Link>
          ) : d?.status === 'Open' ? (
            <span className="text-xs text-muted mr-auto">No case yet; a published rule with an OpenCase action would create one.</span>
          ) : null}
          <Button onClick={onClose}>Close</Button>
        </>
      }
    >
      {query.isLoading && <SkeletonLines lines={8} />}
      {query.isError && <ErrorAlert error={query.error} title="Could not load the discrepancy" />}
      {query.data && d && (
        <div className="space-y-4">
          <KeyValueList
            columns={3}
            items={[
              { label: 'Match key', value: d.matchKey, mono: true },
              { label: 'Detected', value: fmtDateTime(d.detectedAt) },
              { label: 'Resolved', value: fmtDateTime(d.resolvedAt) },
              { label: 'Run', value: <Link className="text-accent hover:underline mono" to={`/reconciliation/runs/${d.runId}`}>{d.runId.slice(0, 8)}</Link> },
              { label: 'Applied rule', value: d.appliedRuleId ? <IdChip id={d.appliedRuleId} /> : '—' },
              { label: 'Case', value: query.data.caseId ? <Link className="text-accent hover:underline mono" to={`/cases/${query.data.caseId}`}>{query.data.caseId.slice(0, 8)}</Link> : '—' },
            ]}
          />
          {d.resolutionNote && (
            <div className="rounded-md border border-line bg-surface-2/60 px-3 py-2 text-sm">
              <p className="text-[11px] uppercase tracking-wide text-muted">Resolution note</p>
              <p className="text-ink whitespace-pre-wrap">{d.resolutionNote}</p>
            </div>
          )}
          <RecordCompare detail={query.data} />
        </div>
      )}
    </Modal>
  );
}
