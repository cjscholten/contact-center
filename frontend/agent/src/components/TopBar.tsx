import { Button, Group, Menu, Text, Title } from '@mantine/core';
import {
  IconChevronDown,
  IconLogout,
  IconPhone,
  IconPhoneOff,
  IconPlayerPause,
  IconPlayerPlay,
} from '@tabler/icons-react';
import type { AgentStatus, Presence } from '../api/agentApi';
import type { CallState } from '../softphone/useSoftphone';
import { AgentStatusBadge } from './AgentStatusBadge';

const PRESENCE_LABEL: Record<Presence, string> = {
  Available: 'Beschikbaar',
  Break: 'Pauze',
  Unavailable: 'Niet beschikbaar',
};

interface Props {
  agentName: string;
  status: AgentStatus;
  presence: Presence;
  callState: CallState;
  onHold: boolean;
  onAnswer: () => void;
  onHangup: () => void;
  onToggleHold: () => void;
  onFinishWrapUp: () => void;
  onSetPresence: (presence: Presence) => void;
  onLogout: () => void;
}

export function TopBar(props: Props) {
  const inCall = props.callState === 'in_call';
  return (
    <Group h="100%" px="md" justify="space-between" wrap="nowrap">
      <Group gap="sm">
        <Title order={3}>ZetaDesk</Title>
        <AgentStatusBadge status={props.status} presence={props.presence} />
      </Group>

      <Group gap="sm" wrap="nowrap">
        {props.callState === 'ringing' && (
          <Button color="green" leftSection={<IconPhone size={18} />} onClick={props.onAnswer}>
            Aannemen
          </Button>
        )}
        <Button
          color="red"
          variant="light"
          leftSection={<IconPhoneOff size={18} />}
          disabled={props.callState === 'idle'}
          onClick={props.onHangup}
        >
          Ophangen
        </Button>
        <Button
          variant="default"
          leftSection={props.onHold ? <IconPlayerPlay size={18} /> : <IconPlayerPause size={18} />}
          disabled={!inCall}
          onClick={props.onToggleHold}
        >
          {props.onHold ? 'Uit de wacht' : 'In de wacht'}
        </Button>
        {props.status === 'WrapUp' && (
          <Button color="orange" onClick={props.onFinishWrapUp}>
            Klaar
          </Button>
        )}

        <Menu position="bottom-end" withinPortal>
          <Menu.Target>
            <Button variant="subtle" rightSection={<IconChevronDown size={14} />}>
              {PRESENCE_LABEL[props.presence]}
            </Button>
          </Menu.Target>
          <Menu.Dropdown>
            <Menu.Label>Status</Menu.Label>
            <Menu.Item onClick={() => props.onSetPresence('Available')}>Beschikbaar</Menu.Item>
            <Menu.Item onClick={() => props.onSetPresence('Break')}>Pauze</Menu.Item>
            <Menu.Item onClick={() => props.onSetPresence('Unavailable')}>Niet beschikbaar</Menu.Item>
          </Menu.Dropdown>
        </Menu>

        <Text size="sm" c="dimmed">
          {props.agentName}
        </Text>
        <Button variant="subtle" color="gray" leftSection={<IconLogout size={16} />} onClick={props.onLogout}>
          Afmelden
        </Button>
      </Group>
    </Group>
  );
}
