import type { ReactNode } from 'react';
import { Button, Center, Loader, Stack, Text, Title } from '@mantine/core';
import { IconLogin, IconLogout } from '@tabler/icons-react';

/** Vult het scherm en centreert zijn inhoud — basis voor alle volledige-pagina-statussen. */
export function Centered({ children }: { children: ReactNode }) {
  return (
    <Center mih="100vh" p="md">
      {children}
    </Center>
  );
}

/** Volledig-scherm laad-status (bv. tijdens het ophalen van de auth-sessie). */
export function LoadingScreen({ message }: { message?: string }) {
  return (
    <Centered>
      <Stack align="center">
        <Loader />
        {message ? <Text c="dimmed">{message}</Text> : null}
      </Stack>
    </Centered>
  );
}

/** Aanmeldscherm met de Keycloak-redirect-knop; appName is de merknaam (ZetaDesk / ZetaBeheer). */
export function LoginScreen({ appName, onLogin }: { appName: string; onLogin: () => void }) {
  return (
    <Centered>
      <Stack align="center">
        <Title order={2}>{appName}</Title>
        <Button size="md" leftSection={<IconLogin size={18} />} onClick={onLogin}>
          Aanmelden met Keycloak
        </Button>
      </Stack>
    </Centered>
  );
}

/** Foutscherm na een mislukte aanmelding, met een opnieuw-proberen-knop. */
export function AuthErrorScreen({ message, onRetry }: { message?: string; onRetry: () => void }) {
  return (
    <Centered>
      <Stack align="center">
        <Title order={4}>Aanmelden mislukt</Title>
        {message ? (
          <Text c="dimmed" size="sm">
            {message}
          </Text>
        ) : null}
        <Button onClick={onRetry}>Opnieuw proberen</Button>
      </Stack>
    </Centered>
  );
}

/** Toegang-geweigerd-scherm voor wie wel is aangemeld maar de vereiste rol mist. */
export function AccessDeniedScreen({ message, onLogout }: { message: string; onLogout: () => void }) {
  return (
    <Centered>
      <Stack align="center">
        <Title order={3}>Geen toegang</Title>
        <Text c="dimmed" size="sm">
          {message}
        </Text>
        <Button variant="default" leftSection={<IconLogout size={16} />} onClick={onLogout}>
          Afmelden
        </Button>
      </Stack>
    </Centered>
  );
}
