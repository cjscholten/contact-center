// Tenant- en hostconfiguratie voor ZetaBeheer. De tenant bepaalt de Keycloak-realm; host/
// Keycloak-basis komen uit env-variabelen. Zet in frontend/admin/.env.local bijvoorbeeld:
//   VITE_API_BASE=http://20.107.0.204:5080
//   VITE_KEYCLOAK_BASE=http://20.107.0.204:8080
//   VITE_TENANT=default            (of: gebruik ?tenant=acme in de URL)
const params = new URLSearchParams(window.location.search);

/** Tenant-slug: ?tenant= > eerder gekozen (localStorage) > subdomein > VITE_TENANT > 'default'. */
function resolveTenant(): string {
  const fromQuery = params.get('tenant');
  if (fromQuery) localStorage.setItem('cc.tenant', fromQuery);
  const stored = localStorage.getItem('cc.tenant');
  const host = window.location.hostname;
  const sub = host.split('.').length > 2 ? host.split('.')[0] : null;
  return fromQuery ?? stored ?? sub ?? (import.meta.env.VITE_TENANT as string | undefined) ?? 'default';
}

/** Realmnaam per tenant: de default-tenant houdt de bestaande realm 'contactcenter'. */
function realmForTenant(tenant: string): string {
  return tenant === 'default' ? 'contactcenter' : `tenant-${tenant}`;
}

export const tenant: string = resolveTenant();

export const apiBase: string = import.meta.env.VITE_API_BASE ?? 'http://localhost:5080';

const keycloakBase: string = import.meta.env.VITE_KEYCLOAK_BASE ?? 'http://localhost:8080';

// Keycloak (OIDC). Authority volgt uit de tenant; client 'zetabeheer' is een publieke PKCE-client.
export const oidcConfig = {
  authority: `${keycloakBase}/realms/${realmForTenant(tenant)}`,
  client_id: 'zetabeheer',
  redirect_uri: `${window.location.origin}/`,
  post_logout_redirect_uri: `${window.location.origin}/`,
  scope: 'openid profile',
  automaticSilentRenew: false,
};
