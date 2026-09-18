# BOM push guard source facts

PR title: "fix(bom): block Push for BOM lines with no matching InvenTree part" (Closes #173, part of milestone-3)

- During BOM Compare, a line whose IPN matches no InvenTree part gets BOM Diff State IpnNotFound.
- Before: those lines could be checked and pushed. The server rejected them with a generic error after the push started, and the error did not say which line failed.
- Now: IpnNotFound lines are unchecked and cannot be checked. The diff row explains "No InvenTree part with this IPN".
- Match, New, and Conflict lines still push normally.
- The Push Selected button count only counts checkable lines.
- 518 tests pass.
