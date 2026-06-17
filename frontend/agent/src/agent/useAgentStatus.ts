import { useEffect, useState } from 'react';
import { agentApi, type AgentStatus } from '../api/agentApi';

/** Pollt de agent-status (2s). Later te vervangen door SignalR-push. */
export function useAgentStatus(agentName: string | null): AgentStatus {
  const [status, setStatus] = useState<AgentStatus>('LoggedOut');

  useEffect(() => {
    if (!agentName) {
      setStatus('LoggedOut');
      return;
    }
    let active = true;
    const poll = async () => {
      try {
        const snapshot = await agentApi.get(agentName);
        if (active && snapshot) setStatus(snapshot.status);
      } catch {
        /* backend even weg; volgende tick opnieuw */
      }
    };
    void poll();
    const id = window.setInterval(poll, 2000);
    return () => {
      active = false;
      clearInterval(id);
    };
  }, [agentName]);

  return status;
}
