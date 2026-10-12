ARG BASE_IMAGE
FROM ${BASE_IMAGE}

RUN apt-get update \
    && apt-get install --yes --no-install-recommends passwd libpam-modules \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system serval-admin \
    && useradd --system --user-group --no-create-home --home-dir /nonexistent \
        --shell /usr/sbin/nologin serval-web \
    && install -d -o root -g root -m 0755 /etc/serval \
    && install -d -o root -g root -m 0700 /var/lib/serval \
    && install -d -o root -g root -m 0755 /opt/serval/agent /opt/serval/fixture

COPY agent/ /opt/serval/agent/
COPY client/ /opt/serval/fixture/
COPY serval.pam /etc/pam.d/serval
COPY serval-agent.service /etc/systemd/system/serval-agent.service
COPY verify-docker-agent.sh /opt/serval/fixture/verify-docker-agent.sh

RUN printf '{"webUid":%s,"webGid":%s,"adminGroup":"serval-admin","adminGid":%s,"idleMinutes":15}\n' \
        "$(id -u serval-web)" "$(id -g serval-web)" "$(getent group serval-admin | cut -d: -f3)" \
        > /etc/serval/agent.json \
    && chown -R root:root /opt/serval /etc/serval /etc/pam.d/serval /etc/systemd/system/serval-agent.service \
    && chmod 0600 /etc/serval/agent.json \
    && chmod 0644 /etc/pam.d/serval /etc/systemd/system/serval-agent.service \
    && chmod -R go-w /opt/serval \
    && chmod 0755 /opt/serval/agent/Serval.Agent /opt/serval/fixture/Serval.Agent.FixtureClient \
        /opt/serval/fixture/verify-docker-agent.sh \
    && systemctl enable serval-agent.service
