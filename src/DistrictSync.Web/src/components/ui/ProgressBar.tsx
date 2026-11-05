import { cn } from '@/lib/cn';

export function ProgressBar({ value, max = 100, indeterminate = false, label, className, tone = 'accent' }: { value?: number; max?: number; indeterminate?: boolean; label?: string; className?: string; tone?: 'accent' | 'good' | 'warn' | 'crit' }) {
  const pctValue = indeterminate || value === undefined ? 0 : Math.min(100, Math.max(0, (value / Math.max(1, max)) * 100));
  const fill = { accent: 'bg-accent', good: 'bg-good', warn: 'bg-warn', crit: 'bg-crit' }[tone];
  return (
    <div
      role="progressbar"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={indeterminate ? undefined : Math.round(pctValue)}
      className={cn('relative h-1.5 w-full overflow-hidden rounded-full bg-accent-soft/70', className)}
    >
      <div className={cn('h-full rounded-full transition-[width] duration-500', fill, indeterminate && 'progress-indeterminate')} style={indeterminate ? undefined : { width: `${pctValue}%` }} />
    </div>
  );
}
