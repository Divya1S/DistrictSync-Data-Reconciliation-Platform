import { cn } from '@/lib/cn';

export interface TabItem<K extends string> {
  key: K;
  label: React.ReactNode;
  count?: number;
}

export function Tabs<K extends string>({ items, value, onChange, className }: { items: Array<TabItem<K>>; value: K; onChange: (k: K) => void; className?: string }) {
  return (
    <div role="tablist" className={cn('flex items-center gap-1 border-b border-line px-2', className)}>
      {items.map((t) => {
        const active = t.key === value;
        return (
          <button
            key={t.key}
            role="tab"
            type="button"
            aria-selected={active}
            onClick={() => onChange(t.key)}
            className={cn(
              '-mb-px flex items-center gap-1.5 border-b-2 px-3 py-2 text-sm font-medium transition-colors',
              active ? 'border-accent text-ink' : 'border-transparent text-ink-2 hover:text-ink hover:border-line-2',
            )}
          >
            {t.label}
            {t.count !== undefined && <span className="rounded-full bg-surface-3 px-1.5 text-[11px] text-ink-2 tabular-nums">{t.count}</span>}
          </button>
        );
      })}
    </div>
  );
}
