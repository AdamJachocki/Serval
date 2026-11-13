# Minimal systemd host for package lifecycle tests. Only systemd itself is installed: the serval
# package must pull in everything else it needs through its declared dependencies.
#   docker build --build-arg BASE=debian:13-slim -f tests/packaging/systemd.Dockerfile -t serval-systemd-test tests/packaging
ARG BASE=debian:13-slim
FROM ${BASE}

ENV container=docker \
    DEBIAN_FRONTEND=noninteractive

RUN apt-get update \
    && apt-get install --yes --no-install-recommends systemd systemd-sysv dbus \
    && rm -rf /var/lib/apt/lists/*

STOPSIGNAL SIGRTMIN+3

CMD ["/sbin/init"]
