import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { CASE_PRIORITIES, RESOLUTION_STRATEGIES, RULE_ACTION_KINDS, type CasePriority, type ResolutionStrategy, type RuleActionKind, type RuleDto, type UpsertRuleRequest, type ValidateRuleResponse } from '@/api/types';
import { Alert, ErrorAlert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Checkbox, Field, Input, Select, Textarea } from '@/components/ui/Field';
import { Modal } from '@/components/ui/Modal';
import { Spinner } from '@/components/ui/Spinner';
import { cn } from '@/lib/cn';
import { humanize } from '@/lib/format';
import { useValidateRule } from './hooks';

interface RuleFormProps {
  open: boolean;
  onClose: () => void;
  initial?: RuleDto | null;
  nextOrder: number;
  pending: boolean;
  error: unknown;
  onSubmit: (body: UpsertRuleRequest) => void;
}

const ROLE_OPTIONS = ['', 'analyst', 'approver', 'admin'];

export function RuleForm({ open, onClose, initial, nextOrder, pending, error, onSubmit }: RuleFormProps) {
  const [name, setName] = useState(initial?.name ?? '');
  const [description, setDescription] = useState(initial?.description ?? '');
  const [condition, setCondition] = useState(initial?.condition ?? '');
  const [kind, setKind] = useState<RuleActionKind>(initial?.action.kind ?? 'OpenCase');
  const [strategy, setStrategy] = useState<ResolutionStrategy>(initial?.action.strategy ?? 'PreferHigherTrust');
  const [priority, setPriority] = useState<CasePriority>(initial?.action.priority ?? 'Normal');
  const [assignToRole, setAssignToRole] = useState(initial?.action.assignToRole ?? '');
  const [note, setNote] = useState(initial?.action.note ?? '');
  const [isEnabled, setIsEnabled] = useState(initial?.isEnabled ?? true);
  const [order, setOrder] = useState<number>(initial?.order ?? nextOrder);

  const { mutate: runValidate, isPending: validating, isError: validationUnavailable } = useValidateRule();
  // Result tagged with the condition it validated, so a stale result never shows for new text.
  const [validated, setValidated] = useState<{ condition: string; result: ValidateRuleResponse } | null>(null);
  const trimmedCondition = condition.trim();
  const validation = validated?.condition === trimmedCondition ? validated.result : null;

  // Live validation against POST /rules/validate, debounced while typing.
  useEffect(() => {
    if (!trimmedCondition) return;
    const handle = window.setTimeout(() => {
      runValidate(trimmedCondition, { onSuccess: (result) => setValidated({ condition: trimmedCondition, result }) });
    }, 350);
    return () => window.clearTimeout(handle);
  }, [trimmedCondition, runValidate]);

  const errorMarker = useMemo(() => {
    if (!validation || validation.isValid || validation.errorPosition === null) return null;
    const pos = Math.max(0, Math.min(validation.errorPosition, condition.length));
    return { before: condition.slice(0, pos), after: condition.slice(pos) };
  }, [validation, condition]);

  const conditionInvalid = validation !== null && !validation.isValid;
  const canSubmit = name.trim() && trimmedCondition && !conditionInvalid && !validating;

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;
    onSubmit({
      name: name.trim(),
      description: description.trim() || null,
      condition: condition.trim(),
      action: {
        kind,
        strategy: kind === 'AutoResolve' ? strategy : null,
        priority: kind === 'OpenCase' ? priority : null,
        assignToRole: kind === 'OpenCase' && assignToRole ? assignToRole : null,
        note: note.trim() || null,
      },
      isEnabled,
      order: Number.isFinite(order) ? order : null,
    });
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={initial ? `Edit rule: ${initial.name}` : 'Add rule'}
      description="Rules run in order; the first enabled match wins."
      size="lg"
      footer={<><Button onClick={onClose}>Cancel</Button><Button variant="primary" type="submit" form="rule-form" loading={pending} disabled={!canSubmit}>{initial ? 'Save changes' : 'Add rule'}</Button></>}
    >
      <form id="rule-form" onSubmit={submit} className="space-y-4">
        <div className="grid grid-cols-1 sm:grid-cols-[1fr_6rem] gap-3">
          <Field label="Name" required>{(p) => <Input {...p} value={name} onChange={(e) => setName(e.target.value)} required data-autofocus placeholder="Auto-resolve phone-only conflicts" />}</Field>
          <Field label="Order" hint="Lower runs first">{(p) => <Input {...p} type="number" min={0} value={order} onChange={(e) => setOrder(Number(e.target.value))} />}</Field>
        </div>
        <Field label="Description">{(p) => <Input {...p} value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Why this rule exists" />}</Field>

        <Field
          label={
            <span className="flex items-center gap-2">
              Condition
              {validating && <Spinner className="size-3" />}
              {validation && !validating && (validation.isValid ? <Badge tone="good" dot>valid</Badge> : <Badge tone="crit" dot>invalid</Badge>)}
            </span>
          }
          required
          error={conditionInvalid ? `${validation.error ?? 'Invalid condition'}${validation.errorPosition !== null ? ` (position ${validation.errorPosition})` : ''}` : undefined}
          hint={validation?.isValid && validation.referencedFacts.length > 0 ? undefined : 'Validated as you type.'}
        >
          {(p) => (
            <div className="space-y-1.5">
              <Textarea {...p} value={condition} onChange={(e) => setCondition(e.target.value)} mono rows={4} required spellCheck={false} invalid={conditionInvalid} placeholder='discrepancy.type == "FieldConflict" and fields.only("phone")' />
              {errorMarker && (
                <pre className="mono text-xs rounded border border-crit/40 bg-crit/8 px-2 py-1 whitespace-pre-wrap break-all" aria-hidden="true">
                  {errorMarker.before}<span className="bg-crit text-white rounded-sm px-0.5">{errorMarker.after.charAt(0) || ' '}</span>{errorMarker.after.slice(1)}
                </pre>
              )}
              {validation?.isValid && validation.referencedFacts.length > 0 && (
                <p className="flex flex-wrap items-center gap-1 text-[11px] text-muted">
                  References:
                  {validation.referencedFacts.map((f) => (
                    <code key={f} className="mono rounded bg-surface-2 px-1 text-ink-2">{f}</code>
                  ))}
                </p>
              )}
            </div>
          )}
        </Field>

        <fieldset className="rounded-md border border-line p-3 space-y-3">
          <legend className="px-1 text-xs font-medium text-ink-2">Action</legend>
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-2">
            {RULE_ACTION_KINDS.map((k) => (
              <label key={k} className={cn('flex items-start gap-2 rounded-md border px-3 py-2 cursor-pointer', kind === k ? 'border-accent bg-accent/8' : 'border-line hover:bg-surface-2')}>
                <input type="radio" name="kind" value={k} checked={kind === k} onChange={() => setKind(k)} className="mt-1 accent-[var(--accent)]" />
                <span>
                  <span className="block text-sm font-medium text-ink">{humanize(k)}</span>
                  <span className="block text-xs text-muted">{k === 'AutoResolve' ? 'Apply a strategy without a human.' : k === 'OpenCase' ? 'Queue a case for review.' : 'Mark as ignored and move on.'}</span>
                </span>
              </label>
            ))}
          </div>
          {kind === 'AutoResolve' && (
            <Field label="Strategy" required>
              {(p) => (
                <Select {...p} value={strategy} onChange={(e) => setStrategy(e.target.value as ResolutionStrategy)}>
                  {RESOLUTION_STRATEGIES.map((s) => (<option key={s} value={s}>{humanize(s)}</option>))}
                </Select>
              )}
            </Field>
          )}
          {kind === 'OpenCase' && (
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <Field label="Priority" required>
                {(p) => (
                  <Select {...p} value={priority} onChange={(e) => setPriority(e.target.value as CasePriority)}>
                    {CASE_PRIORITIES.map((pr) => (<option key={pr} value={pr}>{pr}</option>))}
                  </Select>
                )}
              </Field>
              <Field label="Assign to role" hint="Optional. Workers with this role see it in their queue.">
                {(p) => (
                  <Select {...p} value={assignToRole} onChange={(e) => setAssignToRole(e.target.value)}>
                    {ROLE_OPTIONS.map((r) => (<option key={r} value={r}>{r || 'No role'}</option>))}
                  </Select>
                )}
              </Field>
            </div>
          )}
          <Field label="Note" hint="Stored on the discrepancy as the resolution note.">{(p) => <Input {...p} value={note} onChange={(e) => setNote(e.target.value)} />}</Field>
        </fieldset>

        <Checkbox label="Rule is enabled" checked={isEnabled} onChange={(e) => setIsEnabled(e.target.checked)} />
        {validationUnavailable && <Alert tone="warning">Live validation is unavailable right now; the server will validate on save.</Alert>}
        <ErrorAlert error={error} title="Could not save the rule" />
      </form>
    </Modal>
  );
}
