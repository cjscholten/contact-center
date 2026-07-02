import { useEffect, useState } from 'react';
import { Button, Group, Menu, Text, Title } from '@mantine/core';
import {
  IconCheck,
  IconChevronDown,
  IconLogout,
  IconMicrophone,
  IconMicrophoneOff,
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

function formatCountdown(secs: number): string {
  const m = Math.floor(secs / 60);
  const s = secs % 60;
  return `${m}:${s.toString().padStart(2, '0')}`;
}

interface Props {
  agentName: string;
  status: AgentStatus;
  presence: Presence;
  wrapUpEndsAt: string | null;
  callState: CallState;
  onHold: boolean;
  muted: boolean;
  consultWith: string | null;
  onAnswer: () => void;
  onHangup: () => void;
  onToggleHold: () => void;
  onToggleMute: () => void;
  onFinishWrapUp: () => void;
  onSetPresence: (presence: Presence) => void;
  onCompleteWarmTransfer: () => void;
  onCancelWarmTransfer: () => void;
  onLogout: () => void;
}

export function TopBar(props: Props) {
  const inCall = props.callState === 'in_call';
  const consulting = props.consultWith !== null;
  const inWrapUp = props.status === 'WrapUp';
  const view = CALL_VIEW[props.status] ?? PRESENCE_VIEW[props.presence];

  // Tikkende klok; de resterende nawerktijd leiden we tijdens render af (geen setState-in-effect).
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 500);
    return () => clearInterval(id);
  }, []);
  const remaining =
    inWrapUp && props.wrapUpEndsAt
      ? Math.max(0, Math.round((new Date(props.wrapUpEndsAt).getTime() - now) / 1000))
      : null;

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
        <Button
          variant={props.muted ? 'filled' : 'default'}
          color={props.muted ? 'red' : undefined}
          leftSection={props.muted ? <IconMicrophoneOff size={18} /> : <IconMicrophone size={18} />}
          disabled={!inCall}
          onClick={props.onToggleMute}
        >
          {props.muted ? 'Demping opheffen' : 'Microfoon dempen'}
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
      </Group>

      <Group gap="md" wrap="nowrap">
        {inWrapUp && (
          <Group gap="xs" wrap="nowrap">
            <Text size="sm" c="orange" fw={600}>
              Nawerktijd{remaining !== null ? ` ${formatCountdown(remaining)}` : ''}
            </Text>
            <Button color="orange" onClick={props.onFinishWrapUp}>
              Klaar
            </Button>
          </Group>
        )}
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
