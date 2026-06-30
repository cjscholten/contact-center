import type { ReactNode } from 'react';
import { MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { AuthProvider } from 'react-oidc-context';
import '@mantine/core/styles.css';
import '@mantine/notifications/styles.css';
import './styles/global.css';
import { zetaTheme } from './theme';
import type { OidcConfig } from './tenant';

// Na de Keycloak-redirect de code/state uit de URL strippen.
const onSigninCallback = () => {
  window.history.replaceState({}, document.title, window.location.pathname);
};

/**
 * Gedeelde app-bootstrap voor beide front-ends: OIDC-auth, het Zeta-thema, notificaties en de
 * basis-CSS in één plek. Elke app rendert hierbinnen zijn eigen <App />.
 */
export function AppProviders({ oidcConfig, children }: { oidcConfig: OidcConfig; children: ReactNode }) {
  return (
    <AuthProvider {...oidcConfig} onSigninCallback={onSigninCallback}>
      <MantineProvider theme={zetaTheme} defaultColorScheme="auto">
        <Notifications />
        {children}
      </MantineProvider>
    </AuthProvider>
  );
}
