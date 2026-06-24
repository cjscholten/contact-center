import { apiBase } from '../config';

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
  const response = await fetch(`${apiBase}/api/agents/${encodeURIComponent(name)}/${path}`, {
    method: 'POST',
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) throw new Error(`${path}: HTTP ${response.status}`);
}

export const agentApi = {
  async get(name: string): Promise<AgentSnapshot | null> {
    const response = await fetch(`${apiBase}/api/agents/${encodeURIComponent(name)}`);
    return response.ok ? (response.json() as Promise<AgentSnapshot>) : null;
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
  async searchDirectory(query: string, exclude?: string): Promise<DirectoryEntry[]> {
    const params = new URLSearchParams();
    if (query) params.set('q', query);
    if (exclude) params.set('exclude', exclude);
    const response = await fetch(`${apiBase}/api/directory/search?${params.toString()}`);
    return response.ok ? (response.json() as Promise<DirectoryEntry[]>) : [];
  },
};
