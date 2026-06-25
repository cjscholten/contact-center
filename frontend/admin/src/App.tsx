import { useState } from 'react';
import { AppShell, Burger, Group, NavLink, Text, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import {
  IconAddressBook,
  IconHeadset,
  IconPhoneCall,
  IconSettings,
} from '@tabler/icons-react';
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
  const [section, setSection] = useState<Section>('queues');
  const [navOpened, { toggle: toggleNav, close: closeNav }] = useDisclosure();

  return (
    <AppShell
      header={{ height: 56 }}
      navbar={{ width: 220, breakpoint: 'sm', collapsed: { mobile: !navOpened } }}
      padding="md"
    >
      <AppShell.Header>
        <Group h="100%" px="md" gap="xs">
          <Burger opened={navOpened} onClick={toggleNav} hiddenFrom="sm" size="sm" />
          <Title order={3}>ZetaBeheer</Title>
          <Text c="dimmed" size="sm">
            beheeromgeving
          </Text>
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
