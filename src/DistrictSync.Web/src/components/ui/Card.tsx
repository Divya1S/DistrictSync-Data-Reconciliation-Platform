import { cn } from '@/lib/cn';

export interface CardProps {
  title?: React.ReactNode;
  description?: React.ReactNode;
  actions?: React.ReactNode;
  children?: React.ReactNode;
  className?: string;
  bodyClassName?: string;
  padded?: boolean;
}

export function Card({ title, description, actions, children, className, bodyClassName, padded = true }: CardProps) {
  return (
    <section className={cn('card flex flex-col min-w-0', className)}>
      {(title || actions) && (
        <header className="flex items-start justify-between gap-3 px-4 py-3 border-b border-line">
          <div className="min-w-0">
            {title && <h2 className="text-sm font-semibold text-ink leading-5">{title}</h2>}
            {description && <p className="text-xs text-muted mt-0.5">{description}</p>}
          </div>
          {actions && <div className="flex items-center gap-2 shrink-0">{actions}</div>}
        </header>
      )}
      <div className={cn('min-w-0 flex-1', padded && 'p-4', bodyClassName)}>{children}</div>
    </section>
  );
}
