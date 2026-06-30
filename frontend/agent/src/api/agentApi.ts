import { apiBase } from '../config';
import { authHeader } from '@zeta/ui';

export type AgentStatus = 'LoggedOut' | 'Available' | 'Ringing' | 'OnCall' | 'WrapUp';
export type Presence = 'Available' | 'Break' | 'Unavailable';

export interface AgentSnapshot {
  name: string;
  displayName: string;
  status: AgentStatus;
  presence: Presence;
  since: string;
}

export interface DirectoryEntry {
  kind: 'agent' | 'contact';
  label: string;
  detail: string;
  target: string;
}

async function send(name: string, path: string, body?: unknown): Promise<void> {
  const headers: Record<string, string> = { ...authHeader() };
  if (body) headers['Content-Type'] = 'application/json';
  const response = await fetch(`${apiBase}/api/agents/${encodeURIComponent(name)}/${path}`, {
    method: 'POST',
    headers,
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) throw new Error(`${path}: HTTP ${response.status}`);
}

export const agentApi = {
  async get(name: string): Promise<AgentSnapshot | null> {
    const response = await fetch(`${apiBase}/api/agents/${encodeURIComponent(name)}`, {
      headers: authHeader(),
    });
    return response.ok ? (response.json() as Promise<AgentSnapshot>) : null;
  },
  async getSipCredentials(): Promise<{ username: string; password: string }> {
    const response = await fetch(`${apiBase}/api/agents/me/sip`, { headers: authHeader() });
    if (!response.ok) throw new Error(`SIP-gegevens ophalen mislukt: HTTP ${response.status}`);
    return response.json() as Promise<{ username: string; password: string }>;
  },
  login: (name: string) => send(name, 'login'),
  logout: (name: string) => send(name, 'logout'),
  finishWrapUp: (name: string) => send(name, 'wrapup/finish'),
  hold: (name: string) => send(name, 'hold'),
  unhold: (name: string) => send(name, 'unhold'),
  coldTransfer: (name: string, target: string) => send(name, 'transfer/cold', { target }),
  setPresence: (name: string, presence: Presence) => send(name, 'presence', { presence }),
  pickup: (name: string, callId: string) => send(name, `calls/${encodeURIComponent(callId)}/pickup`),
  transferToAgent: (name: string, agent: string) => send(name, 'transfer/agent', { agent }),
  // Warm doorverbinden (overleg): starten met een collega, daarna voltooien of annuleren.
  warmTransfer: (name: string, agent: string) => send(name, 'transfer/warm', { agent }),
  completeWarmTransfer: (name: string) => send(name, 'transfer/warm/complete'),
  cancelWarmTransfer: (name: string) => send(name, 'transfer/warm/cancel'),
  async searchDirectory(query: string, exclude?: string): Promise<DirectoryEntry[]> {
    const params = new URLSearchParams();
    if (query) params.set('q', query);
    if (exclude) params.set('exclude', exclude);
    const response = await fetch(`${apiBase}/api/directory/search?${params.toString()}`, {
      headers: authHeader(),
    });
    return response.ok ? (response.json() as Promise<DirectoryEntry[]>) : [];
  },
};
