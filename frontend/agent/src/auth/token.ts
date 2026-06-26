// Houdt het actuele Keycloak-access-token vast buiten React, zodat de fetch-wrappers
// en de SignalR-verbinding er de Authorization-header mee kunnen vullen. App.tsx werkt
// dit bij zodra de auth-status verandert.
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
