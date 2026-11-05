import { Card } from '@/components/ui/Card';

const ROWS: Array<{ group: string; items: Array<[string, string]> }> = [
  {
    group: 'Operators',
    items: [
      ['== != < <= > >=', 'comparison'],
      ['and or not', 'boolean logic'],
      ['x in ["a","b"]', 'membership in a list'],
      ['contains startsWith endsWith', 'string tests, e.g. left.email endsWith "@acme.com"'],
      ['matches', 'regular expression, e.g. right.phone matches "^\\+1"'],
    ],
  },
  {
    group: 'Facts',
    items: [
      ['discrepancy.type', 'MissingInLeft · MissingInRight · FieldConflict · Duplicate'],
      ['discrepancy.severity', 'Low · Medium · High · Critical'],
      ['discrepancy.matchKey · discrepancy.entityType', 'identity of the record pair'],
      ['fields', 'list of differing field names'],
      ['fields.any("salary")', 'true when salary differs'],
      ['fields.only("phone","title")', 'true when nothing else differs'],
      ['left.<field> · right.<field>', 'attribute values on each side'],
      ['left._exists · right._exists', 'whether the record exists on that side'],
      ['source.left.trustRank · source.right.trustRank', 'trust rank of each source system'],
    ],
  },
  {
    group: 'Functions',
    items: [
      ['daysSince(x)', 'days between a date value and now'],
      ['diff(a, b)', 'numeric difference between two values'],
      ['len(x)', 'length of a string or list'],
      ['lower(x)', 'lower-case a string'],
      ['coalesce(a, b)', 'first non-null value'],
    ],
  },
];

const EXAMPLES = [
  'discrepancy.type == "FieldConflict" and fields.only("phone")',
  'discrepancy.severity in ["High","Critical"] and fields.any("salary")',
  'not right._exists and daysSince(left._updatedAt) > 30',
  'diff(left.salary, right.salary) <= 100 and source.left.trustRank > source.right.trustRank',
];

export function RuleLanguageCard() {
  return (
    <Card title="Rule language" description="Conditions are evaluated top to bottom; the first enabled rule that matches decides the action.">
      <div className="space-y-4 text-sm">
        {ROWS.map((g) => (
          <div key={g.group}>
            <h3 className="text-[11px] uppercase tracking-wide text-muted mb-1.5">{g.group}</h3>
            <dl className="grid grid-cols-[minmax(0,1.1fr)_minmax(0,1fr)] gap-x-3 gap-y-1">
              {g.items.map(([code, desc]) => (
                <div key={code} className="contents">
                  <dt><code className="mono text-ink break-words">{code}</code></dt>
                  <dd className="text-xs text-ink-2">{desc}</dd>
                </div>
              ))}
            </dl>
          </div>
        ))}
        <div>
          <h3 className="text-[11px] uppercase tracking-wide text-muted mb-1.5">Examples</h3>
          <ul className="space-y-1">
            {EXAMPLES.map((ex) => (
              <li key={ex}><code className="mono block rounded bg-surface-2 px-2 py-1 text-ink break-words">{ex}</code></li>
            ))}
          </ul>
        </div>
      </div>
    </Card>
  );
}
