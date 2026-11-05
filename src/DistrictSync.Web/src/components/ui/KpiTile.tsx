import { Link } from 'react-router';
import { cn } from '@/lib/cn';
import { fmtCompact } from '@/lib/format';
import { Skeleton } from './Skeleton';

export interface KpiTileProps {
  label: string;
  value: number | undefined;
  hint?: string;
  to?: string;
  tone?: 'neutral' | 'warn' | 'crit' | 'good' | 'info';
  loading?: boolean;
}

const accents = {
  neutral: 'before:bg-line-2',
  info: 'before:bg-accent',
  good: 'before:bg-good',
  warn: 'before:bg-warn',
  crit: 'before:bg-crit',
};

export function KpiTile({ label, value, hint, to, tone = 'neutral', loading }: KpiTileProps) {
  const highlight = tone !== 'neutral' && (value ?? 0) > 0;
  const body = (
    <div className={cn('card relative overflow-hidden px-4 py-3 h-full before:absolute before:inset-y-0 before:left-0 before:w-0.5', highlight ? accents[tone] : accents.neutral, to && 'hover:border-line-2 hover:shadow-md transition-shadow')}>
      <p className="text-xs text-ink-2 leading-4">{label}</p>
      {loading ? <Skeleton className="h-7 w-20 mt-1.5" /> : <p className="text-2xl font-semibold text-ink leading-8 mt-1 tabular-nums">{fmtCompact(value)}</p>}
      {hint && <p className="text-[11px] text-muted mt-0.5">{hint}</p>}
    </div>
  );
  return to ? <Link to={to} className="block h-full rounded-lg focus-visible:outline-2">{body}</Link> : body;
}
