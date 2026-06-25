import { useEffect, useState } from 'react';
import { Button, Card, Group, Loader, NumberInput, Stack, Title } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { adminApi } from '../api/adminApi';

function fail(title: string, e: unknown): void {
  notifications.show({ color: 'red', title, message: e instanceof Error ? e.message : String(e) });
}

export function SettingsPage() {
  const [wrapUp, setWrapUp] = useState<number | null>(null); // null = aan het laden
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    adminApi
      .getSettings()
      .then((s) => setWrapUp(s.wrapUpSeconds))
      .catch((e) => {
        fail('Instellingen laden mislukt', e);
        setWrapUp(30);
      });
  }, []);

  const save = async () => {
    if (wrapUp === null) return;
    setSaving(true);
    try {
      const s = await adminApi.updateSettings({ wrapUpSeconds: wrapUp });
      setWrapUp(s.wrapUpSeconds);
      notifications.show({ color: 'green', message: 'Instellingen opgeslagen.' });
    } catch (e) {
      fail('Opslaan mislukt', e);
    } finally {
      setSaving(false);
    }
  };

  return (
    <Stack>
      <Title order={2}>Instellingen</Title>
      <Card withBorder radius="md" padding="md" maw={440}>
        {wrapUp === null ? (
          <Group justify="center" p="md">
            <Loader />
          </Group>
        ) : (
          <Stack>
            <NumberInput
              label="Nawerktijd (seconden)"
              description="Tijd na elk gesprek voordat een agent weer beschikbaar is. 0 schakelt nawerktijd uit."
              min={0}
              max={3600}
              value={wrapUp}
              onChange={(v) => setWrapUp(typeof v === 'number' ? v : 0)}
            />
            <Group justify="flex-end">
              <Button onClick={() => void save()} loading={saving}>
                Opslaan
              </Button>
            </Group>
          </Stack>
        )}
      </Card>
    </Stack>
  );
}
