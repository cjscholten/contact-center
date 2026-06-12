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

- **Asterisk doet media, de backend beslist.** Inkomende gesprekken landen in een ARI Stasis-app (`contactcenter`); de backend speelt de welkomsttekst af en stuurt het gesprek naar een `app_queue`-wachtrij.
- Agents bellen via de browser (WebRTC, SIP.js) rechtstreeks met Asterisk.
- Wachtrijmechanica (wachtmuziek, positie-meldingen, agentselectie) komt van `app_queue`; openingstijden, teksten, ad-hoc sluiting en nawerktijd worden backend-logica.

## Mappen

| Map | Inhoud |
|---|---|
| `backend/` | ASP.NET Core backend met eigen dunne ARI-client |
| `infra/` | Dockerfile + configuratie voor Asterisk (Ubuntu 24.04, Asterisk 20) |
| `poc-agent/` | Kale browser-agent (statisch HTML + SIP.js) voor de POC |

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
| 22/tcp | jouw publieke IP | beheer |

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
dotnet run --project src/ContactCenter.Api
```

Draait de backend op je dev-machine en Asterisk in Azure? Zet dan `Ari:BaseUrl` in `src/ContactCenter.Api/appsettings.json` op `http://<publiek-IP-VM>:8088/ari/`. Log toont "Verbonden met ARI-events" als de koppeling staat. Healthcheck: `http://localhost:5080/health`.

Let op: ARI gaat dan met basic auth over onversleuteld http het internet over. Voor de POC acceptabel zolang 8088 in de NSG op jouw IP staat dichtgetimmerd; daarna backend in Azure draaien of TLS op de Asterisk-HTTP-server zetten.

### 4. Agent verbinden

```powershell
cd poc-agent
npx serve .
```

Open de pagina via `http://localhost:…` (microfoontoegang vereist localhost of https), vul de Asterisk-host in en klik Verbinden. De POC gebruikt `ws://` op poort 8088; dat werkt alleen vanaf een http-pagina. Voor productie komt hier wss met echte certificaten.

### 5. Testgesprek

Bel een testnummer van de trunk. Verwacht: welkomsttekst ("thank you for your patience"), wachtmuziek, agent-pagina toont "inkomend gesprek", aannemen → spraakverbinding.

## Dev-wachtwoorden

`changeme-dev` staat in `infra/asterisk/conf/ari.conf`, `pjsip.conf` en `backend/.../appsettings.json` — alleen voor de POC, vervangen zodra dit een omgeving met echte nummers wordt.

## Bewuste POC-beperkingen

- Eén vaste wachtrij (`support`) met één statisch agent-account; Engelstalige standaardprompts.
- Geen openingstijden, nawerktijd, doorverbinden of login — dat is de volgende fase (PostgreSQL-config, agent-statusmachine, Keycloak, React-apps).
- `ws://` en dev-wachtwoorden; TLS/wss en TURN (coturn, voor thuiswerkers) volgen na de POC.
