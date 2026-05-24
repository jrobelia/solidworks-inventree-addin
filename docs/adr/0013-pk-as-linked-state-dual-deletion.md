# InvenTree Part PK as LINKED state identifier; Create Part requires both IPN and PK to be blank

The Task Pane enters LINKED state when either an IPN or an InvenTree Part PK is present in the
SolidWorks Document Properties. `LoadPartNumber` reads the PK property as a fallback when IPN
is blank. `FetchPartAsync` routes to `GetPartByPkAsync` on the PK path, bypassing the IPN-based
disambiguation chain entirely.

`CanCreatePart` gates on both IPN **and** InvenTree Part PK being blank. A document with only a
PK present (no IPN) is in LINKED state — Fetch is enabled, Create is disabled. To register a
Save-As copy as a new InvenTree part, the engineer must clear both the IPN and the PK custom
properties before clicking Create.

## Alternatives considered

**Status message + dual-enabled buttons.** When IPN is blank but PK is present, enable both
Fetch and Create and show a status bar message explaining the ambiguity. Create Part would
overwrite the old PK on success, self-correcting the state. Rejected because the status bar is
at the bottom of the Task Pane and easy to miss; having two primary actions simultaneously
active increases the chance of the engineer accidentally Fetching the old part and Pushing new
properties to the wrong InvenTree record.

## Consequences

- Save-As workflow now requires clearing two custom properties (IPN and InvenTree Part PK)
  instead of one before creating a new part.
- IPN-less InvenTree parts (servers that do not use IPNs) are fully supported via PK — the
  Task Pane enters LINKED state and Fetch works without an IPN ever being present.
- Manual PK entry: an engineer can link a SW document to a pre-existing IPN-less InvenTree
  part by manually adding the InvenTree Part PK value to the configured PK custom property.
  No code change required; falls out of the same LoadPartNumber logic.
