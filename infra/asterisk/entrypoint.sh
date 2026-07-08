#!/bin/sh
# Vult de config-sjablonen (ari.conf, pjsip.conf) bij container-start in met secrets/host-IP's
# uit de omgeving (docker-compose → infra/.env), zodat die niet in git staan. Daarna start
# Asterisk via de meegegeven CMD.
set -e

: "${ARI_PASSWORD:?ARI_PASSWORD ontbreekt — zet infra/.env (zie infra/.env.example)}"
# H-4: per-agent SIP-wachtwoorden (elk uniek) i.p.v. één gedeeld AGENT_SIP_PASSWORD.
: "${AGENT1001_SIP_PASSWORD:?AGENT1001_SIP_PASSWORD ontbreekt — zet infra/.env (zie infra/.env.example)}"
: "${AGENT1002_SIP_PASSWORD:?AGENT1002_SIP_PASSWORD ontbreekt — zet infra/.env (zie infra/.env.example)}"
: "${SBC_IP:?SBC_IP ontbreekt — zet infra/.env (zie infra/.env.example)}"

for f in ari.conf pjsip.conf; do
  # Alleen deze variabelen vervangen; overige $-tekens in de config blijven ongemoeid.
  envsubst '${ARI_PASSWORD} ${AGENT1001_SIP_PASSWORD} ${AGENT1002_SIP_PASSWORD} ${SBC_IP}' \
    < "/etc/asterisk/templates/$f" > "/etc/asterisk/$f"
done

# rtp.conf: statische basis uit het sjabloon; de ICE-NAT-mapping (privé => publiek IP) wordt
# alleen toegevoegd als beide RTP-variabelen gezet zijn (nodig bij 1:1 NAT, bv. de Azure-VM).
# Altijd vers uit het sjabloon renderen houdt dit idempotent (geen dubbele secties bij restart).
cp /etc/asterisk/templates/rtp.conf /etc/asterisk/rtp.conf
if [ -n "${RTP_PRIVATE_IP:-}" ] && [ -n "${RTP_PUBLIC_IP:-}" ]; then
  printf '\n[ice_host_candidates]\n%s => %s\n' "$RTP_PRIVATE_IP" "$RTP_PUBLIC_IP" >> /etc/asterisk/rtp.conf
fi

# 5060/SIP-hardening (defense-in-depth): een bron-IP-ACL toevoegen als SIP_ACL_PERMIT gezet is
# (comma-gescheiden CIDR's). Denk aan localhost (WS via de reverse proxy) + de VNet/SBC. De
# primaire poort-gate blijft de NSG. LET OP: een globale PJSIP-ACL geldt óók voor de WS-agents,
# dus zet dit pas aan als WS via Caddy (localhost) loopt — anders worden directe ws://-agents
# geweigerd. Idempotent: pjsip.conf is hierboven vers gerenderd, dus deze append gebeurt één keer.
if [ -n "${SIP_ACL_PERMIT:-}" ]; then
  {
    printf '\n[acl]\ntype=acl\ndeny=0.0.0.0/0\n'
    echo "$SIP_ACL_PERMIT" | tr ',' '\n' | while read -r cidr; do
      cidr=$(echo "$cidr" | tr -d ' ')
      [ -n "$cidr" ] && printf 'permit=%s\n' "$cidr"
    done
  } >> /etc/asterisk/pjsip.conf
fi

exec "$@"
