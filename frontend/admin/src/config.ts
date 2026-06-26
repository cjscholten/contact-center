// Backend-API-adres voor ZetaBeheer. Te overschrijven via VITE_API_BASE (dev/preview).
export const apiBase: string = import.meta.env.VITE_API_BASE ?? 'http://localhost:5080';

// Keycloak (OIDC). Authority op de VM; client 'zetabeheer' is een publieke PKCE-client.
export const oidcConfig = {
  authority: import.meta.env.VITE_OIDC_AUTHORITY ?? 'http://20.107.0.204:8080/realms/contactcenter',
  client_id: 'zetabeheer',
  redirect_uri: `${window.location.origin}/`,
  post_logout_redirect_uri: `${window.location.origin}/`,
  scope: 'openid profile',
  automaticSilentRenew: false,
};
