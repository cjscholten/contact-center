// Asterisk-host (voor de WebRTC-WebSocket) en backend-API-adres.
// De host ligt voor nu vast; later wordt dit waarschijnlijk onderdeel van de
// beheeromgeving. Te overschrijven via ?host= of VITE_ASTERISK_HOST (dev/preview).
const params = new URLSearchParams(window.location.search);

const ASTERISK_HOST = '4.210.180.67';

export const asteriskHost: string =
  params.get('host') ?? import.meta.env.VITE_ASTERISK_HOST ?? ASTERISK_HOST;

export const apiBase: string = import.meta.env.VITE_API_BASE ?? 'http://localhost:5080';
