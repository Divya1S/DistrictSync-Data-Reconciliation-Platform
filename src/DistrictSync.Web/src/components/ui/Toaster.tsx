import { Link } from 'react-router';
import { cn } from '@/lib/cn';
import { dismissToast, toastStore, type ToastTone } from './toastStore';

const toneStyles: Record<ToastTone, string> = {
  info: 'border-l-accent',
  success: 'border-l-good',
  warning: 'border-l-warn',
  error: 'border-l-crit',
};

const icons: Record<ToastTone, React.ReactNode> = {
  info: <path d="M12 8v4m0 4h.01M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0Z" />,
  success: <path d="m8 12.5 2.5 2.5L16 9.5M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0Z" />,
  warning: <path d="M12 9v4m0 4h.01M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z" />,
  error: <path d="m15 9-6 6m0-6 6 6M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0Z" />,
};

export function Toaster() {
  const toasts = toastStore.use();
  return (
    <div className="fixed bottom-4 right-4 z-[60] flex w-full max-w-sm flex-col gap-2 pointer-events-none" aria-live="polite" aria-relevant="additions">
      {toasts.map((t) => (
        <div key={t.id} role="status" className={cn('toast-enter pointer-events-auto card flex items-start gap-3 border-l-4 px-3 py-2.5 shadow-lg', toneStyles[t.tone])}>
          <svg viewBox="0 0 24 24" className="size-4 mt-0.5 shrink-0 text-ink-2" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            {icons[t.tone]}
          </svg>
          <div className="min-w-0 flex-1">
            <p className="text-sm font-medium text-ink leading-5">{t.title}</p>
            {t.description && <p className="text-xs text-ink-2 mt-0.5 break-words">{t.description}</p>}
            {t.href && (
              <Link to={t.href} onClick={() => dismissToast(t.id)} className="text-xs text-accent hover:underline mt-1 inline-block">
                Open
              </Link>
            )}
          </div>
          <button type="button" onClick={() => dismissToast(t.id)} aria-label="Dismiss notification" className="text-muted hover:text-ink -mr-1 rounded p-0.5">
            <svg viewBox="0 0 20 20" className="size-3.5" fill="none" stroke="currentColor" strokeWidth="1.75" aria-hidden="true"><path d="m5 5 10 10M15 5 5 15" /></svg>
          </button>
        </div>
      ))}
    </div>
  );
}
