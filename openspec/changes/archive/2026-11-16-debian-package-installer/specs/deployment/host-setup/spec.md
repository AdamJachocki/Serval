# Spec Delta

## Purpose

Give host administrators one local, root-only command, `serval-setup`, for the
post-installation decisions that change Serval's exposure or administrators:
the Web listener with its TLS certificate and the host-admin group.

## ADDED Requirements

### Requirement: Root-only local command
`serval-setup` SHALL be installed by the package and SHALL run only as root. It SHALL NOT be reachable through Web or the Agent socket, and SHALL accept only its defined subcommands and options. It SHALL NOT pass any argument to a shell.

#### Scenario: Non-root caller
- **WHEN** a non-root user runs any `serval-setup` subcommand
- **THEN** it exits non-zero before reading or changing any Serval file

#### Scenario: Unknown or malformed arguments
- **WHEN** `serval-setup` receives an unknown subcommand, an unknown option, a duplicated option or a value containing shell metacharacters or control characters
- **THEN** it exits non-zero with usage help and changes nothing

### Requirement: Expose Web with TLS
`serval-setup listen` SHALL set Web's listener address and port and SHALL require TLS for any non-loopback address. TLS SHALL be either a newly generated self-signed certificate for given host names, or an operator certificate and key copied into Serval's configuration directory after validation. It SHALL restart Web after a successful change.

#### Scenario: Self-signed exposure
- **WHEN** root runs `serval-setup listen --address 0.0.0.0 --port 8443 --tls self-signed --hostname admin.example.test`
- **THEN** a certificate for that name is generated, Web restarts and serves HTTPS on port 8443, and the command prints the certificate's SHA-256 fingerprint for out-of-band verification

#### Scenario: Operator certificate
- **WHEN** root supplies a PEM certificate chain and matching unencrypted private key
- **THEN** both are copied into Serval's TLS directory with the key readable only by root and the `serval-web` group, and Web serves that certificate

#### Scenario: Non-loopback without TLS
- **WHEN** root requests a non-loopback address without a TLS option
- **THEN** the command fails and the current listener is unchanged

#### Scenario: Invalid certificate input
- **WHEN** the certificate is not valid PEM, is expired, does not match the key, or the key is encrypted, or either path is a symbolic link, a directory or not a regular file
- **THEN** the command fails, no TLS file or listener setting is changed and the private key content is never printed

#### Scenario: Invalid address or port
- **WHEN** the address is not a literal IPv4 or IPv6 address, or the port is outside 1024 to 65535, because Web runs without the privilege to bind lower ports
- **THEN** the command fails and the current listener is unchanged

#### Scenario: Return to loopback
- **WHEN** root runs `serval-setup listen --loopback`
- **THEN** Web restarts listening only on the loopback default over HTTP

### Requirement: Change the host-admin group
`serval-setup admin-group <group>` SHALL validate the group with the same rules as installation, update `/etc/serval/agent.json` atomically with the group name and resolved GID, and restart Agent and then Web.

#### Scenario: Valid group
- **WHEN** root runs `serval-setup admin-group serval-admins` for an existing group
- **THEN** `agent.json` names `serval-admins` and its GID, both services restart, and members of the previous group lose administrator rights while members of the new group gain them

#### Scenario: Invalid group
- **WHEN** the group name is malformed, does not exist or resolves to GID 0
- **THEN** the command fails and `agent.json` and the services are unchanged

### Requirement: Atomic and recoverable changes
`serval-setup` SHALL write each configuration and TLS file atomically with its documented owner and mode, and SHALL NOT write through symbolic links. When a restarted service fails to become active, it SHALL restore the previous files, restart the service again and exit non-zero.

#### Scenario: Web fails after a listener change
- **WHEN** Web does not become active after a listener change, for example because the port is already in use
- **THEN** the previous listener and TLS files are restored, Web is restarted with them and the command reports the failure

#### Scenario: Symbolic link in Serval configuration
- **WHEN** a target file or directory under `/etc/serval` is a symbolic link
- **THEN** the command fails without writing through it

### Requirement: Show current setup
`serval-setup status` SHALL print the configured listener, TLS mode, certificate subject names, expiry and SHA-256 fingerprint, the host-admin group and both services' states. It SHALL never print private key material.

#### Scenario: Status after exposure
- **WHEN** root runs `serval-setup status` after a self-signed exposure
- **THEN** the output lists the listener, `self-signed`, the host names, expiry and fingerprint, and no key material
