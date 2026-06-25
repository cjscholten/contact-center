import { Button, Group, Menu, Text, Title } from '@mantine/core';
import {
  IconCheck,
  IconChevronDown,
  IconLogout,
  IconPhone,
  IconPhoneOff,
  IconPlayerPause,
  IconPlayerPlay,
  IconX,
} from '@tabler/icons-react';
import type { AgentStatus, Presence } from '../api/agentApi';
import type { CallState } from '../softphone/useSoftphone';

const PRESENCE_LABEL: Record<Presence, string> = {
  Available: 'Beschikbaar',
  Break: 'Pauze',
  Unavailable: 'Niet beschikbaar',
};

// Afgeleide statusweergave (label + kleur): een lopende gespreksfase overheerst,
// anders toont de knop de handmatig gekozen beschikbaarheid.
const CALL_VIEW: Partial<Record<AgentStatus, { label: string; color: string }>> = {
  Ringing: { label: 'Wordt gebeld', color: 'yellow' },
  OnCall: { label: 'In gesprek', color: 'blue' },
  WrapUp: { label: 'Nawerktijd', color: 'orange' },
};
const PRESENCE_VIEW: Record<Presence, { label: string; color: string }> = {
  Available: { label: 'Beschikbaar', color: 'green' },
  Break: { label: 'Pauze', color: 'gray' },
  Unavailable: { label: 'Niet beschikbaar', color: 'red' },
};

interface Props {
  agentName: string;
  status: AgentStatus;
  presence: Presence;
  callState: CallState;
  onHold: boolean;
  consultWith: string | null;
  onAnswer: () => void;
  onHangup: () => void;
  onToggleHold: () => void;
  onFinishWrapUp: () => void;
  onSetPresence: (presence: Presence) => void;
  onCompleteWarmTransfer: () => void;
  onCancelWarmTransfer: () => void;
  onLogout: () => void;
}

export function TopBar(props: Props) {
  const inCall = props.callState === 'in_call';
  const consulting = props.consultWith !== null;
  const view = CALL_VIEW[props.status] ?? PRESENCE_VIEW[props.presence];

  return (
    <Group h="100%" px="md" justify="space-between" wrap="nowrap">
      <Group gap="sm" wrap="nowrap">
        <Title order={3} mr="xs">
          ZetaDesk
        </Title>
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
          disabled={!inCall || consulting}
          onClick={props.onToggleHold}
        >
          {props.onHold ? 'Uit de wacht' : 'In de wacht'}
        </Button>
        {consulting && (
          <>
            <Text size="sm" c="dimmed" ml="xs">
              Overleg met {props.consultWith}
            </Text>
            <Button color="teal" leftSection={<IconCheck size={18} />} onClick={props.onCompleteWarmTransfer}>
              Overleg voltooien
            </Button>
            <Button
              variant="light"
              color="gray"
              leftSection={<IconX size={18} />}
              onClick={props.onCancelWarmTransfer}
            >
              Annuleren
            </Button>
          </>
        )}
        {props.status === 'WrapUp' && (
          <Button color="orange" onClick={props.onFinishWrapUp}>
            Klaar
          </Button>
        )}
      </Group>

      <Group gap="md" wrap="nowrap">
        <Menu position="bottom-end" withinPortal>
          <Menu.Target>
            <Button color={view.color} variant="filled" size="md" rightSection={<IconChevronDown size={16} />}>
              {view.label}
            </Button>
          </Menu.Target>
          <Menu.Dropdown>
            <Menu.Label>Mijn status</Menu.Label>
            {(Object.keys(PRESENCE_LABEL) as Presence[]).map((p) => (
              <Menu.Item key={p} onClick={() => props.onSetPresence(p)}>
                {PRESENCE_LABEL[p]}
              </Menu.Item>
            ))}
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
