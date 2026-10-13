FROM mcr.microsoft.com/dotnet/aspnet:10.0

USER root

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        ca-certificates \
        curl \
        libpam-modules \
        passwd \
        procps \
        tzdata \
        util-linux \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system serval-admin \
    && useradd --system --user-group --no-create-home --home-dir /nonexistent \
        --shell /usr/sbin/nologin serval-web \
    && useradd --create-home --shell /bin/sh serval-dev \
    && printf 'serval-dev:serval\n' | chpasswd \
    && install -d -o root -g root -m 0755 \
        /etc/serval \
        /opt/serval/agent \
        /opt/serval/web \
        /opt/serval/development \
    && install -d -o root -g serval-web -m 0750 /run/serval \
    && install -d -o root -g root -m 0700 /var/lib/serval \
    && install -d -o serval-web -g serval-web -m 0700 /var/lib/serval-web

COPY agent/ /opt/serval/agent/
COPY web/ /opt/serval/web/
COPY serval.pam /etc/pam.d/serval
COPY start-agent.sh /opt/serval/development/start-agent.sh
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
        /opt/serval/development/verify-development-host.sh \
    && test -f /opt/serval/web/Serval.Web.dll

HEALTHCHECK --interval=2s --timeout=7s --start-period=10s --retries=45 \
    CMD /opt/serval/development/verify-development-host.sh --health || exit 1

EXPOSE 8080
WORKDIR /opt/serval/web
