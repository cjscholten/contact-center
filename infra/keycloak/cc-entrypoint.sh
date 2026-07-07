#!/usr/bin/bash
# Rendert de realm-templates (met ${KEYCLOAK_SEED_PASSWORD}-placeholder) bij container-start naar
# de import-map, zodat de fixture-wachtwoorden uit infra/.env komen en niet in git staan. De
# Keycloak-image heeft geen envsubst/gettext; we gebruiken bash-parameter-expansie — die is veilig
# tegen alle tekens in het wachtwoord (geen sed-escaping nodig). Daarna start Keycloak via de CMD.
set -euo pipefail

: "${KEYCLOAK_SEED_PASSWORD:?KEYCLOAK_SEED_PASSWORD ontbreekt — zet infra/.env (zie infra/.env.example)}"

template_dir=/opt/keycloak/data/import-templates
import_dir=/opt/keycloak/data/import
search='${KEYCLOAK_SEED_PASSWORD}'

# Idempotent: bij elke start vers renderen. Alleen de placeholder wordt vervangen.
mkdir -p "$import_dir"
shopt -s nullglob
for f in "$template_dir"/*.json; do
  content="$(< "$f")"
  printf '%s' "${content//"$search"/$KEYCLOAK_SEED_PASSWORD}" > "$import_dir/${f##*/}"
done

exec /opt/keycloak/bin/kc.sh "$@"
