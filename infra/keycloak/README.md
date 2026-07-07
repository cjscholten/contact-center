# Keycloak (OIDC) — productie

Keycloak draait in `docker-compose.yml` op de VM in **productie-modus** (`start`) op
poort **8080**, achter Caddy die de TLS termineert. De browser bereikt Keycloak via
`https://auth.<CC_DOMAIN>`; de backend haalt de OIDC-metadata intern op `localhost:8080` op.

Kenmerken (custom image, `Dockerfile` + `cc-entrypoint.sh`):

- **Persistente Postgres-database** `keycloak` (aparte rol, gemaakt door de
  `keycloak-db-init`-service). Geen ephemeral H2 meer.
- **Seed-wachtwoorden niet in git**: de realm-JSON's bevatten `${KEYCLOAK_SEED_PASSWORD}`;
  het entrypoint vult die bij start uit `infra/.env` (envsubst, zoals Asterisk).
- **Brute-force-detectie** aan (per realm), **`sslRequired: external`**, en
  **`directAccessGrantsEnabled: false`** — de ROPC-token-flow (wachtwoord-grant zonder
  browser) staat uit; login gaat via auth-code + PKCE.

> Belangrijk (gewijzigd t.o.v. dev-mode): door de **persistente DB** worden de realm-JSON's
> alleen bij de **eerste** start geïmporteerd. Latere JSON-wijzigingen komen er niet vanzelf in.
> Opnieuw toepassen: `docker compose exec keycloak /opt/keycloak/bin/kc.sh import \
> --dir /opt/keycloak/data/import --override true`, of de realm in de console aanpassen,
> of (POC) de `keycloak`-database droppen en Keycloak herstarten.

## Eenmalig: NSG-regel

Poort **8080 hoort niet publiek** te staan — de browser gaat via `auth.<CC_DOMAIN>` (Caddy, 443).
Beperk inbound TCP 8080 tot localhost/Caddy; open het hooguit tijdelijk vanaf je dev-IP.

## Starten

```bash
# op de VM, in de map infra/  (na het invullen van infra/.env — zie .env.example)
docker compose up -d --build keycloak    # bouwt de custom image + start db-init + keycloak
```

- Admin-console: `https://auth.<CC_DOMAIN>/` — `admin` / `${KEYCLOAK_ADMIN_PASSWORD}`
- Issuer (browser): `https://auth.<CC_DOMAIN>/realms/contactcenter`

## Wachtwoorden roteren

De fixture-gebruikers zijn POC-testaccounts. Roteer bij een echte uitrol:

1. Zet sterke, unieke waarden in `infra/.env` voor `KEYCLOAK_ADMIN_PASSWORD`,
   `KEYCLOAK_DB_PASSWORD` en `KEYCLOAK_SEED_PASSWORD`.
2. Bestaat de `keycloak`-DB al met een oude admin/seed? Dan tellen `.env`-wijzigingen pas
   na een verse import (zie de import-noot hierboven) of pas je ze aan in de console.
3. Voor echte gebruikers: schakel over op **Entra ID**-federatie (zie hieronder) i.p.v.
   lokale wachtwoorden.

## Multi-tenant: realm per klant

Elke klant heeft een **eigen realm** met een eigen Microsoft Entra ID als gekoppelde
identity provider. De backend leidt de tenant af uit de token-issuer (de realm) en scheidt
alle data op `TenantId`. Twee realms worden bij start geïmporteerd:

| Tenant (slug) | Realm | Gebruikers (lokaal, dev) |
|---------------|-------|--------------------------|
| `default`     | `contactcenter` | `agent1001`, `agent1002` (agent), `beheerder` (admin) |
| `acme`        | `tenant-acme`   | `agent2001` (agent), `beheerder` (admin) |

Wachtwoord bij de eerste import = `KEYCLOAK_SEED_PASSWORD` (uit `infra/.env`). Clients in elke
realm: `zetadesk` (→ `localhost:5173`), `zetabeheer` (→ `localhost:5174`), publiek + PKCE (S256).
Rollen: `agent`, `admin`.

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

Het script leest het admin-wachtwoord uit de omgeving (`KC_ADMIN_PW`, = `KEYCLOAK_ADMIN_PASSWORD`).
Geen herstart nodig: de backend herlaadt de tenant-registry bij start; voor een live
toegevoegde tenant kun je de backend herstarten of de registry-reload triggeren.

> De *direct access grants* (ROPC / wachtwoord-grant zonder browser) staan bewust **uit**.
> Een token haal je op via de normale auth-code + PKCE-flow in de app; er is geen
> `grant_type=password`-pad meer (dat was de bypass uit finding K-1).
