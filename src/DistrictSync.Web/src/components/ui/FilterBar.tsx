import { cn } from '@/lib/cn';

/** A single row of filter controls above a table; children are labelled controls. */
export function FilterBar({ children, className, trailing }: { children: React.ReactNode; className?: string; trailing?: React.ReactNode }) {
  return (
    <div className={cn('flex flex-wrap items-end gap-3 px-4 py-3 border-b border-line bg-surface', className)} role="search">
      {children}
      {trailing && <div className="ml-auto flex items-center gap-2">{trailing}</div>}
    </div>
  );
}

export function FilterField({ label, children, className }: { label: string; children: React.ReactNode; className?: string }) {
  return (
    <label className={cn('flex flex-col gap-1 text-[11px] font-medium text-muted uppercase tracking-wide', className)}>
      {label}
      {children}
    </label>
  );
}
