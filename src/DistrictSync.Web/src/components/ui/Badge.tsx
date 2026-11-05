import { cn } from '@/lib/cn';
import { humanize } from '@/lib/format';

import { LIVE_STATUSES, toneFor, type Tone } from './statusTones';

export type { Tone };

const tones: Record<Tone, string> = {
  neutral: 'bg-surface-3 text-ink-2 border-line-2',
  info: 'bg-accent/12 text-accent border-accent/30',
  good: 'bg-good/12 text-good-ink border-good/35',
  warn: 'bg-warn/15 text-warn-ink border-warn/40',
  serious: 'bg-serious/15 text-serious-ink border-serious/40',
  crit: 'bg-crit/12 text-crit-ink border-crit/35',
  violet: 'bg-violet/12 text-violet-ink border-violet/35',
};

const dots: Record<Tone, string> = {
  neutral: 'bg-muted',
  info: 'bg-accent',
  good: 'bg-good',
  warn: 'bg-warn',
  serious: 'bg-serious',
  crit: 'bg-crit',
  violet: 'bg-violet',
};

export interface BadgeProps {
  tone?: Tone;
  children: React.ReactNode;
  className?: string;
  dot?: boolean;
  pulse?: boolean;
  title?: string;
}

export function Badge({ tone = 'neutral', children, className, dot = false, pulse = false, title }: BadgeProps) {
  return (
    <span
      title={title}
      className={cn('inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-[11px] font-semibold leading-4 whitespace-nowrap', tones[tone], className)}
    >
      {dot && <span className={cn('size-1.5 rounded-full', dots[tone], pulse && 'animate-pulse')} aria-hidden="true" />}
      {children}
    </span>
  );
}

export function StatusBadge({ value, className }: { value: string | null | undefined; className?: string }) {
  if (!value) return <span className="text-muted">—</span>;
  return (
    <Badge tone={toneFor(value)} dot pulse={LIVE_STATUSES.has(value)} className={className} title={value}>
      {humanize(value)}
    </Badge>
  );
}
