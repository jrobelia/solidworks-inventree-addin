# Domain voice examples

## Implementation to domain voice

**Before:**
"The `BomCompareViewModel` now exposes an `ICommand` through the `ICommandFactory` so the `PushAsync` method can be bound to the Task Pane button. We refactored the service registration in the DI container."

**After:**
"Clicking Push Selected to InvenTree in the Task Pane now runs the BOM Compare push directly. I removed the command factory, so the button calls the push itself."

## AI tell to human voice

**Before:**
"We are excited to announce that this pivotal enhancement underscores our commitment to delivering a seamless, vibrant user experience."

**After:**
"The add-in now fetches the InvenTree part without blocking the SolidWorks UI. That makes Part Sync feel a lot snappier."

## Hedge and vague to direct

**Before:**
"It could potentially be argued that the mapping file might need to be reviewed in the event that Mapping Health shows a warning."

**After:**
"Check the Property Mapping if Mapping Health warns you. A warning blocks Part Sync until you review and save the file."

## Generic code change to workflow language

**Before:**
"The `CreatePartViewModel` constructor now accepts an `ICreatePartValidationErrorService` parameter and assigns it to a private field. This was added to support validation during part creation."

**After:**
"Creating a part now validates against InvenTree before any data is sent. The Create Part workflow stores the validator so the Task Pane can flag errors before the part is created."

## Code identifier kept, effect explained

**Before:**
"`CanCreatePart` in `TaskPaneViewModel` was changed to check `_validationService != null`."

**After:**
"`CanCreatePart` now checks that the validation error service is present, so the Task Pane disables Create Part when that dependency is missing instead of silently doing nothing."

## Code identifier with broader design context

**Before:**
"The deprecated `GetMapping()` is gone and everything reads the mapping through `GetMappingResult()`. The duplicate-IPN check and InvenTree error parsing moved out of `CreatePartViewModel` into a dedicated validation-error component. 517 tests pass."

**After:**
"`GetMapping()` is gone in favor of `GetMappingResult()`, which returns the mapping *and* its `MappingHealth`. That lets every Part Sync write gate itself on health before it touches InvenTree, matching the pattern we use in BOM Compare and Create Part. The duplicate-IPN pre-check was also pulled out of `CreatePartViewModel` into a dedicated `ICreatePartValidationErrorService`, so the UI no longer second-guesses the server and the viewmodel only orchestrates the dialog."
