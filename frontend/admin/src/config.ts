// App-specifieke configuratie voor ZetaBeheer. De gedeelde tenant-/realm-resolutie en apiBase komen
// uit @zeta/ui; hier blijft alleen de Keycloak-basis en de OIDC-client_id 'zetabeheer'. Zet in
// frontend/admin/.env.local bijvoorbeeld:
//   VITE_API_BASE=http://20.107.0.204:5080
//   VITE_KEYCLOAK_BASE=http://20.107.0.204:8080
//   VITE_TENANT=default            (of: gebruik ?tenant=acme in de URL)
import { apiBase, buildOidcConfig } from '@zeta/ui';

export { apiBase };

const keycloakBase: string = import.meta.env.VITE_KEYCLOAK_BASE ?? 'http://localhost:8080';

export const oidcConfig = buildOidcConfig('zetabeheer', keycloakBase);
