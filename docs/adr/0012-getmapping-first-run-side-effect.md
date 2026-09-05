# GetMappingResult() writes defaults on first call

`PropertyMappingProvider.GetMappingResult()` writes default JSON to the local path on first call if the file does not exist. This means opening the Settings dialog (which calls `RefreshMappingStatus()` → `GetMappingResult()`) silently creates the defaults file on first launch — intentional first-run bootstrapping, not a bug.

Callers must be aware that `GetMappingResult()` is not read-only.
