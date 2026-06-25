import { useEffect, useState } from 'react';
import {
  Button,
  Divider,
  Drawer,
  Group,
  Loader,
  MultiSelect,
  Stack,
  TextInput,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { adminApi, type AgentDetail, type AgentWriteRequest, type QueueListItem } from '../api/adminApi';

interface Props {
  target: AgentDetail | 'new' | null;
  onClose: () => void;
  onSaved: () => void;
}

interface FormState {
  name: string;
  displayName: string;
  endpoint: string;
  queueIds: string[]; // MultiSelect werkt met string-values
}

export function AgentEditorDrawer({ target, onClose, onSaved }: Props) {
  const [form, setForm] = useState<FormState | null>(null);
  const [queues, setQueues] = useState<QueueListItem[]>([]);
  const [saving, setSaving] = useState(false);
  const [endpointDirty, setEndpointDirty] = useState(false);
  const isNew = target === 'new';

  useEffect(() => {
    if (target === null) return;
    const creating = target === 'new';
    setEndpointDirty(!creating); // bij bewerken endpoint niet automatisch overschrijven
    setForm(
      creating
        ? { name: '', displayName: '', endpoint: '', queueIds: [] }
        : {
            name: target.name,
            displayName: target.displayName,
            endpoint: target.endpoint,
            queueIds: target.queueIds.map(String),
          },
    );
    adminApi.listQueues().then(setQueues).catch(() => setQueues([]));
  }, [target]);

  const patch = (p: Partial<FormState>) => setForm((f) => (f ? { ...f, ...p } : f));

  // Op 'nieuw' volgt de endpoint automatisch de naam tot de gebruiker hem zelf aanpast.
  const onNameChange = (name: string) =>
    setForm((f) => (f ? { ...f, name, endpoint: endpointDirty ? f.endpoint : name ? `PJSIP/${name}` : '' } : f));

  const save = async () => {
    if (!form) return;
    setSaving(true);
    const body: AgentWriteRequest = {
      name: form.name.trim(),
      displayName: form.displayName.trim(),
      endpoint: form.endpoint.trim(),
      queueIds: form.queueIds.map(Number),
    };
    try {
      if (isNew) await adminApi.createAgent(body);
      else await adminApi.updateAgent((target as AgentDetail).id, body);
      notifications.show({ color: 'green', message: 'Agent opgeslagen.' });
      onSaved();
    } catch (e) {
      notifications.show({
        color: 'red',
        title: 'Opslaan mislukt',
        message: e instanceof Error ? e.message : String(e),
      });
    } finally {
      setSaving(false);
    }
  };

  return (
    <Drawer
      opened={target !== null}
      onClose={onClose}
      position="right"
      size="lg"
      title={isNew ? 'Nieuwe agent' : `Agent bewerken: ${form?.displayName ?? ''}`}
    >
      {!form ? (
        <Group justify="center" p="xl">
          <Loader />
        </Group>
      ) : (
        <Stack>
          <TextInput
            label="Naam"
            description="Loginnaam = SIP-gebruikersnaam; moet overeenkomen met pjsip.conf zolang er geen IdP is."
            placeholder="bijv. agent1003"
            value={form.name}
            disabled={!isNew}
            onChange={(e) => onNameChange(e.currentTarget.value)}
          />
          <TextInput
            label="Weergavenaam"
            withAsterisk
            value={form.displayName}
            onChange={(e) => patch({ displayName: e.currentTarget.value })}
          />
          <TextInput
            label="Endpoint"
            description="PJSIP-endpoint, bv. PJSIP/agent1003"
            withAsterisk
            value={form.endpoint}
            onChange={(e) => {
              setEndpointDirty(true);
              patch({ endpoint: e.currentTarget.value });
            }}
          />
          <MultiSelect
            label="Wachtrijen"
            description="In welke wachtrijen werkt deze agent?"
            placeholder={form.queueIds.length === 0 ? 'Kies wachtrijen' : undefined}
            data={queues.map((q) => ({ value: String(q.id), label: q.displayName }))}
            value={form.queueIds}
            onChange={(v) => patch({ queueIds: v })}
            searchable
            clearable
          />

          <Divider mt="sm" />
          <Group justify="flex-end">
            <Button variant="default" onClick={onClose}>
              Annuleren
            </Button>
            <Button onClick={() => void save()} loading={saving}>
              Opslaan
            </Button>
          </Group>
        </Stack>
      )}
    </Drawer>
  );
}
