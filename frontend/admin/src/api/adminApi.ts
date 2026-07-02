import { apiBase } from '../config';
import { authHeader } from '@zeta/ui';

export type DayOfWeek =
  | 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday';

/** Hoe gesprekken uit de wachtrij worden aangeboden. */
export type OfferMode = 'AutoDispatch' | 'ManualPickup';

/** Verdeelmethode bij automatisch aanbieden. */
export type RoutingStrategy = 'RingAll' | 'LongestIdle' | 'Linear';

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
  welcomeText: string;
  closedText: string;
  voice: string;
  adHocClosed: boolean;
  adHocForwardNumber: string | null;
  timeZone: string;
  openingHours: OpeningHours[];
  numbers: string[];
  musicOnHoldClass: string;
  offerMode: OfferMode;
  routingStrategy: RoutingStrategy;
}

export interface QueueWriteRequest {
  name: string;
  displayName: string;
  welcomeText: string;
  closedText: string;
  voice: string;
  adHocClosed: boolean;
  adHocForwardNumber: string | null;
  timeZone: string;
  openingHours: OpeningHours[];
  numbers: string[];
  musicOnHoldClass: string;
  offerMode: OfferMode;
  routingStrategy: RoutingStrategy;
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

export interface Contact {
  id: number;
  name: string;
  number: string;
  department: string | null;
}

export interface ContactWriteRequest {
  name: string;
  number: string;
  department: string | null;
}

export interface Settings {
  wrapUpSeconds: number;
}

async function http<T>(path: string, init?: RequestInit): Promise<T> {
  const headers: Record<string, string> = { ...authHeader() };
  if (init?.body) headers['Content-Type'] = 'application/json';
  const res = await fetch(`${apiBase}/api/admin/${path}`, { ...init, headers });
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

  listContacts: () => http<Contact[]>('contacts'),
  getContact: (id: number) => http<Contact>(`contacts/${id}`),
  createContact: (c: ContactWriteRequest) => http<Contact>('contacts', { method: 'POST', body: JSON.stringify(c) }),
  updateContact: (id: number, c: ContactWriteRequest) =>
    http<Contact>(`contacts/${id}`, { method: 'PUT', body: JSON.stringify(c) }),
  deleteContact: (id: number) => http<void>(`contacts/${id}`, { method: 'DELETE' }),

  getSettings: () => http<Settings>('settings'),
  updateSettings: (s: Settings) => http<Settings>('settings', { method: 'PUT', body: JSON.stringify(s) }),

  // TTS-voorbeeld: geeft de gesynthetiseerde audio (WAV) terug om te beluisteren.
  async previewTts(text: string, voice: string): Promise<Blob> {
    const res = await fetch(`${apiBase}/api/admin/tts/preview`, {
      method: 'POST',
      headers: { ...authHeader(), 'Content-Type': 'application/json' },
      body: JSON.stringify({ text, voice }),
    });
    if (!res.ok) {
      let message = `HTTP ${res.status}`;
      try {
        const body = (await res.json()) as { error?: string };
        if (body?.error) message = body.error;
      } catch { /* geen JSON-body */ }
      throw new Error(message);
    }
    return res.blob();
  },
};
