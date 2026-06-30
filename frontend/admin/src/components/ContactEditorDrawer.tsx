import { useState } from 'react';
import { Button, Divider, Drawer, Group, Loader, Stack, TextInput } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { adminApi, type Contact, type ContactWriteRequest } from '../api/adminApi';

interface Props {
  target: Contact | 'new' | null;
  onClose: () => void;
  onSaved: () => void;
}

interface FormState {
  name: string;
  number: string;
  department: string;
}

export function ContactEditorDrawer({ target, onClose, onSaved }: Props) {
  const [form, setForm] = useState<FormState | null>(null);
  const [saving, setSaving] = useState(false);
  const isNew = target === 'new';

  // Formulier (re)initialiseren zodra een ander target geopend wordt — tijdens render op basis van
  // het vorige target i.p.v. via een effect (voorkomt cascading renders).
  const [prevTarget, setPrevTarget] = useState(target);
  if (target !== prevTarget) {
    setPrevTarget(target);
    if (target !== null) {
      setForm(
        target === 'new'
          ? { name: '', number: '', department: '' }
          : { name: target.name, number: target.number, department: target.department ?? '' },
      );
    }
  }

  const patch = (p: Partial<FormState>) => setForm((f) => (f ? { ...f, ...p } : f));

  const save = async () => {
    if (!form) return;
    setSaving(true);
    const body: ContactWriteRequest = {
      name: form.name.trim(),
      number: form.number.trim(),
      department: form.department.trim() || null,
    };
    try {
      if (isNew) await adminApi.createContact(body);
      else await adminApi.updateContact((target as Contact).id, body);
      notifications.show({ color: 'green', message: 'Contact opgeslagen.' });
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
      size="md"
      title={isNew ? 'Nieuw contact' : `Contact bewerken: ${form?.name ?? ''}`}
    >
      {!form ? (
        <Group justify="center" p="xl">
          <Loader />
        </Group>
      ) : (
        <Stack>
          <TextInput
            label="Naam"
            withAsterisk
            value={form.name}
            onChange={(e) => patch({ name: e.currentTarget.value })}
          />
          <TextInput
            label="Nummer"
            description="E.164, bv. +31201234500"
            withAsterisk
            placeholder="+31..."
            value={form.number}
            onChange={(e) => patch({ number: e.currentTarget.value })}
          />
          <TextInput
            label="Afdeling (optioneel)"
            value={form.department}
            onChange={(e) => patch({ department: e.currentTarget.value })}
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
