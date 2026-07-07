# Custom Keycloak-image: officiële Keycloak in PRODUCTIE-modus, vooraf gebouwd voor Postgres
# (kc.sh build → snelle, deterministische 'start --optimized'), plus envsubst (gettext) en een
# entrypoint dat de seed-wachtwoorden bij start uit de omgeving in de realm-JSON's vult — zodat
# er geen credentials in git staan (zelfde patroon als infra/asterisk/entrypoint.sh).

FROM quay.io/keycloak/keycloak:26.1 AS builder
# DB-vendor is een build-time optie; vastzetten zodat 'start --optimized' niet opnieuw hoeft te bouwen.
ENV KC_DB=postgres
RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/keycloak/keycloak:26.1
USER root
# gettext levert /usr/bin/envsubst voor de template-substitutie in het entrypoint.
RUN microdnf install -y gettext && microdnf clean all
COPY --from=builder /opt/keycloak/ /opt/keycloak/
COPY cc-entrypoint.sh /opt/keycloak/cc-entrypoint.sh
RUN chmod +x /opt/keycloak/cc-entrypoint.sh \
    && mkdir -p /opt/keycloak/data/import-templates /opt/keycloak/data/import \
    && chown -R 1000:0 /opt/keycloak/data && chmod -R g+rw /opt/keycloak/data
USER 1000

# Het entrypoint rendert de realm-templates en start daarna kc.sh met de meegegeven CMD-args.
ENTRYPOINT ["/opt/keycloak/cc-entrypoint.sh"]
CMD ["start", "--optimized", "--import-realm"]
