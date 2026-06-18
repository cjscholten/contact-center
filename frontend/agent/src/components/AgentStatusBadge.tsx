import { Badge } from '@mantine/core';
import type { AgentStatus, Presence } from '../api/agentApi';

const PRESENCE: Record<Presence, { label: string; color: string }> = {
  Available: { label: 'beschikbaar', color: 'green' },
  Break: { label: 'pauze', color: 'gray' },
  Unavailable: { label: 'niet beschikbaar', color: 'red' },
};

const CALL: Partial<Record<AgentStatus, { label: string; color: string }>> = {
  LoggedOut: { label: 'afgemeld', color: 'gray' },
  Ringing: { label: 'wordt gebeld', color: 'yellow' },
  OnCall: { label: 'in gesprek', color: 'blue' },
  WrapUp: { label: 'nawerktijd', color: 'orange' },
};

export function AgentStatusBadge({ status, presence }: { status: AgentStatus; presence: Presence }) {
  // Een lopende gespreksfase overheerst; anders toont de badge de handmatige presence.
  const view = CALL[status] ?? PRESENCE[presence];
  return (
    <Badge color={view.color} variant="light" size="lg">
      {view.label}
    </Badge>
  );
}
