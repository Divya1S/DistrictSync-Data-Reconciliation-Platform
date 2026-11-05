import { useEffect } from 'react';
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { HUB_URL } from '@/api/client';
import type { LiveUpdate } from '@/api/types';
import { getToken } from '@/auth/authStore';
import { handleLiveUpdate } from './handlers';
import { setConnection } from './liveStore';

/**
 * Maintains one hub connection for the authenticated session. The server pushes `liveUpdate`
 * messages; each one invalidates the relevant react-query caches and feeds the activity store.
 */
export function useSignalR(token: string | null): void {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!token) {
      setConnection('disconnected');
      return;
    }

    let disposed = false;
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl(HUB_URL, { accessTokenFactory: () => getToken() ?? '' })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000, 30000])
      .configureLogging(import.meta.env.DEV ? LogLevel.Information : LogLevel.Warning)
      .build();

    connection.on('liveUpdate', (update: LiveUpdate) => {
      if (!disposed && update && typeof update.kind === 'string') handleLiveUpdate(update, queryClient);
    });
    connection.onreconnecting((err) => setConnection('reconnecting', err?.message ?? null));
    connection.onreconnected(() => {
      setConnection('connected');
      // Anything missed while offline: refresh all active queries.
      void queryClient.invalidateQueries();
    });
    connection.onclose((err) => {
      if (!disposed) setConnection('disconnected', err?.message ?? null);
    });

    let retryTimer: number | undefined;
    const start = async () => {
      if (disposed) return;
      setConnection('connecting');
      try {
        await connection.start();
        if (!disposed) setConnection('connected');
      } catch (e) {
        if (disposed) return;
        setConnection('disconnected', e instanceof Error ? e.message : String(e));
        retryTimer = window.setTimeout(() => void start(), 5000);
      }
    };
    void start();

    return () => {
      disposed = true;
      if (retryTimer) window.clearTimeout(retryTimer);
      if (connection.state !== HubConnectionState.Disconnected) {
        void connection.stop().catch(() => undefined);
      }
      setConnection('disconnected');
    };
  }, [token, queryClient]);
}
