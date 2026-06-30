import { useEffect, useState } from 'react';
import { agentApi, type AgentSnapshot } from '../api/agentApi';

/** Pollt de eigen agent-snapshot (status + presence, 2s). Later eventueel via SignalR-push. */
export function useAgentSnapshot(agentName: string | null): AgentSnapshot | null {
  const [snapshot, setSnapshot] = useState<AgentSnapshot | null>(null);

  useEffect(() => {
    if (!agentName) return;
    let active = true;
    const poll = async () => {
      try {
        const s = await agentApi.get(agentName);
        if (active) setSnapshot(s);
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

  // Zonder agent geen snapshot tonen; de (mogelijk verouderde) state wordt afgedekt door de
  // afgeleide return i.p.v. via een setState in het effect (voorkomt cascading renders).
  return agentName ? snapshot : null;
}
