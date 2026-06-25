import { apiBase } from '../config';

export type DayOfWeek =
  | 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday';

/** Tijden komen als "HH:mm:ss" van de backend (TimeOnly). */
export interface OpeningHours {
  day: DayOfWeek;
  opens: string;
  closes: string;
}

export interface QueueListItem {
  id: number;
  name: string;
  displayName: string;
  numberCount: number;
  adHocClosed: boolean;
  openNow: boolean;
}

export interface QueueDetail {
  id: number;
  name: string;
  displayName: string;
  welcomePrompt: string;
  closedPrompt: string;
  adHocClosed: boolean;
  adHocForwardNumber: string | null;
  timeZone: string;
  openingHours: OpeningHours[];
  numbers: string[];
}

export interface QueueWriteRequest {
  name: string;
  displayName: string;
  welcomePrompt: string;
  closedPrompt: string;
  adHocClosed: boolean;
  adHocForwardNumber: string | null;
  timeZone: string;
  openingHours: OpeningHours[];
  numbers: string[];
}

export interface AgentListItem {
  id: number;
  name: string;
  displayName: string;
  endpoint: string;
  queues: string[];
}

export interface AgentDetail {
  id: number;
  name: string;
  displayName: string;
  endpoint: string;
  queueIds: number[];
}

export interface AgentWriteRequest {
  name: string;
  displayName: string;
  endpoint: string;
  queueIds: number[];
}

async function http<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${apiBase}/api/admin/${path}`, {
    ...init,
    headers: init?.body ? { 'Content-Type': 'application/json' } : undefined,
  });
  if (!res.ok) {
    let message = `HTTP ${res.status}`;
    try {
      const body = (await res.json()) as { error?: string };
      if (body?.error) message = body.error;
    } catch { /* geen JSON-body */ }
    throw new Error(message);
  }
  return (res.status === 204 ? undefined : await res.json()) as T;
}

export const adminApi = {
  listQueues: () => http<QueueListItem[]>('queues'),
  getQueue: (id: number) => http<QueueDetail>(`queues/${id}`),
  createQueue: (q: QueueWriteRequest) => http<QueueDetail>('queues', { method: 'POST', body: JSON.stringify(q) }),
  updateQueue: (id: number, q: QueueWriteRequest) =>
    http<QueueDetail>(`queues/${id}`, { method: 'PUT', body: JSON.stringify(q) }),
  deleteQueue: (id: number) => http<void>(`queues/${id}`, { method: 'DELETE' }),

  listAgents: () => http<AgentListItem[]>('agents'),
  getAgent: (id: number) => http<AgentDetail>(`agents/${id}`),
  createAgent: (a: AgentWriteRequest) => http<AgentDetail>('agents', { method: 'POST', body: JSON.stringify(a) }),
  updateAgent: (id: number, a: AgentWriteRequest) =>
    http<AgentDetail>(`agents/${id}`, { method: 'PUT', body: JSON.stringify(a) }),
  deleteAgent: (id: number) => http<void>(`agents/${id}`, { method: 'DELETE' }),
};
