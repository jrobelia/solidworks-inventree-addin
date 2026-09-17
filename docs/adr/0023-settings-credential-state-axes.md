# Settings credential state model: two axes, persist-then-probe

## Context

ADR-0022 rebuilt the Settings credential area around a fixed workflow: a compact
status card claiming "configured", an edit form behind a mode switcher, a
revealable API key, and a test-before-save rule where Apply/Save could not run
until a probe passed. QA on PR #226 exposed the seams in that design: the card
conflated what was saved with whether it worked, "Remove API key" deleted the
whole config (URL, Property Mapping path, BOM keyword, key), the
test-before-save gate was invisible (Apply/Save greyed out for reasons the UI
never explained), and a reopened window kept claiming a stale "configured" state
that was never re-verified.

This ADR replaces that design with the model landed across #232, #233, and #234
(parent spec #231, driven by `prototype/settings-security-ui-prototype-1b.html`).

## Decision

- **Two independent state axes.** The card reports the *configuration axis*
  (nothing saved → server only → server + credential; what is persisted) crossed
  with the *connection axis* (untested → testing → connected → failed;
  session-only, never persisted). `ServerConnectionStatus.From(config,
  lastProbe, probeInFlight)` produces the title, dot, and the card's fixed
  three lines; a text title always names the state so colour never carries
  meaning alone.
- **Persist-then-probe replaces test-before-save.** `ApplyAsync` validates,
  resolves the credential, persists, then probes — a normal return means the
  settings were saved and the result carries the probe outcome. A failed probe
  never throws and never rolls back the save. This deliberately reverses
  ADR-0022's "test first, save only on success" consequence: the probe moves
  the connection axis; it is not a gatekeeper.
- **The saved API key is write-once.** It is never re-shown and has no Show/Hide
  toggle or reveal path; the dots placeholder is a display detail derived from
  the saved config, not an editable value. Pasting a new key replaces it.
- **Credential precedence: draft key > complete typed pair > saved key.** A
  non-blank typed key draft wins outright; otherwise a complete non-blank
  username/password pair resolves to a token (the saved key and its resolved
  token are replaced on persist); otherwise the saved key rides along so a
  URL-only edit still probes with it. Half-typed pairs never count.
- **Remove API key removes only the credential.** `RemoveApiKeyAsync` clears
  `ApiKey` and re-saves; the server URL, Property Mapping path, BOM keyword,
  and IPN flag survive. The card lands on Authentication required.
- **Probe on open.** When a saved URL and key exist, opening Settings fires a
  non-blocking `TestConnectionAsync` with the saved values: the card shows
  Testing connection…, then settles to Connected or Connection failed — a live
  result each open, never a stale claim. The service bounds every probe to ~4 s
  internally; the window owns the lifecycle token — cancelled on Close, on
  Apply start, and on Test connection — and a verdict arriving after
  cancellation is discarded untouched. Failure text distinguishes "server
  unreachable" from "server rejected the credential".
- **Status channels stay split.** The card reports persistent state; the Server
  Connection section's status bar reports what the last connection action did
  ("Saved — connection successful."). The open probe writes to the card only.
  *(Amended after #239: the action status bar and Test connection live at the
  bottom of the Server Connection section — matching ADR-0018's
  status-bar-next-to-its-action pattern — not in the window footer.)*

## Considered options

- **Keep test-before-save and explain the gate better** — rejected: a save that
  silently requires a live server destroys typed work when the network is down,
  and the gate is exactly what users could not see.
- **Window-side timeout via `Task.WhenAny` race or a window-owned CTS** —
  rejected: a race abandons rather than cancels (the ticket requires
  cancel-on-close) and a window-owned timeout would force the window to
  fabricate Unreachable results, duplicating classification policy the service
  already owns. The bound lives inside `TestConnectionAsync`; the token across
  the seam carries lifecycle only.
- **Persist the last probe outcome** — rejected: the connection axis is
  session-only by design; a saved "connected" claim is exactly the staleness
  this change removes.
- **Probe on open in the authentication-required state too** — rejected: the
  card already knows what is missing; there is no credential to test.

## Consequences

- `ISettingsApplyService.ApplyAsync` returns `ConnectionProbeResult`
  (status + user-facing message, never containing the key) instead of throwing
  on probe failure; `TestConnectionAsync` gains a `CancellationToken` and an
  internal ~4 s bound — caller cancellation propagates
  `OperationCanceledException`, a timeout returns Unreachable.
- `RemoveServerConfigAsync` is gone; `RemoveApiKeyAsync` is credential-only,
  and `IConfigProvider.DeleteServerConfig` is no longer used by Settings.
- `CredentialEditorState` shrinks to write-once key state; the reveal path and
  the `CredentialEntryMode` switcher are deleted.
- `SettingsWindow` holds a session `ConnectionProbeResult` plus an in-flight
  flag feeding every card render, and owns the open-probe lifecycle
  (`OpenProbeTask`, cancel-on-Closed, discard late results).
- Opening Settings with a saved key now costs one bounded network probe
  (~4 s worst case) but the window is fully interactive while it runs.
- ADR-0022's status card, mode switcher, key reveal, Remove-deletes-config, and
  test-before-save decisions are superseded; its DPAPI storage and
  no-key-in-UI-text decisions remain in effect.
