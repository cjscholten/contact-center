import { useEffect, useState } from 'react';
import { type HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { apiBase } from '../config';
import { authHeader, getAccessToken } from '@zeta/ui';

export interface WaitingCall {
  callId: string;
  queueName: string;
  callerNumber: string;
  enqueuedAt: string;
}

/**
 * Verbindt met de SignalR-hub en houdt de live wachtrij bij.
 * Haalt de initiële stand via GET /api/queues; updates komen op "queuesChanged".
 */
export function useContactCenterHub(enabled: boolean): { waiting: WaitingCall[] } {
  const [waiting, setWaiting] = useState<WaitingCall[]>([]);

  useEffect(() => {
    if (!enabled) return;
    let connection: HubConnection | undefined;
    let cancelled = false;

    void (async () => {
      try {
        const response = await fetch(`${apiBase}/api/queues`, { headers: authHeader() });
        if (response.ok && !cancelled) setWaiting(await response.json());
      } catch {
        /* hub-update volgt zodra de verbinding staat */
      }

      connection = new HubConnectionBuilder()
        .withUrl(`${apiBase}/hub`, { accessTokenFactory: () => getAccessToken() ?? '' })
        .withAutomaticReconnect()
        .build();
      connection.on('queuesChanged', (w: WaitingCall[]) => setWaiting(w));
      try {
        await connection.start();
      } catch {
        /* automatische reconnect probeert opnieuw */
      }
    })();

    return () => {
      cancelled = true;
      void connection?.stop();
    };
  }, [enabled]);

  // Uitgeschakeld: lege lijst teruggeven i.p.v. de state via setState in het effect te wissen.
  return { waiting: enabled ? waiting : [] };
}
