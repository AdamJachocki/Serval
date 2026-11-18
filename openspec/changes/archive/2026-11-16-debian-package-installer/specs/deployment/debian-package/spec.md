# Spec Delta

## Purpose

Define the contract of the `serval` Debian package: which hosts it supports and
how installing, upgrading, removing and purging it leaves Serval's identities,
files, database and services in a secure, working state without manual steps.

## ADDED Requirements

### Requirement: Supported platforms and bundled runtime
The `serval` package SHALL be built for `amd64` and `arm64` and SHALL support Debian 13 and Ubuntu 22.04, 24.04 and 26.04. It SHALL contain self-contained Agent and Web builds, so the host SHALL NOT need a .NET runtime or a Microsoft package source. Agent and Web SHALL always come from the same package version.

#### Scenario: Install on a host without .NET
- **WHEN** an administrator installs `serval` on a supported release that has no .NET runtime or SDK
- **THEN** installation completes and both services start without installing any .NET package

#### Scenario: Unsupported architecture
- **WHEN** an administrator attempts to install the package on an architecture other than `amd64` or `arm64`
- **THEN** the package manager refuses the installation and no Serval file, account or service is created

### Requirement: Install-time identity and configuration
On first installation the package SHALL create the locked, non-interactive `serval-web` system account if absent, validate it as `deploy/validate-web-identity.sh` does, install the `serval` PAM policy and both units, and write `/etc/serval/agent.json` with the resolved Web UID/GID and host-admin group. Installation SHALL fail without starting services when validation fails.

#### Scenario: Clean install
- **WHEN** `serval` is installed on a host with no prior Serval state
- **THEN** `serval-web` exists with home `/nonexistent`, shell `/usr/sbin/nologin`, a locked password and no supplementary groups, and `/etc/serval/agent.json` is root-owned with mode `0600`

#### Scenario: Pre-existing unsafe account
- **WHEN** a `serval-web` account already exists with a login shell, an unlocked password or a supplementary group
- **THEN** installation fails, the account is left unchanged and neither service is started

#### Scenario: Symbolic link at a protected path
- **WHEN** `/etc/serval`, `/var/lib/serval` or `/etc/pam.d/serval` is a symbolic link before installation
- **THEN** installation fails without writing through the link

### Requirement: Default host-admin group
The package SHALL ask for the host-admin group through debconf with a default of `sudo`, SHALL accept a preseeded answer for non-interactive installs, and SHALL validate it with the same name grammar and existence check as the current installer. An invalid or absent group SHALL fail installation without starting services.

#### Scenario: Non-interactive default
- **WHEN** the package is installed with `DEBIAN_FRONTEND=noninteractive` and no preseeded group
- **THEN** `agent.json` names the `sudo` group with its resolved GID

#### Scenario: Preseeded group
- **WHEN** a valid existing group is preseeded for the package before installation
- **THEN** `agent.json` names that group and its resolved GID

#### Scenario: Invalid or missing group
- **WHEN** the selected group name is malformed, does not exist or resolves to GID 0
- **THEN** installation fails with a sanitized message and neither service is started

### Requirement: Installed file ownership
Installed Agent and Web binary trees SHALL be owned by root and SHALL NOT be writable by `serval-web` or any other non-root account. The package SHALL leave every path listed in `docs/identity-deployment.md` with the documented owner and mode.

#### Scenario: Binary trees are not writable by Web
- **WHEN** installation completes
- **THEN** no file or directory under the installed Agent and Web trees is owned by or writable by `serval-web`, its group or others

### Requirement: Service enablement and order
After a successful installation the package SHALL enable both units and start Agent before Web. Web SHALL listen only on the loopback default defined by `web/network-listener` until an administrator changes it.

#### Scenario: Services running after install
- **WHEN** installation completes
- **THEN** `serval-agent.service` and `serval-web.service` are enabled and active, the Agent socket exists under `/run/serval`, and Web answers on its loopback address only

### Requirement: Configuration preserved across upgrades
An upgrade SHALL NOT overwrite `/etc/serval/agent.json`, `/etc/serval/web.json`, TLS material under `/etc/serval` or the policy database. Package-owned files that an administrator may edit SHALL be handled as configuration files.

#### Scenario: Changed admin group survives upgrade
- **WHEN** an administrator has changed the host-admin group and then upgrades the package
- **THEN** `agent.json` still names the changed group after the upgrade

### Requirement: Upgrade with database backup
Before an upgrade replaces binaries the package SHALL stop Web and then Agent and SHALL copy the policy database and its SQLite companion files to a root-only backup named with the previous package version. After unpacking it SHALL start Agent before Web. A failed backup SHALL abort the upgrade before any binary is replaced.

#### Scenario: Successful upgrade
- **WHEN** an installed version with existing grants, favorites and audit records is upgraded
- **THEN** a root-owned `0600` backup of the previous database exists, Agent starts with the upgraded schema, and grants, favorites and audit records are preserved

#### Scenario: Backup failure
- **WHEN** the backup cannot be written completely
- **THEN** the upgrade fails, the previous binaries remain installed and the original database is unchanged

#### Scenario: Agent rejects the database after upgrade
- **WHEN** Agent fails its startup checks after an upgrade
- **THEN** the package reports the failure, Web is not started, and the pre-upgrade backup remains available for the documented rollback

### Requirement: Remove and purge
Removing the package SHALL stop and disable both services and remove the installed binaries and units. Purging SHALL additionally remove `/etc/serval`, the `serval` PAM policy and Web's state directory, but SHALL preserve `/var/lib/serval` and the `serval-web` account and SHALL tell the administrator what was kept.

#### Scenario: Remove keeps configuration
- **WHEN** the package is removed but not purged
- **THEN** both services are stopped and disabled, and `/etc/serval` and `/var/lib/serval` remain

#### Scenario: Purge preserves audit data
- **WHEN** the package is purged
- **THEN** `/etc/serval`, `/etc/pam.d/serval` and `/var/lib/serval-web` are removed, `/var/lib/serval/policy.db` and its backups remain root-only, and the administrator is told they were kept

#### Scenario: Reinstall after purge
- **WHEN** the package is installed again after a purge
- **THEN** installation recreates configuration and Agent opens the preserved database with its grants and audit history
