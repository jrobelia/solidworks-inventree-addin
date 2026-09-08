# Fetch verifies a stamped InvenTree Part PK against the document (Link Mismatch)

A stamped InvenTree Part PK is the authoritative document link (ADR 0013): Fetch resolves
the part by PK and skips IPN resolution entirely. Issue #186 surfaced the stale-link risk —
a renumber or revision bump in InvenTree, or a repointed IPN on the document, leaves the PK
addressing a part that no longer matches what the document claims to be, and Apply/Push
then act on the wrong record.

We decided: **PK wins, but verify.** On the PK fetch path, the fetched part's IPN and
Revision are compared against the document's stamped values. A **Link Mismatch** — a value
stamped on both sides that disagrees, Revision compared with `RevisionComparer` —
interrupts the fetch with an OK/Cancel `MessageDialog` naming both sides. OK loads the
PK-addressed part; Cancel leaves the document LINKED with nothing loaded, and the prompt
text explains that clearing the PK Document Property re-enables IPN resolution. A field
blank on either side is "can't verify" and stays silent — a part without an IPN or revision
is not evidence of a stale link.

## Considered options

- **PK wins unconditionally** — the pre-existing behavior; rejected because it follows a
  stale link silently into Apply and Push on the wrong InvenTree record.
- **Status-bar warning** — rejected; ADR 0013 already established that identity ambiguity
  must not sit in a spot engineers don't look at.
- **Hard block on mismatch** — rejected; a renumber can be intentional, and the engineer is
  the only one who can judge whether the PK-addressed part is still the right one.
- **Auto-fallback to IPN resolution on Cancel** — rejected; when the document's IPN is the
  stale half of the pair, falling back loads a different wrong part and blurs Cancel.
- **In-prompt repair action** (update the document's stamps to match the part) — rejected;
  a fix-it button inside a warning encourages one-click pasting over the evidence that
  flagged the mismatch. Apply remains the deliberate path once the Task Pane is POPULATED.
- **Verify again at Push/Apply time** — rejected; once the engineer confirms the link, the
  session legitimately describes that part. Re-warning at write time would cry wolf.

## Consequences

- A stale PK costs one prompt instead of a silent wrong link.
- The check runs only on the PK fetch path; IPN resolution and the duplicate-IPN prompt are
  unchanged.
- **Link Mismatch** is a glossary term (CONTEXT.md).
