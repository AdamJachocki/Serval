# Application Shell Specification

## Purpose

Provide a consistent, accessible Obsidian interface around Serval's existing browser workflows across desktop and small screens.

## Requirements

### Requirement: Coherent Obsidian presentation
The interface SHALL use the selected dark Obsidian design, one shared brand mark, consistent typography and reusable visual states. Browser controls and text SHALL remain native accessible content rather than flattened screenshots. Fonts and decorative assets SHALL be served locally.

#### Scenario: Navigating between screens
- **WHEN** a user moves between login, inventory and details
- **THEN** the same brand, dark palette and component styling appear, with decoration confined to login
- **AND** no third-party asset request is required to render the interface

### Requirement: Application navigation
Service and grant pages SHALL provide one primary navigation containing Usługi, Uprawnienia and a POST-based Wyloguj się action. The current area SHALL be identified visually and programmatically. Login SHALL omit application navigation. Navigation SHALL NOT assert roles or identity unavailable from the existing contract.

#### Scenario: Service detail navigation
- **WHEN** a user opens service details
- **THEN** Usługi is marked current and a breadcrumb and explicit return link identify the list
- **AND** no duplicate primary menu or fabricated account or host selector appears

#### Scenario: Grant access denied
- **WHEN** a non-administrator follows Uprawnienia
- **THEN** the existing server-side denial and HTTP 403 behavior remain in effect
- **AND** the presence of the navigation link does not imply authorization

### Requirement: Polish interface copy
Serval-owned visible copy, document language, form validation messages and accessible control names SHALL be Polish across the shared shell, login, service, grant and ancillary pages. Technical identifiers, permissions and raw system states SHALL remain unchanged.

#### Scenario: Grant management
- **WHEN** an administrator opens the styled grants page
- **THEN** labels and feedback are Polish while Service.View and service identifiers retain their original values
- **AND** existing add and remove operations and form protections are preserved

### Requirement: Responsive layout
The interface SHALL provide a desktop sidebar at widths of at least 1024 CSS pixels and an in-flow collapsible navigation below that width. At 320 CSS pixels, controls and content SHALL remain reachable without horizontal page overflow; only data tables SHALL scroll horizontally within a named region.

#### Scenario: Narrow viewport
- **WHEN** a user opens the inventory at 360 CSS pixels
- **THEN** the toolbar wraps, all table columns remain available by horizontal table scrolling and navigation can be opened using a labeled Menu control

#### Scenario: Zoom and long content
- **WHEN** a user zooms to 400 percent or encounters a long service identifier
- **THEN** content remains readable without controls overlapping or information being irretrievably clipped
- **AND** details wrap identifiers and labels into the available width

### Requirement: Accessible interaction
The interface SHALL offer visible keyboard focus, a skip link, semantic forms and tables, text labels for status, and accessible names for icon actions. Normal text SHALL meet 4.5:1 contrast and essential control boundaries and focus indicators 3:1. Nonessential motion SHALL respect reduced-motion preferences.

#### Scenario: Keyboard-only use
- **WHEN** a user traverses login, navigation, filters and service links using only the keyboard
- **THEN** focus is visible, order is logical and all actions are reachable without hover or a pointer
- **AND** dynamic counts and pending feedback are announced without repeatedly announcing the full table

#### Scenario: Script unavailable
- **WHEN** JavaScript fails or is disabled
- **THEN** login, logout, navigation, list filtering by submission, service links, refresh and grant forms remain usable
- **AND** enhancement-only controls are absent or have a usable native fallback
