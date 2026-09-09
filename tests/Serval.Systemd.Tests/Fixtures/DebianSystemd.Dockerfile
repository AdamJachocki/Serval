FROM debian:13-slim

ENV container=docker

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        ca-certificates \
        dbus \
        libgcc-s1 \
        libgssapi-krb5-2 \
        libicu76 \
        libssl3t64 \
        libstdc++6 \
        systemd-sysv \
        tzdata \
        zlib1g \
    && rm -rf /var/lib/apt/lists/*

STOPSIGNAL SIGRTMIN+3

CMD ["/sbin/init"]
