import { useEffect, useState } from 'react';
import { AppShell, Burger, Button, Group, NavLink, Text, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import {
  IconAddressBook,
  IconHeadset,
  IconLogout,
  IconPhoneCall,
  IconSettings,
} from '@tabler/icons-react';
import { useAuth } from 'react-oidc-context';
import {
  AccessDeniedScreen,
  AuthErrorScreen,
  LoadingScreen,
  LoginScreen,
  realmRolesFromToken,
  setAccessToken,
} from '@zeta/ui';
import { QueuesPage } from './components/QueuesPage';
import { AgentsPage } from './components/AgentsPage';
import { ContactsPage } from './components/ContactsPage';
import { SettingsPage } from './components/SettingsPage';

type Section = 'queues' | 'agents' | 'contacts' | 'settings';

const NAV: { key: Section; label: string; icon: typeof IconPhoneCall }[] = [
  { key: 'queues', label: 'Wachtrijen', icon: IconPhoneCall },
  { key: 'agents', label: 'Agents', icon: IconHeadset },
  { key: 'contacts', label: 'Contacten', icon: IconAddressBook },
  { key: 'settings', label: 'Instellingen', icon: IconSettings },
];

export default function App() {
  const auth = useAuth();

  useEffect(() => {
    setAccessToken(auth.user?.access_token ?? null);
  }, [auth.user]);

  const logout = () => {
    setAccessToken(null);
    void auth.signoutRedirect();
  };

  if (auth.isLoading) {
    return <LoadingScreen />;
  }
  if (auth.error) {
    return <AuthErrorScreen message={auth.error.message} onRetry={() => void auth.signinRedirect()} />;
  }
  if (!auth.isAuthenticated) {
    return <LoginScreen appName="ZetaBeheer" onLogin={() => void auth.signinRedirect()} />;
  }
  if (!realmRolesFromToken(auth.user?.access_token).includes('admin')) {
    return (
      <AccessDeniedScreen
        message="Je account heeft de rol 'admin' nodig voor de beheeromgeving."
        onLogout={logout}
      />
    );
  }

  return <AdminShell username={auth.user?.profile.preferred_username ?? ''} onLogout={logout} />;
}

function AdminShell({ username, onLogout }: { username: string; onLogout: () => void }) {
  const [section, setSection] = useState<Section>('queues');
  const [navOpened, { toggle: toggleNav, close: closeNav }] = useDisclosure();

  return (
    <AppShell
      header={{ height: 56 }}
      navbar={{ width: 220, breakpoint: 'sm', collapsed: { mobile: !navOpened } }}
      padding="md"
    >
      <AppShell.Header>
        <Group h="100%" px="md" justify="space-between" wrap="nowrap">
          <Group gap="xs" wrap="nowrap">
            <Burger opened={navOpened} onClick={toggleNav} hiddenFrom="sm" size="sm" />
            <Title order={3}>ZetaBeheer</Title>
            <Text c="dimmed" size="sm" visibleFrom="xs">
              beheeromgeving
            </Text>
          </Group>
          <Group gap="sm" wrap="nowrap">
            <Text size="sm" c="dimmed">
              {username}
            </Text>
            <Button variant="subtle" color="gray" size="compact-sm" leftSection={<IconLogout size={16} />} onClick={onLogout}>
              Afmelden
            </Button>
          </Group>
        </Group>
      </AppShell.Header>

      <AppShell.Navbar p="xs">
        {NAV.map((item) => (
          <NavLink
            key={item.key}
            active={section === item.key}
            label={item.label}
            leftSection={<item.icon size={18} />}
            onClick={() => {
              setSection(item.key);
              closeNav();
            }}
          />
        ))}
      </AppShell.Navbar>

      <AppShell.Main>
        {section === 'queues' ? (
          <QueuesPage />
        ) : section === 'agents' ? (
          <AgentsPage />
        ) : section === 'contacts' ? (
          <ContactsPage />
        ) : (
          <SettingsPage />
        )}
      </AppShell.Main>
    </AppShell>
  );
}
