# Security policy

Serval is designed as security-sensitive Linux administration software. We appreciate responsible, private reports that help protect future users.

## Supported versions

Serval has no supported release yet. The default branch receives security fixes during development, but the bootstrap is not suitable for production deployment.

Once releases are available, this section will identify the versions receiving security updates.

## Reporting a vulnerability

Do not open a public issue, discussion, or pull request for a suspected vulnerability. Use GitHub's **Security** tab and select **Report a vulnerability** to submit a private report to the maintainers.

Include only the information needed to reproduce and assess the issue:

- affected revision or version;
- affected component and security boundary;
- prerequisites and reproducible steps;
- expected and observed behavior;
- likely impact;
- suggested mitigation, if known.

Never include real credentials, environment-variable values, personal data, or production system details. Use synthetic values and redact logs before attaching them.

Maintainers will acknowledge the report, assess severity and scope, coordinate a fix and tests, and agree on disclosure timing with the reporter. Response targets will be published once the project has a staffed release and security-response process.

## Security expectations

The authoritative development invariants are maintained in [`AGENTS.md`](AGENTS.md). In particular, reports involving privilege boundaries, protected services, command or path injection, authorization bypass, secret exposure, or unintended service lifecycle operations are in scope.
