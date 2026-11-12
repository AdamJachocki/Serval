# Packaging and release

How the `serval` Debian package is built, what it does on a host, how releases
reach the APT repository, and what a maintainer must do to publish and service
them. Operator instructions are in [identity-deployment.md](identity-deployment.md).

## Package layout

One package, `serval`, for Debian 13 and Ubuntu 22.04, 24.04 and 26.04 on
`amd64` and `arm64`. It is self-contained: Agent and Web are published with the
.NET runtime, so the host needs no .NET installation.

| Installed path | Content |
| --- | --- |
| `/opt/serval/agent`, `/opt/serval/web` | Published Agent and Web. Root-owned, mode `0755` for directories, `0644` for files; only `Serval.Agent` and `Serval.Web` are executable |
| `/usr/lib/systemd/system/serval-agent.service`, `serval-web.service` | The units from `deploy/systemd` |
| `/etc/pam.d/serval` | PAM policy; a conffile |
| `/usr/lib/serval/serval-common.sh` | Shared POSIX shell rules, sourced by the maintainer scripts, `serval-setup` and `deploy/install-identity.sh` |
| `/usr/sbin/serval-setup`, `serval-setup(8)` | Root-only listener, TLS and host-admin group administration |
| `/usr/share/serval/serval-archive-keyring.gpg` | Public keyring of the APT repository, in release packages only; `postinst` refreshes the host's keyring copy with it |

`Depends` lists only distribution libraries (ICU and OpenSSL as alternatives
across the supported releases). The `Serval-Dotnet-Runtime` control field
records the bundled runtime version (`dpkg -s serval`), and the changelog
repeats it. The package is built by `packaging/build-deb.sh` with
`dpkg-deb --root-owner-group`, not debhelper, because a self-contained .NET
publish does not fit a source-package build. Reviewed lintian findings that are
inherent to that publish are listed in `packaging/debian/lintian-overrides`.

The bundled runtime comes from the .NET SDK in the build image
(`packaging/tools.Dockerfile`, `mcr.microsoft.com/dotnet/sdk:10.0`), constrained by
`global.json`; the `Serval-Dotnet-Runtime` field records what was bundled.

## Maintainer-script behavior

All scripts are POSIX `sh`, take no input other than dpkg's arguments and
debconf answers, and use the validation rules in `deploy/serval-common.sh`.

- **`preinst`** (install, upgrade): rejects symbolic links at `/etc/serval`,
  `/var/lib/serval`, `/etc/pam.d/serval`, `agent.json` and `policy.db`, and an
  unsafe existing `serval-web` account. On upgrade it stops Web and then the
  Agent and copies `policy.db` and its SQLite companion files into
  `/var/lib/serval/backups/<previous-version>-<UTC timestamp>/` (root-only, the
  five newest kept). A failed backup aborts the upgrade before any binary
  changes. The shared library is embedded in this script at build time because
  it is not unpacked yet on a first installation.
- **`config`**: asks the one debconf question, the host-admin group.
- **`postinst`** (configure): creates `serval-web`, validates the identity,
  creates `/etc/serval` and `/var/lib/serval`, removes an unchanged copy of the
  old source-installer units (identified by SHA-256; a modified copy stops the
  installation), writes `agent.json` only when it is absent, enables both units
  with `deb-systemd-helper` (an earlier `systemctl disable` survives upgrades),
  starts the Agent, waits until its socket exists, starts Web and waits until it
  listens. If the Agent does not become ready, Web stays stopped and the backup
  stays in place. `dpkg-reconfigure serval` applies the answer through the same
  code as `serval-setup admin-group`, with rollback.
- **`prerm`** stops both units; enablement stays with `deb-systemd-helper`, so
  `remove` followed by an install enables and starts them again, and a unit an
  administrator disabled stays disabled. **`postrm`** masks the unit names on
  `remove` (postinst unmasks them) and removes nothing else; on `purge` it deletes `/etc/serval`, `/var/lib/serval-web`, the PAM
  file and the debconf data. `/var/lib/serval` (grants, audit history, backups)
  and the `serval-web` account are kept: audit retention is an operator decision.
- In a chroot or image build without a running systemd the scripts skip every
  service step.

`/etc/serval/web.json` and `/etc/serval/tls/` are never package-owned, so
upgrades never overwrite a listener configuration.

## Tests

| Check | Command |
| --- | --- |
| Shell static checks and unit tests | `bash tests/packaging/run-shell-checks.sh` |
| Package lifecycle on real systemd (install, upgrade, backup, remove, purge, `serval-setup`) | `bash tests/packaging/run-package-tests.sh <image> <new.deb> <old.deb>` |
| Signed repository: acceptance, tampering, earlier versions | `bash tests/packaging/run-apt-repository-test.sh <image> <package-dir>` |
| `install.sh` against a local signed repository | `bash tests/packaging/run-install-script-test.sh <image> <package-dir>` |

`<image>` is one of `debian:13-slim`, `ubuntu:22.04`, `ubuntu:24.04`,
`ubuntu:26.04`; the package directory holds at least two versions. CI
(`.github/workflows/ci.yml`) builds both architectures and runs all of them on
all four releases. Adding a supported release means adding it to that matrix
first.

## Repository and bootstrap

The repository and `install.sh` are served from GitHub Pages of this
repository (`https://<owner>.github.io/<repository>`, or the address in the
repository variable `SERVAL_SITE_URL`):

```text
install.sh
serval-archive-keyring.gpg
apt/dists/stable/{InRelease,Release,Release.gpg}
apt/dists/stable/main/binary-{amd64,arm64}/Packages*
apt/pool/main/s/serval/*.deb
```

One suite (`stable`) and one component (`main`) serve every supported release,
because the package is identical across them. `packaging/build-apt-repo.sh`
builds the repository with `apt-ftparchive` from the `.deb` files of all
released versions, so earlier versions stay installable with
`apt-get install serval=<version>`, and signs it. `packaging/assemble-site.sh`
adds the keyring and the stamped `install.sh`.

`install.sh` is stamped with the repository address and the SHA-256 of the
keyring (`packaging/stamp-install-script.sh`). It verifies the host, the
architecture and the downloaded keyring against that hash before writing
`/usr/share/keyrings/serval-archive-keyring.gpg` and
`/etc/apt/sources.list.d/serval.sources` (with `Signed-By`), then installs the
package. The whole script is one function called on the last line, so a
truncated download runs nothing. The unstamped template refuses to run.

## Releasing

A release is a tag `vX.Y.Z` on a commit that is on `main`.

1. Merge everything for the release into `main` and make sure CI is green.
2. Push the tag. `.github/workflows/release.yml` builds and lints both
   packages, then waits for a maintainer to approve the `release` environment.
3. After approval it signs the repository, creates the GitHub Release with the
   `.deb` files and `SHA256SUMS`, and publishes the site to GitHub Pages.
4. Check the Pages site and run the published one-liner on a test host.

The workflow can be run manually (**Actions → Release → Run workflow**) as a
dry run: it builds the packages, assembles a site with a throwaway key and
publishes nothing. Do this after changing packaging or the workflow.

The package version is the tag without `v`. A tag such as `v1.2.3` yields
version `1.2.3`.

## Release key

The repository is signed with a dedicated RSA 4096 key that signs nothing else. (The committed key also carries an RSA 4096 encryption subkey that Serval never uses.)

- **Create** it on a trusted machine: `gpg --quick-generate-key 'Serval release
  <maintainer address>' rsa4096 sign 2y`. Back up the secret key and its
  revocation certificate offline.
- **Store** it as the environment secret `APT_GPG_PRIVATE_KEY` (ASCII-armored;
  `gpg --armor --export-secret-keys <fingerprint>`). A key with a passphrase
  needs the passphrase in a second secret, `APT_GPG_PASSPHRASE`, of the same
  environment of the protected GitHub environment `release`, with
  required reviewers. Only the `publish` job of the release workflow uses the
  environment, so no other job or workflow can read it. The key exists on the
  runner only for that job and is deleted at its end.
- **Publish** nothing but the public key. Commit it as
  `packaging/release-keyring/serval-archive-keyring.gpg` (dearmored; the file
  may hold several keys). The release workflow refuses to build without it,
  ships it inside the package and publishes the same file on the site.
  `install.sh` pins its SHA-256, so a substituted key file is rejected.
- **How hosts get key updates:** `install.sh` writes the keyring to
  `/usr/share/keyrings/serval-archive-keyring.gpg`, and every release package
  carries the current keyring at `/usr/share/serval/serval-archive-keyring.gpg`.
  `postinst` copies it over that file when the host has it (that is, the host
  was set up from the repository), so an extended expiry or a rollover key
  arrives through a normal `apt-get upgrade`. Hosts installed from a `.deb`
  by hand have no repository and get no copy.
- **Extend the expiry** well before it passes (`gpg --quick-set-expire`),
  export the updated public key into the committed keyring and publish a
  release signed while the old self-signature is still valid. A host that
  upgrades after the expiry date cannot verify the repository any more and
  must re-run `install.sh` once.
- **Rotate** (planned, or after a suspected leak) in two releases. First add
  the new public key to the committed keyring next to the old one and release,
  still signing with the old key: every host receives both keys. Then replace
  the signing secret with the new key and release again, signed with the new
  key; hosts verify it with the keyring they already have. Drop the old key
  from the keyring in a later release. After a leak, revoke the old key, skip
  the transition (hosts must re-run `install.sh`) and say so in the advisory.

## Servicing releases

Because the .NET runtime is bundled, a runtime security fix reaches hosts only
through a new Serval release.

- Dependabot opens pull requests for `global.json` (`dotnet-sdk` ecosystem)
  and NuGet packages every week.
- After merging an SDK or package update that fixes a security issue, publish
  a release (a new patch version) promptly; do not wait for a feature release.
- Release checklist: SDK and package updates merged, `main` green, tag pushed,
  release approved, site checked, `dpkg -s serval` shows the expected
  `Serval-Dotnet-Runtime`, advisory published when the fix is security
  relevant.
- Before the first public release: create the production release key, commit
  its public key to `packaging/release-keyring/`, configure the `release` environment, enable GitHub Pages, and decide on a
  custom domain (cheapest before the first release, because existing clients
  would need a one-time source update later).
