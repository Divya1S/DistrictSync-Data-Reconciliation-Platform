import { cn } from '@/lib/cn';

export function EmptyState({ title, description, action, className, compact = false }: { title: string; description?: string; action?: React.ReactNode; className?: string; compact?: boolean }) {
  return (
    <div className={cn('flex flex-col items-center justify-center text-center', compact ? 'py-6' : 'py-12', className)}>
      <svg className="size-8 text-muted mb-2" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true">
        <path d="M4 7.5 12 4l8 3.5v9L12 20l-8-3.5z" />
        <path d="M4 7.5 12 11l8-3.5M12 11v9" />
      </svg>
      <p className="text-sm font-medium text-ink">{title}</p>
      {description && <p className="text-xs text-muted mt-1 max-w-sm">{description}</p>}
      {action && <div className="mt-3">{action}</div>}
    </div>
  );
}
