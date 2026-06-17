import { useState } from 'react';
import { Button, Group, TextInput } from '@mantine/core';
import { IconArrowForwardUp } from '@tabler/icons-react';

interface Props {
  disabled: boolean;
  onTransfer: (target: string) => void;
}

export function TransferControls({ disabled, onTransfer }: Props) {
  const [target, setTarget] = useState('');

  return (
    <Group align="flex-end" gap="sm">
      <TextInput
        label="Doorverbinden naar"
        placeholder="wachtrij of nummer"
        value={target}
        onChange={(e) => setTarget(e.currentTarget.value)}
        disabled={disabled}
        style={{ flex: 1 }}
      />
      <Button
        variant="light"
        leftSection={<IconArrowForwardUp size={18} />}
        disabled={disabled || target.trim().length === 0}
        onClick={() => onTransfer(target.trim())}
      >
        Koud doorverbinden
      </Button>
    </Group>
  );
}
