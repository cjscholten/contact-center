import { createTheme, type MantineColorsTuple } from '@mantine/core';

// Het gedeelde Zeta-design system: één Mantine-thema voor ZetaDesk (agent) én ZetaBeheer (admin),
// zodat beide apps dezelfde merkkleur, typografie en component-defaults delen.
const zeta: MantineColorsTuple = [
  '#eef3ff',
  '#dce4f5',
  '#b9c7e2',
  '#94a8d0',
  '#748dc1',
  '#5f7cb8',
  '#5474b4',
  '#44639f',
  '#3a5890',
  '#2c4b80',
];

export const zetaTheme = createTheme({
  primaryColor: 'zeta',
  colors: { zeta },
  defaultRadius: 'md',
  fontFamily:
    '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif',
  headings: {
    fontWeight: '600',
  },
  cursorType: 'pointer',
});
