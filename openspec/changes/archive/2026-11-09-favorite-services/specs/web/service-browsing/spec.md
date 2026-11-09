# Spec Delta

## MODIFIED Requirements

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

## ADDED Requirements

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
