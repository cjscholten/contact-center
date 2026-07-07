#!/usr/bin/env bash
# Maakt een nieuwe klant-realm aan uit realm-template.json en koppelt Microsoft Entra ID.
# Idempotent: bestaat de realm al, dan wordt 'm bijgewerkt (kcadm create faalt → we doen update).
#
# Vereist: de Keycloak-container (cc-keycloak) draait. Draai dit from de map infra/.
#
# Gebruik:
#   ./keycloak/provision-tenant.sh \
#       --slug acme --realm tenant-acme --display "Acme BV" \
#       --base-url https://acme.example.com \
#       --entra-tenant <ENTRA_TENANT_ID> \
#       --entra-client <ENTRA_CLIENT_ID> \
#       --entra-secret <ENTRA_CLIENT_SECRET>
#
# Na afloop: registreer in Entra de redirect-URI
#   http://<keycloak-host>:8080/realms/<realm>/broker/entra/endpoint
# en voeg de tenant toe aan de backend (tabel "Tenants") — zie --register-db hieronder.
set -euo pipefail

KC_CONTAINER="${KC_CONTAINER:-cc-keycloak}"
KC_URL="${KC_URL:-http://localhost:8080}"
KC_ADMIN="${KC_ADMIN:-admin}"
# Geen default-wachtwoord in git: zet KC_ADMIN_PW in de omgeving (= KEYCLOAK_ADMIN_PASSWORD uit infra/.env).
KC_ADMIN_PW="${KC_ADMIN_PW:?zet KC_ADMIN_PW in de omgeving (het Keycloak-admin-wachtwoord)}"
PG_CONTAINER="${PG_CONTAINER:-cc-postgres}"

SLUG="" REALM="" DISPLAY="" BASE_URL=""
ENTRA_TENANT="__ENTRA_TENANT_ID__" ENTRA_CLIENT="__ENTRA_CLIENT_ID__" ENTRA_SECRET="changeme"
REGISTER_DB=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --slug) SLUG="$2"; shift 2;;
    --realm) REALM="$2"; shift 2;;
    --display) DISPLAY="$2"; shift 2;;
    --base-url) BASE_URL="$2"; shift 2;;
    --entra-tenant) ENTRA_TENANT="$2"; shift 2;;
    --entra-client) ENTRA_CLIENT="$2"; shift 2;;
    --entra-secret) ENTRA_SECRET="$2"; shift 2;;
    --register-db) REGISTER_DB=1; shift;;
    *) echo "Onbekende optie: $1" >&2; exit 1;;
  esac
done

[[ -z "$SLUG" || -z "$REALM" || -z "$DISPLAY" || -z "$BASE_URL" ]] && {
  echo "Vereist: --slug --realm --display --base-url" >&2; exit 1; }

TEMPLATE="$(dirname "$0")/realm-template.json"
WORK="$(mktemp)"
trap 'rm -f "$WORK"' EXIT

# Placeholders vervangen. ZetaDesk en ZetaBeheer draaien onder hetzelfde domein (subpaden of
# subdomeinen); pas de redirect-URI's aan jouw hosting aan.
sed \
  -e "s#__REALM__#${REALM}#g" \
  -e "s#__ZETADESK_REDIRECT__#${BASE_URL}/*#g" \
  -e "s#__ZETADESK_ORIGIN__#${BASE_URL}#g" \
  -e "s#__ZETABEHEER_REDIRECT__#${BASE_URL}/*#g" \
  -e "s#__ZETABEHEER_ORIGIN__#${BASE_URL}#g" \
  -e "s#__ENTRA_TENANT_ID__#${ENTRA_TENANT}#g" \
  -e "s#__ENTRA_CLIENT_ID__#${ENTRA_CLIENT}#g" \
  -e "s#__ENTRA_CLIENT_SECRET__#${ENTRA_SECRET}#g" \
  "$TEMPLATE" > "$WORK"

echo "→ Realm '${REALM}' importeren in Keycloak ($KC_URL)…"
docker cp "$WORK" "${KC_CONTAINER}:/tmp/realm.json"
docker exec "$KC_CONTAINER" /opt/keycloak/bin/kcadm.sh config credentials \
  --server "$KC_URL" --realm master --user "$KC_ADMIN" --password "$KC_ADMIN_PW"
docker exec "$KC_CONTAINER" /opt/keycloak/bin/kcadm.sh create realms -f /tmp/realm.json \
  || docker exec "$KC_CONTAINER" /opt/keycloak/bin/kcadm.sh update "realms/${REALM}" -f /tmp/realm.json

if [[ "$REGISTER_DB" == "1" ]]; then
  echo "→ Tenant '${SLUG}' registreren in de database (tabel Tenants)…"
  docker exec -i "$PG_CONTAINER" psql -U cc -d contactcenter -v ON_ERROR_STOP=1 <<SQL
    INSERT INTO "Tenants" ("Slug", "DisplayName", "Realm", "Enabled")
    SELECT '${SLUG}', '${DISPLAY}', '${REALM}', true
    WHERE NOT EXISTS (SELECT 1 FROM "Tenants" WHERE "Slug" = '${SLUG}');
SQL
fi

cat <<DONE

Klaar. Realm '${REALM}' is aangemaakt/bijgewerkt.

Nog te doen in Azure (Entra ID App Registration):
  • Redirect URI (Web): ${KC_URL}/realms/${REALM}/broker/entra/endpoint
  • Client-secret invullen via --entra-secret (nu: ${ENTRA_SECRET:0:3}…)

Front-end gebruikt voor deze tenant: ?tenant=${SLUG}  (realm ${REALM})
DONE
