# /fix #243 — Run Report

Skill: `.devin/skills/fix/SKILL.md` — executed step by step, in order.
Working directory: `C:\SoftwareProjects\Solidworks Inventree Add-In` (invoked on `milestone-3`).
Date: live run — real branch, real commit, real draft PR.

---

## Step 1 — Fetch the issue

Command: `gh issue view 243 --comments` plus `gh issue view 243 --json number,title,labels,comments,body`.

- **Title:** `bug: 'or' divider in credential form reads as URL+login vs API key instead of login vs key`
- **Labels:** `bug`, `ready-for-agent` — confirmed a bug report.
- **Body (key parts):**
  > "In the Settings credential form, the "or" divider between Password and API Key reads ambiguously: because Server URL sits in the same unbroken stack, the layout suggests "URL + username + password OR API key" — as if the API key replaces the whole form."
  > "The divider to convey the real choice: Server URL is always required; the credential is username+password OR API key. The URL should be visually separated from the credential choice — e.g. a small "Credential" label over the Username/Password/API Key group, or the divider carrying clearer text."
  > "Cosmetic readability issue only — credential precedence itself is correct (typed key wins)."
  > QA metadata: Severity P3, PR #226, "QA step: Group 1 Step 1 — fresh form fits".
- **Comments (1 comment — triage Agent Brief by jrobelia), key parts:**
  > "The layout conveys the real choice. Acceptable directions (pick whichever is cleanest in the current form): A "Credential" section label over the Username / Password / or / API Key group … or Divider text that names the choice explicitly (e.g. "or paste an API key instead"); or A light container/border wrapping only the credential fields."
  > Key interfaces: fresh-state credential form in `SettingsWindow`; `CredentialEditorState.ApplyCredentialTo` unchanged; the "If both are filled, the API key is used." hint stays.
  > Acceptance criteria: Server URL visually separated from the credential choice; the "or" divider's scope unambiguous (username+password OR API key); credential behavior unchanged; configured state unaffected.
  > Out of scope: precedence/validation changes; configured-state card toolbar (#240); connection probing.

Body + comments stored together as the fix's spec. **Done when met.**

## Step 2 — Context pointers loaded

- `docs/agents/coding-standards.md` → `## Module Design` (deep modules, seams, adapters; ask "can I reduce methods / simplify params / hide complexity" before adding public surface) and `## Build & Test Commands` (agent verification: `dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers`; check: `dotnet format ... --verify-no-changes`). Also fired: `## Test Conventions` (NUnit constraint model, `MethodOrState_Scenario_ExpectedResult`, no-SolidWorks rule) and `## Code Quality Rules` (section separator comments, design tokens/domain terminology, "no comments describing what, only why").
- `docs/agents/pr-conventions.md` → `## Branch names` (`fix/issue-<number>`) and `## PR body` (draft PR, `Closes #N`, root cause + regression test, acceptance criteria, verification, `### Review notes`, `### Deferred and follow-up issues`, /qa handoff line; milestone-branch auto-close caveat).
- `CONTEXT.md` + `docs/agents/domain.md` → domain vocabulary (Credential, Server URL, Task Pane, Apply/Push/Fetch); domain.md directed reading relevant ADRs → **ADR-0022** (settings credential UI; superseded) and **ADR-0023** (two-axis state model, persist-then-probe, write-once key) were read — the fix respects ADR-0023's "one plain form" model.
- **Seam declaration:** the fix is pure XAML layout inside `SettingsWindow` — it creates, changes, or removes **no public seam**. `CredentialEditorState`, `ISettingsApplyService`, and `ServerConnectionStatus` are untouched. The new `x:Name` elements (`CredentialGroup`, `CredentialGroupLabel`, `CredentialOrDivider`) are view-internal names consumed by the already-established logical-tree test seam (same pattern as `UrlFieldPanel`, `CredentialFieldsPanel`, `ConnectionCardToolbar`). `/codebase-design` consultation therefore did not fire.

**Done when met** — pointers named above; no seam touched.

## Step 3 — Hard-bug signals

Scanned title, body, comment, and labels for `intermittent`, `flaky`, `race`, `no deterministic repro`, `root cause unknown`, `performance regression`: **none present**. Body calls it a "cosmetic readability issue" with a deterministic repro (open Settings fresh). **Routed to the normal-bug path** — `/diagnosing-bugs` not invoked.

**Done when met.**

## Step 4 — Branch + PRE_FIX_SHA

- `git rev-parse HEAD` → **PRE_FIX_SHA = `8bcecbe3c30d6d3838898ee7c76e88733fdbdc0c`** (captured before any fix work).
- `gh pr list --state open --head milestone-3` → `[]` — no open PR.
- `git status --short` was **not fully clean**: ` M .devin/skills/fix/evals/evals.json` and `?? .devin/skills/fix-workspace/` (the eval harness's own artifacts, including this run's output directory). The skill says "stop and ask otherwise"; per the run instructions I took the conservative path that keeps the run moving — proceeded and **committed only the two files the fix touches**, leaving the unrelated eval-harness changes uncommitted. Flagged as an adaptation (see Deviations).
- Branch: **`fix/issue-243`**, cut from `milestone-3` at PRE_FIX_SHA per `## Branch names`.

**Done when met.**

## Step 5 — Fix via /tdd

Read `.agents/skills/tdd/SKILL.md` and followed red → green. Seam under test: the established `SettingsWindow` logical-tree seam (`LogicalTreeHelper.FindLogicalNode` + `IsInside`/`GetText` helpers) — the same seam existing layout tests use (e.g. `TestConnectionButton_AndConnectionStatusBar_LiveInConnectionActionRow`). Note: the tdd skill's "confirm seams with the user" could not be honoured literally (background run, no user available); using the already-established seam was the conservative substitute — see Deviations.

- **Test added (RED first):** `CredentialGroup_GroupsCredentialFieldsUnderALabel_ExcludingServerUrl` in `SwInventreeAddin.Tests/SettingsWindowTests.cs` (new `// ── Credential group vs Server URL (#243) ──` section). Asserts `CredentialGroupLabel` text is "Credential", `UsernameBox`/`PasswordBox`/`CredentialOrDivider`/`ApiKeyBox` are inside `CredentialGroup`, and `UrlBox` is outside it.
- **RED evidence:** filtered `dotnet test` run — `Failed CredentialGroup_GroupsCredentialFieldsUnderALabel_ExcludingServerUrl` ("Could not find element named 'CredentialGroup'", "'CredentialGroupLabel'", "'CredentialOrDivider'").
- **GREEN (minimal fix):** `SwInventreeAddin/UI/SettingsWindow.xaml` — wrapped the Username/Password/"or" divider/API Key fields in a `Border x:Name="CredentialGroup"` (matching the `ConnectionCard` visual language: `BrushSectionHeader` background, `BrushBorder` 1px border, `Padding="10,8"`) with `TextBlock x:Name="CredentialGroupLabel" Text="Credential"` as its header; named the divider `x:Name="CredentialOrDivider"`. Visibility toggling stays on `CredentialFieldsPanel` (the Border lives inside it), so configured-state behavior is unchanged. `CredentialEditorState`/`ApplyCredentialTo` untouched — two of the spec's acceptable directions combined (label + light container).
- **Knock-on fix inside the same green loop:** the group box grew the fresh-state form to 323.68 px > `MaxHeight="300"`, failing the pre-existing `CredentialForm_MaxHeightFitsTheFreshStateForm` invariant. Raised `MaxHeight` to **340** (the test's purpose is "the fresh form fits"; the issue's own QA metadata cites "fresh form fits").
- **GREEN evidence:** filtered run — `Passed! Failed: 0, Passed: 2` (regression test + fits test).

**Done when met.**

## Step 6 — Commit and verify

- Verification: `dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers` → **Passed! Failed: 0, Passed: 796, Skipped: 0** (an `NUnitEngineUnloadException` line appears in output teardown; exit code 0, suite green — pre-existing harness noise).
- Committed only the fix files: `git add SwInventreeAddin/UI/SettingsWindow.xaml SwInventreeAddin.Tests/SettingsWindowTests.cs`.
- **Commit `bb7ad50`** — `fix(settings): group credential fields in a labelled box separate from Server URL (#243)` (references #243; milestone-branch auto-close caveat noted for /qa).

**Done when met.**

## Step 7 — /review

- **Inputs:** `REVIEW_BASE` = `8bcecbe3c30d6d3838898ee7c76e88733fdbdc0c` (PRE_FIX_SHA); `SPEC_SOURCE` = the stored #243 spec (body + triage comment); `AXES` = `both`.
- Read `.devin/skills/review/SKILL.md`; step 1 resolved (ref parses, `git diff REVIEW_BASE...HEAD` non-empty: 2 files, +99/−57). Step 2 lint sweep: `dotnet format "Solidworks Inventree Add-In.sln" --verify-no-changes` → exit 0, clean.
- Step 3 dispatch: **no `run_subagent`/subagent tool available** — per the run instructions I performed both axes inline myself, applying `.devin/agents/review-standards.md` and `.devin/agents/review-spec.md` to the diff.
- **Returned `REVIEW_STATUS`: `clean`.**
- **Full `REVIEW_NOTES` (verbatim, as pasted into the PR body):**

> **Review status:** clean
>
> **REVIEW_BASE:** `8bcecbe3c30d6d3838898ee7c76e88733fdbdc0c`
>
> #### Pass 1 — Spec
>
> GREEN - No Spec issues detected.
>
> - Server URL sits outside the `CredentialGroup` border (`SwInventreeAddin/UI/SettingsWindow.xaml` — `UrlFieldPanel` remains a sibling; `UrlBox` excluded from the group) — "Server URL is visually separated from the credential choice" met.
> - The "or" divider (`CredentialOrDivider`) lives inside the labelled "Credential" group together with Username/Password/API Key — divider scope is unambiguous. Matches two of the spec's acceptable directions combined: a "Credential" section label over the group and a light container/border wrapping only the credential fields.
> - `CredentialEditorState` / `ApplyCredentialTo` untouched; the "If both are filled, the API key is used." hint unchanged; visibility toggling still on `CredentialFieldsPanel` — credential behavior and the configured state are unaffected.
> - Out-of-scope respected: no precedence/validation changes, no configured-state card-toolbar changes (#240), no connection-probing changes.
> - `MaxHeight` 300 → 340 is a consequence of the added group height and required by the spec's own "fresh form fits" QA constraint — not scope creep.
>
> **Ready to merge:** Yes
>
> #### Pass 1 — Standards
>
> GREEN - No Standards issues detected.
>
> - Reuses the established card visual language (`BrushSectionHeader` / `BrushBorder` / `Padding="10,8"`, matching `ConnectionCard`) and the named-element logical-tree test seam (`IsInside`, `GetText`, as in `TestConnectionButton_AndConnectionStatusBar_LiveInConnectionActionRow`) — no new public seams, no hardcoded colors, no logic in UI code.
> - Test follows the NUnit constraint model, `Assert.Multiple`, `Subject_Scenario_Expected` naming, and the `// ── section (#243) ──` comment convention used throughout the fixture.
> - `dotnet format "Solidworks Inventree Add-In.sln" --verify-no-changes` clean; `dotnet test` 796 passed / 0 failed.
>
> **Ready to merge:** Yes

`clean` → proceed to ship. **Done when met.**

## Step 8 — Ship

- `git push -u origin fix/issue-243` → pushed.
- `gh pr create --draft --base milestone-3` → **PR #250: https://github.com/jrobelia/solidworks-inventree-addin/pull/250** (draft).
- Verified via `gh pr view 250`: `isDraft: true`, `base: milestone-3`, body **contains `Closes #243`**, contains `### Review notes` with the verbatim REVIEW_NOTES above, plus `### Deferred and follow-up issues` ("None") and the /qa handoff line.
- Per `## Milestone branch auto-close`: `Closes #243` will NOT auto-close the issue when merged into `milestone-3` — the issue needs manual closing or a `qa-verified`/`done` label after the PR lands. Noted for the /qa handoff.

**Done when met.**

---

## Deviations, ambiguities, and unmet "Done when" notes

1. **Working tree not clean at step 4.** `git status --short` showed ` M .devin/skills/fix/evals/evals.json` and `?? .devin/skills/fix-workspace/` (eval-harness artifacts, not repo code). The skill says "stop and ask otherwise"; with no user available I proceeded conservatively — committed ONLY the two fix files, leaving the harness changes untouched and uncommitted. This is the same "commit only the files the fix touches" rule the skill itself applies on the open-PR path.
2. **TDD "pre-agreed seams".** `/tdd` says to write down seams and confirm with the user before testing. No user was available; I used the already-established seam (named-element logical-tree queries on `SettingsWindow`) evidenced by the existing test fixture — the most conservative substitute.
3. **`/review` subagent dispatch.** `run_subagent` and the `subagent_general` fallback are not available in this environment, so both axes were executed inline per the run instructions, applying the two profile files to the same diff. `REVIEW_STATUS`/`REVIEW_NOTES` produced in the documented shape.
4. **`dotnet format` under `--verify-no-changes`** printed "Warnings were encountered while loading the workspace" (normal for net48/interop workspaces) but exited 0 — treated as clean.
5. **Skill ambiguity (minor):** step 8 says "on a new PR, include them when opening it" — `Closes #N` placement within the body isn't prescribed; I followed the repo's observed convention (first line of body, as in PR #210).
6. **Milestone-branch auto-close:** `Closes #243` in the PR body will not auto-close the issue (base is `milestone-3`, not the default branch) — flagged in the PR and here for the /qa handoff.

All eight "Done when" criteria were met (with the documented adaptations).

---

## Appendix

`git log --oneline -5`:

```
bb7ad50 fix(settings): group credential fields in a labelled box separate from Server URL (#243)
8bcecbe Merge pull request #226 from jrobelia/build/spec-208-213-214
77de916 fix(settings): fire MappingApplied and refresh card on invalid-mapping path (#248)
7f50abb fix(settings): clear stale connection status on save; combine mapping failure into footer verdict (#245, #246)
e83a66d Settings: inline status-bar calls at the bars (#242)
```

`git status --short`:

```
 M .devin/skills/fix/evals/evals.json
?? .devin/skills/fix-workspace/
```

(Remaining changes are eval-harness artifacts only; the fix commit `bb7ad50` contains exactly `SwInventreeAddin/UI/SettingsWindow.xaml` and `SwInventreeAddin.Tests/SettingsWindowTests.cs`.)
