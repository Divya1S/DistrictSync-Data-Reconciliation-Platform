import { createStore } from '@/lib/store';
import type { IngestionProgressEvent, LiveUpdate, ReconciliationProgressEvent } from '@/api/types';

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

export interface ActivityItem {
  id: number;
  update: LiveUpdate;
  receivedAt: number;
}

export interface LiveState {
  connection: ConnectionState;
  lastError: string | null;
  activity: ActivityItem[];
  ingestionProgress: Record<string, IngestionProgressEvent>;
  reconciliationProgress: Record<string, ReconciliationProgressEvent>;
}

const MAX_ACTIVITY = 60;
let seq = 1;

export const liveStore = createStore<LiveState>({
  connection: 'disconnected',
  lastError: null,
  activity: [],
  ingestionProgress: {},
  reconciliationProgress: {},
});

export function setConnection(connection: ConnectionState, lastError: string | null = null): void {
  liveStore.set((s) => ({ ...s, connection, lastError }));
}

export function recordUpdate(update: LiveUpdate): void {
  liveStore.set((s) => {
    const next: LiveState = { ...s };
    // Progress events are ephemeral and high-frequency: keep them out of the activity feed.
    if (update.kind === 'ingestion.progress') {
      const p = update.payload as IngestionProgressEvent;
      next.ingestionProgress = { ...s.ingestionProgress, [p.batchId]: p };
    } else if (update.kind === 'reconciliation.progress') {
      const p = update.payload as ReconciliationProgressEvent;
      next.reconciliationProgress = { ...s.reconciliationProgress, [p.runId]: p };
    } else {
      next.activity = [{ id: seq++, update, receivedAt: Date.now() }, ...s.activity].slice(0, MAX_ACTIVITY);
      if (update.kind === 'ingestion.batch-completed' || update.kind === 'ingestion.batch-failed') {
        const { batchId } = update.payload as { batchId: string };
        if (batchId in s.ingestionProgress) {
          next.ingestionProgress = { ...s.ingestionProgress };
          delete next.ingestionProgress[batchId];
        }
      }
      if (update.kind === 'reconciliation.completed' || update.kind === 'reconciliation.failed') {
        const { runId } = update.payload as { runId: string };
        if (runId in s.reconciliationProgress) {
          next.reconciliationProgress = { ...s.reconciliationProgress };
          delete next.reconciliationProgress[runId];
        }
      }
    }
    return next;
  });
}

export function clearActivity(): void {
  liveStore.set((s) => ({ ...s, activity: [] }));
}
