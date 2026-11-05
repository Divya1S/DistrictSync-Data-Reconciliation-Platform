import { useMemo, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router';
import { ErrorAlert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Field, Input, Select } from '@/components/ui/Field';
import { Modal } from '@/components/ui/Modal';
import { toast } from '@/components/ui/toastStore';
import { useSourceSystems } from '@/features/sources/hooks';
import { useComparisonProfiles, useCreateRun } from './hooks';

export function NewRunDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const sources = useSourceSystems();
  const profiles = useComparisonProfiles();
  const create = useCreateRun();
  const navigate = useNavigate();
  const [entityType, setEntityType] = useState('');
  const [leftId, setLeftId] = useState('');
  const [rightId, setRightId] = useState('');
  const [profileId, setProfileId] = useState('');

  const enabled = sources.data?.filter((s) => s.isEnabled) ?? [];
  const matchingProfiles = useMemo(() => {
    const all = profiles.data ?? [];
    const et = entityType.trim().toLowerCase();
    const matching = et ? all.filter((p) => p.entityType.toLowerCase() === et) : all;
    return matching.length > 0 ? matching : all;
  }, [profiles.data, entityType]);

  const sameSide = leftId !== '' && leftId === rightId;
  const valid = entityType.trim() && leftId && rightId && !sameSide;

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!valid) return;
    create.mutate(
      { entityType: entityType.trim(), leftSourceSystemId: leftId, rightSourceSystemId: rightId, comparisonProfileId: profileId || null },
      {
        onSuccess: (run) => {
          toast.success('Reconciliation queued', { description: `${run.leftSourceSystemCode} vs ${run.rightSourceSystemCode} for ${run.entityType}` });
          onClose();
          void navigate(`/reconciliation/runs/${run.id}`);
        },
      },
    );
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="New reconciliation run"
      description="Compares the latest records of two source systems for one entity type. The worker picks it up within seconds."
      footer={
        <>
          <Button onClick={onClose}>Cancel</Button>
          <Button variant="primary" type="submit" form="new-run-form" loading={create.isPending} disabled={!valid}>Queue run</Button>
        </>
      }
    >
      <form id="new-run-form" onSubmit={submit} className="space-y-3">
        <Field label="Entity type" required hint="Records of this type are compared, e.g. employee.">
          {(p) => <Input {...p} value={entityType} onChange={(e) => setEntityType(e.target.value)} placeholder="employee" required data-autofocus />}
        </Field>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <Field label="Left source" required error={sameSide ? 'Pick two different sources.' : undefined}>
            {(p) => (
              <Select {...p} value={leftId} onChange={(e) => setLeftId(e.target.value)} required>
                <option value="">Select…</option>
                {enabled.map((s) => (
                  <option key={s.id} value={s.id}>{s.code} — {s.name} (trust {s.trustRank})</option>
                ))}
              </Select>
            )}
          </Field>
          <Field label="Right source" required>
            {(p) => (
              <Select {...p} value={rightId} onChange={(e) => setRightId(e.target.value)} required invalid={sameSide}>
                <option value="">Select…</option>
                {enabled.map((s) => (
                  <option key={s.id} value={s.id}>{s.code} — {s.name} (trust {s.trustRank})</option>
                ))}
              </Select>
            )}
          </Field>
        </div>
        <Field label="Comparison profile" hint="Optional. Without one the tenant default for this entity type is used.">
          {(p) => (
            <Select {...p} value={profileId} onChange={(e) => setProfileId(e.target.value)}>
              <option value="">Default profile</option>
              {matchingProfiles.map((pr) => (
                <option key={pr.id} value={pr.id}>{pr.name} · {pr.entityType} · {pr.fields.length} fields{pr.isDefault ? ' (default)' : ''}</option>
              ))}
            </Select>
          )}
        </Field>
        <ErrorAlert error={create.error} title="Could not queue the run" />
      </form>
    </Modal>
  );
}
