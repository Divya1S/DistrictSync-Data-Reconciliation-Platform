import { useMemo, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import type { RuleSetSummaryDto } from '@/api/types';
import { useAuth } from '@/auth/useAuth';
import { Icons } from '@/components/layout/icons';
import { ErrorAlert } from '@/components/ui/Alert';
import { StatusBadge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { Field, Input } from '@/components/ui/Field';
import { Modal } from '@/components/ui/Modal';
import { PageHeader } from '@/components/ui/PageHeader';
import { SkeletonLines } from '@/components/ui/Skeleton';
import { toast } from '@/components/ui/toastStore';
import { cn } from '@/lib/cn';
import { fmtDateTime, fmtNumber } from '@/lib/format';
import { useCreateRuleSet, useRuleSets } from './hooks';

interface Family {
  familyId: string;
  name: string;
  entityType: string;
  versions: RuleSetSummaryDto[];
}

export default function RulesPage() {
  const query = useRuleSets();
  const auth = useAuth();
  const [createOpen, setCreateOpen] = useState(false);
  const canEdit = auth.hasRole('admin');

  const families = useMemo<Family[]>(() => {
    const map = new Map<string, Family>();
    for (const s of query.data ?? []) {
      const f = map.get(s.familyId) ?? { familyId: s.familyId, name: s.name, entityType: s.entityType, versions: [] };
      f.versions.push(s);
      // Name of the newest version represents the family.
      if (s.version >= Math.max(...f.versions.map((v) => v.version))) f.name = s.name;
      map.set(s.familyId, f);
    }
    return [...map.values()]
      .map((f) => ({ ...f, versions: [...f.versions].sort((a, b) => b.version - a.version) }))
      .sort((a, b) => a.entityType.localeCompare(b.entityType) || a.name.localeCompare(b.name));
  }, [query.data]);

  return (
    <div className="space-y-4">
      <PageHeader
        title="Rules"
        description="Versioned rule sets per entity type. Only one version per family is published and used by reconciliation runs."
        actions={<Button variant="primary" onClick={() => setCreateOpen(true)} disabled={!canEdit} title={canEdit ? undefined : 'Requires the admin role'} icon={<Icons.plus className="size-4" />}>New rule set</Button>}
      />
      {query.isError && <ErrorAlert error={query.error} />}
      {query.isLoading && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          {[0, 1].map((i) => (<Card key={i}><SkeletonLines lines={4} /></Card>))}
        </div>
      )}
      {!query.isLoading && families.length === 0 && (
        <Card><EmptyState title="No rule sets yet" description="Create a draft, add rules and publish it to start auto-resolving discrepancies." action={<Button variant="primary" onClick={() => setCreateOpen(true)} disabled={!canEdit}>New rule set</Button>} /></Card>
      )}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {families.map((f) => (
          <Card key={f.familyId} title={f.name} description={<span>Entity type <code className="mono">{f.entityType}</code> · family <code className="mono">{f.familyId.slice(0, 8)}</code></span>} bodyClassName="p-0">
            <ul className="divide-y divide-line">
              {f.versions.map((v) => (
                <li key={v.id}>
                  <Link to={`/rules/${v.id}`} className={cn('flex items-center gap-3 px-4 py-2.5 hover:bg-surface-2 focus-visible:bg-surface-2', v.status === 'Published' && 'bg-good/5')}>
                    <span className="mono text-sm font-semibold text-ink w-10">v{v.version}</span>
                    <StatusBadge value={v.status} />
                    <span className="text-xs text-ink-2 tabular-nums">{fmtNumber(v.ruleCount)} rule{v.ruleCount === 1 ? '' : 's'}</span>
                    <span className="ml-auto text-xs text-muted whitespace-nowrap">{v.publishedAt ? `published ${fmtDateTime(v.publishedAt)}` : `created ${fmtDateTime(v.createdAt)}`}</span>
                    <Icons.external className="size-3.5 text-muted" />
                  </Link>
                </li>
              ))}
            </ul>
          </Card>
        ))}
      </div>
      <CreateRuleSetDialog open={createOpen} onClose={() => setCreateOpen(false)} />
    </div>
  );
}

function CreateRuleSetDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const create = useCreateRuleSet();
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [entityType, setEntityType] = useState('');
  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!name.trim() || !entityType.trim()) return;
    create.mutate(
      { name: name.trim(), entityType: entityType.trim() },
      {
        onSuccess: (set) => {
          toast.success('Draft rule set created', { description: `${set.name} v${set.version}` });
          onClose();
          void navigate(`/rules/${set.id}`);
        },
      },
    );
  };
  return (
    <Modal open={open} onClose={onClose} title="New rule set" description="Creates version 1 as a draft. Add rules, then publish." size="sm" footer={<><Button onClick={onClose}>Cancel</Button><Button variant="primary" type="submit" form="create-ruleset-form" loading={create.isPending} disabled={!name.trim() || !entityType.trim()}>Create draft</Button></>}>
      <form id="create-ruleset-form" onSubmit={submit} className="space-y-3">
        <Field label="Name" required>{(p) => <Input {...p} value={name} onChange={(e) => setName(e.target.value)} placeholder="Employee reconciliation" required data-autofocus />}</Field>
        <Field label="Entity type" required hint="Runs for this entity type use the published version of this family.">{(p) => <Input {...p} value={entityType} onChange={(e) => setEntityType(e.target.value)} placeholder="employee" required />}</Field>
        <ErrorAlert error={create.error} />
      </form>
    </Modal>
  );
}
