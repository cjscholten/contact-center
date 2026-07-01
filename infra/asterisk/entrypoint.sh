#!/bin/sh
# Vult de config-sjablonen (ari.conf, pjsip.conf) bij container-start in met secrets/host-IP's
# uit de omgeving (docker-compose → infra/.env), zodat die niet in git staan. Daarna start
# Asterisk via de meegegeven CMD.
set -e

: "${ARI_PASSWORD:?ARI_PASSWORD ontbreekt — zet infra/.env (zie infra/.env.example)}"
: "${AGENT_SIP_PASSWORD:?AGENT_SIP_PASSWORD ontbreekt — zet infra/.env (zie infra/.env.example)}"
: "${SBC_IP:?SBC_IP ontbreekt — zet infra/.env (zie infra/.env.example)}"

for f in ari.conf pjsip.conf; do
  # Alleen deze drie variabelen vervangen; overige $-tekens in de config blijven ongemoeid.
  envsubst '${ARI_PASSWORD} ${AGENT_SIP_PASSWORD} ${SBC_IP}' \
    < "/etc/asterisk/templates/$f" > "/etc/asterisk/$f"
done

exec "$@"
