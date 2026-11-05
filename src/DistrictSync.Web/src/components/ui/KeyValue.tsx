import { cn } from '@/lib/cn';

export interface KV {
  label: string;
  value: React.ReactNode;
  mono?: boolean;
}

export function KeyValueList({ items, className, columns = 1 }: { items: KV[]; className?: string; columns?: 1 | 2 | 3 }) {
  return (
    <dl className={cn('grid gap-x-6 gap-y-2 text-sm', columns === 2 && 'sm:grid-cols-2', columns === 3 && 'sm:grid-cols-3', className)}>
      {items.map((it) => (
        <div key={it.label} className="min-w-0">
          <dt className="text-[11px] uppercase tracking-wide text-muted">{it.label}</dt>
          <dd className={cn('text-ink break-words', it.mono && 'mono')}>{it.value ?? '—'}</dd>
        </div>
      ))}
    </dl>
  );
}
