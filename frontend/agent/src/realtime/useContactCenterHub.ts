import { useEffect, useState } from 'react';
import { type HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { apiBase } from '../config';
import { authHeader, getAccessToken } from '@zeta/ui';
import { agentApi, type AgentSnapshot } from '../api/agentApi';

export interface WaitingCall {
  callId: string;
  queueName: string;
  callerNumber: string;
  enqueuedAt: string;
}

export interface HubState {
  waiting: WaitingCall[];
  agentSnapshot: AgentSnapshot | null;
}

/**
 * Eén SignalR-verbinding voor de agent-schermen. Houdt zowel de live wachtrij ("queuesChanged")
 * als de eigen agent-status ("agentChanged", per-agent-groep) bij. De initiële standen komen via
 * GET /api/queues en GET /api/agents/{name}; daarna lopen updates via de hub (geen polling meer).
 */
export function useContactCenterHub(agentName: string | null): HubState {
  const [waiting, setWaiting] = useState<WaitingCall[]>([]);
  const [agentSnapshot, setAgentSnapshot] = useState<AgentSnapshot | null>(null);

  useEffect(() => {
    if (agentName === null) return;
    let connection: HubConnection | undefined;
    let cancelled = false;

    const loadSnapshot = async () => {
      try {
        const snap = await agentApi.get(agentName);
        if (!cancelled && snap) setAgentSnapshot(snap);
      } catch {
        /* volgende push/reconnect herstelt de stand */
      }
    };

    void (async () => {
      try {
        const response = await fetch(`${apiBase}/api/queues`, { headers: authHeader() });
        if (response.ok && !cancelled) setWaiting(await response.json());
      } catch {
        /* hub-update volgt zodra de verbinding staat */
      }
      await loadSnapshot();

      connection = new HubConnectionBuilder()
        .withUrl(`${apiBase}/hub`, { accessTokenFactory: () => getAccessToken() ?? '' })
        .withAutomaticReconnect()
        .build();
      connection.on('queuesChanged', (w: WaitingCall[]) => setWaiting(w));
      connection.on('agentChanged', (s: AgentSnapshot) => setAgentSnapshot(s));
      // Na een (her)verbinding kan er een push gemist zijn: haal de eigen status opnieuw op.
      connection.onreconnected(() => void loadSnapshot());
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
  }, [agentName]);

  // Zonder agent geen state tonen; afgeleide return i.p.v. een setState in het effect.
  return agentName === null
    ? { waiting: [], agentSnapshot: null }
    : { waiting, agentSnapshot };
}
