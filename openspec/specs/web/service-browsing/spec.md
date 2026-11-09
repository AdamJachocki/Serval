# Service Browsing Specification

## Purpose

Allow users to find and inspect the system services available to them through an accessible read-only browser interface.

## Requirements

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
Inventory SHALL offer case-insensitive substring search over identifier and description, combined with an exact active-state filter using AND. Filters SHALL operate only on the currently authorized inventory, apply equally to the Ulubione and Pozostałe usługi sections, show one result count across both sections and support clearing. They SHALL NOT change identifier matching for authorization.

#### Scenario: Combined filters
- **WHEN** a user enters a name fragment and selects active
- **THEN** only authorized records matching the text and active state remain and the count shows matching records out of the authorized total

#### Scenario: Unavailable filter value
- **WHEN** a selected state has no matching record after refresh
- **THEN** the selection remains explicit and yields zero results until changed or cleared

#### Scenario: Bounded filter input
- **WHEN** search exceeds 256 characters, state exceeds 64 characters, or either parameter is repeated
- **THEN** the UI returns a generic invalid-filter response without reflecting the rejected values or broadening access

#### Scenario: Filters across sections
- **WHEN** a user has favorites and enters a search that matches one favorite and two other services
- **THEN** Ulubione shows the matching favorite, Pozostałe usługi shows the two other services, and the count reports three matching records out of the authorized total

#### Scenario: Section without matches
- **WHEN** filters match no record in one section but match records in the other
- **THEN** the section without matches shows no rows and the overall no-results state is not shown

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

### Requirement: Favorites section
When the authorized inventory contains at least one of the user's favorites, the inventory SHALL show an Ulubione section with those records above a Pozostałe usługi section with the remaining records. Both sections SHALL use the inventory columns and keep Agent order. Each record SHALL appear in exactly one section. With no favorites, the inventory SHALL render as a single list without either heading.

#### Scenario: User with favorites
- **WHEN** the inventory succeeds and two returned services are favorites
- **THEN** those two services appear under Ulubione, every other returned service appears under Pozostałe usługi, and no service appears twice

#### Scenario: User without favorites
- **WHEN** the inventory succeeds and no returned service is a favorite
- **THEN** no Ulubione heading or empty favorites placeholder is shown and the list looks as it did before favorites existed

#### Scenario: Inventory failure
- **WHEN** inventory retrieval fails
- **THEN** neither section nor any previously known favorite is shown

### Requirement: Favorite toggle
Every inventory row SHALL offer a keyboard-accessible star toggle that works without JavaScript. Its accessible name SHALL name the service and the action (Dodaj … do ulubionych or Usuń … z ulubionych), and its state SHALL not be conveyed by color or icon alone. Activating it SHALL send an antiforgery-protected POST and then return to the fixed `/Services` route with only the current bounded filter values.

#### Scenario: Add from list
- **WHEN** a user activates the star of a non-favorite service
- **THEN** after the round trip the service appears under Ulubione and the previous search and state filters are still applied

#### Scenario: Remove from list
- **WHEN** a user activates the star of a favorite service
- **THEN** after the round trip the service appears under Pozostałe usługi, or the list returns to a single section if no favorites remain

#### Scenario: Forged or replayed toggle
- **WHEN** a toggle POST lacks a valid antiforgery token or session
- **THEN** no favorite change is requested from Agent, and an invalid session follows the existing invalidation and login redirection

#### Scenario: Rejected toggle
- **WHEN** Agent rejects an add as not found or invalid
- **THEN** the user returns to the list with a generic Polish message that does not reveal whether the service exists or is protected

#### Scenario: Limit reached
- **WHEN** Agent rejects an add because the favorites limit is reached
- **THEN** the user returns to the list with a Polish message explaining that the limit of 100 favorites has been reached

#### Scenario: Toggle unavailable
- **WHEN** Agent reports the favorite operation as unavailable
- **THEN** the user returns to the list with a generic Polish message, and the list reflects only freshly authorized data

#### Scenario: Untrusted return target
- **WHEN** a toggle POST carries a return URL, a host, or over-length or repeated filter values
- **THEN** Web never redirects outside `/Services` and does not reflect rejected filter values
