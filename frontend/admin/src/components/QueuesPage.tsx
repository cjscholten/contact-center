import { useCallback, useEffect, useState } from 'react';
import {
  ActionIcon,
  Badge,
  Button,
  Card,
  Group,
  Loader,
  Stack,
  Table,
  Text,
  Title,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { IconPlus, IconTrash } from '@tabler/icons-react';
import { adminApi, type QueueDetail, type QueueListItem } from '../api/adminApi';
import { QueueEditorDrawer } from './QueueEditorDrawer';

function fail(title: string, e: unknown): void {
  notifications.show({ color: 'red', title, message: e instanceof Error ? e.message : String(e) });
}

function statusBadge(q: QueueListItem) {
  if (q.adHocClosed) return <Badge color="red">Handmatig gesloten</Badge>;
  return q.openNow ? <Badge color="green">Open</Badge> : <Badge color="gray">Buiten openingstijd</Badge>;
}

export function QueuesPage() {
  const [queues, setQueues] = useState<QueueListItem[] | null>(null);
  const [editing, setEditing] = useState<QueueDetail | 'new' | null>(null);

  const reload = useCallback(async () => {
    try {
      setQueues(await adminApi.listQueues());
    } catch (e) {
      fail('Wachtrijen laden mislukt', e);
      setQueues([]);
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  const openEdit = async (id: number) => {
    try {
      setEditing(await adminApi.getQueue(id));
    } catch (e) {
      fail('Wachtrij laden mislukt', e);
    }
  };

  const remove = async (q: QueueListItem) => {
    if (!window.confirm(`Wachtrij '${q.displayName}' verwijderen? Dit kan niet ongedaan worden gemaakt.`)) return;
    try {
      await adminApi.deleteQueue(q.id);
      notifications.show({ color: 'green', message: `Wachtrij '${q.displayName}' verwijderd.` });
      void reload();
    } catch (e) {
      fail('Verwijderen mislukt', e);
    }
  };

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>Wachtrijen</Title>
        <Button leftSection={<IconPlus size={18} />} onClick={() => setEditing('new')}>
          Nieuwe wachtrij
        </Button>
      </Group>

      <Card withBorder radius="md" padding="0">
        {queues === null ? (
          <Group justify="center" p="xl">
            <Loader />
          </Group>
        ) : queues.length === 0 ? (
          <Text c="dimmed" p="md">
            Nog geen wachtrijen. Maak er een aan met "Nieuwe wachtrij".
          </Text>
        ) : (
          <Table highlightOnHover verticalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Naam</Table.Th>
                <Table.Th>Status</Table.Th>
                <Table.Th>Nummers</Table.Th>
                <Table.Th w={100}>Acties</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {queues.map((q) => (
                <Table.Tr key={q.id} style={{ cursor: 'pointer' }} onClick={() => void openEdit(q.id)}>
                  <Table.Td>
                    <Text fw={500}>{q.displayName}</Text>
                    <Text c="dimmed" size="xs">
                      {q.name}
                    </Text>
                  </Table.Td>
                  <Table.Td>{statusBadge(q)}</Table.Td>
                  <Table.Td>{q.numberCount}</Table.Td>
                  <Table.Td>
                    <ActionIcon
                      variant="subtle"
                      color="red"
                      aria-label="Verwijderen"
                      onClick={(e) => {
                        e.stopPropagation();
                        void remove(q);
                      }}
                    >
                      <IconTrash size={18} />
                    </ActionIcon>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>

      <QueueEditorDrawer
        target={editing}
        onClose={() => setEditing(null)}
        onSaved={() => {
          setEditing(null);
          void reload();
        }}
      />
    </Stack>
  );
}
