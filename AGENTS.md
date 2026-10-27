# Agent Instructions — InvenTree Link for SOLIDWORKS

A C# WPF add-in for SolidWorks that bridges parts and assemblies to an InvenTree inventory server.

## User Guidelines

### Who is the user?

The user is the engineer and domain expert who is using this add-in. They are not a software developer.

### Reporting

When reporting information to me, be extremely concise and sacrifice grammar for the sake of concision.

## Quick commands

- Test: `dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers` — primary verification loop. Builds the add-in into a throwaway `bin_unit_test` folder, so it can run while SolidWorks is open.
- Build: `dotnet build "SwInventreeAddin/SwInventreeAddin.csproj" --disable-build-servers` — produces the SolidWorks-facing `bin\Debug\net48` output. Manual step only: SolidWorks locks `bin\Debug\net48\SwInventreeAddin.dll` while running, so agents verify with the test command and never run this one.
- Full solution build: `dotnet build "Solidworks Inventree Add-In.sln" --disable-build-servers` — builds the solution and the test project; same SolidWorks-closed constraint as Build, so it is not an agent verification command.
- Package manager: NuGet (restored automatically by `dotnet build`).
- AFK loop regression tests: `python -m pytest ".devin/skills/afk-loop/tests" -n 8 --reruns 1 -q` — ~7 min on 12 cores. Requires `pip install -r ".devin/skills/afk-loop/tests/requirements.txt"`; sequential `python -m unittest discover -s ".devin/skills/afk-loop/tests" -p "test_*.py"` still works but takes ~26 min. Isolated fixtures and stub external commands; no credentials or real agent sessions. Do not run while a live AFK loop or a manual checkout may move canonical HEAD — the suite reads and hashes package files mid-run. Use targeted tests (`-k <name>`) or `AFK_TEST_FAST=1` while iterating, then match the commit gate to what changed:
  - Instructions-only edits (SKILL.md, contracts.md prose): diff checks plus relevant focused tests.
  - `guard.py` or guard-policy changes: `python -m unittest discover -s ".devin/skills/afk-loop/tests" -p "test_guard.py" -v`.
  - `driver.py`, launchers (`afk-loop.sh`/`.ps1`), worker prompts (`prompts/`), persisted state, model selection, or the test harness (`stub_tools.py`, `test_*.py`): one full run without `AFK_TEST_FAST` before commit — checkpoint/resume integrity is cross-cutting, so scoping below the suite is unsafe there. Do not repeat it solely for later explanatory-text edits.
  - Merges: if the incoming branch already has a passing full-suite run and no runner-side commits (`driver.py`, `guard.py`, launchers, prompts, state, harness) landed on the base since its merge-base, that run stands — verify the merge with a targeted pass, not a full re-run.

## Skill layout

Custom skills live in `.devin/skills/`. `.agents/skills/` holds downloaded skills installed via `npx skills add` — reinstall with the installer rather than editing in place. `.devin/skills-retired/` holds inactive skills kept for reference.

## Design discipline

Before any design decision that creates, changes, or removes a public seam, read `docs/agents/coding-standards.md` `## Module Design`, consult `/codebase-design`, and proceed only when you can state the seam declaration it requires.

## Spec discipline

Before a spec is published to the issue tracker, run the **playback** in `.devin/skills/spec-playback/` — the spec re-narrated in the engineer's terms — and wait for their confirmation. Post the confirmed playback as a comment on the published spec issue.

## Where to look next

- [Build, test, language, naming, and code-quality rules](docs/agents/coding-standards.md)
- [UI design language — read before any XAML or GUI-visible change](docs/agents/design-language.md)
- [Branch and pull request conventions](docs/agents/pr-conventions.md)
- [Scope: what the add-in does and out-of-bounds](docs/agents/scope.md)
- [User communication preferences](docs/agents/user-preferences.md)
- [Domain glossary and ADRs](docs/agents/domain.md) — see also [GLOSSARY.md](GLOSSARY.md)
- [Issue tracker conventions](docs/agents/issue-tracker.md) — see also [triage labels](docs/agents/triage-labels.md)
