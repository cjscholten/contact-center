# Infra — de stack op de VM

Alle backenddiensten draaien als containers op de VM via `docker-compose.yml`,
met `network_mode: host` (vermijdt NAT-problemen met SIP/RTP):

| Service  | Poort | Wat |
|----------|-------|-----|
| asterisk | 5060/udp, 8088, 10000-10100/udp | Telefonie (PJSIP/ARI) |
| coturn   | 3478/udp+tcp, 49160-49200/udp | TURN/STUN (WebRTC-relay voor thuiswerkers) |
| postgres | 5432 | Database |
| keycloak | 8080 | OIDC (zie `keycloak/README.md`) |
| backend  | 5080 | .NET-API (ZetaDesk/ZetaBeheer) |

## NSG-regels (eenmalig, vanaf je dev-IP)

Inbound toestaan: **5060/udp**, **8088**, **10000-10100/udp** (telefonie),
**5432** (db, dev), **8080** (Keycloak), **5080** (backend). De trunk-leg vanaf de
SBC loopt via `AllowVnetInBound`.

Voor TURN (thuiswerkers) ook **3478/udp**, **3478/tcp** en de relay-range
**49160-49200/udp** openzetten — vanaf de netwerken van de agents (thuis), dus in de
praktijk breed (`Internet`) of per bekend agent-IP.

## Secrets (`infra/.env`)

De stack-secrets staan **niet in git**. Maak eenmalig per omgeving een `infra/.env`
op basis van het sjabloon en vul echte waarden in (productie: geen `changeme-dev`):

```bash
# op de VM, in deze map
cp .env.example .env
# bewerk .env: POSTGRES_PASSWORD, KEYCLOAK_ADMIN_PASSWORD, ARI_PASSWORD
```

`docker compose` leest `infra/.env` automatisch. Ontbreekt het bestand of een variabele,
dan stopt `compose up` met een duidelijke melding (de `${VAR:?…}`-bewaking). `ARI_PASSWORD`
moet voorlopig gelijk zijn aan het wachtwoord in `asterisk/conf/ari.conf` (die is nog statisch).

Lokaal de backend draaien (`dotnet run`): kopieer
`backend/src/ContactCenter.Api/appsettings.Local.json.example` naar
`appsettings.Local.json` (ook gitignored) — Program.cs laadt dat in elke omgeving.

## Starten

```bash
# op de VM, in deze map (vereist infra/.env, zie hierboven)
docker compose up -d --build
```

De backend past bij het opstarten de EF-migraties toe en seedt de database.
`docker compose logs -f backend` om mee te kijken. De DB-connectionstring en het
ARI-wachtwoord komen via env uit `infra/.env`; `appsettings.json` bevat alleen
secret-loze defaults.

## Front-ends (lokaal, dev)

De Vite-apps draaien lokaal maar praten met de VM-backend en Keycloak. Zet daarom
`VITE_API_BASE` (bijv. in `frontend/agent/.env.local` en `frontend/admin/.env.local`):

```
VITE_API_BASE=http://20.107.0.204:5080
```

De OIDC-authority en de Asterisk-host wijzen al naar de VM. CORS staat
`localhost:5173/5174` toe.

## TURN/STUN (coturn) voor thuiswerkers

Browser-agents achter (symmetrische) NAT hebben een TURN-relay nodig voor de WebRTC-media.
`coturn` draait als container op de VM (host-netwerk, publiek IP) met **use-auth-secret**:
de backend geeft per agent een **tijdelijke** credential uit (username `vervaltijd:agent`,
credential = HMAC-SHA1 over het gedeelde `TURN_SECRET`), via `GET /api/agents/me/ice`. De
softphone geeft de teruggekregen `iceServers` door aan SIP.js. Zet in `infra/.env`:
`TURN_SECRET`, `TURN_PUBLIC_HOST` (publiek IP voor de browser) en `TURN_EXTERNAL_IP`
(`publiek/privaat`, want bij host-netwerk ziet coturn het privé-IP). Leeg `Turn:Secret`
in de backend = TURN uit → terugval op host-kandidaten (lokaal netwerk werkt dan nog).
TLS/`turns:` (poort 5349) is nog niet ingericht — hangt samen met de wss/TLS-stap.

## Gedeeld geluidsvolume

`cc-sounds` is gemount in de backend (`/var/lib/cc-sounds`, schrijft gegenereerde
audio) én in Asterisk (`/usr/share/asterisk/sounds/custom`, speelt `sound:custom/...`).
Hierop bouwen de TTS-prompts en eigen wachtmuziek voort.

## TTS (Piper)

De backend-image bundelt **Piper** (lokale neural-TTS) in `/opt/piper` met twee NL-stemmen
(`nl_NL-pim-medium`, `nl_NL-ronnie-medium`) en `sox`. Bij het opslaan van een wachtrij in
ZetaBeheer wordt een ingevulde welkomst-/gesloten-tekst gesynthetiseerd naar
`cc-sounds/queue-<naam>-{welcome,closed}.wav` (8kHz mono) en als `sound:custom/...` afgespeeld;
lege tekst valt terug op de standaard-Asterisk-prompt. Een extra stem toevoegen: het
`.onnx`(+`.onnx.json`)-paar in `/opt/piper/voices` zetten (Dockerfile) en opnemen in `VOICES`
in `frontend/admin/src/components/QueueEditorDrawer.tsx`.

## Netwerk-/auth-notities

- De backend praat met Asterisk/Postgres/Keycloak via **localhost** (host-netwerk),
  niet via het publieke IP — dat kan de VM zelf niet bereiken (geen NAT-hairpin), dus
  géén `VmHost` in de container. Alleen de **token-issuer** is het publieke IP
  (`Keycloak__ValidIssuer`), want de browser haalt het token daar; de backend haalt de
  metadata/JWKS lokaal op en accepteert beide issuers.
- Het sounds-pad `…/sounds/custom` voor `sound:custom/<naam>` even live verifiëren
  (Asterisk valt terug van de taalmap op de basismap).
