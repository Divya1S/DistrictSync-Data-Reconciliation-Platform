import { useMemo, useState, type FormEvent } from 'react';
import { useNavigate, useParams } from 'react-router';
import type { RuleDto, UpsertRuleRequest } from '@/api/types';
import { useAuth } from '@/auth/useAuth';
import { Icons } from '@/components/layout/icons';
import { Alert, ErrorAlert } from '@/components/ui/Alert';
import { Badge, StatusBadge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { Field, Textarea } from '@/components/ui/Field';
import { KeyValueList } from '@/components/ui/KeyValue';
import { Modal } from '@/components/ui/Modal';
import { IdChip } from '@/components/ui/Mono';
import { PageHeader } from '@/components/ui/PageHeader';
import { SkeletonLines } from '@/components/ui/Skeleton';
import { toast } from '@/components/ui/toastStore';
import { cn } from '@/lib/cn';
import { fmtDateTime, humanize } from '@/lib/format';
import { DryRunPanel } from './DryRunPanel';
import { RuleForm } from './RuleForm';
import { RuleLanguageCard } from './RuleLanguageCard';
import { useAddRule, useDeleteRule, useNextVersion, usePublishRuleSet, useRuleSet, useUpdateRule } from './hooks';

export default function RuleSetDetailPage() {
  const { id = '' } = useParams();
  const query = useRuleSet(id);
  const auth = useAuth();
  const navigate = useNavigate();
  const add = useAddRule(id);
  const update = useUpdateRule(id);
  const remove = useDeleteRule(id);
  const publish = usePublishRuleSet(id);
  const nextVersion = useNextVersion(id);

  const [form, setForm] = useState<{ open: boolean; rule: RuleDto | null; key: number }>({ open: false, rule: null, key: 0 });
  const [publishOpen, setPublishOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<RuleDto | null>(null);
  const [changeNote, setChangeNote] = useState('');

  const set = query.data;
  const canEdit = auth.hasRole('admin');
  const isDraft = set?.status === 'Draft';
  const rules = useMemo(() => [...(set?.rules ?? [])].sort((a, b) => a.order - b.order), [set?.rules]);
  const nextOrder = rules.length > 0 ? Math.max(...rules.map((r) => r.order)) + 1 : 1;

  const openForm = (rule: RuleDto | null) => {
    add.reset();
    update.reset();
    setForm((f) => ({ open: true, rule, key: f.key + 1 }));
  };
  const closeForm = () => setForm((f) => ({ ...f, open: false }));

  const submitRule = (body: UpsertRuleRequest) => {
    if (form.rule) {
      update.mutate({ ruleId: form.rule.id, body }, { onSuccess: () => { toast.success('Rule updated'); closeForm(); } });
    } else {
      add.mutate(body, { onSuccess: (r) => { toast.success('Rule added', { description: r.name }); closeForm(); } });
    }
  };

  const doPublish = (e: FormEvent) => {
    e.preventDefault();
    publish.mutate({ changeNote: changeNote.trim() || null }, {
      onSuccess: (s) => {
        toast.success(`Published v${s.version}`, { description: 'Runs for this entity type now use it.' });
        setPublishOpen(false);
        setChangeNote('');
      },
    });
  };

  const doNextVersion = () => {
    nextVersion.mutate(undefined, {
      onSuccess: (s) => {
        toast.success(`Draft v${s.version} created`, { description: 'Edit the copy and publish when ready.' });
        void navigate(`/rules/${s.id}`);
      },
      onError: (err) => toast.error('Could not create the next version', { description: err.message }),
    });
  };

  return (
    <div className="space-y-4">
      <PageHeader
        crumbs={[{ label: 'Rules', to: '/rules' }, { label: set ? `${set.name} v${set.version}` : 'Rule set' }]}
        title={set ? <span>{set.name} <span className="mono text-ink-2 text-base">v{set.version}</span></span> : 'Rule set'}
        meta={set && <StatusBadge value={set.status} />}
        description={set ? <span>Entity type <code className="mono">{set.entityType}</code>{set.publishedAt ? ` · published ${fmtDateTime(set.publishedAt)} by ${set.publishedBy ?? 'unknown'}` : ` · created ${fmtDateTime(set.createdAt)}`}</span> : undefined}
        actions={
          set && (
            <>
              {isDraft && <Button onClick={() => openForm(null)} disabled={!canEdit} icon={<Icons.plus className="size-4" />}>Add rule</Button>}
              {isDraft && <Button variant="primary" onClick={() => setPublishOpen(true)} disabled={!canEdit || rules.length === 0} title={rules.length === 0 ? 'Add at least one rule first' : undefined}>Publish</Button>}
              {set.status === 'Published' && <Button variant="primary" onClick={doNextVersion} loading={nextVersion.isPending} disabled={!canEdit}>Create next version</Button>}
            </>
          )
        }
      />

      {query.isError && <ErrorAlert error={query.error} title="Could not load the rule set" />}
      {set && !isDraft && <Alert tone="info">{set.status === 'Published' ? 'Published versions are immutable. Create the next version to make changes; publishing it retires this one.' : 'This version is retired and kept for audit. Runs that used it still reference it.'}</Alert>}
      {set?.changeNote && <Alert tone="info" title="Change note">{set.changeNote}</Alert>}

      <div className="grid grid-cols-1 xl:grid-cols-3 gap-4">
        <div className="xl:col-span-2 space-y-4">
          <Card title="Rules" description={`${rules.length} rule${rules.length === 1 ? '' : 's'}, evaluated in order; the first enabled match wins.`} bodyClassName="p-0">
            {query.isLoading ? (
              <SkeletonLines lines={5} className="p-4" />
            ) : rules.length === 0 ? (
              <EmptyState title="No rules yet" description={isDraft ? 'Add the first rule to this draft.' : 'This version has no rules.'} action={isDraft ? <Button variant="primary" size="sm" onClick={() => openForm(null)} disabled={!canEdit}>Add rule</Button> : undefined} />
            ) : (
              <ol className="divide-y divide-line">
                {rules.map((r) => (
                  <li key={r.id} className={cn('px-4 py-3', !r.isEnabled && 'opacity-60')}>
                    <div className="flex items-start gap-3">
                      <span className="mono text-xs text-muted pt-1 w-6 text-right shrink-0">{r.order}</span>
                      <div className="min-w-0 flex-1 space-y-1.5">
                        <div className="flex items-center gap-2 flex-wrap">
                          <p className="text-sm font-medium text-ink">{r.name}</p>
                          {!r.isEnabled && <Badge tone="neutral">disabled</Badge>}
                          <IdChip id={r.id} className="text-muted" />
                        </div>
                        {r.description && <p className="text-xs text-ink-2">{r.description}</p>}
                        <pre className="mono text-xs rounded-md border border-line bg-surface-2 px-3 py-2 whitespace-pre-wrap break-words text-ink">{r.condition}</pre>
                        <div className="flex items-center gap-1.5 flex-wrap text-xs">
                          <span className="text-muted">then</span>
                          <StatusBadge value={r.action.kind} />
                          {r.action.strategy && <Badge tone="info">{humanize(r.action.strategy)}</Badge>}
                          {r.action.priority && <StatusBadge value={r.action.priority} />}
                          {r.action.assignToRole && <Badge tone="neutral">role: {r.action.assignToRole}</Badge>}
                          {r.action.note && <span className="text-muted truncate max-w-xs" title={r.action.note}>“{r.action.note}”</span>}
                        </div>
                      </div>
                      {isDraft && (
                        <div className="flex items-center gap-1 shrink-0">
                          <Button size="xs" variant="ghost" onClick={() => openForm(r)} disabled={!canEdit} aria-label={`Edit ${r.name}`} icon={<Icons.edit className="size-3.5" />}>Edit</Button>
                          <Button size="xs" variant="ghost" className="text-crit-ink" onClick={() => setDeleteTarget(r)} disabled={!canEdit} aria-label={`Delete ${r.name}`} icon={<Icons.trash className="size-3.5" />}>Delete</Button>
                        </div>
                      )}
                    </div>
                  </li>
                ))}
              </ol>
            )}
          </Card>
          {set && <DryRunPanel setId={set.id} />}
        </div>
        <div className="space-y-4">
          <Card title="Version">
            {set ? (
              <KeyValueList
                items={[
                  { label: 'Rule set id', value: <IdChip id={set.id} length={36} /> },
                  { label: 'Family', value: <IdChip id={set.familyId} length={36} /> },
                  { label: 'Status', value: <StatusBadge value={set.status} /> },
                  { label: 'Created', value: fmtDateTime(set.createdAt) },
                  { label: 'Published', value: set.publishedAt ? `${fmtDateTime(set.publishedAt)} by ${set.publishedBy ?? ''}` : '—' },
                ]}
              />
            ) : <SkeletonLines lines={5} />}
          </Card>
          <RuleLanguageCard />
        </div>
      </div>

      {form.open && <RuleForm key={form.key} open={form.open} onClose={closeForm} initial={form.rule} nextOrder={nextOrder} pending={add.isPending || update.isPending} error={add.error ?? update.error} onSubmit={submitRule} />}

      <Modal open={publishOpen} onClose={() => setPublishOpen(false)} title={`Publish v${set?.version ?? ''}`} description="The currently published version of this family is retired and new reconciliation runs use this one." size="sm" footer={<><Button onClick={() => setPublishOpen(false)}>Cancel</Button><Button variant="primary" type="submit" form="publish-form" loading={publish.isPending}>Publish</Button></>}>
        <form id="publish-form" onSubmit={doPublish} className="space-y-3">
          <Field label="Change note" hint="Optional. What changed and why; shown on the version.">{(p) => <Textarea {...p} value={changeNote} onChange={(e) => setChangeNote(e.target.value)} rows={3} data-autofocus />}</Field>
          <ErrorAlert error={publish.error} title="Could not publish" />
        </form>
      </Modal>

      <Modal open={deleteTarget !== null} onClose={() => setDeleteTarget(null)} title="Delete rule" size="sm" footer={<><Button onClick={() => setDeleteTarget(null)}>Cancel</Button><Button variant="danger" loading={remove.isPending} onClick={() => deleteTarget && remove.mutate(deleteTarget.id, { onSuccess: () => { toast.info('Rule deleted'); setDeleteTarget(null); } })}>Delete</Button></>}>
        <p className="text-sm text-ink-2">Remove <strong className="text-ink">{deleteTarget?.name}</strong> from this draft? This cannot be undone.</p>
        <ErrorAlert error={remove.error} className="mt-3" />
      </Modal>
    </div>
  );
}
