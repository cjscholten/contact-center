// App-specifieke configuratie voor ZetaDesk. De gedeelde tenant-/realm-resolutie en apiBase komen
// uit @zeta/ui; hier blijft alleen wat agent-eigen is: de Asterisk-host (WebRTC-WebSocket) en de
// OIDC-client_id 'zetadesk'. Zet in frontend/agent/.env.local bijvoorbeeld:
//   VITE_API_BASE=https://api.zetadesk.net           (of http://20.107.0.204:5080 zonder TLS)
//   VITE_KEYCLOAK_BASE=https://auth.zetadesk.net     (of http://20.107.0.204:8080)
//   VITE_SIP_WS_URL=wss://sip.zetadesk.net/ws        (of laat weg voor ws://<host>:8088/ws)
//   VITE_ASTERISK_HOST=20.107.0.204
//   VITE_TENANT=default            (of: gebruik ?tenant=acme in de URL)
import { apiBase, buildOidcConfig } from '@zeta/ui';

export { apiBase };

const params = new URLSearchParams(window.location.search);

// Asterisk-host (voor de WebRTC-WebSocket). Override via ?host= of VITE_ASTERISK_HOST.
export const asteriskHost: string =
  params.get('host') ?? import.meta.env.VITE_ASTERISK_HOST ?? 'localhost';

// SIP-WebSocket-URL voor de softphone. Prod (achter Caddy): wss://sip.<domein>/ws;
// dev-fallback: ongebeveiligde ws:// direct naar Asterisk op de VM.
export const sipWsUrl: string =
  import.meta.env.VITE_SIP_WS_URL ?? `ws://${asteriskHost}:8088/ws`;

// Keycloak-basis: env-gedreven, met fallback op de Asterisk-host (zelfde VM).
const keycloakBase: string =
  import.meta.env.VITE_KEYCLOAK_BASE ?? `http://${asteriskHost}:8080`;

export const oidcConfig = buildOidcConfig('zetadesk', keycloakBase);
