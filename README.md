# SolidWorks InvenTree Add-In

> **Use at your own risk.** This add-in is personal/work-adjacent software. It writes data to both SolidWorks documents and your InvenTree server. Test against a non-production InvenTree instance before using it on real data. No warranty is provided — see [LICENSE](LICENSE).

A SolidWorks task-pane add-in that bridges SolidWorks parts and assemblies with an [InvenTree](https://inventree.org) inventory server. Stay in SolidWorks — the add-in handles creating parts, syncing properties, and comparing BOMs.

## What It Does

**New parts** — Browse the InvenTree category tree, type a name, and the add-in creates the part server-side, waits for IPN generation, then writes the IPN and name back into SolidWorks custom properties.

**Existing parts** — Shows a live comparison of SolidWorks vs InvenTree data (name, description, notes, revision, image). Sync any field in either direction with one click.

**Push revision** — Sends the current SolidWorks revision to InvenTree, with an optional viewport screenshot attached.

**Assemblies** -- Reads the SolidWorks assembly BOM (immediate children, IPN + quantity), fetches the corresponding InvenTree BOM, and shows a side-by-side diff: added, updated, matched, and InvenTree-only lines. Select which lines to push. InvenTree-only lines are never touched. Duplicate IPNs are resolved by revision match.

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

**Property name mapping** — SolidWorks custom property names vary by company. Open Settings → Edit Mapping to configure which custom property names correspond to IPN, part name, description, notes, and revision, plus the BOM table keyword used to locate the assembly BOM. The mapping can be stored in two ways:

- **Local file** — saved in `%AppData%\SwInventreeAddin\` on the current machine. Good for personal use.
- **Shared file** — a path to a JSON file on a network share. All machines pointing at the same file stay in sync automatically. Set the shared path in Settings; the add-in falls back to a local copy if the share is unreachable.

**How parts are looked up** — IPN is the primary link between SolidWorks and InvenTree. On first fetch the add-in queries InvenTree by IPN and stores the InvenTree part PK (primary key) back into the SolidWorks document. Subsequent operations use the stored PK directly, which is faster and handles edge cases where an IPN search might return multiple candidates (resolved by revision match). If the PK property is missing or stale, the add-in falls back to an IPN search.

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
| `milestone-1` | M1 complete (part creation, property mapping, bidirectional sync). Merged into `main`. |
| `milestone-2` | M2 complete (assembly BOM sync). Merged into `main`. |

Never run a milestone branch in production.

## Versioning and Releases

Releases are tagged `vMAJOR.MINOR.0` and published as [GitHub Releases](../../releases). Each release corresponds to a milestone or a significant increment within one. The roadmap in [`docs/roadmap.md`](docs/roadmap.md) tracks what is planned and what shipped.

| Tag | Contents |
|---|---|
| `v1.0.0` | Initial working add-in: fetch, compare, apply, push revision. Encrypted settings panel. Installer and DevRegister scripts. |
| `v1.1.0` | Push viewport screenshot as part image |
| `v1.2.0` | WPF UI migration, sign-in with username/password, security hardening (HTTPS enforcement, TLS 1.2, header injection fix), installer improvements |
| `v1.3.0` | Thumbnail display, bidirectional Name/Notes push, revision match indicator, drawing block |
| `v2.0.0` | Assembly BOM sync: side-by-side diff, per-line push, InvenTree-only line protection, duplicate IPN resolution by revision, BOM status indicator in task pane |

Milestone 1 (part creation, property sync) and Milestone 2 (assembly BOM sync) are complete. See [`docs/roadmap.md`](docs/roadmap.md) for M3 plans.

## Scope Limits

This add-in does **not** replace the InvenTree web UI for purchasing, build orders, supplier management, or anything outside the SolidWorks design workflow.

## License

[MIT](LICENSE). Copyright (c) 2026 Jon Robelia.
