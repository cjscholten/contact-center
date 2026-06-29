import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // Vaste poort: de Keycloak-client 'zetabeheer' staat alleen localhost:5174 toe.
  server: { port: 5174, strictPort: true },
})
