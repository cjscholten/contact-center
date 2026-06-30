// Gedeelde tenant- en OIDC-resolutie. Niets staat hardcoded op één klant/IP: de tenant bepaalt
// de Keycloak-realm, en host/Keycloak-basis komen uit env-variabelen (met localhost-fallback voor
// lokaal draaien). Beide front-ends gebruiken dezelfde resolutie; alleen de OIDC-client_id en de
// app-specifieke host-fallback verschillen, die geeft elke app mee aan buildOidcConfig.
const params = new URLSearchParams(window.location.search);

/** Tenant-slug: ?tenant= > eerder gekozen (localStorage) > subdomein > VITE_TENANT > 'default'. */
export function resolveTenant(): string {
  const fromQuery = params.get('tenant');
  if (fromQuery) localStorage.setItem('cc.tenant', fromQuery);
  const stored = localStorage.getItem('cc.tenant');
  const host = window.location.hostname;
  const sub = host.split('.').length > 2 ? host.split('.')[0] : null; // acme.zetadesk.… → acme
  return fromQuery ?? stored ?? sub ?? (import.meta.env.VITE_TENANT as string | undefined) ?? 'default';
}

/** Realmnaam per tenant: de default-tenant houdt de bestaande realm 'contactcenter'. */
export function realmForTenant(tenant: string): string {
  return tenant === 'default' ? 'contactcenter' : `tenant-${tenant}`;
}

/** De actuele tenant-slug, eenmaal afgeleid bij het laden. */
export const tenant: string = resolveTenant();

/** Backend-API-basis; override via VITE_API_BASE. */
export const apiBase: string = import.meta.env.VITE_API_BASE ?? 'http://localhost:5080';

export interface OidcConfig {
  authority: string;
  client_id: string;
  redirect_uri: string;
  post_logout_redirect_uri: string;
  scope: string;
  automaticSilentRenew: boolean;
}

/**
 * Bouwt de OIDC-config voor een app. De realm volgt uit de huidige tenant; de Keycloak-basis-URL
 * is env-gedreven (per app, want de fallback verschilt). client_id is per app ('zetadesk' /
 * 'zetabeheer'). POC: geen silent-renew-iframe; token-lifespan is ruim.
 */
export function buildOidcConfig(clientId: string, keycloakBase: string): OidcConfig {
  return {
    authority: `${keycloakBase}/realms/${realmForTenant(tenant)}`,
    client_id: clientId,
    redirect_uri: `${window.location.origin}/`,
    post_logout_redirect_uri: `${window.location.origin}/`,
    scope: 'openid profile',
    automaticSilentRenew: false,
  };
}
