Closes #243

## What changed

- The credential fields (Username, Password, the "or" divider, API Key) now sit inside a bordered box labelled **Credential**, below the Server URL field. The "or" divider can no longer read as "URL + username + password OR API key" — the Server URL stays outside the labelled group entirely.
- `CredentialFormScroll.MaxHeight` raised 300 → 340 so the tallest layout (the fresh-state form, ~324 px measured) still fits without scrolling.

## Root cause

Server URL, Username, Password, the "or" divider, and API Key were stacked in one unbroken column, so the divider appeared to offer the API key as an alternative to everything above it — including the always-required Server URL.

## Regression test

- `CredentialGroup_GroupsCredentialFieldsUnderALabel_ExcludingServerUrl` — asserts Username/Password/"or" divider/API Key live inside the labelled `CredentialGroup` while `UrlBox` sits outside it.

## Acceptance criteria

- [x] Server URL is visually separated from the credential choice — it no longer reads as part of either alternative
- [x] The "or" divider's scope is unambiguous: username+password OR API key
- [x] Credential behavior unchanged: precedence, placeholders, dirty gating all intact
- [x] The configured state is unaffected

## Verification

```
dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers
dotnet format "Solidworks Inventree Add-In.sln" --verify-no-changes
```

Result: 796 tests passed, 0 failed; `dotnet format` clean.

## Changed GUI flows and edge cases

- Settings window → Server Connection section, fresh state and authentication-required state: the credential fields now render inside a bordered "Credential" group under the Server URL field.
- Configured state → "Change credential": reveals the same labelled credential group (URL field hidden) — unchanged behavior, clearer grouping.
- Configured state → "Change server": reveals only the Server URL field — unchanged.
- Edge case: the group box adds ~24 px to the fresh-state form; `MaxHeight` raised to 340 to keep it unclipped on small monitors.

### Review notes

**Review status:** clean

**REVIEW_BASE:** `8bcecbe3c30d6d3838898ee7c76e88733fdbdc0c`

#### Pass 1 — Spec

GREEN - No Spec issues detected.

- Server URL sits outside the `CredentialGroup` border (`SwInventreeAddin/UI/SettingsWindow.xaml` — `UrlFieldPanel` remains a sibling; `UrlBox` excluded from the group) — "Server URL is visually separated from the credential choice" met.
- The "or" divider (`CredentialOrDivider`) lives inside the labelled "Credential" group together with Username/Password/API Key — divider scope is unambiguous. Matches two of the spec's acceptable directions combined: a "Credential" section label over the group and a light container/border wrapping only the credential fields.
- `CredentialEditorState` / `ApplyCredentialTo` untouched; the "If both are filled, the API key is used." hint unchanged; visibility toggling still on `CredentialFieldsPanel` — credential behavior and the configured state are unaffected.
- Out-of-scope respected: no precedence/validation changes, no configured-state card-toolbar changes (#240), no connection-probing changes.
- `MaxHeight` 300 → 340 is a consequence of the added group height and required by the spec's own "fresh form fits" QA constraint — not scope creep.

**Ready to merge:** Yes

#### Pass 1 — Standards

GREEN - No Standards issues detected.

- Reuses the established card visual language (`BrushSectionHeader` / `BrushBorder` / `Padding="10,8"`, matching `ConnectionCard`) and the named-element logical-tree test seam (`IsInside`, `GetText`, as in `TestConnectionButton_AndConnectionStatusBar_LiveInConnectionActionRow`) — no new public seams, no hardcoded colors, no logic in UI code.
- Test follows the NUnit constraint model, `Assert.Multiple`, `Subject_Scenario_Expected` naming, and the `// ── section (#243) ──` comment convention used throughout the fixture.
- `dotnet format "Solidworks Inventree Add-In.sln" --verify-no-changes` clean; `dotnet test` 796 passed / 0 failed.

**Ready to merge:** Yes

### Deferred and follow-up issues

None — no deferred or escalated findings.

Run /qa on this branch. /qa will take the PR out of draft if QA passes and ask whether to merge.
