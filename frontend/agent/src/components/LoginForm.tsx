import { useState } from 'react';
import { Button, Card, PasswordInput, Stack, TextInput, Title } from '@mantine/core';
import { IconLogin } from '@tabler/icons-react';
import { asteriskHost } from '../config';

interface Props {
  onLogin: (host: string, user: string, password: string) => Promise<void>;
}

export function LoginForm({ onLogin }: Props) {
  const [host, setHost] = useState(asteriskHost);
  const [user, setUser] = useState('agent1001');
  const [password, setPassword] = useState('changeme-dev');
  const [busy, setBusy] = useState(false);

  const submit = async () => {
    setBusy(true);
    try {
      await onLogin(host.trim(), user.trim(), password);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card withBorder shadow="sm" radius="md" w={360} padding="lg">
      <Stack>
        <Title order={3}>Agent aanmelden</Title>
        <TextInput label="Asterisk-host" value={host} onChange={(e) => setHost(e.currentTarget.value)} />
        <TextInput label="Gebruiker" value={user} onChange={(e) => setUser(e.currentTarget.value)} />
        <PasswordInput
          label="Wachtwoord"
          value={password}
          onChange={(e) => setPassword(e.currentTarget.value)}
        />
        <Button onClick={submit} loading={busy} fullWidth leftSection={<IconLogin size={18} />}>
          Aanmelden
        </Button>
      </Stack>
    </Card>
  );
}
