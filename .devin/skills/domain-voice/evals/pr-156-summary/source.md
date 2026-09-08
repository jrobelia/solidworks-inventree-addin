# PR 156 source facts

- Title: "feat(mapping): complete property mapping health and Part Sync lockout (#140)"
- State: OPEN, target `milestone-3`, head branch `build/issue-146`.
- Closes #141, #148, #142, #143, #144, #145, #146, #160, #161, #162, #163, and is part of #140.
- It is the final PR for the #140 redesign of property mapping health, status, and Part Sync lockout.

Main changes:
- Removes the deprecated `GetMapping()` method and uses `GetMappingResult()` everywhere.
- `CreatePartViewModel` now calls `GetMappingResult()`, gates Create Part on `MappingHealth.CanUseForPartSync`, and handles invalid mapping without throwing.
- Task Pane and Settings now reflect mapping health and disable actions when mapping is Invalid, NeedsUpgrade, or NewerSchema.
- Property Mapping editor now uses a validated draft, supports BOM column aliases, and round-trips unknown keys.
- Create Part no longer does a client-side duplicate-IPN pre-check; it submits the request and surfaces server validation errors.
- IPN duplicate checking and JSON error parsing were moved out of `CreatePartViewModel` into a new validation-error service.
- `WaitForAutoPartNumber` was renamed to `WaitForServerAssignedIpn` with backward-compatible config migration.
- Copy-to-local was moved from the Property Mapping Editor to the Settings window.
- Build and test are green: 517 tests passed.
