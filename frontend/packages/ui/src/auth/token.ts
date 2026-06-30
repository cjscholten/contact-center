// Houdt het actuele Keycloak-access-token vast buiten React, zodat de fetch-wrappers en de
// SignalR-verbinding er de Authorization-header mee kunnen vullen. De app werkt dit bij zodra
// de auth-status verandert. Gedeeld door ZetaDesk en ZetaBeheer.
let accessToken: string | null = null;

export function setAccessToken(token: string | null): void {
  accessToken = token;
}

export function getAccessToken(): string | null {
  return accessToken;
}

/** Authorization-header met het huidige token (leeg object als er geen token is). */
export function authHeader(): Record<string, string> {
  return accessToken ? { Authorization: `Bearer ${accessToken}` } : {};
}

/** Realm-rollen uit het access-token (Keycloak zet ze in realm_access.roles). */
export function realmRolesFromToken(token: string | undefined): string[] {
  if (!token) return [];
  try {
    const payloadPart = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = payloadPart + '==='.slice((payloadPart.length + 3) % 4);
    const payload = JSON.parse(atob(padded)) as { realm_access?: { roles?: string[] } };
    return payload.realm_access?.roles ?? [];
  } catch {
    return [];
  }
}
