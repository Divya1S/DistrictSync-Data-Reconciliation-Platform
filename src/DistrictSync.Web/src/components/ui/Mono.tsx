import { useState } from 'react';
import { cn } from '@/lib/cn';
import { shortId } from '@/lib/format';

/** Monospace id with full value on hover and click-to-copy. */
export function IdChip({ id, length = 8, className }: { id: string | null | undefined; length?: number; className?: string }) {
  const [copied, setCopied] = useState(false);
  if (!id) return <span className="text-muted">—</span>;
  const copy = async (e: React.MouseEvent) => {
    e.stopPropagation();
    try {
      await navigator.clipboard.writeText(id);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1200);
    } catch {
      /* clipboard unavailable */
    }
  };
  return (
    <button
      type="button"
      onClick={copy}
      title={copied ? 'Copied' : `${id} (click to copy)`}
      aria-label={`Copy id ${id}`}
      className={cn('mono inline-flex items-center rounded px-1 -mx-1 text-ink-2 hover:bg-surface-3 hover:text-ink', copied && 'text-good-ink', className)}
    >
      {copied ? 'copied' : shortId(id, length)}
    </button>
  );
}

export function Mono({ children, className, block = false }: { children: React.ReactNode; className?: string; block?: boolean }) {
  return block ? (
    <pre className={cn('mono whitespace-pre-wrap break-words rounded-md border border-line bg-surface-2 px-3 py-2 text-ink', className)}>{children}</pre>
  ) : (
    <code className={cn('mono rounded bg-surface-2 px-1 py-0.5 text-ink', className)}>{children}</code>
  );
}

export function CopyButton({ value, label = 'Copy' }: { value: string; label?: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <button
      type="button"
      className="text-xs text-accent hover:underline"
      onClick={async () => {
        try {
          await navigator.clipboard.writeText(value);
          setCopied(true);
          window.setTimeout(() => setCopied(false), 1200);
        } catch {
          /* ignore */
        }
      }}
    >
      {copied ? 'Copied' : label}
    </button>
  );
}
