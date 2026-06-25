import { useEffect, useState } from 'react';
import {
  ActionIcon,
  Button,
  Divider,
  Drawer,
  Group,
  Loader,
  Select,
  Stack,
  Switch,
  Text,
  TextInput,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { IconPlus, IconTrash } from '@tabler/icons-react';
import {
  adminApi,
  type DayOfWeek,
  type QueueDetail,
  type QueueWriteRequest,
} from '../api/adminApi';

interface Props {
  target: QueueDetail | 'new' | null;
  onClose: () => void;
  onSaved: () => void;
}

interface Window {
  id: number;
  day: DayOfWeek;
  opens: string; // "HH:mm" voor de native time-input
  closes: string;
}

interface FormState {
  name: string;
  displayName: string;
  welcomePrompt: string;
  closedPrompt: string;
  timeZone: string;
  adHocClosed: boolean;
  adHocForwardNumber: string;
  windows: Window[];
  numbers: string[];
}

const DAYS: { value: DayOfWeek; label: string }[] = [
  { value: 'Monday', label: 'Maandag' },
  { value: 'Tuesday', label: 'Dinsdag' },
  { value: 'Wednesday', label: 'Woensdag' },
  { value: 'Thursday', label: 'Donderdag' },
  { value: 'Friday', label: 'Vrijdag' },
  { value: 'Saturday', label: 'Zaterdag' },
  { value: 'Sunday', label: 'Zondag' },
];

const TIMEZONES = ['Europe/Amsterdam', 'Europe/Brussels', 'Europe/London', 'UTC', 'America/New_York'];

let seq = 0;
const nextId = () => ++seq;

const toInputTime = (s: string) => s.slice(0, 5); // "09:00:00" → "09:00"
const toApiTime = (s: string) => (s.length === 5 ? `${s}:00` : s); // "09:00" → "09:00:00"

function defaultForm(): FormState {
  const workdays: DayOfWeek[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'];
  return {
    name: '',
    displayName: '',
    welcomePrompt: 'sound:queue-thankyou',
    closedPrompt: 'sound:vm-goodbye',
    timeZone: 'Europe/Amsterdam',
    adHocClosed: false,
    adHocForwardNumber: '',
    windows: workdays.map((day) => ({ id: nextId(), day, opens: '09:00', closes: '17:00' })),
    numbers: [],
  };
}

function fromDetail(q: QueueDetail): FormState {
  return {
    name: q.name,
    displayName: q.displayName,
    welcomePrompt: q.welcomePrompt,
    closedPrompt: q.closedPrompt,
    timeZone: q.timeZone,
    adHocClosed: q.adHocClosed,
    adHocForwardNumber: q.adHocForwardNumber ?? '',
    windows: q.openingHours.map((w) => ({
      id: nextId(),
      day: w.day,
      opens: toInputTime(w.opens),
      closes: toInputTime(w.closes),
    })),
    numbers: [...q.numbers],
  };
}

export function QueueEditorDrawer({ target, onClose, onSaved }: Props) {
  const [form, setForm] = useState<FormState | null>(null);
  const [saving, setSaving] = useState(false);
  const isNew = target === 'new';

  useEffect(() => {
    if (target === null) return;
    setForm(target === 'new' ? defaultForm() : fromDetail(target));
  }, [target]);

  const patch = (p: Partial<FormState>) => setForm((f) => (f ? { ...f, ...p } : f));

  const addWindow = (day: DayOfWeek) =>
    setForm((f) => (f ? { ...f, windows: [...f.windows, { id: nextId(), day, opens: '09:00', closes: '17:00' }] } : f));
  const updateWindow = (id: number, p: Partial<Window>) =>
    setForm((f) => (f ? { ...f, windows: f.windows.map((w) => (w.id === id ? { ...w, ...p } : w)) } : f));
  const removeWindow = (id: number) =>
    setForm((f) => (f ? { ...f, windows: f.windows.filter((w) => w.id !== id) } : f));

  const setNumber = (i: number, value: string) =>
    setForm((f) => (f ? { ...f, numbers: f.numbers.map((n, j) => (j === i ? value : n)) } : f));
  const addNumber = () => setForm((f) => (f ? { ...f, numbers: [...f.numbers, ''] } : f));
  const removeNumber = (i: number) =>
    setForm((f) => (f ? { ...f, numbers: f.numbers.filter((_, j) => j !== i) } : f));

  const save = async () => {
    if (!form) return;
    setSaving(true);
    const body: QueueWriteRequest = {
      name: form.name.trim(),
      displayName: form.displayName.trim(),
      welcomePrompt: form.welcomePrompt.trim(),
      closedPrompt: form.closedPrompt.trim(),
      adHocClosed: form.adHocClosed,
      adHocForwardNumber: form.adHocForwardNumber.trim() || null,
      timeZone: form.timeZone,
      openingHours: form.windows.map((w) => ({ day: w.day, opens: toApiTime(w.opens), closes: toApiTime(w.closes) })),
      numbers: form.numbers.map((n) => n.trim()).filter(Boolean),
    };
    try {
      if (isNew) await adminApi.createQueue(body);
      else await adminApi.updateQueue((target as QueueDetail).id, body);
      notifications.show({ color: 'green', message: 'Wachtrij opgeslagen.' });
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

  const tzData = form && !TIMEZONES.includes(form.timeZone) ? [form.timeZone, ...TIMEZONES] : TIMEZONES;

  return (
    <Drawer
      opened={target !== null}
      onClose={onClose}
      position="right"
      size="xl"
      title={isNew ? 'Nieuwe wachtrij' : `Wachtrij bewerken: ${form?.displayName ?? ''}`}
    >
      {!form ? (
        <Group justify="center" p="xl">
          <Loader />
        </Group>
      ) : (
        <Stack>
          <TextInput
            label="Technische naam"
            description="Alleen kleine letters en cijfers; vaste sleutel van de wachtrij."
            placeholder="bijv. sales"
            value={form.name}
            disabled={!isNew}
            onChange={(e) => patch({ name: e.currentTarget.value })}
          />
          <TextInput
            label="Weergavenaam"
            withAsterisk
            value={form.displayName}
            onChange={(e) => patch({ displayName: e.currentTarget.value })}
          />
          <Group grow>
            <TextInput
              label="Welkomstprompt"
              description="Asterisk media-URI"
              value={form.welcomePrompt}
              onChange={(e) => patch({ welcomePrompt: e.currentTarget.value })}
            />
            <TextInput
              label="Gesloten-prompt"
              description="Asterisk media-URI"
              value={form.closedPrompt}
              onChange={(e) => patch({ closedPrompt: e.currentTarget.value })}
            />
          </Group>
          <Select
            label="Tijdzone"
            description="IANA-tijdzone voor de openingstijden"
            data={tzData}
            value={form.timeZone}
            searchable
            onChange={(v) => v && patch({ timeZone: v })}
          />

          <Divider label="Ad-hoc sluiting" labelPosition="left" mt="sm" />
          <Switch
            label="Handmatig gesloten (gaat vóór de openingstijden)"
            checked={form.adHocClosed}
            onChange={(e) => patch({ adHocClosed: e.currentTarget.checked })}
          />
          {form.adHocClosed && (
            <TextInput
              label="Doorschakelnummer (optioneel)"
              description="Leeg laten = de gesloten-prompt afspelen; anders wordt hierheen doorgeschakeld."
              placeholder="+31..."
              value={form.adHocForwardNumber}
              onChange={(e) => patch({ adHocForwardNumber: e.currentTarget.value })}
            />
          )}

          <Divider label="Openingstijden" labelPosition="left" mt="sm" />
          <Stack gap="xs">
            {DAYS.map((d) => {
              const dayWindows = form.windows.filter((w) => w.day === d.value);
              return (
                <Group key={d.value} align="flex-start" wrap="nowrap">
                  <Text w={90} pt={6} size="sm">
                    {d.label}
                  </Text>
                  <Stack gap={4} style={{ flex: 1 }}>
                    {dayWindows.length === 0 && (
                      <Text c="dimmed" size="sm" pt={6}>
                        Gesloten
                      </Text>
                    )}
                    {dayWindows.map((w) => (
                      <Group key={w.id} gap="xs" wrap="nowrap">
                        <TextInput
                          type="time"
                          value={w.opens}
                          onChange={(e) => updateWindow(w.id, { opens: e.currentTarget.value })}
                          w={120}
                        />
                        <Text size="sm">–</Text>
                        <TextInput
                          type="time"
                          value={w.closes}
                          onChange={(e) => updateWindow(w.id, { closes: e.currentTarget.value })}
                          w={120}
                        />
                        <ActionIcon variant="subtle" color="red" aria-label="Venster verwijderen" onClick={() => removeWindow(w.id)}>
                          <IconTrash size={16} />
                        </ActionIcon>
                      </Group>
                    ))}
                  </Stack>
                  <Button variant="subtle" size="compact-sm" leftSection={<IconPlus size={14} />} onClick={() => addWindow(d.value)}>
                    Venster
                  </Button>
                </Group>
              );
            })}
          </Stack>

          <Divider label="Inkomende nummers" labelPosition="left" mt="sm" />
          <Stack gap="xs">
            {form.numbers.length === 0 && (
              <Text c="dimmed" size="sm">
                Nog geen nummers gekoppeld.
              </Text>
            )}
            {form.numbers.map((n, i) => (
              <Group key={i} gap="xs" wrap="nowrap">
                <TextInput
                  placeholder="+31..."
                  value={n}
                  onChange={(e) => setNumber(i, e.currentTarget.value)}
                  style={{ flex: 1 }}
                />
                <ActionIcon variant="subtle" color="red" aria-label="Nummer verwijderen" onClick={() => removeNumber(i)}>
                  <IconTrash size={16} />
                </ActionIcon>
              </Group>
            ))}
            <Group>
              <Button variant="light" size="compact-sm" leftSection={<IconPlus size={14} />} onClick={addNumber}>
                Nummer toevoegen
              </Button>
            </Group>
          </Stack>

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
