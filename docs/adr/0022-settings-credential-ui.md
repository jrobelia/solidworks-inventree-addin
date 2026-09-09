# Settings window credential UI and disclosure

The Settings dialog stores the InvenTree API key as DPAPI-encrypted data, but it gives the engineer no indication that credentials are saved. The API key is shown in a plain `TextBox`, the username and password fields are always visible even though they are not persisted, and the only way to know the stored key works is to press Test Connection. We are redesigning the Settings window so it communicates the saved state clearly, masks the key, and tests before it saves.

## Decision

- Replace the always-visible credential fields with a compact status card: **"Server connection configured — API key saved"**.
- Reveal the connection form only when the engineer clicks **"Edit connection"**.
- Inside the edit form, use a mode switch with **"Sign in with InvenTree account"** (primary, top) and **"Paste API key"** (secondary, below). Only one form is visible at a time, in the same place.
- Mask the API key in a `PasswordBox` with a **Show** toggle and a **Remove API key** action using the trash-can icon.
- **Apply** and **Save** first test the connection, then save. **Save** closes the dialog only after the test passes.
- Cap the expanded form height with a `ScrollViewer` so the window does not overflow small monitors.

## Considered options

- **Keep the current layout and just add a status label** — rejected because it leaves the API key visible on screen and the username/password fields permanently shown.
- **Two separate expanders for sign-in and API key** — rejected because the second form ends up far from its button and the window height jumps twice.
- **Test only when the Test Connection button is pressed** — rejected because Save can persist a non-working key and the user only discovers the failure later.
- **Save without testing on Save** — rejected for the same reason; it is safer to fail before closing the dialog.

## Consequences

- The `ISettingsApplyService` interface changes: `ApplyAsync` accepts an `HttpClient` so it can test before saving, and a `RemoveServerConfigAsync` method is added for the Remove action.
- `IConfigProvider` gains a `DeleteServerConfig` method to remove the encrypted file.
- The compact default view reduces shoulder-surfing and visual noise, but it costs one extra click to edit credentials.
- Testing on Apply/Save adds a network round-trip, but it prevents saving an invalid key.
