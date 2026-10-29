# Spec Delta

## Purpose

Allow users to find and inspect the system services available to them through an accessible read-only browser interface.

## ADDED Requirements

### Requirement: Inventory presentation
The inventory SHALL render only Agent-authorized records with columns Usługa, Opis, Stan, Podstan and Załadowanie. Canonical service identifiers SHALL be full, encoded links to details. Descriptions and state values SHALL be rendered as text, never interpreted as markup.

#### Scenario: Available records
- **WHEN** the inventory request succeeds
- **THEN** each returned record displays its identifier, description and three states
- **AND** no protected or unauthorized record is added by the frontend

#### Scenario: Untrusted metadata
- **WHEN** a description or identifier contains markup-like characters or a long string
- **THEN** it is encoded and remains inspectable without script execution or unintended navigation

### Requirement: Search and state filtering
Inventory SHALL offer case-insensitive substring search over identifier and description, combined with an exact active-state filter using AND. Filters SHALL operate only on the currently authorized inventory, show a result count and support clearing. They SHALL NOT change identifier matching for authorization.

#### Scenario: Combined filters
- **WHEN** a user enters a name fragment and selects active
- **THEN** only authorized records matching the text and active state remain and the count shows matching records out of the authorized total

#### Scenario: Unavailable filter value
- **WHEN** a selected state has no matching record after refresh
- **THEN** the selection remains explicit and yields zero results until changed or cleared

#### Scenario: Bounded filter input
- **WHEN** search exceeds 256 characters, state exceeds 64 characters, or either parameter is repeated
- **THEN** the UI returns a generic invalid-filter response without reflecting the rejected values or broadening access

### Requirement: Browsing context and refresh
The inventory SHALL preserve search and state selection across refresh and list-to-detail-to-list navigation. Refresh SHALL request current authorized data. Browsing context SHALL contain only filter values, SHALL NOT accept an arbitrary return destination and SHALL NOT persist service records or credentials in browser storage.

#### Scenario: Return to filtered list
- **WHEN** a user opens a record from a filtered list and follows Wróć do usług
- **THEN** the same filters are applied to a newly authorized inventory response

#### Scenario: Session expires
- **WHEN** refresh or navigation discovers an invalid session
- **THEN** existing session invalidation and login redirection occur and no old inventory is substituted for the failed response

#### Scenario: Browser history restoration
- **WHEN** an enhanced authenticated page is restored from browser back-forward cache
- **THEN** it reloads through the server before reusing the prior service view
- **AND** session-dependent responses retain existing no-store protections

### Requirement: Accurate status labels
Active-state presentation SHALL provide text and a symbol: active as Aktywna, inactive as Nieaktywna, failed as Błąd, activating as Aktywacja, deactivating as Dezaktywacja and reloading as Przeładowanie. Other nonempty values SHALL remain visible with neutral styling; an absent value SHALL show Nieznany stan.

#### Scenario: Inactive and unfamiliar states
- **WHEN** records have inactive or unfamiliar active-state values
- **THEN** they are shown neutrally rather than as failures or successful active services
- **AND** raw SubState and LoadState remain available unchanged

### Requirement: Empty pending and failed inventory states
Inventory SHALL distinguish successful empty data, empty filtered results, pending navigation and failed retrieval. Failure SHALL offer retry without displaying stale rows or claiming a successful zero-service result.

#### Scenario: No authorized records
- **WHEN** a successful response contains no records
- **THEN** the page shows Brak usług do wyświetlenia.

#### Scenario: No matches
- **WHEN** records exist but filters match none
- **THEN** the page shows Brak wyników. and Wyczyść filtry

#### Scenario: Refresh pending or failed
- **WHEN** an enhanced refresh begins
- **THEN** the UI announces Pobieranie usług… without presenting a new zero count
- **WHEN** retrieval fails
- **THEN** the returned page shows Lista usług jest chwilowo niedostępna. with retry and no prior rows

### Requirement: Read-only detail presentation
Details SHALL show canonical identifier, description, active-state label and raw active state, substate and load state in Stan usługi and Identyfikacja sections. They SHALL provide refresh and return navigation without inventing unsupported fields or operations.

#### Scenario: Inspect service
- **WHEN** inspection succeeds
- **THEN** the returned canonical identifier and read-only metadata are shown
- **AND** no autostart, lifecycle buttons, environment values, metrics or per-service permission tab appear

#### Scenario: Hidden or invalid target
- **WHEN** inspection returns denied, not found or invalid request
- **THEN** the existing indistinguishable HTTP 404 response remains and no target metadata is displayed

#### Scenario: Detail retrieval unavailable
- **WHEN** inspection is temporarily unavailable
- **THEN** the UI shows a generic Polish unavailable message with retry and return navigation, without stale metadata
