# Copilot Instructions — SolidWorks InvenTree Add-In

## What This Project Is

A C# WPF add-in for SolidWorks that bridges SolidWorks parts/assemblies with an InvenTree
inventory server. The engineer stays in SolidWorks; the add-in handles creating parts,
syncing properties, and comparing BOMs.

## Stack

- **Runtime**: .NET Framework 4.8 (`net48`) — not modern .NET
- **Language**: C# 8.0 (`LangVersion 8.0`) — nullable refs and switch expressions are available; C# 9+ features (`init`, `record`, top-level statements) are NOT
- **UI**: WPF (XAML + code-behind in `SwInventreeAddin/UI/`)
- **API client**: `SwInventreeAddin/Api/` — wraps InvenTree REST endpoints; use `System.Text.Json`, not Newtonsoft
- **Tests**: NUnit 3.14 in `SwInventreeAddin.Tests/` — use `Assert.That(x, Is.EqualTo(y))` constraint model
- **SolidWorks interop**: `EmbedInteropTypes=True`, `Private=False` — never copy SolidWorks DLLs to output; SolidWorks provides them at runtime

## Project-Specific Rules

- Property names that map between SolidWorks and InvenTree are user-configurable — never hardcode them.
- IPN generation is server-side — always re-fetch after creation, never assume the value.
- InvenTree-only BOM lines must never be modified or deleted by the add-in.

## What This Add-In Does NOT Do

Does not replace InvenTree's web UI for purchasing, build orders, supplier management,
or anything outside the SolidWorks design workflow.
