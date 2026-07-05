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

De site draait op **externe hosting** (niet op de VM): upload de zes bestanden
hierboven (alles behalve deze README) naar de webroot. Er is geen build-step en
geen serverconfiguratie nodig; de onderlinge links gebruiken `.html`-extensies
en werken dus op elke statische hosting.

Het apex-blok in de Caddyfile en de website-mount in docker-compose zijn
verwijderd toen de hosting verhuisde (juli 2026) — de apex-DNS wijst niet meer
naar de VM.

Lokaal bekijken: open `index.html` direct in de browser, of serveer de map
(bv. `npx serve website`, of de launch-config `website` op poort 3211).
