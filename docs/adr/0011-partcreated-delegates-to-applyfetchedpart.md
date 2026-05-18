# PartCreated delegates to ApplyFetchedPart to avoid async race

After a successful part create, the Task Pane needs to enter POPULATED state. The first attempt called `FetchPartAsync()` from the PartCreated handler, causing a race: SolidWorks fires `ActiveDocChangeNotify` when the dialog closes, which called `LoadPartNumber()` → `ResetInvenTreeState()`, blowing away state before or after the async fetch completed.

Fix: `ApplyFetchedPart(part, thumbBytes?)` is the single authoritative place to enter POPULATED state. Both `FetchPartAsync` and the PartCreated handler call it. The handler sets state synchronously (part already in hand), so no async race is possible. `LoadPartNumber` guards against resetting if `_lastFetchedPart.Ipn` already matches the document IPN.
