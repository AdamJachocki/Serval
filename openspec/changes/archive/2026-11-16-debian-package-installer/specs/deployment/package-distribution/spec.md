# Spec Delta

## Purpose

Define how administrators obtain and update the `serval` package: a signed APT
repository, a one-line bootstrap script, release assets, and the obligation to
republish when the bundled .NET runtime receives security fixes.

## ADDED Requirements

### Requirement: Signed APT repository
Serval SHALL publish the `serval` package in an APT repository served only over HTTPS whose metadata is signed with a dedicated Serval release key. The public key SHALL be published at a stable HTTPS URL. Clients SHALL be configured to trust that key only for the Serval source through `signed-by`, never through a global keyring.

#### Scenario: Install from the repository
- **WHEN** an administrator has added the Serval source with its key in a dedicated keyring and runs `apt-get install serval`
- **THEN** APT verifies the repository signature and installs the package

#### Scenario: Tampered metadata
- **WHEN** repository metadata or a package does not match the signed release information
- **THEN** APT refuses to install or upgrade from that repository

#### Scenario: Key scope
- **WHEN** the Serval source has been configured
- **THEN** the Serval key is not trusted for any other APT source

### Requirement: One-line bootstrap
Serval SHALL publish an `install.sh` script that, run as root, verifies the host is a supported release and architecture, installs the Serval key into a dedicated keyring, adds the Serval source with `signed-by`, refreshes package lists and installs `serval`. It SHALL fetch only fixed Serval HTTPS URLs and SHALL NOT execute downloaded content other than through APT.

#### Scenario: Supported host
- **WHEN** a root user runs `curl -fsSL <published install.sh URL> | sh` on a supported release
- **THEN** the Serval source is configured, `serval` is installed and the script prints how to reach Web through an SSH tunnel

#### Scenario: Unsupported host
- **WHEN** the script runs on an unsupported distribution, release or architecture
- **THEN** it exits non-zero with a message naming the supported targets and changes nothing on the host

#### Scenario: Not root
- **WHEN** the script runs without root privileges
- **THEN** it exits non-zero before changing anything

#### Scenario: Repeated run
- **WHEN** the script runs on a host where the Serval source is already configured
- **THEN** it leaves a single Serval source and keyring and installs or upgrades `serval`

#### Scenario: Interrupted download
- **WHEN** the script is only partially received by the shell
- **THEN** no partially defined step runs, because the script body executes only after it has been read completely

### Requirement: Release assets
Each tagged release SHALL attach the `amd64` and `arm64` packages and their SHA-256 checksums to the GitHub Release, so an administrator can install a package without the repository. The repository SHALL keep previous released versions available for downgrade.

#### Scenario: Manual install from a release
- **WHEN** an administrator downloads a released `.deb`, verifies its checksum and installs it with `apt-get install ./<file>.deb`
- **THEN** the result is the same as installing that version from the repository

### Requirement: Runtime servicing releases
Because the .NET runtime is bundled, Serval SHALL publish a new package version for each .NET servicing release that fixes a security vulnerability affecting the bundled runtime, and the package metadata SHALL identify the bundled runtime version.

#### Scenario: .NET security update
- **WHEN** a .NET servicing release with a security fix for the bundled runtime is published
- **THEN** a Serval package built with that runtime is published to the repository and released, and administrators receive it through a normal `apt-get upgrade`

#### Scenario: Identify the bundled runtime
- **WHEN** an administrator inspects the installed package metadata
- **THEN** the bundled .NET runtime version is shown
