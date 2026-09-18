# Sidebar sync fix source facts

Commit: "fix: reload part number on panel sync" (Closes #171, part of milestone-3)

- The sidebar now re-loads the part number field from the server after every sync.
- Before, the custom property in the SW file kept its old part number after a sync that renamed it on the server, so the sidebar showed a stale part number.
- The fix reloads the field mapping file too, so renamed columns update.
- The panel's load spinner no longer sticks when the server is slow; it times out after 30 seconds and the sync button stays clickable.
- Upload of the part thumbnail still runs in the background and is unchanged.
- 512 tests pass.
