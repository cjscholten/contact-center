import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // Vaste poort: de Keycloak-client 'zetadesk' staat alleen localhost:5173 toe.
  server: { port: 5173, strictPort: true },
  // De @tabler/icons-react-barrel (6149 re-exports) laat de dev-time dep-optimizer van
  // rolldown-vite struikelen ("Failed to parse source for import analysis"); uitsluiten van
  // pre-bundling laat Vite de losse icoon-modules serveren. De productie-build is niet geraakt.
  optimizeDeps: { exclude: ['@tabler/icons-react'] },
})
