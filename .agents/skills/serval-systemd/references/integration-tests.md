# Real-systemd integration matrix

Read this reference before completing a systemd implementation or review. Unit tests may supplement these cases but cannot replace a real Linux/systemd test environment.

Use disposable test units and temporary Serval-owned fixtures. Assert both the operation result and that no vendor/user-owned source changed. Never print real or generated environment values in CI output.

Required coverage:

| Area | Minimum real-systemd cases |
| --- | --- |
| Source reading | `Environment=`; one `EnvironmentFile=`; multiple ordered `EnvironmentFile=` declarations; optional/missing file where supported |
| Effective values | Conflicting assignments demonstrating systemd precedence; provenance remains correct and values remain masked |
| Serval override | Add/change a Serval-only override; inherited source remains byte-for-byte unchanged |
| Removal | Remove a Serval override and observe the current inherited value becoming effective; clean up empty Serval artifacts |
| Reload | Successful `daemon-reload`; reload failure reported without restart or partial-file corruption |
| Lifecycle | Explicit permitted restart; save without restart; restart denied independently of edit permission |
| Validation | Malformed and malicious unit names; traversal/path manipulation; non-system or nonexistent target |
| Authorization | Missing privileges; unauthorized service; protected service; Serval's own privileged components |
| Failure safety | Interrupted/failed write leaves the previous complete file; symlink target is rejected |

For every privileged operation, include both permitted and denied paths and apply `serval-privileged-operation` as well.
