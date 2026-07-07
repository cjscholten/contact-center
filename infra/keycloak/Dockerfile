# Custom Keycloak-image: officiële Keycloak in PRODUCTIE-modus, vooraf gebouwd voor Postgres
# (kc.sh build → snelle, deterministische 'start --optimized'), plus een entrypoint dat de
# seed-wachtwoorden bij start uit de omgeving in de realm-JSON's vult — zodat er geen credentials
# in git staan. De Keycloak-image (ubi-micro) heeft geen package-manager/envsubst; het entrypoint
# gebruikt daarom alleen bash-builtins (bash + mkdir zitten wél in de image).

FROM quay.io/keycloak/keycloak:26.1 AS builder
# DB-vendor is een build-time optie; vastzetten zodat 'start --optimized' niet opnieuw hoeft te bouwen.
ENV KC_DB=postgres
RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/keycloak/keycloak:26.1
COPY --from=builder /opt/keycloak/ /opt/keycloak/
COPY cc-entrypoint.sh /opt/keycloak/cc-entrypoint.sh
USER root
RUN chmod +x /opt/keycloak/cc-entrypoint.sh \
    && mkdir -p /opt/keycloak/data/import-templates /opt/keycloak/data/import \
    && chown -R 1000:0 /opt/keycloak/data
USER 1000

# Het entrypoint rendert de realm-templates en start daarna kc.sh met de meegegeven CMD-args.
ENTRYPOINT ["/opt/keycloak/cc-entrypoint.sh"]
CMD ["start", "--optimized", "--import-realm"]
