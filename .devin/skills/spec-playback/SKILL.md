---
name: spec-playback
description: "Play back a spec or ticket set in plain end-user terms — what the engineer does, what they see, defaults, errors, edge cases, and the judgment calls made — and hold for their confirmation. Runs before a spec publishes, at the build-afk/build-hitl batch gate, and standalone via /spec-playback; use whenever a spec is being finalized or someone asks what the work will actually do."
triggers: ["user", "model"]
---

# Spec playback

A read-back: restate what the add-in will do in the engineer's terms and wait for them to confirm or correct. The spec template is written for the agents that consume it; the playback is written for the person who has to live with the result. A long user-story list does not read as an experience — a walkthrough does. Small mismatches survive every downstream stage that reads the spec faithfully; this is the last cheap place to catch them.

## When it runs

- **Spec publish** — end of `/to-spec`, before the spec hits the tracker. Required by `AGENTS.md` `## Spec discipline`. A full playback of the drafted spec.
- **Batch gate** — inside the `/build-afk` and `/build-hitl` batch confirmation: a per-ticket digest, one or two lines each on what the ticket makes the add-in do. A reminder, not a re-litigation — flag only where a ticket drifts from the spec or had to choose where the spec was silent. A correction here means amending the ticket or spec before dispatch, not just noting it.
- **Standalone** — `/spec-playback` on a spec, issue, or ticket set: an old spec about to be queued for build, or any "what will this actually do?" check.

## Write the playback

Read `CONTEXT.md` first (follow `CONTEXT-MAP.md` when present) and write in domain voice — `/domain-voice` carries the full check: preferred terms only, STE sentences, one idea each.

Group by the feature or workflow the engineer recognizes (Part Sync, BOM Compare, Create Part, Task Pane) — not by ticket or module. For each feature:

- **Trigger and result** — what the engineer does, what they see. "You click Create Part with the IPN field blank; the server assigns the IPN and the add-in stamps it on the document."
- **Defaults and empty states** — what happens when the engineer does nothing, leaves a field blank, or faces an empty list.
- **Errors and blocks** — unreachable server, Mapping Health not Healthy, a cancelled dialog: what the engineer sees, and what does not happen.

Then two flat sections:

- **Decisions we made** — every place the spec chose between defensible options. These are the mismatch hotspots; never bury one inside a feature bullet — list them so the engineer can veto them in one scan.
- **Out of scope** — the spec's exclusions in the same plain terms, confirmed rather than assumed.

Rules:

- Observable behavior only — no module names, code identifiers, or file paths. When a thing needs a name, use the UI label or the CONTEXT.md term.
- The source is the spec draft plus the conversation that produced it — re-narrate, do not reformat the user-story list.
- When the playback exposes a behavior the spec never settled, surface it as an open question in the playback rather than guessing — the answer goes back into the spec.
- Short enough to read once at speed. If it needs a second pass, cut.

## Confirm

End with the ask — "Confirm this matches what you want, or correct what doesn't" — and wait. Fold corrections and answers to open questions back into the spec draft; re-play only what changed.

**Done when:** the engineer has confirmed. At spec-publish time, publish the spec, then post the confirmed playback as a comment on the issue per `docs/agents/issue-tracker.md` — comments are part of the spec, so implementers and reviewers read the confirmed behavior as settled spec.
