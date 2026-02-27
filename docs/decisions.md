# Decision Log

Append-only. One entry per non-obvious decision. Never delete old entries.

## 2026-02-26 -- DPAPI over AES key file for credential storage
Settings encrypted with Windows DPAPI (user scope). No key management needed --
Windows ties decryption to the logged-in user account. Trade-off: credentials
don't survive a Windows account migration, but that's rare and re-entering
a URL + API key is trivial.

## 2026-02-26 -- ConfigureAwait(false) + RunOnUiThread instead of Task.Run
SolidWorks runs on an STA thread. Task.Run forced continuations onto thread
pool threads, which deadlocked NUnit's STA runner and risked COM violations.
Direct await with ConfigureAwait(false) lets the HTTP call run on the thread
pool naturally, then RunOnUiThread marshals back via Invoke.

## 2026-02-26 -- Removed JsonFileConfigProvider entirely
The encrypted settings panel replaces the JSON file approach. Keeping both
alive would mean two code paths and user confusion. Clean cut.

## 2026-02-26 -- Installer versioning from git tags
Package.ps1 runs `git describe --tags --always` to name the zip and stamp
a version.txt inside it. No manual version bumping required.

## 2026-02-27 -- Test gap: spec items that touch the UI shell need explicit tests
During image push feature build, two spec items had no covering tests written
in Stage 5, so they survived to manual verify broken: (1) the "Include image"
checkbox in the Push Revision confirmation dialog, (2) `SwAddin` wiring the
real `SwViewportCaptureService` into the `TaskPaneControl` constructor.
Rule: any time the spec says "dialog shows X" or "SwAddin wires Y", write a
failing test for it in Stage 5 before writing any production code. If a
dialog-level behaviour is hard to unit-test, call it out explicitly in the
manual-verify checklist rather than leaving it implicit.

## 2026-02-27 -- PNG over JPEG for InvenTree part thumbnails
PNG chosen for clean edges when images are shrunk or converted to B&W for
label printing. 800x800 max PNG is ~50-150 KB -- negligible difference vs
JPEG at this size.

## 2026-02-27 -- Registration is one-time per DLL path
`RegAsm /codebase` writes the DLL path to the Windows registry once.
SolidWorks reads that path on every startup. Re-registration is only needed
if the DLL path changes (e.g. moving the project folder). Rebuilding in place
does not require re-registration.

## 2026-02-27 -- SaveBMP over screen capture for viewport images
Original approach used Win32 `EnumChildWindows` to find the SolidWorks viewport
window and `BitBlt` to screen-capture it. Fragile: heuristic child-window
matching, captured overlapping windows, wrong region if panels were docked
differently. Replaced with `IModelDoc2.SaveBMP(path, 0, 0)` -- official API
that renders the viewport to a temp BMP file at current dimensions. Clean,
reliable, no P/Invoke.

## 2026-02-27 -- White border on buttons instead of spacer panels
Attempted invisible spacer Panel between buttons for visual separation.
WinForms AutoSize layout collapsed the spacer repeatedly. Simpler fix:
1px white border + 2px vertical margin on every button via `MakeButton`.
Stands out against the blue background and requires no extra controls.
