import { Badge } from '@mantine/core';
import type { AgentStatus } from '../api/agentApi';

const MAP: Record<AgentStatus, { label: string; color: string }> = {
  LoggedOut: { label: 'afgemeld', color: 'gray' },
  Available: { label: 'beschikbaar', color: 'green' },
  Ringing: { label: 'wordt gebeld', color: 'yellow' },
  OnCall: { label: 'in gesprek', color: 'blue' },
  WrapUp: { label: 'nawerktijd', color: 'orange' },
};

export function AgentStatusBadge({ status }: { status: AgentStatus }) {
  const { label, color } = MAP[status];
  return (
    <Badge color={color} variant="light" size="lg">
      {label}
    </Badge>
  );
}
