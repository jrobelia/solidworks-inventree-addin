# Fix report — issue #243: credential form "or" divider scope ambiguity

## Bug

In the Settings "Inventree Server Connection" form, the Server URL field sat in
the same unbroken stack as Username / Password / "or" / API Key, so the "or"
divider read as "URL + username + password OR API key" — as if the API key
replaced the whole form. Cosmetic readability issue; credential precedence was
already correct.

## Files changed

- `SwInventreeAddin/UI/SettingsWindow.xaml` (only file touched)

## Approach

Combined the two cleanest acceptable directions from the triage brief, both
inside the existing `CredentialFieldsPanel` (the panel that is shown exactly
when the credential group is shown — fresh state, server-only state, and the
"Change credential" toolbar slice):

1. **"Credential" group header** — a `TextBlock` ("Credential",
   `FontWeight="SemiBold"`, `BrushForeground`, `Margin="0,10,0,0"`) added as the
   first child of `CredentialFieldsPanel`, between the Server URL field and the
   Username label. It visually fences the credential choice off from the URL
   field and matches the terminology already used by the status card's
   "Credential" row and the "Change credential" toolbar button.
2. **Explicit divider text** — the separator-grid divider text changed from
   "or" to "or paste an API key instead" (the wording suggested in the issue),
   naming the alternative so the divider cannot read as "everything above vs
   key".

3. **ScrollViewer `MaxHeight` 300 → 320** — the new header adds ~26 px to the
   fresh-state form's desired height (307.68 > 300). The cap exists to keep the
   expanded dialog inside small monitors (ADR-0022); 320 keeps that bound while
   letting the tallest form layout fit without scrolling, per the
   `CredentialForm_MaxHeightFitsTheFreshStateForm` test contract.

No code-behind changes. `CredentialEditorState.ApplyCredentialTo` precedence
(typed key wins), the API key placeholders, the "If both are filled, the API
key is used." hint, dirty gating, and the configured-state card/toolbar are all
untouched.

## Tests

- Ran `dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers`
  from the worktree root.
- First run: 794/795 passed; `CredentialForm_MaxHeightFitsTheFreshStateForm`
  failed because the header pushed the form's desired height (307.68) past the
  `MaxHeight="300"` cap — fixed by raising the cap to 320.
- Final run: **795/795 passed**, 0 failed (exit code 0; the runner prints a
  known `NUnitEngineUnloadException` on app-domain unload — cosmetic, present
  on baseline runs too).

## Commit

- `2ca8eb1` on `eval/baseline-243` —
  `fix(settings): scope credential form's or-divider to the credential choice (#243)`

## Acceptance criteria check

- Server URL visually separated from the credential choice — the "Credential"
  group header now sits between the URL field and the Username/Password/API Key
  group.
- "or" divider scope unambiguous — it lives inside the labelled Credential
  group and its text names the alternative ("or paste an API key instead").
- Credential behavior unchanged — XAML-only change; no C# touched.
- Configured state unaffected — `CredentialFieldsPanel` visibility logic is
  untouched; the header simply shows with the group it labels.
