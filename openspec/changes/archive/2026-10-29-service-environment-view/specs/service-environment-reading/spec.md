# Spec Delta

## MODIFIED Requirements

### Requirement: Enforce target policy before sensitive acquisition
Every production entry and construction path SHALL validate the identifier, resolve identity, and enforce canonical/alias and privileged-family protection before querying value-bearing properties or reading source contents. Malformed identifiers and bare templates SHALL be rejected safely. An absent concrete service SHALL return `NotFound`; built-in protected targets SHALL return `ProtectedTarget`, while Serval privileged-family exclusions SHALL preserve the established identity-policy outcome. Calling the library directly SHALL NOT bypass these checks. The capability SHALL be available to the Agent through its authenticated and authorized environment operations; Web SHALL NOT register or call the reader directly. Agent callers MUST authenticate and authorize the exact canonical service operation before invoking it. Secret access SHALL be limited to the dedicated single-variable disclosure path, without a public unrestricted reveal API or whole-result serialization.

#### Scenario: Direct protected alias request
- **WHEN** a caller bypasses any hypothetical Web validation and requests an alias of a protected service
- **THEN** the library denies the read before value-bearing property or content access

#### Scenario: Malicious or excluded identity
- **WHEN** a direct request contains an invalid identifier, a bare template, or an excluded Serval privileged instance
- **THEN** the established identity policy rejects it without exposing environment contents

#### Scenario: Agent integration
- **WHEN** the Agent invokes a metadata read or single-variable reveal
- **THEN** it has authenticated the session and authorized the operation for the resolved canonical service before calling the reader
- **AND** the library independently preserves its target checks, consistency validation, bounds and disposal contract
