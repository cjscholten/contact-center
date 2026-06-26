// Houdt het actuele Keycloak-access-token vast buiten React, zodat de fetch-wrapper
// in adminApi er de Authorization-header mee kan vullen. App.tsx werkt dit bij op
// elke auth-wijziging.
let accessToken: string | null = null;

export function setAccessToken(token: string | null): void {
  accessToken = token;
}

/** Authorization-header met het huidige token (leeg object als er geen token is). */
export function authHeader(): Record<string, string> {
  return accessToken ? { Authorization: `Bearer ${accessToken}` } : {};
}

/** Realm-rollen uit het access-token (Keycloak zet ze in realm_access.roles). */
export function realmRolesFromToken(accessToken: string | undefined): string[] {
  if (!accessToken) return [];
  try {
    const payloadPart = accessToken.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = payloadPart + '==='.slice((payloadPart.length + 3) % 4);
    const payload = JSON.parse(atob(padded)) as { realm_access?: { roles?: string[] } };
    return payload.realm_access?.roles ?? [];
  } catch {
    return [];
  }
}
