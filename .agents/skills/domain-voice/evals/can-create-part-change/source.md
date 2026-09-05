# CanCreatePart change source facts

Latest commit on the PR branch:
- Message: "refactor(validation): accept ICreatePartValidationErrorService as a dependency"
- Body:
  - Rename `ICreatePartValidationService` and its adapters to `...ErrorService` to reflect that the seam only extracts server validation errors.
  - Remove inline creation of the service from `TaskPaneControl` and `TaskPaneViewModel`. `SwAddin` now creates and injects `InventreeClientCreatePartValidationErrorService`.
  - `TaskPaneViewModel.CanCreatePart` now requires a non-null validation service, so the UI disables Create Part when the dependency is missing instead of silently doing nothing.
  - Update all tests and call sites.

Code diff in `TaskPaneViewModel.cs`:
```csharp
private bool CanCreatePart() =>
    _client != null
    && _validationService != null
    && string.IsNullOrEmpty(_partNumber)
    && _isDocumentOpen
    && !_documentPkPresent
    ...
```
