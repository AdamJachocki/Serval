# Proposal

## Why

Installing Serval today is a manual, multi-step procedure: publish both
processes for the host architecture, copy and lock down the binary trees, run
`deploy/install-identity.sh`, configure HTTPS and the Web listener, then start
the units in order (see `docs/identity-deployment.md`). Upgrades additionally
require a manual stop, a consistent `policy.db` backup and matching binaries.
This is too much for the target user, a host administrator who expects to
install and update a tool through the distribution's package manager with one
or two shell commands and no prerequisite runtime.

## What Changes

- Serval is distributed as a single `serval` Debian package (`.deb`) for
  `amd64` and `arm64`, containing self-contained Agent and Web builds. The host
  does not need .NET installed.
- Supported targets are the releases the repository already verifies against
  real systemd: Debian 13 and Ubuntu 22.04, 24.04 and 26.04.
- The package's maintainer scripts take over `deploy/install-identity.sh`:
  they create the `serval-web` identity, install the PAM policy and units,
  generate `/etc/serval/agent.json` with a default host-admin group of `sudo`
  (a debconf question, also preseedable), and start Agent before Web.
- Upgrades stop Web and then Agent, keep a root-only backup of `policy.db`
  before new binaries run, and restart Agent before Web. Agent and Web always
  come from the same package version.
- `apt remove` stops and disables both services. `apt purge` also removes
  Serval configuration but preserves the grants and audit database, consistent
  with audit retention being an operator task.
- After installation, Web listens only on `127.0.0.1` over HTTP. Exposing it to
  the network is a deliberate step through a new root-only `serval-setup`
  command, which requires TLS (a generated self-signed certificate or an
  operator-supplied certificate) for any non-loopback address. Web refuses to
  start with a non-loopback HTTP listener.
- `serval-setup` can also change the host-admin group after installation.
- A signed APT repository and an `install.sh` bootstrap give the one-line
  install: the script detects a supported release, installs the repository's
  signing key with `signed-by`, adds the source and runs `apt-get install
  serval`. Each release also attaches the `.deb` files to its GitHub Release.
- The release process rebuilds and republishes the package for each .NET
  servicing release that contains security fixes, because the runtime is
  bundled.

### Non-goals

- No `.rpm`, RHEL-family PAM policy, Snap, Flatpak or container distribution.
- No Debian 12 support in this change; it is not covered by the repository's
  real-systemd CI.
- No automatic public-CA certificates (for example Caddy with Let's Encrypt)
  and no reverse-proxy configuration. These may follow later.
- No NativeAOT build of Agent. It is a separate investigation.
- No new Agent IPC operation and no Web-initiated host configuration.
  `serval-setup` is a local root command only.
- No submission to the official Debian or Ubuntu archives.

## Capabilities

### New Capabilities
- `deployment/debian-package`: the `serval` package contract: supported
  platforms, bundled runtime, install-time identity and configuration, file
  ownership, service ordering, upgrade with backup, remove and purge.
- `deployment/package-distribution`: the signed APT repository, the
  `install.sh` bootstrap and release assets, including the servicing-release
  obligation.
- `deployment/host-setup`: the `serval-setup` command for exposing Web with TLS
  and changing the host-admin group.
- `web/network-listener`: Web listener configuration, its loopback HTTP
  default and the rule that non-loopback listeners require TLS.

### Modified Capabilities
- None. The existing host-administrator bootstrap requirement in
  `authorization/system-service-access` already requires installation to
  select an existing group; the package satisfies it with a default instead of
  changing it.

## Impact

- **Packaging (new)**: package layout, maintainer scripts and debconf
  templates, `serval-setup`, `install.sh`, and build scripts producing the
  `.deb` and the APT repository metadata.
- **deploy/**: `install-identity.sh` and `validate-web-identity.sh` logic moves
  into the package or is shared with it; the unit files gain only what packaging
  requires. Unit names stay `serval-agent.service` and `serval-web.service`,
  which the privileged-unit exclusion depends on.
- **Serval.Web**: reads listener and TLS settings from a root-owned
  `/etc/serval/web.json` and refuses a non-loopback listener without TLS.
- **Serval.Agent**: no new capability. Its startup validation of
  `/etc/serval/agent.json`, PAM, runtime and policy paths must accept the
  package-created state unchanged.
- **CI and release**: package build and lint, install/upgrade/remove/purge
  tests on every supported release and architecture against real systemd, and
  a tag-triggered release workflow that signs the repository. The signing key is
  a new release secret.
- **Security**: the package runs root maintainer scripts and ships a root
  service; `serval-setup` is a new root-only local tool; the Web listener
  default narrows network exposure. `SECURITY.md` gains the supported-versions
  and update-channel statement.
- **Docs**: `docs/identity-deployment.md` becomes package-first, a new
  packaging and release document, `README.md` install instructions,
  `docs/code-map.md`.
