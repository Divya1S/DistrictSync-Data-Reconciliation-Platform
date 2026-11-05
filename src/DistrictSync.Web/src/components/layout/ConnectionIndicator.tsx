import { cn } from '@/lib/cn';
import { liveStore, type ConnectionState } from '@/realtime/liveStore';

const meta: Record<ConnectionState, { label: string; dot: string; text: string }> = {
  connected: { label: 'Live', dot: 'bg-good', text: 'text-good-ink' },
  connecting: { label: 'Connecting', dot: 'bg-warn animate-pulse', text: 'text-warn-ink' },
  reconnecting: { label: 'Reconnecting', dot: 'bg-warn animate-pulse', text: 'text-warn-ink' },
  disconnected: { label: 'Offline', dot: 'bg-crit', text: 'text-crit-ink' },
};

export function ConnectionIndicator() {
  const { connection, lastError } = liveStore.use();
  const m = meta[connection];
  return (
    <span
      className={cn('inline-flex items-center gap-1.5 rounded-full border border-line px-2 py-0.5 text-[11px] font-medium', m.text)}
      title={lastError ? `${m.label}: ${lastError}` : `Real-time updates: ${m.label.toLowerCase()}`}
      role="status"
      aria-live="polite"
    >
      <span className={cn('size-1.5 rounded-full', m.dot)} aria-hidden="true" />
      {m.label}
    </span>
  );
}
