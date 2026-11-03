# Spec Delta

## Purpose

Provide the Obsidian sign-in experience while preserving existing Linux-account authentication and browser-session behavior.

## ADDED Requirements

### Requirement: Focused sign-in form
The login page SHALL show a labeled Linux username field, password field and Zaloguj się action. It SHALL support password managers and pasting, begin with empty fields on a fresh visit, and omit signup, social login and persistent-login options. The decorative panel SHALL disappear on phone-sized screens.

#### Scenario: Fresh login
- **WHEN** a user opens login
- **THEN** the page shows Zaloguj się and Użyj konta systemowego Linux., with username and current-password autocomplete semantics and no application sidebar

### Requirement: Password visibility
With scripting available, a labeled non-submitting control SHALL toggle only the password field's visibility and expose its pressed state. The password SHALL initially be concealed and toggling SHALL retain focus without submitting the form.

#### Scenario: Show and hide
- **WHEN** the user activates Pokaż hasło and then Ukryj hasło
- **THEN** the same entered value is revealed and concealed without a network request, form submission or loss of keyboard focus

### Requirement: Safe validation feedback
Login SHALL preserve existing input bounds and authentication validation semantics. It SHALL show Polish validation feedback associated with affected fields and a focused error summary after a failed submission. Login rejection SHALL remain generic across missing accounts, invalid credentials and PAM account denial.

#### Scenario: Invalid username input
- **WHEN** username is missing or exceeds its existing bound
- **THEN** the form shows a Polish validation error and does not submit an authentication operation

#### Scenario: Empty password
- **WHEN** a validly formed username is submitted with an empty password
- **THEN** the UI does not introduce a new required-password rule and the existing authentication path decides the result

#### Scenario: Authentication rejection
- **WHEN** the existing login operation rejects credentials or cannot establish a session
- **THEN** the page displays Nie udało się zalogować. Sprawdź dane i spróbuj ponownie.
- **AND** it does not disclose account existence, native authentication details or the submitted password

### Requirement: Submission lifecycle
An enhanced valid login submission SHALL indicate Logowanie… and prevent duplicate submission without omitting credential fields from the request. Returned forms SHALL never repopulate the password. Validation failure and history restoration SHALL leave the form usable.

#### Scenario: Pending and returned form
- **WHEN** login is submitted and a failure response returns
- **THEN** pending feedback ends, the password is empty and the user can retry

#### Scenario: Rejected input before authentication
- **WHEN** server validation rejects an overlong field before authentication runs
- **THEN** neither response markup nor validation messages contain the submitted password

### Requirement: Existing security contract
The login interface SHALL retain existing POST anti-forgery checks, session cookie behavior and success redirect. It SHALL NOT store credentials in browser storage, URLs, diagnostic output or persistent frontend state.

#### Scenario: Missing anti-forgery token
- **WHEN** a login POST lacks a valid anti-forgery token
- **THEN** the request is rejected before authentication, as in the existing application

#### Scenario: Successful sign-in
- **WHEN** authentication succeeds
- **THEN** the existing session is established and the user is redirected to the service inventory without restoring filters from an earlier login
