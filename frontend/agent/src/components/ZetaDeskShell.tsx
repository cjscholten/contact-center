import { AppShell, Container, Stack } from '@mantine/core';
import type { AgentStatus, DirectoryEntry, Presence } from '../api/agentApi';
import type { CallState } from '../softphone/useSoftphone';
import type { WaitingCall } from '../realtime/useContactCenterHub';
import { TopBar } from './TopBar';
import { QueuePanel } from './QueuePanel';
import { TransferSearchPanel } from './TransferSearchPanel';

interface Props {
  agentName: string;
  status: AgentStatus;
  presence: Presence;
  callState: CallState;
  onHold: boolean;
  consultWith: string | null;
  waiting: WaitingCall[];
  canPickup: boolean;
  onAnswer: () => void;
  onHangup: () => void;
  onToggleHold: () => void;
  onFinishWrapUp: () => void;
  onSetPresence: (presence: Presence) => void;
  onPickup: (callId: string) => void;
  onSearch: (query: string) => Promise<DirectoryEntry[]>;
  onTransfer: (entry: DirectoryEntry) => void;
  onWarmTransfer: (entry: DirectoryEntry) => void;
  onCompleteWarmTransfer: () => void;
  onCancelWarmTransfer: () => void;
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
          consultWith={props.consultWith}
          onAnswer={props.onAnswer}
          onHangup={props.onHangup}
          onToggleHold={props.onToggleHold}
          onFinishWrapUp={props.onFinishWrapUp}
          onSetPresence={props.onSetPresence}
          onCompleteWarmTransfer={props.onCompleteWarmTransfer}
          onCancelWarmTransfer={props.onCancelWarmTransfer}
          onLogout={props.onLogout}
        />
      </AppShell.Header>
      <AppShell.Main>
        <Container size="md" px={0}>
          <Stack>
            <QueuePanel waiting={props.waiting} canPickup={props.canPickup} onPickup={props.onPickup} />
            <TransferSearchPanel
              canTransfer={props.callState === 'in_call'}
              consulting={props.consultWith !== null}
              onSearch={props.onSearch}
              onTransfer={props.onTransfer}
              onWarmTransfer={props.onWarmTransfer}
            />
          </Stack>
        </Container>
      </AppShell.Main>
    </AppShell>
  );
}
