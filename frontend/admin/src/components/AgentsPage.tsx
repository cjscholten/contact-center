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
import { adminApi, type AgentDetail, type AgentListItem } from '../api/adminApi';
import { AgentEditorDrawer } from './AgentEditorDrawer';

function fail(title: string, e: unknown): void {
  notifications.show({ color: 'red', title, message: e instanceof Error ? e.message : String(e) });
}

export function AgentsPage() {
  const [agents, setAgents] = useState<AgentListItem[] | null>(null);
  const [editing, setEditing] = useState<AgentDetail | 'new' | null>(null);

  const reload = useCallback(async () => {
    try {
      setAgents(await adminApi.listAgents());
    } catch (e) {
      fail('Agents laden mislukt', e);
      setAgents([]);
    }
  }, []);

  useEffect(() => {
    void (async () => {
      await reload();
    })();
  }, [reload]);

  const openEdit = async (id: number) => {
    try {
      setEditing(await adminApi.getAgent(id));
    } catch (e) {
      fail('Agent laden mislukt', e);
    }
  };

  const remove = async (a: AgentListItem) => {
    if (!window.confirm(`Agent '${a.displayName}' verwijderen?`)) return;
    try {
      await adminApi.deleteAgent(a.id);
      notifications.show({ color: 'green', message: `Agent '${a.displayName}' verwijderd.` });
      void reload();
    } catch (e) {
      fail('Verwijderen mislukt', e);
    }
  };

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>Agents</Title>
        <Button leftSection={<IconPlus size={18} />} onClick={() => setEditing('new')}>
          Nieuwe agent
        </Button>
      </Group>

      <Card withBorder radius="md" padding="0">
        {agents === null ? (
          <Group justify="center" p="xl">
            <Loader />
          </Group>
        ) : agents.length === 0 ? (
          <Text c="dimmed" p="md">
            Nog geen agents. Maak er een aan met "Nieuwe agent".
          </Text>
        ) : (
          <Table highlightOnHover verticalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Naam</Table.Th>
                <Table.Th>Endpoint</Table.Th>
                <Table.Th>Wachtrijen</Table.Th>
                <Table.Th w={100}>Acties</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {agents.map((a) => (
                <Table.Tr key={a.id} style={{ cursor: 'pointer' }} onClick={() => void openEdit(a.id)}>
                  <Table.Td>
                    <Text fw={500}>{a.displayName}</Text>
                    <Text c="dimmed" size="xs">
                      {a.name}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">{a.endpoint}</Text>
                  </Table.Td>
                  <Table.Td>
                    {a.queues.length === 0 ? (
                      <Text c="dimmed" size="sm">
                        —
                      </Text>
                    ) : (
                      <Group gap={4}>
                        {a.queues.map((q) => (
                          <Badge key={q} variant="light">
                            {q}
                          </Badge>
                        ))}
                      </Group>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <ActionIcon
                      variant="subtle"
                      color="red"
                      aria-label="Verwijderen"
                      onClick={(e) => {
                        e.stopPropagation();
                        void remove(a);
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

      <AgentEditorDrawer
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
