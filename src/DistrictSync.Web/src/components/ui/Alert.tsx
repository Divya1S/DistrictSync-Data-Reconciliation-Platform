import { cn } from '@/lib/cn';
import { errorMessage, isApiError } from '@/api/client';

type Tone = 'info' | 'success' | 'warning' | 'error';

const styles: Record<Tone, string> = {
  info: 'border-accent/40 bg-accent/8 text-ink',
  success: 'border-good/40 bg-good/10 text-ink',
  warning: 'border-warn/50 bg-warn/12 text-ink',
  error: 'border-crit/40 bg-crit/10 text-ink',
};

export function Alert({ tone = 'info', title, children, className }: { tone?: Tone; title?: string; children?: React.ReactNode; className?: string }) {
  return (
    <div role={tone === 'error' ? 'alert' : 'status'} className={cn('rounded-md border px-3 py-2 text-sm', styles[tone], className)}>
      {title && <p className="font-semibold">{title}</p>}
      {children && <div className={cn(title && 'mt-0.5', 'text-ink-2')}>{children}</div>}
    </div>
  );
}

/** Renders an API/unknown error with ProblemDetails validation errors listed. */
export function ErrorAlert({ error, title = 'Request failed', className }: { error: unknown; title?: string; className?: string }) {
  if (!error) return null;
  const messages = isApiError(error) ? error.validationMessages : [];
  return (
    <Alert tone="error" title={isApiError(error) ? error.title : title} className={className}>
      {isApiError(error) && error.detail && <p>{error.detail}</p>}
      {!isApiError(error) && <p>{errorMessage(error)}</p>}
      {messages.length > 0 && (
        <ul className="mt-1 list-disc pl-4 space-y-0.5">
          {messages.map((m) => (
            <li key={m}>{m}</li>
          ))}
        </ul>
      )}
    </Alert>
  );
}
