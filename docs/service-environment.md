# Viewing service environment declarations

Open a service's details page and find **Środowisko usługi**. This section shows
supported configuration declarations, not the running process environment.
Names are sorted ordinally. Every value initially displays the same mask,
including empty values. Source labels identify aggregate systemd declarations
or numbered environment-file occurrences, without revealing filesystem paths.

Page access requires `Service.View`; environment metadata requires
`Environment.View`. **Pokaż** also requires `Environment.Reveal`. Administrators
can grant these separately on the grants page; dependent grants are never added
automatically. Protected services remain unavailable to administrators too.

Choose **Pokaż** on one row to fetch its current value. Multiline values display
as text, and an empty value is labeled **Pusta wartość** only after disclosure.
The source label updates from the same fresh read. **Ukryj** clears the value;
choosing **Pokaż** again makes another authorized request. There is no timer.
Refresh, navigation and history restoration discard values and require fresh
authorization. Values are never saved in browser storage or included in URLs.

Buttons support normal Tab navigation and Enter/Space activation. Long names
and multiline values wrap within the section. A pending reveal can be canceled
with **Ukryj**; a late response cannot restore the hidden value.

Empty declarations have an explicit empty state. A failed or changing
configuration shows a sanitized error with **Spróbuj ponownie**, without partial
rows. Service-state details remain usable independently. A failed reveal leaves
the row masked. Revoked permissions apply to subsequent requests; they cannot
recall a value already read by an operator.
