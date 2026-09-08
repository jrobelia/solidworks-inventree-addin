# Agent Instructions — SolidWorks InvenTree Add-In

A C# WPF add-in for SolidWorks that bridges parts and assemblies to an InvenTree inventory server.

## Quick commands

- Test: `dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers` — primary verification loop. Builds the add-in into a throwaway `bin_unit_test` folder, so it can run while SolidWorks is open.
- Build: `dotnet build "SwInventreeAddin/SwInventreeAddin.csproj" --disable-build-servers` — produces the SolidWorks-facing `bin\Debug\net48` output. Manual step only: SolidWorks locks `bin\Debug\net48\SwInventreeAddin.dll` while running, so agents verify with the test command and never run this one.
- Full solution build: `dotnet build "Solidworks Inventree Add-In.sln" --disable-build-servers` — builds the solution and the test project; same SolidWorks-closed constraint as Build, so it is not an agent verification command.
- Package manager: NuGet (restored automatically by `dotnet build`).

## Skill layout

Custom skills live in `.devin/skills/`. `.agents/skills/` holds downloaded skills installed via `npx skills add` — reinstall with the installer rather than editing in place. `.devin/skills-retired/` holds inactive skills kept for reference.

## Design discipline

Before any design decision that creates, changes, or removes a public seam, read `docs/agents/coding-standards.md` `## Module Design`, consult `/codebase-design`, and proceed only when you can state the seam declaration it requires.

## Where to look next

- [Build, test, language, naming, and code-quality rules](docs/agents/coding-standards.md)
- [Branch and pull request conventions](docs/agents/pr-conventions.md)
- [Scope: what the add-in does and out-of-bounds](docs/agents/scope.md)
- [User communication preferences](docs/agents/user-preferences.md)
- [Domain glossary and ADRs](docs/agents/domain.md) — see also [CONTEXT.md](CONTEXT.md)
- [Issue tracker conventions](docs/agents/issue-tracker.md) — see also [triage labels](docs/agents/triage-labels.md)
