# SolidWorks InvenTree Add-In

> **Use at your own risk.** This add-in is personal/work-adjacent software, not a commercial product. It writes data to both SolidWorks documents and your InvenTree server. Test against a non-production InvenTree instance before using it on real data. No warranty is provided — see [LICENSE](LICENSE).

A SolidWorks task-pane add-in that bridges SolidWorks parts and assemblies with an [InvenTree](https://inventree.org) inventory server. Stay in SolidWorks — the add-in handles creating parts, syncing properties, and comparing BOMs.

## What It Does

**New parts** — Browse the InvenTree category tree, type a name, and the add-in creates the part server-side, waits for IPN generation, then writes the IPN and name back into SolidWorks custom properties.

**Existing parts** — Shows a live comparison of SolidWorks vs InvenTree data (name, description, notes, revision, image). Sync any field in either direction with one click.

**Push revision** — Sends the current SolidWorks revision to InvenTree, with an optional viewport screenshot attached.

## Requirements

- SolidWorks 2022 or newer (2022–2026+ tested)
- Windows 10 or 11, 64-bit
- .NET Framework 4.8 (pre-installed on supported Windows versions)
- An InvenTree server with a valid API token

## Installation

1. Download the latest release zip.
2. Extract it anywhere (e.g. your Desktop).
3. Right-click **Install (Run as Administrator).bat** → Run as administrator.
4. Start SolidWorks. The InvenTree panel appears in the right-hand task pane.
5. Click the gear icon → enter your server URL and API key → Save.

To get an API key: InvenTree → click your username → **Account Settings** → **API Tokens**.

**Updating:** Download the new zip and run the installer again. Settings are preserved.

**Uninstalling:** Windows Settings → Apps → SwInventreeAddin → Uninstall, or run `Uninstall (Run as Administrator).bat` from `C:\Program Files\SwInventreeAddin\`.

## Configuration

Settings are stored per-user using Windows DPAPI encryption — no plain-text config files.

> **Note (v1.3.0 and earlier):** SolidWorks custom property names are hardcoded. Your documents must use these exact names for the add-in to read and write them correctly:
> - `PartNo` → IPN
> - `Description` → Part name
> - `Notes` → Notes
> - `Revision` → Revision
>
> Configurable property name mapping is coming in milestone 1.

**IPN is the link between SolidWorks and InvenTree.** The add-in reads `PartNo` from the open document and looks up the matching InvenTree part by IPN. All comparisons and syncs are keyed on IPN — there is no automatic matching by name or description alone.

## Building from Source

### Prerequisites

- Visual Studio 2022 with the **.NET desktop development** workload
- SolidWorks installed (the interop DLLs are resolved from the SolidWorks install directory at build time)

### Setup

Copy `Directory.Build.props.user.template` to `Directory.Build.props.user` and set your SolidWorks API path:

```xml
<SolidWorksApiRedist>C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist</SolidWorksApiRedist>
```

Then open `Solidworks Inventree Add-In.sln` in Visual Studio and build.

### Running Tests

```
dotnet test SwInventreeAddin.Tests
```

Tests use NUnit 3 and do not require SolidWorks to be installed.

### Registering the Add-In (Development)

After the first build, register the DLL with SolidWorks by running `DevRegister.ps1` as administrator (one-time per machine; only needed again if the DLL path changes).

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET Framework 4.8 |
| Language | C# 8.0 |
| UI | WPF (XAML) |
| SolidWorks API | COM interop, `EmbedInteropTypes=True` |
| HTTP client | `System.Net.Http` + `System.Text.Json` |
| Credential storage | Windows DPAPI (user scope) |
| Tests | NUnit 3 |

## Project Structure

```
SwInventreeAddin/
  AddIn/          COM entry point and SolidWorks lifecycle
  Config/         Settings storage and property mapping
  InvenTree/      HTTP client wrapping InvenTree REST API
  SolidWorks/     Document property and viewport capture services
  UI/             WPF task pane, dialogs, and view models
SwInventreeAddin.Tests/
  NUnit test suite (no SolidWorks dependency)
docs/
  architecture.md, decisions.md, roadmap.md
Installer/
  PowerShell packaging and install/uninstall scripts
```

## Branches

| Branch | Purpose |
|---|---|
| `main` | Latest stable release. Matches the most recent tag. |
| `milestone-1` | Current active development. Work in progress — may be incomplete or broken. |

Never run a milestone branch in production.

## Versioning and Releases

Releases are tagged `vMAJOR.MINOR.0` and published as [GitHub Releases](../../releases). Each release corresponds to a milestone or a significant increment within one. The roadmap in [`docs/roadmap.md`](docs/roadmap.md) tracks what is planned and what shipped.

| Tag | Contents |
|---|---|
| `v1.0.0` | Initial working add-in: fetch, compare, apply, push revision. Encrypted settings panel. Installer and DevRegister scripts. |
| `v1.1.0` | Push viewport screenshot as part image |
| `v1.2.0` | WPF UI migration, sign-in with username/password, security hardening (HTTPS enforcement, TLS 1.2, header injection fix), installer improvements |
| `v1.3.0` | Thumbnail display, bidirectional Name/Notes push, revision match indicator, drawing block |

Milestone 1 is currently in progress on the `milestone-1` branch. Already complete on that branch: configurable property mapping (no more hardcoded SolidWorks property names), description row, InvenTree PK storage, PK match indicator, and UI refinements (focus rings, icons, status indicators). Part creation from SolidWorks is still in progress.

## Scope Limits

This add-in does **not** replace the InvenTree web UI for purchasing, build orders, supplier management, or anything outside the SolidWorks design workflow.

## License

[MIT](LICENSE). Copyright (c) 2026 OpenRespirator.
