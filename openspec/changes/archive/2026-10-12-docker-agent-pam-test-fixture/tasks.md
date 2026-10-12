## 1. Disposable Agent image

- [x] 1.1 Publish the Linux Agent with locked dependencies and stage an allowlisted Docker context containing only its output, the existing PAM policy and Agent unit, and fixture setup files; verify the context has no repository-wide copy, untracked data, or credential material.
- [x] 1.2 Build the separate Debian Agent image with locked `serval-web`, dedicated admin group, root-owned fixed configuration, PAM file, policy directory, and Agent executable; verify UID/GID, home, shell, group membership, ownership, and modes inside a fresh container.
- [x] 1.3 Start the image with systemd PID 1 and container-local tmpfs only, then start the actual Agent service; verify mount metadata, root process identity, sustained active state, and `/run/serval` plus socket owner/group/modes (`0750` and `0660`).

## 2. Credential and peer integration checks

- [x] 2.1 Create disposable admitted and PAM-disallowed accounts after container startup and supply a fresh test password through stdin only; verify no password or hash appears in build context, image history, Docker inspect metadata, command lines, logs, or test artifacts.
- [x] 2.2 Send typed login requests as `serval-web`; verify valid credentials yield a session while wrong credentials and the disallowed account yield sanitized denial without a session, with no credential or session content printed.
- [x] 2.3 Send a syntactically valid login request from another non-Web UID and from root; verify both peers are rejected before request dispatch while the configured `serval-web` UID succeeds.

## 3. Startup failures and automation

- [x] 3.1 Mutate one container-local deployment condition at a time, including a mismatched Web identity, unsafe configuration ownership or mode, and missing PAM service file; verify each bounded Agent start fails and leaves no listening socket, then restore the fixture state.
- [x] 3.2 Add a Linux CI gate and a single local runner that executes all positive and negative cases, rejects host mounts/ports, and removes only task-owned containers and staging on success or failure; verify a passing run and an injected failure both clean up.
- [x] 3.3 Document local Docker Desktop Linux-container prerequisites, invocation, expected result, credential handling, and cleanup in the canonical developer guide; verify the commands match the CI runner and do not request a password through an argument or environment variable.
- [x] 3.4 Run the relevant formatting, build, test, Docker integration, and secret-leak checks, recording passed, failed, or unverified results; complete the mandatory independent post-change review and resolve all blocking findings before considering implementation complete.
