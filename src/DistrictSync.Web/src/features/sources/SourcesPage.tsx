import { useMemo, useState, type FormEvent } from 'react';
import { SOURCE_SYSTEM_KINDS, type SourceSystemDto, type SourceSystemKind } from '@/api/types';
import { useAuth } from '@/auth/useAuth';
import { Icons } from '@/components/layout/icons';
import { Alert, ErrorAlert } from '@/components/ui/Alert';
import { Badge, StatusBadge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { DataTable, type Column } from '@/components/ui/DataTable';
import { Field, Input, Select, Textarea } from '@/components/ui/Field';
import { Modal } from '@/components/ui/Modal';
import { CopyButton, IdChip, Mono } from '@/components/ui/Mono';
import { PageHeader } from '@/components/ui/PageHeader';
import { toast } from '@/components/ui/toastStore';
import { fmtDateTime, humanize } from '@/lib/format';
import { useCreateSourceSystem, useRotateApiKey, useSourceSystems } from './hooks';

const KIND_TONE: Record<SourceSystemKind, 'info' | 'violet' | 'good'> = { RestApi: 'info', CsvUpload: 'good', Webhook: 'violet' };

export default function SourcesPage() {
  const sources = useSourceSystems();
  const rotate = useRotateApiKey();
  const auth = useAuth();
  const [createOpen, setCreateOpen] = useState(false);
  const [revealed, setRevealed] = useState<{ code: string; apiKey: string; title: string } | null>(null);
  const [rotateTarget, setRotateTarget] = useState<SourceSystemDto | null>(null);
  const canManage = auth.hasRole('admin');

  const columns = useMemo<Array<Column<SourceSystemDto>>>(
    () => [
      {
        key: 'name',
        header: 'Source system',
        cell: (s) => (
          <div className="min-w-0">
            <p className="font-medium text-ink">{s.name}</p>
            <p className="text-xs text-muted"><IdChip id={s.id} /></p>
          </div>
        ),
      },
      { key: 'code', header: 'Code', cell: (s) => <code className="mono">{s.code}</code> },
      { key: 'kind', header: 'Kind', cell: (s) => <Badge tone={KIND_TONE[s.kind] ?? 'neutral'}>{humanize(s.kind)}</Badge> },
      { key: 'trust', header: 'Trust rank', align: 'right', cell: (s) => <span className="tabular-nums" title="Higher wins under PreferHigherTrust">{s.trustRank}</span> },
      { key: 'enabled', header: 'Enabled', cell: (s) => (s.isEnabled ? <Badge tone="good" dot>Enabled</Badge> : <Badge tone="neutral" dot>Disabled</Badge>) },
      { key: 'key', header: 'API key', cell: (s) => (s.hasApiKey ? <Badge tone="info">configured</Badge> : <span className="text-muted">none</span>) },
      { key: 'created', header: 'Created', cell: (s) => <span className="whitespace-nowrap">{fmtDateTime(s.createdAt)}</span> },
      {
        key: 'actions',
        header: <span className="sr-only">Actions</span>,
        align: 'right',
        cell: (s) => (
          <Button size="xs" onClick={() => setRotateTarget(s)} disabled={!canManage} title={canManage ? 'Issue a new API key; the old one stops working' : 'Requires the admin role'} icon={<Icons.key className="size-3" />}>
            Rotate key
          </Button>
        ),
      },
    ],
    [canManage],
  );

  return (
    <div className="space-y-4">
      <PageHeader
        title="Source systems"
        description="The systems whose records are ingested and reconciled. Trust rank decides who wins under the PreferHigherTrust strategy."
        actions={
          <Button variant="primary" onClick={() => setCreateOpen(true)} disabled={!canManage} title={canManage ? undefined : 'Requires the admin role'} icon={<Icons.plus className="size-4" />}>
            New source system
          </Button>
        }
      />
      <Card bodyClassName="p-0">
        {sources.isError && <ErrorAlert error={sources.error} className="m-4" />}
        <DataTable columns={columns} rows={sources.data} rowKey={(s) => s.id} isLoading={sources.isLoading} emptyTitle="No source systems" emptyDescription="Create one to start ingesting records." caption="Source systems" />
      </Card>

      <CreateSourceDialog
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        onCreated={(res) => {
          setCreateOpen(false);
          if (res.apiKey) setRevealed({ code: res.sourceSystem.code, apiKey: res.apiKey, title: 'Source system created' });
          else toast.success('Source system created', { description: res.sourceSystem.code });
        }}
      />

      <Modal
        open={rotateTarget !== null}
        onClose={() => setRotateTarget(null)}
        title="Rotate API key"
        size="sm"
        footer={
          <>
            <Button onClick={() => setRotateTarget(null)}>Cancel</Button>
            <Button
              variant="danger"
              loading={rotate.isPending}
              onClick={() => {
                if (!rotateTarget) return;
                rotate.mutate(rotateTarget.id, {
                  onSuccess: (r) => {
                    setRevealed({ code: rotateTarget.code, apiKey: r.apiKey, title: 'API key rotated' });
                    setRotateTarget(null);
                  },
                });
              }}
            >
              Rotate key
            </Button>
          </>
        }
      >
        <p className="text-sm text-ink-2">
          Issue a new API key for <code className="mono">{rotateTarget?.code}</code>? Any integration using the current key will start receiving 401 responses immediately.
        </p>
        <ErrorAlert error={rotate.error} className="mt-3" />
      </Modal>

      <Modal open={revealed !== null} onClose={() => setRevealed(null)} title={revealed?.title ?? ''} size="md" footer={<Button variant="primary" onClick={() => setRevealed(null)}>Done</Button>}>
        {revealed && (
          <div className="space-y-3">
            <Alert tone="warning" title="Shown once">
              Only a hash of this key is stored. Copy it now and place it in the <code className="mono">{revealed.code}</code> integration; it cannot be retrieved later.
            </Alert>
            <div className="flex items-center justify-between gap-3 rounded-md border border-line bg-surface-2 px-3 py-2">
              <Mono className="bg-transparent px-0 break-all select-all">{revealed.apiKey}</Mono>
              <CopyButton value={revealed.apiKey} label="Copy key" />
            </div>
            <p className="text-xs text-muted">Send it as the <code className="mono">X-Api-Key</code> header on webhook and pull requests, or as configured for this source kind.</p>
          </div>
        )}
      </Modal>
    </div>
  );
}

function CreateSourceDialog({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (r: { sourceSystem: SourceSystemDto; apiKey: string | null }) => void }) {
  const create = useCreateSourceSystem();
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [kind, setKind] = useState<SourceSystemKind>('CsvUpload');
  const [trustRank, setTrustRank] = useState(50);
  const [configJson, setConfigJson] = useState('{\n  \n}');

  const jsonError = useMemo(() => {
    const trimmed = configJson.trim();
    if (!trimmed) return null;
    try {
      JSON.parse(trimmed);
      return null;
    } catch (e) {
      return e instanceof Error ? e.message : 'Invalid JSON';
    }
  }, [configJson]);

  const reset = () => {
    setName('');
    setCode('');
    setKind('CsvUpload');
    setTrustRank(50);
    setConfigJson('{\n  \n}');
    create.reset();
  };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (jsonError) return;
    const trimmed = configJson.trim();
    create.mutate(
      { name: name.trim(), code: code.trim(), kind, trustRank, configJson: trimmed && trimmed !== '{}' ? trimmed : null },
      {
        onSuccess: (res) => {
          onCreated(res);
          reset();
        },
      },
    );
  };

  const hint: Record<SourceSystemKind, string> = {
    RestApi: 'e.g. {"baseUrl":"https://hr.example.com/api","pageSize":500}. The worker pulls records from here.',
    CsvUpload: 'Optional column mapping, e.g. {"matchKeyColumn":"employee_id"}.',
    Webhook: 'Optional. Webhook pushes authenticate with the API key issued on creation.',
  };

  return (
    <Modal
      open={open}
      onClose={() => { onClose(); reset(); }}
      title="New source system"
      description="A REST API or Webhook source receives an API key once, on creation."
      footer={
        <>
          <Button onClick={() => { onClose(); reset(); }}>Cancel</Button>
          <Button variant="primary" type="submit" form="create-source-form" loading={create.isPending} disabled={!name.trim() || !code.trim() || Boolean(jsonError)}>Create</Button>
        </>
      }
    >
      <form id="create-source-form" onSubmit={submit} className="space-y-3">
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <Field label="Name" required>{(p) => <Input {...p} value={name} onChange={(e) => setName(e.target.value)} placeholder="Workday HR" required data-autofocus />}</Field>
          <Field label="Code" required hint="Short, unique, stable; appears on batches and runs.">{(p) => <Input {...p} value={code} onChange={(e) => setCode(e.target.value.toUpperCase())} placeholder="HR" mono required maxLength={32} />}</Field>
          <Field label="Kind" required>
            {(p) => (
              <Select {...p} value={kind} onChange={(e) => setKind(e.target.value as SourceSystemKind)}>
                {SOURCE_SYSTEM_KINDS.map((k) => (
                  <option key={k} value={k}>{humanize(k)}</option>
                ))}
              </Select>
            )}
          </Field>
          <Field label="Trust rank" required hint="0–100; higher wins under PreferHigherTrust.">{(p) => <Input {...p} type="number" min={0} max={100} value={trustRank} onChange={(e) => setTrustRank(Number(e.target.value))} required />}</Field>
        </div>
        <Field label="Config JSON" hint={hint[kind]} error={jsonError}>
          {(p) => <Textarea {...p} value={configJson} onChange={(e) => setConfigJson(e.target.value)} mono rows={5} invalid={Boolean(jsonError)} spellCheck={false} />}
        </Field>
        <ErrorAlert error={create.error} title="Could not create the source system" />
        <p className="text-xs text-muted flex items-center gap-1.5"><StatusBadge value="Draft" /> <span>is not a thing here: sources are live as soon as they are created and enabled.</span></p>
      </form>
    </Modal>
  );
}
