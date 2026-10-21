# Docker Visual Studio Web Debug Specification

## Purpose

Provide a reproducible one-container Visual Studio workflow that starts the complete Serval application with F5 and automatically debugs Web without changing production deployment behavior.

## Requirements

### Requirement: One-action complete application startup
The development profile SHALL start exactly one Compose application service from one image containing Serval.Agent and Serval.Web. Pressing F5 in Visual Studio SHALL start both application processes without requiring a separate command, manual process attachment, or prior container startup.

#### Scenario: F5 starts the complete application
- **WHEN** a developer starts the documented Compose profile with F5 in Visual Studio on a supported Linux-container Docker environment
- **THEN** exactly one Compose application service is running and both Serval.Agent and Serval.Web are active in that container

#### Scenario: Agent startup fails
- **WHEN** the development launcher cannot start Serval.Agent or establish its local IPC endpoint
- **THEN** startup fails with an actionable diagnostic instead of reporting the complete application as ready

### Requirement: Automatic Web debugging
Visual Studio F5 SHALL launch exactly one framework-dependent Serval.Web process under the dedicated `serval-web` kernel UID and automatically attach the managed debugger to that process. Serval.Agent SHALL run as root and remain the only Serval process permitted to perform privileged operations.

#### Scenario: Breakpoint is reached
- **WHEN** a developer sets a breakpoint in an executed Web code path, presses F5, and requests that path
- **THEN** Visual Studio stops on the breakpoint while the same container has one Serval.Web process owned by `serval-web` and one root Serval.Agent process

#### Scenario: Duplicate or privileged Web process
- **WHEN** the container contains zero or multiple Serval.Web processes, or the Web process kernel UID is not the UID of `serval-web`
- **THEN** the documented F5 inspection reports the session invalid even if a browser page loads or the debugger attaches

#### Scenario: Debugger cannot attach
- **WHEN** F5 starts the container but cannot stop execution at an executed Web breakpoint
- **THEN** the profile reports a failed debug startup with enough information to diagnose the selected process and generated Visual Studio configuration

### Requirement: Development isolation and production separation
The profile SHALL keep Web-to-Agent IPC local to the container and SHALL NOT expose a generic privileged command capability or require a host account, host PAM configuration, Docker socket, or privileged host filesystem mounts. The profile SHALL be development-only and SHALL NOT change production publishing, systemd units, or deployment behavior.

#### Scenario: Profile inspection
- **WHEN** the resolved Compose profile and running container are inspected
- **THEN** the service uses only documented development/debugging mounts and a loopback Web port, and it has no host account, PAM, Docker socket, or privileged host filesystem mount

#### Scenario: Production artifacts are inspected
- **WHEN** the development profile is added or changed
- **THEN** the production systemd units and production publish settings remain unchanged

### Requirement: Disposable development login
The development image SHALL contain the fixed interactive Linux account `serval-dev` with password `serval`, authenticated by the container-local `serval` PAM policy. The credential SHALL be documented as public development data and SHALL NOT be added to production deployment artifacts.

#### Scenario: Developer signs in
- **WHEN** a developer submits username `serval-dev` and password `serval` to the running F5 application
- **THEN** Agent authenticates the account through container-local PAM and Web establishes an application session

#### Scenario: Development credential isolation
- **WHEN** production deployment artifacts are inspected
- **THEN** they contain no `serval-dev` account creation or fixed development password
