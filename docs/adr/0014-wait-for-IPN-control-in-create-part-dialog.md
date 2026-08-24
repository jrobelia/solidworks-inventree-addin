# Move the wait-for-IPN control into the Create Part dialog

The Settings panel had a checkbox labelled "Server assigns part numbers automatically" that controlled whether the Create Part flow waited and polled for an auto-generated IPN. This label was misleading because the server can always assign an IPN; the control only decided whether the dialog blocked to wait for it.

Two options were considered:

1. **Relabel the checkbox in Settings** and add helper text. This would be a small change but keeps the decision far from the action the engineer is performing, and the existing label still requires a trip to Settings before creating a part.
2. **Move the checkbox into the Create Part dialog** next to the IPN field, default it to on, and remember the last state. This colocates the choice with the action and removes the misleading Settings option.

Decision: move the control into the Create Part dialog.

The new checkbox is labelled "Wait for server-assigned IPN before closing" and is disabled when the engineer types an IPN, because polling only matters when the IPN field is left blank for InvenTree auto-assignment. When the field is cleared, the checkbox re-enables with its remembered state. If InvenTree rejects a user-entered IPN, the dialog stays open with a clear inline error under the IPN field and allows the engineer to edit and retry.
