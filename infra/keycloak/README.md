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

## Realm-inhoud

| Wat       | Waarde |
|-----------|--------|
| Realm     | `contactcenter` |
| Clients   | `zetadesk` (→ `http://localhost:5173`), `zetabeheer` (→ `http://localhost:5174`) — publiek, PKCE (S256) |
| Rollen    | `agent`, `admin` |
| Gebruikers | `agent1001`, `agent1002` (rol `agent`), `beheerder` (rol `admin`) — wachtwoord overal `changeme-dev` |

Beide clients hebben *direct access grants* aan (dev) zodat je een token kunt
ophalen voor het testen van de API:

```bash
curl -s -d grant_type=password -d client_id=zetadesk \
  -d username=agent1001 -d password=changeme-dev \
  http://20.107.0.204:8080/realms/contactcenter/protocol/openid-connect/token
```
