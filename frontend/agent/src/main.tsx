import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { AppProviders } from '@zeta/ui';
import App from './App.tsx';
import { oidcConfig } from './config';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppProviders oidcConfig={oidcConfig}>
      <App />
    </AppProviders>
  </StrictMode>,
);
