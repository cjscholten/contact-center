# Contact center

Zelfgebouwd en zelf gehost contact center. Drie onderdelen: agent-applicatie (web), beheeromgeving (web) en backend. Telefonie via Asterisk (ARI) achter een AudioCodes SBC met een Twilio SIP-trunk.

## Architectuur

```
Beller (PSTN) → Twilio-trunk → AudioCodes SBC → Asterisk
                                                   │ ARI (REST + WebSocket)
                                            Backend-API (.NET)
                                                   │
                              Agent-app & beheeromgeving (React, later)
```

- **Asterisk doet media, de backend beslist.** Inkomende gesprekken landen in een ARI Stasis-app (`contactcenter`); de backend zoekt op het gebelde nummer de wachtrijconfiguratie op in PostgreSQL en beslist: welkomsttekst + wachtrij, gesloten-melding, of doorschakelen.
- Agents bellen via de browser (WebRTC, SIP.js) rechtstreeks met Asterisk.
- **De wachtrij draait server-side via ARI** (geen `app_queue`): de backend houdt wachtende bellers in een holding-brug met wachtmuziek, belt een beschikbare agent (originate) en zet beller en agent bij opnemen samen in een mixing-brug. Dat geeft de backend volledige grip voor in de wacht zetten en doorverbinden. Openingstijden, teksten en ad-hoc sluiting/doorschakeling staan in de database (zie "Wachtrijconfiguratie").

## Mappen

| Map | Inhoud |
|---|---|
| `backend/` | ASP.NET Core backend: ARI-client, wachtrijbeslissingen (EF Core/PostgreSQL) + unit-tests |
| `infra/` | Docker-opzet voor Asterisk (Ubuntu 24.04, Asterisk 20) en PostgreSQL 17 |
| `frontend/agent/` | Agent-app: React + TypeScript + Mantine (Vite), met SIP.js/WebRTC |
| `poc-agent/` | Oorspronkelijke kale browser-agent (HTML + SIP.js); vervangen door `frontend/agent`, blijft als referentie |

## POC draaien

Doel: testnummer bellen → welkomsttekst → wachtrij met muziek → browser-agent neemt aan.

### 1. Asterisk starten (Linux-VM in Azure, naast de SBC)

De SBC draait in Azure; zet de Asterisk-VM in **dezelfde VNet** (of een gepeerde VNet). De trunk-leg blijft dan volledig privé. Concreet:

- Ubuntu 24.04 LTS, B2s volstaat voor de POC, met een **Standard static public IP** (voor de agent-leg).
- Docker installeren, deze repo erheen, dan:

```bash
cd infra
# eerst: SBC_IP invullen in asterisk/conf/pjsip.conf (2 plekken)
#        = het privé-IP van het SBC-interface in de VNet
# en in asterisk/conf/rtp.conf de [ice_host_candidates]-mapping
#        privé-IP VM => publiek IP VM invullen (agent-leg via internet)
docker compose up --build
```

NSG-regels voor de VM:

| Poort | Bron | Doel |
|---|---|---|
| 10000–10100/udp | jouw publieke IP | WebRTC-media agent |
| 8088/tcp | jouw publieke IP | ARI + agent-WebSocket |
| 5432/tcp | jouw publieke IP | PostgreSQL (zolang de backend op de dev-machine draait) |
| 22/tcp | jouw publieke IP | beheer |

(Poort 5038/AMI is niet meer nodig — de wachtrij draait nu via ARI; de NSG-regel mag weg.)

De trunk-leg (5060 + RTP vanaf de SBC) hoeft géén eigen regels: dat is VNet-intern verkeer en valt onder de standaardregel `AllowVnetInBound`; internet wordt door `DenyAllInBound` geblokkeerd. Kanttekening: dat vangnet werkt alleen zolang er geen brede allow-regels bij komen. Vóór er echte nummers aan hangen: een expliciet allow/deny-paar voor 5060 (allow vanaf SBC-subnet, deny voor de rest) toevoegen. Een Asterisk met 5060 open op een publiek IP wordt binnen minuten gevonden door SIP-scanners.

`network_mode: host` werkt niet op Docker Desktop voor Windows; lokaal draaien kan alleen via WSL2 met `networkingMode=mirrored`, maar met de SBC in Azure is de VM-route de logische.

### 2. SBC configureren (AudioCodes)

- IP Group / Proxy Set richting `<privé-IP Asterisk-VM>:5060` (UDP).
- Routing rule: testnummer(s) van de Twilio-trunk → Asterisk IP Group.
- Codecs: G.711 a-law/µ-law toestaan.
- Media (RTP) van de SBC naar de VM loopt ook privé; de NSG-regels hierboven dekken dit.

### 3. Backend starten

Windows-snelstart: zet eenmalig het publieke IP van de VM in `vm-ip.txt` (gitignored) in de repo-root en dubbelklik `start-poc.cmd` — dat opent backend en agent-pagina elk in een eigen venster plus de browser. Handmatig:

```powershell
cd backend
dotnet run --project src/ContactCenter.Api -- --VmHost=<publiek-IP-VM>
```

`--VmHost` wijst zowel ARI als PostgreSQL naar de VM; zonder die parameter gelden de localhost-waarden uit `appsettings.json`. Log toont "Verbonden met ARI-events" en "Database gemigreerd en gereed" als beide koppelingen staan. Healthcheck: `http://localhost:5080/health`. Bij de eerste start worden de migraties uitgevoerd en wordt wachtrij `support` geseed (24/7 open, testnummer +19205008321).

Let op: ARI gaat dan met basic auth over onversleuteld http het internet over. Voor de POC acceptabel zolang 8088 in de NSG op jouw IP staat dichtgetimmerd; daarna backend in Azure draaien of TLS op de Asterisk-HTTP-server zetten.

### 4. Agent verbinden

De agent-app is een React + TypeScript + Mantine-app (Vite) in `frontend/agent/`:

```powershell
cd frontend/agent
npm install   # eenmalig
npm run dev   # Vite dev-server op http://localhost:5173
```

Open `http://localhost:5173/?host=<publiek-IP-VM>` (microfoontoegang vereist localhost of https), meld je aan en bedien het gesprek. De app gebruikt `ws://` op poort 8088; voor productie komt hier wss met echte certificaten. (`start-poc.cmd` start backend + deze app samen.) De oude `poc-agent/` blijft als kale referentie bestaan.

### 5. Testgesprek

Bel een testnummer van de trunk. Verwacht: welkomsttekst ("thank you for your patience"), wachtmuziek, agent-pagina toont "inkomend gesprek", aannemen → spraakverbinding.

## Wachtrijconfiguratie (PostgreSQL)

De routering is data: een inkomend nummer (`InboundNumbers`) wijst naar een wachtrij (`Queues`) met openingstijden (`OpeningHoursWindow`), prompts en ad-hoc-instellingen. De backend leest dit per gesprek — wijzigingen gelden direct voor het volgende gesprek, zonder herstart.

Beheren kan (tot de beheeromgeving er is) via psql op de VM:

```bash
sudo docker exec -it cc-postgres psql -U cc -d contactcenter
```

```sql
-- ad-hoc sluiten / heropenen (gesloten-tekst voor de beller)
UPDATE "Queues" SET "AdHocClosed" = true  WHERE "Name" = 'support';
UPDATE "Queues" SET "AdHocClosed" = false WHERE "Name" = 'support';

-- ad-hoc sluiting mét doorschakeling naar een extern nummer
UPDATE "Queues" SET "AdHocClosed" = true, "AdHocForwardNumber" = '+31201234567'
WHERE "Name" = 'support';

-- openingstijden: bv. doordeweeks 09:00-17:00 (dag: 0=zondag ... 6=zaterdag)
DELETE FROM "OpeningHoursWindow" WHERE "QueueConfigId" = 1;
INSERT INTO "OpeningHoursWindow" ("QueueConfigId", "Day", "Opens", "Closes")
SELECT 1, d, '09:00', '17:00' FROM generate_series(1, 5) AS d;

-- extra nummer naar een wachtrij routeren
INSERT INTO "InboundNumbers" ("Number", "QueueConfigId") VALUES ('+31858001234', 1);
```

Aandachtspunten:

- **Nieuwe wachtrij** = rij in `Queues` (naam in `[a-z0-9]`); de holding-brug en wachtmuziek worden door de backend aangemaakt. Wijs agents toe via `AgentQueueAssignment`.
- **Doorschakelen** belt uit via de trunk en vereist een uitgaande route op de SBC (Asterisk IP Group → Twilio); die bestaat nog niet, dus dit pad is nog ongetest.
- Onbekend nummer → melding "not in service" + ophangen. Database onbereikbaar → terugval: alles naar `support` met de standaard-welkomsttekst.

## Agents en nawerktijd

Agents staan in de database (`Agents`, met wachtrij-toewijzingen in `AgentQueueAssignment`; geseed: `agent1001` in alle wachtrijen, `agent1002` in support). De agent-pagina meldt na de SIP-registratie de agent aan via de API; de backend ziet hem dan als beschikbaar voor zijn wachtrijen. Statusverloop per agent: afgemeld → beschikbaar → rinkelt → in gesprek → **nawerktijd** → beschikbaar.

- Een wachtende beller wordt door de backend toegewezen aan een beschikbare agent: die wordt gebeld (de browser rinkelt), en bij opnemen komen beller en agent samen in een mixing-brug.
- Nawerktijd is globaal instelbaar (`Settings.WrapUpSeconds`, geseed op 30; `0` = uit) en gaat per direct in:
  ```sql
  UPDATE "Settings" SET "WrapUpSeconds" = 60;
  ```
- Tijdens nawerktijd is de agent niet kiesbaar voor nieuwe gesprekken; die blijven in de wacht. De "Klaar"-knop op de agent-pagina (of het verstrijken van de timer) maakt de agent weer beschikbaar.
- Agent-API (nog zonder authenticatie): `GET /api/agents`, `GET/POST /api/agents/{naam}` + `/login`, `/logout`, `/wrapup/finish`.
- Agentstatus leeft in-memory: backend-herstart = iedereen afgemeld (opnieuw aanmelden in de agent-pagina).

## Dev-wachtwoorden

`changeme-dev` staat in `infra/asterisk/conf/ari.conf`, `pjsip.conf`, `infra/docker-compose.yml` (postgres) en `backend/.../appsettings.json` — alleen voor de POC, vervangen zodra dit een omgeving met echte nummers wordt.

## Bewuste POC-beperkingen

- Wachtrijpositie-meldingen (kwamen van `app_queue`) zijn er tijdelijk uit door de overstap naar de server-side wachtrij; de backend kent de wachtlijst, dus ze komen later eenvoudig terug.
- Twee vaste SIP-accounts (`agent1001`/`agent1002`); Engelstalige standaardprompts (eigen NL-teksten volgen met de beheeromgeving).
- Agent-API zonder authenticatie en agentstatus in-memory; Keycloak en SignalR-push komen met de React-apps.
- Doorverbinden (warm/koud) door de agent is de volgende fase.
- `ws://` en dev-wachtwoorden; TLS/wss en TURN (coturn, voor thuiswerkers) volgen na de POC.
