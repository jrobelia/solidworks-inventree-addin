# Decision Log

Append-only. One entry per non-obvious decision. Never delete old entries.

## 2026-02-26 — DPAPI over AES key file for credential storage
Settings encrypted with Windows DPAPI (user scope). No key management needed —
Windows ties decryption to the logged-in user account. Trade-off: credentials
don't survive a Windows account migration, but that's rare and re-entering
a URL + API key is trivial.

## 2026-02-26 — ConfigureAwait(false) + RunOnUiThread instead of Task.Run
SolidWorks runs on an STA thread. Task.Run forced continuations onto thread
pool threads, which deadlocked NUnit's STA runner and risked COM violations.
Direct await with ConfigureAwait(false) lets the HTTP call run on the thread
pool naturally, then RunOnUiThread marshals back via Invoke.

## 2026-02-26 — Removed JsonFileConfigProvider entirely
The encrypted settings panel replaces the JSON file approach. Keeping both
alive would mean two code paths and user confusion. Clean cut.

## 2026-02-26 — Installer versioning from git tags
Package.ps1 runs `git describe --tags --always` to name the zip and stamp
a version.txt inside it. No manual version bumping required.
