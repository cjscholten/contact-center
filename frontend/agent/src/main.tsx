import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { AuthProvider } from 'react-oidc-context';
import '@mantine/core/styles.css';
import '@mantine/notifications/styles.css';
import './global.css';
import App from './App.tsx';
import { oidcConfig } from './config';

// Na de Keycloak-redirect de code/state uit de URL strippen.
const onSigninCallback = () => {
  window.history.replaceState({}, document.title, window.location.pathname);
};

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthProvider {...oidcConfig} onSigninCallback={onSigninCallback}>
      <MantineProvider defaultColorScheme="auto">
        <Notifications />
        <App />
      </MantineProvider>
    </AuthProvider>
  </StrictMode>,
);
