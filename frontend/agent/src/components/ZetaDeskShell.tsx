import { AppShell, Container, Stack } from '@mantine/core';
import type { AgentStatus, Presence } from '../api/agentApi';
import type { CallState } from '../softphone/useSoftphone';
import type { WaitingCall } from '../realtime/useContactCenterHub';
import { TopBar } from './TopBar';
import { QueuePanel } from './QueuePanel';

interface Props {
  agentName: string;
  status: AgentStatus;
  presence: Presence;
  callState: CallState;
  onHold: boolean;
  waiting: WaitingCall[];
  canPickup: boolean;
  onAnswer: () => void;
  onHangup: () => void;
  onToggleHold: () => void;
  onFinishWrapUp: () => void;
  onSetPresence: (presence: Presence) => void;
  onPickup: (callId: string) => void;
  onLogout: () => void;
}

export function ZetaDeskShell(props: Props) {
  return (
    <AppShell header={{ height: 64 }} padding="md">
      <AppShell.Header>
        <TopBar
          agentName={props.agentName}
          status={props.status}
          presence={props.presence}
          callState={props.callState}
          onHold={props.onHold}
          onAnswer={props.onAnswer}
          onHangup={props.onHangup}
          onToggleHold={props.onToggleHold}
          onFinishWrapUp={props.onFinishWrapUp}
          onSetPresence={props.onSetPresence}
          onLogout={props.onLogout}
        />
      </AppShell.Header>
      <AppShell.Main>
        <Container size="md" px={0}>
          <Stack>
            <QueuePanel waiting={props.waiting} canPickup={props.canPickup} onPickup={props.onPickup} />
            {/* Doorverbind-zoekpaneel komt in fase Z3 */}
          </Stack>
        </Container>
      </AppShell.Main>
    </AppShell>
  );
}
