// Asterisk-host (voor de WebRTC-WebSocket) en backend-API-adres.
// Host komt uit ?host= → localStorage → build-time env, met localhost als laatste val.
const params = new URLSearchParams(window.location.search);

export const asteriskHost: string =
  params.get('host') ??
  localStorage.getItem('cc-host') ??
  import.meta.env.VITE_ASTERISK_HOST ??
  'localhost';

export const apiBase: string = import.meta.env.VITE_API_BASE ?? 'http://localhost:5080';

export function rememberHost(host: string): void {
  localStorage.setItem('cc-host', host);
}
