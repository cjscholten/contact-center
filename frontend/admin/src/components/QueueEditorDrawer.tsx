import { useState } from 'react';
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
  Textarea,
  TextInput,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { IconCopy, IconPlus, IconTrash, IconVolume } from '@tabler/icons-react';
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
  welcomeText: string;
  closedText: string;
  voice: string;
  timeZone: string;
  adHocClosed: boolean;
  adHocForwardNumber: string;
  windows: Window[];
  numbers: string[];
  musicOnHoldClass: string;
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

const WORKDAYS: DayOfWeek[] = ['Tuesday', 'Wednesday', 'Thursday', 'Friday'];

const TIMEZONES = ['Europe/Amsterdam', 'Europe/Brussels', 'Europe/London', 'UTC', 'America/New_York'];

// Moet overeenkomen met de klassen in musiconhold.conf op de Asterisk-host.
const MOH_CLASSES = ['default', 'office'];

// Moet overeenkomen met de gebundelde Piper-stemmen (infra/backend/Dockerfile).
const VOICES = [
  { value: 'nl_NL-pim-medium', label: 'Pim (Nederlands)' },
  { value: 'nl_NL-ronnie-medium', label: 'Ronnie (Nederlands)' },
  { value: 'nl_NL-alex-medium', label: 'Alex (Nederlands)' },
  { value: 'nl_BE-nathalie-medium', label: 'Nathalie (Vlaams)' },
  { value: 'nl_BE-rdh-medium', label: 'RDH (Vlaams)' },
];

// Validatiepatronen — spiegelen de backend-regels (AdminApi.cs), zodat fouten al vóór het opslaan zichtbaar zijn.
const NAME_RE = /^[a-z0-9]+$/;
const NUMBER_RE = /^\+[0-9]{6,15}$/;
const FORWARD_RE = /^\+?[0-9]{3,15}$/;

interface FormErrors {
  name?: string;
  displayName?: string;
  adHocForwardNumber?: string;
  numbers: Map<number, string>; // index → fout
  windows: Map<number, string>; // venster-id → fout
  days: Map<DayOfWeek, string>; // dag → overlapfout
  any: boolean;
}

function computeErrors(form: FormState, isNew: boolean): FormErrors {
  const numbers = new Map<number, string>();
  const windows = new Map<number, string>();
  const days = new Map<DayOfWeek, string>();

  let name: string | undefined;
  if (isNew) {
    if (!form.name.trim()) name = 'Verplicht.';
    else if (!NAME_RE.test(form.name.trim())) name = 'Alleen kleine letters en cijfers.';
  }

  const displayName = form.displayName.trim() ? undefined : 'Verplicht.';

  let adHocForwardNumber: string | undefined;
  if (form.adHocClosed && form.adHocForwardNumber.trim() && !FORWARD_RE.test(form.adHocForwardNumber.trim()))
    adHocForwardNumber = 'Ongeldig nummer.';

  const seen = new Map<string, number>();
  form.numbers.forEach((n, i) => {
    const t = n.trim();
    if (!t) return;
    if (!NUMBER_RE.test(t)) numbers.set(i, 'Verwacht E.164, bv. +3120…');
    else if (seen.has(t)) numbers.set(i, 'Dubbel nummer.');
    else seen.set(t, i);
  });

  for (const w of form.windows)
    if (w.opens >= w.closes) windows.set(w.id, 'Eindtijd moet ná de begintijd liggen.');

  // Overlap per dag: sorteer de geldige vensters en kijk of het volgende vóór het einde van het vorige begint.
  for (const d of DAYS) {
    const dayWindows = form.windows
      .filter((w) => w.day === d.value && w.opens < w.closes)
      .sort((a, b) => a.opens.localeCompare(b.opens));
    for (let i = 1; i < dayWindows.length; i++) {
      if (dayWindows[i].opens < dayWindows[i - 1].closes) {
        days.set(d.value, 'Vensters overlappen elkaar.');
        break;
      }
    }
  }

  const any =
    !!name || !!displayName || !!adHocForwardNumber || numbers.size > 0 || windows.size > 0 || days.size > 0;
  return { name, displayName, adHocForwardNumber, numbers, windows, days, any };
}

let seq = 0;
const nextId = () => ++seq;

const toInputTime = (s: string) => s.slice(0, 5); // "09:00:00" → "09:00"
const toApiTime = (s: string) => (s.length === 5 ? `${s}:00` : s); // "09:00" → "09:00:00"

function defaultForm(): FormState {
  const workdays: DayOfWeek[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'];
  return {
    name: '',
    displayName: '',
    welcomeText: '',
    closedText: '',
    voice: 'nl_NL-pim-medium',
    timeZone: 'Europe/Amsterdam',
    adHocClosed: false,
    adHocForwardNumber: '',
    windows: workdays.map((day) => ({ id: nextId(), day, opens: '09:00', closes: '17:00' })),
    numbers: [],
    musicOnHoldClass: 'default',
  };
}

function fromDetail(q: QueueDetail): FormState {
  return {
    name: q.name,
    displayName: q.displayName,
    welcomeText: q.welcomeText,
    closedText: q.closedText,
    voice: q.voice,
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
    musicOnHoldClass: q.musicOnHoldClass,
  };
}

export function QueueEditorDrawer({ target, onClose, onSaved }: Props) {
  const [form, setForm] = useState<FormState | null>(null);
  const [saving, setSaving] = useState(false);
  const [previewing, setPreviewing] = useState<'welcome' | 'closed' | null>(null);
  const isNew = target === 'new';

  // Formulier (re)initialiseren zodra een ander target geopend wordt — tijdens render op basis van
  // het vorige target i.p.v. via een effect (voorkomt cascading renders).
  const [prevTarget, setPrevTarget] = useState(target);
  if (target !== prevTarget) {
    setPrevTarget(target);
    if (target !== null) {
      setForm(target === 'new' ? defaultForm() : fromDetail(target));
    }
  }

  const patch = (p: Partial<FormState>) => setForm((f) => (f ? { ...f, ...p } : f));

  const addWindow = (day: DayOfWeek) =>
    setForm((f) => (f ? { ...f, windows: [...f.windows, { id: nextId(), day, opens: '09:00', closes: '17:00' }] } : f));
  const updateWindow = (id: number, p: Partial<Window>) =>
    setForm((f) => (f ? { ...f, windows: f.windows.map((w) => (w.id === id ? { ...w, ...p } : w)) } : f));
  const removeWindow = (id: number) =>
    setForm((f) => (f ? { ...f, windows: f.windows.filter((w) => w.id !== id) } : f));

  // Kopieert de maandag-vensters naar de overige werkdagen (di–vr); handig voor uniforme kantooruren.
  const copyMondayToWorkdays = () =>
    setForm((f) => {
      if (!f) return f;
      const monday = f.windows.filter((w) => w.day === 'Monday');
      const kept = f.windows.filter((w) => !WORKDAYS.includes(w.day));
      const copied = WORKDAYS.flatMap((day) =>
        monday.map((w) => ({ id: nextId(), day, opens: w.opens, closes: w.closes })),
      );
      return { ...f, windows: [...kept, ...copied] };
    });

  const setNumber = (i: number, value: string) =>
    setForm((f) => (f ? { ...f, numbers: f.numbers.map((n, j) => (j === i ? value : n)) } : f));
  const addNumber = () => setForm((f) => (f ? { ...f, numbers: [...f.numbers, ''] } : f));
  const removeNumber = (i: number) =>
    setForm((f) => (f ? { ...f, numbers: f.numbers.filter((_, j) => j !== i) } : f));

  // Beluister de opgegeven tekst met de gekozen stem (zoals de beller het hoort).
  const preview = async (which: 'welcome' | 'closed') => {
    if (!form) return;
    const text = (which === 'welcome' ? form.welcomeText : form.closedText).trim();
    if (!text) return;
    setPreviewing(which);
    try {
      const blob = await adminApi.previewTts(text, form.voice);
      const url = URL.createObjectURL(blob);
      const audio = new Audio(url);
      audio.addEventListener('ended', () => URL.revokeObjectURL(url));
      await audio.play();
    } catch (e) {
      notifications.show({
        color: 'red',
        title: 'Voorbeeld mislukt',
        message: e instanceof Error ? e.message : String(e),
      });
    } finally {
      setPreviewing(null);
    }
  };

  const errors = form ? computeErrors(form, isNew) : null;

  const save = async () => {
    if (!form || (errors?.any ?? true)) return;
    setSaving(true);
    const body: QueueWriteRequest = {
      name: form.name.trim(),
      displayName: form.displayName.trim(),
      welcomeText: form.welcomeText.trim(),
      closedText: form.closedText.trim(),
      voice: form.voice,
      adHocClosed: form.adHocClosed,
      adHocForwardNumber: form.adHocForwardNumber.trim() || null,
      timeZone: form.timeZone,
      openingHours: form.windows.map((w) => ({ day: w.day, opens: toApiTime(w.opens), closes: toApiTime(w.closes) })),
      numbers: form.numbers.map((n) => n.trim()).filter(Boolean),
      musicOnHoldClass: form.musicOnHoldClass,
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
  const voiceData =
    form && !VOICES.some((v) => v.value === form.voice)
      ? [{ value: form.voice, label: form.voice }, ...VOICES]
      : VOICES;
  const hasMonday = !!form && form.windows.some((w) => w.day === 'Monday');

  return (
    <Drawer
      opened={target !== null}
      onClose={onClose}
      position="right"
      size="xl"
      title={isNew ? 'Nieuwe wachtrij' : `Wachtrij bewerken: ${form?.displayName ?? ''}`}
    >
      {!form || !errors ? (
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
            error={errors.name}
            onChange={(e) => patch({ name: e.currentTarget.value })}
          />
          <TextInput
            label="Weergavenaam"
            withAsterisk
            value={form.displayName}
            error={errors.displayName}
            onChange={(e) => patch({ displayName: e.currentTarget.value })}
          />
          <Stack gap={4}>
            <Textarea
              label="Welkomsttekst (gesproken)"
              description="Wordt met TTS naar spraak omgezet. Leeg = standaardprompt."
              autosize
              minRows={2}
              value={form.welcomeText}
              onChange={(e) => patch({ welcomeText: e.currentTarget.value })}
            />
            <Group justify="flex-end">
              <Button
                variant="subtle"
                size="compact-sm"
                leftSection={<IconVolume size={14} />}
                disabled={!form.welcomeText.trim()}
                loading={previewing === 'welcome'}
                onClick={() => void preview('welcome')}
              >
                Beluister
              </Button>
            </Group>
          </Stack>
          <Stack gap={4}>
            <Textarea
              label="Gesloten-tekst (gesproken)"
              description="Afgespeeld buiten openingstijden. Leeg = standaardprompt."
              autosize
              minRows={2}
              value={form.closedText}
              onChange={(e) => patch({ closedText: e.currentTarget.value })}
            />
            <Group justify="flex-end">
              <Button
                variant="subtle"
                size="compact-sm"
                leftSection={<IconVolume size={14} />}
                disabled={!form.closedText.trim()}
                loading={previewing === 'closed'}
                onClick={() => void preview('closed')}
              >
                Beluister
              </Button>
            </Group>
          </Stack>
          <Group grow>
            <Select
              label="Stem"
              description="Piper TTS-stem"
              data={voiceData}
              value={form.voice}
              onChange={(v) => v && patch({ voice: v })}
            />
            <Select
              label="Tijdzone"
              description="IANA-tijdzone voor de openingstijden"
              data={tzData}
              value={form.timeZone}
              searchable
              onChange={(v) => v && patch({ timeZone: v })}
            />
            <Select
              label="Wachtmuziek"
              description="Music-on-hold-klasse"
              data={MOH_CLASSES}
              value={form.musicOnHoldClass}
              onChange={(v) => v && patch({ musicOnHoldClass: v })}
            />
          </Group>

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
              error={errors.adHocForwardNumber}
              onChange={(e) => patch({ adHocForwardNumber: e.currentTarget.value })}
            />
          )}

          <Divider label="Openingstijden" labelPosition="left" mt="sm" />
          <Group>
            <Button
              variant="subtle"
              size="compact-sm"
              leftSection={<IconCopy size={14} />}
              disabled={!hasMonday}
              onClick={copyMondayToWorkdays}
            >
              Maandag kopiëren naar werkdagen
            </Button>
          </Group>
          <Stack gap="xs">
            {DAYS.map((d) => {
              const dayWindows = form.windows.filter((w) => w.day === d.value);
              const dayError = errors.days.get(d.value);
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
                    {dayWindows.map((w) => {
                      const windowError = errors.windows.get(w.id);
                      return (
                        <Stack key={w.id} gap={2}>
                          <Group gap="xs" wrap="nowrap">
                            <TextInput
                              type="time"
                              value={w.opens}
                              error={!!windowError}
                              onChange={(e) => updateWindow(w.id, { opens: e.currentTarget.value })}
                              w={120}
                            />
                            <Text size="sm">–</Text>
                            <TextInput
                              type="time"
                              value={w.closes}
                              error={!!windowError}
                              onChange={(e) => updateWindow(w.id, { closes: e.currentTarget.value })}
                              w={120}
                            />
                            <ActionIcon variant="subtle" color="red" aria-label="Venster verwijderen" onClick={() => removeWindow(w.id)}>
                              <IconTrash size={16} />
                            </ActionIcon>
                          </Group>
                          {windowError && (
                            <Text c="red" size="xs">
                              {windowError}
                            </Text>
                          )}
                        </Stack>
                      );
                    })}
                    {dayError && (
                      <Text c="red" size="xs">
                        {dayError}
                      </Text>
                    )}
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
              <Group key={i} gap="xs" wrap="nowrap" align="flex-start">
                <TextInput
                  placeholder="+31..."
                  value={n}
                  error={errors.numbers.get(i)}
                  onChange={(e) => setNumber(i, e.currentTarget.value)}
                  style={{ flex: 1 }}
                />
                <ActionIcon mt={6} variant="subtle" color="red" aria-label="Nummer verwijderen" onClick={() => removeNumber(i)}>
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
            <Button onClick={() => void save()} loading={saving} disabled={errors.any}>
              Opslaan
            </Button>
          </Group>
        </Stack>
      )}
    </Drawer>
  );
}
