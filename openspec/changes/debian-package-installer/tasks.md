# Tasks

## 1. Web network listener (Serval.Web)

- [x] 1.1 Add the Production-only listener configuration reader per D7: safe open of `/etc/serval/web.json` (no symlink, root owner, `serval-web` group, no group/other write), strict deserialization rejecting unknown fields, non-literal addresses, ports outside 1024 to 65535 and TLS paths outside `/etc/serval/tls/`. Verify with Web unit tests for each rejection and for a valid HTTP-loopback and HTTPS configuration.
- [x] 1.2 Configure Kestrel with a single explicit endpoint from that configuration, defaulting to `127.0.0.1:9080` HTTP, refusing a non-loopback address without TLS, and enabling HTTPS redirection and one-year HSTS only with TLS. Verify with tests that `ASPNETCORE_URLS` and a `Kestrel` section are ignored in Production, that startup fails for non-loopback HTTP and for a missing or mismatched key without logging key material, and that HSTS is sent only over TLS.
- [x] 1.3 Keep `Development` and `Testing` behavior unchanged. Verify that the existing Web test suite and the F5 compose smoke test (`eng/visual-studio-compose/test-development-host.ps1`) pass unmodified.
- [x] 1.4 Verify login over loopback HTTP: a Playwright case reaching Web on `http://localhost` in Chromium logs in and receives the `Secure` session cookie. Record Firefox coverage or the reason it is unverified.
- [x] 1.5 Document the listener rule in `ARCHITECTURE.md` (runtime boundaries) and `SECURITY.md`, and the file paths and modes of `web.json` and `/etc/serval/tls/` in `docs/identity-deployment.md`. Verify the documented paths and modes match the reader's checks.

## 2. Shared shell library and source install

- [x] 2.1 Create `serval-common.sh` per D2/D4 with: protected-path symlink checks, the Web identity validation from `deploy/validate-web-identity.sh`, host-admin group validation (grammar, existence, non-zero GID), atomic root-owned file write, and `agent.json` creation and `jq`-based rewrite that preserves other fields. Verify with `shellcheck` and a shell test script covering valid and malformed group names, GID 0, a symlinked target and preservation of `idleMinutes` on rewrite.
- [x] 2.2 Make `deploy/install-identity.sh` and `deploy/validate-web-identity.sh` source `serval-common.sh` instead of duplicating the rules. Verify that `tests/Serval.Agent.Tests/Fixtures/test-web-identity.sh` and the Agent PAM fixture still pass.

## 3. Debian package build

- [x] 3.1 Add the package build script per D1/D2: self-contained publish of Agent and Web for `linux-x64` and `linux-arm64`, staged tree with explicit owners and modes, units under `/usr/lib/systemd/system`, `/etc/pam.d/serval` as a conffile, `serval-common.sh`, and `DEBIAN/control` with the D8 dependencies, version from the input tag and the `Serval-Dotnet-Runtime` field. Verify that both `.deb` files build, `lintian` reports no errors, `dpkg -c` shows no file writable by group or others under `/opt/serval`, and `dpkg -f` shows the runtime field.
- [x] 3.2 Add debconf `templates` and `config` for `serval/admin-group` (priority high, default `sudo`) per D4. Verify that `debconf-show serval` lists the default after a non-interactive install and that a preseeded value is used.
- [x] 3.3 Implement `preinst`, `postinst`, `prerm` and `postrm` per D3 and D5, including `abort-upgrade` restart, ordered Agent-then-Web start with an Agent activity check, the five-backup retention, handling of old unit copies in `/etc/systemd/system`, and purge preserving `/var/lib/serval` with a notice. Verify with `shellcheck` and with the package tests in group 5.
- [x] 3.4 Confirm whether Agent's `/var/lib/serval` validation accepts the `backups` subdirectory, and choose the D5 backup location accordingly. Verify with a package test where Agent starts after an upgrade that created a backup.

## 4. serval-setup

- [x] 4.1 Implement `/usr/sbin/serval-setup` with the root check and the fixed grammar for `listen`, `admin-group` and `status` per D6. Verify with tests for a non-root caller, an unknown subcommand, unknown and duplicated options, and values with shell metacharacters or control characters, each changing nothing.
- [x] 4.2 Implement `listen` with self-signed generation (ECDSA P-256, SANs, 365 days, fingerprint output), operator certificate validation and copy, `--loopback`, save-and-restore of previous files, and Web restart with an activity and port check. Verify with package tests for self-signed exposure reachable over HTTPS, operator certificate use, non-loopback without TLS refused, invalid address and port, expired, mismatched, encrypted, non-PEM, symlinked and directory inputs refused, rollback when the port is already in use, and return to loopback.
- [x] 4.3 Implement `admin-group` using the shared rewrite and restarting Agent then Web, and make `dpkg-reconfigure serval` use the same path. Verify with package tests that the new group's member gains and the old group's member loses grant administration, and that invalid groups leave `agent.json` and services unchanged.
- [x] 4.4 Implement `status`. Verify that its output after self-signed exposure lists listener, TLS mode, names, expiry, fingerprint, group and service states, and contains no `PRIVATE KEY` text.
- [x] 4.5 Document `serval-setup` and the package-first install in `docs/identity-deployment.md`, including SSH tunnel access, the Safari limitation of loopback HTTP and the certificate fingerprint check. Verify each documented command against the package test fixture.

## 5. Package lifecycle tests on real systemd

- [x] 5.1 Add systemd container fixtures for Ubuntu 22.04, 24.04 and 26.04 next to the existing Debian 13 fixture, starting from minimal images. Verify each boots to a running systemd and that the package installs there with only its declared dependencies.
- [x] 5.2 Add a package test script covering install scenarios of `deployment/debian-package`: no .NET present, identity and modes, binary trees not writable by Web, enabled and active services, socket present, Web reachable on loopback only, unsafe pre-existing account, symlinked protected paths, invalid and preseeded groups, and unsupported architecture refusal. Verify it passes in every fixture.
- [x] 5.3 Extend the script with upgrade scenarios: lower-version install with grants, favorites and audit rows, upgrade with backup created and data preserved, forced backup failure leaving old binaries and database, Agent startup failure after upgrade leaving Web stopped and the backup present, and configuration changed through `serval-setup` surviving the upgrade. Verify it passes in every fixture.
- [x] 5.4 Extend the script with remove, purge and reinstall scenarios as specified. Verify it passes in every fixture.
- [ ] 5.5 Add the package build, `lintian`, `shellcheck` and the package test matrix (four releases, `amd64` and `arm64`) to `.github/workflows/ci.yml`. Verify the jobs pass on a pull request.

## 6. Distribution: APT repository, bootstrap and release

- [x] 6.1 Implement `install.sh` per D9: whole body in a function invoked last, root check, `/etc/os-release` and architecture checks, keyring download with pinned SHA-256, deb822 source with `Signed-By`, install. Verify with tests against a locally served repository signed with a throwaway key: supported host install, unsupported release, unsupported architecture, non-root, key hash mismatch, repeated run leaving one source, and a truncated script running nothing.
- [x] 6.2 Add the repository build script using `apt-ftparchive` for suite `stable`, component `main`, both architectures, from all released `.deb` files, with signed `InRelease` and `Release.gpg`. Verify in the test from 6.1 that APT accepts the signed repository and rejects modified metadata, and that an earlier version is installable with `apt-get install serval=<version>`.
- [ ] 6.3 Add a tag-triggered release workflow that builds both packages, attaches them with SHA-256 checksums to the GitHub Release, rebuilds and signs the repository with the key from a protected `release` environment, and publishes `install.sh`, the keyring and `apt/` to GitHub Pages. Verify with a dry run on a test tag in a fork or with signing replaced by a throwaway key, and confirm the workflow has no access to the signing secret outside the `release` environment.
- [x] 6.4 Add the `dotnet-sdk` ecosystem to `.github/dependabot.yml`. Verify that the configuration validates and names `global.json`'s directory.
- [x] 6.5 Write `docs/packaging-and-release.md` covering layout, maintainer-script behavior, repository hosting, release key creation, storage and rotation, and the servicing-release checklist; update `SECURITY.md` "Supported versions" with the supported releases and update channel; add install instructions to `README.md`; update `docs/code-map.md` for the new packaging and setup files. Verify the README one-liner and manual steps match `install.sh`.

## 7. Integration check

- [x] 7.1 On one fresh Ubuntu 24.04 and one fresh Debian 13 systemd fixture, run the published-style flow end to end: `install.sh` from the local test repository, SSH-tunnel-style loopback login with a PAM test user, `serval-setup listen` with self-signed TLS and HTTPS login, upgrade to a newer test version, and purge. Verify each step and record the results in the change.

Result of 7.1 (`PACKAGE_TEST_SCRIPT=e2e-test.sh bash tests/packaging/run-package-tests.sh <image> <0.2.0.deb> <0.1.0.deb>`, amd64, systemd containers): on Ubuntu 24.04 and Debian 13 all 35 checks passed. The flow was `curl | sh` of the stamped `install.sh` from a local repository signed with a throwaway key (0.1.0 only), loopback HTTP login of a host-admin and a non-admin PAM user, `serval-setup listen` with a self-signed certificate (printed fingerprint equals the served one, HTTPS login, old HTTP port closed), publication of 0.2.0 and `apt-get upgrade` (backup created, `web.json` and the TLS listener kept, login still works), and `apt-get purge` (binaries and configuration removed, `/var/lib/serval` and the account kept). Not covered locally: arm64 and a plain (non-container) host; CI runs the same script on all eight combinations.

## Workflow follow-up

- Before the first public release: create the production release key, configure the protected `release` environment, and decide on a custom domain (see design Open Questions).
- Complete `.agents/workflows/post-change-review.md` before considering the change finished.
- Archive the change after the review requirements are satisfied.
