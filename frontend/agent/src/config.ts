// Asterisk-host (voor de WebRTC-WebSocket) en backend-API-adres.
// De host ligt voor nu vast; later wordt dit waarschijnlijk onderdeel van de
// beheeromgeving. Te overschrijven via ?host= of VITE_ASTERISK_HOST (dev/preview).
const params = new URLSearchParams(window.location.search);

const ASTERISK_HOST = '20.107.0.204';

export const asteriskHost: string =
  params.get('host') ?? import.meta.env.VITE_ASTERISK_HOST ?? ASTERISK_HOST;

export const apiBase: string = import.meta.env.VITE_API_BASE ?? 'http://localhost:5080';

// Keycloak (OIDC). De authority draait op de VM; client 'zetadesk' is een publieke PKCE-client.
const KEYCLOAK_AUTHORITY = `http://${ASTERISK_HOST}:8080/realms/contactcenter`;

export const oidcConfig = {
  authority: import.meta.env.VITE_OIDC_AUTHORITY ?? KEYCLOAK_AUTHORITY,
  client_id: 'zetadesk',
  redirect_uri: `${window.location.origin}/`,
  post_logout_redirect_uri: `${window.location.origin}/`,
  scope: 'openid profile',
  automaticSilentRenew: false, // POC: geen silent-renew-iframe; token-lifespan is ruim
};
