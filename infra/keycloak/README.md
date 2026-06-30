# Keycloak (OIDC) — dev

Keycloak draait in `docker-compose.yml` op de VM in **dev-mode** op poort **8080**.
Het realm `contactcenter` wordt bij elke start (her)geïmporteerd uit
`contactcenter-realm.json` — dat bestand is de bron van waarheid. Wijzigingen via
de admin-console overleven een herstart dus niet; pas het JSON aan en herstart.

> Dev-only: http (geen TLS) en een ephemeral H2-database. Niet voor productie.

## Eenmalig: NSG-regel

Open op de VM inbound **TCP 8080** vanaf je dev-IP (zelfde patroon als 8088/5432),
zodat de browser én de backend Keycloak kunnen bereiken.

## Starten

```bash
# op de VM, in de map infra/
docker compose up -d keycloak      # of: docker compose up -d (hele stack)
```

- Admin-console: `http://20.107.0.204:8080/` — `admin` / `changeme-dev`
- Issuer: `http://20.107.0.204:8080/realms/contactcenter`

## Multi-tenant: realm per klant

Elke klant heeft een **eigen realm** met een eigen Microsoft Entra ID als gekoppelde
identity provider. De backend leidt de tenant af uit de token-issuer (de realm) en scheidt
alle data op `TenantId`. Twee realms worden bij start geïmporteerd:

| Tenant (slug) | Realm | Gebruikers (lokaal, dev) |
|---------------|-------|--------------------------|
| `default`     | `contactcenter` | `agent1001`, `agent1002` (agent), `beheerder` (admin) |
| `acme`        | `tenant-acme`   | `agent2001` (agent), `beheerder` (admin) |

Wachtwoord overal `changeme-dev`. Clients in elke realm: `zetadesk` (→ `localhost:5173`),
`zetabeheer` (→ `localhost:5174`), publiek + PKCE (S256). Rollen: `agent`, `admin`.

De front-ends kiezen de realm via de tenant: `?tenant=default` (realm `contactcenter`) of
`?tenant=acme` (realm `tenant-acme`). Conventie: `default` → `contactcenter`, anders
`tenant-<slug>`.

### Nieuwe klant toevoegen

```bash
# in infra/ (Keycloak-container draait)
./keycloak/provision-tenant.sh \
  --slug klantx --realm tenant-klantx --display "Klant X" \
  --base-url https://klantx.example.com \
  --entra-tenant <ENTRA_TENANT_ID> --entra-client <ENTRA_CLIENT_ID> --entra-secret <SECRET> \
  --register-db
```

Dit maakt de realm uit `realm-template.json`, koppelt Entra ID, en registreert de tenant in
de backend-tabel `Tenants` (`--register-db`). **In Azure** registreer je daarna de redirect-URI:

```
http://<keycloak-host>:8080/realms/<realm>/broker/entra/endpoint
```

Geen herstart nodig: de backend herlaadt de tenant-registry bij start; voor een live
toegevoegde tenant kun je de backend herstarten of de registry-reload triggeren.

Beide clients hebben *direct access grants* aan (dev) zodat je een token kunt
ophalen voor het testen van de API:

```bash
curl -s -d grant_type=password -d client_id=zetadesk \
  -d username=agent1001 -d password=changeme-dev \
  http://20.107.0.204:8080/realms/contactcenter/protocol/openid-connect/token
```
