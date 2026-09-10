# Systemd service enumeration

Issue: #11

`SystemdServiceEnumerator` builds a fresh internal snapshot over the typed
system-bus transport. It is intentionally not registered with Web or exposed as
an IPC/application inventory: inspection and Agent policy enforcement are
separate work. Before exposing these records, Agent must enforce `Service.View`
and protected-service policy against the canonical ID and all retained names,
and always exclude its own privileged units.

The operation reads the manager version (minimum 249), installed unit files and
loaded services, then resolves missing concrete installed services by name.
Linux basenames are validated without filesystem access. Uninstantiated templates
remain internal metadata. Each distinct unit object is read once on the normal
path; identities are merged by ordinal canonical ID and aliases are retained.
Descriptions and unknown state strings survive mapping. Services, names and
templates are sorted ordinally and published as read-only collections only after
the complete operation succeeds.

A `not-found` load state, `NoSuchUnit` or unit-object `UnknownObject` triggers one name-based
re-resolution for that candidate. Continued absence omits the candidate. Other
errors, including manager-object `UnknownObject`, fail the whole operation; no partial snapshot or cache is returned.
The internal 30-second deadline includes connection establishment, all calls,
re-resolution and assembly. Earlier caller cancellation remains cancellation;
deadline expiry is a typed timeout. Unsupported versions have their own typed
failure. Diagnostics do not contain reply payloads.

## Capability and trust boundary

This is internal read-only discovery for the future Agent. Untrusted inputs are
D-Bus replies and caller cancellation. The transport admits only fixed manager
calls and opaque issued unit references. Names are bounded `SystemServiceId`
values before reuse and return. No principal, authorization assertion, path,
command or environment value is accepted by enumeration. It introduces no new
root operation, arbitrary D-Bus operation, process execution, filesystem access,
service lifecycle action or daemon reload. No audit or persistent storage is
added; future Agent audit must use verified actor and canonical resource metadata
without payloads. Protected/unauthorized-service tests belong to the future
Agent boundary, which must not return this raw snapshot directly.

## Verification

Typed-response tests cover installed and loaded units, aliases/shared objects,
case-sensitive identity, templates and instances, unknown states, validation,
version rejection, disappearance/reappearance, non-retryable failures,
cancellation and the whole-operation deadline.

`Fixtures/run-enumeration-tests.sh` is a test-only root harness for a disposable
Linux/systemd environment. It creates uniquely named files under
`/run/systemd/system`, a loaded instance and a transient service. It runs the test
command passed by CI and cleans up its exact fixture files and services on exit.
The integration test checks inactive discovery, alias deduplication, template
exclusion, installed/loaded instances, transient discovery, ordering and unchanged
fixture contents. The existing Ubuntu 249/255/259 and Debian 257 CI matrix runs
these tests on both configured architectures. Production enumeration never uses
this harness or its process/mutation capabilities.
