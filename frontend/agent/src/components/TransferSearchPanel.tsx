import { useEffect, useState } from 'react';
import { Badge, Button, Card, Group, Stack, Text, TextInput, Title } from '@mantine/core';
import { useDebouncedValue } from '@mantine/hooks';
import { IconArrowForwardUp, IconSearch } from '@tabler/icons-react';
import type { DirectoryEntry } from '../api/agentApi';

interface Props {
  canTransfer: boolean;
  onSearch: (query: string) => Promise<DirectoryEntry[]>;
  onTransfer: (entry: DirectoryEntry) => void;
}

export function TransferSearchPanel({ canTransfer, onSearch, onTransfer }: Props) {
  const [query, setQuery] = useState('');
  const [debounced] = useDebouncedValue(query, 300);
  const [results, setResults] = useState<DirectoryEntry[]>([]);

  useEffect(() => {
    let active = true;
    onSearch(debounced)
      .then((r) => {
        if (active) setResults(r);
      })
      .catch(() => {
        if (active) setResults([]);
      });
    return () => {
      active = false;
    };
  }, [debounced, onSearch]);

  return (
    <Card withBorder radius="md" padding="md">
      <Stack gap="sm">
        <Title order={4}>Doorverbinden</Title>
        <TextInput
          leftSection={<IconSearch size={16} />}
          placeholder="Zoek collega of contact…"
          value={query}
          onChange={(e) => setQuery(e.currentTarget.value)}
        />
        {!canTransfer && (
          <Text c="dimmed" size="sm">
            Doorverbinden kan alleen tijdens een gesprek.
          </Text>
        )}
        {results.length === 0 && (
          <Text c="dimmed" size="sm">
            Geen resultaten.
          </Text>
        )}
        {results.map((entry) => (
          <Group
            key={`${entry.kind}:${entry.target}`}
            justify="space-between"
            wrap="nowrap"
            style={{
              border: '1px solid var(--mantine-color-default-border)',
              borderRadius: 'var(--mantine-radius-md)',
              padding: '8px 12px',
            }}
          >
            <Group gap="sm" wrap="nowrap">
              <Badge variant="light" color={entry.kind === 'agent' ? 'blue' : 'grape'}>
                {entry.kind === 'agent' ? 'collega' : 'contact'}
              </Badge>
              <div>
                <Text>{entry.label}</Text>
                {(entry.detail || entry.kind === 'contact') && (
                  <Text c="dimmed" size="xs">
                    {[entry.detail, entry.kind === 'contact' ? entry.target : null].filter(Boolean).join(' · ')}
                  </Text>
                )}
              </div>
            </Group>
            <Button
              size="compact-sm"
              variant="light"
              leftSection={<IconArrowForwardUp size={16} />}
              disabled={!canTransfer}
              onClick={() => onTransfer(entry)}
            >
              Doorverbinden
            </Button>
          </Group>
        ))}
      </Stack>
    </Card>
  );
}
