import { useMemo, useRef, useState, type FormEvent } from 'react';
import { INGESTION_BATCH_STATUSES, type IngestionBatchDto } from '@/api/types';
import { Icons } from '@/components/layout/icons';
import { ErrorAlert } from '@/components/ui/Alert';
import { StatusBadge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { DataTable, type Column } from '@/components/ui/DataTable';
import { Field, Input, Select } from '@/components/ui/Field';
import { FilterBar, FilterField } from '@/components/ui/FilterBar';
import { Modal } from '@/components/ui/Modal';
import { IdChip } from '@/components/ui/Mono';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { ProgressBar } from '@/components/ui/ProgressBar';
import { toast } from '@/components/ui/toastStore';
import { fmtBytes, fmtDateTime, fmtElapsed, fmtNumber, fmtRelative, humanize } from '@/lib/format';
import { useNow } from '@/lib/useNow';
import { useSearchFilters } from '@/lib/useSearchFilters';
import { liveStore } from '@/realtime/liveStore';
import { useSourceSystems } from '@/features/sources/hooks';
import { useBatches, useRetryBatch, useStartPull, useUploadCsv, type BatchFilters } from './hooks';

const DEFAULTS = { status: '', sourceSystemId: '', page: 1, pageSize: 25 };

export default function IngestionPage() {
  const [filters, setFilters] = useSearchFilters(DEFAULTS);
  const query = useBatches(filters as BatchFilters);
  const sources = useSourceSystems();
  const retry = useRetryBatch();
  const [pullOpen, setPullOpen] = useState(false);
  const now = useNow();

  const columns = useMemo<Array<Column<IngestionBatchDto>>>(
    () => [
      {
        key: 'id',
        header: 'Batch',
        cell: (b) => (
          <div className="min-w-0">
            <IdChip id={b.id} />
            {b.fileName && <p className="text-xs text-muted truncate max-w-48" title={b.fileName}>{b.fileName}</p>}
          </div>
        ),
      },
      { key: 'source', header: 'Source', cell: (b) => <code className="mono">{b.sourceSystemCode}</code> },
      { key: 'entity', header: 'Entity type', cell: (b) => b.entityType },
      {
        key: 'status',
        header: 'Status',
        cell: (b) => (
          <div className="min-w-40 space-y-1">
            <StatusBadge value={b.status} />
            {b.status === 'Processing' || b.status === 'Received' ? <BatchProgress batch={b} /> : null}
            {b.failureReason && (
              <p className="text-xs text-crit-ink truncate max-w-64" title={b.failureReason}>{b.failureReason}</p>
            )}
          </div>
        ),
      },
      {
        key: 'records',
        header: 'Records',
        align: 'right',
        cell: (b) => (
          <div className="tabular-nums leading-4 text-right">
            <p className="text-ink">{fmtNumber(b.totalRecords)}</p>
            <p className="text-[11px] text-muted whitespace-nowrap" title="accepted / rejected / duplicates">
              <span className="text-good-ink">{fmtNumber(b.acceptedRecords)}</span> / <span className="text-crit-ink">{fmtNumber(b.rejectedRecords)}</span> / <span className="text-warn-ink">{fmtNumber(b.duplicateRecords)}</span>
            </p>
          </div>
        ),
      },
      { key: 'size', header: 'Size', align: 'right', cell: (b) => <span className="tabular-nums">{fmtBytes(b.sizeBytes)}</span> },
      {
        key: 'received',
        header: 'Received',
        cell: (b) => (
          <span title={fmtDateTime(b.receivedAt)} className="whitespace-nowrap">{fmtRelative(b.receivedAt, now)}</span>
        ),
      },
      { key: 'duration', header: 'Duration', cell: (b) => <span className="tabular-nums whitespace-nowrap">{fmtElapsed(b.startedAt, b.completedAt, now)}</span> },
      { key: 'by', header: 'Submitted by', cell: (b) => <span className="truncate max-w-32 inline-block" title={b.submittedBy}>{b.submittedBy}</span> },
      {
        key: 'actions',
        header: <span className="sr-only">Actions</span>,
        align: 'right',
        cell: (b) =>
          b.status === 'Failed' ? (
            <Button
              size="xs"
              loading={retry.isPending && retry.variables === b.id}
              onClick={() => retry.mutate(b.id, { onSuccess: () => toast.info('Batch re-queued', { description: `Batch ${b.id.slice(0, 8)} will be processed again.` }), onError: (e) => toast.error('Retry failed', { description: e.message }) })}
              icon={<Icons.refresh className="size-3" />}
            >
              Retry
            </Button>
          ) : null,
      },
    ],
    [now, retry],
  );

  const data = query.data;
  const restSources = sources.data?.filter((s) => s.kind === 'RestApi' && s.isEnabled) ?? [];

  return (
    <div className="space-y-4">
      <PageHeader
        title="Ingestion"
        description="Batches received from every source system, with live progress while records are being processed."
        actions={
          <>
            <Button onClick={() => setPullOpen(true)} disabled={restSources.length === 0} title={restSources.length === 0 ? 'No enabled REST API sources' : undefined} icon={<Icons.play className="size-3.5" />}>
              Start REST pull
            </Button>
            <Button variant="ghost" onClick={() => void query.refetch()} loading={query.isFetching && !query.isLoading} icon={<Icons.refresh className="size-3.5" />}>Refresh</Button>
          </>
        }
      />

      <div className="grid grid-cols-1 xl:grid-cols-3 gap-4">
        <Card className="xl:col-span-2" bodyClassName="p-0">
          <FilterBar>
            <FilterField label="Status">
              <Select value={filters.status} onChange={(e) => setFilters({ status: e.target.value })} className="w-40 h-8">
                <option value="">All statuses</option>
                {INGESTION_BATCH_STATUSES.map((s) => (
                  <option key={s} value={s}>{humanize(s)}</option>
                ))}
              </Select>
            </FilterField>
            <FilterField label="Source system">
              <Select value={filters.sourceSystemId} onChange={(e) => setFilters({ sourceSystemId: e.target.value })} className="w-48 h-8">
                <option value="">All sources</option>
                {sources.data?.map((s) => (
                  <option key={s.id} value={s.id}>{s.code} — {s.name}</option>
                ))}
              </Select>
            </FilterField>
            {(filters.status || filters.sourceSystemId) && (
              <Button size="sm" variant="ghost" onClick={() => setFilters({ status: '', sourceSystemId: '' })}>Clear</Button>
            )}
          </FilterBar>
          {query.isError && <ErrorAlert error={query.error} className="m-4" />}
          <DataTable
            columns={columns}
            rows={data?.items}
            rowKey={(b) => b.id}
            isLoading={query.isLoading || query.isPlaceholderData}
            emptyTitle="No batches match"
            emptyDescription="Upload a CSV or start a REST pull to create the first batch."
            caption="Ingestion batches"
          />
          {data && <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount} onPageChange={(page) => setFilters({ page })} onPageSizeChange={(pageSize) => setFilters({ pageSize })} />}
        </Card>

        <UploadPanel />
      </div>

      <StartPullDialog open={pullOpen} onClose={() => setPullOpen(false)} />
    </div>
  );
}

function BatchProgress({ batch }: { batch: IngestionBatchDto }) {
  const live = liveStore.use((s) => s.ingestionProgress[batch.id]);
  const processed = live?.processed ?? 0;
  const total = batch.totalRecords;
  if (!live && batch.status === 'Received') {
    return <ProgressBar indeterminate label="Queued" />;
  }
  return (
    <div>
      <ProgressBar value={total > 0 ? processed : undefined} max={total} indeterminate={total <= 0} label={`Batch ${batch.id} progress`} />
      <p className="text-[11px] text-muted tabular-nums mt-0.5">
        {total > 0 ? `${fmtNumber(processed)} / ${fmtNumber(total)}` : `${fmtNumber(processed)} processed`}
        {live && ` · ${fmtNumber(live.accepted)} ok · ${fmtNumber(live.rejected)} rejected · ${fmtNumber(live.duplicates)} dup`}
      </p>
    </div>
  );
}

function UploadPanel() {
  const sources = useSourceSystems();
  const upload = useUploadCsv();
  const [sourceSystemId, setSourceSystemId] = useState('');
  const [entityType, setEntityType] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  const csvSources = useMemo(() => {
    const list = sources.data?.filter((s) => s.isEnabled) ?? [];
    // CSV sources first; the API accepts uploads for any source so the others remain available.
    return [...list].sort((a, b) => Number(b.kind === 'CsvUpload') - Number(a.kind === 'CsvUpload') || a.code.localeCompare(b.code));
  }, [sources.data]);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!file || !sourceSystemId || !entityType.trim()) return;
    upload.mutate(
      { sourceSystemId, entityType: entityType.trim(), file },
      {
        onSuccess: (batch) => {
          toast.success('Upload accepted', { description: `Batch ${batch.id.slice(0, 8)} is ${humanize(batch.status).toLowerCase()}.` });
          setFile(null);
          if (fileRef.current) fileRef.current.value = '';
        },
      },
    );
  };

  return (
    <Card title="Upload CSV" description="Creates a batch and processes it in the background.">
      <form onSubmit={submit} className="space-y-3">
        <Field label="Source system" required>
          {(p) => (
            <Select {...p} value={sourceSystemId} onChange={(e) => setSourceSystemId(e.target.value)} required>
              <option value="">Select a source…</option>
              {csvSources.map((s) => (
                <option key={s.id} value={s.id}>{s.code} — {s.name} ({humanize(s.kind)})</option>
              ))}
            </Select>
          )}
        </Field>
        <Field label="Entity type" required hint="Must match the entity type used by the comparison profile, e.g. employee.">
          {(p) => <Input {...p} value={entityType} onChange={(e) => setEntityType(e.target.value)} placeholder="employee" required />}
        </Field>
        <Field label="CSV file" required hint={file ? `${file.name} · ${fmtBytes(file.size)}` : 'UTF-8 CSV with a header row.'}>
          {(p) => (
            <Input
              {...p}
              ref={fileRef}
              type="file"
              accept=".csv,text/csv"
              required
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              className="file:mr-3 file:rounded file:border-0 file:bg-surface-3 file:px-2 file:py-0.5 file:text-xs file:text-ink"
            />
          )}
        </Field>
        <ErrorAlert error={upload.error} title="Upload rejected" />
        <Button type="submit" variant="primary" className="w-full" loading={upload.isPending} disabled={!file || !sourceSystemId || !entityType.trim()} icon={<Icons.upload className="size-4" />}>
          Upload and process
        </Button>
      </form>
    </Card>
  );
}

function StartPullDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const sources = useSourceSystems();
  const start = useStartPull();
  const [sourceSystemId, setSourceSystemId] = useState('');
  const [entityType, setEntityType] = useState('');
  const restSources = sources.data?.filter((s) => s.kind === 'RestApi' && s.isEnabled) ?? [];

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!sourceSystemId || !entityType.trim()) return;
    start.mutate(
      { sourceSystemId, entityType: entityType.trim() },
      {
        onSuccess: (batch) => {
          toast.success('Pull started', { description: `Batch ${batch.id.slice(0, 8)} created.` });
          onClose();
        },
      },
    );
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Start a REST pull"
      description="Asks the worker to fetch records from a REST API source and ingest them as a batch."
      footer={
        <>
          <Button onClick={onClose}>Cancel</Button>
          <Button variant="primary" type="submit" form="start-pull-form" loading={start.isPending} disabled={!sourceSystemId || !entityType.trim()}>Start pull</Button>
        </>
      }
    >
      <form id="start-pull-form" onSubmit={submit} className="space-y-3">
        <Field label="REST API source" required>
          {(p) => (
            <Select {...p} value={sourceSystemId} onChange={(e) => setSourceSystemId(e.target.value)} required data-autofocus>
              <option value="">Select a source…</option>
              {restSources.map((s) => (
                <option key={s.id} value={s.id}>{s.code} — {s.name}</option>
              ))}
            </Select>
          )}
        </Field>
        <Field label="Entity type" required>
          {(p) => <Input {...p} value={entityType} onChange={(e) => setEntityType(e.target.value)} placeholder="employee" required />}
        </Field>
        <ErrorAlert error={start.error} title="Could not start the pull" />
      </form>
    </Modal>
  );
}
