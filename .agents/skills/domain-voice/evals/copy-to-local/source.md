# Copy-to-local change source facts

Commit message: "fix: move Copy-to-local from editor to Settings window" (Closes #163)

Commit body:
- The Copy-to-local affordance was in the Property Mapping Editor, but the editor cannot be opened for a shared read-only mapping.
- The action belongs in Settings where the engineer selects Local vs. Shared.
- Removed `CanCopyToLocal`, `CopyToLocalInstruction`, and `CopyToLocal` from `MappingEditorViewModel`.
- Removed `CopyToLocalPanel`, `CopyToLocalButton`, and `CopyToLocalInstructionText` from `PropertyMappingEditorWindow`.
- Added a Copy to local button to the Settings window, visible when the current mapping is shared and not Invalid.
- Clicking it calls `IPropertyMappingProvider.CopyToLocal`, refreshes the mapping status, and shows an instruction to select Local and Apply.
- Updated ADR-0017 to record that the Copy-to-local action lives in Settings.

Context from the PR body:
- Copy-to-local moved from Property Mapping Editor to Settings window.
- Removes `CanCopyToLocal` / `CopyToLocalInstruction` from `MappingEditorViewModel`.
- Updates ADR-0017 and `CONTEXT.md`.
