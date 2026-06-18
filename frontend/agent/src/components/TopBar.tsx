import { Button, Group, Text, Title } from '@mantine/core';
import { IconLogout, IconPhone, IconPhoneOff, IconPlayerPause, IconPlayerPlay } from '@tabler/icons-react';
import type { AgentStatus } from '../api/agentApi';
import type { CallState } from '../softphone/useSoftphone';
import { AgentStatusBadge } from './AgentStatusBadge';

interface Props {
  agentName: string;
  status: AgentStatus;
  callState: CallState;
  onHold: boolean;
  onAnswer: () => void;
  onHangup: () => void;
  onToggleHold: () => void;
  onFinishWrapUp: () => void;
  onLogout: () => void;
}

export function TopBar(props: Props) {
  const inCall = props.callState === 'in_call';
  return (
    <Group h="100%" px="md" justify="space-between" wrap="nowrap">
      <Group gap="sm">
        <Title order={3}>ZetaDesk</Title>
        <AgentStatusBadge status={props.status} />
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
