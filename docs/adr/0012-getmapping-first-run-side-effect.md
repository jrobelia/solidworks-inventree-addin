# GetMapping() writes defaults on first call

`PropertyMappingProvider.GetMapping()` writes default JSON to the local path on first call if the file does not exist. This means opening the Settings dialog (which calls `RefreshMappingStatus()` → `GetMapping()`) silently creates the defaults file on first launch — intentional first-run bootstrapping, not a bug.

Callers must be aware that `GetMapping()` is not read-only.
