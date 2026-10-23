FROM mcr.microsoft.com/dotnet/aspnet:10.0

USER root
ENV container=docker

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        ca-certificates \
        curl \
        dbus \
        libpam-modules \
        passwd \
        procps \
        systemd-sysv \
        tzdata \
        util-linux \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system serval-admin \
    && useradd --system --user-group --no-create-home --home-dir /nonexistent \
        --shell /usr/sbin/nologin serval-web \
    && useradd --system --user-group --no-create-home --home-dir /nonexistent \
        --shell /usr/sbin/nologin serval-demo \
    && useradd --create-home --shell /bin/sh --groups serval-admin serval-dev \
    && printf 'serval-dev:serval\n' | chpasswd \
    && install -d -o root -g root -m 0755 \
        /etc/serval \
        /opt/serval/agent \
        /opt/serval/web \
        /opt/serval/development \
    && install -d -o root -g serval-web -m 0750 /run/serval \
    && install -d -o root -g root -m 0700 /var/lib/serval \
    && install -d -o root -g root -m 0700 /var/lib/serval-development \
    && install -d -o serval-web -g serval-web -m 0700 /var/lib/serval-web

COPY agent/ /opt/serval/agent/
COPY web/ /opt/serval/web/
COPY serval.pam /etc/pam.d/serval
COPY start-agent.sh /opt/serval/development/start-agent.sh
COPY start-web.sh /opt/serval/development/start-web.sh
COPY start-debugger.sh /opt/serval/development/start-debugger.sh
COPY stop-web.sh /opt/serval/development/stop-web.sh
COPY initialize-demos.sh serval-demo-running.service serval-demo-inactive.service serval-demo-failed.service /opt/serval/development/
COPY serval-agent.service /etc/systemd/system/serval-agent.service
COPY verify-development-host.sh /opt/serval/development/verify-development-host.sh

RUN printf '{"webUid":%s,"webGid":%s,"adminGroup":"serval-admin","adminGid":%s,"idleMinutes":15}\n' \
        "$(id -u serval-web)" "$(id -g serval-web)" "$(getent group serval-admin | cut -d: -f3)" \
        > /etc/serval/agent.json \
    && chown -R root:root /opt/serval /etc/serval /etc/pam.d/serval \
    && chmod 0600 /etc/serval/agent.json \
    && chmod 0644 /etc/pam.d/serval \
    && chmod -R go-w /opt/serval \
    && chmod 0755 /opt/serval/agent/Serval.Agent \
        /opt/serval/development/start-agent.sh \
        /opt/serval/development/start-web.sh \
        /opt/serval/development/start-debugger.sh \
        /opt/serval/development/stop-web.sh \
        /opt/serval/development/initialize-demos.sh \
        /opt/serval/development/verify-development-host.sh \
    && test -f /opt/serval/web/Serval.Web.dll

HEALTHCHECK --interval=2s --timeout=20s --start-period=10s --retries=45 \
    CMD /opt/serval/development/verify-development-host.sh --health || exit 1

EXPOSE 8080
WORKDIR /opt/serval/web
STOPSIGNAL SIGRTMIN+3
ENTRYPOINT ["/sbin/init"]
