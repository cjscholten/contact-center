# Ubuntu als basis: Asterisk is uit de stabiele Debian-releases verwijderd,
# Ubuntu 24.04 LTS levert Asterisk 20 (LTS) uit universe.
FROM ubuntu:24.04

RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        asterisk \
        asterisk-core-sounds-en-gsm \
        asterisk-moh-opsound-gsm \
        sox \
        gettext-base \
    && rm -rf /var/lib/apt/lists/*

# Statische configs direct; de getemplatete (ari.conf, pjsip.conf) komen als sjabloon binnen
# en worden bij container-start door entrypoint.sh ingevuld met secrets uit de omgeving.
COPY conf/*.conf /etc/asterisk/
COPY templates/ /etc/asterisk/templates/
COPY entrypoint.sh /usr/local/bin/cc-entrypoint.sh
RUN chmod +x /usr/local/bin/cc-entrypoint.sh

# Demo-wachtmuziekklasse 'office' (rechtenvrij, met sox gegenereerd) zodat wachtmuziek
# per wachtrij meteen aantoonbaar is. De pakket-[default]-klasse (opsound) blijft staan;
# we appenden alleen. Eigen muziek per klasse komt later via het gedeelde sounds-volume.
RUN mkdir -p /var/lib/asterisk/moh-office \
    && sox -n -r 8000 -c 1 -b 16 /var/lib/asterisk/moh-office/hold.wav \
        synth 30 sine 392 sine 494 sine 587 gain -14 tremolo 3 20 fade t 2 30 3 \
    && chown -R asterisk:asterisk /var/lib/asterisk/moh-office \
    && printf '\n[office]\nmode=files\ndirectory=/var/lib/asterisk/moh-office\n' >> /etc/asterisk/musiconhold.conf

EXPOSE 5060/udp 8088/tcp 10000-10100/udp

ENTRYPOINT ["/usr/local/bin/cc-entrypoint.sh"]
CMD ["asterisk", "-f", "-vvv"]
