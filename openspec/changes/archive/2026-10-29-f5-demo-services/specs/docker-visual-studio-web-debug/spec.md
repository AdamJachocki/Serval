## ADDED Requirements

### Requirement: Real development service manager
The F5 profile SHALL run a real system-level systemd manager inside the application container. Service metadata SHALL reach Web through the existing authenticated Agent read operations. The profile SHALL retain automatic debugging under the dedicated Web identity and SHALL NOT substitute simulated service responses.

#### Scenario: Complete service browsing startup
- **WHEN** a developer starts the profile with F5 and signs in with the documented development account
- **THEN** the UI lists and opens the demonstration services using metadata from the container's systemd manager
- **AND** an executed Web breakpoint is reached under the `serval-web` kernel UID

#### Scenario: Manager unavailable
- **WHEN** systemd cannot start or cannot answer bounded manager queries
- **THEN** startup verification fails with an actionable diagnostic rather than reporting the environment ready

### Requirement: Reproducible demonstration services
Every fresh development container SHALL initialize three clearly identified demonstration services with active states `active`, `inactive`, and `failed`. Their workloads SHALL be minimal, local, and unprivileged. The documentation SHALL describe expected UI results and recreation of the disposable baseline.

#### Scenario: Fresh baseline
- **WHEN** a fresh container completes initialization
- **THEN** all three services exist and their UI states agree with direct systemd queries
- **AND** no demonstration workload listens on a network port or needs host resources

#### Scenario: Recreate baseline
- **WHEN** the developer removes only the disposable Compose project and starts a fresh container through F5
- **THEN** the three initial states are restored without manual service commands

### Requirement: Development service authorization
The development account SHALL obtain sample access through the existing Linux identity and Agent authorization model. Provisioning SHALL be confined to the development image. Protected services SHALL remain hidden and denied, including to a development administrator.

#### Scenario: Authorized sample read
- **WHEN** the authenticated development account requests a sample service
- **THEN** Agent permits the read through the existing authorization policy

#### Scenario: Unauthorized or invalid read
- **WHEN** an unauthenticated caller or a disposable user without a grant requests a sample, or a caller supplies a malformed service identity
- **THEN** the existing Agent boundary denies the request without returning service metadata

#### Scenario: Protected service read
- **WHEN** the development administrator requests a protected service by canonical identity or alias
- **THEN** the service is absent from discovery and inspection is denied

### Requirement: Bounded demonstration readiness
Startup verification SHALL check manager availability and the three initial service states within a finite deadline. The expected failed sample SHALL NOT invalidate readiness. Unexpected sample states or Agent failures SHALL fail readiness. Health checks SHALL NOT change service state to conceal a failure.

#### Scenario: Expected failure is healthy
- **WHEN** the manager responds, Agent is ready, and the three samples have their expected states
- **THEN** development health verification succeeds despite the intentional failed sample

#### Scenario: Broken initialization
- **WHEN** a sample is missing or has an unexpected state, or Agent is unavailable
- **THEN** verification fails with a bounded diagnostic identifying the failed check without exposing credentials or environment values

### Requirement: Isolated systemd development runtime
The disposable local F5 runtime SHALL enable Docker privileged mode for systemd startup, use private PID, IPC and cgroup namespaces, and expose only the existing loopback Web port. The runtime SHALL NOT mount the host systemd bus, host cgroup tree, Docker socket, or host account data.

#### Scenario: Runtime inspection
- **WHEN** the resolved Visual Studio Compose configuration and running container are inspected
- **THEN** privileged mode is enabled only for the disposable development profile, the namespace and mount constraints hold, and certificate and user-secret mounts remain read-only

#### Scenario: Unsupported runtime
- **WHEN** the Docker environment cannot satisfy systemd startup within these isolation constraints
- **THEN** the profile fails with a compatibility diagnostic without adding prohibited host mounts or sharing host namespaces

### Requirement: Development-only container privilege exception
Docker privileged mode SHALL be documented as a disposable local development exception and SHALL NOT change production deployment. Demo assets SHALL be excluded from production deployment. Serval.Web SHALL still run as `serval-web` without retained root capabilities.

#### Scenario: Privilege scope inspection
- **WHEN** the development runtime and production artifacts are inspected
- **THEN** Web has the dedicated unprivileged identity and no retained root capabilities, and production artifacts contain neither demo assets nor this container privilege exception
