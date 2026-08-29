## Summary

Describe the problem, the implemented change, and what remains intentionally out of scope.

## Security and architecture impact

- Trust boundaries or attacker-controlled inputs changed:
- Privileged capabilities added or changed:
- Authorization, protected-service, secret-handling, command-injection, or path-safety impact:
- Architecture dependency changes:

Write `None` only after evaluating each item. Never include credentials or environment-variable values.

## Validation

- [ ] `dotnet format Serval.slnx --verify-no-changes --no-restore`
- [ ] `dotnet build Serval.slnx --configuration Release --no-restore`
- [ ] `dotnet test --solution Serval.slnx --configuration Release --no-build --no-restore`
- [ ] Relevant permitted and denied paths are tested.
- [ ] Real Linux/systemd testing is included when required by `AGENTS.md` and the project skills.
- [ ] Documentation is updated for changed behavior or assumptions.

List any additional manual or integration validation:

## Checklist

- [ ] I read and followed `AGENTS.md` and every applicable project-specific skill.
- [ ] The change is focused and adds no speculative dependencies or abstractions.
- [ ] Environment values, credentials, and other secrets are absent from code, logs, tests, screenshots, and this pull request.
- [ ] Save/apply and service restart remain separate explicit operations.
- [ ] The network-facing process remains unprivileged.
