// Backend-API-adres voor ZetaBeheer. Te overschrijven via VITE_API_BASE (dev/preview).
export const apiBase: string = import.meta.env.VITE_API_BASE ?? 'http://localhost:5080';
