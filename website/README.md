# Zetadesk marketing-website

Statische site (geen build-step) voor de apex `zetadesk.net` + `www.`:

| Pagina | Inhoud |
| --- | --- |
| `index.html` | Frontpage: marketing (hero met CSS-mockup van ZetaDesk, features, USP's) |
| `techniek.html` | Architectuur (SVG-diagram) + de bouwstenen van de stack |
| `contact.html` | Contactformulier (opent mailto — er is geen mail-backend) + directe gegevens |

Gedeeld: `styles.css` (kleuren uit het Mantine-thema in
`frontend/packages/ui/src/theme.ts`), `site.js` (mobiel menu + formulier),
`favicon.svg`.

## Deploy

De map wordt read-only in de Caddy-container gemount (`infra/docker-compose.yml`)
en geserveerd door het apex-blok in `infra/caddy/Caddyfile`. Vereist:

1. DNS-A-records voor `<CC_DOMAIN>` (apex) en `www.<CC_DOMAIN>` → het VM-IP.
2. `docker compose up -d caddy` (of een volledige `up -d`) na wijzigingen aan
   compose/Caddyfile; pure content-wijzigingen zijn direct zichtbaar via de mount
   zodra de repo op de VM gepulld is.

Lokaal bekijken: open `index.html` direct in de browser, of serveer de map
(bv. `npx serve website`). Extensieloze URL's (`/techniek`) werken alleen achter
Caddy (`try_files`); de onderlinge links gebruiken daarom `.html`.
