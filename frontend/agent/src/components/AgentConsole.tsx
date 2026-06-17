import { Alert, Button, Card, Divider, Group, Stack, Text, Title } from '@mantine/core';
import { IconBellRinging, IconLogout } from '@tabler/icons-react';
import type { AgentStatus } from '../api/agentApi';
import type { CallState } from '../softphone/useSoftphone';
import { AgentStatusBadge } from './AgentStatusBadge';
import { CallControls } from './CallControls';
import { TransferControls } from './TransferControls';

interface Props {
  agentName: string;
  status: AgentStatus;
  callState: CallState;
  onHold: boolean;
  onAnswer: () => void;
  onHangup: () => void;
  onToggleHold: () => void;
  onTransfer: (target: string) => void;
  onFinishWrapUp: () => void;
  onLogout: () => void;
}

function callLabel(callState: CallState, onHold: boolean): string {
  if (callState === 'ringing') return 'Inkomend gesprek…';
  if (callState === 'in_call') return onHold ? 'In de wacht' : 'In gesprek';
  return 'Geen actief gesprek';
}

export function AgentConsole(props: Props) {
  const { agentName, status, callState, onHold } = props;

  return (
    <Card withBorder shadow="sm" radius="md" w={460} padding="lg">
      <Stack>
        <Group justify="space-between">
          <Group gap="sm">
            <Title order={4}>{agentName}</Title>
            <AgentStatusBadge status={status} />
          </Group>
          <Button
            variant="subtle"
            color="gray"
            size="compact-sm"
            leftSection={<IconLogout size={16} />}
            onClick={props.onLogout}
          >
            Afmelden
          </Button>
        </Group>

        <Divider />

        {status === 'WrapUp' && (
          <Alert color="orange" icon={<IconBellRinging size={18} />} title="Nawerktijd">
            <Group justify="space-between" align="center">
              <Text size="sm">Rond je administratie af.</Text>
              <Button size="compact-sm" color="orange" onClick={props.onFinishWrapUp}>
                Klaar
              </Button>
            </Group>
          </Alert>
        )}

        <Text c="dimmed" size="sm">
          {callLabel(callState, onHold)}
        </Text>

        <CallControls
          callState={callState}
          onHold={onHold}
          onAnswer={props.onAnswer}
          onHangup={props.onHangup}
          onToggleHold={props.onToggleHold}
        />

        <Divider label="Doorverbinden" labelPosition="center" />

        <TransferControls disabled={callState !== 'in_call'} onTransfer={props.onTransfer} />
      </Stack>
    </Card>
  );
}
