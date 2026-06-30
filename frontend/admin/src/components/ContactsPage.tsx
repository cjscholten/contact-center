import { useCallback, useEffect, useState } from 'react';
import {
  ActionIcon,
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
import { adminApi, type Contact } from '../api/adminApi';
import { ContactEditorDrawer } from './ContactEditorDrawer';

function fail(title: string, e: unknown): void {
  notifications.show({ color: 'red', title, message: e instanceof Error ? e.message : String(e) });
}

export function ContactsPage() {
  const [contacts, setContacts] = useState<Contact[] | null>(null);
  const [editing, setEditing] = useState<Contact | 'new' | null>(null);

  const reload = useCallback(async () => {
    try {
      setContacts(await adminApi.listContacts());
    } catch (e) {
      fail('Contacten laden mislukt', e);
      setContacts([]);
    }
  }, []);

  useEffect(() => {
    void (async () => {
      await reload();
    })();
  }, [reload]);

  const openEdit = async (id: number) => {
    try {
      setEditing(await adminApi.getContact(id));
    } catch (e) {
      fail('Contact laden mislukt', e);
    }
  };

  const remove = async (c: Contact) => {
    if (!window.confirm(`Contact '${c.name}' verwijderen?`)) return;
    try {
      await adminApi.deleteContact(c.id);
      notifications.show({ color: 'green', message: `Contact '${c.name}' verwijderd.` });
      void reload();
    } catch (e) {
      fail('Verwijderen mislukt', e);
    }
  };

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>Contacten</Title>
        <Button leftSection={<IconPlus size={18} />} onClick={() => setEditing('new')}>
          Nieuw contact
        </Button>
      </Group>

      <Card withBorder radius="md" padding="0">
        {contacts === null ? (
          <Group justify="center" p="xl">
            <Loader />
          </Group>
        ) : contacts.length === 0 ? (
          <Text c="dimmed" p="md">
            Nog geen contacten. Maak er een aan met "Nieuw contact".
          </Text>
        ) : (
          <Table highlightOnHover verticalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Naam</Table.Th>
                <Table.Th>Nummer</Table.Th>
                <Table.Th>Afdeling</Table.Th>
                <Table.Th w={80}>Acties</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {contacts.map((c) => (
                <Table.Tr key={c.id} style={{ cursor: 'pointer' }} onClick={() => void openEdit(c.id)}>
                  <Table.Td>
                    <Text fw={500}>{c.name}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">{c.number}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" c={c.department ? undefined : 'dimmed'}>
                      {c.department ?? '—'}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <ActionIcon
                      variant="subtle"
                      color="red"
                      aria-label="Verwijderen"
                      onClick={(e) => {
                        e.stopPropagation();
                        void remove(c);
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

      <ContactEditorDrawer
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
