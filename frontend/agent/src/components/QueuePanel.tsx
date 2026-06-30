import { useEffect, useState } from 'react';
import { Badge, Button, Card, Group, Stack, Text, Title } from '@mantine/core';
import { IconPhoneIncoming } from '@tabler/icons-react';
import type { WaitingCall } from '../realtime/useContactCenterHub';

function waitLabel(enqueuedAt: string, now: number): string {
  const secs = Math.max(0, Math.floor((now - new Date(enqueuedAt).getTime()) / 1000));
  const m = Math.floor(secs / 60);
  const s = secs % 60;
  return m > 0 ? `${m}m ${s}s` : `${s}s`;
}

function groupByQueue(waiting: WaitingCall[]): Map<string, WaitingCall[]> {
  const map = new Map<string, WaitingCall[]>();
  for (const call of waiting) {
    const list = map.get(call.queueName);
    if (list) list.push(call);
    else map.set(call.queueName, [call]);
  }
  return map;
}

interface Props {
  waiting: WaitingCall[];
  canPickup: boolean;
  onPickup?: (callId: string) => void;
}

export function QueuePanel({ waiting, canPickup, onPickup }: Props) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, []);

  const byQueue = groupByQueue(waiting);

  return (
    <Card withBorder radius="md" padding="md">
      <Stack gap="sm">
        <Title order={4}>Wachtrijen</Title>
        {waiting.length === 0 && (
          <Text c="dimmed" size="sm">
            Geen wachtende gesprekken.
          </Text>
        )}
        {[...byQueue.entries()].map(([queue, calls]) => (
          <Stack key={queue} gap="xs">
            <Group gap="xs">
              <Text fw={500}>{queue}</Text>
              <Badge variant="light">{calls.length}</Badge>
            </Group>
            {calls.map((call) => (
              <Group
                key={call.callId}
                justify="space-between"
                wrap="nowrap"
                style={{
                  border: '1px solid var(--mantine-color-default-border)',
                  borderRadius: 'var(--mantine-radius-md)',
                  padding: '8px 12px',
                }}
              >
                <Group gap="sm">
                  <IconPhoneIncoming size={18} />
                  <Text>{call.callerNumber || 'onbekend'}</Text>
                  <Text c="dimmed" size="sm">
                    wacht {waitLabel(call.enqueuedAt, now)}
                  </Text>
                </Group>
                <Button size="compact-sm" disabled={!canPickup} onClick={() => onPickup?.(call.callId)}>
                  Aannemen
                </Button>
              </Group>
            ))}
          </Stack>
        ))}
      </Stack>
    </Card>
  );
}
