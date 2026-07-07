#!/bin/sh
# Rendert de realm-templates (met ${KEYCLOAK_SEED_PASSWORD}-placeholder) bij container-start
# naar de import-map, zodat de fixture-wachtwoorden uit infra/.env komen en niet in git staan.
# Daarna start Keycloak via de meegegeven CMD (bv. 'start --import-realm').
set -e

: "${KEYCLOAK_SEED_PASSWORD:?KEYCLOAK_SEED_PASSWORD ontbreekt — zet infra/.env (zie infra/.env.example)}"

TEMPLATE_DIR=/opt/keycloak/data/import-templates
IMPORT_DIR=/opt/keycloak/data/import

# Idempotent: bij elke start vers renderen. Alleen ${KEYCLOAK_SEED_PASSWORD} vervangen; overige
# ${...} in de JSON (mochten die er ooit komen) blijven ongemoeid.
if [ -d "$TEMPLATE_DIR" ]; then
  for f in "$TEMPLATE_DIR"/*.json; do
    [ -e "$f" ] || continue
    envsubst '${KEYCLOAK_SEED_PASSWORD}' < "$f" > "$IMPORT_DIR/$(basename "$f")"
  done
fi

exec /opt/keycloak/bin/kc.sh "$@"
