# Spec Delta

## Purpose

Define where Serval.Web listens in a production deployment, how it obtains its
listener and TLS settings, and the rule that Web is never reachable from the
network over plain HTTP.

## ADDED Requirements

### Requirement: Production listener scope
The listener rules of this capability SHALL apply whenever Web runs in the `Production` environment, which the packaged unit sets. The `Development` and `Testing` environments used by repository fixtures SHALL keep their existing listener configuration and SHALL NOT read `/etc/serval/web.json`.

#### Scenario: Packaged service
- **WHEN** Web starts from the packaged `serval-web.service`
- **THEN** it applies the listener rules of this capability

#### Scenario: Development fixture
- **WHEN** Web starts in the `Development` environment with `ASPNETCORE_URLS` set by the development compose file
- **THEN** it listens as configured by that file, as before this change

### Requirement: Loopback HTTP default
When no listener configuration exists, Web SHALL listen only on `127.0.0.1` port 9080 over HTTP. It SHALL NOT listen on any other address and SHALL NOT redirect to HTTPS in this mode.

#### Scenario: Fresh installation
- **WHEN** Web starts with no listener configuration file
- **THEN** it accepts HTTP connections on `127.0.0.1:9080` only and refuses connections to the host's other addresses

#### Scenario: Login through an SSH tunnel
- **WHEN** an administrator forwards a local port to `127.0.0.1:9080` and logs in from Chromium or Firefox using `http://localhost:<port>`
- **THEN** the session cookie is accepted and the authenticated inventory is shown

### Requirement: Root-owned listener configuration
Web SHALL read its listener and TLS settings only from `/etc/serval/web.json`, owned by root, group `serval-web`, mode `0640`, and SHALL ignore listener settings from command-line arguments, environment variables and application settings. Web SHALL refuse to start if the file is malformed, has unsafe ownership or mode, or is a symbolic link.

#### Scenario: Unsafe file
- **WHEN** `/etc/serval/web.json` is writable by `serval-web` or others, or is a symbolic link
- **THEN** Web logs a sanitized error and exits without listening

#### Scenario: Override through environment
- **WHEN** `ASPNETCORE_URLS` or a `Kestrel` application setting names a different address
- **THEN** Web ignores it in the `Production` environment and uses only the configured or default listener

#### Scenario: Malformed configuration
- **WHEN** the file is not valid JSON or contains an unknown field, an invalid address or a port outside 1024 to 65535
- **THEN** Web exits without listening

### Requirement: Non-loopback requires TLS
Web SHALL refuse to start when a non-loopback address is configured without TLS. With TLS, Web SHALL serve only HTTPS on the configured address, SHALL send HSTS, and SHALL load the certificate and key only from Serval's TLS directory under `/etc/serval`.

#### Scenario: Plain HTTP on a network address
- **WHEN** the configuration names `0.0.0.0` or another non-loopback address without TLS
- **THEN** Web exits without listening

#### Scenario: HTTPS exposure
- **WHEN** the configuration names a non-loopback address with a valid certificate and key
- **THEN** Web serves HTTPS on that address, sends `Strict-Transport-Security`, and does not accept plain HTTP on it

#### Scenario: Missing or unreadable key
- **WHEN** the configured certificate or key is missing, unreadable, mismatched or outside Serval's TLS directory
- **THEN** Web exits without listening and never logs key material
