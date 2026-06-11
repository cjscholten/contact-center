FROM debian:12-slim

RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        asterisk \
        asterisk-core-sounds-en-gsm \
        asterisk-moh-opsound-gsm \
    && rm -rf /var/lib/apt/lists/*

COPY conf/*.conf /etc/asterisk/

EXPOSE 5060/udp 8088/tcp 10000-10100/udp

CMD ["asterisk", "-f", "-vvv"]
