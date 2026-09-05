# BOM Column Alias Blank Handling and BOM Compare Warnings

ADR-0018 says blank or missing individual mapping values are allowed, but the mapping editor still rejects blank BOM Column Aliases and BOM Compare silently turns missing IPN and Qty aliases into NoIpn rows and zero quantities.

## Decision

- The **Property Mapping** editor allows blank **BOM Column Aliases**.
- Duplicate aliases within one field and aliases shared across two fields still fail validation.
- A blank IPN or Qty alias gets a status warning and a required indicator, but the file can still be saved.
- A validation error no longer discards the whole draft; only **Cancel** reverts.
- When **BOM Compare** starts with a blank IPN or Qty alias, the add-in shows a warning that must be acknowledged before the compare window opens; the window opens after the warning is acknowledged.
- Reference and Note aliases may be blank without a warning.

## Consequences

- Tests that expect blank aliases to fail must be updated.
- The mapping editor status bar must support a warning severity.
- The **BOM Compare** pre-flight check must be able to read the current **Property Mapping**.
- The **Task Pane** must show a warning message before the compare window opens when the mapping is incomplete.
