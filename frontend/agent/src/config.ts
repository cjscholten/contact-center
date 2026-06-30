// Tenant- en hostconfiguratie. Niets staat meer hardcoded op één klant/IP: de tenant bepaalt
// de Keycloak-realm, en host/Keycloak-basis komen uit env-variabelen (met localhost-fallback
// voor lokaal draaien). Zet in frontend/agent/.env.local bijvoorbeeld:
//   VITE_API_BASE=http://20.107.0.204:5080
//   VITE_KEYCLOAK_BASE=http://20.107.0.204:8080
//   VITE_ASTERISK_HOST=20.107.0.204
//   VITE_TENANT=default            (of: gebruik ?tenant=acme in de URL)
const params = new URLSearchParams(window.location.search);

/** Tenant-slug: ?tenant= > eerder gekozen (localStorage) > subdomein > VITE_TENANT > 'default'. */
function resolveTenant(): string {
  const fromQuery = params.get('tenant');
  if (fromQuery) localStorage.setItem('cc.tenant', fromQuery);
  const stored = localStorage.getItem('cc.tenant');
  const host = window.location.hostname;
  const sub = host.split('.').length > 2 ? host.split('.')[0] : null; // acme.zetadesk.… → acme
  return fromQuery ?? stored ?? sub ?? (import.meta.env.VITE_TENANT as string | undefined) ?? 'default';
}

/** Realmnaam per tenant: de default-tenant houdt de bestaande realm 'contactcenter'. */
function realmForTenant(tenant: string): string {
  return tenant === 'default' ? 'contactcenter' : `tenant-${tenant}`;
}

export const tenant: string = resolveTenant();

// Asterisk-host (voor de WebRTC-WebSocket). Override via ?host= of VITE_ASTERISK_HOST.
export const asteriskHost: string =
  params.get('host') ?? import.meta.env.VITE_ASTERISK_HOST ?? 'localhost';

export const apiBase: string = import.meta.env.VITE_API_BASE ?? 'http://localhost:5080';

// Keycloak (OIDC). De realm volgt uit de tenant; de basis-URL is env-gedreven.
const keycloakBase: string =
  import.meta.env.VITE_KEYCLOAK_BASE ?? `http://${asteriskHost}:8080`;

export const oidcConfig = {
  authority: `${keycloakBase}/realms/${realmForTenant(tenant)}`,
  client_id: 'zetadesk',
  redirect_uri: `${window.location.origin}/`,
  post_logout_redirect_uri: `${window.location.origin}/`,
  scope: 'openid profile',
  automaticSilentRenew: false, // POC: geen silent-renew-iframe; token-lifespan is ruim
};
