import { Button, Group } from '@mantine/core';
import { IconPhone, IconPhoneOff, IconPlayerPause, IconPlayerPlay } from '@tabler/icons-react';
import type { CallState } from '../softphone/useSoftphone';

interface Props {
  callState: CallState;
  onHold: boolean;
  onAnswer: () => void;
  onHangup: () => void;
  onToggleHold: () => void;
}

export function CallControls({ callState, onHold, onAnswer, onHangup, onToggleHold }: Props) {
  return (
    <Group>
      <Button
        color="green"
        leftSection={<IconPhone size={18} />}
        disabled={callState !== 'ringing'}
        onClick={onAnswer}
      >
        Aannemen
      </Button>
      <Button
        color="red"
        leftSection={<IconPhoneOff size={18} />}
        disabled={callState === 'idle'}
        onClick={onHangup}
      >
        Ophangen
      </Button>
      <Button
        variant="default"
        leftSection={onHold ? <IconPlayerPlay size={18} /> : <IconPlayerPause size={18} />}
        disabled={callState !== 'in_call'}
        onClick={onToggleHold}
      >
        {onHold ? 'Uit de wacht' : 'In de wacht'}
      </Button>
    </Group>
  );
}
