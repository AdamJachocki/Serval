## Purpose

Define safe, bounded interpretation of one systemd `EnvironmentFile=` content source while preserving secret values and systemd-compatible assignment semantics.

## ADDED Requirements

### Requirement: Parse systemd environment-file grammar
The parser SHALL accept raw content only as strict UTF-8 and SHALL interpret comments, blank lines, ignored non-assignment lines, assignment separators, whitespace, unquoted values, single-quoted values, double-quoted values, escapes, continuations, and multiline quoted values according to the supported systemd `EnvironmentFile=` grammar. It SHALL reject malformed UTF-8, U+0000 NUL, U+FEFF byte-order marks in any position, every Unicode noncharacter, invalid variable names, invalid assignments, and unterminated quoted values as `InvalidSource` for the supplied source ID.

#### Scenario: Parse supported records
- **WHEN** a source contains comments, blank lines, ignored non-assignment lines, empty values, and valid unquoted, single-quoted, and double-quoted assignments spanning physical lines where the grammar permits
- **THEN** the parser returns the exact names and values produced by systemd for those assignments and identifies the supplied source as their provenance

#### Scenario: Reject malformed source as a whole
- **WHEN** any part of a source has invalid UTF-8, NUL, U+FEFF, a Unicode noncharacter, an invalid assignment or name, or unterminated quoting
- **THEN** the parser returns `InvalidSource` with only the supplied source ID and publishes no partial values

### Requirement: Use one version-independent compatibility subset
The parser SHALL implement one deterministic grammar for the supported systemd 249, 255, 257, and 259 baselines. A comment whose final byte before a single-byte LF or single-byte CR line ending is a backslash SHALL be rejected as `InvalidSource`, because systemd versions before 254 consume the next physical line as part of that comment while versions 254 and later begin a new record. A backslash before a CRLF pair SHALL follow the stable comment behavior shared by all supported baselines: the comment ends and the following physical line begins a new record. Parser behavior SHALL NOT depend on the host systemd version.

#### Scenario: Reject a version-divergent comment continuation
- **WHEN** a `#` or `;` comment ends with a backslash immediately before an LF-only or CR-only line ending
- **THEN** the parser returns `InvalidSource` with the supplied source ID on every supported baseline and does not interpret the following physical line

#### Scenario: Parse a stable CRLF comment
- **WHEN** a `#` or `;` comment ends with a backslash immediately before a CRLF pair
- **THEN** the parser ignores that comment and parses the following physical line identically on every supported baseline

#### Scenario: Parse a stable ordinary comment
- **WHEN** a comment does not end with a backslash immediately before its line ending
- **THEN** the parser ignores that comment and parses the following physical line identically on every supported baseline

### Requirement: Treat values as literal data
The parser SHALL NOT perform shell evaluation, variable interpolation, command substitution, specifier expansion, or dotenv-specific `export` handling. Percent signs, dollar signs, command-looking text, and assignment separators after the first separator SHALL remain literal value data except where systemd's quoting or escaping grammar removes syntax characters.

#### Scenario: Preserve command-looking content
- **WHEN** a valid assignment contains `$NAME`, `${NAME}`, `$(command)`, backticks, `%n`, `%%`, or additional `=` characters
- **THEN** the parser returns the systemd-decoded literal value without executing, interpolating, or expanding any content

#### Scenario: Do not accept export syntax
- **WHEN** a line uses `export NAME=value`
- **THEN** the parser returns `InvalidSource` because `export` is not part of the supported environment-file assignment grammar

### Requirement: Apply last-assignment-wins semantics
For repeated valid assignments in one source, the parser SHALL retain only the last value for each name, count every assignment before deduplication, and attribute each retained variable to the supplied request-local source ID. An explicitly empty value SHALL remain distinct from an absent variable.

#### Scenario: Replace a duplicate assignment
- **WHEN** one source assigns the same valid name more than once
- **THEN** the result contains one variable with the final value and the assignment count includes every occurrence

#### Scenario: Retain an empty winning value
- **WHEN** the final assignment for a name has no value after its separator
- **THEN** the result contains that name with a present zero-length value

### Requirement: Enforce fixed resource limits
The parser SHALL enforce the fixed inclusive limits of 1,048,576 raw bytes for one source, 65,536 UTF-8 bytes for one logical record including its syntax, and the caller's remaining allowance within the 16,384-assignment operation limit. Equality SHALL succeed when the content is otherwise valid, and the next byte or assignment SHALL return `LimitExceeded` for the supplied source ID. Ignored records and overridden assignments SHALL still contribute to the applicable byte or item accounting.

#### Scenario: Accept exact boundaries
- **WHEN** a valid source, logical record, or assignment count is exactly at its applicable limit
- **THEN** parsing succeeds and reports the exact source-byte and assignment accounting

#### Scenario: Reject a boundary overrun
- **WHEN** raw source bytes, accumulated logical-record bytes, or the remaining assignment allowance is exceeded by one
- **THEN** parsing stops with `LimitExceeded`, identifies only the supplied source ID, and publishes no values

### Requirement: Protect sensitive values throughout parsing
Parsed values SHALL be treated as secrets from receipt onward. Success metadata SHALL expose names, winning source IDs, source accounting, and assignment accounting but not values, value lengths, source text, or paths. Failures, exceptions, string representations, serialization, diagnostics, logs, and test output SHALL NOT contain source content or parsed values. Replaced, failed, canceled, and otherwise unpublished owned value buffers SHALL be cleared promptly.

#### Scenario: Failure after an earlier secret
- **WHEN** a source contains a valid secret assignment followed by invalid or over-limit content
- **THEN** the result and its serialization expose only the fixed failure classification and source ID, and the earlier value is not retained or disclosed

#### Scenario: Duplicate secret replacement
- **WHEN** a later assignment replaces an earlier secret value
- **THEN** only the winning value remains owned and the losing owned buffer is cleared promptly

### Requirement: Honor caller cancellation
The parser SHALL observe caller cancellation before processing, during bounded parsing work, and before publishing success. Cancellation SHALL throw `OperationCanceledException` carrying the caller's token, dispose unpublished secret state, and take precedence when already requested even for empty or invalid input.

#### Scenario: Cancellation precedes parsing
- **WHEN** the supplied cancellation token is already canceled
- **THEN** parsing throws `OperationCanceledException` with that token and publishes no result

#### Scenario: Cancellation interrupts parsing
- **WHEN** cancellation is observed after secret assignments have been processed but before success is published
- **THEN** unpublished secret buffers are cleared and `OperationCanceledException` with the caller's token is thrown

### Requirement: Match supported systemd baselines
The supported grammar SHALL be verified against real systemd using private synthetic environment files on the systemd 249, 255, 257, and 259 Debian/Ubuntu baselines. Verification SHALL cover quoting modes, escaping, continuation, multiline values, LF, CR, and CRLF comments, ignored non-assignment lines, empty values, duplicate assignments, permitted Unicode, forbidden Unicode scalars, and literal shell- and specifier-looking content without modifying non-test unit or environment files. The fixture SHALL also demonstrate the pre-254/post-254 LF-only and CR-only comment-continuation difference while verifying Serval's deterministic rejection on every baseline and the shared CRLF behavior.

#### Scenario: Compare with the real manager parser
- **WHEN** a synthetic fixture is loaded by a supported real-systemd baseline and the same file bytes are parsed by Serval
- **THEN** Serval's names and values match the manager-observed environment for every construction in the version-independent supported subset

#### Scenario: Verify the divergent construction policy
- **WHEN** the LF-only and CR-only comment-continuation fixtures are run on a pre-254 and a post-254 baseline
- **THEN** the fixture observes the documented baseline difference and Serval returns `InvalidSource` on both without publishing values

#### Scenario: Verify stable CRLF behavior
- **WHEN** the equivalent comment-continuation fixture uses CRLF on every supported baseline
- **THEN** both systemd and Serval ignore the comment and parse the following assignment
