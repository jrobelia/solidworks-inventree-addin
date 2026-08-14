# Agent Instructions — SolidWorks InvenTree Add-In

A C# WPF add-in for SolidWorks that bridges parts and assemblies to an InvenTree inventory server.

## Quick commands

- Test: `dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers` — primary verification loop. Builds the add-in into a throwaway `bin_unit_test` folder, so it can run while SolidWorks is open.
- Build: `dotnet build "SwInventreeAddin/SwInventreeAddin.csproj" --disable-build-servers` — produces the SolidWorks-facing `bin\Debug\net48` output. Run with SolidWorks closed if it needs to overwrite a locked `bin\Debug\net48\SwInventreeAddin.dll`.
- Full solution build: `dotnet build "Solidworks Inventree Add-In.sln" --disable-build-servers` — builds the solution and the test project; still valid but not the primary agent build command because it also writes the add-in to the `bin\Debug` path.
- Package manager: NuGet (restored automatically by `dotnet build`).

## Where to look next

- [Build, test, language, naming, and code-quality rules](docs/agents/coding-standards.md)
- [Scope: what the add-in does and out-of-bounds](docs/agents/scope.md)
- [User communication preferences](docs/agents/user-preferences.md)
- [Domain glossary and ADRs](docs/agents/domain.md) — see also [CONTEXT.md](CONTEXT.md)
- [Issue tracker conventions](docs/agents/issue-tracker.md) — see also [triage labels](docs/agents/triage-labels.md)
- [Code-review environment notes](docs/agents/code-review-known-issues.md)
